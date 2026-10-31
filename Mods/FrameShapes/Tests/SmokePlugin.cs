using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using Bpsim.Mods;
using Bpsim.Parts;
using Bpsim.Rendering;
using Newtonsoft.Json;
using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Bpsim.FrameShapesTests
{
	[BepInPlugin("bput.bpsim.frame-shapes.qa", "Frame Shapes acceptance probe", "1.0.0")]
	public sealed class SmokePlugin : BaseUnityPlugin
	{
		private readonly StringBuilder m_log = new();
		private void Awake()
		{
			if (Array.IndexOf(Environment.GetCommandLineArgs(), "--frame-shapes-smoke-test") >= 0)
			{
				Application.runInBackground = true;
				Logger.LogInfo("Acceptance probe Awake; background updates enabled.");
				if (SceneManager.GetActiveScene().isLoaded) StartCoroutine(Accept());
				else SceneManager.sceneLoaded += OnSceneLoaded;
			}
		}
		private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			SceneManager.sceneLoaded -= OnSceneLoaded;
			Logger.LogInfo("Acceptance scene loaded; starting checks.");
			StartCoroutine(Accept());
		}
		private void Check(string name, bool value)
		{
			m_log.AppendLine((value ? "PASS " : "FAIL ") + name);
			if (!value) throw new Exception(name);
		}
		private IEnumerator Accept()
		{
			if (Array.IndexOf(Environment.GetCommandLineArgs(), "--frame-shapes-smoke-test") < 0) yield break;
			float until = Time.realtimeSinceStartup + 60;
			while ((PartManager.Instance == null || !PartManager.Instance.IsReady || ModManager.Instance?.Find("bput.bpsim.bpreboot-frame-shapes") == null) && Time.realtimeSinceStartup < until)
				yield return null;
			try { Run(); }
			catch (Exception ex) { m_log.AppendLine("FAILURES: 1\n" + ex); }
			string output = Path.Combine(Paths.GameRootPath, "output", "frame-shapes-smoke.txt");
			Directory.CreateDirectory(Path.GetDirectoryName(output));
			File.WriteAllText(output, m_log.ToString());
			Logger.LogInfo(m_log.ToString());
			if (m_log.ToString().Contains("FAILURES: 0") && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
			{
				Bpsim.SceneCamera.Instance.MoveTo(new Vector2(4.5f, 4.5f));
				Bpsim.SceneCamera.Instance.ScaleTo(7f);
				for (int i = 0; i < 12; i++) yield return null;
				Capture("frame-shapes.png");
				yield return new WaitForSecondsRealtime(.5f);
				ModManagerUI.Open();
				for (int i = 0; i < 4; i++) yield return null;
				Capture("frame-shapes-mod-list.png");
				yield return new WaitForSecondsRealtime(.5f);
				ModManagerUI.Close();
				var uiType = typeof(PartManager).Assembly.GetType("Bpsim.UI.UserInterface");
				var ui = uiType.GetProperty("Instance").GetValue(null);
				var partOperator = uiType.GetProperty("PartOperator").GetValue(ui);
				var scene = PartManager.Instance.ActiveScene;
				partOperator.GetType().GetMethod("OpenPartPropertyPanel").Invoke(partOperator, new object[] { scene, scene.Unmanaged.Parts[3] });
				for (int i = 0; i < 4; i++) yield return null;
				Button shapeButton = null;
				foreach (var button in UnityEngine.Object.FindObjectsOfType<Button>())
					if (button.name == "FrameShape") shapeButton = button;
				try
				{
					Check("property shape button is visible and enabled", shapeButton != null && shapeButton.isActiveAndEnabled && shapeButton.interactable);
					var em = scene.Unmanaged.EntityManager;
					int before = em.GetComponentData<FrameShape>(scene.Unmanaged.Parts[3]).Value;
					shapeButton.onClick.Invoke();
					Check("property button changes selected part shape", em.GetComponentData<FrameShape>(scene.Unmanaged.Parts[3]).Value == (before + 1) % FrameShape.Count);
					for (int i = 0; i < 3; i++) shapeButton.onClick.Invoke();
				}
				catch (Exception ex) { m_log.AppendLine("FAILURES: 1\n" + ex); }
				File.WriteAllText(output, m_log.ToString());
				var scroll = shapeButton?.GetComponentInParent<ScrollRect>();
				if (scroll != null) scroll.verticalNormalizedPosition = 0;
				for (int i = 0; i < 4; i++) yield return null;
				Capture("frame-shapes-properties.png");
				yield return new WaitForSecondsRealtime(.5f);
				if (shapeButton != null)
				{
					var panelType = typeof(PartManager).Assembly.GetType("Bpsim.UI.PartPropertyPanel");
					var panel = shapeButton.GetComponentInParent(panelType);
					panelType.GetMethod("UpdateLayout", new[] { typeof(bool) }).Invoke(panel, new object[] { false });
					for (int i = 0; i < 4; i++) yield return null;
					Capture("frame-shapes-properties-floating.png");
					yield return new WaitForSecondsRealtime(.5f);
				}
			}
			if (Array.IndexOf(Environment.GetCommandLineArgs(), "--frame-shapes-keep-open") < 0) Application.Quit();
		}
		private static void Capture(string name)
		{
			var camera = Bpsim.SceneCamera.Instance.Camera;
			var previousTarget = camera.targetTexture;
			var previousActive = RenderTexture.active;
			int previousMask = camera.cullingMask;
			var overlays = new List<(Canvas canvas, Camera camera, float distance)>();
			var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
			var pixels = new Texture2D(1280, 720, TextureFormat.RGBA32, false);
			try
			{
				target.Create();
				camera.targetTexture = target;
				camera.cullingMask |= 1 << 5;
				foreach (var canvas in UnityEngine.Object.FindObjectsOfType<Canvas>())
				{
					if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
					overlays.Add((canvas, canvas.worldCamera, canvas.planeDistance));
					canvas.renderMode = RenderMode.ScreenSpaceCamera;
					canvas.worldCamera = camera;
					canvas.planeDistance = camera.nearClipPlane + 1;
				}
				Canvas.ForceUpdateCanvases();
				camera.Render();
				RenderTexture.active = target;
				pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
				pixels.Apply();
				File.WriteAllBytes(Path.Combine(Paths.GameRootPath, "output", name), pixels.EncodeToPNG());
			}
			finally
			{
				foreach (var overlay in overlays)
				{
					overlay.canvas.renderMode = RenderMode.ScreenSpaceOverlay;
					overlay.canvas.worldCamera = overlay.camera;
					overlay.canvas.planeDistance = overlay.distance;
				}
				camera.cullingMask = previousMask;
				camera.targetTexture = previousTarget;
				RenderTexture.active = previousActive;
				target.Release();
				UnityEngine.Object.Destroy(target);
				UnityEngine.Object.Destroy(pixels);
			}
		}
		private void Run()
		{
			var manager = PartManager.Instance;
			Check("player ECS world ready", manager != null && manager.IsReady);
			var mods = ModManager.Instance;
			var pkg = mods.Find("bput.bpsim.bpreboot-frame-shapes");
			Check("BepInEx plugin in Mod list, default enabled and loaded", pkg != null && pkg.Source == ModSource.BepInEx && pkg.Enabled && pkg.IsLoaded && FrameShapeSystem.FeatureEnabled);
			var ecs = World.DefaultGameObjectInjectionWorld.EntityManager;
			using (var query = ecs.CreateEntityQuery(ComponentType.ReadOnly<ModEntityComponent>()))
			using (var entries = query.ToComponentDataArray<ModEntityComponent>(Allocator.Temp))
			{
				bool mirrored = false;
				foreach (var entry in entries)
					if (entry.Id.ToString() == pkg.Id) mirrored = entry.Enabled != 0 && entry.State == (byte)ModState.Loaded;
				Check("BepInEx registration creates a loaded ECS registry entry", mirrored);
			}
			mods.Rescan();
			Check("rescan retains same plugin instance", ReferenceEquals(pkg, mods.Find(pkg.Id)) && pkg.IsLoaded);
			var data = new Schematics();
			for (int row = 0; row < 4; row++)
				for (int shape = 0; shape < 4; shape++)
					data.Units.Add(new Schematics.Unit(shape * 3, row * 3, (int)(row == 0 ? PartType.WoodenFrame : PartType.MetalFrame), row < 2 ? 0 : row == 2 ? 12 : 132, 0, false, 0, 0, 0, 1, 1, 1, shape));
			using var stream = new MemoryStream();
			Schematics.CreateLoader(SchematicsFormat.Json).Write(stream, data); stream.Position = 0;
			int id = (int)typeof(PartManager).GetField("m_sceneID", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
			manager.LoadScene(stream, "BPIR 变形框验收", SchematicsFormat.Json);
			manager.SelectScene(id);
			var scene = manager.ActiveScene;
			var em = scene.Unmanaged.EntityManager;
			var system = em.World.GetOrCreateSystemManaged<FrameShapeSystem>();
			system.ApplyPending();
			Check("16 frame instances loaded", scene.Unmanaged.Parts.Length == 16);
			foreach (var entity in scene.Unmanaged.Parts)
			{
				int shape = em.GetComponentData<FrameShape>(entity).Value;
				Check("shape collision " + shape, shape == 0 || em.GetComponentData<PhysicsCollider>(entity).Value.Value.Type == Unity.Physics.ColliderType.Convex);
				Check("shape BRG data " + shape, em.GetComponentData<BlendFactor>(entity).Value.z == shape);
			}
			using var saved = new MemoryStream();
			manager.SaveScene(saved, id, SchematicsFormat.Json); saved.Position = 0;
			var read = Schematics.CreateLoader(SchematicsFormat.Json).Read(saved);
			Check("player save keeps per-part shapes", read.Units.Count == 16 && read.Units[3].FrameShape == 3 && read.Units[6].FrameShape == 2);
			using var nativeSaved = new NativeArray<Schematics.Unit>(16, Allocator.TempJob);
			Unity.Jobs.IJobExtensions.Run(PartSceneJobs.SaveParts(scene.Unmanaged.Parts, nativeSaved));
			Check("copy/paste snapshot keeps shapes", nativeSaved[3].FrameShape == 3 && nativeSaved[6].FrameShape == 2);
			VerifyManagedToggle(mods, pkg);
			mods.UnloadPackage(pkg);
			Check("Mod unload disables ECS feature", !FrameShapeSystem.FeatureEnabled && !pkg.IsLoaded);
			foreach (var entity in scene.Unmanaged.Parts)
				Check("disabled frame restores original collider", em.GetComponentData<PhysicsCollider>(entity).Value == em.GetComponentData<FrameShapeOriginalCollider>(entity).Value && em.GetComponentData<BlendFactor>(entity).Value.z == 0);
			mods.LoadPackage(pkg);
			Check("Mod reload restores ECS feature", FrameShapeSystem.FeatureEnabled && pkg.IsLoaded);
			var target = scene.Unmanaged.Parts[3];
			FrameShapeSystem.SetShape(em, target, 1);
			Check("live shape edit", em.GetComponentData<FrameShape>(target).Value == 1 && em.GetComponentData<BlendFactor>(target).Value.z == 1);
			FrameShapeSystem.SetShape(em, target, 3);
			manager.StartSimulation();
			Check("simulation starts with shaped hulls", manager.IsSimulating);
			mods.UnloadPackage(pkg);
			Check("disable safely ends simulation", !manager.IsSimulating && !FrameShapeSystem.FeatureEnabled);
			mods.LoadPackage(pkg);
			m_log.AppendLine("FAILURES: 0");
		}
		private void VerifyManagedToggle(ModManager mods, ModPackage pkg)
		{
			string path = ModPaths.SettingsPath;
			byte[] previous = File.Exists(path) ? File.ReadAllBytes(path) : null;
			try
			{
				mods.SetEnabled(pkg.Id, false);
				mods.Rescan();
				var disabled = JsonConvert.DeserializeObject<ModSettings>(File.ReadAllText(path));
				Check("list toggle persists disabled state across rescan", !pkg.Enabled && !pkg.IsLoaded && !FrameShapeSystem.FeatureEnabled && disabled.disabled.Contains(pkg.Id));
				mods.SetEnabled(pkg.Id, true);
				mods.Rescan();
				var enabled = JsonConvert.DeserializeObject<ModSettings>(File.ReadAllText(path));
				Check("list toggle persists enabled state with same plugin", pkg.Enabled && pkg.IsLoaded && FrameShapeSystem.FeatureEnabled && enabled.IsEnabled(pkg.Id) && ReferenceEquals(pkg, mods.Find(pkg.Id)));
			}
			finally
			{
				try { if (!pkg.Enabled || !pkg.IsLoaded) mods.SetEnabled(pkg.Id, true); }
				finally
				{
					if (previous != null) File.WriteAllBytes(path, previous);
					else if (File.Exists(path)) File.Delete(path);
				}
			}
		}
	}
}
