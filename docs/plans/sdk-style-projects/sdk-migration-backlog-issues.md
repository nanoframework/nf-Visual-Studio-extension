<!--
  PASTE-READY backlog issue stubs for nanoFramework/Home — the spec deliverables (docs 00–10)
  that the SDK-style migration plan was NOT tracking. Each stub is a child of the epic
  nanoFramework/Home#1784 ([Epic] SDK-style MSBuild project system migration).

  Filing: open each as a "Feature request" (or a team "Chore/Task") in nanoFramework/Home,
  title from the heading, body from the stub. Suggested labels: area-Config-and-Build
  (+ area-Tools-and-Utilities for the CLI/fleet ones). Reference the epic and the spec doc.
  These are scoped intentionally small — productization of already-specced behavior, not
  feasibility. Cross-checked against the shipped SDK + tools on 2026-06-16.

  Status key for "where the gap is": MISSING = not built and not previously tracked.
-->

# SDK-style migration — backlog (spec deliverables not yet tracked)

Parent epic: **nanoFramework/Home#1784**. Spec set:
`nf-Visual-Studio-extension/docs/plans/sdk-style-projects/`. Coverage matrix:
`EXECUTION-PLAN.md → Spec coverage (docs 00–10)`.

---

## 1. SDK build-time ABI gate: `NanoValidateChecksum` / `NanoChecksumCheck` task

**Spec:** doc 04 §4.5, doc 10 §10.1 (component **C4**, listed in the Phase-1 MVS) + §10.3 (signature).
**Gap:** MISSING — not in the shipped `nanoFramework.Mdp.targets`; the PE native-methods checksum is
emitted but never validated at build time. (Distinct from extension deploy pre-check B1.)

**Build:**
- A `NanoChecksumCheck` MSBuild task (signature in doc 10 §10.3): inputs `PeChecksum`,
  `TargetAbiManifest`; output `Result`.
- A `NanoValidateChecksum` target, **opt-in** via `$(NanoValidateChecksum)=true`, that fails the build
  with an actionable message when a PE is built against a mismatched firmware exported-ABI manifest.

**Acceptance (doc 10 §10.6 C4):** with the gate on + a target ABI manifest, a mismatched build fails
with a clear message; a matching build passes; with the gate off, the build proceeds unchanged.
**Scope/phase:** Phase 1 (MVS). Managed-only; no device required.

---

## 2. `NanoDeploy` task + `Deploy` MSBuild target (+ `dotnet nano deploy`)

**Spec:** doc 05 §5.3 (Path A target + Path B CLI), doc 10 §10.1 (component **C5**) + §10.3 (signature).
**Gap:** MISSING — no SDK `Deploy` target; `dotnet nano deploy` ships as a not-implemented placeholder.

**Build:**
- A `NanoDeploy` task (doc 10 §10.3): `Assemblies` (the `.pe` set), `SerialPort`, `NanoffPath`,
  `RebootAfter`; wraps the `nanoff` push.
- A `Deploy` target so `dotnet build -t:Deploy` flashes the built app (doc 05 §5.3 Path A).
- Wire `dotnet nano deploy` (umbrella) to build → `NanoDeploy` (doc 05 §5.3 Path B, the preferred UX).

**Acceptance (doc 10 §10.6 C5/C6):** `dotnet nano deploy` flashes the app and it runs on a device.
**Scope/phase:** Phase 3 (device-side). Depends on `nanoff` resolution (already in the umbrella).
**Related:** the `dotnet nano monitor` / `devices` placeholders (doc 05 §5.3) are the companion verbs —
fold in here or track as a small follow-up.

---

## 3. Hot inner-loop: `dotnet watch` / `dotnet nano watch`

**Spec:** doc 05 §5.4.
**Gap:** MISSING — no watch flow.

**Build:** a `dotnet watch -t:Deploy` / `dotnet nano watch` loop that keeps a single serial session
open and re-deploys only changed `.pe` on source change.

**Acceptance:** editing a source file re-deploys without a full redeploy/reconnect cycle; one serial
session is reused. **Scope/phase:** Phase 3, after deploy (#2). Device-side.

---

## 4. Device selection: resolution order + `nanoFramework.config.json`

**Spec:** doc 05 §5.5.
**Gap:** MISSING — no port/device resolution logic, no repo-level config file.

**Build:** a documented resolution order (`--port` > project property > `nanoFramework.config.json`
> auto-select / interactive prompt) shared by deploy/monitor/watch, plus reading a repo-level
`nanoFramework.config.json` for the default device/port.

**Acceptance:** with multiple devices, the documented precedence picks the right one; a repo config sets
the default without a flag. **Scope/phase:** Phase 3; prerequisite for a clean deploy/watch UX (#2/#3).

---

## 5. CI fast-fail on missing device

**Spec:** doc 05 §5.6.
**Gap:** MISSING — a deploy/monitor with no device should error fast, not hang.

**Build:** in non-interactive/CI contexts, deploy/monitor/watch detect "no device" and exit non-zero
promptly with a clear message instead of blocking on a prompt or a connect timeout.

**Acceptance:** `dotnet nano deploy` in CI with no device attached fails fast with an actionable error.
**Scope/phase:** Phase 3; small, rides along with #2/#4.

---

## 6. Fleet CI / Azure-pipeline template rewriter

**Spec:** doc 07 §7.6, doc 10 §10.1 (component **C8** — the "+ CI template rewriter" half).
**Gap:** MISSING — the `.nfproj`→SDK converter (`dotnet nano migrate`) is done; the per-repo
Azure-Pipelines / CI template rewrite for the `lib-*` fleet is not built.

**Build:** a rewriter that updates each migrated repo's CI (Azure DevOps pipeline, `nuget restore` +
`MSBuild.exe` → `dotnet build`/`pack`/`test`) as part of (or alongside) the `fleet` command.

**Acceptance:** a migrated `lib-*` repo's pipeline builds/packs/tests the SDK-style project on CI
without manual edits. **Scope/phase:** Phase 4 (fleet). Gated on the Phase-1 `lib-*` pilot.

---

## 7. Fleet auto-PR renderer

**Spec:** `PR-INSTRUCTIONS.md` → "Fleet-upgrader usage (auto-created PRs)" renderer contract.
**Gap:** MISSING — the contract is documented; the reference renderer is not built.

**Build:** a renderer next to the converter (`tools/migrate`) that takes `(repo, homeIssue, types[])`,
fills the org PR-template slots per the contract (Description / Motivation / How-tested / Types /
Checklist; no AI attribution; never issue refs in the title; base `move-to-sdk`/`develop`), checks for
an existing open PR (idempotency), respects rate limits, and shells out to the `gh pr create` recipe.

**Acceptance:** running it over the pilot opens compliant draft PRs leaf-first, idempotently (re-run
opens no duplicates). **Scope/phase:** Phase 4 (fleet); pairs with #6.
