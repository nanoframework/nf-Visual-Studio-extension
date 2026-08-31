# 03 — Project File Migration

Concrete before/after project files, the minimal cases, multi-targeting integration, and the backward-compatibility story.

---

## 3.1 Before: a legacy `.nfproj` (abridged)

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props"
          Condition="Exists('...Microsoft.Common.props')" />
  <PropertyGroup Label="Globals">
    <NanoFrameworkProjectSystemPath>$(MSBuildExtensionsPath)\nanoFramework\v1.0\</NanoFrameworkProjectSystemPath>
  </PropertyGroup>
  <Import Project="$(NanoFrameworkProjectSystemPath)NFProjectSystem.Default.props"
          Condition="Exists('$(NanoFrameworkProjectSystemPath)NFProjectSystem.Default.props')" />
  <PropertyGroup>
    <Configuration Condition=" '$(Configuration)' == '' ">Debug</Configuration>
    <Platform Condition=" '$(Platform)' == '' ">AnyCPU</Platform>
    <ProjectTypeGuids>{11A8DD76-328B-46DF-9F39-F559912D0360};{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}</ProjectTypeGuids>
    <ProjectGuid>{02118A19-3E52-45FE-A827-50814366F917}</ProjectGuid>
    <OutputType>Library</OutputType>
    <RootNamespace>Iot.Device.Ad5328</RootNamespace>
    <AssemblyName>Iot.Device.Ad5328</AssemblyName>
    <TargetFrameworkVersion>v1.0</TargetFrameworkVersion>
    <FileAlignment>512</FileAlignment>
  </PropertyGroup>
  <Import Project="$(NanoFrameworkProjectSystemPath)NFProjectSystem.props"
          Condition="Exists('$(NanoFrameworkProjectSystemPath)NFProjectSystem.props')" />
  <ItemGroup>
    <Compile Include="Ad5328.cs" />
    <Compile Include="Properties\AssemblyInfo.cs" />
  </ItemGroup>
  <ItemGroup>
    <Reference Include="mscorlib"><HintPath>packages\...\mscorlib.dll</HintPath></Reference>
    <Reference Include="nanoFramework.System.Device.Spi"><HintPath>packages\...\...dll</HintPath></Reference>
  </ItemGroup>
  <ItemGroup>
    <None Include="packages.config" />
    <None Include="Ad5328.nuspec" />
  </ItemGroup>
  <Import Project="$(NanoFrameworkProjectSystemPath)NFProjectSystem.CSharp.targets"
          Condition="Exists('$(NanoFrameworkProjectSystemPath)NFProjectSystem.CSharp.targets')" />
  <!-- + NFProjectSystem.MDP.targets, post-build nuget pack, etc. -->
</Project>
```

Pain points it embodies: explicit `Compile` per file, `packages.config` + `HintPath` references, GUIDs, the `NanoFrameworkProjectSystemPath` dance, separate `.nuspec`.

## 3.2 After: minimal managed app

```xml
<Project Sdk="nanoFramework.Sdk/2.0.0">
  <PropertyGroup>
    <TargetFramework>netnano1.0</TargetFramework>
    <OutputType>Exe</OutputType>   <!-- app entry point; libraries omit this (Library is default) -->
  </PropertyGroup>
</Project>
```

That's the whole file. Defaults supplied by the SDK: `AnyCPU`, implicit `Compile` glob of `**/*.cs`, `GenerateAssemblyInfo` (no hand-written `AssemblyInfo.cs`), PE emission on. References become `PackageReference` (PackageReference restores, no `packages.config`/`HintPath`).

## 3.3 After: minimal managed library with package dependency

```xml
<Project Sdk="nanoFramework.Sdk/2.0.0">
  <PropertyGroup>
    <TargetFramework>netnano1.0</TargetFramework>
    <RootNamespace>Iot.Device.Ad5328</RootNamespace>
    <AssemblyName>Iot.Device.Ad5328</AssemblyName>
    <!-- packing metadata inline; no separate .nuspec -->
    <PackageId>nanoFramework.Iot.Device.Ad5328</PackageId>
    <Description>AD5328 DAC binding for .NET nanoFramework.</Description>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="nanoFramework.System.Device.Spi" Version="1.5.0" />
  </ItemGroup>
</Project>
```

## 3.4 Multi-targeting

TFM multi-targeting is rarely needed (the framework version is usually singular),
but the SDK supports `<TargetFrameworks>` orthogonally if a library ever needs to
target more than one nanoFramework version. There is **no RID axis** in scope:
native modules and per-RID artifacts are out of scope (separate effort), so a
managed project builds a single, target-agnostic PE.

## 3.5 Backward compatibility during migration

Three mechanisms let old and new coexist while ~100+ repos migrate (doc 07):

### (a) `.nfproj` keeps working unchanged
Phase 1 ships the SDK *alongside* the legacy project system. Existing `.nfproj` files build via the old `NFProjectSystem.*` imports exactly as before. Nothing forces migration.

### (b) An SDK-style `.nfproj` (transitional extension)
Visual Studio keys the project *type* off the file extension + flavor GUID. To keep VS opening migrated projects as nanoFramework projects during the transition, a migrated file can retain the `.nfproj` extension but adopt SDK form:

```xml
<Project Sdk="nanoFramework.Sdk/2.0.0">
  <PropertyGroup><TargetFramework>netnano1.0</TargetFramework></PropertyGroup>
</Project>
```

This requires the VS extension to register the SDK-style project system for `.nfproj` (doc 06 §6.3). Once VS support lands, the extension can be dropped from `.csproj` (the long-term shape) since the SDK fully describes the build.

### (c) Cross-references both directions
- A migrated SDK project can `ProjectReference` a legacy `.nfproj` (MSBuild resolves both; the legacy one still emits a `.pe`).
- A legacy `.nfproj` can `Reference`/`PackageReference` a package produced by a migrated project (the package contents are PE-compatible at the same TFM).
The TFM is unchanged (`netnano1.0`), so there's no framework-version break. The
migration order still flows **leaf-first** (corlib/core libraries first), because
a dependency must be available as a republished `netnano1.0` package before its
dependents restore cleanly against it — see doc 07.

## 3.6 Property reference (project-author surface)

| Property | Default | Meaning |
|----------|---------|---------|
| `TargetFramework` | — (required) | `netnano1.0`. |
| `OutputType` | `Library` | `Exe` for app entry assemblies. |
| `NanoEmitPe` | `true` | Emit `.pe`. (`false` only for ref-only/analyzer projects.) |
| `NanoDeployTarget` | — | Default device serial port for `Deploy` (doc 05). |
| `NanoValidateChecksum` | `false` | Opt in to the build-time ABI gate (doc 04 §4.5). |

All `Nano*` properties are safe to omit; the SDK supplies defaults so the minimal
file (§3.2) stays minimal.
