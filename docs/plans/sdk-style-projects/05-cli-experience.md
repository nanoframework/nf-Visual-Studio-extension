# 05 — CLI Experience

What `dotnet` verbs work, how `deploy` is implemented, and iterative deployment.

---

## 5.1 The verbs

| Command | Mechanism | Status |
|---------|-----------|--------|
| `dotnet build` | Standard MSBuild; SDK's targets emit `.pe` | Works once SDK exists |
| `dotnet restore` | Standard NuGet restore (PackageReference) | Works |
| `dotnet pack` | SDK `Pack` target override → nano NuGet layout (doc 08) | Works |
| `dotnet clean` | Standard; SDK registers `FileWrites` (doc 04) | Works |
| `dotnet new nanoapp` / `nanolib` | Templates in the SDK (doc 10 §10.4) | Works |
| `dotnet build -t:Deploy` | SDK `Deploy` target orchestrates `nanoff` | Works |
| `dotnet nano deploy` | `dotnet-nano` global/local tool (§5.3) | Preferred UX |
| `dotnet watch` (deploy loop) | `dotnet watch` + `-t:Deploy` MSBuild target | Works (§5.4) |

## 5.2 Why `deploy` can't *just* be `dotnet deploy`

The `dotnet <verb>` extensibility model resolves `dotnet-<verb>` executables from the **tool** resolvers (global tools on PATH, or local tools via a `dotnet-tools.json` manifest). Project-scoped CLI tools (`DotNetCliToolReference`) were **deprecated after .NET Core 2.1**, so there is no per-project verb injection anymore. `dotnet ef` works because EF ships a `dotnet-ef` global/local tool — not because EF extends the project system.

Two consequences:
1. A bare `dotnet deploy` would require a *global* tool named `dotnet-deploy`, a generic name that risks collision and implies ownership of a common verb. **Avoid.**
2. The clean, ownable surface is a **`dotnet-nano` tool** exposing `dotnet nano deploy`, `dotnet nano flash`, `dotnet nano monitor`, etc. — namespaced, collision-free, and the natural home for everything `nanoff` does plus build-aware orchestration.

## 5.3 Two deploy paths (both supported)

### Path A — MSBuild target (no extra tool)
The SDK defines `Deploy` so plain `dotnet build -t:Deploy` works on any machine with just the SDK restored:

```xml
<!-- nanoFramework.Deploy.targets -->
<Target Name="Deploy" DependsOnTargets="Build">
  <ItemGroup>
    <!-- the app PE + all referenced PEs that must be on-device -->
    <NanoDeployAssembly Include="$(NanoPeOutputPath)" />
    <NanoDeployAssembly Include="@(NanoReferencePe)" />
  </ItemGroup>
  <NanoDeploy
      Assemblies="@(NanoDeployAssembly)"
      SerialPort="$(NanoDeployTarget)"
      NanoffPath="$(NanoffToolPath)"
      RebootAfter="$(NanoDeployReboot)" />
</Target>
```

`NanoDeploy` is a thin task that shells `nanoff` (or links its library) with the already-built artifacts — the build output, not a re-discovery of files.

### Path B — `dotnet nano` tool (preferred UX)
`dotnet-nano` is a .NET tool (global: `dotnet tool install -g nanoFramework.Tool`; or local manifest). It wraps Path A *and* the device-side operations:

```
dotnet nano deploy            # build + deploy current project to the configured/only device
dotnet nano deploy --port COM7
dotnet nano flash --target ESP32_S3   # nanoff firmware flash (unchanged)
dotnet nano monitor           # serial monitor / device output
dotnet nano devices           # list connected devices (device explorer, CLI form)
```

Internally `dotnet nano deploy` invokes MSBuild on the project's `Deploy` target (Path A), so there is exactly one deploy code path; the tool just provides device selection and a friendlier UX.

## 5.4 Iterative deployment (`dotnet watch`)

`dotnet watch` re-runs an MSBuild target on source change. Wire it to `Deploy`:

```
dotnet watch -t:Deploy
# or, via the tool, which sets up the watch + device session:
dotnet nano watch
```

`dotnet nano watch` adds value over raw `dotnet watch`:
- Keeps a single serial session open (avoids reconnect churn).
- Deploys **only changed PEs** — since each PE is content-addressable by checksum, unchanged assemblies are skipped and only the changed managed assemblies are pushed.
- Streams device output back into the watch console.

This is the embedded analogue of hot-reload: edit C#, save, the changed PE redeploys in ~hundreds of ms without reflashing firmware.

## 5.5 Device selection and configuration

Resolution order for the target device, highest priority first:
1. `--port`/`--target` CLI argument.
2. `$(NanoDeployTarget)` project property.
3. `nanoFramework.config.json` in the repo (per-repo default port/target).
4. Auto-select if exactly one device is connected; otherwise prompt (interactive) or error (CI).

## 5.6 CI considerations

- CI builds use `dotnet build`/`dotnet pack` only; no device. Deploy/flash verbs require hardware and are excluded from PR builds (as today).
- The `dotnet nano` tool installs as a **local** tool (`dotnet-tools.json`) so CI pins its version with the rest of the toolchain.
- `dotnet build -t:Deploy` failing fast with a clear "no device" error (not a hang) fixes a current VS Code pain point where flashing "hangs forever."
