# Windows Anvil Native Build

`Build-Windows.ps1` builds the Anvil bridge and pinned Box3D commit
`e77352cd606dc1a34209094076199549a52ea0a1`. It checks the source archive's
SHA-256 before applying patches. Use Zig 0.13.0 with `-SparseWeld` for the
production configuration (bridge version 4 and wrapped hinge limits).
`-LegacyBridge` and `-ExperimentalIntegratedSubsteps` are comparison builds,
not release configurations. Integrated substeps did not clear the CCD gate.

From the repository root, with `$zig` pointing to `zig.exe`:

```powershell
$scratch = (New-Item -ItemType Directory -Force -Path ./_codex_work/anvil-native).FullName
$env:ZIG_GLOBAL_CACHE_DIR = Join-Path $scratch 'zig-global-rebuild'
$env:ZIG_LOCAL_CACHE_DIR = Join-Path $scratch 'zig-local-rebuild'
$dll = Join-Path $scratch 'box3d-rebuild.dll'
& ./Tools/AnvilNative/Build-Windows.ps1 -Zig $zig -SparseWeld -Output $dll
python -X utf8 ./Tools/AnvilParity/native_probe.py --native-dll $dll
& ./Tools/AnvilParity/Run-Private.ps1 -NativeDll $dll -Label native-rebuild-a
& ./Tools/AnvilParity/Run-Private.ps1 -NativeDll $dll -Label native-rebuild-b
python -X utf8 ./Tools/AnvilParity/check_report.py Logs/anvil-parity-native-rebuild-a.json Logs/anvil-parity-native-rebuild-b.json --native-dll $dll
```

A fresh global cache resolved Zig's `AccessDenied` while generating MinGW CRT and import
libraries on the test host. The script does not install the DLL; compare
release Player saves before replacing `Assets/Plugins/x86_64/box3d.dll`.
An Editor that has already loaded that plugin must be restarted to use a
replacement DLL.
