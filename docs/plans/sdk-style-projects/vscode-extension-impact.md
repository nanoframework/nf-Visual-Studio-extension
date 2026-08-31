# VS Code Extension — SDK-Style Migration Impact Analysis

Companion to [06-ide-integration.md](06-ide-integration.md) §6.4, grounded in the
**shipped** extension (`nanoframework.vscode-nanoframework` v1.0.249, inspected at
`~/.vscode/extensions/...`) rather than assumptions. It corrects two premises in
doc 06 and lays out the concrete migration impact.

> Cross-reference: the SDK design is [02-sdk-design.md](02-sdk-design.md); the MDP
> re-host is [04-mdp-native-integration.md](04-mdp-native-integration.md); the
> executed POC (build proven, `.pe`/`.pdbx` emitted) is in
> [poc-findings/RESULTS.md](poc-findings/RESULTS.md).

## 0. Two corrections to doc 06 §6.4 (from reading the extension)

1. **The VS Code extension HAS its own debugger — and it is cross-platform.** Doc
   06 implies VS Code is build/deploy/test only and that "debugger" means the VS
   AD7 engine. In fact the extension contributes a full debug type
   (`package.json`: `"debuggers"` → `"type": "nanoframework"`, with
   `"breakpoints"`), implemented as a DAP adapter (`out/debugger/`:
   `nanoDebugAdapter.js`, `nanoDebugSession.js`, `nanoRuntime.js`, `bridge/`)
   backed by a **native, cross-platform** bridge binary
   `bin/nanoDebugBridge/{v1,v2}/` shipped for `darwin-arm64`, `darwin-x64`,
   `linux-arm64`, `linux-x64`, `win32-arm64`, `win32-x64`
   (`nanoFramework.Tools.DebugBridge`, a Mach-O/ELF/PE .NET executable).
2. **VS Code debugging is NOT gated by the project-file format the way VS is.** It
   launches through a `launch.json` `"type": "nanoframework"` configuration and the
   bridge attaches to the *device running the deployed `.pe`*, over the shared
   `nf-debugger` wire protocol. It does **not** load the project through CPS or the
   legacy flavor, and it does not use the AD7 engine or the
   [INanoDebugEngineBinding](poc-findings/RESULTS.md) seam (that seam is a
   VS-only concern). So the VS debugger gate (doc 09 §9.5) does **not** apply to VS
   Code.

**Consequence:** the VS Code extension is *even more* unblocked than doc 06 states
— not just build/pack/test, but **debugging too**. Once an SDK-style project builds
and deploys, VS Code can debug it, because the bridge debugs the PE+`.pdbx`, which
the SDK produces identically (byte-identical PE — see RESULTS).

## 1. What the extension actually is today (grounded inventory)

| Subsystem | Files (in the shipped extension) | How it works today |
|-----------|----------------------------------|--------------------|
| Build | `out/dotnet.js`, `out/executor.js`, `out/extension.js` | Shells **`msbuild`** (Mono on macOS/Linux), injecting `NanoFrameworkProjectSystemPath = …/dist/utils/nanoFramework/v1.0\|v2.0\` and `NF_MDP_MSBUILDTASK_PATH`/`NF_MSBUILDTASK_PATH` overrides at the bundled legacy targets + MDP task |
| Restore | `out/nuget.js` | Shells **`nuget restore`** (`packages.config`-era) |
| Project system payload | `dist/utils/nanoFramework/{v1.0,v2.0}/NFProjectSystem.*.{props,targets}` + `nanoFramework.Tools.MetadataProcessor.MsBuildTask.dll` + corlib bits | The same legacy targets the VS extension ships, bundled into the VSIX and located via the injected path |
| Deploy / flash | `out/extension.js` (`nfdeploy`, `nfflash`) | Shells **`nanoff`** |
| Debug | `out/debugger/*` + `bin/nanoDebugBridge/{v1,v2}/<rid>/nanoFramework.Tools.DebugBridge` | DAP adapter → cross-platform bridge → `nf-debugger` wire protocol → device |
| Test | `out/testDiscovery.js`, `out/testExecution.js`, `out/runSettings.js` | Discover/run nanoFramework unit tests with a `.runsettings` |
| Virtual device | `out/nanoclrManager.js` | Manages the **`nanoclr`** `dotnet tool` (install/update + run) as a virtual device |
| Project scaffolding | `out/createProject.js`, templates in `dist/utils/CS.*-vs2022/` | `nfcreate` / `nfadd` copy legacy `.nfproj` templates |
| NuGet management | `nfaddnuget` / `nfremovenuget` / `nfupdatenuget` | Edits `packages.config` / project references |
| Prerequisites | `out/prerequisites.js` | Checks for **mono, msbuild, nuget, nanoff, dotnet** |

The defining characteristic: the build/restore path is the **legacy Mono-msbuild +
nuget + injected-targets** stack, which is exactly the fragile part the SDK
migration removes. The debug/test/virtual-device paths are already modern and
mostly format-agnostic.

## 2. Migration impact per subsystem

| Subsystem | Impact | Gated on the VS debugger? |
|-----------|--------|---------------------------|
| **Build** | `msbuild` + injected `NanoFrameworkProjectSystemPath` → **`dotnet build`** (SDK self-resolves via NuGet). Drop the bundled `dist/utils/nanoFramework/*` targets payload. | No |
| **Restore** | `nuget restore` + `packages.config` → restore is implicit in `dotnet build` with `PackageReference`. Drop `out/nuget.js`'s restore path. | No |
| **Deploy / flash** | Keep `nanoff`, or move to the SDK **`Deploy` target** (`dotnet build -t:Deploy`, doc [05](05-cli-experience.md)). Either way the bundled targets path injection goes away. | No |
| **Debug** | **No change required to the bridge.** It already debugs the deployed PE/`.pdbx` over the wire protocol, independent of project format. Only the `preLaunchTask` (build) changes from msbuild to `dotnet build`. | **No** (corrects doc 06/09) |
| **Test** | Today's discovery/execution should keep working; ideally moves to `dotnet test` against the SDK test project (doc 06 §6.4). | No |
| **Virtual device** | No change — `nanoclrManager.js` keeps managing the `nanoclr` tool; the SDK-built `.pe` runs on it (this is the POC's Layer A target). | No |
| **Project scaffolding** | `nfcreate`/`nfadd` emit a 6-line SDK-style `.csproj` (doc [03](03-project-file-migration.md)) instead of the legacy `.nfproj` templates. | No |
| **NuGet management** | `nfaddnuget`/etc. edit `<PackageReference>` instead of `packages.config`. | No |
| **Prerequisites** | **mono, msbuild, nuget drop out** of the required-tools check; only the **.NET SDK** (+ `nanoff`/`nanoclr` tools) remain. Big reduction. | No |

### What gets deleted / simplified

- The bundled **`dist/utils/nanoFramework/{v1.0,v2.0}/`** targets + MDP-task payload
  (~MBs of legacy targets, two parallel versions) — the SDK resolves itself via
  NuGet, so the VSIX no longer ships or injects a project system.
- **`NanoFrameworkProjectSystemPath` / `NF_MDP_MSBUILDTASK_PATH` / `NF_MSBUILDTASK_PATH`**
  injection — gone.
- The **Mono-msbuild + nuget** dependency and its failure modes (doc 06 §6.4:
  "MSBuild cannot find target Build", injected-path errors, Mono version skew)
  — gone, replaced by a single `dotnet build`.
- `out/nuget.js` restore path; the `.nfproj`/`packages.config` templates.

### What stays

- The **debug adapter + `nanoDebugBridge`** (cross-platform) — unchanged.
- **`nanoff`** deploy/flash and the **`nanoclr`** virtual device manager.
- Serial monitor, device selection, test discovery/execution.

## 3. The Mac angle (relevant to running this without Windows)

Because the bridge is cross-platform and the debug path is format-agnostic, **VS
Code on macOS can build, deploy, and *debug* SDK-style nanoFramework projects with
no Windows and no Visual Studio** — once the build path is switched to
`dotnet build`. Concretely, on a Mac:

- `dotnet build` an SDK-style project (proven in the POC — RESULTS WS1).
- Deploy/run on the `nanoclr` virtual device (managed cross-platform by
  `nanoclrManager.js` as a `dotnet tool`) or a physical board.
- Set breakpoints and debug via the `nanoframework` debug type
  (`nanoDebugBridge`, darwin-arm64).

This makes the **VS Code path the most attractive near-term validation surface for
SDK-style debugging** — it is the productized, cross-platform sibling of the POC's
Layer A harness, and it sidesteps the headless-VS problem entirely. The VS AD7 F5
path (POC Layer B) remains the Windows-only gate, but it is no longer the only way
to demonstrate SDK-style debugging end to end.

## 4. Risks

| Risk | Note / mitigation |
|------|-------------------|
| Bridge assumes the deployed `.pe`/`.pdbx` layout | The SDK emits `.pe`+`.pdbx` byte-identically to the legacy MDP (RESULTS WS1 gate); validate the bridge resolves the SDK's `$(NanoPdbxOutputPath)` location |
| `preLaunchTask` / `tasks.json` still call msbuild | Update the generated `tasks.json`/`launch.json` to `dotnet build`; old workspaces need a re-scaffold or a migration of their task definitions |
| Test discovery may assume `.nfproj` output conventions | Verify against an SDK-style test project; prefer `dotnet test` |
| Mixed fleets (some `.nfproj`, some SDK-style) during rollout | The extension must detect both; keep the legacy build path until repos are converted (doc [07](07-library-migration.md), doc [09](09-implementation-strategy.md)) |
| `nanoclr` cross-platform parity | The Windows virtual device uses `virtualserial` (com0com); confirm the macOS/Linux `nanoclr` transport when validating the Mac debug loop |

## 5. Phased plan (VS Code)

1. **Build/restore swap (unblocked, highest value):** add an SDK-style detection
   (`<Project Sdk="nanoFramework.Sdk…">` or `TargetFramework=netnano1.0`); when
   detected, use `dotnet build`/restore and skip the injected targets payload.
   Keep the legacy path for `.nfproj`.
2. **Scaffolding:** `nfcreate`/`nfadd` emit SDK-style `.csproj` + `global.json` SDK
   pin; NuGet commands edit `PackageReference`.
3. **Debug task wiring:** generated `launch.json` keeps `"type": "nanoframework"`;
   `preLaunchTask` becomes `dotnet build`. No bridge change.
4. **Test:** move to `dotnet test` against the SDK test project.
5. **Cleanup:** once the fleet is converted, drop the bundled
   `dist/utils/nanoFramework/*` payload, the `NanoFrameworkProjectSystemPath`
   injection, and the mono/msbuild/nuget prerequisites.

The VS Code work was entirely in the "unblocked" column of doc 06 §6.3 — none of it
ever waited on the VS AD7 debugger gate (which the POC has since cleared anyway).
