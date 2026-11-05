#if UNITY_EDITOR
using EPOOutline;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Bpsim.EditorTools
{
	public static class FactionEpoSetup
	{
		public const string RendererPath="Assets/MonoBehaviour/URP_2D_Asset_Renderer.asset";
		[MenuItem("Bpsim/Factions/Ensure Easy Performant Outline URP feature")]
		public static void InstallMenu() => Debug.Log(EnsureFeature());
		public static string EnsureFeature()
		{
			var data=AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererPath);
			if (data==null) throw new System.Exception("Missing active URP 2D renderer asset");
			foreach (var feature in data.rendererFeatures) if (feature is URPOutlineFeature) return "EPO URP feature already installed";
			var created=ScriptableObject.CreateInstance<URPOutlineFeature>(); created.name="Faction EPO Outline";
			AssetDatabase.AddObjectToAsset(created,data); data.rendererFeatures.Add(created); data.SetDirty(); EditorUtility.SetDirty(data);
			AssetDatabase.SaveAssetIfDirty(data);
			return "Installed asset URPOutlineFeature into active URP 2D renderer";
		}
	}
}
#endif
