# 07 — Library Repository Migration (~100+ repos)

The migration path for the `lib-*` fleet, an automated converter, what breaks, and native co-location.

---

## 7.1 The fleet and what's uniform about it

There are ~100+ `lib-*` repos, plus aggregate repos like `nanoFramework.IoT.Device`. They are highly uniform — the same `.nfproj` skeleton, `packages.config`, `version.json` (Nerdbank.GitVersioning), `.nuspec`, Azure DevOps pipeline. **Uniformity is the asset:** a converter that handles the common shape correctly migrates the long tail mechanically, leaving a small set of special cases (corlib, native-bearing libraries) for manual attention.

## 7.2 What the converter does

For each `.nfproj` it produces an SDK-style project by:

1. Replacing the header/imports/GUIDs with `<Project Sdk="nanoFramework.Sdk/<v>">`.
2. Mapping `TargetFrameworkVersion v1.0` → `TargetFramework netnano1.0`.
3. Dropping explicit `<Compile Include>` (default globs cover them) — but **preserving non-default** includes/excludes/links.
4. Converting `packages.config` + `<Reference HintPath>` → `<PackageReference>` (version from `packages.config`).
5. Folding the `.nuspec` into MSBuild `Pack*` properties (`PackageId`, `Description`, `Authors`, `PackageTags`, icon, license, `RepositoryUrl`), keeping `version.json` for NBGV.
6. Removing `ProjectTypeGuids`, `ProjectGuid`, `FileAlignment`, `AppDesignerFolder`, `NanoFrameworkProjectSystemPath`, and the four `NFProjectSystem.*` imports.
7. Leaving a `.bak` and emitting a diff report.

## 7.3 The converter (reference implementation)

`nano-migrate.py` — deliberately conservative: it refuses to silently drop anything it doesn't recognize, flagging it for human review instead.

```python
#!/usr/bin/env python3
"""nano-migrate: convert a legacy .nfproj to an SDK-style nanoFramework project."""
import sys, re, json, shutil, xml.etree.ElementTree as ET
from pathlib import Path

NS = "http://schemas.microsoft.com/developer/msbuild/2003"
ET.register_namespace("", NS)
Q = lambda t: f"{{{NS}}}{t}"

DROP_PROPS = {
    "ProjectTypeGuids","ProjectGuid","FileAlignment","AppDesignerFolder",
    "NanoFrameworkProjectSystemPath","TargetFrameworkVersion","OldToolsVersion",
    "Configuration","Platform",  # SDK supplies defaults
}
KEEP_PROPS = {  # carried through verbatim if present
    "RootNamespace","AssemblyName","DocumentationFile","DefineConstants","LangVersion",
}

def load_packages_config(proj_dir):
    pc = proj_dir / "packages.config"
    refs = {}
    if pc.exists():
        for p in ET.parse(pc).getroot().findall("package"):
            refs[p.get("id")] = p.get("version")
    return refs

# legacy <Reference Include="X"> names whose NuGet package id differs from X
LEGACY_PKG_ALIASES = {
    "mscorlib": "nanoFramework.CoreLibrary",
    "System": "nanoFramework.CoreLibrary",
}

def is_default_compile(inc):  # default glob already covers plain **/*.cs
    base = inc.lstrip(".\\")
    if not inc.endswith(".cs"):
        return False
    # a hand-written AssemblyInfo.cs collides with GenerateAssemblyInfo → must be dropped
    if base.replace("\\", "/").endswith("Properties/AssemblyInfo.cs"):
        return True  # treat as default → dropped (SDK regenerates it)
    return "\\" not in base

def convert(nfproj: Path, sdk_version: str, tfm: str, out_ext: str):
    proj_dir = nfproj.parent
    tree = ET.parse(nfproj); root = tree.getroot()
    pkgs = load_packages_config(proj_dir)

    props, pkg_refs, proj_refs, keep_items, review = {}, {}, [], [], []

    for pg in root.findall(Q("PropertyGroup")):
        for el in list(pg):
            tag = el.tag.replace(f"{{{NS}}}","")
            if tag in DROP_PROPS: continue
            if tag in KEEP_PROPS or tag in ("Description","Authors","PackageTags","Copyright"):
                props[tag] = el.text

    for ig in root.findall(Q("ItemGroup")):
        for el in list(ig):
            tag = el.tag.replace(f"{{{NS}}}","")
            inc = el.get("Include","")
            if tag == "Reference":
                name = inc.split(",")[0]
                name = LEGACY_PKG_ALIASES.get(name, name)
                ver = pkgs.get(name) or pkgs.get(inc.split(",")[0])
                if ver: pkg_refs[name] = ver
                else: review.append(f"Reference without resolvable version: {inc} "
                                    f"(map to a PackageReference manually)")
            elif tag == "PackageReference":
                pkg_refs[inc] = el.get("Version") or pkgs.get(inc,"")
            elif tag == "ProjectReference":
                proj_refs.append(inc)
            elif tag == "Compile":
                if not is_default_compile(inc) or el.get("Link"):
                    keep_items.append(("Compile", el))  # non-default → preserve
            elif tag == "None":
                if inc not in ("packages.config",) and not inc.endswith(".nuspec"):
                    keep_items.append(("None", el))
            elif tag in ("EmbeddedResource","Content"):
                keep_items.append((tag, el))
            else:
                review.append(f"Unhandled item <{tag} Include='{inc}'>")

    # fold nuspec
    nuspec = next(proj_dir.glob("*.nuspec"), None)
    if nuspec:
        meta = ET.parse(nuspec).getroot().find(".//{*}metadata")
        if meta is not None:
            for k_xml, k_msb in (("id","PackageId"),("description","Description"),
                                 ("authors","Authors"),("tags","PackageTags"),
                                 ("projectUrl","PackageProjectUrl")):
                e = meta.find(f"{{*}}{k_xml}")
                if e is not None and e.text: props.setdefault(k_msb, e.text)

    # emit
    lines = [f'<Project Sdk="nanoFramework.Sdk/{sdk_version}">','','  <PropertyGroup>',
             f'    <TargetFramework>{tfm}</TargetFramework>']
    for k,v in props.items():
        if v: lines.append(f"    <{k}>{v}</{k}>")
    lines += ['  </PropertyGroup>','']
    if pkg_refs:
        lines.append('  <ItemGroup>')
        for n,v in sorted(pkg_refs.items()):
            lines.append(f'    <PackageReference Include="{n}" Version="{v}" />')
        lines += ['  </ItemGroup>','']
    if proj_refs:
        lines.append('  <ItemGroup>')
        for r in proj_refs: lines.append(f'    <ProjectReference Include="{r}" />')
        lines += ['  </ItemGroup>','']
    if keep_items:
        lines.append('  <ItemGroup>')
        for tag, el in keep_items:
            attrs = " ".join(f'{k}="{v}"' for k,v in el.attrib.items())
            lines.append(f'    <{tag} {attrs} />')
        lines += ['  </ItemGroup>','']
    lines.append('</Project>')

    out = nfproj.with_suffix(out_ext)
    shutil.copy2(nfproj, nfproj.with_suffix(nfproj.suffix + ".bak"))
    out.write_text("\n".join(lines), encoding="utf-8")
    (proj_dir / "packages.config").unlink(missing_ok=True)
    return out, review

if __name__ == "__main__":
    import argparse
    ap = argparse.ArgumentParser()
    ap.add_argument("path"); ap.add_argument("--sdk", default="2.0.0")
    ap.add_argument("--tfm", default="netnano1.0")
    ap.add_argument("--ext", default=".nfproj", choices=[".nfproj",".csproj"])
    a = ap.parse_args()
    targets = [Path(a.path)] if a.path.endswith(".nfproj") else list(Path(a.path).rglob("*.nfproj"))
    total_review = []
    for nf in targets:
        out, review = convert(nf, a.sdk, a.tfm, a.ext)
        print(f"converted {nf} -> {out}")
        for r in review: total_review.append(f"  [{nf.name}] {r}")
    if total_review:
        print("\nMANUAL REVIEW NEEDED:"); print("\n".join(total_review))
        sys.exit(2)
```

Design choices that matter:
- **Fails loud, not silent.** Anything unrecognized goes to a review list and a non-zero exit, so a CI gate can require a human before merging the conversion of an unusual repo.
- **`--ext .nfproj`** by default (keeps VS recognition during the window, doc 06 §6.3); switch to `--csproj` once VS SDK support lands.
- Leaves `version.json` (NBGV) untouched — it works with SDK projects.

## 7.4 Migration order — leaf-first

A dependency must be available as a republished `netnano1.0` package before its dependents restore cleanly against it. So migrate **bottom-up**:

```
1. mscorlib / CoreLibrary  (the root; special SDK variant, manual)   ──┐
2. System.* base libraries (Math, Collections, Text, Runtime, ...)    │ leaf
3. Device libraries (Gpio, Spi, I2c, Pwm, Adc)                        │  ↓
4. Protocol/stack libraries (Net, Http, Mqtt, Json)                   │
5. IoT.Device bindings (~the long tail; converter-automated)        ──┘ consumer
```

Each tier publishes its republished `netnano1.0` packages before the tier above it migrates. The TFM does not change, so there is no framework-version split to manage on the feed — only the usual package version bumps.

## 7.5 What breaks (and the fix)

| Breaks | Why | Fix |
|--------|-----|-----|
| `packages.config` tooling | Removed in favor of PackageReference | Converter deletes it; `dotnet restore` replaces it |
| `.nuspec`-based `nuget pack` in CI | Pack moves to SDK | Replace `nuget pack X.nuspec` with `dotnet pack` (doc 08) |
| Azure DevOps `MSBuild@1` + nuget restore steps | SDK self-resolves | Replace with `DotNetCoreCLI@2` `build`/`pack` |
| `-nr=False` test prebuild hack | x64 MDP task + nodeReuse | SDK fixes node-reuse handling (doc 04 §4.1); hack removed |
| HintPath-pinned mscorlib references | No `packages` folder | PackageReference to `nanoFramework.CoreLibrary` |
| Hand-listed `Compile` items | Default globs | Converter drops defaults, keeps non-defaults |

## 7.6 Repo-level CI conversion (sketch)

A second script rewrites the common Azure Pipelines template:

```yaml
# before:  nuget restore + MSBuild@1 + nuget pack
# after:
- task: UseDotNet@2
  inputs: { packageType: sdk, version: 8.x }
- script: dotnet build -c Release
- script: dotnet pack -c Release --no-build
- task: NuGetCommand@2
  inputs: { command: push, ... }
```

Because the pipeline templates are nearly identical across `lib-*`, this is a find/replace on a shared template repo plus per-repo submodule bump — not 100 hand-edits.

## 7.7 Opening the PRs (per repo)

Every auto-created PR **must** be rendered from the org pull-request template — see
[PR-INSTRUCTIONS.md](PR-INSTRUCTIONS.md) for the verbatim template, the slot-filling
contract, and the `gh pr create` recipe. Do not invent a bespoke PR body for the fleet.

Per-repo summary (full rules in PR-INSTRUCTIONS.md §"Fleet-upgrader usage"):

- **Title**: uniform, issue-free — `Migrate to SDK-style project system`.
- **Body**: the org template, *Types of changes* = **Config and build** (+ **Dependencies**
  if references changed); *Motivation* = `Resolves nanoFramework/Home#NNNN`.
- **Base**: the migration integration branch (`move-to-sdk`) if present, else `develop`.
  Never `main`.
- **Draft** until CI is green, then `gh pr ready`.
- **Order**: leaf-first (§7.4) — don't open a dependent repo's PR before its dependencies
  merge/publish.
- **Idempotent**: skip if an open PR already exists for the branch; respect rate limits.

The reference renderer lives next to the converter in [NanoMigrate/](NanoMigrate/):
`(repo, homeIssue, types[]) → filled org template → gh pr create --draft`.
