# uGUI / TextMeshPro: plugin DLL → UPM package migration (bpsim-main)

Status: **done** (2026-10-31). The decompiled plugin DLLs are gone; every serialized
`m_Script` reference now points at the package scripts.

## Why

`Assets/Plugins/UnityEngine.UI.dll` and `Assets/Plugins/Unity.TextMeshPro.dll` were
decompiled copies of the very assemblies that `Packages/com.unity.ugui` and
`Packages/com.unity.textmeshpro` now compile, so every domain reload logged:

```
Plugin 'Assets/Plugins/UnityEngine.UI.dll' has the same filename as Assembly Definition
File 'Packages/com.unity.ugui/Runtime/UnityEngine.UI.asmdef'. Rename the assemblies to
avoid hard to diagnose issues and crashes.
```

They became a hard blocker for `com.xxhq.htmltougui`, which pulls in TextMeshPro 3.0.6 →
uGUI 1.0.0 and therefore compiles `Unity.TextMeshPro` / `UnityEngine.UI` from source.

Migrating the references (not hiding the DLL) is the only fix that keeps prefabs working:
42 prefab/asset files bound `m_Script` to the DLL's MonoScripts
(guid `d3e719b59ab71ba3f6b398058c866280`, 834 refs) and 6 more bound to TMP's
(guid `67dfb1fdfb2b407222eda8e23ac8b724`, 28 refs).

## What was done

1. `Assets/Editor/BpsimUiPluginAudit.cs` (temporary helper, archived at
   `Tools/BPSim/BpsimUiPluginAudit.cs.txt`) dumped every MonoScript in both DLLs and its
   matching package script:
   `Logs/UnityEngine.UI.dll.map.txt`, `Logs/Unity.TextMeshPro.dll.map.txt`,
   lines `localId|class|packagePath|packageGuid`.
   Result: uGUI 44 scripts / 43 mapped, TMP 20 / 18 mapped — the unmapped ones
   (`-132589691`, `-995609174` class-less, `2042998964` TMP editor window) are referenced
   by no asset.
2. `Tools/bpsim_migrate_ui_refs.py --apply` rewrote `fileID: <dll localId>, guid: <dll guid>`
   → `fileID: 11500000, guid: <package script guid>` in every text asset under `Assets`
   (skipping `.meta`/`.cs`): **862 references in 46 files**. Backups:
   `Tools/BPSim/backup/ui-refs/<same relative path>` (local only — the directory is
   git-ignored, since the same contents are recoverable from git history).
3. Pre/post audit with `GameObjectUtility.GetMonoBehavioursWithMissingScriptCount` over all
   101 prefabs stayed at **missingScripts=0** (9660 component slots).
4. `AssetDatabase.DeleteAsset` removed both DLLs and their `.meta` files.
5. Cold editor restart: `*** Tundra build success`, zero `error CS`, zero "same filename"
   warnings, play mode enters and exits with an empty console.

## Regenerating the map (if it is ever needed again)

The helper archived in `Tools/BPSim/BpsimUiPluginAudit.cs.txt` needs the DLLs and a
public wrapper to be called from the DshBridge (`invoke_static` only reaches public
static methods and the helper writes to `Logs/`).

## Lessons

* Never rename/move/delete a DLL that anchors serialized references: Unity keeps handing
  `-analyzer:"<old path>"` from `Library/SourceAssetDB` and every compile fails with
  `CS0006: Metadata file ... could not be found`. Only restarting the editor (or
  recreating the file at the old path and deleting it) clears that state.
* `AssetDatabase.Refresh()` alone does not always re-import a package asset that lost a
  GUID conflict; `AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate)` does.
* After the migration, uGUI/TMP types come from the packages, so assembly definitions can
  list `UnityEngine.UI` / `Unity.TextMeshPro` in `references` — that is what
  `Assets/Plugins/IngameDebugConsole/IngameDebugConsole.Runtime.asmdef` now does.
