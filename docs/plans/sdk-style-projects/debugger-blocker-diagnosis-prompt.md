# Prompt — Diagnose **and** solve the nanoFramework SDK-style debugger blocker

> **OUTCOME — blocker SOLVED ✅ (this prompt is retained as the method record).** The
> investigation + A+C POC it describes were executed: an SDK-style project deploys and
> debugs (F5 + source breakpoints) on a real ESP32_S3_OCTAL with the AD7 engine
> unchanged. The gate was **build-targets composition + the `NanoCSharpProject`
> capability**, exactly as hypothesized; the engine was orthogonal. Results in
> [poc-findings/RESULTS.md](poc-findings/RESULTS.md), full decision record in
> [poc-findings/DEBUGGING-LOG.md](poc-findings/DEBUGGING-LOG.md).

> Paste this into an agent (e.g. Claude Code) running in a workspace where the
> nanoFramework repos are cloned. It is a **read-only investigation**: inspect
> code and produce a findings + solution-options report. Do not modify any repo.

---

## Context

We want to move nanoFramework projects from the legacy non-SDK `.nfproj` to an
**SDK-style** MSBuild project. The managed build/pack/test paths are believed
portable. The maintainer (José Simões) has stated the blocker is the **Visual
Studio debugger**: discussion #1635 says SDK-style isn't viable right now "because
of the debugger," and moving to "the new debugger API" would be "an immense load
of work." Prior art: nf-Visual-Studio-extension PR #889 added a
`DeployToNanoDevice` reference-metadata check in the F5 deploy crawler.

Your job has two halves:
1. **Diagnose** — find, in the real code, exactly what couples the debugger and
   F5 deployment to the legacy project system.
2. **Solve** — analyze the feasible alternatives to unblock SDK-style *properly*,
   with effort/risk/VS-dependency for each, and recommend a path.

Replace assumptions with `file:line` facts throughout.

## Preliminary assessment (from a first read of `nf-Visual-Studio-extension`)

This is a head start from a shallow read of the `develop` branch
(`vs-extension.shared/` + `VisualStudio.Extension-2022/`). **Verify each point
against the local checkout — line numbers and branch may differ — and correct
anything wrong.** The picture is more decomposable than "the debugger is
unportable," which is the main thing to confirm or refute.

1. **The VS project system is already CPS, not legacy MPF/flavor.**
   - `vs-extension.shared/ProjectSystem/NanoCSharpProjectUnconfigured.cs` /
     `NanoCSharpProjectConfigured.cs` use `Microsoft.VisualStudio.ProjectSystem`
     (`UnconfiguredProject`, `ConfiguredProject`, `[AppliesTo(UniqueCapability)]`),
     keyed off the `.nfproj` extension and a `NanoCSharpProject` capability.
   - `VisualStudio.Extension-2022/Targets/NFProjectSystem.targets` declares
     `<ProjectCapability Include="CPS" />` plus `NanoCSharpProject`,
     `AssemblyReferences;ProjectReferences;SharedProjectReferences`,
     `ProjectConfigurationsDeclaredAsItems`, `DeclaredSourceItems;UserSourceItems`,
     `CSharp`.

2. **Deploy and debug-launch are CPS providers, not flavor code.**
   - `vs-extension.shared/DeployProvider/DeployProvider.cs` —
     `[Export(typeof(IDeployProvider))] [AppliesTo(NanoCSharpProject…)]`. It reads
     the **evaluated MSBuild project** (reflects the private `MSBuildProject` →
     `Microsoft.Build.Evaluation.Project`, reads `Items`/`Properties`, e.g.
     `OutputType`), then uses `ReferenceCrawler` (over CPS `IProjectService`) to
     collect referenced assemblies, maps `.dll`/`.exe` → `.pe` by string replace,
     and pushes the PEs. It reads project **evaluation**, not build output — the
     deviation Frank Robijn flagged in the Discord thread / PR #889.
   - `vs-extension.shared/DebugLauncher/NanoDebuggerLaunchProvider.cs` —
     `[ExportDebugger("NanoDebugger")] [AppliesTo(NanoCSharpProject…)]`, a CPS
     `DebugLaunchProviderBase`. `QueryDebugTargetsAsync` builds a
     `/load:<pe>` command line via the same `ReferenceCrawler` and returns a
     `DebugLaunchSettings` whose `LaunchDebugEngineGuid = CorDebug.EngineGuid`,
     `PortSupplierGuid = DebugPortSupplier.PortSupplierGuid`, `Project = VsHierarchy`.

3. **The genuinely legacy pieces look like three separable things:**
   - **(a) Build-targets composition.**
     `VisualStudio.Extension-2022/Targets/NFProjectSystem.CSharp.targets` directly
     imports the legacy chain (`<Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />`,
     and a hack around `Microsoft.Common.CurrentVersion.targets`), then
     `NFProjectSystem.MDP.targets`. This is what collides with
     `<Project Sdk="Microsoft.NET.Sdk">` (the double-import MSB4011 in #1635). The
     targets are authored to *be* the build system, not to *compose over* the .NET
     SDK.
   - **(b) Project-type registration.**
     `VisualStudio.Extension-2022/NanoFrameworkPackage.cs` —
     `[assembly: ProjectTypeRegistration(projectTypeGuid: "11A8DD76-328B-46DF-9F39-F559912D0360", …)]`
     binds the `.nfproj` extension + project-type GUID to the nano project system.
     For SDK-style you'd compose over the .NET SDK's CPS project type and inject
     the `NanoCSharpProject` capability + nano targets, rather than own the type.
   - **(c) The debug engine is AD7, not Concord.**
     `vs-extension.shared/CorDebug/CorDebug.cs` uses
     `Microsoft.VisualStudio.Debugger.Interop` (AD7: `IDebugEngine2`,
     `IDebugProgram2`, `IDebugPort2`, `IDebugRemoteCorDebug`, …), registered via a
     custom `ProvideDebugEngineAttribute`. "The new debugger API" almost certainly
     means **Concord** (`Microsoft.VisualStudio.Debugger.Engine` / DkmDebugger).
     The AD7→Concord rewrite is the large effort — **but note it is launched by
     GUID through CPS `DebugLaunchProviderBase` and does not inspect the project
     file format**, so it may be *orthogonal* to whether the project is SDK-style.

4. **Key implication to test.** If (a) targets-composition and (b) registration
   are solved, the existing CPS deploy + AD7 launch might already attach to
   SDK-style projects — meaning the AD7→Concord rewrite (c) is a *separate*
   modernization that becomes mandatory only if a future VS drops AD7 engine
   hosting. **Confirm or refute this** — it determines whether the blocker is
   "large but incremental" or "blocked on a full engine rewrite."

## Scope

- **Primary repo:** `nf-Visual-Studio-extension` (`vs-extension.shared/`,
  `VisualStudio.Extension-2022/`, `Tools.BuildTasks-2022/`).
- **Secondary, only if referenced:** `nf-debugger`, `metadata-processor`,
  `nf-VSCodeExtension`.
- Ignore firmware and class libraries.

## Method

Work in passes; record findings before moving on. Passes 1–4 confirm the
diagnosis; **Pass 5 is the solution analysis the maintainers actually want.**

### Pass 1 — Verify the integration surface
Confirm/correct the preliminary assessment with exact `file:line`. Produce a table
**extension point → implementing type → file:line → purpose** covering: the CPS
project-system classes; the `ProjectCapability` set and where declared; the
`IDeployProvider`; the `DebugLaunchProviderBase`; the AD7 engine
(`CorDebug.cs`) + port supplier; the engine/port-supplier registration attributes;
the `ProjectTypeRegistration` + project-type GUID; the targets import chain.

### Pass 2 — Trace the F5 flow end to end
From the debug-launch / deploy entry point, trace the call path on F5. For each
step record `file:line` and whether it reads **MSBuild build output** vs **parses /
evaluates the project** (`MSBuildProject`, `IProjectService`, project references).
Identify every place that would behave differently — or throw — for an SDK-style
CPS project vs the current `.nfproj`.

### Pass 3 — Classify each coupling
For every coupling: **type** (`targets-composition` / `project-type-registration`
/ `project-evaluation-read` / `deploy-provider` / `debug-launch` / `debug-engine`
/ `capability-declaration`), **blocks SDK-style?** (yes/no/partial + why),
**CPS/SDK equivalent**, **effort** (S/M/L + rationale).

### Pass 4 — Test the hypotheses
With evidence, support or refute:
- **H1:** the F5 deploy/launch reads project *evaluation* (not build output).
- **H2:** the debug engine is AD7 and is launched by GUID independently of the
  project file format (i.e. SDK-style does not, by itself, break the engine).
- **H3:** the concrete blockers to an SDK-style project *loading and debugging*
  today are (a) targets-composition and (b) project-type registration — not (c)
  the engine.
List anything already project-format-agnostic and reusable as-is.

### Pass 5 — Feasible solution alternatives (the deliverable)
Enumerate and evaluate concrete ways to unblock SDK-style **properly**. For each:
mechanism, what code/targets/registration it touches (`file:line`), effort,
risk, VS-version dependency, whether it preserves debugging, and whether it's a
stepping-stone or an end state. Seed the analysis with at least these — validate
each against the code, discard any the code rules out, and **add others you find**:

- **A. Compose targets over `Microsoft.NET.Sdk` (author `nanoFramework.Sdk`).**
  Make the nano targets import/compose over the .NET SDK instead of
  `Microsoft.CSharp.targets`; SDK owns the import chain so the #1635 double-import
  can't occur. Re-host MDP (`NFProjectSystem.MDP.targets`) as ordered targets.
  Assess: does this alone let an SDK-style project build + load under CPS?
- **B. Interim `Microsoft.NET.Sdk` + imported nano targets (the #1635 shape).**
  Resolve only the double-import so build/pack/test work now via CLI, debugging
  unchanged on the legacy path. Lowest effort; is it stable enough to ship as
  "experimental"?
- **C. Re-register the project type by composition.** Inject the
  `NanoCSharpProject` capability + nano CPS pieces onto SDK-style projects
  (`.csproj`/SDK-style `.nfproj`) instead of owning the project type via the
  `11A8DD76` GUID. What exactly must change in `ProjectTypeRegistration` /
  capability injection?
- **D. Keep the AD7 engine; verify it attaches to an SDK-style CPS project.**
  Since launch is by GUID via `DebugLaunchProviderBase`, test whether A+C are
  sufficient for F5 debugging without touching the engine. (If yes, this is the
  cheapest route to "SDK-style + debugging.")
- **E. Rewrite the debug engine AD7 → Concord (DkmDebugger).** The "immense work."
  Scope it; identify which `CorDebug/*` types map to Dkm components; flag whether
  a future VS version forces this regardless (AD7 deprecation).
- **F. Decouple deploy/launch from project evaluation.** Replace the reflected
  `MSBuildProject` read + `.dll`→`.pe` string-mapping with reading MSBuild **build
  output** / well-known items, making the crawler project-format-agnostic
  (addresses Frank Robijn's deviation and PR #889's direction).

Then produce a **recommendation**: the smallest ordered sequence that delivers
SDK-style build/pack/test first and SDK-style debugging as soon as feasible,
explicitly separating what's blocked on a VS-version/Concord dependency from what
isn't.

## Output — write `debugger-blocker-findings.md`

```
# nanoFramework debugger blocker — findings & solution options

## 1. Summary
- Is the debugger the blocker per the code, and is it decomposable? (H1/H2/H3 verdicts)
- The cheapest route to SDK-style + debugging, and the big rocks.

## 2. Integration surface (Pass 1 table)
| Extension point | Implementing type | file:line | Purpose |

## 3. F5 flow trace (Pass 2)
Ordered steps with file:line; "reads build output" vs "parses/evaluates project".

## 4. Coupling inventory (Pass 3 table)
| Coupling | Type | file:line | Blocks SDK-style? | CPS/SDK equivalent | Effort |

## 5. Hypotheses (Pass 4)
H1 / H2 / H3 — supported/refuted + evidence. Reusable-as-is list.

## 6. Solution alternatives (Pass 5 matrix)
| Option | Mechanism (file:line touched) | Effort | Risk | VS-version dep | Keeps debugging? | Stepping-stone or end state |
(rows A–F + any you add)

## 7. Recommendation
Ordered, smallest-first sequence; what ships now vs what's gated on VS/Concord.

## 8. Open questions / couldn't determine
Anything needing a maintainer or a runtime experiment (e.g. "does F5 attach to an
SDK-style CPS project with A+C applied?").
```

## References & links

**nanoFramework**
- Discussion #1635 (the blocker statement): https://github.com/orgs/nanoframework/discussions/1635
- Related issue #1067: https://github.com/nanoframework/Home/issues/1067
- PR #889 (`DeployToNanoDevice` in the deploy crawler): https://github.com/nanoframework/nf-Visual-Studio-extension/pull/889
- VS extension repo: https://github.com/nanoframework/nf-Visual-Studio-extension
- Debugger library (wire protocol): https://github.com/nanoframework/nf-debugger
- Key files to read first: `vs-extension.shared/DeployProvider/DeployProvider.cs`,
  `vs-extension.shared/DebugLauncher/NanoDebuggerLaunchProvider.cs`,
  `vs-extension.shared/ProjectSystem/NanoCSharpProject{Unconfigured,Configured}.cs`,
  `vs-extension.shared/Utilities/ReferenceCrawler.cs`,
  `vs-extension.shared/CorDebug/CorDebug.cs`,
  `VisualStudio.Extension-2022/Targets/NFProjectSystem*.targets`,
  `VisualStudio.Extension-2022/NanoFrameworkPackage.cs`.

**VS project system (CPS)**
- CPS docs: https://github.com/microsoft/VSProjectSystem/blob/master/doc/Index.md
- Project capabilities: https://github.com/microsoft/VSProjectSystem/blob/master/doc/overview/about_project_capabilities.md
- Extensibility (deploy/debug providers): https://github.com/microsoft/VSProjectSystem/blob/master/doc/extensibility/IDeployProvider.md
- `DebugLaunchProviderBase` / debuggers: https://github.com/microsoft/VSProjectSystem/blob/master/doc/overview/debuggers.md
- CPS samples: https://github.com/microsoft/VSProjectSystem-CustomProjectSystem
- .NET Project System (real-world SDK composition reference): https://github.com/dotnet/project-system

**VS debugger APIs**
- AD7 (current engine — `Microsoft.VisualStudio.Debugger.Interop`): https://learn.microsoft.com/en-us/visualstudio/extensibility/debugger/visual-studio-debugger-extensibility
- Concord ("the new debugger API"): https://learn.microsoft.com/en-us/visualstudio/extensibility/debugger/concord-extensibility-samples
- Concord samples repo: https://github.com/microsoft/ConcordExtensibilitySamples
- AD7 engine sample (IDebugEngine2 et al.): https://github.com/microsoft/VSSDK-Extensibility-Samples

**MSBuild / SDK**
- MSBuild project SDKs (how to author one): https://learn.microsoft.com/en-us/visualstudio/msbuild/how-to-use-project-sdk
- `netnano1.0` is a recognized TFM: https://learn.microsoft.com/en-us/dotnet/standard/frameworks#supported-target-frameworks

## Rules

- **Read-only.** Do not edit, build, or run anything that changes state.
- **Cite file:line** for every concrete claim; label inference as "(inference)".
- Verify the preliminary assessment against the local checkout and correct it.
- Prefer breadth (find all couplings) over deep-reading any one file.
- If an expected repo/file is absent, say so and proceed.
- Tables are the deliverable; keep prose tight.
