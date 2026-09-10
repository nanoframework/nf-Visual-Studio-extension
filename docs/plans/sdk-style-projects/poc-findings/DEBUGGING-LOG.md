# SDK-style POC — debugging log (decision record)

Running log of problems hit while making the SDK-style nanoFramework project
(`samples/Blink`) **build, deploy, and debug** on a real device, what was tried,
and the outcome. Purpose: stop re-treading the same ground. Newest sections last.

Device under test: **ESP32_S3_OCTAL**, nanoCLR `2.0.0.467`, firmware native
`mscorlib v100.22.0.4` checksum `0x2D5CA905`.

---

## 1. VS "NuGet restore loop" on Blink — FIXED ✅

**Symptom:** VS banner *"A NuGet restore loop has been detected … project might be
in a bad state"* on every solution open; CLI build was clean.

**Root cause:** self-referential MDP `PackageReference`. `Sdk.props` gated it on
`'$(NanoMdpTaskAssembly)' == ''`, but `Mdp.targets` *set* `NanoMdpTaskAssembly`
from the restored package path → the reference appeared/disappeared between VS
restore nominations → infinite loop.

**Fix:** `Mdp.targets` derives the path into a **private** `_NanoMdpTaskAssembly`;
`NanoMdpTaskAssembly` stays a pure user-override knob. Guard step added to
`build-and-verify.sh`/`.cmd` (step 7) that fails if the MDP reference oscillates.

---

## 2. PE format: `NFMRK1` vs `NFMRK2` — FIXED ✅

**Symptom:** first deploys produced `Error: a2000000`, all-zero metadata.

**Root cause:** POC pinned MDP `3.0.100` which emits the **v1** PE format
(`NFMRK1`). The device's v2 firmware + CoreLibrary use **`NFMRK2`**.

**Fix:** use the **develop** `metadata-processor` (v4.0-preview, emits `NFMRK2`).
Its task ships `net8.0` + `net472` (not `net6.0`), so `Mdp.targets` `_NanoMdpTaskTfm`
now picks `net8.0` for the `dotnet build` host. Submodules restored and put on
`develop` (`metadata-processor`, `nf-debugger`, CoreLibrary at
`MetadataProcessor.Tests/mscorlib`).

---

## 3. mscorlib version mismatch saga — RESOLVED for deploy ✅

**Symptom:** `Link failure … needs assembly 'mscorlib' (X)` / `Error: a3000000`
(CLR `CLR_E_TYPE_UNAVAILABLE`) after deploy+reboot.

### Version facts established
| CoreLibrary | mscorlib native version | checksum (content) |
|---|---|---|
| `2.0.0-preview.49` | `100.22.0.4` | `0xE3176D8B` |
| `2.0.0-preview.52` | `100.22.0.5` | `0x2D5CA905` |
| **device firmware** | **`100.22.0.4`** | **`0x2D5CA905`** |

- `mscorlib` carries a **separate `[AssemblyNativeVersion]`** (`100.x`, the value
  the firmware reports) distinct from the managed `AssemblyVersion` (`2.0.0.0`,
  fixed by `version.json` for every `2.0.0-preview.x`).
- The `100.22.0.5` bump commit (`9049bf0`) changed **only** `AssemblyInfo.cs` →
  `preview.52`'s mscorlib is byte-identical content to the device's, just stamped
  `.5` vs `.4`. **No published nuget has `100.22.0.4` + `0x2D5CA905` together**
  (that pair exists only in the firmware; the `.50/.51` that would were never
  published).

### How the CLR actually matches (the key finding)
`CLR_RT_Assembly::Resolve_AssemblyRef` → `CLR_RT_TypeSystem::FindAssembly(name,
ver, fExact=false)` ([TypeSystem.cpp:3486-3521]). With `fExact=false` it compares
**name + MAJOR + MINOR only** — build & revision are *deliberately ignored*
("only the minor field is required to be bumped when native assemblies change
CRC"). **So `.4` vs `.5` is irrelevant to runtime resolution — only `100.22`
matters.** There are **two gates**:
1. **Host deploy pre-check** (nf-debugger / VS): version **and checksum** (strict).
2. **Runtime CLR link** (`FindAssembly`): name + **major.minor** only.

### What actually fixed it (CORRECTED — the MDP patch was a wrong turn)
The on-device run output was the key evidence:
```
Assembly: mscorlib (2.0.0.0)            <- the DEPLOYMENT IMAGE ships mscorlib.pe at 2.0.0.0
Blink needs assembly 'mscorlib' (100.22.0.4)   <- the MDP patch made Blink ask for the native version
Link failure / Error: a3000000
```
So an app must reference `mscorlib` at its **managed `AssemblyVersion` (2.0.0.0)** —
which is what the CLR `FindAssembly` resolves against (`m_header->version`) and what
the deployment image ships as `mscorlib.pe (2.0.0.0, checksum 0x2D5CA905`, binding to
the firmware natives). The firmware's `100.22.0.4` is a **separate native-binding
version**, NOT what the app references.

**The MDP `AssemblyNativeVersion` patch was therefore wrong** — it stamped Blink's
reference with `100.22.x`, which nothing on the device provides → `a3000000`.
**Reverted:** SDK now uses the **published, unpatched** MDP
(`NanoMdpTaskPackageVersion = 4.0.0-preview.94`, stamps the managed version); Blink
uses a standard `nanoFramework.CoreLibrary 2.0.0-preview.52` PackageReference. (The
local mscorlib hack and the patched `4.0.0-preview.96` MDP nupkg were removed; the
`metadata-processor` commit `94b119b` is obsolete and should be dropped.)

**The real gate is the deploy PRE-CHECK** in the extension —
`DeployProvider.cs::CheckNativeAssembliesAvailabilityAsync` (line ~500). For every
deployed PE with native methods (mscorlib), it requires BOTH `checksum` AND the
**exact 4-part native version** to match the firmware:
```
Version mismatch for mscorlib. Need v2.0.0.0, checksum 0x2D5CA905.
The connected target has v100.22.0.4, checksum 0x2D5CA905.
```
The firmware (`preview.467`, mscorlib native `100.22.0.4`) is **one native revision
behind** the published CoreLibrary `preview.52` (`100.22.0.5`) — checksums identical
(`0x2D5CA905` = same ABI), only the revision label differs. No published CoreLibrary
has `100.22.0.4` + `0x2D5CA905` together, so the exact-version check rejects every
candidate.

**Fix = relax the pre-check to a checksum match** (`DeployProvider.cs`): the
`nativeMethodsChecksum` is the native-ABI fingerprint; the build/revision label is
cosmetic (nanoFramework convention bumps only minor when the ABI/CRC changes, which
the on-device `FindAssembly` already honours by comparing major.minor). With that,
`preview.52`'s `mscorlib.pe` (checksum `0x2D5CA905`) deploys, and at runtime Blink
(referencing managed `2.0.0.0`) resolves against the deployed `mscorlib.pe (2.0.0.0)`,
which binds to the firmware natives via the matching checksum. **This is an EXTENSION
change — rebuild the VSIX.** (Deploy still needs "Generate deployment image" so
`mscorlib.pe` is shipped.)

The whole `100.x` native-version archaeology was a red herring; the MDP
native-version patch is NOT needed (reverted — stock MDP referencing the managed
version is correct). The single required extension change is the pre-check relax.

---

## 4. F5 / Run launches a console app instead of the debugger — FIX IMPLEMENTED, needs VS verification ⏳

**Symptom:** with deploy working, pressing **Run/F5** on the SDK-style Blink
launches a **local .NET console app**; no **device selector**. Legacy `NFApp1`
correctly shows ".NET nanoFramework Device".

**Root cause:** an SDK-style `Exe` `.csproj` inherits the **`LaunchProfiles`**
capability from `Microsoft.NET.Sdk`, so the C# project system owns F5 and runs the
console launcher. The extension's `NanoDebuggerLaunchProvider`
(`[ExportDebugger("NanoDebugger")]`, `[AppliesTo("NanoCSharpProject")]`) is never
selected. The legacy `.nfproj` avoids this in
`NFProjectSystem.CSharp.targets:28-29` by doing `<ProjectCapability
Remove="LaunchProfiles" />` + the `NanoDebugger.xaml` rule + a `NanoDebugger`
debugger flavor.

**Fix (in this SDK, `Sdk.targets`, after the Microsoft.NET.Sdk import, guarded on
`.NETnanoFramework`):**
- `<ProjectCapability Remove="LaunchProfiles" />`
- `<PropertyPageSchema Include="…Rules\NanoDebugger.xaml" />` (SDK ships its own copy
  at `nanoFramework.Sdk/Sdk/Rules/NanoDebugger.xaml`)
- `<DebuggerFlavor>NanoDebugger</DebuggerFlavor>`

SDK repacked into `local-feed`. **Verify in VS:** reload Blink → the debug target
dropdown should show ".NET nanoFramework Device" (with the Device Explorer
selection), F5 deploys+debugs, breakpoint hits. (Cannot be tested headlessly — VS
CPS behavior.)

---

## 5. Breakpoints don't hit (debug attaches, `Console.WriteLine` shows, no break) — FIXED ✅ (confirmed on hardware)

**Symptom:** debugger attaches, deploy + link succeed, `Debug.WriteLine` output
appears, app runs the loop and exits — but the source breakpoint never stops.

**Diagnosis (via `[BP-DIAG]` logging added to the extension):** the trace showed
- both `.pdbx` load + version-match (Blink + mscorlib) — symbols are fine;
- the breakpoint binds at **`ilCLR=0x0`** (`map 'Main' ilCLR=0x0 -> ilNanoCLR=0x0`,
  `BIND ... ilCLR=0x0 md=0x1000001`) — i.e. the **method entry of `Main`**, not the
  user's line;
- the device **HIT**s it once (`HIT IP=0x0 md=0x1000001 flags=0x8`=`c_HARD`) at
  startup, then the loop runs free.

`ilCLR=0x0` is exactly what `ICorDebugFunction.CreateBreakpoint` produces
(`new CorDebugFunctionBreakpoint(this, 0)`, CorDebugFunction.cs:244) — the
**function-entry fallback** VS uses when it **cannot map the source line → IL
offset**. The source→IL step reads the project **`.pdb`**.

**Root cause:** the SDK-style build emitted a **portable** PDB (`BSJB` magic);
nanoFramework's VS debug path resolves source↔IL only from a **Windows/full** PDB
(`Microsoft C/C++ MSF ...`). The **legacy** project system forces
`<DebugType>full</DebugType>` for Debug (`NFProjectSystem.Default.props:13-19`) for
exactly this reason. With a portable PDB VS can't find the line, so it binds at the
method entry → one startup hit, never on the line.

**Key build fact:** `DebugType=full` yields a Windows PDB **only under VS's .NET
Framework MSBuild/csc**. Under `dotnet build` (.NET Core csc) `full` still emits
portable — but the CLI doesn't debug, so that's harmless. (This is why an earlier
`full` test "did nothing": it was run via `dotnet build`.)

**Fix (this SDK, `Sdk/Sdk.props`):** set `DebugType=full` for Debug **before** the
`Microsoft.NET.Sdk` import, so the SDK's `'$(DebugType)'==''→portable` default
becomes a no-op and all `/debug` args derive from `full`. Release/CLI fall through to
portable. Verified: VS-MSBuild Debug build now writes `Blink.pdb` =
`Microsoft C/C++ MSF 7.00` (17920 B vs 1600 B portable); `.pe`/`.pdbx` still emitted.

**GOTCHA that cost time:** the SDK is consumed as a **NuGet package** (`global.json`
`msbuild-sdks` → `~/.nuget/packages/nanoframework.sdk/1.0.0`), **not** the local
`Sdk/` folder. Editing `Sdk.props` has **no effect** until you **repack**
(`dotnet pack nanoFramework.Sdk/nanoFramework.Sdk.csproj -c Release -o local-feed`)
**and clear the cached version** (`rm -r ~/.nuget/packages/nanoframework.sdk/1.0.0`,
since the version is unchanged). A global `-p:DebugType=full` "worked" earlier only
because global properties bypass the SDK entirely.

**Verify in VS:** **close + reopen VS** (the running instance holds the old SDK +
locks `Blink.exe`), rebuild Blink in **Debug**, redeploy, F5. Prefer a breakpoint on
a **statement** line (e.g. `Debug.WriteLine`) over `while (true)`. The next
`[BP-DIAG]` trace should show `ICorDebugCode.CreateBreakpoint (SOURCE-LINE bind)
offset=0x<non-zero>` instead of the function-entry fallback.

---

## 6. Legacy `.nfproj` won't load in the dev instance (only SDK `.csproj` loads) — ROOT CAUSE FOUND ✅

**Symptom:** in the solution, the SDK-style `Blink.csproj` loads but `NFApp1.nfproj`
shows **(unloaded)** under the custom/experimental extension.

**Diagnosis:**
- The extension **does** register the `.nfproj` type (CPS `ProjectTypeRegistration`,
  GUID `11A8DD76-…`, `NanoFrameworkPackage.cs:27`). Exactly **one** nano extension is in
  the Exp instance — so it's **not** a duplicate-registration conflict.
- A `.nfproj` imports its project system from
  `$(MSBuildExtensionsPath)\nanoFramework\v1.0\NFProjectSystem.*.{props,targets}`
  — **conditionally** (`Condition="Exists(...)"`). If absent, the imports **silently
  skip** and VS unloads the project.
- Those files are **missing** at `$(MSBuildExtensionsPath)` (=
  `<VSInstallDir>\MSBuild\`). They exist only inside the extension's deployed
  `…\<ExtFolder>\$MSBuild\nanoFramework\v1.0\` (the complete set: 5 targets/props +
  `Rules\*.xaml` + build-task DLLs `nanoFramework.Tools.BuildTasks.dll`,
  `…MetadataProcessor.MsBuildTask.dll`, `Mono.Cecil*`).

**Root cause:** the extension ships these as `InstallRoot="MSBuild"` /
`VSIXSubPath="nanoFramework\v1.0\"` assets (`VisualStudio.Extension-vs2022.csproj`). A
normal, **elevated** VSIX install copies them into `<VSInstallDir>\MSBuild\` →
`$(MSBuildExtensionsPath)\nanoFramework\v1.0\` resolves → `.nfproj` loads. The
**experimental-instance F5 deploy is non-elevated**, can't write to Program Files, so
the assets stay in the extension's `$MSBuild` subtree and never surface. SDK-style
`.csproj` is immune (it gets everything from the `nanoFramework.Sdk` NuGet package) —
hence "only SDK projects load."

**Fix (dev/Exp):** `dev-install-legacy-targets.ps1` — self-elevating; discovers the
deployed `$MSBuild\nanoFramework\v1.0\` and the VS MSBuild path, copies the complete set
to `$(MSBuildExtensionsPath)\nanoFramework\v1.0\`. Re-run after redeploying the
extension. Then restart VS and **Reload** `NFApp1.nfproj`. (A normal elevated VSIX
install makes this unnecessary — that's the real-world path.) Project files are left
**unmodified** (standard `.nfproj`), so this faithfully tests the extension.

**NOT done:** copying into Program Files directly (a global mutation) — the dev script
is the controlled, repeatable form.

---

## Dead ends / red herrings (do NOT repeat)
- **Building the firmware** to get `100.22.0.5` (option a): futile. The firmware's
  mscorlib version is hardcoded in `nf-interpreter/src/CLR/CorLib/corlib_native.cpp`,
  and the pinned submodule (`719ce37da`) says `{100,5,0,24}` — building it yields
  `100.5.0.24`, matching nothing. ESP-IDF isn't installed here anyway.
- **`2.0.0-preview.49`**: right native version (`100.22.0.4`) but wrong content
  (`0xE3176D8B`) — different mscorlib layout; doesn't help.
- **Local binary-patched mscorlib** (`preview.52` content re-stamped to `100.22.0.4`):
  still failed `a3000000` via the **headless** harness — because the failure was the
  harness's incremental-deploy + ClrOnly reboot, not the version. The proper
  deployment-image deploy from VS works.
- **Chasing `.4` vs `.5`**: irrelevant for runtime link (`FindAssembly` = major.minor).
- **`DebugType=full` via `dotnet build`**: no-op — the .NET Core csc emits portable
  regardless. `full` yields a Windows PDB only under VS's .NET Framework MSBuild (§5).
- **Editing the SDK's local `Sdk/` folder and expecting a build change**: the SDK is a
  cached NuGet package; you must repack + clear `~/.nuget/packages/nanoframework.sdk`
  (§5). A global `-p:DebugType=...` bypasses the SDK and is not a real test.
- **Standalone `d:\src\nnf\nanoFramework-CoreLibrary`** (main, v1.15, native
  `100.5.0.19`): old `100.5` line; not usable for a `100.22` device.

## Toolchain modification sets (independent)
- **(a) MDP native-version stamping** — `metadata-processor` submodule. **Build
  only.** Makes `Blink.pe` reference mscorlib at the native version. Use via
  `Mdp.targets` `NanoMdpTaskAssembly` knob pointing at the locally-built patched task.
- **(b) WS3 engine-binding seam** — `nf-Visual-Studio-extension` (`DebugLauncher/`).
  **Deploy/debug only**, behavior-preserving (AD7 default). Already in the running
  `9.99.999.0-DEBUG` extension binary.
- **(c) F5 launch wiring** — this SDK (§4). **VS launch only.**
- Deploy + Debug providers are gated on the **`NanoCSharpProject` capability**, not
  the `.nfproj` GUID — so they apply to SDK-style `.csproj` (the SDK injects the
  capability). Confirmed by the working deploy.

## Current state
- Build ✅ · Deploy ✅ (official extension + deployment-image settings) ·
  F5 launches device debugger ✅ (Console output confirmed) ·
  **Breakpoints ✅ confirmed on hardware** (portable→`full` PDB fix, §5).
  Next: per-device Run-dropdown selection (see `DEVICE-RUN-DROPDOWN.md`).
