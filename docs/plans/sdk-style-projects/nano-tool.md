# The `dotnet-nano` umbrella tool

> **The tool now ships in the SDK repo — its READMEs are the source of truth.** For the
> as-built command surface, options, install, and external-tool resolution, read the shipped docs:
> - **[`tools/nano/README.md`](https://github.com/danielmeza/nanoFramework.Sdk/blob/move-to-sdk/tools/nano/README.md)** — the `dotnet nano` umbrella CLI.
> - **[`tools/migrate/README.md`](https://github.com/danielmeza/nanoFramework.Sdk/blob/move-to-sdk/tools/migrate/README.md)** — the `nano-migrate` converter (`migrate`/`clean`/`rollback`/`clone`/`fleet`).
>
> This document is the **design narrative** behind that layout; when it disagrees with the READMEs,
> the READMEs win.

How the single `dotnet nano` CLI is laid out so it can (1) host built-in managed commands like
`migrate`, and (2) ship/deploy already-built external tools from other repos (e.g. `nanoff`) under
the same namespace. Ships with / alongside the nanoFramework SDK. Complements doc
[05-cli-experience.md](05-cli-experience.md) (the verbs) and [10-tooling-specs.md](10-tooling-specs.md)
(the build-list).

## Goals

- One entry point: `dotnet nano <command>` for every nanoFramework workflow.
- **Built-in commands** implemented in managed code in this ecosystem (e.g. `migrate`, `deploy`,
  `monitor`, `devices`).
- **External prebuilt tools** wrapped, not rebuilt: deploy/pin already-released binaries from
  their own repos (e.g. `nanoff`) and expose them under `dotnet nano *` with a uniform Spectre UX.
- Logic is library code (testable, NuGet-ready); the CLI is a thin host.

## Architecture

- **Host** — a single .NET tool: `PackageId` e.g. `nanoFramework.Tool`, `PackAsTool=true`,
  `ToolCommandName=nano` → invoked as `dotnet nano …`. Built on **Spectre.Console.Cli**
  (`CommandApp`); each verb is a `Command<TSettings>`.
- **Built-in commands** (in-proc, managed): each references an engine *library*, not a nested
  process. `migrate` references `NanoMigrate.Core`; `deploy`/`monitor`/`devices` wrap the build
  `Deploy` target + device comms.
- **External tool providers** — an `IExternalTool` abstraction: `Name`, `ResolvePath()`,
  pinned `Version`, `Invoke(args)`. A command (e.g. `dotnet nano flash`) resolves the external
  binary, maps args, runs it, and renders output/errors through the shared Spectre reporter.
  Resolution order:
  1. a binary **bundled** in the tool package (`tools/<name>/` — prebuilt, version-pinned),
  2. a globally/locally **installed** tool (`dotnet tool` / PATH),
  3. **download** a pinned release to a user cache (verified by version/hash).
- **Tool manifest** — `nano-tools.json` (embedded or shipped) listing each external tool, its
  pinned version, and download source, so resolution/fetch is deterministic and CI-reproducible.

## Repo layout

```
tools/
  nano/
    nanoFramework.Tool/          # the dotnet-nano umbrella (PackAsTool, ToolCommandName=nano)
      Program.cs                 # Spectre CommandApp host
      Commands/                  # built-in commands: Flash (nanoff), Deploy/Monitor/Devices placeholders
      ExternalTools/             # IExternalTool + providers (NanoffTool, …) + nano-tools.json
    nanoFramework.Tool.Tests/    # tests for the umbrella (external-tool resolution, …)
  migrate/
    src/NanoMigrate.Core/         # conversion logic, NO console (testable, NuGet-ready library)
    src/NanoMigrate.Cli.Commands/ # shared Spectre commands (migrate) — referenced by both CLIs
    src/NanoMigrate.Cli/          # thin standalone CLI (`nano-migrate`)
    tests/NanoMigrate.Tests/      # unit tests against NanoMigrate.Core
# nanoFramework.Tool.slnx (repo root) ties the umbrella + its tests + the NanoMigrate libs together.
```

The migrate `migrate` command is the shared `NanoMigrate.Cli.Commands` library (over the
console-free `NanoMigrate.Core` engine); both the standalone `nano-migrate` CLI and the umbrella's
`dotnet nano migrate` reference it, so there is one implementation. All CLI projects live under
`tools/`; `src/` holds only the SDK package and build tasks.

The migrate engine is a **library** (`NanoMigrate.Core`) with no `AnsiConsole`/`Console`
dependency, so it is unit-testable and packable on its own; both the standalone `nano-migrate`
CLI and the umbrella's `dotnet nano migrate` command are thin presentation layers over it.

## Built-in vs external (initial set)

| Command | Kind | Backed by |
|---|---|---|
| `dotnet nano migrate` | built-in | `NanoMigrate.Core` (this ecosystem) |
| `dotnet nano deploy` / `monitor` / `devices` | built-in | build `Deploy` target + device comms (doc 05) |
| `dotnet nano flash` | external | `nanoff` (prebuilt release, version-pinned) |

## Shipping with the SDK

- Installed as a **local** tool via `dotnet-tools.json` (CI pins the version with the rest of the
  toolchain) or globally for interactive use.
- External prebuilt tools (`nanoff`, …) are **not** rebuilt here — the umbrella deploys/wraps the
  released binaries, pinned by version, so the nanoFramework toolchain is one install + one
  uniform CLI.
