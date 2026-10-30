// Diagnostic helper for the "missing script" warnings emitted by CoreManager.Awake.
// Callable from the DSH bridge: invoke_static BpsimWarningsAudit MissingScripts
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class BpsimWarningsAudit
{
    private static readonly string[] Prefabs =
    {
        "Assets/GameObject/StartPage.prefab",
        "Assets/GameObject/SpriteManager.prefab",
        "Assets/GameObject/PartMananger.prefab",
        "Assets/GameObject/UserInterface.prefab",
        "Assets/GameObject/SimulationInterface.prefab",
    };

    public static string MissingScripts()
    {
        var sb = new StringBuilder();
        foreach (string path in Prefabs)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                sb.AppendLine(path + ": LOAD FAILED");
                continue;
            }
            sb.AppendLine(path + ": totalMissing=" + GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(prefab));
            foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
            {
                int n = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                if (n <= 0) continue;
                sb.AppendLine("  GO " + t.name + " missing=" + n);
                var so = new SerializedObject(t.gameObject);
                var comps = so.FindProperty("m_Component");
                for (int i = 0; comps != null && i < comps.arraySize; i++)
                {
                    var sp = comps.GetArrayElementAtIndex(i).FindPropertyRelative("component");
                    var obj = sp != null ? sp.objectReferenceValue : null;
                    sb.AppendLine("    [" + i + "] " + (obj == null ? "NULL/MISSING" : obj.GetType().FullName));
                }
            }
        }
        return sb.ToString();
    }

    public static string DllScriptIds()
    {
        var sb = new StringBuilder();
        const string dll = "Assets/Plugins/UnityEngine.UI.dll";
        var assets = AssetDatabase.LoadAllAssetsAtPath(dll);
        sb.AppendLine(dll + ": subAssets=" + assets.Length);
        string guid;
        long localId;
        foreach (var a in assets)
        {
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(a, out guid, out localId))
            {
                sb.AppendLine("  " + localId + "  " + a.name + "  (" + (a as MonoScript != null ? "MonoScript" : a.GetType().Name) + ")");
            }
        }
        return sb.ToString();
    }
}
