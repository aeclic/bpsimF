gate: passed | mode=直接做 | oracle=FileAPI implementation, isolated Android DocumentsUI, exported bytes and loaded state | criteria=save and reopen a scene, export and reinstall a mod ZIP, import a Mod folder; cancel and retry | round=1 | status=running | date=2026-11-18

# Android document picker

The shared `FileAPI` unconditionally invokes the Windows Shell dialog DLL, even in an Android Player. `OpenDirectory` also invokes the file dialog. The Android acceptance target is the installed player with the system picker actually in the foreground, not a desktop path that happens to read and write.

| ID | Claim and discriminator | Evidence / status |
|---|---|---|
| F_ANDROID_DOCUMENTS_01 | A picker request on the old APK never reaches Android DocumentsUI; the new build launches the system picker, and selecting a file, folder, or destination produces the matching side effect. | Expected: ADB foreground activity and captured app result or actual file bytes. Pending. |

1. Keep Windows browse calls intact. On Android, use the Storage Access Framework and stage provider streams in app-owned files without requesting broad storage permissions.
2. Keep a successful scene import available for session restoration; discard transient picks on cancel. Install a selected Mod folder as a local copy, and export its package as a ZIP from Mod settings.
3. Exercise both builds on the isolated `BpsimHavokQA` emulator (`emulator-5556`). Verify the picker via its accessibility nodes, and inspect exported bytes and loaded state separately.

2026-11-18, F_ANDROID_DOCUMENTS_02 (pending): The Mod import button enables the blocker, but the menu child starts disabled and is never re-enabled. Start with the blocker disabled instead. Pass when both options are visible and the folder and ZIP buttons each open the corresponding Android picker on the rebuilt APK. See `Logs/AndroidDocuments/mod-import-menu-confirm.png` for the second build's negative control.
