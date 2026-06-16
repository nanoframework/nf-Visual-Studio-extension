# 01 — Current State Analysis

This document inventories what the legacy `.nfproj` system actually does, so the SDK design (doc 02) has a concrete migration target. Where a fact is load-bearing for the migration, it is called out.

---

## 1.1 The three layers of the current build

The current managed build is split across three components with poorly-defined boundaries. The migration's first job is to draw those boundaries cleanly.

### Layer A — Visual Studio extension (`nf-Visual-Studio-Extension`)

Provides:

- **The project flavor.** Registered under project type GUID `{11A8DD76-328B-46DF-9F39-F559912D0360}` (composed with the C# project GUID `{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}` via `<ProjectTypeGuids>`). This is what makes VS load `.nfproj` with the nanoFramework property pages, references UI, and build behavior.
- **The MSBuild props/targets payload.** Drops `NFProjectSystem.*.props/targets` into `$(MSBuildExtensionsPath)\nanoFramework\v1.0\`, which `.nfproj` files locate via `$(NanoFrameworkProjectSystemPath)`.
- **Device Explorer** — serial/USB device discovery, ping, device capabilities, deploy UI.
- **Deploy button** — builds, then pushes PE assemblies to the device.
- **Debugger integration** — the wire-protocol debug engine (breakpoints, stepping, the managed debugging session against nanoCLR).

The problem: **build logic lives here that should live in the SDK.** The deploy button, in particular, encodes deploy orchestration that ought to be an MSBuild target callable from CLI. Today CLI users can't `deploy` without `nanoff` invoked separately.

### Layer B — MSBuild project system (`NFProjectSystem.*`)

Distributed as four files (names/contents reconstructed from public sources; exact internals to be confirmed against the repo during Phase 0):

| File | Role |
|------|------|
| `NFProjectSystem.Default.props` | Default property values; imported at top of `.nfproj`. Sets `NanoFrameworkProjectSystemPath`, output type, framework version defaults. |
| `NFProjectSystem.props` | Core properties; references the C# common props. |
| `NFProjectSystem.CSharp.targets` | Hooks Roslyn (`csc`) compilation into the nanoFramework graph; AnyCPU enforcement. |
| `NFProjectSystem.MDP.targets` | The MDP stage. Defines/invokes the metadata-processor task(s) — e.g. `GenerateBinaryOutputTask` (~line 718) — after the C# compile. Produces `.pe`, `.pdbx`, native stubs, checksum. |

These import the standard `Microsoft.CSharp.targets` underneath, then layer the nanoFramework PE stage on top. This is *exactly* the structure an MSBuild SDK formalizes (`Sdk.props` at the top, `Sdk.targets` at the bottom, replacing the explicit imports).

### Layer C — Metadata Processor (MDP)

Two shipping forms:

- **`nanoFramework.Tools.MetadataProcessor.MsBuildTask`** — the managed MSBuild task DLL (built x64 to match VS2022's host). This is the integrated path; `NFProjectSystem.MDP.targets` calls it. **MDP is already a task**, contradicting the "external post-build tool" framing.
- **`nanoFramework.Tools.MetadataProcessor.CLI`** — the standalone CLI (`-loadhints`, `-parse`, `-compile` in order) for runtime-codegen scenarios (generate C# at runtime, compile with Roslyn, convert IL→PE on the fly). Distributed as a *content* package; on non-SDK projects you must set Copy-to-Output manually.

What MDP does (the core of the managed pipeline):

1. Parses the IL assembly Roslyn emitted.
2. Emits the **`.pe`** (nanoFramework Portable Executable) — the actual on-device assembly format.
3. Emits **`.pdbx`** (nanoFramework debug DB) for the VS debugger.
4. For `[MethodImpl(InternalCall)]` / native interop declarations, generates **native stub C++** into `bin/<config>/stubs/<assembly-name>/`.
5. Computes the **`NativeMethodsChecksum`** (the PE↔native ABI hash), writes it into `corlib_native.cpp` / the stub headers, and embeds it in the PE. This checksum is what the runtime checks at load time to refuse a PE whose native counterpart doesn't match.

> **Migration-relevant:** the SDK should surface this checksum as a build output
> (an MSBuild property), not bury it in generated C++, so an optional build-time
> ABI gate can consume it (doc 04 §4.5). This is a managed-build concern; the
> stubs themselves feed the firmware build and are never shipped in a package.

## 1.2 The PE build pipeline, today

```
                 .nfproj (MSBuild, AnyCPU, flavored)
                        │
          ┌─────────────┴──────────────┐
          │  CoreCompile (Roslyn csc)  │   →  obj/<cfg>/<Asm>.dll  (standard IL)
          └─────────────┬──────────────┘      + .pdb
                        │
          ┌─────────────┴──────────────┐
          │  MDP MSBuild task           │
          │  (NFProjectSystem.MDP.      │   →  bin/<cfg>/<Asm>.pe      (nano PE)
          │   targets, GenerateBinary   │   →  bin/<cfg>/<Asm>.pdbx
          │   OutputTask et al.)        │   →  bin/<cfg>/stubs/<Asm>/*.cpp,*.h
          └─────────────┬──────────────┘   →  NativeMethodsChecksum (embedded)
                        │
                 NuGet pack (.nuspec) → lib/netnanoframework1.0/<Asm>.{pe,pdbx,dll,xml}
```

Notes that matter:

- The `.dll` Roslyn produces is a throwaway intermediate; the **`.pe` is the artifact**. (NuGet packages ship the `.pe` *and* the reference `.dll` so other projects can compile against the API.)
- AnyCPU is mandatory; the MDP task DLL being x64 + `nodeReuse` interactions cause the well-known "build the test project's nfproj in a pre-build event with `-nr=False`" workaround.
- There is **no RID** anywhere. The build is target-agnostic; the *same* PE runs on any device whose CLR exports the matching native methods (validated by checksum). This stays true for the managed migration — there is no per-RID native artifact in scope.

## 1.3 NuGet packaging, today

- Packages are produced from a hand-written **`.nuspec`** alongside the `.nfproj` (see the IoT.Device repo layout: `Binding1.nuspec`, `version.json`). `dotnet pack` / SDK pack is *not* used; packaging is `nuget pack` against the nuspec.
- Versioning via **Nerdbank.GitVersioning** (`version.json`).
- Package contents: managed only — `lib/netnanoframework1.0/<Asm>.pe`, `.pdbx`, the reference `.dll`, and `.xml` docs. **No native payload.** Native code (where it exists) lives in `nf-interpreter/targets/` and is compiled into the monolithic firmware, *not* shipped in the library's package.

The managed migration keeps this property: packages ship managed assets only.
What changes is the folder (`lib/netnano1.0/`) and the mechanics (`dotnet pack`
from MSBuild properties instead of `nuget pack` from a `.nuspec`) — see doc 08.

## 1.4 The firmware build (out of scope, but adjacent)

`nf-interpreter` builds nanoCLR/nanoBooter with **CMake**, entirely separate from MSBuild. `nanoff` downloads pre-built firmware images and flashes them. The SDK migration does **not** absorb the CMake firmware build, and does not introduce any native build path of its own — native compilation and firmware packaging are out of scope (separate effort). For native interop, MDP continues to emit stubs that the CMake firmware build consumes; the managed SDK's job stops at producing the PE.

## 1.5 Inventory: properties / items / tasks to migrate

The SDK must preserve or replace these. Exact set to be enumerated from the repo in Phase 0; known/expected members:

**Properties**
- `NanoFrameworkProjectSystemPath` — *eliminated* (SDK resolves itself).
- `TargetFrameworkVersion` = `v1.0` — *replaced* by `TargetFramework=netnano1.0`.
- `ProjectTypeGuids` — *eliminated* (no flavor).
- AnyCPU `Platform` — *retained as default*, but RID becomes the meaningful axis.
- `NativeMethodsChecksum` (output) — *surfaced as a public output property/item*.

**Tasks**
- `GenerateBinaryOutputTask` and siblings in MDP.targets — *re-hosted* into `nanoFramework.Sdk` targets (doc 04).

**Items**
- Implicit `Compile` globs do not exist today (every `.cs` is listed). SDK introduces default globbing — a major project-file simplification (doc 03).

**Targets**
- The Roslyn→MDP chain — *reimplemented* as SDK targets ordered against `AfterCompile`/`CoreCompile` (doc 04 §4.4).
