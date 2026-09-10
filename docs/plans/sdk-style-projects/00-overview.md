# nanoFramework SDK-Style Project System Migration — Specification Set

**Status:** Draft v2
**Scope:** Migration of .NET nanoFramework from the legacy `.nfproj` flavored
project system to a first-class MSBuild SDK — **managed project system only**.
**Audience:** nanoFramework core contributors with CLR-internals and MSBuild
expertise.

---

## 0.1 Why this exists

The legacy project system relies on a project flavor, MSBuild targets shipped via
the VS / VS Code extensions, `packages.config`, hand-written `.nuspec`, AnyCPU-only
builds, and an x64-task / `nodeReuse` workaround. That couples builds to the IDE
extensions and diverges from mainstream .NET tooling, making the CLI and CI story
harder than it should be. This set specifies a move to an MSBuild **SDK**, so that
`dotnet build` / `dotnet pack` / `dotnet test` work on a clean machine with only
the .NET SDK and a NuGet restore.

## 0.2 Scope boundary (read this first)

This is a **managed project-system migration**. The following are **out of scope**
and belong to a separate, later effort — they are deliberately absent from these
specs and must not be reintroduced:

- OTA update system.
- Modular / relocatable native firmware packaging, native compile/link, and any
  native binaries shipped inside NuGet packages (`runtimes/{rid}/native/`,
  pre-linked modules, module/ABI manifests, CoreRuntime firmware packs, toolchain
  packs).
- Any firmware- or device-side changes.

NuGet packages in scope ship **managed** assets only (`.pe` + reference `.dll` +
`.pdbx` + `.xml`).

## 0.3 The blocker — RESOLVED by the POC ✅

The maintainer attributed the SDK-style block to the **VS debugger**
([#1635](https://github.com/orgs/nanoframework/discussions/1635)). A code-level
read of `nf-Visual-Studio-extension` (`develop`) **decomposed** that into a more
tractable picture — and the executed POC then **confirmed** the decomposition by
achieving deploy + F5 + source breakpoints on a real ESP32_S3_OCTAL:

- The VS project system is **already CPS**, not a legacy MPF flavor
  (`NanoCSharpProject{Unconfigured,Configured}.cs`;
  `<ProjectCapability Include="CPS" />` in `NFProjectSystem.targets`).
- Deploy (`DeployProvider : IDeployProvider`) and debug-launch
  (`NanoDebuggerLaunchProvider : DebugLaunchProviderBase`) are **CPS providers**
  keyed off the `NanoCSharpProject` capability, and the engine is **launched by
  GUID** (`LaunchDebugEngineGuid = CorDebug.EngineGuid`). None of this inspects the
  project-file format.

So the concrete gate was **(1) build-targets composition** — the nano targets import
the legacy MSBuild chain and collide with `Microsoft.NET.Sdk` (#1635) — and
**(2) project-type registration / capability injection** onto SDK-style projects. The
**AD7 debug engine (`CorDebug`) is orthogonal** — confirmed: launched by GUID, it
attaches to the SDK-style CPS project unchanged once the project carries the
`NanoCSharpProject` capability. Migrating the engine **AD7 → Concord** is separate
modernization (future-proofing against AD7 deprecation), **not** the unlock.

**Plan of record (executed):** an A+C proof-of-concept — author a minimal
`nanoFramework.Sdk` + inject the capability, keep the AD7 engine behind an
engine-binding abstraction so Concord can be swapped later
([poc-sdk-style-debugging-plan.md](poc-sdk-style-debugging-plan.md)). **The POC is
done and the gate is cleared on hardware.** Results in
[poc-findings/RESULTS.md](poc-findings/RESULTS.md); the full decision record
(every blocker + fix, §1–§6) in
[poc-findings/DEBUGGING-LOG.md](poc-findings/DEBUGGING-LOG.md). Four issues
surfaced and were fixed: F5-console (LaunchProfiles removed + `DebuggerFlavor`),
deploy version mismatch (checksum-only pre-check), breakpoints (Debug must emit a
**Windows/full** PDB, not portable), and dev-only legacy `.nfproj` load (surface the
`InstallRoot="MSBuild"` assets).

What this means in practice now:

## 0.3.1 Blocked vs. not blocked → all paths proven

- **Never blocked:** build, pack, and test via the CLI (cross-platform). MDP and the
  test adapter look only at build outputs and standard MSBuild items, so they're
  project-type agnostic.
- **Was blocked, now proven:** VS deploy + debugging on SDK-style projects. The POC
  demonstrated it on real hardware, so the flavor *can* be retired.

The remaining work is **productization** — packaging/publishing the `nanoFramework.Sdk`,
folding the POC fixes back into the shipped extension, and fleet migration (doc 09) —
**not** a feasibility question.

## 0.4 Corrected premises

Two framing assumptions from the original prompt that the codebase / ecosystem
have invalidated:

1. **MDP is already an MSBuild task**, not an external post-build tool. It ships as
   `nanoFramework.Tools.MetadataProcessor.MsBuildTask` wired into
   `NFProjectSystem.MDP.targets` (`GenerateBinaryOutputTask` et al.), with a
   `.CLI` variant for runtime-codegen. The work is **re-hosting** it inside an SDK
   with proper incrementality and ordering (doc 04).
2. **`netnano1.0` is a real, recognized TFM.** It appears in the
   [Microsoft TFM table](https://learn.microsoft.com/en-us/dotnet/standard/frameworks#supported-target-frameworks)
   (".NET nanoFramework → `netnano1.0`"), recognized by the .NET SDK and NuGet
   client. The real gap is narrower: nanoFramework's **packages aren't published
   against `netnano1.0`** yet (consumers fall back to `net` to restore — see
   [#1635](https://github.com/orgs/nanoframework/discussions/1635)), and projects
   still use `packages.config`. Closing that is unblocked work (doc 02 §2.2).

Two further realities the specs build on:

- The current project-system files (`NFProjectSystem.Default.props`,
  `NFProjectSystem.props`, `NFProjectSystem.CSharp.targets`,
  `NFProjectSystem.MDP.targets`) are distributed via the **VS extension**
  (`$(MSBuildExtensionsPath)\nanoFramework\v1.0\`, shipped as `InstallRoot="MSBuild"`
  VSIX assets — note a non-elevated *experimental-instance* deploy can't surface these,
  which is why a legacy `.nfproj` fails to load there until restored; see DEBUGGING-LOG
  §6) and the **VS Code extension** (`dist/utils/nanoFramework/v1.0/`), located via
  `$(NanoFrameworkProjectSystemPath)`. One of them
  (`NFProjectSystem.CSharp.targets`) re-imports
  `Microsoft.CSharp.CurrentVersion.targets`, which collides in SDK-style/imported
  contexts (#1635, #1067) — the SDK must own the import chain.
- The project flavor GUID `{11A8DD76-328B-46DF-9F39-F559912D0360}` (plus the C#
  GUID `{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}`) is what makes VS load the custom
  project system, and is tied to the debugger gate (doc 06).

## 0.5 Document map

| Doc | Title |
|-----|-------|
| 00 | Overview (this doc) |
| [01](01-current-state.md) | Current State Analysis |
| [02](02-sdk-design.md) | SDK Design: `nanoFramework.Sdk`, the TFM, the target graph |
| [03](03-project-file-migration.md) | Project File Migration |
| [04](04-mdp-native-integration.md) | Metadata Processor (MDP) Integration |
| [05](05-cli-experience.md) | CLI Experience (`dotnet build/deploy/new/watch`) |
| [06](06-ide-integration.md) | Visual Studio & VS Code Integration (debugger gate cleared ✅) |
| [07](07-library-migration.md) | Library Repository Migration (~100+ repos) |
| [08](08-nuget-pipeline.md) | NuGet Pipeline (managed `pack`) |
| [09](09-implementation-strategy.md) | Implementation Strategy & Phasing |
| [10](10-tooling-specs.md) | Tooling Specifications, Package Layouts, Templates |
| [POC](poc-sdk-style-debugging-plan.md) | A+C debugging proof-of-concept (executed: [results](poc-findings/RESULTS.md)) |
| [VSCode](vscode-extension-impact.md) | VS Code extension migration impact |

## 0.6 Naming conventions used across the set

- **SDK package:** `nanoFramework.Sdk` (the MSBuild project SDK). Referenced as
  `<Project Sdk="nanoFramework.Sdk/<version>">`. The SDK package version is
  independent of the TFM.
- **TFM:** `netnano1.0`, long form `.NETnanoFramework,Version=v1.0`.
- **Deploy tool:** `nanoff` remains the on-device executor; the SDK target
  `Deploy` orchestrates it (doc 05).

## 0.7 What "done" looks like (for the unblocked, managed part)

A minimal nanoFramework app is a single `.csproj`:

```xml
<Project Sdk="nanoFramework.Sdk/1.0.0">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>netnano1.0</TargetFramework>
  </PropertyGroup>
</Project>
```

`dotnet build` produces a `.pe` (+ `.pdbx`); `dotnet pack` emits a package with the
managed assets under `lib/netnano1.0/`; `dotnet test` runs the unit tests. No VS
extension is required to build, pack, or test. VS debugging of SDK-style projects is now
**proven** (the POC: F5 + source breakpoints on real hardware — doc 06, doc 09 §9.5);
productizing it in the shipped extension is the remaining step. The MSBuild SDK itself now
exists as the official repo
[`nanoframework/nanoFramework.Sdk`](https://github.com/nanoframework/nanoFramework.Sdk)
(WIP on `move-to-sdk`).
