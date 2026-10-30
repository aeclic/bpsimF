// Migrates bpsim-main's serialized references off the decompiled uGUI plugin DLL and onto the
// built-in com.unity.ugui package scripts, then the DLL can be deleted.
//
// WHY
// ---
// Assets/Plugins/UnityEngine.UI.dll (guid d3e719b59ab71ba3f6b398058c866280) is the decompiled
// uGUI *plugin*. com.unity.ugui is a built-in package that Unity's default-dependency merge adds
// to every project, so the package's UnityEngine.UI assembly and the plugin produce the same
// assembly name. The plugin loses, the components that reference it by GUID become
// "The referenced script on this Behaviour ... is missing!", and the console fills with missing
// script errors (the EventSystem, every Image/Text/Button/Toggle).
//
// The repository CLAUDE.md documents the identical migration for the main BPRC project ("uGUI now
// comes from the built-in com.unity.ugui package only ... 3197 m_Script references in 93
// prefabs/scenes/animations were migrated to fileID: 11500000, guid: <ugui .cs guid>").
//
// HOW
// ---
// A component inside a DLL serializes as {fileID: <MonoScript local id>, guid: <dll guid>}. For
// every MonoScript in the DLL this tool resolves the local id and its class, finds the class's
// source script inside the com.unity.ugui package, and rewrites every text asset under Assets/.
// Then it deletes the DLL. Use -WhatIf to only report.
//
// USAGE
//   Unity.exe -batchmode -quit -projectPath <proj> -executeMethod BpsimUguiMigration.Run
//   Unity.exe -batchmode -quit -projectPath <proj> -executeMethod BpsimUguiMigration.Report
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class BpsimUguiMigration
{
    const string DllPath = "Assets/Plugins/UnityEngine.UI.dll";
    const string DllGuid = "d3e719b59ab71ba3f6b398058c866280";
    const string PackageRoot = "Packages/com.unity.ugui";
    const string BackupRoot = "Assets/../Tools/BPSim/backup/ugui-refs";

    static readonly string[] TextExtensions = { ".asset", ".prefab", ".unity", ".controller", ".anim", ".mat", ".meta" };

    // uGUI is a built-in package, so PackageInfo.resolvedPath comes back empty and its assets are
    // not reachable through AssetDatabase.FindAssets; fall back to the Editor's BuiltInPackages
    // folder and then to the project's package cache.
    static string FindUguiRoot()
    {
        try
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(UnityEngine.UI.Button).Assembly);
            if (info != null && !string.IsNullOrEmpty(info.resolvedPath) && Directory.Exists(info.resolvedPath))
                return info.resolvedPath;
        }
        catch (Exception e) { Debug.LogWarning("UGUI: FindForAssembly failed: " + e.Message); }

        var editorDir = Path.GetDirectoryName(EditorApplication.applicationPath);
        if (!string.IsNullOrEmpty(editorDir))
        {
            var builtin = Path.Combine(editorDir, "Data/Resources/PackageManager/BuiltInPackages/com.unity.ugui");
            if (Directory.Exists(builtin)) return builtin;
        }

        var proj = Path.GetDirectoryName(Application.dataPath);
        var cache = Path.Combine(proj, "Library/PackageCache");
        if (Directory.Exists(cache))
        {
            foreach (var d in Directory.GetDirectories(cache, "com.unity.ugui*")) return d;
        }
        return null;
    }

    // class simple name -> package script guid
    //
    // AssetDatabase.FindAssets does not look inside Packages/ by default (it returned 0 ugui
    // scripts), so locate the package that owns UnityEngine.UI through PackageInfo and read its
    // .cs.meta files off disk.  Keys are file basenames, which is how uGUI names its scripts
    // (Button.cs -> UnityEngine.UI.Button), and the DLL side is keyed by the class simple name.
    static Dictionary<string, string> BuildPackageMap()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        string root = FindUguiRoot();
        if (string.IsNullOrEmpty(root))
        {
            Debug.LogError("UGUI: could not locate the package that owns UnityEngine.UI");
            return map;
        }
        Debug.Log("UGUI: package root " + root);

        foreach (var meta in Directory.GetFiles(root, "*.cs.meta", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(meta);
            name = name.Substring(0, name.Length - ".cs.meta".Length);
            if (map.ContainsKey(name)) continue;
            foreach (var line in File.ReadLines(meta))
            {
                if (line.StartsWith("guid:", StringComparison.Ordinal))
                {
                    map[name] = line.Substring(5).Trim();
                    break;
                }
            }
        }
        return map;
    }

    public static void Report()
    {
        var sb = new StringBuilder();
        sb.Append("dll guid ").Append(AssetDatabase.AssetPathToGUID(DllPath)).Append("  (expected ").Append(DllGuid).Append(")\n");
        var map = BuildPackageMap();
        sb.Append("com.unity.ugui scripts indexed: ").Append(map.Count).Append('\n');

        var objs = AssetDatabase.LoadAllAssetsAtPath(DllPath);
        sb.Append("assets inside the dll: ").Append(objs == null ? 0 : objs.Length).Append('\n');
        int resolved = 0, unresolved = 0;
        if (objs != null)
        {
            foreach (var o in objs)
            {
                var ms = o as MonoScript;
                if (ms == null) continue;
                string g; long localId;
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ms, out g, out localId)) continue;
                var cls = ms.GetClass();
                string csGuid = null;
                if (cls != null) map.TryGetValue(cls.FullName, out csGuid);
                if (csGuid == null && cls != null) map.TryGetValue(cls.Name, out csGuid);
                if (csGuid != null) resolved++;
                else { unresolved++; if (unresolved <= 20) sb.Append("  UNRESOLVED localId=").Append(localId).Append(" class=").Append(cls == null ? "<null>" : cls.FullName).Append('\n'); }
            }
        }
        sb.Append("dll scripts resolved to a package script: ").Append(resolved).Append("   unresolved: ").Append(unresolved).Append('\n');
        Debug.Log("UGUI-REPORT\n" + sb.ToString());
        EditorApplication.Exit(0);
    }

    public static void Run()
    {
        var map = BuildPackageMap();
        var localIdToPackageGuid = new Dictionary<long, string>();

        var objs = AssetDatabase.LoadAllAssetsAtPath(DllPath);
        if (objs == null) { Debug.LogError("UGUI: dll not found at " + DllPath); EditorApplication.Exit(1); return; }
        foreach (var o in objs)
        {
            var ms = o as MonoScript;
            if (ms == null) continue;
            string g; long localId;
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ms, out g, out localId)) continue;
            var cls = ms.GetClass();
            if (cls == null) continue;
            string csGuid;
            if (!map.TryGetValue(cls.FullName, out csGuid) && !map.TryGetValue(cls.Name, out csGuid)) continue;
            localIdToPackageGuid[localId] = csGuid;
        }
        Debug.Log("UGUI: resolved " + localIdToPackageGuid.Count + " dll script id(s) to package scripts");

        // rewrite every text asset that mentions the dll guid.  The dll's own .meta is NOT included:
        // it necessarily contains its own guid and is deleted along with the dll.
        var files = new List<string>();
        foreach (var ext in TextExtensions)
        {
            foreach (var f in Directory.GetFiles(Application.dataPath, "*" + ext, SearchOption.AllDirectories))
            {
                var p = f.Replace('\\', '/');
                if (p.EndsWith("/" + DllPath + ".meta", StringComparison.OrdinalIgnoreCase)) continue;
                files.Add(p);
            }
        }

        int changedFiles = 0, changedRefs = 0, unresolvedRefs = 0;
        Directory.CreateDirectory(BackupRoot);
        foreach (var abs in files)
        {
            string text;
            try { text = File.ReadAllText(abs); } catch { continue; }
            if (text.IndexOf(DllGuid, StringComparison.Ordinal) < 0) continue;

            var before = text;
            foreach (var kv in localIdToPackageGuid)
            {
                var from = "{fileID: " + kv.Key + ", guid: " + DllGuid;
                var to = "{fileID: 11500000, guid: " + kv.Value;
                int n = CountOccurrences(text, from);
                if (n > 0) { text = text.Replace(from, to); changedRefs += n; }
            }
            // anything left still pointing at the dll cannot be mapped
            int left = CountOccurrences(text, "guid: " + DllGuid);
            unresolvedRefs += left;

            if (text != before)
            {
                var rel = abs.Substring(abs.IndexOf("/Assets/", StringComparison.Ordinal) + 1);
                var backup = Path.Combine(BackupRoot, rel.Replace('/', '_'));
                if (!File.Exists(backup)) File.WriteAllText(backup, before);
                File.WriteAllText(abs, text);
                changedFiles++;
            }
        }
        Debug.Log("UGUI: rewrote " + changedRefs + " reference(s) in " + changedFiles + " file(s); unresolved " + unresolvedRefs);

        if (unresolvedRefs > 0)
        {
            Debug.LogError("UGUI: " + unresolvedRefs + " reference(s) could not be mapped - NOT deleting the dll");
            EditorApplication.Exit(1);
            return;
        }

        AssetDatabase.Refresh();
        if (AssetDatabase.DeleteAsset(DllPath))
        {
            Debug.Log("UGUI: deleted " + DllPath);
        }
        else
        {
            Debug.LogError("UGUI: could not delete " + DllPath);
            EditorApplication.Exit(1);
            return;
        }
        AssetDatabase.Refresh();
        Debug.Log("UGUI-OK");
        EditorApplication.Exit(0);
    }

    // Undoes Run(): rewrites the package script guids back to the DLL's local ids.  Used because
    // com.unity.ugui is NOT an active package in this project - uGUI is provided only by the
    // decompiled plugin DLL, so migrating off it removes UnityEngine.UI entirely (the project
    // then fails to compile and the Editor opens in Safe Mode).
    public static void Revert()
    {
        var map = BuildPackageMap();
        if (map.Count == 0) { Debug.LogError("UGUI: no package scripts indexed - cannot revert"); EditorApplication.Exit(1); return; }

        var localIdToPackageGuid = new Dictionary<long, string>();
        var objs = AssetDatabase.LoadAllAssetsAtPath(DllPath);
        if (objs != null)
        {
            foreach (var o in objs)
            {
                var ms = o as MonoScript;
                if (ms == null) continue;
                string g; long localId;
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ms, out g, out localId)) continue;
                var cls = ms.GetClass();
                if (cls == null) continue;
                string csGuid;
                if (!map.TryGetValue(cls.Name, out csGuid) && !map.TryGetValue(cls.FullName, out csGuid)) continue;
                localIdToPackageGuid[localId] = csGuid;
            }
        }
        Debug.Log("UGUI: revert mapping size " + localIdToPackageGuid.Count);

        var files = new List<string>();
        foreach (var ext in TextExtensions)
        {
            foreach (var f in Directory.GetFiles(Application.dataPath, "*" + ext, SearchOption.AllDirectories))
            {
                var p = f.Replace('\\', '/');
                if (p.EndsWith("/" + DllPath + ".meta", StringComparison.OrdinalIgnoreCase)) continue;
                files.Add(p);
            }
        }

        int changedFiles = 0, changedRefs = 0;
        foreach (var abs in files)
        {
            string text;
            try { text = File.ReadAllText(abs); } catch { continue; }
            var before = text;
            foreach (var kv in localIdToPackageGuid)
            {
                var from = "{fileID: 11500000, guid: " + kv.Value;
                var to = "{fileID: " + kv.Key + ", guid: " + DllGuid;
                int n = CountOccurrences(text, from);
                if (n > 0) { text = text.Replace(from, to); changedRefs += n; }
            }
            if (text != before) { File.WriteAllText(abs, text); changedFiles++; }
        }
        Debug.Log("UGUI: reverted " + changedRefs + " reference(s) in " + changedFiles + " file(s)");
        AssetDatabase.Refresh();
        Debug.Log("UGUI-REVERT-OK");
        EditorApplication.Exit(0);
    }

    static int CountOccurrences(string haystack, string needle)
    {
        int n = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }
}
