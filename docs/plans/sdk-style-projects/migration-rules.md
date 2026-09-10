# Migration rules reference

The precise rules the converter (`tools/migrate`) applies, the reasoning
behind each, and the edge cases you may meet. Read this when a `MANUAL REVIEW
NEEDED` item appears, when output looks surprising, or when changing the rules.

## Contents

- [Property handling](#property-handling)
- [Reference and package handling](#reference-and-package-handling)
- [Compile items and AssemblyInfo](#compile-items-and-assemblyinfo)
- [Other item types](#other-item-types)
- [.nuspec folding](#nuspec-folding)
- [File operations and safety](#file-operations-and-safety)
- [Fail-loud philosophy](#fail-loud-philosophy)
- [Dependency ordering for the fleet](#dependency-ordering-for-the-fleet)
- [Things intentionally out of scope](#things-intentionally-out-of-scope)

## Property handling

The legacy project carries properties that the SDK now supplies, plus boilerplate
the flavored project system needed. These are **dropped**:

`ProjectTypeGuids`, `ProjectGuid`, `FileAlignment`, `AppDesignerFolder`,
`NanoFrameworkProjectSystemPath`, `TargetFrameworkVersion`, `OldToolsVersion`,
`Configuration`, `Platform`.

`TargetFrameworkVersion` (`v1.0`) is replaced by a single `<TargetFramework>`
(default `netnano1.0`, override with `--tfm`). `Configuration`/`Platform` defaults
come from the SDK. The `NFProjectSystem` import disappears entirely — the
`Sdk="nanoFramework.Sdk/<version>"` attribute replaces it.

These are **carried through verbatim** when present:

`RootNamespace`, `AssemblyName`, `DocumentationFile`, `DefineConstants`,
`LangVersion`, plus packaging metadata `Description`, `Authors`, `PackageTags`,
`Copyright`.

Any other property is **not** copied. If a repo relies on an unusual property,
add it back by hand after reviewing whether the SDK already provides it. (This is
a conservative default: copying unknown properties blindly risks dragging legacy
assumptions into the SDK build.)

## Reference and package handling

The legacy system used `<Reference>` items resolved through `packages.config`.
The SDK system uses `<PackageReference>`. Conversion:

1. For each `<Reference Include="X">`, strip any assembly-name suffix after a
   comma, giving the bare name `X`.
2. Apply legacy aliases: `mscorlib` and `System` both map to the package
   `nanoFramework.CoreLibrary`, because the legacy reference name does not match
   the NuGet package id.
3. Resolve the version from `packages.config` (by package id, then by the raw
   reference name).
4. If that fails, **fall back** to inferring the version from the `HintPath`
   folder segment (e.g. `packages\nanoFramework.CoreLibrary.1.15.0\lib\...` →
   `1.15.0`). This inference is always flagged for review so a human confirms it.
5. If neither works, emit a review line and leave the reference for manual mapping.

`<PackageReference>` items already present are carried through (version taken from
the item, else from `packages.config`). `PackageReference`s are emitted sorted by
package id for stable diffs.

## Compile items and AssemblyInfo

SDK-style projects glob `**/*.cs` by default, so explicit `<Compile>` items for
files the glob already covers are redundant and are **dropped**. The rule:

- A `Compile Include="Foo.cs"` (top-level, no folder separator) → dropped.
- A `Compile Include="Internal\Helper.cs"` (in a subfolder) → **kept**, because
  legacy projects sometimes list files that the default glob would still pick up
  but whose presence is load-bearing in unusual layouts; preserving them is the
  safe choice.
- A `Compile` with a `Link` attribute → kept (it is a linked file, not a default).
- `Properties\AssemblyInfo.cs` → **dropped even though it is hand-written**,
  because the SDK's `GenerateAssemblyInfo` produces assembly attributes and a
  checked-in `AssemblyInfo.cs` causes duplicate-attribute build errors. If the
  repo needs custom assembly attributes, set them as MSBuild properties
  (`<Product>`, `<Company>`, etc.) or disable `GenerateAssemblyInfo` deliberately.

## Other item types

- `None Include="packages.config"` and `None Include="*.nuspec"` → dropped (both
  are obsolete after migration).
- Other `None`, plus `EmbeddedResource` and `Content` → kept verbatim (attributes
  preserved, emitted self-closing).
- `ProjectReference` → kept verbatim.
- Any other item element → emitted as a review line ("Unhandled item…") so a
  human decides. The tool does not silently drop or transform unknown items.

## .nuspec folding

If a `.nuspec` sits next to the project, its `<metadata>` is mapped into MSBuild
Pack properties so `dotnet pack` reproduces the package:

| .nuspec        | MSBuild property      |
|----------------|-----------------------|
| `id`           | `PackageId`           |
| `description`  | `Description`         |
| `authors`      | `Authors`             |
| `tags`         | `PackageTags`         |
| `projectUrl`   | `PackageProjectUrl`   |

Existing project properties win (the fold uses set-if-absent), so a value already
on the project is not overwritten. `version` is intentionally **not** folded:
versioning is usually handled by Nerdbank.GitVersioning or a CI property, and
hard-coding it from an old `.nuspec` would regress that. Confirm the repo's
versioning strategy carries over.

## File operations and safety

- By default a `<file>.nfproj.bak` is written next to each converted project.
  `--no-backup` skips it; `fleet --commit` skips it automatically (git history is
  the backup).
- With the default `--ext .nfproj`, the project is rewritten in place. With
  `--ext .csproj`, a new `.csproj` is written and the original `.nfproj` is
  removed.
- `packages.config` is deleted after its contents are folded into
  `PackageReference`s.
- `--dry-run` performs the full analysis and prints the review list but writes
  nothing and touches no git state — safe to run anywhere, any number of times.

## Fail-loud philosophy

The tool never guesses when it is unsure. Every uncertain case becomes a review
line rather than a silent transformation. This matters at fleet scale: a silent
wrong guess multiplied across 100+ repos is far more expensive than a review line
a human clears in seconds. When triaging a fleet report, treat the review list as
the real work and the clean set as already done.

## Dependency ordering for the fleet

Migrate leaf-first so a dependency is already available as an SDK-style package
when its dependents build:

1. `nanoFramework.CoreLibrary` (the root; may need a special SDK variant).
2. Base/runtime libraries (`nanoFramework.Runtime.*`, `System.*` bindings).
3. Device and protocol libraries that depend on the base.
4. Higher-level IoT bindings and aggregates last.

Within a single dry-run the order does not matter (nothing is published), but it
does matter for the publish/PR sequence: do not merge a dependent before the
dependency it relies on has shipped an SDK-style package.

## Things intentionally out of scope

The tool stops at the project system. It does not, and should not be extended to:

- generate OTA update artifacts or manifests,
- produce `runtimes/{rid}/native/` layouts or relocatable native modules,
- emit ABI/module compatibility manifests,
- build firmware or invoke the metadata processor.

Those belong to separate, later phases. Keeping this tool narrow is what makes it
safe to run unattended across the whole fleet.
