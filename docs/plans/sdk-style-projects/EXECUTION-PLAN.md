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

> **Tooling & samples migration (2026-06-16): migration tooling hardened, Samples migrated,
> tools reorganized, docs shipped.** Building on the enablers above, the migration toolchain
> and the full Samples conversion are now complete on `move-to-sdk`. The three upstream PRs
> remain **drafts** held for review (no change to PR status). Detail in
> [phase-1-execution.md](phase-1-execution.md); the as-built tool docs are the SDK-repo READMEs.
>
> | Area | What landed | Where (`danielmeza/*` `move-to-sdk`) |
> |---|---|---|
> | **NanoMigrate engine** | Core reorganized into vertical slices; `.sln`/`.slnx` via official `Microsoft.VisualStudio.SolutionPersistence`; CPM support; packages.config/HintPath resolution; idempotent + reentrant | `nanoFramework.Sdk` @ `d72da89`, `ceab679` |
> | **Migrate features** | `--report` (Markdown/HTML); rollback journal (`.nanomigrate/`) + `rollback`/`clean` commands; post-migration `--verify`; `--no-backup` fully suppresses loose `.bak` | `nanoFramework.Sdk` @ `48d5817`, `b7136f2` |
> | **`dotnet nano` umbrella** | new tool: built-in `migrate`/`clean`/`rollback` + external `flash` (nanoff); `deploy`/`monitor`/`devices` placeholders | `nanoFramework.Sdk` @ `4a1883f` |
> | **Repo reorg** | tools moved out of `src/`: `tools/nano` (umbrella) + `tools/migrate` (converter); 108 migrate + 8 umbrella tests pass | `nanoFramework.Sdk` @ `4a1883f`, `967078f` |
> | **Tool docs** | `tools/nano/README.md` + `tools/migrate/README.md` (as-built, verified vs `--help`); migration skill expanded; plan docs redirect to the READMEs | `nanoFramework.Sdk` @ `7c05b4a` |
> | **Samples** | whole repo migrated via `dotnet nano migrate`: 153 projects, 111 solutions, 0 review flags; SDK now defines `NANOFRAMEWORK_1_0` for source compat | `Samples` (PR #463), `nanoFramework.Sdk` @ `58b5afd` |
> | **PR rule** | no AI/tool attribution in PR text (org-template addendum) | both repos |

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
5. ✅ **Upstream PRs opened as drafts** — SDK [#2](https://github.com/nanoframework/nanoFramework.Sdk/pull/2),
   extension [#929](https://github.com/nanoframework/nf-Visual-Studio-extension/pull/929), samples
   [#463](https://github.com/nanoframework/Samples/pull/463); cross-referenced, linked to `Home#1784`.
   Held as drafts for maintainer review before marking ready.

## Spec coverage (docs 00–10) — C-components + the developer loop

This effort grew past what the POC-landing tracker above captures. The matrix below maps every
concrete deliverable from the spec set (doc 10's C1–C11 build-list, doc 05's developer loop, doc 04's
checksum gate, doc 07's fleet automation) to its real status, so nothing falls between this plan and
[phase-1-execution.md](phase-1-execution.md). Legend: ✅ done · 🔶 tracked (planned/deferred, not
done) · ⛔ missing (was not built **and** not tracked — now filed as child issues
[Home#1787–#1793](https://github.com/nanoframework/Home/issues/1784#issuecomment-4724148120) under
epic Home#1784; index in [sdk-migration-backlog-issues.md](sdk-migration-backlog-issues.md)).

### SDK build pipeline
| Item (doc§) | Status | Note |
|---|---|---|
| C1 `nanoFramework.NET.Sdk` package (10 §10.1) | ✅ | packs to `artifacts/` |
| C2 `netnano1.0` TFM moniker props (02 §2.2, C2) | ✅ | `nanoFramework.Tfm.props` |
| C3 `GenerateNanoBinary` / MDP re-host, incremental (04 §4.2–4.3, C3) | ✅ | `nanoFramework.Mdp.targets`, MDP 4.x/NFMRK2 |
| **C4 `NanoChecksumCheck` / `NanoValidateChecksum` build-time ABI gate (04 §4.5, 10 §10.3)** | ⛔ | opt-in gate; **listed in the doc-10 MVS Phase 1** but not in `Sdk.targets`. Not B1 (B1 is extension-side deploy). Filed [Home#1787](https://github.com/nanoframework/Home/issues/1787). |
| C10 `nanoFramework.Sdk.Corlib` variant (02 §2.6, C10) | 🔶 | maintainer-owned; `CoreLibrary.Sdk.csproj` exists, not primary |
| C11 workload manifest (02 §2.3, C11) | 🔶 | explicitly "later" |

### Developer loop (doc 05) + CLI (C6)
| Item (doc§) | Status | Note |
|---|---|---|
| C6 `dotnet nano` umbrella + `migrate`/`flash` (05, C6) | ✅ | shipped |
| **C5 `NanoDeploy` task + `Deploy` MSBuild target (05 §5.3 Path A, C5)** | ⛔ | no SDK `Deploy` target. Filed [Home#1788](https://github.com/nanoframework/Home/issues/1788). |
| **`dotnet nano deploy` / `monitor` / `devices` (05 §5.3)** | ⛔ | shipped as not-implemented placeholders only. Filed [Home#1788](https://github.com/nanoframework/Home/issues/1788). |
| **`dotnet watch` / `dotnet nano watch` hot loop (05 §5.4)** | ⛔ | not built. Filed [Home#1789](https://github.com/nanoframework/Home/issues/1789). |
| **Device-selection order + `nanoFramework.config.json` (05 §5.5)** | ⛔ | not built. Filed [Home#1790](https://github.com/nanoframework/Home/issues/1790). |
| **CI fast-fail on missing device (05 §5.6)** | ⛔ | not built. Filed [Home#1791](https://github.com/nanoframework/Home/issues/1791). |

### Tooling, fleet & templates
| Item (doc§) | Status | Note |
|---|---|---|
| C8 NanoMigrate converter (07, C8) | ✅ | `migrate`/`clean`/`rollback`/`clone`/`fleet`, 108 tests |
| **C8 (other half) CI / Azure-pipeline template rewriter (07 §7.6)** | ⛔ | converter done; the per-repo CI rewrite is not built. Filed [Home#1792](https://github.com/nanoframework/Home/issues/1792). |
| **Fleet auto-PR renderer (PR-INSTRUCTIONS contract)** | ⛔ | contract documented; reference renderer not built. Filed [Home#1793](https://github.com/nanoframework/Home/issues/1793). |
| C7 `dotnet new nanoapp`/`nanolib` templates (10 §10.4, C7) | 🔶 | in MVS; phase-1 "next step", not built |

### IDE
| Item (doc§) | Status | Note |
|---|---|---|
| C9 VS CPS capability + XAML rules (06, C9) | ✅ | in extension PR #929 (productized POC) |
| VS Code → detect SDK-style `.csproj` + `dotnet build` (06 §6.4) | 🔶 | phase-1 "next step", not started |

### Phase status (doc 09 §9.3)
Phase 0 ✅ · Phase 1 🔶 (C4/C7 + VS Code + a real ~5-repo `lib-*` pilot still open — Samples is a proxy,
not a `lib-*`) · Phase 2 ✅ (debugger gate passed on hardware) · Phase 3 🔶 (B1–B3+WS3 drafted in #929,
not merged) · Phase 4 ⛔ (fleet not started). **Net: the developer inner-loop (05 §5.3–5.6), the C4
gate, and fleet automation (07 §7.6 + the auto-PR renderer) are the spec areas this plan had not been
tracking; they are now captured here and filed as Home#1787–#1793 under epic Home#1784.**

## Validation gates

- `dotnet build` an SDK-style lib + app against the packed `nanoFramework.NET.Sdk` →
  `.pe` + **Windows** `.pdbx`/`.pdb`.
- Debug build's `.pdb` magic = `Microsoft C/C++ MSF` (not `BSJB`) under VS MSBuild.
- On hardware: F5 deploys, a source breakpoint **binds + hits** (not method-entry).

## PR strategy (3 drafts open — opened from the org template, see [PR-INSTRUCTIONS.md](PR-INSTRUCTIONS.md))

All three are **draft**, cross-referenced, and linked to `Home#1784`; mark ready after review
(`gh pr ready <n> --repo <owner/repo>`).

- **SDK** [nanoframework/nanoFramework.Sdk#2](https://github.com/nanoframework/nanoFramework.Sdk/pull/2)
  (`danielmeza:move-to-sdk → move-to-sdk`): full PDB + F5 wiring + v2/NFMRK2.
- **Extension** [nanoframework/nf-Visual-Studio-extension#929](https://github.com/nanoframework/nf-Visual-Studio-extension/pull/929)
  (`danielmeza:move-to-sdk → develop`): B1–B2 + the WS3 engine-binding seam.
- **Samples** [nanoframework/Samples#463](https://github.com/nanoframework/Samples/pull/463)
  (`danielmeza:move-to-sdk → main`): all samples migrated to SDK-style. **Depends on the SDK
  publishing** — the dev-local `global.json`/`NuGet.Config` local feed must be swapped for the
  published SDK before merge.

### Order to land
1. SDK#2 first (everything consumes the SDK), then publish it.
2. Extension#929 (productizes VS deploy/debug for SDK-style).
3. Samples#463 once the SDK is on nuget.org (drop the local feed, pin the published version).

## Open questions for maintainers

- ~~MDP version line (A4): v1 (`NFMRK1`) or v2 (`NFMRK2`)?~~ **Resolved: targeting v2**
  (`4.0.0-preview.94`, emits `NFMRK2`).
- ~~Is `DebugType=full` (Windows-PDB-under-VS) acceptable?~~ **Shipped** in A1; a
  portable→Windows (Pdb2Pdb) path for the `dotnet`-CLI debug story remains a possible
  follow-up but isn't needed for the VS flow.
- Package naming already settled: `nanoFramework.NET.Sdk`.
