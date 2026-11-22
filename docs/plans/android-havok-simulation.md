gate: passed | mode=改造后做 | oracle=existing Android APK entries, isolated LDPlayer instance, PlayerSelfTest and native crash logs | criteria=exact Havok library name in APK; on Android, Havok is active, a rigid body falls, and simulation can stop without a native crash | round=2 | status=done | date=2026-11-18

# Android Havok simulation crash

1. Isolate the installed player and its data from the user's emulator instance. Reproduce with the current APK, then retest with only the library entry corrected (F_ANDROID_HAVOK_01).
2. Fix the confirmed packaging defect in the standard Unity Android build path. Test an incremental build against the existing all-lowercase cache as well as a repeat build.
3. Check the simulation transition and physics ticks separately. Use the first new native stack, not just a successful startup, to determine whether the historical ECB fault is still relevant (F_ANDROID_HAVOK_02).

Facts file for this task: `docs/android-havok-facts.md` (the shared `docs/facts.md` is claimed by another task).

## Changes and Acceptance

- 2026-11-18 (F_ANDROID_HAVOK_01): Normalize `libHavokNative.so` in the generated Gradle project and remove stale wrong-case copies from Gradle outputs. Verify the name inside the completed APK. Both the incremental migration from a lowercase cache and subsequent builds passed.
- 2026-11-18 (F_ANDROID_HAVOK_05, F_ANDROID_HAVOK_07): Package a separate Android `Havok.Physics.dll` with ARMv7 range sizes 12/16/32 rather than the desktop 16/32/48. On Android with Havok active, a project-compiled job reads the finished impulse stream and feeds the existing breakage logic. Do not filter or skip Havok collision events. The desktop DLL and both native binaries are unchanged.
- 2026-11-18 (F_ANDROID_HAVOK_08): Run only `CopyPhysicsVelocityJob` without Burst in Android players. Leave Burst enabled elsewhere and keep the Havok solver selected.
- 2026-11-18 (F_ANDROID_HAVOK_09): The final APK at `Builds/Android/bpsimF_android.apk` contains the exact-case ARMv7 native library, the Android managed DLL (verified against the source asset by hash), and `lib_burst_generated.so`. Both fixtures passed twice with separate cold launches. This is a player-level regression via the same simulation start entry as the UI, not an input replay or handset test.

## Reproduce

Build with **Tools > BPSim > Android > Build APK**, or call `Bpsim.EditorTools.AndroidBuild.Build` under the editor lease. The separate DLL is checked in; regenerate only for the pinned source pair with `Tools/PatchHavokManagedAndroid.ps1 -UnityEditorDirectory '<Unity installation>/Editor'`. A new DLL or architecture requires rechecking the layouts and replaying both fixtures.

Install the APK on an isolated Android test device with the simulation backend set to Havok. The recorded runs used `BpsimHavokQA` (`emulator-5556`), not the user's other instance. In PowerShell with the Unity SDK's `adb` on `PATH`, run the following twice, once per value of `$flags`:

```powershell
$serial = 'emulator-5556'
$package = 'com.bprc.bpsimF'
$flags = '-bpsimSelfTest' # second fixture: '-bpsimSelfTest -bpsimHavokEvents'
adb -s $serial shell am force-stop $package
$since = (adb -s $serial shell 'date "+%m-%d %H:%M:%S"').Trim() + '.000'
$launch = 'am start -W -n {0}/com.unity3d.player.UnityPlayerActivity --es unity "{1}"' -f $package, $flags
adb -s $serial shell $launch
adb -s $serial logcat -v threadtime -b main -b system -b crash -T $since
```

Stop the log capture after `[SelfTest] wrote ... (exit 0)` and check both runs for `simSingleton=HavokPhysics step=HavokPhysics` at step two, `maxDrop > 1 m`, `IsSimulating=False` after stop, zero errors, and no fatal signal. The events fixture must also report `initial joints=5` and `remaining joints=0 landed=True havok=True`. Across the two independent runs per fixture, the pass/fail and joint-count outcomes are identical; the measured basic drop varies with the number of fixed steps in the sampling window (1.111 m difference), so do not use exact positions as a pass condition.
