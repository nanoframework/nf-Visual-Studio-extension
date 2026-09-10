# 06 — Visual Studio & VS Code Integration

How the IDE tooling changes when the build moves into the SDK — and where the
Visual Studio debugger gates that change.

**Gate note — CLEARED ✅.** The maintainer attributed the VS-side block to the
**debugger** (doc 09 §9.5, discussion
[#1635](https://github.com/orgs/nanoframework/discussions/1635)). A code read
decomposed it: the project system is already CPS and the deploy/debug-launch
providers key off a capability and launch the engine **by GUID**, so the real gate
was **build-targets composition + capability registration**, with the AD7 engine
orthogonal. The A+C POC ([poc-sdk-style-debugging-plan.md](poc-sdk-style-debugging-plan.md))
**confirmed this on real hardware** — deploy + F5 + source breakpoints on an
SDK-style project, AD7 unchanged. So every "behind the gate" item below is **proven
feasible**; what remains is productizing it in the shipped extension. The **VS Code /
CLI** build path was never gated.

**Scope note.** OTA session UI and modular-device debugging are out of scope
(separate effort) and are not covered here.

---

## 6.1 The principle: thin the extension, fatten the SDK

Today the VS extension carries build logic (deploy orchestration, parts of the
project system). The destination is that **everything buildable lives in the SDK**
and the extension keeps only what genuinely needs an IDE:

| Capability | Today | Destination | Was gated? |
|-----------|-------|-------------|--------|
| Project system / build | VS extension flavor + `NFProjectSystem.*` | **SDK** (`dotnet build`) — now [`nanoframework/nanoFramework.Sdk`](https://github.com/nanoframework/nanoFramework.Sdk) | VS load: was, now POC-proven |
| Restore | VS + nuget | **SDK** (PackageReference) | no |
| Deploy | VS extension button (own logic) | **SDK `Deploy` target**, extension invokes it | no (CLI); VS button: POC-proven |
| Device Explorer | VS extension | VS extension (kept) | — |
| Debugger | VS extension | VS extension (kept) | **was the gate — now cleared ✅** |
| Property pages | VS extension flavor | CPS defaults + light nano page | with VS load |

The extension's destination is **device explorer + debugger + a deploy button that
calls an MSBuild target**. The POC proved that VS path works on hardware (deploy + F5
+ breakpoints); productizing it in the shipped extension is what remains. (The
"Gated?" column reflects the now-cleared gate.)

## 6.2 Flavor GUID → CPS (gate cleared ✅)

Legacy `.nfproj` loads via project flavor GUID
`{11A8DD76-328B-46DF-9F39-F559912D0360}` over the old C# project system. The
destination is the **Common Project System (CPS)**, recognized by `<Project
Sdk="...">`. This is the single largest IDE change — and was thought to be the one
the debugger gated. The POC settled it: an SDK-style `<Project Sdk="...">` carrying
the `NanoCSharpProject` capability **deploys and debugs (F5 + breakpoints) on real
hardware** with the debug/F5 path unchanged. During the transition both load
side-by-side (the legacy flavor stays supported).

The extension lights up Device Explorer / Deploy / Debug on SDK-style projects via a
small **CPS capability** keyed off the nanoFramework SDK (detecting
`nanoFramework.Sdk` or `TargetFrameworkIdentifier=.NETnanoFramework`). The POC injects
exactly this capability from the SDK targets.

## 6.3 What was always unblocked vs. what the POC proved

The right column was *thought* to be gated; the POC proved each item works. The
remaining effort is productizing them in the shipped extension, not feasibility.

| Always unblocked | Proven by the POC ✅ (now: productize) |
|-----------------|--------------------------|
| VS Code: `dotnet build` / `dotnet restore` | VS: load SDK-style projects via CPS |
| CLI build/pack/test of SDK-style projects | VS: F5 debug of SDK-style projects |
| Republished `netnano1.0` packages restore cleanly | VS deploy button → `Deploy` target |
| Test Explorer via `dotnet test` | Retiring the flavor project system (now feasible) |

## 6.4 VS Code extension (unblocked)

> A detailed, code-grounded impact analysis of this migration on the VS Code
> extension — what simplifies, what stays, and a phased plan — is in
> [vscode-extension-impact.md](vscode-extension-impact.md).

The VS Code extension already shells `nuget restore` + `msbuild` + `nanoff` and
injects `NanoFrameworkProjectSystemPath`. Post-migration it gets *simpler*, and
this does **not** depend on the debugger:

- Replace `nuget restore` + `msbuild` + injected project-system path with
  **`dotnet build`** (the SDK self-resolves; no injected path).
- Replace its bespoke deploy with **`dotnet nano deploy`** / `dotnet build
  -t:Deploy`.
- Test Explorer integration targets the same test targets via `dotnet test`.
- The current class of failures — "MSBuild cannot find target Build", injected
  `NanoFrameworkProjectSystemPath`, Mono MSBuild version skew — largely
  **disappear**, because the SDK supplies `Build` and resolves itself through
  standard restore rather than a path hack.

## 6.5 The deploy button → MSBuild target (VS, post-gate productization)

Once VS loads SDK-style projects, the VS "Deploy" command becomes a wrapper that
runs the project's `Deploy` target (doc 05) against the device the Device Explorer
selected, instead of bespoke extension code:

```
VS Deploy command
  → resolve selected device (Device Explorer)  [IDE responsibility]
  → MSBuild Deploy target with NanoDeployTarget=<selected port>  [SDK responsibility]
  → stream task output to VS output window
```

This collapses two deploy implementations (VS extension's and CLI's) into one (the
SDK target). It rides on the same CPS work the POC validated, so it lands as that work
is productized into the shipped extension.

## 6.6 Debugger — what actually couples it

The debug engine (`vs-extension.shared/CorDebug/CorDebug.cs`) is a custom **AD7**
engine (`Microsoft.VisualStudio.Debugger.Interop`) plus a custom port supplier,
and it talks to the device via the shared `nf-debugger` wire protocol. Critically,
it is **launched by GUID** through the CPS `DebugLaunchProviderBase`
(`LaunchDebugEngineGuid = CorDebug.EngineGuid`) — it does not inspect the
project-file format. The POC **confirmed** the engine is **orthogonal** to SDK-style:
what was gating VS debugging was the project loading with the `NanoCSharpProject`
capability (targets composition + registration), not the engine — AD7 attached and
breakpoints hit unchanged.

The A+C POC therefore keeps the AD7 engine and introduces an **engine-binding
abstraction** (`INanoDebugEngineBinding`) so the launcher no longer hard-codes the
engine/port GUIDs. That isolates a future **AD7 → Concord** migration (model on the
Concord *Iris* sample) to one swappable implementation, with the device wire client
shared across both. See [poc-sdk-style-debugging-plan.md](poc-sdk-style-debugging-plan.md). When SDK-style debugging
lands, the engine consumes `.pdbx` at the SDK's output path
(`$(NanoPdbxOutputPath)`).

## 6.7 What the extension still must own

- **Device discovery / Device Explorer** — USB/serial enumeration, device
  capabilities, ping. Not a build concern.
- **Debug engine** — breakpoints, stepping, evaluation over the wire protocol.
- **CPS rule + capability registration** (post-gate) so the above light up on
  SDK-style nanoFramework projects.
