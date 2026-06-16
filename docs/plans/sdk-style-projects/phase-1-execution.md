# Phase 1 — execution status

Phase 1 = ship the managed/CLI groundwork (see [09-implementation-strategy.md](09-implementation-strategy.md) §9.3).
This tracks per-repo status and what has actually been done. Sibling clones live under `D:\src\nnf\`.

## Per-repo readiness (assessed 2026-06-15)

| Repo | Phase 1 item | Status | Notes |
|---|---|---|---|
| `nanoFramework.Sdk` | SDK package | ✅ done | Packs `nanoFramework.NET.Sdk` 1.0.0 to `artifacts/` (`GeneratePackageOnBuild`). |
| `CoreLibrary` | republish against `netnano1.0` | 🔶 partial (maintainer-owned) | `CoreLibrary.Sdk.csproj` (targets `netnano1.0`) exists on `upstream/move-to-sdk` but is not yet the primary project. The published v2 previews on nuget.org (`2.0.0-preview.52`) already restore natively against `netnano1.0`. |
| `Samples` | convert to SDK-style | 🔶 in progress | Pilot done (below); 153 legacy `.nfproj` remain. |
| `nf-VSCodeExtension` | switch to `dotnet build` | ⛔ not started | Still `nuget restore` + `MSBuild.exe`; only detects `.nfproj`, no `dotnet build` path. |
| (templates) | `dotnet new nanoapp`/`nanolib` | ⛔ not started | — |

## Samples pilot — done ✅

Converted two Beginner samples to SDK-style **v2** projects and verified they build from the CLI:

- `samples/Beginner/BlinkLed/1-BlinkLed.csproj` and `samples/Beginner/Button/2-Button.csproj`:
  `<Project Sdk="nanoFramework.NET.Sdk">`, `netnano1.0`, with `PackageReference` to
  `nanoFramework.CoreLibrary` `2.0.0-preview.52`, `nanoFramework.Runtime.Events` `2.0.1`,
  `nanoFramework.System.Device.Gpio` `2.0.0-preview.18`.
- Removed each sample's `*.nfproj`, `packages.config`, and `Properties/AssemblyInfo.cs`.
- `Beginner.sln`: the two entries switched to the SDK-style C# CPS project-type GUID
  `{9A19103F-16F7-4668-BE54-9A1E7A4F7556}` and `.csproj` paths. The other six samples stay
  legacy `.nfproj` (coexistence).
- **Build (CLI):** `dotnet build` against the official SDK succeeds; outputs
  `BlinkLed.pe` / `Button.pe` with PE magic **`NFMRK2`** (v2), and the full assembly graph
  (`mscorlib`, `System.Device.Gpio`, `nanoFramework.Runtime.Events` `.pe`) restores natively
  as v2 — no `AssetTargetFallback`/NU1701 noise.

## Dev-local build harness

To build SDK-style samples before the SDK ships to nuget.org:

- `Samples/global.json` pins `nanoFramework.NET.Sdk` `1.0.0`.
- `Samples/NuGet.Config` adds a `local-sdk` source (`../nanoFramework.Sdk/artifacts`) plus
  `nuget.org`.

These assume the sibling-clone layout and the unpublished local SDK build. **Once the SDK
ships to nuget.org: drop the `local-sdk` source and pin the published version in
`global.json`.** Legacy `.nfproj` samples are unaffected — they don't consume the msbuild-sdk.

## Remaining Phase 1

- **Bulk-convert** the remaining ~153 samples — mechanical; drive with the `NanoMigrate`
  converter (now in the nanoFramework.NET.Sdk repo, `tools/NanoMigrate`; see docs
  [07](07-library-migration.md)/[10](10-tooling-specs.md)) rather than by hand.
- **CoreLibrary:** adopt `CoreLibrary.Sdk.csproj` as the primary project (maintainer-owned).
- **Templates:** `dotnet new nanoapp`/`nanolib` (doc 10).
- **VS Code extension:** detect SDK-style `.csproj` and invoke `dotnet build`; install the SDK
  from a feed instead of extracting MSBuild folders from the VSIX (doc 06).
- **Exit gate:** a pilot set of ~5 pure-managed `lib-*` repos build/pack/test from the CLI.
