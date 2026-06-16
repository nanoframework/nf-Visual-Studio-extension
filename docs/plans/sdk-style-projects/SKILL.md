---
name: nanoframework-sdk-migration
description: >-
  Migrate .NET nanoFramework projects from the legacy custom .nfproj project
  system to the SDK-style MSBuild project system. Use this whenever a maintainer
  wants to convert a nanoFramework library or application to an SDK-style project,
  fold packages.config into PackageReference, fold .nuspec into MSBuild Pack
  properties, or bulk-migrate many repos at once (e.g. cloning and converting the
  whole fleet of nanoframework/lib-* repositories). Trigger this for any mention
  of ".nfproj", "SDK-style nanoFramework", "nanoFramework project migration",
  "migrate the nano libraries", "convert nfproj to csproj", or running a
  fleet/bulk migration across nanoFramework repos — even if the word "skill" is
  never used. SCOPE: project-system migration only; this does NOT cover OTA
  updates or modular firmware packaging.
---

# nanoFramework SDK-style project migration

This skill converts a nanoFramework repo from the legacy flavored `.nfproj`
project system onto an SDK-style MSBuild project that composes over the
nanoFramework SDK. It ships a tested C# tool (`tools/NanoMigrate`) that does
the mechanical conversion, plus the rules and workflows for using it on one repo
or across the whole library fleet.

## Scope — read this first

This is **project-system migration only**. The tool and this skill deliberately
do **not** touch, and you should not add to them:

- OTA update artifacts (`NFMANIF1`/`NFUPD1`, update packages, rollback metadata)
- Modular firmware packaging (`runtimes/{rid}/native/`, relocatable native modules)
- ABI / module manifests (`module_manifest.json`, `abi_compatibility.json`)
- Anything firmware- or device-side

If a request asks to combine migration with OTA or modular packaging, do the
project-system migration here and treat the OTA/packaging work as a separate,
later phase. Keep the two cleanly separated.

## What the conversion does

For each `.nfproj` the tool produces an SDK-style project and:

- Replaces the legacy `<Project xmlns=...>` + imports with `<Project Sdk="nanoFramework.Sdk/<version>">`.
- Drops project-system boilerplate and anything the SDK now supplies (`ProjectTypeGuids`,
  `ProjectGuid`, `TargetFrameworkVersion`, `Configuration`/`Platform`, the
  `NFProjectSystem` import, etc.) and sets a single `<TargetFramework>`.
- Folds `packages.config` into `<PackageReference>` items, deletes `packages.config`,
  and aliases legacy references (`mscorlib`/`System` → `nanoFramework.CoreLibrary`).
- Folds `.nuspec` metadata (`id`, `description`, `authors`, `tags`, `projectUrl`)
  into MSBuild Pack properties so the package is produced by `dotnet pack`.
- Drops default `Compile` globs and a hand-written `Properties/AssemblyInfo.cs`
  (it would collide with `GenerateAssemblyInfo`), while preserving non-default
  items (linked files, files in subfolders, `EmbeddedResource`, `Content`).
- **Fails loud**: anything it cannot confidently resolve is written to a
  `MANUAL REVIEW NEEDED` list instead of being silently guessed.

The full rule set, including edge cases, is in `references/migration-rules.md`.
Read it when you hit a manual-review item or need to explain a transformation.

## Prerequisites

- **.NET 8 SDK** (`dotnet --version` ≥ 8). The tool is BCL-only and ships a
  `nuget.config` that clears package sources, so it builds and runs offline.
- **git**, for the `clone` and `fleet` commands.
- Optionally a **GitHub token** (`--token` or `GITHUB_TOKEN`) to lift the
  unauthenticated API rate limit when cloning the fleet.
- For PR-bound work, a **signed nanoFramework CLA** and a **real name/email** in
  git config (`user.name`/`user.email`) — the commit sign-off depends on it.
  See `references/contributing-compliance.md` for the full contribution rules.

Invoke the tool with `dotnet run` (no separate build step needed):

```bash
dotnet run --project tools/NanoMigrate -- <command> [options]
```

For repeated fleet runs, build once and call the dll directly (faster):

```bash
dotnet build -c Release tools/NanoMigrate
DLL=tools/NanoMigrate/bin/Release/net8.0/nano-migrate.dll
dotnet "$DLL" <command> [options]
```

## Workflow A — migrate a single repo

Use this when a maintainer is converting their own library or app.

1. **Always dry-run first** to see what will change and what needs review:
   ```bash
   dotnet run --project tools/NanoMigrate -- migrate <repo-or-.nfproj> --dry-run
   ```
2. **Apply** once the dry-run looks right (writes a `.nfproj.bak` next to each file):
   ```bash
   dotnet run --project tools/NanoMigrate -- migrate <repo-or-.nfproj>
   ```
3. **Resolve every `MANUAL REVIEW NEEDED` line** before moving on — see
   "Handling review items" below. Do not hand-wave these; each is a real
   decision the tool refused to guess.
4. **Verify it builds**: `dotnet build <repo>`. A clean restore + build is the
   acceptance signal that the migration is sound.
5. Once green, the `.bak` files can be deleted (or rely on git and pass
   `--no-backup`).

`--ext .csproj` renames the project to `.csproj` (retiring the `.nfproj`); the
default keeps the `.nfproj` extension and rewrites it in place, which is the
lower-risk default during a phased rollout.

## Workflow B — fleet migration (clone + bulk convert)

Use this to migrate many repos at once, e.g. all `nanoframework/lib-*`.

1. **Clone the fleet** (skips archived repos by default):
   ```bash
   dotnet run --project tools/NanoMigrate -- clone ./nano-repos --token $GITHUB_TOKEN
   ```
   Narrow with `--filter` (default `lib-`) or point at a different `--org`.
2. **Dry-run the whole fleet** and read the report before changing anything:
   ```bash
   dotnet run --project tools/NanoMigrate -- fleet ./nano-repos \
     --branch sdk-migration --dry-run --report fleet-report.md
   ```
3. **Apply per-repo on a branch and commit**, so each repo ends up with a
   ready-to-PR branch:
   ```bash
   dotnet run --project tools/NanoMigrate -- fleet ./nano-repos \
     --branch sdk-migration --commit --issue <tracking-issue> --report fleet-report.md
   ```
   `--commit` implies `--no-backup` (git history already preserves the original),
   writes a contribution-compliant commit message (≤50-char summary, 72-wrapped
   body, `Fix #<issue>` when `--issue` is given), and signs off the commit
   (`Signed-off-by`) using your git identity. Branch names starting with
   `develop` are rejected per the project's workflow.
4. **Triage `fleet-report.md`** (see below). Repos in *Clean migrations* are
   ready to push; repos under *Needs manual review* or *Errored* need a human.
5. Push branches and open PRs per repo using the maintainers' normal flow. The
   tool stops at the commit — it does not push or open PRs. Follow
   `references/contributing-compliance.md` for the fork model, PR template,
   labels (`area-Config-and-Build`), and CLA requirement.

Migrate in dependency order (leaf-first): `nanoFramework.CoreLibrary` and the
base libraries first, then libraries that depend on them, so that a freshly
published SDK-style dependency is available when its dependents build. The
dependency notes in `references/migration-rules.md` describe the ordering.

## Handling review items

Each `MANUAL REVIEW NEEDED` line is one of a small number of cases:

- **"Reference without resolvable version: X"** — the tool found a `<Reference>`
  whose version it could not determine (no `packages.config` entry, no parseable
  `HintPath`). Add a `<PackageReference Include="..." Version="..." />` by hand,
  using the correct nanoFramework package id and version.
- **"Version for X inferred from HintPath as N"** — the tool fell back to reading
  the version out of the `HintPath` folder. Usually correct; confirm the package
  id and version match what the repo actually intends, then drop the note.
- **"Unhandled item <Tag Include=...>"** — an item type the tool does not
  rewrite. Decide whether it carries over verbatim into the SDK-style project or
  is now redundant, and add it back if needed.

When several repos share the same review item, fix the root cause once (often a
missing or stale `packages.config`) and re-run.

## Report structure (fleet)

The fleet report always uses this layout:

```
# nanoFramework SDK-style migration — fleet report
## Summary          (counts: repos / clean / needs review / errored)
## Errored repos     (git or parse failures — investigate first)
## Repos needing manual review   (per-repo list of review lines)
## Clean migrations  (ready to push)
```

Triage top-down: errored repos first (they did not migrate), then review repos,
then confirm the clean set.

## Guardrails

- **Dry-run before every apply.** It is free and it is the cheapest way to catch
  a surprise across 100+ repos.
- **A migration is "done" only when the project builds.** Always finish with
  `dotnet build`; a converted file that does not restore/build is not migrated.
- **Don't expand scope into OTA or modular packaging** (see "Scope").
- **Don't push or open PRs automatically.** Leave that to the maintainers.
- **Meet the contribution rules before any PR.** A migration must be a structural
  change only (no `.cs` edits, no style-only reformatting, `.editorconfig` left
  intact), the contributor must have signed the CLA, commits must be signed off
  with a real name, and the repo must build clean with tests passing. The full
  checklist is in `references/contributing-compliance.md` — consult it whenever
  the work is destined for an upstream PR.
- If you change the conversion rules, change them in `tools/NanoMigrate` and
  re-run the dry-run on a known repo to confirm the output is still correct.
