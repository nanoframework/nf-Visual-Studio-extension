# 08 — NuGet Pipeline

How `dotnet pack` produces a managed nanoFramework package, and how packaging
metadata moves off the hand-written `.nuspec`.

**Scope note.** This covers **managed** packages only. NuGet packages here ship
managed assemblies (`.pe` + reference `.dll` + `.pdbx` + `.xml`) under
`lib/<tfm>/`. Shipping native binaries inside packages
(`runtimes/{rid}/native/`, pre-linked modules), module/ABI manifests, CoreRuntime
firmware packages, and toolchain packs are **out of scope** — they belong to a
separate, later effort and are deliberately not produced by this pipeline.

---

## 8.1 Goal layout

A managed library package:

```
nanoFramework.Iot.Device.Gpio.1.2.3.nupkg
└─ lib/netnano1.0/
    ├─ Iot.Device.Gpio.pe        ← the on-device managed assembly
    ├─ Iot.Device.Gpio.pdbx      ← debug DB
    ├─ Iot.Device.Gpio.dll       ← reference assembly (compile-against)
    └─ Iot.Device.Gpio.xml       ← API docs
```

That's the whole package. No `runtimes/`, no manifests, no native artifacts.

### Target the recognized framework folder

The package must place its assets under the framework's `lib/<tfm>/` folder so
NuGet resolves them natively for `netnano1.0`, removing the `AssetTargetFallback`
to `net` that consumers hit today (see doc 02 §2.2 and
[#1635](https://github.com/orgs/nanoframework/discussions/1635)). `$(NanoPackTfmFolder)`
is the folder NuGet derives from the framework moniker.

## 8.2 Overriding the SDK `Pack` target

`Microsoft.NET.Sdk`'s pack maps build output into `lib/<tfm>/` using the `.dll`.
We override what goes into `lib/` so the **`.pe`** is the primary on-device
assembly, alongside the reference `.dll`, the `.pdbx`, and docs:

```xml
<!-- nanoFramework.Pack.targets -->
<Target Name="NanoPackBuildOutputs" BeforeTargets="_GetPackageFiles">
  <ItemGroup>
    <_NanoLib Include="$(NanoPeOutputPath)" />
    <_NanoLib Include="$(NanoPdbxOutputPath)" />
    <_NanoLib Include="@(IntermediateRefAssembly)" />        <!-- reference .dll -->
    <_NanoLib Include="$(DocumentationFile)" Condition="'$(DocumentationFile)'!=''" />
    <None Include="@(_NanoLib)" Pack="true"
          PackagePath="lib/$(NanoPackTfmFolder)/%(filename)%(extension)" />
  </ItemGroup>
</Target>
```

## 8.3 Packaging metadata (off the `.nuspec`)

Legacy packages ship a hand-written `.nuspec` and run `nuget pack`. SDK-style
projects carry the same metadata as MSBuild properties so `dotnet pack` produces
the package, and the `.nuspec` is deleted. The migration tool folds the common
fields automatically:

| `.nuspec` | MSBuild property |
|-----------|------------------|
| `id` | `PackageId` |
| `description` | `Description` |
| `authors` | `Authors` |
| `tags` | `PackageTags` |
| `projectUrl` | `PackageProjectUrl` |

Versioning is intentionally left alone: most repos use Nerdbank.GitVersioning
(`version.json`) or a CI property, and that carries over unchanged. Don't fold a
hard-coded `<version>` from the old `.nuspec`.

## 8.4 `dotnet pack` invariants

- **Restore → build → pack → push** stays the CI shape; the difference is
  `dotnet pack` instead of `nuget pack X.nuspec`, and `PackageReference`/`dotnet
  restore` instead of `packages.config`/`nuget restore`.
- `--no-build` is safe for these managed packages (single build output, no
  per-RID inner builds).
- Symbol packages (`.snupkg`) can carry the `.pdbx` mapping for the debugger;
  whether to ship them is a per-feed toggle (`IncludeSymbols`).
- Deterministic packaging: the package contents derive from build output, so
  repeated builds of the same source produce stable packages.

## 8.5 What this pipeline does **not** do

For clarity, and to keep the boundary firm:

- It does not produce `runtimes/{rid}/native/` content or pre-linked modules.
- It does not generate `module_manifest.json` or `abi_compatibility.json`.
- It does not build or repackage CoreRuntime firmware (`nanoFramework.CoreRuntime.*`).
- It does not produce toolchain packs (`nanoFramework.Toolchain.*`).

Those are part of the separate native/modular-firmware effort and must not be
introduced into the managed pack path.
