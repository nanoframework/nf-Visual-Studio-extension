<!--
  PASTE-READY issue body for nanoFramework/Home, formatted to the repo's
  "Feature request" issue form (.github/ISSUE_TEMPLATE/feature_request.yml):
  sections = Description / How to solve the problem / Alternatives / Additional context.

  When filing: pick "Feature request" (auto-labels: "Type: Feature request",
  "Status: waiting feedback"), then paste each section into the matching field — or
  open a blank issue and paste the whole body below.

  Suggested title:
  SDK-style MSBuild project system for .NET nanoFramework (VS debugger gate proven solvable)

  All links are absolute permalinks pinned to commit b8c2ede in
  danielmeza/nf-Visual-Studio-extension so they resolve from the issue body.
-->

## Description

.NET nanoFramework projects build through a **legacy custom project flavor**
(`.nfproj`, flavor GUID `{11A8DD76-328B-46DF-9F39-F559912D0360}` over the old C#
project system), with the MSBuild targets shipped by the VS / VS Code extensions and
dependencies via `packages.config` + a hand-written `.nuspec`.

This blocks the modern **SDK-style** project format (`<Project Sdk="…">`) and, with it,
the standard `dotnet` CLI experience (`build` / `restore` / `pack` / `test`),
cross-platform builds, and low-friction maintenance of the ~100+ `lib-*` repos. The
move to SDK-style has been attributed to the **Visual Studio debugger**
([discussion #1635](https://github.com/orgs/nanoframework/discussions/1635)) — held to
need a future VS version — and the `dotnet` CLI flow is not an officially supported path
today.

## How to solve the problem

Author a minimal **`nanoFramework.Sdk`** MSBuild project SDK that **composes over
`Microsoft.NET.Sdk`** (owns the import chain, re-hosts the Metadata Processor PE stage)
and **injects the `NanoCSharpProject` capability** that the existing CPS deploy/debug
providers key off. The deploy provider and the AD7 debug engine are **launched by GUID**
and never inspect the project-file format, so the engine is **orthogonal** to the
project shape — the real gate was build-targets composition + capability registration.

**A proof-of-concept proved this end to end on real hardware** (ESP32_S3_OCTAL): an
SDK-style `Blink.csproj` **builds, deploys, runs, and debugs with F5 + source
breakpoints** using the existing AD7 engine **unchanged**. Concrete issues found and
fixed (full record:
[DEBUGGING-LOG.md](https://github.com/danielmeza/nf-Visual-Studio-extension/blob/b8c2edeb1ff775e3f78ba74af9ed384d1ee5c333/poc-sdk-style/DEBUGGING-LOG.md)):

- **Breakpoints** — the SDK forces `DebugType=full` for Debug so VS gets a *Windows/full*
  PDB; a portable PDB made VS bind at the method entry (IL 0), never the source line.
- **Deploy version mismatch** — relax the extension's deploy pre-check to a **checksum**
  match (firmware mscorlib `100.22.0.4` vs published `.5`, identical checksum; the
  runtime links on name + major.minor anyway).
- **F5 launched a console app** — the SDK removes the `LaunchProfiles` capability and
  sets `DebuggerFlavor=NanoDebugger`.
- **Legacy `.nfproj` + SDK `.csproj` load side by side** in the experimental instance.

Proposed sequence: ship the SDK + capability (debugging now proven) → republish packages
against `netnano1.0` → fix the `NFProjectSystem.CSharp.targets` import collision (the SDK
owns the chain) → migrate the fleet leaf-first → deprecate (not delete) the flavor.

**POC artifacts (permalinks @ `b8c2ede`):**

- SDK: [`Sdk.props`](https://github.com/danielmeza/nf-Visual-Studio-extension/blob/b8c2edeb1ff775e3f78ba74af9ed384d1ee5c333/poc-sdk-style/nanoFramework.Sdk/Sdk/Sdk.props) · [`Sdk.targets`](https://github.com/danielmeza/nf-Visual-Studio-extension/blob/b8c2edeb1ff775e3f78ba74af9ed384d1ee5c333/poc-sdk-style/nanoFramework.Sdk/Sdk/Sdk.targets) · [`nanoFramework.Mdp.targets`](https://github.com/danielmeza/nf-Visual-Studio-extension/blob/b8c2edeb1ff775e3f78ba74af9ed384d1ee5c333/poc-sdk-style/nanoFramework.Sdk/Sdk/nanoFramework.Mdp.targets)
- Sample: [`samples/Blink/Blink.csproj`](https://github.com/danielmeza/nf-Visual-Studio-extension/blob/b8c2edeb1ff775e3f78ba74af9ed384d1ee5c333/poc-sdk-style/samples/Blink/Blink.csproj) (the ~6-line SDK-style app) · [`poc-sdk-style.sln`](https://github.com/danielmeza/nf-Visual-Studio-extension/blob/b8c2edeb1ff775e3f78ba74af9ed384d1ee5c333/poc-sdk-style/poc-sdk-style.sln)
- Extension fixes: [`DeployProvider.cs`](https://github.com/danielmeza/nf-Visual-Studio-extension/blob/b8c2edeb1ff775e3f78ba74af9ed384d1ee5c333/vs-extension.shared/DeployProvider/DeployProvider.cs) · [`Ad7CorDebugEngineBinding.cs`](https://github.com/danielmeza/nf-Visual-Studio-extension/blob/b8c2edeb1ff775e3f78ba74af9ed384d1ee5c333/vs-extension.shared/DebugLauncher/Ad7CorDebugEngineBinding.cs)
- Results: [`RESULTS.md`](https://github.com/danielmeza/nf-Visual-Studio-extension/blob/b8c2edeb1ff775e3f78ba74af9ed384d1ee5c333/poc-sdk-style/RESULTS.md)

## Describe alternatives you've considered

- **Stay on the legacy `.nfproj` flavor** (status quo) — no `dotnet` CLI / cross-platform
  builds / standard restore + pack; continued maintenance of a custom project flavor and
  extension-shipped targets.
- **Rewrite the debugger AD7 → Concord first** — the POC put an engine-binding seam
  ([`INanoDebugEngineBinding`](https://github.com/danielmeza/nf-Visual-Studio-extension/blob/b8c2edeb1ff775e3f78ba74af9ed384d1ee5c333/vs-extension.shared/DebugLauncher/Ad7CorDebugEngineBinding.cs))
  in place for this, but it was **not needed** for the unlock. Concord stays a deferred
  modernization (future-proofing against AD7 deprecation), behind that seam.
- **Wait for a future Visual Studio version** — unnecessary: the current VS + the existing
  AD7 engine already work with an SDK-style project once it carries the capability.

## Additional context

### Demo

SDK-style project deploying and **F5 debugging with breakpoints hitting on a real
ESP32_S3_OCTAL** (the gate that was thought to block SDK-style):

[![Watch the demo on YouTube](https://img.youtube.com/vi/9qvXsgXCrjM/hqdefault.jpg)](https://youtu.be/9qvXsgXCrjM)

▶️ https://youtu.be/9qvXsgXCrjM

### Design + decision record

- Full specification set (overview, SDK design, MDP integration, CLI, IDE, library
  migration, NuGet, phasing): [`docs/plans/sdk-style-projects/`](https://github.com/danielmeza/nf-Visual-Studio-extension/tree/b8c2edeb1ff775e3f78ba74af9ed384d1ee5c333/docs/plans/sdk-style-projects)
- Decision record — every blocker hit and its fix (§1–§6) + dead ends: [`DEBUGGING-LOG.md`](https://github.com/danielmeza/nf-Visual-Studio-extension/blob/b8c2edeb1ff775e3f78ba74af9ed384d1ee5c333/poc-sdk-style/DEBUGGING-LOG.md)
- Multi-device Run-selection design (MAUI-style `IVsProjectCfgDebugTargetSelection`): [`DEVICE-RUN-DROPDOWN.md`](https://github.com/danielmeza/nf-Visual-Studio-extension/blob/b8c2edeb1ff775e3f78ba74af9ed384d1ee5c333/poc-sdk-style/DEVICE-RUN-DROPDOWN.md)
- POC archive: [`poc-sdk-style-archive`](https://github.com/danielmeza/nf-Visual-Studio-extension/tree/poc-sdk-style-archive) (commit [`b8c2ede`](https://github.com/danielmeza/nf-Visual-Studio-extension/commit/b8c2edeb1ff775e3f78ba74af9ed384d1ee5c333))

### Environment

- Device under test: **ESP32_S3_OCTAL**, nanoCLR `2.0.0.467`, firmware mscorlib native
  `v100.22.0.4` (checksum `0x2D5CA905`); `nanoFramework.CoreLibrary 2.0.0-preview.52`.
- The build-side POC is cross-platform (originally produced on macOS); only VS debugging
  is Windows-only.

### Related

- [#1635 — NFProjectSystem.CSharp.targets import failed in SDK-Style project](https://github.com/orgs/nanoframework/discussions/1635)
- [Home#1067](https://github.com/nanoframework/Home/issues/1067) · [nf-Visual-Studio-extension#889](https://github.com/nanoframework/nf-Visual-Studio-extension/pull/889)
- [Microsoft TFM table (lists `netnano1.0`)](https://learn.microsoft.com/en-us/dotnet/standard/frameworks#supported-target-frameworks)
