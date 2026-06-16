# POC Results — SDK-style nanoFramework build + debugging seam

Executed against `nf-Visual-Studio-extension` @ `develop`, on macOS (darwin) with
.NET SDK 10.0.300, the `nanoFramework.Tools.MetadataProcessor.MsBuildTask` 3.0.100
and `nanoFramework.CoreLibrary` **2.0.0-preview.52** (v2 preview). Reproduce with
[`build-and-verify.sh`](https://github.com/danielmeza/nf-Visual-Studio-extension/blob/b8c2edeb1ff775e3f78ba74af9ed384d1ee5c333/poc-sdk-style/build-and-verify.sh)
(the standalone POC harness, kept on `poc/sdk-style-debugging`).

> **v2 preview note:** the v2 CoreLibrary is **republished against `netnano1.0`**
> (ships `lib/netnano1.0/`), so it restores **natively** — no NU1202, no
> `AssetTargetFallback`, no NU1701 warning. The SDK keeps the fallback as a
> harmless bridge for any not-yet-republished (v1-era) packages in a graph.

> **UPDATE — gate CLEARED on real hardware ✅.** The WS4 engine-attach half (left
> open on macOS below) was since validated on **Windows + Visual Studio against a
> physical ESP32_S3_OCTAL**: the SDK-style `Blink` deploys via F5 and **source
> breakpoints bind and hit**, AD7 engine unchanged. Four issues surfaced and were
> fixed along the way — F5-console (LaunchProfiles removed + `DebuggerFlavor`), deploy
> version mismatch (→ checksum-only pre-check), breakpoints (→ Windows/full PDB, not
> portable), and a dev-only legacy `.nfproj` load. Full decision record:
> [DEBUGGING-LOG.md](DEBUGGING-LOG.md) §3–§6; multi-device design:
> [DEVICE-RUN-DROPDOWN.md](DEVICE-RUN-DROPDOWN.md). The macOS build-side results below
> remain accurate.

## TL;DR

The hypothesis's **build-targets-composition half is confirmed**: a minimal,
reusable `nanoFramework.Sdk` that composes over `Microsoft.NET.Sdk` builds an
SDK-style `.csproj` clean and emits a byte-correct nanoFramework `.pe` + `.pdbx`
on a plain machine, no VS. The **engine-attach half (the actual breakpoint) was not
decidable on macOS** — it needs Visual Studio on Windows — and has since been
**confirmed on real hardware** (see the banner above): F5 + breakpoints work on the
SDK-style project with the AD7 engine unchanged. WS3's engine seam stays in place for
a future Concord swap, but was **not** needed for the unlock.

| WS | What | Status here |
|----|------|-------------|
| WS1 | Minimal `nanoFramework.Sdk` (targets composition + MDP re-host) | ✅ **Proven** — builds, emits `.pe`/`.pdbx` |
| WS1 gate | PE parity vs. legacy MDP | ✅ **Byte-identical** (same IL → same PE) |
| WS2 | `NanoCSharpProject` CPS capability injection | ✅ **Proven** — VS loads the SDK-style project via CPS and instantiates the nano deploy/debug providers (confirmed on Windows) |
| WS3 | `INanoDebugEngineBinding` seam (AD7 impl + Concord stub) | ✅ **Authored & wired**; compiles in the VS extension build (Windows). Not needed for the unlock |
| WS4 | F5 + breakpoint binds/hits | ✅ **PASSED on hardware** — F5 deploy + source breakpoints on a physical ESP32_S3_OCTAL (see banner / [DEBUGGING-LOG.md](DEBUGGING-LOG.md) §3–§6) |

---

## What the POC built (reusable artifacts)

```
poc-sdk-style/
  nanoFramework.Sdk/
    nanoFramework.Sdk.csproj        # packs the SDK package (PackageType=MSBuildSdk)
    Sdk/
      Sdk.props                     # composes over Microsoft.NET.Sdk; nano defaults
      Sdk.targets                   # imports nano targets, then Microsoft.NET.Sdk LAST
      nanoFramework.Tfm.props       # netnano1.0 moniker + MSB3644/NU1202 workarounds
      nanoFramework.Mdp.targets     # NanoEmitPe: MDP task AfterTargets=CoreCompile
      nanoFramework.Capabilities.targets  # injects NanoCSharpProject capability (WS2)
  samples/Blink/                    # 6-line SDK-style app: the deliverable shape
  local-feed/                       # nanoFramework.Sdk.1.0.0.nupkg (SDK resolution)
  global.json                       # msbuild-sdks pin
  nuget.config                      # local feed + nuget.org
  build-and-verify.sh               # end-to-end reproducible proof

# WS3 (lives in the real extension tree, not under poc-sdk-style/):
vs-extension.shared/DebugLauncher/
  INanoDebugEngineBinding.cs        # the seam
  Ad7CorDebugEngineBinding.cs       # today's engine, extracted faithfully
  ConcordEngineBinding.cs           # compiling, config-selectable stub
  NanoDebuggerLaunchProvider.cs     # refactored: zero hard-coded engine GUIDs
vs-extension.shared/vs-extension.shared.projitems   # +3 Compile entries
```

The deliverable a user writes (`samples/Blink/Blink.csproj`):

```xml
<Project Sdk="nanoFramework.Sdk/1.0.0">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>netnano1.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="nanoFramework.CoreLibrary" Version="1.17.11" />
  </ItemGroup>
</Project>
```

---

## WS1 — the concrete gates we hit and solved (the real value)

These were resolved empirically, in order — each is a fact a future
implementer will hit:

1. **`netnano1.0` is recognized** by .NET SDK 10 (it derives
   `.NETnanoFramework,Version=v1.0` on its own). Not a custom TFM problem.
2. **MSB3644** — "reference assemblies for .NETnanoFramework,Version=v1.0 were
   not found." There is no targeting pack. Fixed exactly as the legacy
   `NFProjectSystem.CSharp.targets` does: `TargetingClr2Framework=true` +
   `_TargetFrameworkDirectories`/`_FullFrameworkReferenceAssemblyPaths` pointed
   at a real (dummy) folder. **It composes fine in SDK-style.**
3. **`CS0518 System.Object not defined`** — expected with `NoStdLib`; resolved
   by the `nanoFramework.CoreLibrary` PackageReference (nano's mscorlib).
4. **NU1202** — *v1-era* packages expose assets under a bare `lib/`, which NuGet
   reads as `.NETFramework,v0.0` (discussion #1635). Bridged with
   `AssetTargetFallback=net48` (+ `NoWarn=NU1701`). **Confirmed fixed in v2:** the
   v2-preview CoreLibrary ships `lib/netnano1.0/` and restores natively with no
   fallback and no NU1701 — exactly what doc 02 predicts republishing achieves.
5. **MDP re-host** — `MetaDataProcessorTask` (`Parse`→`Compile`) emits the `.pe`
   and reads the portable `.pdb` to emit `.pdbx`. Runs `AfterTargets=CoreCompile`
   with `Inputs`/`Outputs` for incrementality.

### Two ordering traps worth recording (cost real iterations)

- **TFM-conditional props must NOT live in `Sdk.props`.** At `Sdk.props`
  evaluation the project body hasn't set `<TargetFramework>` yet, so a
  `'$(TargetFramework)'=='netnano1.0'` group there silently no-ops (TFI only
  *looks* set because the SDK derives it independently). They must be imported
  from **`Sdk.targets`**, where the project body has run.
- **`$(TargetDir)`/`$(TargetName)`-derived paths must be defaulted AFTER the
  `Microsoft.NET.Sdk` `Sdk.targets` import** — they're defined by
  `Microsoft.Common.targets` (imported last). Setting them earlier yields `.pe`
  (empty dir + empty name).

### Evidence
- `Blink.pe` starts with `NFMRK1` (the nano PE magic; same as `mscorlib.pe`). *Note:
  this macOS run pinned MDP 3.0.100 = v1/`NFMRK1`; the v2 device firmware needs
  v2/`NFMRK2`, so the hardware build later moved to MDP 4.0-preview — see
  [DEBUGGING-LOG.md](DEBUGGING-LOG.md) §2.*
- `Blink.pdbx` is the nano debug DB (CLR↔nanoCLR token map, `FileName=Blink.exe`).
- Build is **deterministic** (byte-identical `.pe` across rebuilds).
- **WS1 parity gate:** feeding the *same* IL assembly to a legacy-shaped
  `MetaDataProcessorTask` invocation yields a **byte-identical** `.pe`. This
  isolates the re-host from compiler-version noise: the SDK's MDP call ≡ the
  legacy MDP call. (Full legacy-`.nfproj`-toolchain compile belongs on Windows,
  where the csc version is pinned — see WS4.)

---

## WS2 — capability injection (build-side proven)

`nanoFramework.Capabilities.targets` adds `<ProjectCapability
Include="NanoCSharpProject" />` (guarded on `.NETnanoFramework`). Verified the
item is present on the evaluated SDK-style project. That capability is what the
nano CPS providers key off (`[AppliesTo("NanoCSharpProject")]` on
`DeployProvider` and `NanoDebuggerLaunchProvider`), **without** re-owning the
legacy project-type GUID `{11A8DD76-…}`. Whether VS then *loads* the SDK-style
project through CPS and instantiates those providers is the Windows-only part of
WS4.

---

## WS3 — the engine-binding seam (Concord-ready)

`INanoDebugEngineBinding` now owns the three values the launcher used to
hard-code. `NanoDebuggerLaunchProvider.QueryDebugTargetsAsync` is engine-agnostic:
it crawls the PE list (shared `ReferenceCrawler`) and hands
`(launchOptions, device, peList, hierarchy)` to the configured binding.

- `Ad7CorDebugEngineBinding` — today's behavior, extracted verbatim (engine GUID
  `CorDebug.EngineGuid`, port `DebugPortSupplier.PortSupplierGuid`, executable
  `CorDebugProcess`, the `/waitfordebugger /load:` command line). **No behavior
  change.**
- `ConcordEngineBinding` — compiles, is exported, and is selectable by config
  (`EngineId="Concord"`); members `throw NotImplementedException` with pointers
  to the Concord **Iris** model.
- Selection: `ResolveEngineBinding()` picks by `NANOFRAMEWORK_DEBUG_ENGINE`
  (default `AD7`). This is the single point an AD7→Concord swap flips.

Verified: the launcher has **zero** direct references to `CorDebug.EngineGuid` /
`DebugPortSupplier.PortSupplierGuid` / `CorDebugProcess` (only a comment
mentions the latter). The shared `nf-debugger` wire-protocol client is untouched
and is reused by both bindings.

> Compilation note: WS3 is part of the VS extension assembly (needs the VS SDK +
> `Microsoft.VisualStudio.Debugger.Interop`, Windows-only), so it builds with the
> extension on Windows, not on this Mac. The change is additive + a localized,
> brace-balanced refactor; no other call sites touch the removed method.

---

## WS4 — VALIDATED on hardware ✅ (Layer A/B runbook retained for CI)

**RESULT:** Layer B (the literal F5 gesture) was completed on **Windows + Visual
Studio against a physical ESP32_S3_OCTAL** — the SDK-style `Blink` deploys via F5 and
source breakpoints bind and hit, AD7 engine unchanged. Reaching it required four fixes
(F5-console, deploy version mismatch → checksum pre-check, breakpoints → Windows/full
PDB, dev-only legacy `.nfproj` load) — see [DEBUGGING-LOG.md](DEBUGGING-LOG.md) §3–§6.
The Layer A/B runbook below is retained for **CI automation** of this gate.

WS4 is "set a breakpoint, F5, confirm it binds + hits + steps + locals." It can't
run on macOS. There are **two layers** to validate, and they have very different
automation costs — split them:

### Layer A (high value, automatable headless, NO Visual Studio)
Test the **engine-attach + breakpoint** at the *wire-protocol* level against a
device running the SDK-built PE:

- **Target = the `nanoclr` virtual device** (already integrated here:
  `vs-extension.shared/VirtualDeviceService`). It's a `dotnet tool`
  (`dotnet tool install -g nanoclr`) that runs a Win32 nanoCLR instance and
  exposes a **virtual serial port** (`nanoclr virtualserial --create …`). This is
  the realistic nanoFramework "emulator" — there is **no ESP32 hardware
  emulator** in the nanoFramework toolchain; the Win32 virtual device is the
  substitute, and the *same* `.pe` runs on it (no RID, checksum-gated).
- **Driver = the `nanoFramework.Tools.Debugger` library** (the shared
  wire-protocol client, cached as `nanoframework.tools.debugger.net`). A small
  console harness can: connect to the virtual COM port, deploy the SDK-built
  PE list, set a breakpoint by `(assembly, method, IL offset)` read from the
  `.pdbx`, resume, and assert the break + stack/locals.
- This runs on a **plain Windows agent (Session 0 service is fine — no UI)**,
  and arguably even validates the hypothesis's debugging claim more directly
  than the IDE gesture, because it exercises the engine↔device path the AD7 and
  future Concord bindings both sit on.

### Layer B (the literal F5 gesture, needs real VS + interactive desktop)
- VS is **Windows-only and not headless.** Driving an actual F5 session requires
  an **interactive Windows session** (the debugger needs a window station /
  desktop). Microsoft-hosted Azure DevOps Windows agents run as a service in
  Session 0 → they generally **cannot** drive the VS debugger UI.
- So Layer B needs a **self-hosted Windows agent or an Azure Windows VM with
  autologon** into an interactive session.
- Automate VS via **DTE/EnvDTE** (`dte.Solution.Open`,
  `dte.Debugger.Breakpoints.Add`, `dte.Debugger.Go(false)`, wait for
  `dbgModeBreak`, read `dte.Debugger.CurrentStackFrame` / locals, then `Stop`),
  or the VS SDK integration-test harness (**Apex**). Target = `nanoclr` virtual
  device or a physical board attached to the agent.

### Sketch: Azure Pipeline shape
```yaml
# Build everything on a hosted Windows agent (cheap, no UI needed)
- stage: Build            # windows-latest
  - build nanoFramework.Sdk -> push to an internal feed
  - build the VS extension VSIX (msbuild)

# Layer A — headless protocol test (hosted Windows agent OK)
- stage: DebugProtocolTest    # windows-latest, Session 0 fine
  - dotnet tool install -g nanoclr
  - nanoclr virtualserial --create COM_A:COM_B
  - start nanoclr instance on COM_A
  - run the console harness (nanoFramework.Tools.Debugger):
      deploy SDK-built .pe over COM_B, set bp from .pdbx, resume, assert hit

# Layer B — real VS F5 (SELF-HOSTED Windows agent w/ autologon, or skip in CI)
- stage: VsF5Test         # self-hosted, interactive desktop
  - install VS 2022 + the built VSIX + the SDK feed
  - DTE/Apex harness: open Blink.sln, set bp, F5 against nanoclr, assert break
```

### Layer A′ — VS Code's cross-platform debugger (runs on the Mac!)
The VS Code extension ships a **cross-platform** debug adapter
(`bin/nanoDebugBridge/.../darwin-arm64/nanoFramework.Tools.DebugBridge`) that does
source-level debugging over the same wire protocol, independent of project format.
So **SDK-style debugging can be validated from VS Code on macOS** — `dotnet build`
the SDK project, deploy to `nanoclr` or a board, set a breakpoint via the
`nanoframework` debug type — no Windows, no VS. This is the productized sibling of
the Layer A harness and the most attractive near-term debug-validation surface.
See [vscode-extension-impact.md](../vscode-extension-impact.md).

### Your Mac's role
The Mac **orchestrates** (push, `az pipelines run`, read results), runs the
**build-side POC** (this folder), and — via VS Code's cross-platform bridge (Layer
A′) — can even drive a debug session. It cannot host **Visual Studio** or the Win32
`nanoclr` `virtualserial` device; the VS AD7 F5 path (Layer B) and the Windows
`nanoclr` virtual serial run on the Windows agent(s).

---

## Decision gate

**RESULT: ✅ PASSED via Layer B (real VS F5) on hardware.** AD7 attached to the
SDK-style CPS project and breakpoints hit, engine unchanged → the engine is orthogonal
to project format (hypothesis confirmed). Ship WS1+WS2 (+AD7 binding); Concord is
deferred modernization. The branches below are retained as the original decision logic.

- **If Layer A binds+hits the breakpoint on the SDK-built PE** → the engine is
  orthogonal to project format (hypothesis confirmed at the protocol level);
  ship WS1+WS2 (+AD7 binding) and treat Concord as deferred modernization.
- **If Layer B (real VS F5) fails to load/attach** while Layer A passes → the
  gap is CPS load / capability wiring (WS2 in the IDE), not the engine; scope is
  the IDE registration, not a debugger rewrite.
- **If the engine itself can't attach** → WS3's seam means the Concord
  (Iris-style) engine is the next contained workstream; WS1/WS2 stand and the
  device wire client is reused.
```
WS1 (proven) ─┬─► WS2 (build-side proven; VS-load = Layer B)
WS3 (seam in) ─┘     └─► WS4: Layer A (headless, automatable) ─► Layer B (VS F5) ─► GATE
```
