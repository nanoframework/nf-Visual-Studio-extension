# SDK-style migration — backlog (spec deliverables filed as issues)

These are the deliverables from the specification set (docs 00–10) that the migration plan was not
tracking. They were filed as **child issues of epic [nanoFramework/Home#1784](https://github.com/nanoframework/Home/issues/1784)**
on 2026-06-16; each issue follows the Home repo's Feature request template, links back to the epic,
and links to its source spec document. The canonical text now lives in the issues — this file is the
index.

Coverage matrix (docs 00–10 mapped to done/tracked/missing): see
[EXECUTION-PLAN.md → Spec coverage](EXECUTION-PLAN.md).

| Issue | Deliverable | Spec | Phase |
|---|---|---|---|
| [Home#1787](https://github.com/nanoframework/Home/issues/1787) | Build-time ABI checksum gate (`NanoChecksumCheck` / `NanoValidateChecksum`) | [04 §4.5](04-mdp-native-integration.md), [10 §10.3](10-tooling-specs.md) (C4) | 1 |
| [Home#1788](https://github.com/nanoframework/Home/issues/1788) | `NanoDeploy` task + `Deploy` target + `dotnet nano deploy`/`monitor`/`devices` | [05 §5.3](05-cli-experience.md), [10](10-tooling-specs.md) (C5) | 3 |
| [Home#1789](https://github.com/nanoframework/Home/issues/1789) | Hot inner-loop: `dotnet watch` / `dotnet nano watch` redeploy | [05 §5.4](05-cli-experience.md) | 3 |
| [Home#1790](https://github.com/nanoframework/Home/issues/1790) | Device selection: resolution order + `nanoFramework.config.json` | [05 §5.5](05-cli-experience.md) | 3 |
| [Home#1791](https://github.com/nanoframework/Home/issues/1791) | Fast-fail on the command line when no device is connected | [05 §5.6](05-cli-experience.md) | 3 |
| [Home#1792](https://github.com/nanoframework/Home/issues/1792) | Fleet CI / Azure Pipelines template rewriter | [07 §7.6](07-library-migration.md), [10](10-tooling-specs.md) (C8) | 4 |
| [Home#1793](https://github.com/nanoframework/Home/issues/1793) | Fleet auto-PR renderer | [PR-INSTRUCTIONS.md](PR-INSTRUCTIONS.md) | 4 |

The cross-reference comment listing all seven on the epic:
[Home#1784 (comment)](https://github.com/nanoframework/Home/issues/1784#issuecomment-4724148120).
