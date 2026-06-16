# Execution plan — land the POC into `nanoFramework.NET.Sdk` + the extension

How the POC's results get contributed upstream now that the official SDK repo exists.
Status of the POC itself: build + deploy + **F5/breakpoints proven on real hardware**
(see [RESULTS.md](poc-findings/RESULTS.md), [DEBUGGING-LOG.md](poc-findings/DEBUGGING-LOG.md)).

> **Implementation status (2026-06-15): all enablers implemented, built, and pushed.**
> The four SDK gaps (A1–A4) and the extension changes (B1–B3) below are **done** on the
> `move-to-sdk` branch of each fork; the engine-binding seam (WS3) is implemented and the
> extension compiles clean. What remains is opening the upstream PRs (held for review).
>
> | Area | Commits (`danielmeza/*` `move-to-sdk`) |
> |---|---|
> | SDK A1–A3 (full PDB + F5 wiring) | `nanoFramework.Sdk` @ `c89402d` |
> | SDK A4 (MDP 4.x / NFMRK2 + net8.0) | `nanoFramework.Sdk` @ `ecb7c02` |
> | Extension B1–B2 (checksum pre-check + SelectedDevice) | `nf-Visual-Studio-extension` @ `746b408` |
> | Extension WS3 (engine-binding seam) + B3 (no `[BP-DIAG]`) | `nf-Visual-Studio-extension` @ `b897f7b` |

## Workspace & repos (all cloned; forks + upstreams wired)

All clones live under `D:\src\nnf\`; each `origin` = a `danielmeza/*` fork, `upstream` =
`nanoframework/*`. The `lib-*` fleet (doc 07) is intentionally **excluded** — later phase.

Parity branch in every repo: **`move-to-sdk`** (the org's chosen name — already the SDK's
branch and present in CoreLibrary upstream). All six forks have `origin/move-to-sdk`.

| Repo | Role in the plan | Clone dir | `move-to-sdk` born from |
|---|---|---|---|
| **nanoFramework.NET.Sdk** | SDK contribution (A1–A4) | `nanoFramework.Sdk` | the repo's dev branch (it *is* `move-to-sdk`; no `develop`) |
| **nf-Visual-Studio-extension** | extension fixes (B) + this plan | `nf-Visual-Studio-extension` | `develop` — B1–B3 + WS3 + the plan **migrated** off the POC; the old branch is deleted, its commit archived at tag `poc-sdk-style-archive` so the `#1784` `b8c2edeb` permalinks still resolve |
| metadata-processor | MDP build task (A4, only if v2) | `metadata-processor` | `develop` |
| CoreLibrary | corlib migration + validation | `nanoFramework-CoreLibrary` | tracks the org's `upstream/move-to-sdk`; fork keeps the old name, ~51 behind |
| Samples | end-to-end validation (deploy/debug a real app) | `Samples` | `main` (repo has no `develop`) |
| nf-VSCodeExtension | consumer simplification (later phase) | `nf-VSCodeExtension` | `develop` |

Baseline: the SDK's `nanoFramework.Tools.BuildTasks` builds clean (0 errors; only an
NU1903 advisory on `Microsoft.Build.Utilities.Core`).

## Organization — follow the POC's layout

Carry the POC's modular, self-documenting layout into the contribution rather than growing
the official monolithic `Sdk.targets`:
- **`Sdk/Rules/`** folder for XAML rules (e.g. `NanoDebugger.xaml`) — as in the POC.
- Keep **debugging / MDP concerns in their own include(s)** (the POC split out
  `nanoFramework.Mdp.targets`) so the additions stay reviewable + separable, with clear
  sectioning and comments.
- A self-contained **`test/`** sample that exercises build → deploy → F5 (mirrors the POC's
  `samples/Blink`).
- Don't restructure the maintainers' existing files beyond what each change needs; offer the
  fuller split as a follow-up only if they want it.

## Naming — align to the official name

The official package is **`nanoFramework.NET.Sdk`** (per `SDK naming.md`, the
`<Org>.NET.Sdk` pattern, mirroring `Tizen.NET.Sdk`). The POC used `nanoFramework.Sdk`;
all contributions use **`nanoFramework.NET.Sdk`**.

## Gap analysis — official `move-to-sdk` vs the POC

**Already in the official SDK** (no work needed): `Microsoft.NET.Sdk` composition;
`netnano1.0` TFM + identity; `AssetTargetFallback=net`/`NoStdLib`/`TargetingClr2Framework`;
`DebuggerFlavor=NanoDebugger` (`Sdk.props`); `NanoCSharpProject` capability (`Sdk.targets`);
the full MDP pipeline (parse→compile, stubs, core-lib path, resource gen, binary output);
bundled build tasks; auto-injected MDP package (was `3.0.29` at baseline; **A4 bumps it to
`4.0.0-preview.94`** for NFMRK2).

**Missing — the POC's debugging enablers** (what makes VS *deploy + debug*, not just build) —
**all four implemented** in `nanoFramework.Sdk` `move-to-sdk` (`c89402d` A1–A3, `ecb7c02` A4):

| # | Gap | Fix (from the POC) | Where | Status |
|---|---|---|---|---|
| A1 | **Breakpoints bind at method entry** | emit a **Windows/full PDB** for Debug: set `DebugType=full` **before** the `Microsoft.NET.Sdk` import (so its `==''→portable` default is pre-empted; under VS's .NET-Framework csc this yields a Windows PDB) | `Sdk/Sdk.props` | ✅ `c89402d` |
| A2 | **F5 can launch a console app** | `<ProjectCapability Remove="LaunchProfiles" />` so the C# project system's launcher doesn't own F5 (the `DebuggerFlavor` alone isn't enough) | `Sdk/Sdk.targets` | ✅ `c89402d` |
| A3 | **No debugger property page** | ship `Rules/NanoDebugger.xaml` + a `PropertyPageSchema` (`Context=Project`) | SDK `Sdk/Rules/` + targets | ✅ `c89402d` |
| A4 | **v2 devices reject the PE** (`NFMRK1` vs `NFMRK2`) | bump MDP `3.0.29 → 4.x` (emits `NFMRK2`) **and** fix the MDP task TFM `net6.0 → net8.0` for `MSBuildRuntimeType==Core` (4.x ships `net8.0`+`net472`) — **decision: target v2** (`4.0.0-preview.94`) | `Sdk/Sdk.props` (`NanoFrameworkMDPVersion`) + `Sdk/Sdk.targets` (`_NfMdpTasksTFM`) | ✅ `ecb7c02` |

(The breakpoint fix is **entirely SDK-side** — A1. The engine reads the Windows PDB
already; the POC's `[BP-DIAG]` was only diagnostics.) Validated via VS MSBuild on
`test/SmokeTest`: PE magic = `NFMRK2`, Debug `.pdb` magic = `Microsoft C/C++ MSF`.

**Extension-side** — **migrated onto the extension's `move-to-sdk`** from the POC
(archived at tag `poc-sdk-style-archive`), without `[BP-DIAG]` and without the POC's diagnostics:

| # | Change | File | Status |
|---|---|---|---|
| B1 | Deploy pre-check relaxed to a **checksum** match (firmware native-version label is cosmetic) | `DeployProvider/DeployProvider.cs` | ✅ `746b408` |
| B2 | Deploy follows the **Device Explorer SelectedDevice**; engine binding uses the chosen device's port | `DeployProvider.cs`, `DebugLauncher/Ad7CorDebugEngineBinding.cs` | ✅ `746b408` / `b897f7b` |
| B3 | **No `[BP-DIAG]` diagnostics** carried over | `CorDebug/{PdbxFile,CorDebugBreakpoint,CorDebugFunction,CorDebugCode}.cs` | ✅ (verified absent) |
| WS3 | **Engine-binding seam** — `INanoDebugEngineBinding` + `Ad7CorDebugEngineBinding` (today) + `ConcordEngineBinding` (future stub); `NanoDebuggerLaunchProvider` resolves the active binding (`NANOFRAMEWORK_DEBUG_ENGINE`, default AD7) | `DebugLauncher/{INanoDebugEngineBinding,Ad7CorDebugEngineBinding,ConcordEngineBinding,NanoDebuggerLaunchProvider}.cs` | ✅ `b897f7b` |
| B4 | *(future)* per-device Run dropdown | see [DEVICE-RUN-DROPDOWN.md](poc-findings/DEVICE-RUN-DROPDOWN.md) | ⏳ design only |

## Order of execution

1. ✅ **A1–A3** in `nanoFramework.NET.Sdk` (`move-to-sdk`) — the debugging enablers
   (`c89402d`). Small, additive, proven.
2. ✅ **Validate**: packed the SDK; `test/SmokeTest` built under VS MSBuild → PE `NFMRK2`
   + Windows/full `.pdb`. (The full on-hardware F5/breakpoint run was the POC's WS4.)
3. ✅ **A4** (MDP version) — decision made to **target v2**; `4.0.0-preview.94` + net8.0 (`ecb7c02`).
4. ✅ **B1–B3 + WS3** in the extension — migrated clean off the POC (`746b408`, `b897f7b`).
5. ⏳ **Open the upstream PRs** (held for review — see PR strategy).

## Validation gates

- `dotnet build` an SDK-style lib + app against the packed `nanoFramework.NET.Sdk` →
  `.pe` + **Windows** `.pdbx`/`.pdb`.
- Debug build's `.pdb` magic = `Microsoft C/C++ MSF` (not `BSJB`) under VS MSBuild.
- On hardware: F5 deploys, a source breakpoint **binds + hits** (not method-entry).

## PR strategy (next step — branches ready, PRs held for review)

> Open every PR from the org pull-request template — see [PR-INSTRUCTIONS.md](PR-INSTRUCTIONS.md)
> (also the contract the fleet upgrader uses for auto-created PRs).

- **SDK:** PR `danielmeza:move-to-sdk → nanoframework:move-to-sdk`. Title
  e.g. *"Enable VS debugging for SDK-style projects (full PDB, F5 wiring)"*; link
  `Home#1784`, the POC `RESULTS.md`/`DEBUGGING-LOG.md`, and the demo (https://youtu.be/9qvXsgXCrjM).
- **Extension:** PR `danielmeza:move-to-sdk → nanoframework:develop`; scope to B1–B2 + the
  WS3 engine-binding seam. Already clean of `[BP-DIAG]`.

## Open questions for maintainers

- ~~MDP version line (A4): v1 (`NFMRK1`) or v2 (`NFMRK2`)?~~ **Resolved: targeting v2**
  (`4.0.0-preview.94`, emits `NFMRK2`).
- ~~Is `DebugType=full` (Windows-PDB-under-VS) acceptable?~~ **Shipped** in A1; a
  portable→Windows (Pdb2Pdb) path for the `dotnet`-CLI debug story remains a possible
  follow-up but isn't needed for the VS flow.
- Package naming already settled: `nanoFramework.NET.Sdk`.
