// nano-migrate — convert legacy nanoFramework .nfproj projects to the SDK-style
// MSBuild project system, one project at a time or across an entire cloned fleet.
//
// SCOPE: project-system migration ONLY. This tool does NOT touch OTA, modular
// firmware packaging, runtimes/{rid}/native layouts, or ABI manifests. It moves
// a repo from the legacy flavored .nfproj format onto an SDK-style project that
// composes over the nanoFramework SDK, folds packages.config into PackageReference,
// and folds .nuspec metadata into MSBuild Pack properties. Nothing more.
//
// BCL-only: no external NuGet dependencies, so it builds and runs fully offline
// once the .NET SDK is present.

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace NanoFramework.Migrate;

internal static class Program
{
    private const string MsbuildNs = "http://schemas.microsoft.com/developer/msbuild/2003";

    private static int Main(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        var opts = Options.Parse(args.Skip(1));
        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "migrate" => CmdMigrate(opts),
                "clone"   => CmdClone(opts),
                "fleet"   => CmdFleet(opts),
                _         => Fail($"unknown command '{args[0]}'"),
            };
        }
        catch (UserError ue)
        {
            return Fail(ue.Message);
        }
    }

    // ───────────────────────────── migrate ─────────────────────────────

    private static int CmdMigrate(Options o)
    {
        var path = o.Positional ?? throw new UserError("migrate needs a path to a .nfproj or a directory");
        var targets = ResolveProjects(path);
        if (targets.Count == 0) throw new UserError($"no .nfproj found under '{path}'");

        var allReview = new List<string>();
        foreach (var nf in targets)
        {
            var result = Converter.Convert(nf, o);
            Console.WriteLine(o.DryRun
                ? $"would convert {nf} -> {result.OutputPath}"
                : $"converted {nf} -> {result.OutputPath}");
            foreach (var r in result.Review) allReview.Add($"  [{Path.GetFileName(nf)}] {r}");
        }

        if (allReview.Count > 0)
        {
            Console.WriteLine("\nMANUAL REVIEW NEEDED:");
            Console.WriteLine(string.Join("\n", allReview));
            return 2;
        }
        return 0;
    }

    private static List<string> ResolveProjects(string path)
    {
        if (File.Exists(path) && path.EndsWith(".nfproj", StringComparison.OrdinalIgnoreCase))
            return new List<string> { Path.GetFullPath(path) };
        if (Directory.Exists(path))
            return Directory.EnumerateFiles(path, "*.nfproj", SearchOption.AllDirectories)
                            .Select(Path.GetFullPath).OrderBy(p => p).ToList();
        return new List<string>();
    }

    // ───────────────────────────── clone ─────────────────────────────

    private static int CmdClone(Options o)
    {
        var outDir = o.Positional ?? "./nano-repos";
        Directory.CreateDirectory(outDir);

        Console.WriteLine($"enumerating {o.Org} repositories matching '{o.Filter}*'...");
        var repos = GitHub.ListOrgRepos(o.Org, o.Token, o.IncludeArchived)
                          .Where(r => r.Name.StartsWith(o.Filter, StringComparison.OrdinalIgnoreCase))
                          .OrderBy(r => r.Name).ToList();

        if (repos.Count == 0) throw new UserError(
            $"no repos matched '{o.Filter}*' in org '{o.Org}'. " +
            "Check the org name and filter, or pass --token to lift the API rate limit.");

        Console.WriteLine($"found {repos.Count} repositories. cloning into {outDir} ...");
        int ok = 0, skipped = 0, failed = 0;
        foreach (var r in repos)
        {
            var dest = Path.Combine(outDir, r.Name);
            if (Directory.Exists(dest)) { Console.WriteLine($"  skip   {r.Name} (already present)"); skipped++; continue; }
            var (code, _, err) = Run("git", $"clone --depth 1 {r.CloneUrl} \"{dest}\"", outDir);
            if (code == 0) { Console.WriteLine($"  cloned {r.Name}"); ok++; }
            else { Console.WriteLine($"  FAIL   {r.Name}: {err.Trim().Split('\n').LastOrDefault()}"); failed++; }
        }
        Console.WriteLine($"\ndone. cloned {ok}, skipped {skipped}, failed {failed}.");
        return failed > 0 ? 2 : 0;
    }

    // ───────────────────────────── fleet ─────────────────────────────

    private static int CmdFleet(Options o)
    {
        var reposDir = o.Positional ?? throw new UserError("fleet needs a path to a directory of cloned repos");
        if (!Directory.Exists(reposDir)) throw new UserError($"directory not found: {reposDir}");
        if (o.Commit && o.Branch is null) throw new UserError("--commit requires --branch");
        // nanoFramework workflow: branch names must not start with "develop" (they
        // collide with upstream develop-* branches).
        if (o.Branch is not null && o.Branch.StartsWith("develop", StringComparison.OrdinalIgnoreCase))
            throw new UserError("branch name must not start with 'develop' (nanoFramework workflow); "
                              + "use something like 'sdk-migration' or 'issue-123'");
        // In a git repo the commit history already preserves the pre-migration file,
        // so a .bak alongside it is just noise in the diff. Skip backups when committing.
        if (o.Commit) o.NoBackup = true;

        var repoDirs = Directory.EnumerateDirectories(reposDir)
                                .Where(d => Directory.EnumerateFiles(d, "*.nfproj", SearchOption.AllDirectories).Any())
                                .OrderBy(d => d).ToList();
        if (repoDirs.Count == 0) throw new UserError($"no repos containing .nfproj found under '{reposDir}'");

        var report = new List<RepoReport>();
        foreach (var repo in repoDirs)
        {
            var rr = new RepoReport { Name = Path.GetFileName(repo) };
            try
            {
                if (o.Branch is not null && !o.DryRun)
                {
                    var (code, _, err) = Run("git", $"checkout -B {o.Branch}", repo);
                    if (code != 0) { rr.Error = $"git checkout failed: {err.Trim()}"; report.Add(rr); continue; }
                }

                foreach (var nf in Directory.EnumerateFiles(repo, "*.nfproj", SearchOption.AllDirectories).OrderBy(p => p))
                {
                    rr.Projects++;
                    var result = Converter.Convert(nf, o);
                    var rel = Path.GetRelativePath(repo, nf);
                    foreach (var item in result.Review) rr.Review.Add($"{rel}: {item}");
                }

                if (o.Commit && !o.DryRun)
                {
                    Run("git", "add -A", repo);
                    var msgFile = WriteCommitMessage(repo, o);
                    var signOff = o.SignOff ? "-s " : "";
                    var (code, _, err) = Run("git", $"commit {signOff}-F \"{msgFile}\"", repo);
                    File.Delete(msgFile);
                    rr.Committed = code == 0;
                    if (code != 0 && !err.Contains("nothing to commit"))
                    {
                        rr.Error = err.Contains("Please tell me who you are") || err.Contains("user.name")
                            ? "git commit failed: set git user.name/user.email (real name) so the "
                              + "Signed-off-by line is valid, or pass --no-sign-off"
                            : $"git commit: {err.Trim()}";
                    }
                }
            }
            catch (Exception ex)
            {
                rr.Error = ex.Message;
            }
            report.Add(rr);
            var status = rr.Error is not null ? "ERROR" : rr.Review.Count > 0 ? "review" : "ok";
            Console.WriteLine($"  [{status,6}] {rr.Name}  ({rr.Projects} project(s), {rr.Review.Count} review item(s))");
        }

        WriteReport(report, o, reposDir);
        var errored = report.Count(r => r.Error is not null);
        Console.WriteLine($"\n{report.Count} repos processed, {errored} with errors. report: {o.Report}");
        return errored > 0 ? 2 : 0;
    }

    private static void WriteReport(List<RepoReport> report, Options o, string reposDir)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# nanoFramework SDK-style migration — fleet report\n");
        sb.AppendLine($"- Source: `{Path.GetFullPath(reposDir)}`");
        sb.AppendLine($"- Mode: {(o.DryRun ? "dry-run (no files written)" : "applied")}"
                    + (o.Branch is not null ? $", branch `{o.Branch}`" : "")
                    + (o.Commit ? ", committed" : ""));
        sb.AppendLine($"- SDK `{o.Sdk}`, TFM `{o.Tfm}`, output extension `{o.Ext}`\n");

        int total = report.Count, clean = report.Count(r => r.Error is null && r.Review.Count == 0);
        int needsReview = report.Count(r => r.Error is null && r.Review.Count > 0);
        int errors = report.Count(r => r.Error is not null);
        sb.AppendLine("## Summary\n");
        sb.AppendLine($"| Repos | Clean | Needs review | Errored |");
        sb.AppendLine($"|------:|------:|-------------:|--------:|");
        sb.AppendLine($"| {total} | {clean} | {needsReview} | {errors} |\n");

        if (errors > 0)
        {
            sb.AppendLine("## Errored repos\n");
            foreach (var r in report.Where(r => r.Error is not null))
                sb.AppendLine($"- **{r.Name}** — {r.Error}");
            sb.AppendLine();
        }

        if (needsReview > 0)
        {
            sb.AppendLine("## Repos needing manual review\n");
            sb.AppendLine("These migrated, but the tool could not confidently resolve everything. "
                        + "Each line is something a human should confirm before merging.\n");
            foreach (var r in report.Where(r => r.Error is null && r.Review.Count > 0))
            {
                sb.AppendLine($"### {r.Name}\n");
                foreach (var item in r.Review) sb.AppendLine($"- {item}");
                sb.AppendLine();
            }
        }

        if (clean > 0)
        {
            sb.AppendLine("## Clean migrations\n");
            sb.AppendLine("Converted with no items flagged for review:\n");
            foreach (var r in report.Where(r => r.Error is null && r.Review.Count == 0))
                sb.AppendLine($"- {r.Name} ({r.Projects} project(s))"
                            + (r.Committed ? " — committed" : ""));
            sb.AppendLine();
        }

        File.WriteAllText(o.Report, sb.ToString());
    }

    // ───────────────────────────── helpers ─────────────────────────────

    // Builds a commit message that follows the nanoFramework guidance: a short
    // summary (<= 50 chars), a blank line, a body wrapped at 72 columns, and an
    // optional "Fix #<issue>" trailer. Returns the path to a temp message file.
    private static string WriteCommitMessage(string repo, Options o)
    {
        var summary = o.CommitMessage ?? "Migrate project system to SDK-style";
        if (summary.Length > 50) summary = summary[..50].TrimEnd();

        var body = Wrap(
            "Convert the legacy .nfproj project system to an SDK-style MSBuild project: "
          + "drop project-system boilerplate, fold packages.config into PackageReference, "
          + "and fold .nuspec metadata into MSBuild Pack properties. "
          + "No functional code changes.", 72);

        var sb = new StringBuilder();
        sb.Append(summary).Append("\n\n").Append(body).Append('\n');
        if (o.Issue is not null) sb.Append("\nFix #").Append(o.Issue).Append('\n');

        var path = Path.GetTempFileName();
        File.WriteAllText(path, sb.ToString());
        return path;
    }

    private static string Wrap(string text, int width)
    {
        var sb = new StringBuilder();
        int lineLen = 0;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (lineLen > 0 && lineLen + 1 + word.Length > width) { sb.Append('\n'); lineLen = 0; }
            else if (lineLen > 0) { sb.Append(' '); lineLen++; }
            sb.Append(word); lineLen += word.Length;
        }
        return sb.ToString();
    }

    internal static (int code, string stdout, string stderr) Run(string file, string args, string cwd)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi)!;
        var so = p.StandardOutput.ReadToEnd();
        var se = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, so, se);
    }

    private static bool IsHelp(string a) => a is "-h" or "--help" or "help";
    private static int Fail(string msg) { Console.Error.WriteLine($"error: {msg}"); return 1; }

    private static void PrintUsage() => Console.WriteLine("""
        nano-migrate — migrate nanoFramework projects to the SDK-style project system

        USAGE
          nano-migrate migrate <path>      Convert a .nfproj, or every .nfproj under a directory.
          nano-migrate clone   <out-dir>   Clone all matching repos from a GitHub org.
          nano-migrate fleet   <repos-dir> Migrate every .nfproj across cloned repos; write a report.

        COMMON OPTIONS
          --sdk <version>     nanoFramework SDK version to reference   (default 2.0.0)
          --tfm <moniker>     Target framework moniker                 (default netnano1.0)
          --ext <ext>         Output extension: .nfproj or .csproj     (default .nfproj, in place)
          --no-backup         Don't write a .nfproj.bak (implied by fleet --commit).
          --dry-run           Analyse and report only; write nothing.

        clone OPTIONS
          --org <name>        GitHub org                               (default nanoframework)
          --filter <prefix>   Repo name prefix to match                (default lib-)
          --token <pat>       GitHub token (or env GITHUB_TOKEN) to raise the API rate limit.
          --include-archived  Include archived repositories (skipped by default).

        fleet OPTIONS
          --report <path>     Markdown report path             (default migration-report.md)
          --branch <name>     Create/reset this git branch in each repo (must not start with 'develop').
          --commit            Commit the changes (requires --branch). Uses a contribution-compliant
                              message and signs off (Signed-off-by) by default.
          --message <msg>     Commit summary line (kept <= 50 chars).
          --issue <n>         Reference an issue: adds a "Fix #<n>" trailer to the commit.
          --no-sign-off       Don't add a Signed-off-by line.

        SCOPE
          Project-system migration only. Does NOT produce OTA artifacts, modular
          firmware packaging, runtimes/{rid}/native layouts, or ABI manifests.

        EXAMPLES
          nano-migrate migrate ./lib-CoreLibrary
          nano-migrate migrate ./MyDevice/MyDevice.nfproj --ext .csproj
          nano-migrate clone ./nano-repos --token $GITHUB_TOKEN
          nano-migrate fleet ./nano-repos --branch sdk-migration --commit --dry-run
        """);
}
