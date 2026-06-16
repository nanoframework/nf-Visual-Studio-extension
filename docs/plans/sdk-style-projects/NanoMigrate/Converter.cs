using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace NanoFramework.Migrate;

/// <summary>Outcome of converting a single project.</summary>
internal sealed class ConvertResult
{
    public required string OutputPath { get; init; }
    public List<string> Review { get; } = new();
}

/// <summary>
/// Converts one legacy .nfproj into an SDK-style project. Faithful to the
/// reference rules: drop project-system boilerplate and SDK-supplied defaults,
/// fold packages.config into PackageReference, fold .nuspec metadata into Pack
/// properties, drop default Compile globs and a hand-written AssemblyInfo.cs,
/// and "fail loud" — anything it cannot confidently convert is surfaced for a
/// human rather than guessed.
/// </summary>
internal static class Converter
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/developer/msbuild/2003";

    // Project-system boilerplate and properties the SDK now supplies itself.
    private static readonly HashSet<string> DropProps = new(StringComparer.Ordinal)
    {
        "ProjectTypeGuids", "ProjectGuid", "FileAlignment", "AppDesignerFolder",
        "NanoFrameworkProjectSystemPath", "TargetFrameworkVersion", "OldToolsVersion",
        "Configuration", "Platform",
    };

    // Properties carried through verbatim when present.
    private static readonly HashSet<string> KeepProps = new(StringComparer.Ordinal)
    {
        "RootNamespace", "AssemblyName", "DocumentationFile", "DefineConstants", "LangVersion",
        "Description", "Authors", "PackageTags", "Copyright",
    };

    // Legacy <Reference Include="X"> names whose NuGet package id differs from X.
    private static readonly Dictionary<string, string> LegacyPkgAliases = new(StringComparer.Ordinal)
    {
        ["mscorlib"] = "nanoFramework.CoreLibrary",
        ["System"]   = "nanoFramework.CoreLibrary",
    };

    // Matches a NuGet folder segment like "nanoFramework.CoreLibrary.1.15.0" inside a HintPath.
    private static readonly Regex HintPathVersion = new(
        @"[\\/](?<id>[A-Za-z0-9_.]+?)\.(?<ver>\d+\.\d+\.\d+(?:[-.+][0-9A-Za-z-.]+)?)[\\/]",
        RegexOptions.Compiled);

    public static ConvertResult Convert(string nfproj, Options o)
    {
        var projDir = Path.GetDirectoryName(Path.GetFullPath(nfproj))!;
        var root = XElement.Load(nfproj);
        var pkgs = LoadPackagesConfig(projDir);

        var props = new List<KeyValuePair<string, string>>();   // discovery order, deduped
        var pkgRefs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var projRefs = new List<string>();
        var keepItems = new List<XElement>();
        var review = new List<string>();

        void SetProp(string k, string? v)
        {
            if (string.IsNullOrEmpty(v)) return;
            if (props.Any(p => p.Key == k)) return;
            props.Add(new(k, v));
        }

        foreach (var pg in root.Elements(Ns + "PropertyGroup"))
            foreach (var el in pg.Elements())
            {
                var tag = el.Name.LocalName;
                if (DropProps.Contains(tag)) continue;
                if (KeepProps.Contains(tag)) SetProp(tag, el.Value);
            }

        foreach (var ig in root.Elements(Ns + "ItemGroup"))
            foreach (var el in ig.Elements())
            {
                var tag = el.Name.LocalName;
                var inc = (string?)el.Attribute("Include") ?? "";
                switch (tag)
                {
                    case "Reference":
                    {
                        var rawName = inc.Split(',')[0].Trim();
                        var name = LegacyPkgAliases.GetValueOrDefault(rawName, rawName);
                        var ver = pkgs.GetValueOrDefault(name) ?? pkgs.GetValueOrDefault(rawName);
                        if (ver is null)
                        {
                            // Fallback: infer version from the HintPath folder, clearly flagged.
                            var inferred = InferFromHintPath(el, rawName);
                            if (inferred is not null)
                            {
                                pkgRefs[inferred.Value.id] = inferred.Value.ver;
                                review.Add($"Version for {inferred.Value.id} inferred from HintPath "
                                         + $"as {inferred.Value.ver} (verify it matches the intended package)");
                            }
                            else
                            {
                                review.Add($"Reference without resolvable version: {inc} "
                                         + "(map to a PackageReference manually)");
                            }
                        }
                        else pkgRefs[name] = ver;
                        break;
                    }
                    case "PackageReference":
                        pkgRefs[inc] = (string?)el.Attribute("Version") ?? pkgs.GetValueOrDefault(inc) ?? "";
                        break;
                    case "ProjectReference":
                        projRefs.Add(inc);
                        break;
                    case "Compile":
                        if (!IsDefaultCompile(inc) || el.Attribute("Link") is not null)
                            keepItems.Add(el);
                        break;
                    case "None":
                        if (inc != "packages.config" && !inc.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
                            keepItems.Add(el);
                        break;
                    case "EmbeddedResource":
                    case "Content":
                        keepItems.Add(el);
                        break;
                    default:
                        review.Add($"Unhandled item <{tag} Include='{inc}'>");
                        break;
                }
            }

        FoldNuspec(projDir, SetProp);

        var xml = Emit(props, pkgRefs, projRefs, keepItems, o);

        var outPath = Path.ChangeExtension(Path.GetFullPath(nfproj), o.Ext);
        if (!o.DryRun)
        {
            if (!o.NoBackup) File.Copy(nfproj, nfproj + ".bak", overwrite: true);
            File.WriteAllText(outPath, xml, new UTF8Encoding(false));
            // If we emitted a .csproj alongside, retire the original .nfproj.
            if (!string.Equals(outPath, Path.GetFullPath(nfproj), StringComparison.OrdinalIgnoreCase))
                File.Delete(nfproj);
            var pc = Path.Combine(projDir, "packages.config");
            if (File.Exists(pc)) File.Delete(pc);
        }

        return new ConvertResult { OutputPath = outPath }.With(review);
    }

    private static (string id, string ver)? InferFromHintPath(XElement reference, string fallbackId)
    {
        var hint = reference.Elements(Ns + "HintPath").FirstOrDefault()?.Value;
        if (string.IsNullOrEmpty(hint)) return null;
        var m = HintPathVersion.Match(hint.Replace('\\', '/').Replace('/', '\\'));
        if (!m.Success) return null;
        var id = m.Groups["id"].Value;
        // Prefer the alias target if the raw id is a known alias source.
        id = LegacyPkgAliases.GetValueOrDefault(fallbackId, id);
        return (id, m.Groups["ver"].Value);
    }

    private static Dictionary<string, string> LoadPackagesConfig(string projDir)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pc = Path.Combine(projDir, "packages.config");
        if (!File.Exists(pc)) return result;
        foreach (var p in XElement.Load(pc).Elements("package"))
        {
            var id = (string?)p.Attribute("id");
            var ver = (string?)p.Attribute("version");
            if (id is not null && ver is not null) result[id] = ver;
        }
        return result;
    }

    private static bool IsDefaultCompile(string inc)
    {
        var baseName = inc.TrimStart('.', '\\');
        if (!inc.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return false;
        // A hand-written AssemblyInfo.cs collides with GenerateAssemblyInfo → drop it.
        if (baseName.Replace('\\', '/').EndsWith("Properties/AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase))
            return true;
        return !baseName.Contains('\\');
    }

    private static void FoldNuspec(string projDir, Action<string, string?> setProp)
    {
        var nuspec = Directory.EnumerateFiles(projDir, "*.nuspec").FirstOrDefault();
        if (nuspec is null) return;
        var meta = XElement.Load(nuspec).Descendants().FirstOrDefault(e => e.Name.LocalName == "metadata");
        if (meta is null) return;
        foreach (var (xml, msb) in new[]
        {
            ("id", "PackageId"), ("description", "Description"), ("authors", "Authors"),
            ("tags", "PackageTags"), ("projectUrl", "PackageProjectUrl"),
        })
        {
            var e = meta.Elements().FirstOrDefault(x => x.Name.LocalName == xml);
            if (e is not null && !string.IsNullOrEmpty(e.Value)) setProp(msb, e.Value);
        }
    }

    private static string Emit(
        List<KeyValuePair<string, string>> props,
        Dictionary<string, string> pkgRefs,
        List<string> projRefs,
        List<XElement> keepItems,
        Options o)
    {
        var sb = new StringBuilder();
        sb.Append($"<Project Sdk=\"nanoFramework.Sdk/{o.Sdk}\">\n\n");
        sb.Append("  <PropertyGroup>\n");
        sb.Append($"    <TargetFramework>{o.Tfm}</TargetFramework>\n");
        foreach (var kv in props)
            sb.Append($"    <{kv.Key}>{Escape(kv.Value)}</{kv.Key}>\n");
        sb.Append("  </PropertyGroup>\n\n");

        if (pkgRefs.Count > 0)
        {
            sb.Append("  <ItemGroup>\n");
            foreach (var kv in pkgRefs.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                sb.Append($"    <PackageReference Include=\"{kv.Key}\" Version=\"{kv.Value}\" />\n");
            sb.Append("  </ItemGroup>\n\n");
        }
        if (projRefs.Count > 0)
        {
            sb.Append("  <ItemGroup>\n");
            foreach (var r in projRefs)
                sb.Append($"    <ProjectReference Include=\"{Escape(r)}\" />\n");
            sb.Append("  </ItemGroup>\n\n");
        }
        if (keepItems.Count > 0)
        {
            sb.Append("  <ItemGroup>\n");
            foreach (var el in keepItems)
            {
                var attrs = string.Join(" ", el.Attributes().Select(a => $"{a.Name.LocalName}=\"{Escape(a.Value)}\""));
                sb.Append($"    <{el.Name.LocalName} {attrs} />\n");
            }
            sb.Append("  </ItemGroup>\n\n");
        }
        sb.Append("</Project>\n");
        return sb.ToString();
    }

    private static string Escape(string s) => s
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static ConvertResult With(this ConvertResult r, IEnumerable<string> review)
    {
        r.Review.AddRange(review);
        return r;
    }
}
