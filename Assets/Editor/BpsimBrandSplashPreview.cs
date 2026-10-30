using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 开场品牌页的渲染验证工具（batchmode 可用）：不进入 Play Mode，
/// 直接反射调用 BrandSplashController.BuildUi() 建出同一套 uGUI，再把指定时刻的动画帧
/// 渲染到 RenderTexture 存 PNG——用来确认贴图确实以 Sprite 导入、遮罩/水线/闪电都在。
/// </summary>
public static class BpsimBrandSplashPreview
{
	const int W = 1920;
	const int H = 1080;
	static readonly string[] Sprites = { "letters", "sea", "currents", "wave", "bubble", "f", "dot", "fwhite", "bloom", "mwu" };

	[MenuItem("Bpsim/Brand/Capture Splash Frames")]
	public static void CaptureMenu()
	{
		Debug.Log(Capture());
	}

	public static void CaptureEntry()
	{
		Debug.Log(Capture());
	}

	public static string Capture()
	{
		StringBuilder sb = new StringBuilder();
		sb.Append("-- sprites --\n");
		foreach (string name in Sprites)
		{
			Sprite sp = Resources.Load<Sprite>("BrandSplash/" + name);
			sb.Append(name).Append(": ").Append(sp == null ? "NULL" : sp.rect.width + "x" + sp.rect.height + " tex=" + sp.texture.width + "x" + sp.texture.height).Append('\n');
		}

		UnityEngine.SceneManagement.Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
		GameObject host = new GameObject("BrandSplashPreview");
		Bpsim.UI.BrandSplashController controller = host.AddComponent<Bpsim.UI.BrandSplashController>();

		MethodInfo buildUi = typeof(Bpsim.UI.BrandSplashController).GetMethod("BuildUi", BindingFlags.Instance | BindingFlags.NonPublic);
		MethodInfo update = typeof(Bpsim.UI.BrandSplashController).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
		FieldInfo tField = typeof(Bpsim.UI.BrandSplashController).GetField("m_t", BindingFlags.Instance | BindingFlags.NonPublic);
		FieldInfo startedField = typeof(Bpsim.UI.BrandSplashController).GetField("m_started", BindingFlags.Instance | BindingFlags.NonPublic);
		if (startedField != null)
		{
			startedField.SetValue(controller, true);
		}
		if (buildUi == null || update == null || tField == null)
		{
			sb.Append("reflection failed: buildUi=").Append(buildUi != null).Append(" update=").Append(update != null).Append(" m_t=").Append(tField != null).Append('\n');
			return sb.ToString();
		}
		buildUi.Invoke(controller, null);
		sb.Append("-- ui built --\n");

		Transform canvasTr = host.transform.Find("BrandSplashCanvas");
		Canvas canvas = canvasTr == null ? null : canvasTr.GetComponent<Canvas>();
		if (canvas == null)
		{
			sb.Append("canvas missing\n");
			return sb.ToString();
		}
		sb.Append("canvas children=").Append(canvasTr.childCount).Append('\n');

		GameObject camGo = new GameObject("PreviewCamera");
		Camera cam = camGo.AddComponent<Camera>();
		cam.clearFlags = CameraClearFlags.SolidColor;
		cam.backgroundColor = new Color(0.13725491f, 0.12156863f, 0.1254902f, 1f);
		cam.orthographic = true;
		cam.orthographicSize = H * 0.5f;
		cam.transform.position = new Vector3(0f, 0f, -100f);
		cam.transform.rotation = Quaternion.identity;
		cam.nearClipPlane = 0.1f;
		cam.farClipPlane = 500f;

		canvas.renderMode = RenderMode.ScreenSpaceCamera;
		canvas.worldCamera = cam;
		canvas.planeDistance = 50f;

		RenderTexture rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
		rt.antiAliasing = 2;
		cam.targetTexture = rt;
		Canvas.ForceUpdateCanvases();

		string dir = "Assets/../Tools/_final";
		string abs = Path.GetFullPath(dir);
		if (!Directory.Exists(abs))
		{
			Directory.CreateDirectory(abs);
		}

		float[] times = { 0.55f, 1.25f, 2.05f, 3.05f };
		RenderTexture prev = RenderTexture.active;
		foreach (float t in times)
		{
			tField.SetValue(controller, t);
			update.Invoke(controller, null);
			Canvas.ForceUpdateCanvases();
			cam.Render();
			RenderTexture.active = rt;
			Texture2D tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
			tex.ReadPixels(new Rect(0f, 0f, W, H), 0, 0);
			tex.Apply();
			RenderTexture.active = prev;
			string file = Path.Combine(abs, "rt_splash_" + ((int)(t * 100)).ToString("D3") + ".png");
			File.WriteAllBytes(file, tex.EncodeToPNG());
			UnityEngine.Object.DestroyImmediate(tex);
			sb.Append("frame t=").Append(t.ToString("0.00")).Append(" -> ").Append(file).Append('\n');
		}
		cam.targetTexture = null;
		RenderTexture.active = prev;
		UnityEngine.Object.DestroyImmediate(rt);
		sb.Append("done");
		return sb.ToString();
	}
}
