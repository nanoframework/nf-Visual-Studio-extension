# Findings — per-device selector in the Run dropdown

Goal: when more than one nanoFramework device is connected, let the user pick the
**target device** from the VS Run/Start toolbar dropdown (today it only uses the
single globally-selected device from the Device Explorer tool window). This is an
**extension** feature (benefits both legacy `.nfproj` and SDK-style `.csproj`).

## Decision: mirror MAUI — `IVsProjectCfgDebugTargetSelection` (was "Mechanism 3")

The earlier recommendation (CPS dynamic **launch profiles**, "Mechanism 1") is
**rejected** — it has a fatal flaw for this extension:

- The extension deliberately **removes** the `LaunchProfiles` capability and routes
  F5 through the **DebuggerFlavor** model (`[ExportDebugger("NanoDebugger")]`,
  `DebuggerFlavor=NanoDebugger`). The comment at `NFProjectSystem.CSharp.targets:28`
  is explicit: remove it *"otherwise the C# project system provides its
  ProjectDebuggerProvider"* — the console launcher that hijacked Run (the bug we
  fixed). Re-enabling `LaunchProfiles` to get the native profile dropdown
  re-introduces that, unless paired with a custom `CommandName` + our own
  `IDebugProfileLaunchTargetsProvider` + suppression of the default `Project`
  profile. Too much surface area, and launch profiles render a **flat** list (no
  device/emulator grouping).

**How MAUI does it (the model the user pointed to):** the grouped device/emulator
picker in the VS toolbar is the **debug-target menu controller**, driven by
`IVsProjectCfgDebugTargetSelection` (`Microsoft.VisualStudio.Shell.Interop`,
in `Microsoft.VisualStudio.Interop.dll`). MAUI is itself an SDK-style/CPS project, so
this **proves the grouped dropdown is achievable on CPS** — and it coexists with the
project's debugger, so our working DebuggerFlavor F5 stays intact.

### The `IVsProjectCfgDebugTargetSelection` contract (verified on Learn)
Implemented on the project's **configuration** object; VS gets it via `QueryInterface`
from the config's `IVsDebuggableProjectCfg`.

- `bool HasDebugTargets(IVsDebugTargetSelectionService svc, out Array supportedTargetCommandIDs)`
  — returns the supported **target *types*** as `"<Guid>:<Id>"` pairs (each pair is a
  group, e.g. MAUI's "Android Emulators" vs "Physical Devices"). `false` = none.
- `Array GetDebugTargetListOfType(Guid targetType, uint targetTypeId)` — the **instances**
  of a type (the actual device names). This is the `DynamicItemStart` expansion.
- `void GetCurrentDebugTarget(out Guid, out uint, out string)` — the latched selection
  (the icon/text shown on the toolbar).
- `void SetCurrentDebugTarget(Guid, uint, string)` — VS calls this when the user picks.
- Refresh on device connect/disconnect: call
  `IVsDebugTargetSelectionService.UpdateDebugTargets()` (QueryService
  `SVsDebugTargetSelectionService`) — VS re-pulls `HasDebugTargets`/`GetDebugTargetListOfType`
  at next idle. Wire it to the same messages the Device Explorer already handles
  (`NanoDevicesCollectionHasChangedMessage` / `…EnumerationCompleted` / `…HasDeparted`).

### `.vsct` requirement
The target-type command(s) are declared in a `.vsct`, **owned by the debug-target
handler package**, parented to `DebugTargetMenuControllerGroup`
(`guidDebugTargetHandlerCmdSet = {6E87CFAD-6C05-4adf-9CD7-3B7943875B7C}`,
`DebugTargetMenuControllerGroup = 0x1000`), with command flags
`DynamicItemStart | DynamicVisibility | TextChanges | DefaultInvisible | DefaultDisabled`.
nanoFramework needs **one** type — `"nanoFramework Device"` — since there are no
emulators; the flat list is every connected device (the nano equivalent of MAUI's
picker). The `"<Guid>:<Id>"` pair returned by `HasDebugTargets` must match this
command's CommandID.

### nano simplification vs MAUI
- One target **type** ("nanoFramework Device"), so `HasDebugTargets` returns a single
  pair; the list = `DeviceExplorerViewModel.AvailableDevices` (`device.Description`,
  stable id `device.DeviceUniqueId`/`ConnectionId`).
- `SetCurrentDebugTarget` → set `DeviceExplorerViewModel.SelectedDevice` (the existing
  selection state already drives deploy + debug; see below). `GetCurrentDebugTarget` →
  read it back. This keeps the toolbar and the tool window in sync for free.

## The hard part (needs in-VS iteration; MAUI's glue is closed-source)
Exposing `IVsProjectCfgDebugTargetSelection` from a **CPS** project's config object is
the unverified piece — CPS owns `IVsDebuggableProjectCfg`, so the interface must be
aggregated onto it (not a documented public seam; MAUI's implementation is not OSS).
This is COM, VS-only, and cannot be validated headlessly. Recommend scaffolding it
behind the existing `NanoCSharpProject` capability and iterating live in VS.

## Consumer wiring (mechanism-independent — done / safe regardless of the dropdown)
The chosen device must drive **all three** of run/deploy/debug. State of the seams:
1. ✅ `DebugLauncher/NanoDebuggerLaunchProvider.cs` (`QueryDebugTargetsAsync`) — already
   resolves from `DeviceExplorerViewModel.SelectedDevice`.
2. ✅ `DebugLauncher/Ad7CorDebugEngineBinding.cs:67` — fixed: `PortName = device.Description`
   (the passed-in chosen device, not the global `NanoDeviceCommService.Device`).
3. ✅ `DeployProvider/DeployProvider.cs:125` — fixed: now
   `device = deviceExplorer.SelectedDevice ?? NanoDeviceCommService.Device`, so deploy
   targets the same device the launcher does. (Previously it validated `SelectedDevice`
   but deployed to the global device — a latent >1-device bug.)

**No change downstream:** `CorDebug/DebugPort.cs` `RefreshProcesses` enumerates every
connected device and `InternalGetDeviceProcess` resolves by `Description`, so once the
launcher/deploy emit the chosen device's `Description`, the AD7 engine connects to it.

With these three aligned, **selecting in the Device Explorer already runs/deploys/debugs
to the chosen device today** — even before the toolbar dropdown exists. The dropdown is
purely an additional, more convenient selection *surface* over the same `SelectedDevice`.

Status: mechanism decided (MAUI `IVsProjectCfgDebugTargetSelection`); consumer wiring
complete; the COM/`.vsct`/CPS-aggregation dropdown is scaffolding pending in-VS work.
