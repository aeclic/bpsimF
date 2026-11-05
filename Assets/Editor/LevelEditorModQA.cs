#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using Bpsim.Modding;
using Bpsim.Mods;
using Bpsim.Parts;
using Bpsim.Parts.Simulation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.EditorTools
{
	[InitializeOnLoad]
	public static class LevelEditorModQA
	{
		private const string Prefix = "Bpsim.LevelEditorModQA.";
		private static readonly string[] PreferenceKeys = { "Bpsim.Session.Level", "Bpsim.Session.ScenePath", "Bpsim.Session.SceneFormat" };

		static LevelEditorModQA() { EditorApplication.playModeStateChanged += StateChanged; }

		public static string Start() { return Schedule(false); }
		public static string StartPreview() { return Schedule(true); }
		public static string StartCanvasPreview() { return Schedule(true, true); }
		public static string Status() { return SessionState.GetString(Prefix + "Report", "{}"); }
		public static string PreviewUrl() { return SessionState.GetString(Prefix + "Url", string.Empty); }
		public static void FinishPreview() { SessionState.SetBool(Prefix + "Finish", true); }
		public static void EnterScheduled()
		{
			// Only remove this QA's old delayed callback, never other editor callbacks.
			if (EditorApplication.delayCall != null)
			{
				foreach (Delegate callback in EditorApplication.delayCall.GetInvocationList())
				{
					string type = callback.Method.DeclaringType?.FullName;
					if (type == typeof(LevelEditorModQA).FullName || (type != null && type.StartsWith(typeof(LevelEditorModQA).FullName + "+", StringComparison.Ordinal)))
						EditorApplication.delayCall -= (EditorApplication.CallbackFunction)callback;
				}
			}
			if (SessionState.GetBool(Prefix + "Pending", false)) EditorApplication.isPlaying = true;
		}

		private static string Schedule(bool preview, bool canvasOnly = false)
		{
			if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
				throw new InvalidOperationException("Run this QA from stable Edit Mode; it does not interrupt an existing game.");
			SceneAsset mainScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/MainScene.unity");
			if (mainScene == null) throw new InvalidOperationException("The gameplay MainScene is missing.");
			SessionState.SetString(Prefix + "PlayModeStartScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
			EditorSceneManager.playModeStartScene = mainScene;
			JObject preferences = new JObject();
			foreach (string key in PreferenceKeys)
			{
				preferences[key] = new JObject
				{
					["exists"] = PlayerPrefs.HasKey(key),
					["value"] = key.EndsWith("SceneFormat", StringComparison.Ordinal) ? (JToken)PlayerPrefs.GetInt(key) : PlayerPrefs.GetString(key)
				};
				PlayerPrefs.DeleteKey(key);
			}
			PlayerPrefs.Save();
			SessionState.SetString(Prefix + "Preferences", preferences.ToString(Formatting.None));
			SessionState.SetString(Prefix + "ConfigPath", ModPaths.ConfigPath);
			SessionState.SetBool(Prefix + "ConfigExists", File.Exists(ModPaths.ConfigPath));
			SessionState.SetString(Prefix + "Config", File.Exists(ModPaths.ConfigPath) ? Convert.ToBase64String(File.ReadAllBytes(ModPaths.ConfigPath)) : string.Empty);
			// A user may already have 0.1 installed. Do not boot its DLL into this QA's domain.
			// Only a temporary disabled flag is changed; original XML bytes are restored on exit.
			ModdingConfig config = ModdingConfig.Load(ModPaths.ConfigPath);
			config.SetEnabled(Guid.Parse("e47b018a-61a6-4e79-a789-c5f627a00aa4"), false);
			config.Save(ModPaths.ConfigPath);
			SessionState.SetBool(Prefix + "Pending", true);
			SessionState.SetBool(Prefix + "Preview", preview);
			SessionState.SetBool(Prefix + "CanvasOnly", canvasOnly);
			SessionState.SetBool(Prefix + "Finish", false);
			SessionState.SetString(Prefix + "Report", "{\"phase\":\"scheduled\"}");
			EnterScheduled();
			return "scheduled";
		}

		private static void StateChanged(PlayModeStateChange state)
		{
			if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Prefix + "Pending", false))
			{
				GameObject runner = new GameObject("LevelEditorModQaRunner");
				runner.AddComponent<LevelEditorModQaRunner>();
			}
			if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Prefix + "Pending", false))
			{
				Restore();
			}
		}

		private static void Restore()
		{
			string startScene = SessionState.GetString(Prefix + "PlayModeStartScene", string.Empty);
			EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(startScene) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(startScene);
			string snapshot = SessionState.GetString(Prefix + "Preferences", "{}");
			JObject preferences = JObject.Parse(snapshot);
			foreach (string key in PreferenceKeys)
			{
				JObject item = preferences[key] as JObject;
				if (item == null || (bool?)item["exists"] != true) PlayerPrefs.DeleteKey(key);
				else if (key.EndsWith("SceneFormat", StringComparison.Ordinal)) PlayerPrefs.SetInt(key, (int)item["value"]);
				else PlayerPrefs.SetString(key, (string)item["value"]);
			}
			PlayerPrefs.Save();
			string config = SessionState.GetString(Prefix + "Config", string.Empty);
			string configPath = SessionState.GetString(Prefix + "ConfigPath", string.Empty);
			if (!string.IsNullOrEmpty(config) && !string.IsNullOrEmpty(configPath)) File.WriteAllBytes(configPath, Convert.FromBase64String(config));
			else if (!SessionState.GetBool(Prefix + "ConfigExists", false) && File.Exists(configPath)) File.Delete(configPath);
			SessionState.SetBool(Prefix + "Pending", false);
			SessionState.EraseString(Prefix + "Url");
			JObject report = JObject.Parse(Status());
			report["restoredEditMode"] = true;
			SessionState.SetString(Prefix + "Report", report.ToString(Formatting.None));
			string project = Path.GetDirectoryName(Application.dataPath);
			string output = Path.Combine(project, "_codex_work", "level-editor-mod", "unity-qa.json");
			File.WriteAllText(output, report.ToString(Formatting.Indented), new UTF8Encoding(false));
		}

		internal static void Report(JObject value) { SessionState.SetString(Prefix + "Report", value.ToString(Formatting.None)); }
		internal static bool Hold => SessionState.GetBool(Prefix + "Preview", false);
		internal static bool CanvasOnly => SessionState.GetBool(Prefix + "CanvasOnly", false);
		internal static bool Finish => SessionState.GetBool(Prefix + "Finish", false);
		internal static void SetUrl(string url) { SessionState.SetString(Prefix + "Url", url); }
	}

	public sealed class LevelEditorModQaRunner : MonoBehaviour
	{
		private readonly JObject m_report = new JObject { ["phase"] = "running", ["passed"] = false };
		private ModPackage m_package;
		private ModPackage m_previousPackage;
		private ModManager m_manager;
		private ModWebService m_service;
		private ModDataStore m_store;
		private string m_testFile;
		private string m_testId;
		private Vector3 m_anchor;
		private int m_checks;

		private IEnumerator Start()
		{
			float deadline = Time.realtimeSinceStartup + 40f;
			while ((PartManager.Instance == null || !PartManager.Instance.IsReady) && Time.realtimeSinceStartup < deadline) yield return null;
			try { RunChecks(); m_report["passed"] = true; }
			catch (Exception ex) { m_report["error"] = ex.ToString(); Debug.LogException(ex); }
			m_report["canvasOnly"] = LevelEditorModQA.CanvasOnly;
			if ((bool?)m_report["passed"] == true && !LevelEditorModQA.CanvasOnly)
			{
				IEnumerator runtime = RuntimeChecks();
				while (true)
				{
					bool more = false; object next = null;
					try { more = runtime.MoveNext(); if (more) next = runtime.Current; }
					catch (Exception ex) { m_report["passed"] = false; m_report["error"] = ex.ToString(); Debug.LogException(ex); }
					if (!more) break;
					yield return next;
				}
				(runtime as IDisposable)?.Dispose();
			}
			m_report["checks"] = m_checks;
			if ((bool?)m_report["passed"] == true && LevelEditorModQA.Hold)
			{
				m_package.EntryPoint.GetType().GetMethod("OpenEditor").Invoke(m_package.EntryPoint, null);
				LevelEditorModQA.SetUrl((string)m_package.EntryPoint.GetType().GetProperty("EditorUrl").GetValue(m_package.EntryPoint));
				m_report["phase"] = "awaiting-ui";
				LevelEditorModQA.Report(m_report);
				float end = Time.realtimeSinceStartup + 900f;
				while (!LevelEditorModQA.Finish && Time.realtimeSinceStartup < end) yield return null;
			}
			try { Cleanup(); }
			catch (Exception ex) { m_report["cleanupError"] = ex.ToString(); m_report["passed"] = false; }
			yield return null;
			yield return null;
			try
			{
				Check(ButtonCount() == 0, "No menu entry after disable");
				Check(m_service == null || !m_service.IsRunning, "No listener after disable");
				Check(ModFluidSession.ActiveVolumeCount == 0, "No fluid volumes after disable");
				Check(Resources.FindObjectsOfTypeAll<Camera>().Count(camera => camera.name == "LevelEditorWaterCaptureCamera" && camera.gameObject.activeInHierarchy) == 0,
					"No water capture camera after disable");
				Check(Resources.FindObjectsOfTypeAll<GameObject>().Count(go => go.name == "BpsimLevelEditorModWindow" && go.activeInHierarchy) == 0,
					"No active editor window after disable");
			}
			catch (Exception ex) { m_report["cleanupError"] = ex.ToString(); m_report["passed"] = false; }
			m_report["checks"] = m_checks;
			m_report["phase"] = "complete";
			LevelEditorModQA.Report(m_report);
			EditorApplication.isPlaying = false;
		}

		private void RunChecks()
		{
			Check(PartManager.Instance != null && PartManager.Instance.IsReady, "Part world ready");
			JObject mathReport = JObject.Parse(LevelEditorWaterQA.RunMath());
			m_report["waterMath"] = mathReport;
			Check((bool?)mathReport["passed"] == true, "Fluid geometry and stable force math");
			m_manager = ModManager.Ensure();
			Guid modId = Guid.Parse("e47b018a-61a6-4e79-a789-c5f627a00aa4");
			m_previousPackage = m_manager.Find(modId.ToString("D"));
			Check(m_previousPackage == null || !m_previousPackage.IsLoaded, "Existing installed Mod is temporarily disabled, not overwritten");
			if (m_previousPackage != null) { m_manager.Packages.Remove(m_previousPackage); ModRegistry.Rebuild(m_manager.Packages); }
			Check(ButtonCount() == 0, "No menu entry without Mod");
			Check(ModFluidSession.ActiveVolumeCount == 0, "No fluid system registrations without Mod");
			int baseline = ModLevels.BuiltInLevels().Length;
			Check(baseline > 0, "Built-in catalog available");
			foreach (ModLevelDefinition info in ModLevels.BuiltInLevels()) ModLevels.Validate(ModLevels.ReadBuiltIn(info.Key));
			m_report["validatedBuiltInLevels"] = baseline;

			string modDirectory = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Mods", "LevelEditor");
			string error;
			ModManifest manifest = ModLoader.ReadManifest(modDirectory, out error);
			Check(manifest != null, "Manifest parses: " + error);
			Assembly assembly = Assembly.LoadFrom(Path.Combine(modDirectory, "BpsimLevelEditor.dll"));
			ModScanResult scan = ModAssemblyScanner.Scan(assembly, true);
			m_report["scanWarnings"] = JArray.FromObject(scan.Warnings);
			Check(scan.Passed, "Strict Mod scan: " + scan.Describe());
			m_package = new ModPackage { Directory = modDirectory, Manifest = manifest, Source = ModSource.Additional, Enabled = true };
			m_manager.Packages.Add(m_package);
			ModRegistry.Rebuild(m_manager.Packages);
			m_manager.LoadPackage(m_package);
			Check(m_package.IsLoaded, "Mod loads: " + m_package.Error);
			Check(ButtonCount() == 1, "One menu entry when enabled");
			Check(ModFluidSession.ActiveVolumeCount == 0, "Enabling alone does not register any fluid volumes");
			Check(Resources.FindObjectsOfTypeAll<GameObject>().Count(go => go.name.StartsWith("ModWebService_", StringComparison.Ordinal) && go.activeInHierarchy) == 0,
				"Enabling alone does not start the web service");
			m_manager.LoadPackage(m_package);
			Check(ButtonCount() == 1, "Repeated enable does not duplicate the entry");

			m_package.EntryPoint.GetType().GetMethod("OpenEditor").Invoke(m_package.EntryPoint, null);
			m_service = (ModWebService)m_package.EntryPoint.GetType().GetField("m_server", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(m_package.EntryPoint);
			Check(m_service != null && m_service.IsRunning, "Web service starts on opening");
			Check(HttpStatus(m_service.Origin + "/index.html") == 200, "Offline editor entry is served");
			Check(HttpStatus(m_service.Origin + "/api/session") == 401, "API rejects missing session token");
			Check(HttpStatus(m_service.Origin + "/index.html", "http://evil.invalid") == 403, "Service rejects cross-origin requests");
			Check(HttpStatus(m_service.Origin + "/../Mod.xml") != 200, "No parent-directory asset reads");

			JObject session = Call("GET", "/api/session", null);
			JObject document = (JObject)session["document"].DeepClone();
			m_testId = (string)document["id"];
			m_testFile = Guid.Parse(m_testId).ToString("N") + ".bpslevel";
			m_store = ModDataStore.Open(m_package.EntryPoint, "Levels");
			document["title"] = "QA · 关卡编辑器 Mod";
			document["extensions"] = new JObject { ["future_test"] = new JObject { ["opaque_date"] = "2026-11-03T00:00:00+08:00", ["values"] = new JArray(1, "保留", true) } };
			ModLevels.SubscribeLoaded(m_package.EntryPoint, loaded => m_anchor = loaded.Anchor);
			Call("POST", "/api/play", document);
			Check(ModLevels.CaptureCurrent() != null, "Complete level imports into the game");
			JObject water = new JObject
			{
				["id"] = Guid.NewGuid().ToString("D"), ["name"] = "QA 水域",
				["x"] = m_anchor.x / 8f - 2f, ["surface_y"] = m_anchor.y / 8f + 1f, ["width"] = 6f, ["depth"] = 2f,
				["color"] = "#339eb4", ["opacity"] = .45f, ["wave_amplitude"] = .03f, ["wave_speed"] = 1f,
				["physics_enabled"] = false, ["future_water_parameter"] = "保留"
			};
			document["water_bodies"] = new JArray(water);
			Call("POST", "/api/play", document);
			JObject saved = JsonConvert.DeserializeObject<JObject>(m_store.ReadText(m_testFile),
				new JsonSerializerSettings { DateParseHandling = DateParseHandling.None, TypeNameHandling = TypeNameHandling.None });
			Check(JToken.DeepEquals(document["extensions"], saved["extensions"]), "Unknown extension fields survive save");
			JToken canonicalWater = JsonConvert.DeserializeObject<JToken>(document["water_bodies"].ToString(Formatting.None),
				new JsonSerializerSettings { DateParseHandling = DateParseHandling.None, TypeNameHandling = TypeNameHandling.None });
			foreach (JProperty property in ((JObject)canonicalWater[0]).Properties())
				Check(JToken.DeepEquals(property.Value, saved["water_bodies"][0][property.Name]), "Provided water field survives migration: " + property.Name);
			Check((int?)saved["water_schema_version"] == 2 && (bool?)saved["water_bodies"][0]["physics_enabled"] == false, "Old visual-only water migrates without silently enabling physics");
			Check((float?)saved["water_bodies"][0]["density"] == 1f, "Old water gains the shared Web/Unity density default");
			Check((string)document["level_bytes"] == (string)saved["level_bytes"], "Original level and prefab overrides are preserved");
			Check(Resources.FindObjectsOfTypeAll<GameObject>().Any(go => go.name == "LevelEditorWater" && go.activeInHierarchy),
				"Water is generated for the imported Mod level");
			Check(ModFluidSession.ActiveVolumeCount == 1, "One water registration after level replacement, no leaked duplicate");
			JObject malformed = (JObject)document.DeepClone();
			malformed["water_bodies"][0]["width"] = -1;
			JObject rejected = CallRaw("POST", "/api/play", malformed);
			Check((bool?)rejected["ok"] == false, "Invalid water dimensions are rejected");
			Check(ModLevels.CaptureCurrent() != null, "Rejected import keeps the existing level");
			JObject reopened = Call("POST", "/api/open", new JObject { ["id"] = m_testId });
			Check(JToken.DeepEquals(saved["water_bodies"], reopened["document"]["water_bodies"]), "Saved water document can be reopened");
			JObject advanced = (JObject)saved.DeepClone();
			advanced["water_bodies"][0]["physics_enabled"] = true;
			advanced["water_bodies"][0]["flow_x"] = -.5f;
			advanced["water_bodies"][0]["refraction_strength"] = .08f;
			advanced["water_bodies"][0]["reflection_strength"] = .4f;
			Call("POST", "/api/play", advanced);
			Check(ModFluidSession.ActiveVolumeCount == 1, "Physics-enabled water imports without duplicate registrations");
			JObject advancedReopened = Call("POST", "/api/open", new JObject { ["id"] = m_testId });
			JToken canonicalAdvanced = JsonConvert.DeserializeObject<JToken>(advanced["water_bodies"].ToString(Formatting.None),
				new JsonSerializerSettings { DateParseHandling = DateParseHandling.None, TypeNameHandling = TypeNameHandling.None });
			Check(JToken.DeepEquals(canonicalAdvanced, advancedReopened["document"]["water_bodies"]), "Physics/flow/visual parameters roundtrip together");
			malformed = (JObject)advanced.DeepClone(); malformed["water_bodies"][0]["flow_x"] = "NaN";
			Check((bool?)CallRaw("POST", "/api/play", malformed)["ok"] == false, "Invalid current cannot reach physics jobs");
		}

		private IEnumerator RuntimeChecks()
		{
			PartManager manager = PartManager.Instance;
			SimulationSettings settings = UserSettings.Instance.SimulationSettings;
			float gravityX = settings.GravityX, gravityY = settings.GravityY, step = settings.FixedTimeStep;
			bool mega = settings.MegaStructure;
			float breakSpeed = settings.MegaStructureBreakSpeed, timeScale = Time.timeScale;
			JObject preview = (JObject)Call("GET", "/api/session", null)["document"].DeepClone();
			JObject test = (JObject)preview.DeepClone();
			JObject pool = (JObject)test["water_bodies"][0];
			test["level_bytes"] = Convert.ToBase64String(new byte[4]);
			test["starter"] = "5,0,0,2,0,0\n";
			test["starter_format"] = "csv";
			test["parallax_enabled"] = false;
			pool["x"] = -4; pool["surface_y"] = 0; pool["width"] = 8; pool["depth"] = 10;
			pool["physics_enabled"] = true; pool["wave_amplitude"] = 0; pool["flow_x"] = 0;
			pool["density"] = 1.5f; pool["linear_drag"] = 1.8f; pool["angular_drag"] = 1.2f;
			try
			{
				Time.timeScale = 1;
				settings.GravityX = 0; settings.GravityY = -9.81f; settings.FixedTimeStep = 1f / 60;
				settings.MegaStructure = false; settings.MegaStructureBreakSpeed = 0;
				Call("POST", "/api/play", test);
				yield return WaitReal(.4f);
				GameObject editorWindow = GameObject.Find("BpsimLevelEditorModWindow");
				GameObject[] activeEditorWindows = Resources.FindObjectsOfTypeAll<GameObject>()
					.Where(go => go != null && go.name == "BpsimLevelEditorModWindow" && go.activeInHierarchy).ToArray();
				MonoBehaviour[] editorWebViews = Resources.FindObjectsOfTypeAll<MonoBehaviour>()
					.Where(item => item != null && item.GetType().Name == "CanvasWebViewPrefab" && item.gameObject.activeInHierarchy).ToArray();
				JArray editorWebViewStates = new JArray();
				int visibleEditorWebViews = 0;
				foreach (MonoBehaviour item in editorWebViews)
				{
					PropertyInfo visible = item.GetType().GetProperty("Visible");
					bool isVisible = visible != null && (bool)visible.GetValue(item);
					if (isVisible) visibleEditorWebViews++;
					editorWebViewStates.Add(new JObject { ["name"] = item.gameObject.name, ["visible"] = isVisible });
				}
				m_report["activeEditorWindowCount"] = activeEditorWindows.Length;
				m_report["activeEditorWebViews"] = editorWebViewStates;
				Check(activeEditorWindows.Length == 0, "Editor window is destroyed after a level enters gameplay");
				Check(visibleEditorWebViews == 0, "No active editor WebView remains visible during gameplay");
				if (editorWindow != null)
				{
					MonoBehaviour component = editorWindow.GetComponents<MonoBehaviour>().FirstOrDefault(item => item != null && item.GetType().Name == "EditorWindow");
					FieldInfo initializedField = component == null ? null : component.GetType().GetField("m_initialized", BindingFlags.NonPublic | BindingFlags.Instance);
					FieldInfo visibleField = component == null ? null : component.GetType().GetField("m_visible", BindingFlags.NonPublic | BindingFlags.Instance);
					FieldInfo webViewField = component == null ? null : component.GetType().GetField("m_webView", BindingFlags.NonPublic | BindingFlags.Instance);
					Component webView = webViewField == null || component == null ? null : webViewField.GetValue(component) as Component;
					PropertyInfo webViewVisibleProperty = webView == null ? null : webView.GetType().GetProperty("Visible");
					Canvas editorCanvas = editorWindow.GetComponentInChildren<Canvas>(true);
					Transform frame = editorWindow.transform.Find("LevelEditorModCanvas/EditorFrame");
					bool initialized = initializedField != null && (bool)initializedField.GetValue(component);
					m_report["editorWindowAfterPlay"] = new JObject
					{
						["visible"] = visibleField != null && (bool)visibleField.GetValue(component),
						["initialized"] = initialized,
						["rootActive"] = editorWindow.activeInHierarchy,
						["canvasEnabled"] = editorCanvas != null && editorCanvas.enabled,
						["frameActive"] = frame != null && frame.gameObject.activeInHierarchy,
						["webViewActive"] = webView != null && webView.gameObject.activeInHierarchy,
						["webViewVisible"] = webViewVisibleProperty != null && (bool)webViewVisibleProperty.GetValue(webView)
					};
					bool viewHidden = editorWindow == null || !editorWindow.activeInHierarchy ||
						(visibleField != null && !(bool)visibleField.GetValue(component) &&
						(editorCanvas == null || !editorCanvas.enabled) && webViewVisibleProperty != null &&
						!(bool)webViewVisibleProperty.GetValue(webView));
					Check(viewHidden, "Editor WebView surface and input are hidden after a level enters gameplay");
					Check(!initialized || editorWindow == null || !editorWindow.activeInHierarchy,
						"Initialized editor window is deactivated after gameplay starts");
				}
				else Check(true, "Editor WebView is absent after a level enters gameplay");
				SceneCamera.Instance.MoveTo(new Vector2(0, -2));
				SceneCamera.Instance.ScaleTo(8);
				yield return WaitReal(.5f);
				MonoBehaviour surface = WaterComponent("WaterPreview"), world = WaterComponent("WaterWorld");
				Check(surface != null && world != null, "Water runtime components exist");
				MeshRenderer renderer = surface.GetComponent<MeshRenderer>();
				MeshRenderer surfaceRenderer = (MeshRenderer)surface.GetType().GetProperty("SurfaceRenderer").GetValue(surface);
				Check(renderer != null && renderer.sharedMaterial.shader.name == "BpsimLevelEditor/UltimateUnderwater" && renderer.sharedMaterial.shader.isSupported &&
					surfaceRenderer != null && surfaceRenderer.sharedMaterial.shader.name == "BpsimLevelEditor/UltimateWaterSurface" && surfaceRenderer.sharedMaterial.shader.isSupported,
					"Original surface and underwater shader programs load on the active graphics API");
				UnityEngine.Material partMaterial = AssetDatabase.LoadAssetAtPath<UnityEngine.Material>("Assets/Material/Part_Texture_0.mat");
				Check(surfaceRenderer != null && partMaterial != null &&
					surfaceRenderer.sharedMaterial.renderQueue < partMaterial.renderQueue &&
					partMaterial.renderQueue < renderer.sharedMaterial.renderQueue,
					"Asset surface draws behind parts and underwater compositing draws after parts");
				Check(Convert.ToInt32(world.GetType().GetProperty("CaptureCount").GetValue(world)) > 0, "URP scene capture actually renders");
				RenderTexture scene = (RenderTexture)world.GetType().GetProperty("SceneTexture").GetValue(world);
				RenderTexture terrain = (RenderTexture)world.GetType().GetProperty("TerrainTexture").GetValue(world);
				Check(scene != null && scene.IsCreated() && terrain != null && terrain.IsCreated(), "Refraction and terrain-mask textures are live");
				Check(renderer.sharedMaterial.GetTexture("_CameraSortingLayerTexture") == scene && surfaceRenderer.sharedMaterial.GetTexture("_LocalReflectionMap") is RenderTexture && surfaceRenderer.sharedMaterial.GetTexture("_LocalReflectionMap") != scene,
					"Original water layers are bound to their scene capture");
				Texture normal = surfaceRenderer.sharedMaterial.GetTexture("_NormalTexture");
				Texture foam = surfaceRenderer.sharedMaterial.GetTexture("_RippleFoamTexture");
				Texture distortion = renderer.sharedMaterial.GetTexture("_UnderwaterDistortionMap");
				Check(normal != null && normal.name == "WaterNormals_01" && normal.width >= 256 &&
					foam != null && foam.name == "Foam_01" && distortion == normal && surfaceRenderer.sharedMaterial.GetFloat("_NormalStrength") > 0,
					"Original material normal, ripple foam and underwater distortion textures are bound");
				JArray reflectionFrames = new JArray();
				foreach (float cameraY in new[] { -2f, -3f, -4f })
				{
					SceneCamera.Instance.MoveTo(new Vector2(0, cameraY));
					yield return WaitReal(.25f);
					Camera gameCamera = SceneCamera.Instance.Camera;
					float projectedWaterline = gameCamera.WorldToViewportPoint(surface.transform.position).y;
					float reflectionHorizon = Convert.ToSingle(surface.GetType().GetProperty("ReflectionHorizon").GetValue(surface));
					Check(Mathf.Abs(gameCamera.transform.position.y - cameraY) < .01f, "Camera sweep reaches its requested world height");
					Check(Mathf.Abs(reflectionHorizon - projectedWaterline) < .002f,
						"Reflection mirror axis follows the projected waterline at camera height " + cameraY.ToString("0"));
                    RenderTexture reflectionTexture = surfaceRenderer.sharedMaterial.GetTexture("_LocalReflectionMap") as RenderTexture;
                    JObject contentProbe = LevelEditorWaterCaptureQA.MeasureReflectionContent(scene, reflectionTexture, reflectionHorizon);
                    float reflectionError = (float)contentProbe["errorTexels"];
                    Check(Mathf.Abs(reflectionError) < 1.5f,
                        "Captured object pixels mirror around the visible waterline at camera height " + cameraY.ToString("0") +
                        " (error=" + reflectionError.ToString("0.00") + " texels, sourceV=" + ((float)contentProbe["sourceV"]).ToString("0.000") +
                        ", reflectionV=" + ((float)contentProbe["reflectionV"]).ToString("0.000") +
                        ", expectedV=" + ((float)contentProbe["expectedV"]).ToString("0.000") + ")");
					reflectionFrames.Add(new JObject
					{
						["cameraY"] = gameCamera.transform.position.y,
						["waterlineViewportY"] = projectedWaterline,
                        ["reflectionHorizon"] = reflectionHorizon,
                        ["contentProbe"] = contentProbe
					});
					string cameraLabel = cameraY.ToString("0").Replace("-", "minus");
					LevelEditorWaterCaptureQA.CaptureGame("runtime-water-camera-y-" + cameraLabel);
					yield return WaitReal(.25f);
				}
				m_report["reflectionCameraSweep"] = reflectionFrames;
				SceneCamera.Instance.MoveTo(new Vector2(0, -2));
				yield return WaitReal(.15f);
				ModFluidHandle handle = FluidHandle(surface);
				ModFluidVolume volume = FluidVolume(surface);
				surface.GetType().GetMethod("AddContact").Invoke(surface, new object[] { new ModFluidContact
				{
					Position = new float2(0, 0), Radius = 1, Speed = 4, Entering = true
				} });
				yield return WaitReal(.15f);
				Texture2D rippleParameters = (Texture2D)surfaceRenderer.sharedMaterial.GetTexture("_RippleParamsTex");
				Check(rippleParameters != null && Mathf.Abs(rippleParameters.GetPixel(0, 0).r - 1f) < .001f,
					"World-space contact speed converts to BPLE units before setting the asset splash strength");
				LevelEditorWaterCaptureQA.CaptureGame("runtime-water-ripple-contact");
				Check(Convert.ToInt32(surface.GetType().GetProperty("RippleCount").GetValue(surface)) > 0 &&
					Convert.ToSingle(surface.GetType().GetProperty("MaximumRipple").GetValue(surface)) > .001f, "Contact produces propagating mesh ripples");
				float height = Convert.ToSingle(surface.GetType().GetProperty("MaximumRipple").GetValue(surface));
				Time.timeScale = 0;
				yield return WaitReal(.1f);
				Check(Mathf.Abs(height - Convert.ToSingle(surface.GetType().GetProperty("MaximumRipple").GetValue(surface))) < .0001f, "Pause freezes ripple integration");
				Time.timeScale = 1;

				manager.StartSimulation();
				yield return WaitReal(.05f);
				Entity part = FirstSimPart();
				EntityManager em = World.DefaultGameObjectInjectionWorld.EntityManager;
				float floatStartX = em.GetComponentData<LocalTransform>(part).Position.x;
				PhysicsMass mass = em.GetComponentData<PhysicsMass>(part);
				PhysicsCollider physicsCollider = em.GetComponentData<PhysicsCollider>(part);
				// Collider is a polymorphic Blob header; copying it loses the shape payload.
				Aabb local = physicsCollider.Value.Value.CalculateAabb();
				float colliderVolume = physicsCollider.Value.Value.MassProperties.Volume;
				float3 localSize = math.max(local.Max - local.Min, new float3(.001f));
				float colliderFill = Mathf.Clamp01(colliderVolume / Mathf.Max(.001f, localSize.x * localSize.y * localSize.z));
				float effectiveArea = localSize.x * localSize.y * colliderFill;
				float floatDensity = 1f / mass.InverseMass / Mathf.Max(.001f, effectiveArea) * 1.7f;
				volume.Density = floatDensity; volume.Flow = float2.zero; volume.WaveAmplitude = 0;
				handle.Update(volume);
				yield return WaitReal(4f);
				LocalTransform floatingTransform = em.GetComponentData<LocalTransform>(part);
				PhysicsVelocity floatingVelocity = em.GetComponentData<PhysicsVelocity>(part);
				Aabb contactBounds = physicsCollider.Value.Value.CalculateAabb(new RigidTransform(floatingTransform.Rotation, floatingTransform.Position), floatingTransform.Scale);
				m_report["ordinaryFloat"] = new JObject
				{
					["density"] = floatDensity, ["effectiveArea"] = effectiveArea, ["colliderVolume"] = colliderVolume,
					["y"] = floatingTransform.Position.y, ["vy"] = floatingVelocity.Linear.y,
					["captureContacts"] = volume.CaptureContacts, ["deliveredContacts"] = handle.DeliveredContacts,
					["fluidSteps"] = handle.SimulationSteps, ["queriedBodies"] = ModFluidSession.QueriedBodyCount,
					["executedChunks"] = ModFluidSession.ExecutedChunksLastStep,
					["partPrefab"] = em.HasComponent<Prefab>(part), ["partDisabled"] = em.HasComponent<Disabled>(part),
					["receivedContacts"] = Convert.ToInt32(surface.GetType().GetProperty("ReceivedContacts").GetValue(surface)),
					["inactiveContacts"] = Convert.ToInt32(surface.GetType().GetProperty("InactiveContacts").GetValue(surface)),
					["cooldownContacts"] = Convert.ToInt32(surface.GetType().GetProperty("CooldownContacts").GetValue(surface)),
					["ripples"] = Convert.ToInt32(surface.GetType().GetProperty("RippleCount").GetValue(surface)),
					["active"] = surface.isActiveAndEnabled, ["time"] = Time.time, ["fixedTime"] = Time.fixedTime
					, ["callbackAttached"] = handle.GetType().GetField("Contact", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(handle) != null,
					["scale"] = floatingTransform.Scale, ["aabbMinY"] = contactBounds.Min.y, ["aabbMaxY"] = contactBounds.Max.y,
					["waterSurface"] = volume.Surface, ["waterFlowY"] = volume.Flow.y
				};
				Check(floatingTransform.Position.y > -1.5f && floatingTransform.Position.y < 1.5f && math.abs(floatingVelocity.Linear.y) < 1.5f,
					"An actual ECS part settles at partial immersion instead of sinking or exploding");
				float horizontalDrift = floatingTransform.Position.x - floatStartX;
				m_report["zeroFlowFloat"] = new JObject { ["x"] = floatingTransform.Position.x, ["vx"] = floatingVelocity.Linear.x, ["deltaX"] = horizontalDrift };
				Check(Mathf.Abs(horizontalDrift) < .1f && Mathf.Abs(floatingVelocity.Linear.x) < .1f,
					"A floating part does not drift with zero horizontal flow");
				int receivedContacts = Convert.ToInt32(surface.GetType().GetProperty("ReceivedContacts").GetValue(surface));
				Check(Convert.ToInt32(surface.GetType().GetProperty("RippleCount").GetValue(surface)) > 0 &&
					receivedContacts < handle.SimulationSteps / 2,
					"Surface crossings emit bounded ripples instead of one event per submerged physics step");
				manager.EndSimulation();
				yield return null;

				volume.Density = floatDensity * .1f; handle.Update(volume);
				manager.StartSimulation();
				yield return WaitReal(2.5f);
				part = FirstSimPart();
				Check(em.GetComponentData<LocalTransform>(part).Position.y < -1.5f, "Density contrast allows a heavy body to sink");
				manager.EndSimulation();
				yield return null;

				settings.GravityY = 0;
				volume.Density = 0; volume.Flow = new float2(2, 0); volume.PhysicsEnabled = 1;
				handle.Update(volume);
				manager.StartSimulation();
				part = FirstSimPart();
				em.CompleteAllTrackedJobs();
				LocalTransform submerged = em.GetComponentData<LocalTransform>(part); submerged.Position = new float3(0, -4, 0);
				em.SetComponentData(part, submerged);
				em.SetComponentData(part, new PhysicsVelocity { Linear = new float3(10, 0, 0), Angular = new float3(0, 0, 5) });
				yield return WaitReal(.5f);
				PhysicsVelocity dragged = em.GetComponentData<PhysicsVelocity>(part);
				Check(dragged.Linear.x > 2 && dragged.Linear.x < 8 && math.abs(dragged.Angular.z) < 4.5f, "Actual linear/rotational drag approaches current velocity stably");
				volume.Flow = float2.zero; volume.Density = 0; volume.LinearDrag = 1.8f; handle.Update(volume);
				Time.timeScale = 0;
				yield return WaitReal(.05f);
				em.CompleteAllTrackedJobs();
				PhysicsVelocity paused = em.GetComponentData<PhysicsVelocity>(part);
				int pausedSteps = handle.SimulationSteps;
				yield return WaitReal(.1f);
				PhysicsVelocity afterPause = em.GetComponentData<PhysicsVelocity>(part);
				m_report["pauseProbe"] = new JObject { ["before"] = paused.Linear.ToString(), ["after"] = afterPause.Linear.ToString(), ["stepsBefore"] = pausedSteps, ["stepsAfter"] = handle.SimulationSteps };
				Check(math.distance(paused.Linear, afterPause.Linear) < .0001f && math.distance(paused.Angular, afterPause.Angular) < .0001f,
					"Paused simulation adds no repeated water impulse");
				Time.timeScale = 2;
				yield return WaitReal(.2f);
				PhysicsVelocity faster = em.GetComponentData<PhysicsVelocity>(part);
				Check(math.all(math.isfinite(faster.Linear)) && math.all(math.isfinite(faster.Angular)) && faster.Linear.x >= 0 && faster.Linear.x <= 10,
					"Time multiplier keeps fluid impulses finite");
				Time.timeScale = 1;
				em.CompleteAllTrackedJobs();
				LocalTransform partial = em.GetComponentData<LocalTransform>(part);
				partial.Position = new float3(0, .3f, 0);
				em.SetComponentData(part, partial);
				em.SetComponentData(part, new PhysicsVelocity { Linear = new float3(10, 0, 0) });
				PhysicsMass partialMass = em.GetComponentData<PhysicsMass>(part);
				Aabb partialBounds = physicsCollider.Value.Value.CalculateAabb(new RigidTransform(partial.Rotation, partial.Position), partial.Scale);
				float partialCenterY = partial.Position.y + math.rotate(partial.Rotation, partialMass.Transform.pos).y;
				Check(partialBounds.Min.y < 0 && partialBounds.Max.y > 0 && partialCenterY > 0,
					"Runtime drag probe is partially submerged while its center of mass stays above the waterline");
				yield return WaitReal(.2f);
				PhysicsVelocity partialVelocity = em.GetComponentData<PhysicsVelocity>(part);
				m_report["partialImmersionDrag"] = new JObject { ["vx"] = partialVelocity.Linear.x, ["centerY"] = partialCenterY };
				Check(partialVelocity.Linear.x > 0 && partialVelocity.Linear.x < 9.8f,
					"Partially submerged runtime hull loses horizontal drift before its center crosses the surface");
				volume.PhysicsEnabled = 0; handle.Update(volume);
				em.CompleteAllTrackedJobs();
				submerged = em.GetComponentData<LocalTransform>(part); submerged.Position = new float3(0, -4, 0);
				em.SetComponentData(part, submerged); em.SetComponentData(part, new PhysicsVelocity { Linear = new float3(10, 0, 0) });
				yield return WaitReal(.4f);
				Check(em.GetComponentData<PhysicsVelocity>(part).Linear.x > 8, "Physics-disabled water applies no drag/current to ECS bodies");
				manager.EndSimulation();
				yield return null;

				settings.MegaStructure = true;
				test["starter"] = "5,0,-1,-3,0,0\n5,0,0,-3,0,0\n5,0,1,-3,0,0\n";
				Call("POST", "/api/play", test);
				yield return null;
				surface = WaterComponent("WaterPreview"); handle = FluidHandle(surface); volume = FluidVolume(surface);
				volume.Density = 0; volume.Flow = new float2(2, 0); volume.FlowAcceleration = 0; volume.PhysicsEnabled = 1; handle.Update(volume);
				manager.StartSimulation();
				float started = Time.time;
				yield return WaitReal(.5f);
				Check(MegaStructure.Active && em.Exists(MegaStructure.Body), "Mega vehicle is active for fluid test");
				PhysicsVelocity vehicleVelocity = em.GetComponentData<PhysicsVelocity>(MegaStructure.Body);
				Entity megaPart = FirstSimPart();
				PhysicsMass megaPartMass = em.GetComponentData<PhysicsMass>(megaPart);
				float expected = 2 * (1 - Mathf.Exp(-volume.LinearDrag * (Time.time - started) * megaPartMass.InverseMass));
				PhysicsVelocity megaPartVelocity = em.GetComponentData<PhysicsVelocity>(megaPart);
				var driveSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<MegaStructureDriveSystem>();
				var mirrorSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<MegaStructureMirrorSystem>();
				m_report["megaCurrent"] = new JObject
				{
					["vx"] = vehicleVelocity.Linear.x, ["expectedApproximate"] = expected,
					["fluidSteps"] = handle.SimulationSteps, ["bodies"] = ModFluidSession.QueriedBodyCount,
					["childVx"] = megaPartVelocity.Linear.x,
					["childY"] = em.GetComponentData<LocalTransform>(megaPart).Position.y,
					["childMass"] = 1f / megaPartMass.InverseMass,
					["childInverseMass"] = megaPartMass.InverseMass,
					["driveTicks"] = driveSystem == null ? -1 : driveSystem.DiagnosticTicks,
					["driveStage"] = driveSystem == null ? -1 : driveSystem.DiagnosticStage,
					["driveBodies"] = driveSystem == null ? -1 : driveSystem.DiagnosticBodies,
					["childOffsetMass"] = em.GetComponentData<MegaPartOffset>(megaPart).Mass,
					["driveImpulseX"] = driveSystem == null ? 0 : driveSystem.DiagnosticImpulse.x,
					["mirrorTicks"] = mirrorSystem == null ? -1 : mirrorSystem.DiagnosticTicks
				};
				Check(vehicleVelocity.Linear.x > expected * .8f && vehicleVelocity.Linear.x < expected * 1.2f &&
					Mathf.Abs(vehicleVelocity.Linear.x - megaPartVelocity.Linear.x) < .12f,
					"Mega transfers each part's fluid impulse once, not once per mirror or part count");
				part = FirstSimPart();
				MegaStructure.RequestDetach(part);
				yield return WaitReal(.2f);
				Check(!em.HasComponent<MegaPartOffset>(part) && em.HasComponent<PhysicsWorldIndex>(part), "Detached part returns to independent physics");
				em.CompleteAllTrackedJobs();
				em.SetComponentData(part, new PhysicsVelocity());
				yield return WaitReal(.2f);
				Check(em.GetComponentData<PhysicsVelocity>(part).Linear.x > .1f, "Detached parts continue receiving fluid force");
				manager.EndSimulation();
				yield return null;

				pool["flow_x"] = 0; pool["flow_y"] = 0; pool["flow_acceleration"] = 0; pool["density"] = 0;
				test["starter"] = "5,0,-1,-3,0,0\n5,0,0,-3,0,0\n5,0,1,-3,0,0\n";
				Call("POST", "/api/play", test);
				yield return null;
				surface = WaterComponent("WaterPreview"); handle = FluidHandle(surface); volume = FluidVolume(surface);
                volume.Density = 1f; volume.Flow = float2.zero; volume.FlowAcceleration = 0; volume.WaveAmplitude = 0; volume.PhysicsEnabled = 1;
				handle.Update(volume);
				manager.StartSimulation();
				yield return WaitReal(.2f);
				Entity neutralMega = MegaStructure.Body;
				Check(MegaStructure.Active && em.Exists(neutralMega), "Neutral mega vehicle is active for zero-current drift test");
				float megaNeutralStartX = em.GetComponentData<LocalTransform>(neutralMega).Position.x;
				yield return WaitReal(.75f);
				LocalTransform neutralTransform = em.GetComponentData<LocalTransform>(neutralMega);
				PhysicsVelocity neutralVelocity = em.GetComponentData<PhysicsVelocity>(neutralMega);
				m_report["zeroFlowMega"] = new JObject { ["x"] = neutralTransform.Position.x, ["vx"] = neutralVelocity.Linear.x,
                    ["deltaX"] = neutralTransform.Position.x - megaNeutralStartX, ["density"] = volume.Density,
                    ["buoyancyMultiplier"] = volume.BuoyancyMultiplier };
				Check(Mathf.Abs(neutralTransform.Position.x - megaNeutralStartX) < .15f && Mathf.Abs(neutralVelocity.Linear.x) < .1f,
                    "A buoyant multi-part vehicle does not drift with zero horizontal flow");
				manager.EndSimulation();
				yield return null;

				Call("POST", "/api/play", preview);
				yield return WaitReal(.3f);
				Check(ModFluidSession.ActiveVolumeCount == 1, "Replacing a simulated water level cleans old handles/captures");
				MonoBehaviour parallax = WaterComponent("LevelParallax");
				Check(parallax != null && Convert.ToInt32(parallax.GetType().GetProperty("LayerCount").GetValue(parallax)) > 0,
					"Imported original background layers are registered for parallax");
				Transform nearLayer = GameObject.Find("BGLayerNear")?.transform;
				if (nearLayer != null)
				{
					SceneCamera.Instance.MoveTo(new Vector2(0, 0)); yield return null;
					float before = nearLayer.position.x;
					SceneCamera.Instance.MoveTo(new Vector2(2, 0)); yield return null;
					Check(Mathf.Abs(nearLayer.position.x - before - .8f) < .02f, "Original near-layer 0.4 camera follow ratio");
				}
				m_package.EntryPoint.GetType().GetMethod("OpenEditor").Invoke(m_package.EntryPoint, null);
				yield return WaitReal(2f);
				GameObject reopenedWindow = GameObject.Find("BpsimLevelEditorModWindow");
				MonoBehaviour[] reopenedWebViews = Resources.FindObjectsOfTypeAll<MonoBehaviour>()
					.Where(item => item != null && item.GetType().Name == "CanvasWebViewPrefab" && item.gameObject.activeInHierarchy).ToArray();
				int visibleReopenedWebViews = reopenedWebViews.Count(item =>
				{
					PropertyInfo visible = item.GetType().GetProperty("Visible");
					return visible != null && (bool)visible.GetValue(item);
				});
				m_report["editorWebViewReopen"] = new JObject { ["windows"] = reopenedWindow == null ? 0 : 1,
					["webViews"] = reopenedWebViews.Length, ["visibleWebViews"] = visibleReopenedWebViews };
				Check(reopenedWindow != null && reopenedWindow.activeInHierarchy && reopenedWebViews.Length == 1 && visibleReopenedWebViews == 1,
					"Opening the editor recreates one visible WebView after gameplay closed it");
				Call("POST", "/api/close", null);
				yield return WaitReal(.6f);
				int remainingWebViews = Resources.FindObjectsOfTypeAll<MonoBehaviour>()
					.Count(item => item != null && item.GetType().Name == "CanvasWebViewPrefab" && item.gameObject.activeInHierarchy);
				Check(GameObject.Find("BpsimLevelEditorModWindow") == null && remainingWebViews == 0,
					"Closing the editor destroys its WebView instance");
				m_report["runtimeWater"] = true;
			}
			finally
			{
				if (manager != null && manager.IsSimulating) manager.EndSimulation();
				settings.GravityX = gravityX; settings.GravityY = gravityY; settings.FixedTimeStep = step;
				settings.MegaStructure = mega; settings.MegaStructureBreakSpeed = breakSpeed;
				Time.timeScale = timeScale;
				World injection = World.DefaultGameObjectInjectionWorld;
				if (injection != null && injection.IsCreated)
					injection.GetOrCreateSystemManaged<FixedStepSimulationSystemGroup>().Timestep = step;
			}
		}

		private static IEnumerator WaitReal(float seconds)
		{
			float deadline = Time.realtimeSinceStartup + seconds;
			while (Time.realtimeSinceStartup < deadline) yield return null;
		}

		private static Entity FirstSimPart()
		{
			PartSimulator simulator = PartManager.Instance.PartSimulator;
			if (simulator == null || simulator.Unmanaged.Parts.Length == 0) throw new InvalidOperationException("No simulation parts for fluid QA.");
			return simulator.Unmanaged.Parts[0];
		}

		private static MonoBehaviour WaterComponent(string type)
		{
			GameObject root = GameObject.Find("LevelEditorWater");
			return root == null ? null : root.GetComponentsInChildren<MonoBehaviour>().FirstOrDefault(component => component.GetType().Name == type);
		}

		private static ModFluidHandle FluidHandle(MonoBehaviour water)
		{
			return (ModFluidHandle)water.GetType().GetField("m_fluid", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(water);
		}

		private static ModFluidVolume FluidVolume(MonoBehaviour water)
		{
			return (ModFluidVolume)water.GetType().GetField("m_volume", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(water);
		}

		private static int ButtonCount()
		{
			GameObject menu = GameObject.Find("BpsimLevelMenu");
			return menu == null ? 0 : menu.GetComponentsInChildren<Button>(true)
				.Count(button => button.name.StartsWith("ModLevelAction_", StringComparison.Ordinal) && button.gameObject.activeSelf);
		}

		private static int HttpStatus(string url, string origin = null)
		{
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
			request.Timeout = 5000;
			request.Proxy = null;
			if (origin != null) request.Headers["Origin"] = origin;
			try { using (HttpWebResponse response = (HttpWebResponse)request.GetResponse()) return (int)response.StatusCode; }
			catch (WebException ex)
			{
				if (ex.Response is HttpWebResponse response) { int status = (int)response.StatusCode; response.Dispose(); return status; }
				throw;
			}
		}

		private JObject CallRaw(string method, string path, JObject body)
		{
			ModWebRequest request = new ModWebRequest { Method = method, Path = path, Body = Encoding.UTF8.GetBytes(body?.ToString(Formatting.None) ?? string.Empty) };
			ModWebResponse response = (ModWebResponse)m_package.EntryPoint.GetType().GetMethod("Handle").Invoke(m_package.EntryPoint, new object[] { request });
			return JsonConvert.DeserializeObject<JObject>(Encoding.UTF8.GetString(response.Body),
				new JsonSerializerSettings { DateParseHandling = DateParseHandling.None, TypeNameHandling = TypeNameHandling.None });
		}

		private JObject Call(string method, string path, JObject body)
		{
			JObject result = CallRaw(method, path, body);
			if ((bool?)result["ok"] == false) throw new InvalidOperationException(path + ": " + (string)result["error"]);
			return result;
		}

		private void Check(bool condition, string name)
		{
			if (!condition) throw new InvalidOperationException("QA failed: " + name);
			m_checks++;
		}

		private void Cleanup()
		{
			if (m_store != null && m_testFile != null) m_store.Delete(m_testFile);
			if (m_package != null && m_manager != null)
			{
				m_manager.UnloadPackage(m_package);
				m_manager.Packages.Remove(m_package);
				if (m_previousPackage != null) m_manager.Packages.Add(m_previousPackage);
				ModRegistry.Rebuild(m_manager.Packages);
			}
		}
	}
}
#endif
