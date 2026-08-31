# 02 — SDK Design: `nanoFramework.Sdk`, the TFM, and the Target Graph

This is the central design document. It defines the MSBuild SDK, the target
framework moniker, the SDK↔.NET-SDK relationship, and the managed build target
graph.

**Scope note.** This document covers the *managed* project system only. Native
module compilation, relocatable native linking, shipping native binaries inside
NuGet packages, and OTA are **out of scope** — they belong to a separate, later
effort and are not part of this SDK.

**Status note.** The VS-debugger concern that was thought to gate this is **resolved** —
the POC proved F5 + source breakpoints on an SDK-style project on real hardware (doc 09
§9.5; discussion [#1635](https://github.com/orgs/nanoframework/discussions/1635)). And the
MSBuild-SDK destination described here now exists as an official repo,
[`nanoframework/nanoFramework.Sdk`](https://github.com/nanoframework/nanoFramework.Sdk)
(WIP on `move-to-sdk`), which packages the build pipeline as a NuGet SDK. The build/pack/
test design below is what that SDK implements; VS debugging on SDK-style projects is the
POC's contribution (capability injection + the debugging fixes).

---

## 2.1 What an MSBuild SDK is (and why it's the right shape)

An MSBuild "project SDK" is a NuGet package containing two entry points:

```
nanoFramework.Sdk/<version>/
  Sdk/
    Sdk.props      ← implicitly imported at the TOP of the project
    Sdk.targets    ← implicitly imported at the BOTTOM of the project
```

`<Project Sdk="nanoFramework.Sdk/1.0.0">` is sugar for:

```xml
<Project>
  <Import Project="Sdk.props" Sdk="nanoFramework.Sdk" Version="1.0.0" />
  ...user content...
  <Import Project="Sdk.targets" Sdk="nanoFramework.Sdk" Version="1.0.0" />
</Project>
```

This is *exactly* the Layer-B structure from doc 01 (props at top, targets at
bottom) — but formalized, versioned, and NuGet-resolvable instead of dropped into
`$(MSBuildExtensionsPath)` by a VS extension. That is the whole point: **the build
no longer depends on an installed VS/VSCode extension.** `dotnet build` works on a
clean machine with only the .NET SDK + a NuGet restore.

> The SDK *package* version (`1.0.0` above) is independent of the target
> framework moniker (`netnano1.0`, §2.2). The package version tracks the SDK's own
> releases; the TFM tracks the framework the code targets.

### Resolution mechanics

An SDK reference resolves via, in priority order:
1. Inline version (`Sdk="name/1.0.0"`).
2. `global.json` `msbuild-sdks` entry (lets a whole repo pin one version):
   ```json
   { "msbuild-sdks": { "nanoFramework.Sdk": "1.0.0" } }
   ```
   then `<Project Sdk="nanoFramework.Sdk">`.
3. The NuGet-based SDK resolver (downloads the package on first restore).

**Decision:** ship `nanoFramework.Sdk` as a NuGet-resolved SDK, recommend
`global.json` pinning for repos with many projects (all the `lib-*` repos), and
support inline versions for samples/one-offs.

## 2.2 The TFM: `netnano1.0`

`netnano1.0` **is already a recognized TFM** in the .NET SDK and NuGet client
(see the [Microsoft TFM table](https://learn.microsoft.com/en-us/dotnet/standard/frameworks#supported-target-frameworks),
where ".NET nanoFramework → `netnano1.0`" is listed). So the project author can
write:

```xml
<TargetFramework>netnano1.0</TargetFramework>
```

and NuGet/the SDK understand the moniker for compatibility and asset selection.

### The real gap: packages aren't published against `netnano1.0`

The token is recognized, but the nanoFramework **NuGet packages aren't published
targeting it** — today consumers have to `AssetTargetFallback` to `net` to
restore (as seen in
[#1635](https://github.com/orgs/nanoframework/discussions/1635)), and projects
still use `packages.config`. Closing this is unblocked, mechanical work:

1. Republish class-library packages with assemblies under the framework's
   `lib/<tfm>/` folder so restore resolves them natively for `netnano1.0`,
   removing the `net` fallback.
2. Move consumers from `packages.config` to `PackageReference` (the migration
   tool does this).

The SDK sets the canonical moniker properties so restore and compat checks line
up with the recognized framework:

```xml
<!-- Sdk.props : align the moniker properties with the recognized framework -->
<PropertyGroup Condition="'$(TargetFramework)' == 'netnano1.0'">
  <TargetFrameworkIdentifier>.NETnanoFramework</TargetFrameworkIdentifier>
  <TargetFrameworkVersion>v1.0</TargetFrameworkVersion>
  <TargetFrameworkMoniker>.NETnanoFramework,Version=v1.0</TargetFrameworkMoniker>
  <!-- nanoFramework supplies its own mscorlib subset; don't pull .NET ref packs -->
  <DisableImplicitFrameworkReferences>true</DisableImplicitFrameworkReferences>
  <NoStdLib>true</NoStdLib>
</PropertyGroup>
```

## 2.3 Relationship to the .NET SDK: **workload, not fork**

Three options were considered:

| Option | Verdict |
|--------|---------|
| **A. Fully custom SDK** (no `Microsoft.NET.Sdk` underneath) | Rejected. Loses Roslyn integration, restore, `dotnet` CLI, glob defaults, `dotnet pack` plumbing — re-implementing all of it is the current pain, not a fix. |
| **B. Thin SDK that imports `Microsoft.NET.Sdk`** | Viable and the **starting point**. `nanoFramework.Sdk` composes over the .NET SDK, reusing Roslyn/restore/pack and overriding the post-compile stage. |
| **C. .NET SDK *workload*** (`dotnet workload install nanoframework`) | The **target state**. A workload cleanly delivers the SDK pack, the framework reference, and `dotnet new` templates — installable/updatable via `dotnet workload`. |

**Decision:** implement as **B first, evolve to C.** `nanoFramework.Sdk` always
imports `Microsoft.NET.Sdk` and overrides; the workload (C) is the *distribution*
mechanism layered on once B is stable. This matches how MAUI/wasm/android ship.

### What composing over `Microsoft.NET.Sdk` looks like

```xml
<!-- nanoFramework.Sdk/Sdk/Sdk.props -->
<Project>
  <!-- inherit Roslyn, restore, default globs, pack plumbing -->
  <Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk" />

  <PropertyGroup>
    <!-- nanoFramework defaults -->
    <OutputType>Library</OutputType>
    <Platform Condition="'$(Platform)'==''">AnyCPU</Platform>
    <LangVersion Condition="'$(LangVersion)'==''">latest</LangVersion>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <GenerateAssemblyInfo>true</GenerateAssemblyInfo>
    <!-- the device assembly is the .pe; the .dll is a reference intermediate -->
    <NanoEmitPe>true</NanoEmitPe>
  </PropertyGroup>

  <Import Project="nanoFramework.Tfm.props" />        <!-- §2.2 -->
</Project>
```

```xml
<!-- nanoFramework.Sdk/Sdk/Sdk.targets -->
<Project>
  <!-- our PE stage must sit AFTER Roslyn CoreCompile -->
  <Import Project="nanoFramework.Mdp.targets" />     <!-- doc 04 -->
  <Import Project="nanoFramework.Deploy.targets" />  <!-- doc 05 -->
  <Import Project="nanoFramework.Pack.targets" />    <!-- doc 08 -->

  <!-- inherit the .NET SDK bottom import LAST so its CoreCompile etc. exist -->
  <Import Project="Sdk.targets" Sdk="Microsoft.NET.Sdk" />
</Project>
```

> Ordering subtlety: importing `Microsoft.NET.Sdk`'s `Sdk.targets` *last* means
> our targets are *defined* but ordered relative to the SDK's via
> `BeforeTargets`/`AfterTargets`/`DependsOnTargets` rather than file position. We
> never rely on import order for execution order — only for *definition*
> availability. See doc 04 §4.4.

## 2.4 One pipeline: managed PE

A nanoFramework project in this scope is a managed app or managed library:
C# → IL → **PE**. There is no native build step. Projects whose managed code
declares native interop (`[Native]`/`extern` methods) still go through the
metadata processor's stub-generation and checksum steps (doc 04), because those
are part of producing a correct managed PE — but the SDK does **not** compile,
link, or package native binaries. That is a separate effort.

## 2.5 The end-to-end target graph (managed)

```
Build
 ├─ ResolveReferences        (Microsoft.NET.Sdk)
 ├─ CoreCompile  (Roslyn csc) → obj/<Asm>.dll  (IL)
 ├─ NanoGenerateStubs        [BeforeTargets=NanoEmitPe; only if native interop]  (doc 04 §4.5)
 ├─ NanoEmitPe   (MDP task)  → bin/<Asm>.pe, .pdbx, checksum  (doc 04 §4.4)
 │     └─ (AfterTargets=CoreCompile)
 └─ NanoValidateChecksum     → optional ABI gate vs. target firmware  (doc 04 §4.6)
 NanoPack (on dotnet pack)   → .nupkg with lib/<tfm>/  (PE + pdbx + dll + xml)  (doc 08)
 Deploy  [explicit target]   → nanoff orchestration  (doc 05)
```

## 2.6 Open questions to resolve in Phase 0

1. **Exact target names/ordering inside `NFProjectSystem.MDP.targets`** — must be
   read from the repo to preserve checksum/stub semantics bit-for-bit. The graph
   above is the intended shape; the real `GenerateBinaryOutputTask` inputs/outputs
   map onto `NanoEmitPe`.
2. **Whether mscorlib/CLR-core managed assemblies need a bespoke SDK**
   (`nanoFramework.Sdk.Corlib`) — they bootstrap the framework and can't
   `PackageReference` themselves. Likely a thin SDK variant with `NanoIsCorlib=true`.
3. **The `NFProjectSystem.CSharp.targets` double-import** when composed in
   SDK-style/imported contexts (it re-imports `Microsoft.CSharp.CurrentVersion.targets`,
   which the SDK already imports — see
   [#1635](https://github.com/orgs/nanoframework/discussions/1635),
   [#1067](https://github.com/nanoframework/Home/issues/1067)). The SDK must own
   the import chain so this collision can't happen — **done in the POC** (`Sdk.props`/
   `Sdk.targets` compose over `Microsoft.NET.Sdk` and own the import order; the legacy
   `NFProjectSystem.*` targets are not imported).
4. **The VS debugger dependency** (doc 09) — was the gate for SDK-style as a
   *supported* format. **RESOLVED by the POC ✅:** with the SDK injecting the
   `NanoCSharpProject` capability, the existing AD7 engine deploys + debugs (F5 +
   breakpoints) an SDK-style project on real hardware, unchanged — no VS / VS-SDK
   evolution needed. See [poc-findings/DEBUGGING-LOG.md](poc-findings/DEBUGGING-LOG.md).
