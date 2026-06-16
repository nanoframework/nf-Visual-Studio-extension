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
