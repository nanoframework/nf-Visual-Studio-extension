# 10 — Tooling Specifications, Package Layouts & Templates

The build-list: every component that has to be authored, the SDK package's on-disk
structure, the `dotnet new` templates, and the consolidated target graph.

**Scope note.** Managed project system only. Native compile/link tasks, native
binaries in packages, module/ABI manifest generators, RID graphs for native asset
selection, toolchain/CoreRuntime packs, and OTA are out of scope.

---

## 10.1 Components to build (the build-list)

| # | Component | Type | New / modified | Notes |
|---|-----------|------|----------------|-------|
| C1 | `nanoFramework.Sdk` | MSBuild SDK NuGet pkg | New | `Sdk.props`/`Sdk.targets` over `Microsoft.NET.Sdk` |
| C2 | TFM moniker props | MSBuild | New | sets the `netnano1.0` moniker properties (doc 02 §2.2) |
| C3 | `GenerateNanoBinary` task | C# task | Modified | re-host of existing MDP task with explicit I/O (doc 04 §4.2) |
| C4 | `NanoChecksumCheck` task | C# task | New | optional build-time ABI gate (doc 04 §4.5) |
| C5 | `NanoDeploy` task | C# task | New | wraps `nanoff` push (doc 05) |
| C6 | `dotnet-nano` tool | .NET tool | New | deploy/flash/monitor/**migrate** verbs (doc 05) |
| C7 | `dotnet new` templates | Template pkg | New | `nanoapp`/`nanolib` (§10.5) |
| C8 | NanoMigrate converter + CI template rewriter | Tool | New | ships in the SDK repo `tools/migrate`; surfaced as `dotnet nano migrate`; idempotent + reentrant fleet conversion (doc 07, doc 05 §5.7) |
| C9 | VS CPS capability + XAML rules | VS extension | Modified | **POC-proven** (doc 09 §9.5) — productize into the shipped extension |
| C10 | `nanoFramework.Sdk.Corlib` variant | MSBuild SDK | New | mscorlib bootstrap (doc 02 §2.6) |
| C11 | nanoFramework workload manifest | Workload pkg | New (later) | wraps SDK+templates (doc 02 §2.3 option C) |

MVS (Phase 1) ships C1, C2, C3, C4, C7, C8. C5/C6 follow; C9 is POC-proven (the VS
debugger is no longer a gate — productize it next); C10/C11 land later. The MSBuild SDK
(C1) now exists as [`nanoframework/nanoFramework.Sdk`](https://github.com/nanoframework/nanoFramework.Sdk).

## 10.2 `nanoFramework.Sdk` package structure

```
nanoFramework.Sdk/<version>/
├─ Sdk/
│   ├─ Sdk.props                       ← imports Microsoft.NET.Sdk Sdk.props; nano defaults
│   ├─ Sdk.targets                     ← imports nano targets; Microsoft.NET.Sdk Sdk.targets LAST
│   ├─ nanoFramework.Tfm.props         ← C2: netnano1.0 moniker properties
│   ├─ nanoFramework.Mdp.targets       ← C3/C4: NanoEmitPe, NanoValidateChecksum
│   ├─ nanoFramework.Deploy.targets    ← C5: Deploy target
│   └─ nanoFramework.Pack.targets      ← pack overrides (doc 08 §8.2)
├─ tasks/
│   ├─ net8.0/nanoFramework.Sdk.Tasks.dll          ← C4/C5 tasks (modern host)
│   ├─ net472/nanoFramework.Sdk.Tasks.dll          ← VS/MSBuild.exe host
│   └─ (MDP task referenced via its own package or vendored)
└─ templates/  (or shipped as separate template package C7)
```

Two task TFMs (`net8.0` + `net472`) so the same tasks load under `dotnet build`
and under VS's `MSBuild.exe`; `$(NanoSdkTasks)` selects by `$(MSBuildRuntimeType)`:

```xml
<PropertyGroup>
  <NanoSdkTasks Condition="'$(MSBuildRuntimeType)'=='Core'">$(MSBuildThisFileDirectory)..\tasks\net8.0\nanoFramework.Sdk.Tasks.dll</NanoSdkTasks>
  <NanoSdkTasks Condition="'$(MSBuildRuntimeType)'!='Core'">$(MSBuildThisFileDirectory)..\tasks\net472\nanoFramework.Sdk.Tasks.dll</NanoSdkTasks>
</PropertyGroup>
```

This, plus controlling node reuse, is what retires the x64-task/`-nr=False`
workaround (doc 04 §4.1).

## 10.3 Task specs (signatures)

```csharp
// C3 — re-host of existing MDP task; explicit inputs/outputs
public sealed class GenerateNanoBinary : Task {
    [Required] public ITaskItem[]  Assembly { get; set; }      // IL .dll from Roslyn
    public ITaskItem[]  References { get; set; }
    public ITaskItem[]  LoadHints { get; set; }
    public bool         GenerateStubs { get; set; }
    public string       StubsOutputPath { get; set; }
    [Required] public string PeOutputPath { get; set; }
    public string       PdbxOutputPath { get; set; }
    [Output] public string      NativeMethodsChecksum { get; set; }
    [Output] public ITaskItem[] GeneratedStubFiles { get; set; }
}

// C4 — optional build-time ABI gate vs. target firmware
public sealed class NanoChecksumCheck : Task {
    [Required] public string PeChecksum { get; set; }
    public string TargetAbiManifest { get; set; }     // firmware exported-ABI manifest
    [Output] public bool Result { get; set; }
}

// C5 — managed deploy orchestration (flash the PE set to a device)
public sealed class NanoDeploy : Task {
    [Required] public ITaskItem[] Assemblies { get; set; }   // the .pe set to deploy
    public string SerialPort { get; set; }
    public string NanoffPath { get; set; }
    public bool   RebootAfter { get; set; }
}
```

## 10.4 `dotnet new` templates (C7)

Two templates in `nanoFramework.Templates`:

### `nanoapp`
```
content/nanoapp/
├─ .template.config/template.json
├─ Company.NanoApp.csproj      ← Sdk + netnano1.0 + OutputType Exe
└─ Program.cs                  ← Main with a blink/Debug.WriteLine sample
```
`template.json` (essentials):
```json
{
  "identity": "nanoFramework.App",
  "shortName": "nanoapp",
  "tags": { "language": "C#", "type": "project" },
  "sourceName": "Company.NanoApp"
}
```

### `nanolib`
Library variant: `OutputType Library`, packing metadata stubbed, no `Program.cs`.

Invocation:
```
dotnet new nanoapp -n Blinky
dotnet new nanolib -n MyLib
```

## 10.5 Consolidated target graph (single reference)

```
Restore ─ resolves: nanoFramework.Sdk, PackageReferences
   │
Build
   ├─ ResolveReferences        (Microsoft.NET.Sdk)
   ├─ CoreCompile  (Roslyn csc)                  → obj/<Asm>.dll (IL) + .pdb
   │     │ AfterTargets
   ├─ NanoEmitPe   (C3 GenerateNanoBinary)       → bin/<Asm>.pe,.pdbx
   │                                             → $(NanoNativeMethodsChecksum)
   │                                             → stubs (only if native interop)
   │     │ AfterTargets (optional)
   └─ NanoValidateChecksum (C4)                  → Error if ABI mismatch (opt-in)

Pack (dotnet pack)
   └─ NanoPackBuildOutputs → lib/netnano1.0/*.pe,.pdbx,.dll,.xml → .nupkg

Deploy (explicit target)
   └─ Build → NanoDeploy (C5) → nanoff push
```

## 10.6 Acceptance criteria per component (abbreviated)

- **C1/C2:** ~6-line `.csproj` restores and builds a `.pe` byte-identical to
  legacy on a clean machine (no VS/VSCode extension installed).
- **C3:** checksum, stubs, `.pe`, `.pdbx` match legacy MDP output for a corpus of
  existing libraries; second build is a no-op (incremental).
- **C4:** with `NanoValidateChecksum=true` and a target ABI manifest, a PE built
  against a mismatched firmware fails the build with an actionable message;
  matching build passes; with the gate off, the build proceeds.
- **C5/C6:** `dotnet nano deploy` flashes the app and it runs on device.
- **C7:** `dotnet new nanoapp -n X && cd X && dotnet build` succeeds end-to-end.
- **C8:** running the migration tool over a pilot of 5 `lib-*` repos produces
  building SDK projects with an empty manual-review list (or all review items
  genuinely non-default).
- **C9:** the VS debugger gate (doc 09 §9.5) is **cleared ✅** — the POC deployed and
  hit a source breakpoint on an SDK-style project in VS on real hardware. Remaining: fold
  the POC fixes into the shipped extension (productization, not feasibility).
