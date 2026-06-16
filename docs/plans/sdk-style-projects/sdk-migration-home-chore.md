<!--
  PASTE-READY body for a nanoFramework/Home "Chore or Task entry" issue
  (.github/ISSUE_TEMPLATE/chore_task.md). This is the consolidated EPIC doc — the former
  sdk-migration-tracking-issue.md was merged in here.

  ⚠️ The chore template is "[ONLY for Team Members]". If you're not on the team, file the
  Feature request version instead (sdk-migration-home-issue.md → already filed as
  nanoFramework/Home#1784).

  Suggested title: [Epic] SDK-style MSBuild project system migration
  Labels: Type: Chores (auto) — consider also: enhancement, area-Config-and-Build,
          area-Infrastructure-and-Organization, FEEDBACK REQUESTED

  Keep the "Details about Problem" headings + the "<!-- todo-tag DO NOT REMOVE -->" marker.
  Links are absolute permalinks pinned to commit b8c2ede in danielmeza/nf-Visual-Studio-extension.
  Demo: https://youtu.be/9qvXsgXCrjM
-->

## Details about Problem

nanoFramework area: **Visual Studio extension** (also MDP / CLI / MSBuild project system)

VS version (if relevant): Visual Studio 2022

VS extension version (if relevant): POC build off `2022.x` (dev `9.99.999.0`)

Target (if relevant): ESP32_S3_OCTAL

Firmware image version (if relevant): nanoCLR `2.0.0.467` (mscorlib native `100.22.0.4`, checksum `0x2D5CA905`)

## Description

<!-- todo-tag DO NOT REMOVE -->

**Epic:** move .NET nanoFramework from the legacy flavored `.nfproj` project system to an
**SDK-style** MSBuild project system (`<Project Sdk="…">`) — unlocking the `dotnet` CLI
(build / restore / pack / test), cross-platform builds and standard NuGet, and retiring
the custom project flavor.

**Status — the VS-debugger gate is proven solvable.** A proof-of-concept deploys and
debugs (F5 + source breakpoints) an SDK-style project on real hardware with the existing
AD7 engine **unchanged**. The gate was build-targets composition + the `NanoCSharpProject`
capability — not the engine. The intent of this epic is therefore to (a) agree the
destination, (b) land the now-proven SDK + debugging path, and (c) sequence the groundwork
(republish packages against `netnano1.0`, fix the import collision, fleet migration).

**The `nanoFramework.Sdk` repo now exists.** The MSBuild-SDK destination is no longer
hypothetical — [`nanoframework/nanoFramework.Sdk`](https://github.com/nanoframework/nanoFramework.Sdk)
(branch `move-to-sdk`, WIP, not yet released) packages the nanoFramework build pipeline
(C# compile → MDP IL→PE → resource generation → binary output) as a NuGet-distributed
MSBuild SDK, replacing the build infrastructure previously bundled in the VSIX. That repo
covers the **build** side; this POC additionally proved the **debugging** side (F5 +
source breakpoints in VS), so **debugging is no longer a blocker**. The two efforts
combine into the full SDK-style experience once the POC's capability injection + debugging
fixes land alongside the SDK.

Companion public proposal (Feature request form): **nanoFramework/Home#1784**.

### Demo

SDK-style project deploying and **F5 debugging with breakpoints hitting on a real
ESP32_S3_OCTAL** — the gate that was thought to block SDK-style:

[![Watch the demo on YouTube](https://img.youtube.com/vi/9qvXsgXCrjM/hqdefault.jpg)](https://youtu.be/9qvXsgXCrjM)

▶️ https://youtu.be/9qvXsgXCrjM

### The VS-debugger blocker — RESOLVED by the POC ✅ (decomposed, then confirmed on hardware)

Per maintainer feedback in
[#1635](https://github.com/orgs/nanoframework/discussions/1635), the move to SDK-style was
attributed to the **VS debugger**: SDK-style wasn't viable, with hope that a future VS
version makes it possible; the `dotnet` CLI flow is not an officially supported path today.

A code-level read of `nf-Visual-Studio-extension` (`develop`) **decomposed this** — a
hypothesis the executed POC then **confirmed on real hardware** (deploy + F5 + source
breakpoints, AD7 engine unchanged):

- The VS project system is **already CPS**, not a legacy MPF flavor
  (`NanoCSharpProject{Unconfigured,Configured}.cs`; `<ProjectCapability Include="CPS" />`
  in `NFProjectSystem.targets`).
- Deploy (`DeployProvider : IDeployProvider`) and debug-launch
  (`NanoDebuggerLaunchProvider : DebugLaunchProviderBase`) are **CPS providers** keyed off
  a `NanoCSharpProject` capability, and the engine is **launched by GUID**
  (`LaunchDebugEngineGuid = CorDebug.EngineGuid`). None of this inspects the project-file
  format.

So the concrete gate was **(1) build-targets composition** (the nano targets import the
legacy MSBuild chain and collide with `Microsoft.NET.Sdk` — #1635) and **(2) project-type
registration / capability injection**. The **AD7 debug engine is orthogonal** (confirmed)
— launched by GUID, it attaches to an SDK-style CPS project unchanged once that project
carries the capability. The **AD7 → Concord** engine migration is separate modernization
(future-proofing against AD7 deprecation), **not** the unlock.

**Executed — the A+C proof-of-concept (gate passed ✅):** a minimal `nanoFramework.Sdk`
composing over `Microsoft.NET.Sdk` + the injected `NanoCSharpProject` capability, AD7
engine kept behind an **engine-binding abstraction** for a future Concord swap. On a real
ESP32_S3_OCTAL the SDK-style sample loads in VS, deploys via F5, and a breakpoint **binds
and hits**. Concrete fixes (full record:
[DEBUGGING-LOG.md](https://github.com/danielmeza/nf-Visual-Studio-extension/blob/b8c2edeb1ff775e3f78ba74af9ed384d1ee5c333/poc-sdk-style/DEBUGGING-LOG.md)):

- **Breakpoints** — SDK forces `DebugType=full` for Debug (Windows/full PDB; a portable PDB made VS bind at the method entry).
- **Deploy version mismatch** — relax the extension's deploy pre-check to a checksum match.
- **F5 launched a console app** — SDK removes the `LaunchProfiles` capability + sets `DebuggerFlavor=NanoDebugger`.
- **Legacy `.nfproj` + SDK `.csproj` load side by side** in the experimental instance.

What's reachable regardless of the gate:

- **Build / pack / test.** MDP and the test adapter look only at build *outputs* and standard MSBuild items. In [#1635](https://github.com/orgs/nanoframework/discussions/1635) an SDK-style project targeting `netnano1.0` was made to build (`Microsoft.NET.Sdk` plus imported NFProjectSystem targets).
- Prior art on the deploy crawler: [nf-Visual-Studio-extension#889](https://github.com/nanoframework/nf-Visual-Studio-extension/pull/889).

### Motivation

The legacy project system relies on a project flavor, MSBuild targets shipped via the VS /
VS Code extensions, `packages.config`, hand-written `.nuspec`, AnyCPU-only builds and an
x64 task / `nodeReuse` workaround. That couples builds to the IDE extensions and diverges
from mainstream .NET tooling, which makes the CLI and CI story harder than it needs to be.
SDK-style gives `dotnet build` / `dotnet pack`, `PackageReference`, and an IDE-agnostic,
CI-friendly experience.

### Target framework moniker — already recognized

`netnano1.0` **is** a recognized TFM in the .NET SDK / NuGet client (see the
[Microsoft TFM table](https://learn.microsoft.com/en-us/dotnet/standard/frameworks#supported-target-frameworks)).
The remaining gap is that the nanoFramework **NuGet packages aren't published against
`netnano1.0`** yet — consumers currently fall back to `net` to restore — and projects
still use `packages.config`. Closing that gap is unblocked work and independent of the
debugger.

### Goals

**Near-term (not blocked by the debugger):**

- Publish class-library packages so they properly target `netnano1.0` (removing the need for `AssetTargetFallback` to `net`).
- Fix the NFProjectSystem targets so they compose in SDK-style / imported contexts without the double-import error (see [#1635](https://github.com/orgs/nanoframework/discussions/1635), [#1067](https://github.com/nanoframework/Home/issues/1067)).
- Stand up an **experimental, opt-in** CLI build/pack/test path for SDK-style projects.
- Migration tooling is ready; with the gate cleared (POC), it can proceed.

**Was gated on the debugger — now PROVEN by the POC (remaining work is productization, not feasibility):**

- VS debugging / F5 on SDK-style projects — ✅ demonstrated on real hardware.
- SDK-style as the *supported, default* project format — now unblocked to pursue.
- Retiring the project flavor and the legacy `.nfproj` — now feasible (kept supported during transition).

### Non-goals — out of scope for this effort

- OTA update system.
- Modular / relocatable native firmware packaging (`runtimes/{rid}/native`, ABI / module manifests).
- Any firmware- or device-side changes.

### High-level approach

- Destination is a `nanoFramework.Sdk` (a thin SDK composing over `Microsoft.NET.Sdk`, with room to evolve toward a workload).
- The **metadata processor is already an MSBuild task**, so re-host it as an incremental target rather than wrapping a shell-out.
- Land changes additively and keep legacy `.nfproj` working throughout.

### Phased rollout

- **Phase 0 — Proposal & decisions** (this epic).
- **Phase 1 — Unblocked groundwork**: package `netnano1.0` targeting; targets import fix; experimental CLI build/pack/test; migration tooling ready.
- **Phase 2 — Debugger enablement: the A+C POC (THE GATE) — ✅ PASSED**: minimal `nanoFramework.Sdk` over `Microsoft.NET.Sdk` + `NanoCSharpProject` capability, AD7 engine behind the engine-binding seam. Gate met on real hardware (deploy + F5 + breakpoints).
- **Phase 3 — SDK-style as a supported option**: VS debug/F5 on SDK-style via the proven path; `nanoFramework.Sdk` published. (Optional, parallel: AD7 → Concord for future-proofing.)
- **Phase 4 — Library fleet migration** (leaf-first), using the tooling.
- **Phase 5 — Deprecate** the legacy project system.

### Key open decisions

- **Resolved:** `nanoFramework.Sdk` lives in its own repo — [`nanoframework/nanoFramework.Sdk`](https://github.com/nanoframework/nanoFramework.Sdk) (WIP on `move-to-sdk`). Still open: versioning/publishing cadence and the first released version (README references `0.1.0`, not yet published).
- Republish strategy for packages targeting `netnano1.0`.
- Whether to support an interim `Microsoft.NET.Sdk` + imported-targets shape (as in #1635) vs. waiting for the clean `nanoFramework.Sdk`.

### Affected repositories (initial)

- `nf-Visual-Studio-extension` — project system, build tasks, **and the debugger / F5 deployer**.
- `metadata-processor` — the MDP build task.
- `CoreLibrary` — first validation target (special case).
- `Samples` — end-to-end validation.
- `nf-VSCodeExtension` — consumer; simplifies once the SDK exists.
- [`nanoframework/nanoFramework.Sdk`](https://github.com/nanoframework/nanoFramework.Sdk) — **the SDK itself, now created** (MSBuild SDK + build tasks; WIP on `move-to-sdk`, not yet released).
- the `lib-*` fleet — later phase.

### Work / tracking checklist

- [x] NFProjectSystem targets import fixed for SDK-style/imported contexts — the POC SDK composes over `Microsoft.NET.Sdk` and owns the import chain
- [x] Experimental CLI build/pack/test validated — POC `Blink` builds `.pe`/`.pdbx`, cross-platform
- [x] **Gate:** VS debugger works on SDK-style projects — **PROVEN on real hardware** (deploy + F5 + source breakpoints)
- [x] `nanoFramework.Sdk` repo home decided — [`nanoframework/nanoFramework.Sdk`](https://github.com/nanoframework/nanoFramework.Sdk) created (WIP on `move-to-sdk`)
- [ ] Land `nanoFramework.Sdk` v1 (versioning/publish cadence; first released version) and agree interim-shape policy
- [ ] Packages republished targeting `netnano1.0`
- [ ] Fold the POC's debugging fixes (capability injection, `DebugType=full`, deploy checksum pre-check, F5 wiring) into `nanoFramework.Sdk` + the extension (strip the `[BP-DIAG]` diagnostics)
- [ ] SDK-style supported as an option; preview `nanoFramework.Sdk` published
- [ ] Fleet migration (leaf-first) of the `lib-*` repos
- [ ] Legacy project system deprecated (kept supported during transition)

### POC artifacts — permalinks

The executed POC lives on branch `poc/sdk-style-debugging`
([danielmeza/nf-Visual-Studio-extension](https://github.com/danielmeza/nf-Visual-Studio-extension)).
Permalinks pinned to commit
[`b8c2ede`](https://github.com/danielmeza/nf-Visual-Studio-extension/commit/b8c2edeb1ff775e3f78ba74af9ed384d1ee5c333):

**The SDK + sample (what a project author writes):**
- [`poc-sdk-style.sln`](https://github.com/danielmeza/nf-Visual-Studio-extension/blob/b8c2edeb1ff775e3f78ba74af9ed384d1ee5c333/poc-sdk-style/poc-sdk-style.sln) — solution with the SDK-style and legacy projects side by side
- [`Sdk.props`](https://github.com/danielmeza/nf-Visual-Studio-extension/blob/b8c2edeb1ff775e3f78ba74af9ed384d1ee5c333/poc-sdk-style/nanoFramework.Sdk/Sdk/Sdk.props) · [`Sdk.targets`](https://github.com/danielmeza/nf-Visual-Studio-extension/blob/b8c2edeb1ff775e3f78ba74af9ed384d1ee5c333/poc-sdk-style/nanoFramework.Sdk/Sdk/Sdk.targets) · [`nanoFramework.Mdp.targets`](https://github.com/danielmeza/nf-Visual-Studio-extension/blob/b8c2edeb1ff775e3f78ba74af9ed384d1ee5c333/poc-sdk-style/nanoFramework.Sdk/Sdk/nanoFramework.Mdp.targets) — the `nanoFramework.Sdk` (composition + MDP re-host; `Sdk.props` carries the `DebugType=full` breakpoint fix)
- [`samples/Blink/Blink.csproj`](https://github.com/danielmeza/nf-Visual-Studio-extension/blob/b8c2edeb1ff775e3f78ba74af9ed384d1ee5c333/poc-sdk-style/samples/Blink/Blink.csproj) — the ~6-line SDK-style app
- [`dev-install-legacy-targets.ps1`](https://github.com/danielmeza/nf-Visual-Studio-extension/blob/b8c2edeb1ff775e3f78ba74af9ed384d1ee5c333/poc-sdk-style/dev-install-legacy-targets.ps1) — dev helper so a legacy `.nfproj` loads in the experimental instance

**Extension changes (the gate fixes):**
- [`DeployProvider.cs`](https://github.com/danielmeza/nf-Visual-Studio-extension/blob/b8c2edeb1ff775e3f78ba74af9ed384d1ee5c333/vs-extension.shared/DeployProvider/DeployProvider.cs) — checksum-only deploy pre-check + deploy follows the selected device
- [`Ad7CorDebugEngineBinding.cs`](https://github.com/danielmeza/nf-Visual-Studio-extension/blob/b8c2edeb1ff775e3f78ba74af9ed384d1ee5c333/vs-extension.shared/DebugLauncher/Ad7CorDebugEngineBinding.cs) — per-device port via the engine-binding seam
- [`PdbxFile.cs`](https://github.com/danielmeza/nf-Visual-Studio-extension/blob/b8c2edeb1ff775e3f78ba74af9ed384d1ee5c333/vs-extension.shared/CorDebug/PdbxFile.cs) · [`CorDebugBreakpoint.cs`](https://github.com/danielmeza/nf-Visual-Studio-extension/blob/b8c2edeb1ff775e3f78ba74af9ed384d1ee5c333/vs-extension.shared/CorDebug/CorDebugBreakpoint.cs) · [`CorDebugFunction.cs`](https://github.com/danielmeza/nf-Visual-Studio-extension/blob/b8c2edeb1ff775e3f78ba74af9ed384d1ee5c333/vs-extension.shared/CorDebug/CorDebugFunction.cs) · [`CorDebugCode.cs`](https://github.com/danielmeza/nf-Visual-Studio-extension/blob/b8c2edeb1ff775e3f78ba74af9ed384d1ee5c333/vs-extension.shared/CorDebug/CorDebugCode.cs) — `[BP-DIAG]` breakpoint diagnostics (POC-grade)

**Results & decision record:**
- [`RESULTS.md`](https://github.com/danielmeza/nf-Visual-Studio-extension/blob/b8c2edeb1ff775e3f78ba74af9ed384d1ee5c333/poc-sdk-style/RESULTS.md) — what's proven
- [`DEBUGGING-LOG.md`](https://github.com/danielmeza/nf-Visual-Studio-extension/blob/b8c2edeb1ff775e3f78ba74af9ed384d1ee5c333/poc-sdk-style/DEBUGGING-LOG.md) — every blocker hit and its fix (§1–§6) + dead ends
- [`DEVICE-RUN-DROPDOWN.md`](https://github.com/danielmeza/nf-Visual-Studio-extension/blob/b8c2edeb1ff775e3f78ba74af9ed384d1ee5c333/poc-sdk-style/DEVICE-RUN-DROPDOWN.md) — multi-device Run-selection design
- Full specification set: [`docs/plans/sdk-style-projects/`](https://github.com/danielmeza/nf-Visual-Studio-extension/tree/b8c2edeb1ff775e3f78ba74af9ed384d1ee5c333/docs/plans/sdk-style-projects)

### References

- Discussion: [#1635 — NFProjectSystem.CSharp.targets import failed in SDK Style project](https://github.com/orgs/nanoframework/discussions/1635)
- Related: [Home#1067](https://github.com/nanoframework/Home/issues/1067), [nf-Visual-Studio-extension#889](https://github.com/nanoframework/nf-Visual-Studio-extension/pull/889)
- [Microsoft TFM table (lists `netnano1.0`)](https://learn.microsoft.com/en-us/dotnet/standard/frameworks#supported-target-frameworks)
- [Concord debugger extensibility samples (for a future AD7 → Concord engine; see *Iris*)](https://github.com/microsoft/ConcordExtensibilitySamples)

### Notes

- Migration tooling to convert `.nfproj` → SDK-style and bulk-migrate the fleet is already prototyped (emits `netnano1.0`); it supports the later migration phase.
- Feedback on direction and on the open decisions is very welcome. 🙂
