# Contributing-compliance rules for migration PRs

These rules make a migration land as a contribution the nanoFramework project
will accept. They are distilled from the official contributing docs:

- Contribution workflow — <https://docs.nanoframework.net/content/contributing/contributing-workflow.html>
- C# Coding Style — <https://docs.nanoframework.net/content/contributing/cs-coding-style.html>
- Labels — <https://docs.nanoframework.net/content/contributing/labels.html>
- Contributor License Agreement — <https://docs.nanoframework.net/content/contributing/cla.html>

Treat this as the checklist the migration must satisfy before a PR goes up. The
`nano-migrate` tool enforces the parts it can (commit format, sign-off, branch
naming); the rest is on the contributor.

## Contents

- [Before you start](#before-you-start)
- [Branching](#branching)
- [Scope of the change](#scope-of-the-change)
- [Commits](#commits)
- [Build and test clean](#build-and-test-clean)
- [Opening the PR](#opening-the-pr)
- [C# coding style (only if you touch .cs)](#c-coding-style-only-if-you-touch-cs)
- [What the tool does vs. what you do](#what-the-tool-does-vs-what-you-do)

## Before you start

- **Sign the CLA.** Every contribution (code, config, docs — including a project
  migration) requires a signed Contributor License Agreement. Without it the PR
  cannot be merged.
- **Use your real name.** Commits must use a real name; anonymous or pseudonymous
  contributions are not accepted. Set it once:
  `git config --global user.name "Your Real Name"` and a real
  `git config --global user.email`.
- **File or find an issue for non-trivial work.** A fleet-wide project-system
  migration is not trivial — open a tracking issue (or reuse one) and get team
  agreement before opening PRs. Trivial one-off fixes can skip this.
- **Work from a fork.** The project uses a fork-based workflow: fork the repo,
  clone your fork, add `upstream` pointing at `nanoframework/<repo>`, and sync
  `main` from upstream before branching.

## Branching

- Branch off **`main`**: `git checkout -b <name> main`.
- Name the branch for its intent, e.g. `sdk-migration` or `issue-123`.
- **Never** start a branch name with `develop` — it collides with upstream
  `develop-*` branches. (`nano-migrate fleet` refuses such names.)

## Scope of the change

- This migration is a **structural** change to the project system, not a style
  change. That is allowed. **Do not** bundle in code reformatting or style-only
  edits — the project explicitly does **not** accept style-only PRs and asks
  contributors to preserve the existing style of any file they touch.
- **Do not modify `.cs` source** as part of the migration. `nano-migrate` only
  rewrites `.nfproj`, `packages.config`, and `.nuspec`; keep it that way. If a
  repo genuinely needs a code change, that is a separate PR with its own issue.
- **Keep `.editorconfig`.** Each repo carries an `.editorconfig` (and
  `spelling_exclusion.dic`) from the project template that encodes the style
  rules. Don't delete or reformat around it. If a repo is missing it, copy the
  current one from the `.github` template repo rather than inventing one.
- **Generated project XML**: the rule for non-code files is consistency. The
  SDK-style project the tool emits uses standard two-space MSBuild indentation,
  which is the broadly-accepted convention for a brand-new SDK-style file.
- **Versioning**: the tool does not fold `<version>` from `.nuspec`. Leave the
  repo's existing versioning (typically Nerdbank.GitVersioning via `version.json`)
  in place; don't hard-code a version into the project.

## Commits

Format every commit like this (the tool does this automatically for fleet
commits):

```text
Summarize the change in 50 characters or less

Body explaining what changed and why, wrapped at 72 columns. For a
migration: what was converted and that there are no functional code
changes.

Fix #<issue>

Signed-off-by: Your Real Name <you@example.com>
```

- Summary line **≤ 50 characters**, body wrapped at **72**.
- Reference the tracking issue with a `Fix #<n>` trailer when there is one
  (`nano-migrate ... --issue <n>`).
- Include a **`Signed-off-by`** line (`git commit -s`). The tool signs off by
  default; this requires a configured real name/email.
- **Factor commits sensibly** — not one giant commit of unrelated things, not a
  swarm of tiny ones. Rebase away accidental whitespace/reformatting before the
  PR. A clean migration is usually a single commit per repo.

## Build and test clean

A migration is not done until the repo **builds clean and all tests pass**:

- `dotnet build` (or the repo's build) must restore and build with no errors.
- Run the repo's unit tests; they must pass, including any that exercise the
  migrated project.
- Rebase on `upstream/main` if upstream moved while you worked, then re-build and
  re-test before pushing.

Do not open (or mark ready) a PR whose build is red.

## Opening the PR

- Push the branch to **your fork**, then open a PR against the upstream repo's
  **`main`** branch. The tool stops at the local commit — it does not push or
  open PRs; do that with the maintainers' normal flow.
- **Follow the PR template** that GitHub shows; it doubles as a submission
  checklist. Fill in every section.
- A migration PR is typically **`area-Config-and-Build`** (project system /
  build). For org-wide tooling/coordination, `area-Infrastructure-and-Organization`
  may also apply. Maintainers manage the lifecycle labels (`code review`,
  `do not merge`, `merge OK`); you generally don't set those yourself.
- It's fine to open a **`[WIP]`** PR early to start feedback, and it's fine to be
  asked to **squash** commits before merge.
- Expect review comments — they are normal and not a rejection. Resolve feedback
  and keep the branch building green.

## C# coding style (only if you touch .cs)

The migration should not change `.cs` files. But if you must (e.g. you drop a
hand-written `Properties/AssemblyInfo.cs` and need to re-express a custom
assembly attribute, or add a small fix), the change must follow the project's C#
style. Key rules:

- **Visual Studio defaults**, Allman braces (each brace on its own line), braces
  even on single-line blocks, **4 spaces** indentation, no tabs.
- `_camelCase` for private/internal fields; `s_` for static, `t_` for thread
  static; `static readonly` (in that order) where possible.
- Always state visibility, visibility first (`private string _foo`, `public
  abstract`). Avoid `this.` unless required.
- `using` directives at the top, outside the namespace, sorted alphabetically.
- Use language keywords, not BCL type names (`int`, `string`, not `Int32`,
  `String`). Use `var` only when the type is obvious. Use `nameof(...)` over
  string literals. `PascalCase` for constants.
- One empty line maximum between members; a blank line after a closing `}` unless
  another `}` or an `else`/continuation follows; no spurious whitespace.
- **XML doc comments (`///`) on all public members**, including params and
  returns. Prefer documenting exceptions in the comment and keep thrown-exception
  message strings empty to save PE space (except for tooling like `nanoff`).
- **Include the file license header** (the two-line simplified form is fine).
- Prefer full words over abbreviations; for accepted acronyms follow Pascal
  casing (`HttpSomething`, not `HTTPSomething`).
- If an existing file already uses a different convention (e.g. `m_member`), keep
  that file's existing style.

Re-run the project's `.editorconfig` formatting (in VS, `Ctrl+K, Ctrl+D`) before
committing any code change.

## What the tool does vs. what you do

| Requirement                          | `nano-migrate`        | You |
|--------------------------------------|-----------------------|-----|
| Convert project files                | ✅ automatic           |     |
| Commit message format (50/72, Fix #) | ✅ `--issue`           |     |
| `Signed-off-by` sign-off             | ✅ default (needs real name in git config) |     |
| Reject `develop*` branch names       | ✅ guard               |     |
| Leave `.cs` and `.editorconfig` alone| ✅ by design           |     |
| Sign the CLA                         |                       | ✅  |
| Open/track the issue                 |                       | ✅  |
| Build clean + tests pass             |                       | ✅  |
| Push to fork, open PR, fill template |                       | ✅  |
| Apply/respond to review              |                       | ✅  |
