gate: passed | mode=直接做 | oracle=Logs/AndroidBuild/sdkmanager-stalled-stack.txt and actual Unity Android build | criteria=BuildResult.Succeeded, fresh APK, SDK-check and total duration recorded | round=1 | status=done | date=2026-11-06

# Android SDK detection stall

The active Build Settings build remained in Detecting Android SDK for over 87 minutes. The SDK manager main thread is blocked in TLS handshake during remote package discovery; the local SDK already contains API 33 and build tools 32.0.0. Previous shader stripping and Mono/GLES3 configuration are already present and a prior APK succeeded in 130.7 seconds.

1. Retain the stalled stack and recover only the hung SDK-manager child process.
2. Check whether SDK-manager JVM network timeouts provide a bounded, successful installed-package query. Use the observed result to choose the smallest project-local fix; do not modify the shared SDK.
3. Cover Unity Build Settings as well as the existing Android menu/batch entry, preserve caller options, and restore any temporary environment changes.
4. Run a real Android build, retain stage timings and verify the fresh APK. Record any remaining failure separately from SDK detection.

Evidence: F337. No source/asset deletion, branch creation or commits are part of this task.

2026-11-07 results (F337/F338):

- Two independent JVM timeout probes returned exit 0 with the installed API 33 package, in 19.84 and 26.194 seconds. The real Build Settings SDK-manager child command line contains both timeout properties.
- The real Build Settings -> Build -> Save flow succeeded: SDK detection 74.255 seconds, Gradle 74.742 seconds, full handler 237.2 seconds (Unity build report 235.540 seconds).
- APK: `C:/Users/aecli/Downloads/bpsim1106-1.apk`, 119555614 bytes, SHA-256 `0b86b3ce45817501286eca8b84dc499f26627abc75286c9f15baf7ca5a6b7845`. ZIP CRC and v1/v2 signature verification pass. AAPT confirms target/compile SDK 33, min SDK 22 and ARMv7.
- UniWebView's parsed resolutionStrategy detector now accepts structural nodes. The actual regenerated Gradle input retains five existing blocks instead of appending a sixth; no timing improvement is attributed to this secondary fix.
- The scope rejects a build before Android is the active platform, because Unity's deferred platform-switch path can otherwise outlive the temporary environment. Switch Platform and wait for compilation first; the standard active-Android flow was the path exercised here.
- Evidence: `Logs/AndroidBuild/build-settings-verified.log`, `build-settings-verified.json`, `sdkmanager-stalled-stack.txt`, and SDK probe logs/JSON. Boundary: successful packaging and APK validity, not gameplay/device regression testing or a fixed duration on every network.
