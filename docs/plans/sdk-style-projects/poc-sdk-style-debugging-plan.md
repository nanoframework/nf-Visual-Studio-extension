# POC Plan — Unblock SDK-style debugging (A+C first), engine-swap ready

> **Executed — hypothesis CONFIRMED on real hardware. ✅** WS1/WS2/WS3 are
> proven/authored and **WS4 (the gate) is met**: an SDK-style app deploys via F5 and
> **source breakpoints bind and hit** on a physical ESP32_S3_OCTAL. See
> [poc-findings/RESULTS.md](poc-findings/RESULTS.md) and the full decision
> record (every blocker hit + fix, §1–§6) in
> [poc-findings/DEBUGGING-LOG.md](poc-findings/DEBUGGING-LOG.md). VS Code
> impact: [vscode-extension-impact.md](vscode-extension-impact.md).

## Objective

Prove that an **SDK-style** nanoFramework project can build, load in Visual
Studio via CPS, deploy via F5, and **debug with the existing AD7 engine** — by
authoring a minimal `nanoFramework.Sdk` that composes over `Microsoft.NET.Sdk`
and injecting the `NanoCSharpProject` capability, **without** rewriting the debug
engine. Build the work behind a thin **engine-binding abstraction** so the engine
can later be swapped to Concord with no change to the launch/deploy/project-system
layers.

## Hypothesis under test

From the code assessment of `nf-Visual-Studio-extension` (`develop`):

- The VS project system is **already CPS** (`NanoCSharpProject{Unconfigured,Configured}.cs`;
  `<ProjectCapability Include="CPS" />` in `NFProjectSystem.targets`).
- Deploy (`DeployProvider : IDeployProvider`) and debug-launch
  (`NanoDebuggerLaunchProvider : DebugLaunchProviderBase`) are **CPS providers**
  keyed off the `NanoCSharpProject` capability, and the engine is **launched by
  GUID** (`LaunchDebugEngineGuid = CorDebug.EngineGuid`) — neither inspects the
  project-file format.

**H:** the concrete gate to SDK-style is (1) build-targets composition (the nano
targets import the legacy MSBuild chain and collide with `Microsoft.NET.Sdk` —
discussion #1635) and (2) project-type registration / capability injection. The
**AD7 engine is orthogonal** and should attach to an SDK-style CPS project once it
carries the capability. If H holds, A+C unblocks debugging and the AD7→Concord
rewrite is deferred modernization, not the unlock.

The POC's job is to **confirm or refute H** with a running breakpoint — or a
precise failure point.

## Scope

**In:** a minimal `nanoFramework.Sdk`; capability injection onto an SDK-style
project; one sample app; F5 deploy + a breakpoint that binds and hits; the
engine-binding abstraction with a working AD7 implementation and a compiling
Concord stub.

**Out (this POC):** fleet migration, pack-pipeline polish, native/OTA, and the
actual Concord engine implementation (we build the *seam* for it, not the engine).

---

## Workstreams

### WS1 — Minimal `nanoFramework.Sdk` (targets composition) — solves (1)
- Author `Sdk/Sdk.props` + `Sdk/Sdk.targets` that import `Microsoft.NET.Sdk`'s
  props/targets and **own the import chain**, so the
  `Microsoft.CSharp.CurrentVersion.targets` double-import from #1635 cannot occur.
- Re-host the MDP step (`NFProjectSystem.MDP.targets` →
  `GenerateBinaryOutputTask`) as a target ordered `AfterTargets="CoreCompile"`,
  emitting `.pe`/`.pdbx`/checksum.
- Set the `netnano1.0` moniker properties.
- **Exit:** a ~6-line SDK-style `.csproj` runs `dotnet build` clean and produces a
  `.pe` byte-identical to the legacy `.nfproj` for the sample.

### WS2 — CPS capability injection (registration) — solves (2)
- Bring the `NanoCSharpProject` capability (today declared as a `ProjectCapability`
  item in `NFProjectSystem.targets`) into the SDK targets so it is present on an
  SDK-style project, **without** owning the project type via the legacy
  `ProjectTypeRegistration`/`11A8DD76` GUID (`NanoFrameworkPackage.cs`).
- Ensure VS associates the project with the nano CPS pieces via the capability,
  so `DeployProvider` and `NanoDebuggerLaunchProvider` (`[AppliesTo("NanoCSharpProject")]`)
  activate.
- **Exit:** VS opens the SDK-style project; the nano Deploy and Debug providers
  are instantiated (capability present); Device Explorer targets it.

### WS3 — Engine-binding abstraction (the Concord seam) — independent of WS1/WS2
- Introduce `INanoDebugEngineBinding` (below). Move the three hard-coded values in
  `NanoDebuggerLaunchProvider.QueryDebugTargetsAsync` (`Executable`,
  `PortSupplierGuid`, `LaunchDebugEngineGuid`) behind it.
- Provide `Ad7CorDebugEngineBinding` (today's behavior) and a compiling
  `ConcordEngineBinding` stub (throws `NotImplementedException` but proves the
  swap point and MEF wiring).
- Keep the **`nf-debugger` wire-protocol client shared** by both bindings — it's
  device communication, not a VS debugger API, and is engine-agnostic.
- **Exit:** the launcher has **zero** direct references to `CorDebug.EngineGuid` /
  `DebugPortSupplier.PortSupplierGuid` / `CorDebugProcess`; the AD7 binding is
  selected by config; the Concord stub compiles and is selectable.

### WS4 — End-to-end validation (the gate) — ✅ PASSED
- Author one sample SDK-style app; F5; set a breakpoint; confirm bind + hit +
  step + locals. Compare to the legacy `.nfproj`.
- **Exit (pass):** breakpoint binds and hits on the SDK-style project with the AD7
  binding → **H confirmed**, A+C is the unblock.
- **Exit (fail):** document the exact attach/notify failure point → **H refuted**
  at that seam; WS3 means the next step (Concord engine) is scoped without redoing
  WS1/WS2.
- **RESULT — pass ✅:** on a real ESP32_S3_OCTAL, the SDK-style `Blink` deploys via F5
  and a source breakpoint **binds and hits** with the AD7 binding unchanged → **H
  confirmed**. Four fixes were required en route — F5-console (LaunchProfiles removed +
  `DebuggerFlavor`), deploy version mismatch (checksum-only pre-check), breakpoints
  (Debug must emit a **Windows/full** PDB), and dev-only legacy `.nfproj` load — see
  [poc-findings/DEBUGGING-LOG.md](poc-findings/DEBUGGING-LOG.md) §3–§6.

---

## The abstraction layer

A single seam decouples *which VS debugger API* is used from *how nano launches and
talks to the device*. Today's launcher hard-codes the AD7 engine; the seam makes
that one of two interchangeable implementations.

```csharp
// New: vs-extension.shared/DebugLauncher/INanoDebugEngineBinding.cs
internal interface INanoDebugEngineBinding
{
    // Identity VS uses to select the engine + its transport.
    Guid EngineGuid { get; }
    Guid PortSupplierGuid { get; }

    // Build the launch settings for THIS engine from nano-level inputs.
    // peFilesToDeploy/Load are produced by the shared ReferenceCrawler.
    DebugLaunchSettings CreateLaunchSettings(
        DebugLaunchOptions options,
        NanoDeviceBase device,
        IReadOnlyList<string> peFilesToLoad,
        IVsHierarchy project);
}
```

`NanoDebuggerLaunchProvider.QueryDebugTargetsAsync` then becomes
engine-agnostic:

```csharp
[Import] INanoDebugEngineBinding EngineBinding { get; set; }   // MEF-selected

// ...after device connect + crawling the PE list...
var settings = EngineBinding.CreateLaunchSettings(launchOptions, device, peList, VsHierarchy);
return new[] { settings };
```

Two implementations behind it:

| Concern | `Ad7CorDebugEngineBinding` (today) | `ConcordEngineBinding` (future) |
|--------|------------------------------------|---------------------------------|
| Engine identity | `CorDebug.EngineGuid` | Concord engine GUID (registered via `.vsdconfig`) |
| Port / transport | `DebugPortSupplier.PortSupplierGuid` | `DkmTransport` / custom Dkm port |
| Launch shape | `Executable = CorDebugProcess.dll`, `Arguments = /waitfordebugger /load:<pe>…` | Concord launch (engine GUID; in-proc components, no shell-out exe) |
| Execution control / breakpoints / stepping | AD7 `IDebug*` (`CorDebug/*`, `ManagedCallbacks.cs`) | Dkm components (`IDkm*`) — model on the **Iris** sample |
| Symbol mapping | `Pdbx.cs` / `PdbxFile.cs` | Concord symbol provider (`IDkmSymbolQuery`/module load) |
| **Device comms (shared)** | `nf-debugger` wire protocol | **same** `nf-debugger` wire protocol |

The key design point: the **wire-protocol client is shared and untouched** across
the swap. A Concord migration re-implements only the VS-facing execution-control
and symbol surface, wiring it to the same device client — which is why isolating
the binding now makes that a contained, schedulable workstream rather than a
cross-cutting rewrite.

---

## Sequencing & decision gate

```
WS1 (SDK targets) ─┐
                   ├─► WS2 (capability injection) ─► WS4 (F5 + breakpoint) ─► GATE
WS3 (engine seam) ─┘   (parallel; independent)
```

**GATE RESULT: ✅ PASSED** — AD7 attached to the SDK-style CPS project and breakpoints
hit on real hardware. Concord is therefore a separate, lower-priority modernization item.

- **GATE passes** → ship SDK-style build/pack/test + AD7 debugging; Concord becomes
  a separate, lower-priority modernization item.
- **GATE fails at engine attach** → the seam (WS3) is already in place; scope the
  Concord `Iris`-style engine as the next workstream; WS1/WS2 stand.

## Risks

| Risk | Mitigation |
|------|-----------|
| **Load-bearing assumption**: AD7 may not attach to an SDK-style CPS project | **SETTLED ✅** — WS4 confirmed AD7 attaches and breakpoints hit on hardware; the seam stays for a future Concord move |
| Capability injection subtleties (SDK project type vs nano capability) | Keep the capability/targets approach; avoid re-owning the project-type GUID; test VS load early |
| MDP re-host parity (checksum/stubs/`.pe`) | WS1 byte-identical `.pe` exit gate against legacy |
| Future VS deprecates AD7 hosting | The seam means a forced Concord move doesn't touch WS1/WS2 or the device client |

## References

- Concord overview & architecture: https://github.com/microsoft/ConcordExtensibilitySamples/wiki/Overview
- **Iris** sample (full custom-runtime engine — the model for `ConcordEngineBinding`): https://github.com/microsoft/ConcordExtensibilitySamples/tree/main/Iris
- Hello World sample: https://github.com/microsoft/ConcordExtensibilitySamples/wiki/Hello-World-Sample
- CPS `DebugLaunchProviderBase` / debuggers: https://github.com/microsoft/VSProjectSystem/blob/master/doc/overview/debuggers.md
- Authoring an MSBuild project SDK: https://learn.microsoft.com/en-us/visualstudio/msbuild/how-to-use-project-sdk
- Discussion #1635 (the blocker thread): https://github.com/orgs/nanoframework/discussions/1635
- Deeper read-only diagnosis to run locally first: [debugger-blocker-diagnosis-prompt.md](debugger-blocker-diagnosis-prompt.md)
- Executed POC results, WS4 runbook & decision gate: [poc-findings/RESULTS.md](poc-findings/RESULTS.md)
- VS Code extension migration impact: [vscode-extension-impact.md](vscode-extension-impact.md)
- Related specs: [02-sdk-design.md](02-sdk-design.md), [04-mdp-native-integration.md](04-mdp-native-integration.md), [06-ide-integration.md](06-ide-integration.md), [09-implementation-strategy.md](09-implementation-strategy.md)
