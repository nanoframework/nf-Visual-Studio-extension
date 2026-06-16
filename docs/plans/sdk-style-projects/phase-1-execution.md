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

## Cross-cutting validation (tests)

**Central Package Management (CPM)** — ✅ PASSED. `samples/Beginner/Directory.Packages.props`
(`ManagePackageVersionsCentrally=true`) + versionless `PackageReference`s build to NFMRK2. No
NU1008: the SDK's injected MetadataProcessor reference is `IsImplicitlyDefined`, which CPM
exempts — so the nano SDK is CPM-compatible unchanged. Caveat: a *bulk-converted* repo gets
*versioned* `PackageReference`s, which conflict with CPM. A repo uses **either** CPM
(versionless + a central props) **or** per-project versions, not both; repo-wide CPM would
need the converter to emit versionless + aggregate versions into a root `Directory.Packages.props`
(future enhancement).

**Legacy / SDK coexistence** — ✅ PASSED. A legacy `.nfproj` (VS MSBuild + the installed
NFProjectSystem targets) and an SDK-style `.csproj` (`dotnet build`) build side by side in one
solution; both emit NFMRK2. A direct cross-`ProjectReference` requires both projects on the
**same CoreLibrary version** (different mscorlib = type-identity conflict).

**Fleet conversion (NanoMigrate)** — ✅ PASSED, idempotent + reentrant. Run over a full copy of
`samples/` (143 `.nfproj`): all converted to SDK-style `.csproj` with correct package ids +
versions derived from the `packages\<Id>.<Version>\` HintPath, `packages.config` deleted,
`Properties/AssemblyInfo.cs` deleted (desktop test projects correctly left alone), and `.sln`
entries rewritten (project-type GUID + `.csproj` path). Re-running is a clean no-op
(`nothing to convert`); a mixed tree converts only the remaining `.nfproj`. Known gap:
references with neither a HintPath nor a matching `packages.config` entry (some `System.*` in a
few WebServer/WiFi samples) are flagged for manual review rather than mapped — needs a broader
assembly-name → package-id map.

## Samples migration — ✅ COMPLETED

The whole Samples repo was migrated to SDK-style via `dotnet nano migrate` (the hardened
NanoMigrate, `tools/NanoMigrate`): **153 projects converted, 111 solutions retargeted**,
`packages.config`/generated `AssemblyInfo` removed, 0 review flags. Spot-built across shapes
(app, resx, many-package, shared-project, interop, unit-test) — all build.

Notes / follow-ups from the migration:
- **PE format / v1↔v2.** Migrated **apps compile to NFMRK2** (the SDK pins MDP 4.x). Samples that
  still reference **v1** packages pull **NFMRK1** dependency `.pe` (the v1 packages ship v1 PE), so
  for a v2 device the whole graph isn't aligned yet. Making samples v2-device-ready is a separate
  **fleet package bump** (CoreLibrary + bindings → v2), gated on v2 preview availability across all
  packages each sample uses — not a migration defect (the conversion faithfully kept each sample's
  versions).
- **Source compat.** The SDK now also defines `NANOFRAMEWORK_1_0` (legacy symbol) so existing
  `#if NANOFRAMEWORK_1_0` source compiles.
- **Per-sample fixes applied:** `1-Wire/OneWire.TestApp` (added the missing `Hardware.Esp32`
  reference) and `Interop/test-application` (bin-HintPath sibling → `ProjectReference`).
- **Full build sweep:** every SDK-style project built — **152 / 155 pass**. After removing one
  stray empty project (`AMQP/Azure-ServiceBus-Sender/Sender.csproj`, an orphan `.nfproj` never in a
  solution), the only failures are the **2 AMQP apps** (Azure-IoT-Hub, Azure-ServiceBus-Sender).
- **AMQP — pre-existing rot, out of scope.** Their `packages.config` pins very old, mutually
  inconsistent versions (NU1605 downgrades cascade across CoreLibrary/Runtime.Events/…), and the
  code is half-modernized: `Program.cs` uses `System.Device.Wifi` + `WifiNetworkHelper`/
  `NetworkHelper`, but the package refs are the deprecated `Windows.Devices.Wifi` (IoT-Hub) or have
  no wifi package (Sender), and `NetworkHelper` isn't provided (other samples ship a local
  `NetworkHelpers.cs`). Fixing them is sample modernization (correct wifi package + the missing
  helper), not a project-system-migration concern; the conversion was faithful.
- **`Desktop*` helpers:** regular .NET Framework test projects (not nanoFramework), untouched.

## What's done (Phase 1 + tooling)

- ✅ SDK with the debugging enablers + v2/NFMRK2 (A1–A4); props/targets split into modules.
- ✅ **NanoMigrate** converter — Core/Cli/Tests, Spectre.Console.Cli, solution-aware (`.sln`/`.slnx`),
  packages.config-first resolution, CPM support, item/import hardening (69 tests).
- ✅ **`dotnet nano`** umbrella tool — built-in `migrate` + external-tool (`nanoff`) wrapping.
- ✅ Samples repo fully migrated (153 projects, 111 solutions).
- ✅ SDK defines `NANOFRAMEWORK_1_0` for source compat.
- ✅ Three draft PRs open + cross-referenced (SDK#2, extension#929, Samples#463).

## Next steps (per [09-implementation-strategy.md](09-implementation-strategy.md))

1. **Land the PRs (Phase 3 unlock).** Review → ready → merge SDK#2 first; **publish the SDK** to
   nuget.org; then extension#929; then Samples#463 (swap its local SDK feed for the published
   version). See EXECUTION-PLAN.md → PR strategy.
2. **Templates** (`dotnet new nanoapp`/`nanolib`, doc 10 §10.4) — self-contained; can proceed now.
3. **VS Code extension** → detect SDK-style `.csproj` + invoke `dotnet build`; install the SDK from
   a feed instead of extracting MSBuild folders from the VSIX (doc 06).
4. **CoreLibrary** — adopt `CoreLibrary.Sdk.csproj` as the primary project (maintainer-owned).
5. **Phase 1 exit gate** — a pilot of ~5 pure-managed `lib-*` repos build/pack/test from the CLI
   (clone + `dotnet nano migrate`), opening PRs from the org template.
6. **Phase 4 (fleet)** — bulk-migrate the `lib-*` fleet with `dotnet nano migrate fleet` + the
   auto-PR contract (docs [07](07-library-migration.md)/[PR-INSTRUCTIONS.md](PR-INSTRUCTIONS.md)).

### Deferred / gated
- **Samples v2 alignment** — bump samples to v2 packages so the whole graph is NFMRK2 (gated on v2
  preview availability across every package each sample uses).
- **`nanoff` download** — the umbrella's external-tool downloader is a stub; implement
  fetch+verify+cache when wiring real firmware flashing.
