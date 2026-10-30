using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-side tools for the bundled In-game Debug Console port.
/// </summary>
/// <remarks>
/// This project resolves uGUI and TextMeshPro from precompiled DLLs
/// (Assets/Plugins/UnityEngine.UI.dll, Assets/Plugins/Unity.TextMeshPro.dll) rather than from UPM
/// packages, so their MonoScripts live inside the DLL assets and carry an identity of
/// "fileID != 11500000, guid = dll guid". The asset-store prefabs reference the same components in
/// source form ("fileID: 11500000, guid = uGUI/TMP source guid"), which resolves to nothing here:
/// every Image, Button, ScrollRect, TMP text and layout group deserializes as a missing script, and
/// <c>DebugLogManager.Awake</c> aborts before it can initialize the console.
/// <para>
/// The staged prefabs were relinked offline by <c>Tools/relink_debugconsole.py</c>, which rewrites
/// those <c>m_Script</c> references to DLL form using the MonoScript fileID hash rule
/// (<c>int32_le(MD4("s\0\0\0" + namespace + class)[0..3])</c>). <see cref="VerifyPrefab"/> is how that
/// is checked; it must report zero missing slots, or the console throws at runtime.
/// </para>
/// </remarks>
public static class DebugConsoleRelink
{
	private static readonly string[] Dlls =
	{
		"Assets/Plugins/UnityEngine.UI.dll",
		"Assets/Plugins/Unity.TextMeshPro.dll",
	};

	/// <summary>Lists "typeName guid fileID" for every MonoScript inside the UI/TMP dlls.</summary>
	public static string DumpDllScripts()
	{
		StringBuilder report = new StringBuilder();
		foreach (string dll in Dlls)
		{
			Object[] assets = AssetDatabase.LoadAllAssetsAtPath(dll);
			report.AppendLine("=== " + dll + " (" + assets.Length + " assets) ===");
			foreach (Object asset in assets)
			{
				MonoScript script = asset as MonoScript;
				if (script == null)
				{
					continue;
				}
				string guid;
				long localId;
				AssetDatabase.TryGetGUIDAndLocalFileIdentifier(script, out guid, out localId);
				System.Type type = script.GetClass();
				report.AppendLine((type == null ? "<no class>" : type.Name) + " " + guid + " " + localId);
			}
		}
		return report.ToString();
	}

	/// <summary>
	/// Lists every component slot in a prefab that failed to resolve, by hierarchy path. A console
	/// prefab that reports anything but zero here will throw at runtime.
	/// </summary>
	public static string VerifyPrefab(string path)
	{
		GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
		if (root == null)
		{
			return "prefab not found: " + path;
		}
		List<string> missing = new List<string>();
		int total = 0;
		Walk(root.transform, "", missing, ref total);
		StringBuilder report = new StringBuilder();
		report.AppendLine(path + ": " + total + " component slots, " + missing.Count + " missing");
		foreach (string line in missing)
		{
			report.AppendLine("  MISSING " + line);
		}
		return report.ToString();
	}

	/// <summary>Verifies all three staged console prefabs in one call.</summary>
	public static string VerifyAll()
	{
		return VerifyPrefab("Assets/Resources/INDebugConsole.prefab")
			+ VerifyPrefab("Assets/Plugins/IngameDebugConsole/Prefabs/DebugLogItem.prefab")
			+ VerifyPrefab("Assets/Plugins/IngameDebugConsole/Prefabs/CommandSuggestion.prefab");
	}

	/// <summary>
	/// Queues a game-view capture. The console lives on a screen-space-overlay canvas, which no camera
	/// renders, so <c>Camera.Render</c>-based capture tools cannot see it - only the game view can.
	/// </summary>
	public static string CaptureGameView(string path)
	{
		ScreenCapture.CaptureScreenshot(path);
		return "scheduled " + path;
	}

	/// <summary>Checks that the TMP runtime assets the console needs actually resolve.</summary>
	public static string VerifyTmpAssets()
	{
		StringBuilder report = new StringBuilder();
		Object settings = AssetDatabase.LoadAssetAtPath<Object>("Assets/Resources/TMP Settings.asset");
		report.AppendLine("TMP Settings: " + (settings == null ? "MISSING" : "ok"));
		Object font = AssetDatabase.LoadAssetAtPath<Object>("Assets/Resources/fonts & materials/LiberationSans SDF.asset");
		report.AppendLine("LiberationSans SDF: " + (font == null ? "MISSING" : "ok"));
		Object mat = AssetDatabase.LoadAssetAtPath<Object>("Assets/Resources/fonts & materials/LiberationSans SDF Material.mat");
		report.AppendLine("LiberationSans SDF Material: " + (mat == null ? "MISSING" : "ok"));
		string[] shaders =
		{
			"Assets/Shader/TextMeshPro_Mobile_Distance Field.shader",
			"Assets/Shader/TextMeshPro_Distance Field.shader",
			"Assets/Shader/TextMeshPro_Sprite.shader",
		};
		foreach (string path in shaders)
		{
			Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
			report.AppendLine(path + ": " + (shader == null ? "MISSING" : "ok"));
		}
		return report.ToString();
	}

	private static void Walk(Transform node, string path, List<string> missing, ref int total)
	{
		string here = path + "/" + node.name;
		Component[] components = node.GetComponents<Component>();
		for (int index = 0; index < components.Length; index++)
		{
			total++;
			if (components[index] == null)
			{
				missing.Add(here + " slot #" + index);
			}
		}
		for (int index = 0; index < node.childCount; index++)
		{
			Walk(node.GetChild(index), here, missing, ref total);
		}
	}
}
