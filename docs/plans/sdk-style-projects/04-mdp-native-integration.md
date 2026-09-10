# 04 — Metadata Processor (MDP) Integration

How the metadata processor lives inside the SDK's managed target graph. This is
where the bulk of the new MSBuild authoring lives.

**Scope note.** This covers the *managed* PE pipeline: re-hosting the existing MDP
task with proper incrementality and ordering. Native module compilation,
relocatable native linking, and packaging native binaries are **out of scope**
(separate, later effort). MDP's existing stub generation for `InternalCall` types
is retained because it is part of producing a correct PE, but those stubs feed the
**firmware** build — they are never shipped inside a NuGet package here.

---

## 4.1 Starting point: MDP is already a task

Per doc 01, `nanoFramework.Tools.MetadataProcessor.MsBuildTask` is already an
MSBuild task invoked from `NFProjectSystem.MDP.targets`
(`GenerateBinaryOutputTask` et al.). The migration is **not** "make it a task" —
it's:

1. **Re-host** the task invocation into SDK-owned targets with explicit,
   documented Inputs/Outputs (for incrementality) instead of conventions baked
   into the legacy targets file.
2. **Parameterize** the stub output location and checksum surfacing as MSBuild
   properties/items.
3. **Order** the PE stage correctly against `Microsoft.NET.Sdk`'s `CoreCompile`.
4. **Decouple** the x64-host / `nodeReuse` workaround by packaging the task
   assembly with both architectures and making the SDK set `nodeReuse` behavior,
   eliminating the per-project `-nr=False` hack.

## 4.2 The MDP task contract (as re-hosted)

The SDK wraps the existing task. Target `NanoEmitPe`:

```xml
<!-- nanoFramework.Mdp.targets -->
<UsingTask TaskName="nanoFramework.Tools.MetadataProcessor.MsBuildTask.GenerateNanoBinary"
           AssemblyFile="$(NanoMdpTaskAssembly)" />

<Target Name="NanoEmitPe"
        Condition="'$(NanoEmitPe)'=='true'"
        AfterTargets="CoreCompile"
        BeforeTargets="NanoValidateChecksum"
        Inputs="@(IntermediateAssembly);@(ReferencePathWithRefAssemblies)"
        Outputs="$(NanoPeOutputPath);$(NanoPdbxOutputPath)">

  <GenerateNanoBinary
      Assembly="@(IntermediateAssembly)"
      References="@(ReferencePathWithRefAssemblies)"
      LoadHints="@(NanoLoadHint)"
      GenerateStubs="$(_NanoGenerateStubs)"
      StubsOutputPath="$(NanoStubsDir)"
      PeOutputPath="$(NanoPeOutputPath)"
      PdbxOutputPath="$(NanoPdbxOutputPath)"
      Verbose="$(NanoMdpVerbose)">
    <Output TaskParameter="NativeMethodsChecksum" PropertyName="NanoNativeMethodsChecksum" />
    <Output TaskParameter="GeneratedStubFiles" ItemName="NanoGeneratedStub" />
  </GenerateNanoBinary>

  <ItemGroup>
    <FileWrites Include="$(NanoPeOutputPath);$(NanoPdbxOutputPath);@(NanoGeneratedStub)" />
  </ItemGroup>
</Target>
```

Key changes from legacy:
- **Incrementality** via `Inputs`/`Outputs` (legacy MDP often re-ran every build).
- **`NativeMethodsChecksum` is an output property** (`$(NanoNativeMethodsChecksum)`),
  not just embedded in generated C++, so the optional ABI gate (§4.5) can consume
  it directly.
- **Stub output path is a property** (`$(NanoStubsDir)`), defaulting to
  `bin/stubs/$(AssemblyName)/` and overridable.
- `FileWrites` registration lets `Clean` remove generated artifacts.

## 4.3 Target ordering — the managed sequence

```
ResolveReferences   (Microsoft.NET.Sdk)
        │
CoreCompile         (Roslyn → IL .dll)
        │   AfterTargets=CoreCompile
NanoEmitPe          (MDP → .pe, .pdbx, stubs, checksum)
        │   AfterTargets=NanoEmitPe (optional)
NanoValidateChecksum  (compare PE checksum vs. target firmware ABI)
        │
Build complete
```

Rules:
- We never depend on import order for *execution* order — only
  `BeforeTargets`/`AfterTargets`/`DependsOnTargets`.
- `NanoEmitPe` runs for every project; there is no native build stage in scope.

## 4.4 Stub generation hook

For managed types declaring `InternalCall` native methods, MDP emits stubs that
the **firmware** implements. (These stubs are not part of the NuGet package; they
are inputs to the separate firmware build.) Most libraries don't define new
`InternalCall`s — they call into natives already provided by the CLR Core — so the
default is off:

```xml
<PropertyGroup>
  <_NanoGenerateStubs Condition="'$(NanoGenerateStubs)'!=''">$(NanoGenerateStubs)</_NanoGenerateStubs>
  <_NanoGenerateStubs Condition="'$(_NanoGenerateStubs)'==''">false</_NanoGenerateStubs>
</PropertyGroup>
```

A pure managed library that declares `InternalCall`s against the CLR Core (e.g.
`System.Device.Gpio`, whose natives live in the firmware/CLR Core) leaves stub
generation off and only needs its checksum to match the Core's exported ABI. This
is the common case.

## 4.5 Checksum validation (optional ABI gate)

The legacy `NativeMethodsChecksum` becomes a first-class build output that an
optional target can validate against the target firmware's exported ABI, turning
a class of device-side load failures into build errors:

```xml
<Target Name="NanoValidateChecksum"
        AfterTargets="NanoEmitPe"
        Condition="'$(NanoValidateChecksum)'=='true'">
  <NanoChecksumCheck
      PeChecksum="$(NanoNativeMethodsChecksum)"
      TargetAbiManifest="$(NanoTargetAbiManifest)">
    <Output TaskParameter="Result" PropertyName="_NanoChecksumOk" />
  </NanoChecksumCheck>
  <Error Condition="'$(_NanoChecksumOk)'!='true'"
         Text="nanoFramework ABI mismatch: PE NativeMethodsChecksum $(NanoNativeMethodsChecksum) is not satisfied by the target firmware. The managed assembly was built against native declarations the target runtime does not export." />
</Target>
```

This is the build-time equivalent of nanoCLR's load-time refusal. It is
**opt-in** (`NanoValidateChecksum=true`) and requires the consumer to point at a
target firmware ABI manifest; without it, the build proceeds and the checksum is
simply surfaced as a property.

## 4.6 What MDP hooks into — summary table

| Concern | Legacy hook | SDK hook |
|--------|-------------|----------|
| IL → PE | post-`CoreCompile` task in `MDP.targets` | `NanoEmitPe` `AfterTargets=CoreCompile` |
| Stub generation | inside MDP task, fixed path | `NanoEmitPe` with `NanoStubsDir` property |
| Checksum | embedded in `corlib_native.cpp` | output property `$(NanoNativeMethodsChecksum)` + optional `NanoValidateChecksum` gate |
| Clean | partial | `FileWrites` registration |
| Incrementality | weak | `Inputs`/`Outputs` on every target |
