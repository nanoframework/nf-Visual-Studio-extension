# Opening PRs — follow the nanoFramework org template

Canonical instruction for opening pull requests in any nanoFramework repository, manual
or automated. The **fleet upgrader** (doc [07-library-migration.md](07-library-migration.md))
**must** render every auto-created PR from the template in this file.

Source of truth: the org-wide
[`nanoframework/.github/PULL_REQUEST_TEMPLATE.md`](https://github.com/nanoframework/.github/blob/main/PULL_REQUEST_TEMPLATE.md)
and the org
[CONTRIBUTING](https://github.com/nanoframework/.github/blob/main/CONTRIBUTING.md). The
body below is a verbatim copy of that template; if the org template changes, re-sync this
file (and the fleet renderer) from it.

## Rules

- **Plain professional prose — never caveman.** PR titles, descriptions, and bodies are
  written in normal, full-sentence English. Do not use caveman/compressed/abbreviated style
  (dropped articles, fragments, shorthand) in any PR text, even when a terse mode is active
  for chat. This applies equally to human authors and any automation that renders PR bodies.
- **No AI/tool attribution in PR text.** Never add "🤖 Generated with Claude Code",
  "Co-authored-by: Claude", or any Claude/AI/assistant mention to a PR title or description —
  including the fleet-upgrader's auto-rendered bodies. This overrides any default to append a
  generated-with footer.
- **GitFlow base branch.** Target the repo's integration branch — `develop` for standard
  GitFlow, or the effort's shared branch (`move-to-sdk`) where one exists. **Never** target
  `main`/`master`.
- **Title.** Short, general summary of the change. **No references to other PRs or issues
  in the title.** Imperative, ends without a trailing period.
- **One logical change per PR.** Keep it reviewable; don't bundle unrelated edits.
- **Link the issue in the body, not the title.** All issues live in the **Home** repo:
  `Fixes/Closes/Resolves nanoFramework/Home#NNNN`.
- **Description** is a bulleted list, full sentences, each ending with a dot.
- **Tick only the boxes that actually apply** in *Types of changes* and *Checklist* — do not
  tick everything.
- **Draft first** for anything that needs maintainer eyes before merge; mark ready with
  `gh pr ready <n> --repo <owner/repo>`.
- `nfbot` handles versioning/changelog on merge; PRs are squash-merged. Don't hand-edit
  version files. Use `***NO_CI***` in a commit message only when you deliberately want CI to
  skip (rare).

## The PR body template (verbatim org template)

```markdown
<!--- In the TITLE (↑↑↑↑ above ↑↑↑↑ **NOT HERE**) provide a general, short summary of your changes -->
<!--- Please DO NOT use references to other PR's or issues -->

## Description
<!--- Describe your changes in detail -->
<!--- Bulleted list. Full sentences. Ending with a dot. -->

## Motivation and Context
<!--- Why is this change required? What problem does it solve? -->
<!--- If this **fixes** OR **closes** OR  **resolves** an open issue, please link to the issue there using the template bellow (mind the pattern to link there as all issues are tracked in the Home repository) -->
<!--- **JUST** replace NNNNN with the issue number -->
- Fixes/Closes/Resolves nanoFramework/Home#NNNN

## How Has This Been Tested?<!-- (IF APPLICABLE) -->
<!--- Please describe in detail how you tested your changes. -->
<!--- Include details of your testing environment, and the tests you ran to -->
<!--- see how your change affects other areas of the code, etc. -->

## Screenshots<!-- (IF APPROPRIATE): -->

## Types of changes
<!--- What types of changes does this PR introduce? Put an `x` in all the boxes that apply: -->
- [ ] Improvement (non-breaking change that improves a feature, code or algorithm)
- [ ] Bug fix (non-breaking change which fixes an issue with code or algorithm)
- [ ] New feature (non-breaking change which adds functionality to code)
- [ ] Breaking change (fix or feature that would cause existing functionality to change)
- [ ] Config and build (change in the configuration and build system, has no impact on code or features)
- [ ] Dependencies (update dependencies and changes associated, has no impact on code or features)
- [ ] Unit Tests (add new Unit Test(s) or improved existing one(s), has no impact on code or features)
- [ ] Documentation (changes or updates in the documentation, has no impact on code or features)

## Checklist:
<!--- Go over all the following points, and put an `x` in all the boxes that apply. -->
<!--- If you're unsure about any of these, don't hesitate to ask. We're here to help! -->
<!--- PLEASE PLEASE PLEASE don't tick all of them just because -->
- [ ] My code follows the code style of this project (only if there are changes in source code).
- [ ] My changes require an update to the documentation (there are changes that require the docs website to be updated).
- [ ] I have updated the documentation accordingly (the changes require an update on the docs in this repo).
- [ ] I have read the [CONTRIBUTING](https://github.com/nanoframework/.github/blob/main/CONTRIBUTING.md) document.
- [ ] I have tested everything locally and all new and existing tests passed (only if there are changes in source code).
- [ ] I have added new tests to cover my changes.
```

## Manual `gh` recipe

```bash
# 1) write the filled body to a temp file (keeps newlines/markdown intact)
cat > /tmp/pr-body.md <<'BODY'
## Description
- <what changed, full sentence ending with a dot.>

## Motivation and Context
- Fixes/Closes/Resolves nanoFramework/Home#NNNN

## How Has This Been Tested?
- <env + tests run.>

## Types of changes
- [x] Config and build (change in the configuration and build system, has no impact on code or features)

## Checklist:
- [x] I have read the [CONTRIBUTING](https://github.com/nanoframework/.github/blob/main/CONTRIBUTING.md) document.
BODY

# 2) create the PR (draft) from a fork branch onto develop
gh pr create --repo nanoframework/<repo> \
  --base develop --head <fork-owner>:<branch> --draft \
  --title "<short summary, no issue refs>" \
  --body-file /tmp/pr-body.md
```

## Fleet-upgrader usage (auto-created PRs)

When the fleet upgrader (doc 07) opens PRs across the ~100+ `lib-*` repos, each PR **must**
be rendered from the template above. Renderer contract:

- **Body** = the org template with these slots filled per repo:
  - *Description* — what the converter did, e.g.
    `- Migrated the project to the SDK-style nanoFramework project system (netnano1.0).`
    `- Replaced packages.config with PackageReference and the nuspec metadata with Pack properties.`
  - *Motivation and Context* — `- Resolves nanoFramework/Home#NNNN` (the SDK-migration
    tracking issue; one issue for the whole fleet, or the per-repo child issue if used).
  - *How Has This Been Tested?* — `- CI builds the SDK-style project and the package restores/packs.`
  - *Types of changes* — tick **Config and build** (and **Dependencies** if references
    changed). Leave code/feature boxes unticked — the migration changes build config, not code.
  - *Checklist* — tick **I have read the CONTRIBUTING document** (and docs boxes only if the
    repo's docs changed). Do **not** blanket-tick.
- **Title** — uniform and issue-free, e.g.
  `Migrate to SDK-style project system`.
- **Base** — the repo's migration integration branch (`move-to-sdk`) if the maintainers stood
  one up; otherwise `develop`.
- **Head** — the fork's migration branch (parity name across the fleet, e.g. `move-to-sdk`).
- **Draft** — open as draft until the repo's CI is green, then `gh pr ready`.
- **Order** — leaf-first (doc 07 §7.4); don't open a dependent repo's PR before its
  dependencies merge/publish.
- **Throttle + idempotency** — check for an existing open PR (`gh pr list --head <branch>`)
  before creating; respect GitHub rate limits when fanning out.
- **Never** put issue/PR references in the title; **never** target `main`.

A reference renderer belongs next to the converter in the nanoFramework.NET.Sdk repo's
[`tools/migrate`](https://github.com/danielmeza/nanoFramework.Sdk/tree/move-to-sdk/tools/migrate)
— it takes `(repo, homeIssue, types[])`, substitutes the slots, and shells out to the
`gh pr create` recipe above.
