using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 将静态品牌词标注册到 Unity 原生启动画面，兼容 Personal 许可证。
/// 启动画面结束后直接进入 MainScene，不再播放独立品牌场景。
/// </summary>
public static class BpsimBrandSplashBuilder
{
	public const string SplashScenePath = "Assets/Scenes/BrandSplash.unity";
	public const string MainScenePath = "Assets/Scenes/MainScene.unity";
	public const string LogoPath = "Assets/Texture2D/Bpsim_Splash_Logo.png";

	[MenuItem("Bpsim/Brand/Configure Native Splash")]
	public static void BuildMenu()
	{
		Debug.Log(Build());
	}

	public static void BuildBatch()
	{
		Debug.Log(Build());
	}

	public static string Build()
	{
		TextureImporter importer = AssetImporter.GetAtPath(LogoPath) as TextureImporter;
		if (importer == null)
		{
			throw new InvalidOperationException("Missing splash logo: " + LogoPath);
		}
		importer.textureType = TextureImporterType.Sprite;
		importer.spriteImportMode = SpriteImportMode.Single;
		TextureImporterSettings textureSettings = new TextureImporterSettings();
		importer.ReadTextureSettings(textureSettings);
		textureSettings.spriteMeshType = SpriteMeshType.FullRect;
		importer.SetTextureSettings(textureSettings);
		importer.mipmapEnabled = false;
		importer.alphaIsTransparency = true;
		importer.wrapMode = TextureWrapMode.Clamp;
		importer.filterMode = FilterMode.Bilinear;
		importer.maxTextureSize = 2048;
		importer.npotScale = TextureImporterNPOTScale.None;
		importer.textureCompression = TextureImporterCompression.Uncompressed;
		importer.SaveAndReimport();

		Sprite logo = AssetDatabase.LoadAssetAtPath<Sprite>(LogoPath);
		if (logo == null)
		{
			throw new InvalidOperationException("Splash logo must be imported as a Sprite: " + LogoPath);
		}
		PlayerSettings.SplashScreen.show = true;
		PlayerSettings.SplashScreen.showUnityLogo = true;
		PlayerSettings.SplashScreen.drawMode = PlayerSettings.SplashScreen.DrawMode.UnityLogoBelow;
		PlayerSettings.SplashScreen.unityLogoStyle = PlayerSettings.SplashScreen.UnityLogoStyle.LightOnDark;
		PlayerSettings.SplashScreen.backgroundColor = new Color(10f / 255f, 13f / 255f, 20f / 255f, 1f);
		PlayerSettings.SplashScreen.logos = new[] { PlayerSettings.SplashScreenLogo.Create(2f, logo) };

		List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
		scenes.RemoveAll(s => string.Equals(s.path, SplashScenePath, StringComparison.OrdinalIgnoreCase)
			|| string.Equals(s.path, MainScenePath, StringComparison.OrdinalIgnoreCase));
		scenes.Insert(0, new EditorBuildSettingsScene(MainScenePath, true));
		EditorBuildSettings.scenes = scenes.ToArray();
		AssetDatabase.SaveAssets();
		return "[BrandSplash] Native splash: bpsim logo + Made with Unity, 2s; first scene: MainScene.";
	}
}
