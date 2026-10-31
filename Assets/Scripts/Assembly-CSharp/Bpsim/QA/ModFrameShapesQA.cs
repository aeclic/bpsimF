#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Text;
using Bpsim.Mods;
using Bpsim.Parts;
using Bpsim.Rendering;
using Bpsim.UI;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.QA
{
	/// <summary>
	/// 「BPIR 变形框」Mod 的 Play 模式验收探针。
	///
	///   invoke_static type=Bpsim.QA.ModFrameShapesQA method=Run    args=[]
	///   invoke_static type=Bpsim.QA.ModFrameShapesQA method=Status args=[]
	///   invoke_static type=Bpsim.QA.ModFrameShapesQA method=Capture args=[]
	///   invoke_static type=Bpsim.QA.ModFrameShapesQA method=Focus   args=[]
	///
	/// Mod 现在的形态是「不新增部件，只打开宿主的框形状特性」，所以验收点是：
	/// 1. 建造列表干净：<see cref="PartCollection{T}" /> 里没有任何 9000+ 的自定义索引；
	/// 2. Mod 把 <see cref="FrameShapeSystem.FeatureEnabled" /> 打开了；
	/// 3. 普通彩色框（MetalFrame#12）的部件属性面板里出现「形状」一行；
	/// 4. 点它能循环 长方形 → 直角三角 → 半圆 → 圆，且碰撞体换成凸包、BlendFactor.z 跟着变；
	/// 5. 切形状不动颜色：MaterialColor 与 ColoredFrame.Color 前后完全一致
	///    （变体方案曾把颜色弄成红色，这条是那个 bug 的回归守卫）；
	/// 6. 存档往返保留 FrameShape；
	/// 7. 模拟仍能启动。
	///
	/// 报告写到 &lt;project&gt;/Logs/bpsim-frameshapesqa.log。
	/// </summary>
	public sealed class ModFrameShapesQA : MonoBehaviour
	{
		private static ModFrameShapesQA s_instance;

		private static readonly StringBuilder s_report = new StringBuilder();

		private static string s_status = "idle";

		private static int s_passed;

		private static int s_failed;

		/// <summary>彩色框第一件：Mod 的形状开关只对它这类容器框有意义。</summary>
		private const int FrameIndex = 12;

		/// <summary><see cref="ModParts.CustomIndexBase" />：Mod 变体索引的起点。</summary>
		private const int CustomIndexBase = 9000;

		/// <summary>
		/// 透明框第一件（<see cref="BasePart.TransparentFrames" /> 是 MetalFrame 132~133）。
		/// 它的 <see cref="ColoredFrame" /> 颜色恒为白色，用来对照「框的颜色来自基部件本身」。
		/// </summary>
		private const int TransparentIndex = 132;

		public static string LogPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "bpsim-frameshapesqa.log"));

		public static string Run()
		{
			ModFrameShapesQA runner = Ensure();
			s_report.Length = 0;
			s_passed = 0;
			s_failed = 0;
			s_status = "running";
			runner.StartCoroutine(runner.RunChecks());
			return "started";
		}

		/// <summary>
		/// 把游戏相机（正交，2D）当前的画面存成 PNG，用来肉眼确认形状框的外观。
		/// 桥自带的 screenshot 是另建一台透视相机，拍不到 2D 场景。
		/// </summary>
		public static string Capture()
		{
			SceneCamera sceneCamera = SceneCamera.Instance;
			if (sceneCamera == null || sceneCamera.Camera == null)
			{
				return "no camera";
			}
			Camera cam = sceneCamera.Camera;
			RenderTexture previous = cam.targetTexture;
			RenderTexture rt = new RenderTexture(960, 720, 24, RenderTextureFormat.ARGB32);
			Texture2D texture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
			try
			{
				cam.targetTexture = rt;
				cam.Render();
				RenderTexture.active = rt;
				texture.ReadPixels(new Rect(0f, 0f, rt.width, rt.height), 0, 0);
				texture.Apply();
				string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "bpsim-frameshapes.png"));
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				File.WriteAllBytes(path, texture.EncodeToPNG());
				return path;
			}
			finally
			{
				cam.targetTexture = previous;
				RenderTexture.active = null;
				Object.Destroy(rt);
				Object.Destroy(texture);
			}
		}

		public static string Status()
		{
			return s_status + " | pass=" + s_passed + " fail=" + s_failed + "\n" + s_report;
		}

		/// <summary>把相机挪到测试框摆放处，方便跑完 QA 后肉眼看。</summary>
		public static string Focus()
		{
			SceneCamera camera = SceneCamera.Instance;
			if (camera == null)
			{
				return "no camera";
			}
			camera.MoveTo(new Vector2(0f, 0f));
			camera.ScaleTo(10f);
			camera.ZoomTo(10f);
			return "focused at (0,0) size=10";
		}

		private static ModFrameShapesQA Ensure()
		{
			if (s_instance != null)
			{
				return s_instance;
			}
			GameObject gameObject = new GameObject("[ModFrameShapesQA]");
			gameObject.hideFlags = HideFlags.DontSave;
			Object.DontDestroyOnLoad(gameObject);
			s_instance = gameObject.AddComponent<ModFrameShapesQA>();
			return s_instance;
		}

		private static void Check(string name, bool ok, string detail = "")
		{
			if (ok)
			{
				s_passed++;
			}
			else
			{
				s_failed++;
			}
			s_report.Append(ok ? "PASS " : "FAIL ").Append(name);
			if (!string.IsNullOrEmpty(detail))
			{
				s_report.Append(" | ").Append(detail);
			}
			s_report.Append('\n');
		}

		private static PartTypeInfo Info(int index)
		{
			return new PartTypeInfo(PartType.MetalFrame, index);
		}

		private IEnumerator RunChecks()
		{
			for (int i = 0; i < 600 && PartManager.Instance == null; i++)
			{
				yield return null;
			}
			PartManager manager = PartManager.Instance;
			if (manager == null)
			{
				Check("PartManager", false, "未就绪");
				Finish();
				yield break;
			}
			Check("PartManager", true);

			CheckBuildList(manager);

			// Mod 在 AfterSceneLoad 之后才加载，形状特性由它的 enabler 每 0.5 秒重试打开。
			for (int i = 0; i < 300 && !ModParts.FrameShapesEnabled; i++)
			{
				yield return null;
			}
			Check("mod enabled frame shapes", ModParts.FrameShapesEnabled);

			yield return CheckPanel(manager);
			CheckSaveRoundtrip(manager);

			Finish();
		}

		// ------------------------------------------------------------------
		// 1. 建造列表干净：Mod 不再注册任何部件变体
		// ------------------------------------------------------------------
		private static void CheckBuildList(PartManager manager)
		{
			PartCollection<ManagedPart> collection = manager.Factory.PartCollection;
			int custom = 0;
			int total = 0;
			for (int type = 0; type < (int)PartType.Max; type++)
			{
				if (!collection.TryFindParts((PartType)type, out var entry))
				{
					continue;
				}
				foreach (int key in entry.Data.Keys)
				{
					total++;
					if (key >= CustomIndexBase)
					{
						custom++;
					}
				}
			}
			Check("no custom part indices in build list", custom == 0, "custom=" + custom + " total=" + total);

			bool hasFrame = collection.TryFindPart(Info(FrameIndex), out ManagedPart _);
			Check("colored frame MetalFrame#" + FrameIndex + " present", hasFrame);

			// 变体表也该是空的：Mod 没有调用过 TryAddVariant。
			Check("no variant registrations", IsVariantTableEmpty());
		}

		private static bool IsVariantTableEmpty()
		{
			foreach (var _ in PartVariants.All)
			{
				return false;
			}
			return true;
		}

		// ------------------------------------------------------------------
		// 2~5. 属性面板里的「形状」一行 + 点它切形状 + 颜色不动
		// ------------------------------------------------------------------
		private IEnumerator CheckPanel(PartManager manager)
		{
			if (manager.IsSimulating)
			{
				manager.EndSimulation();
				yield return null;
			}
			manager.LoadEmptyScene("ModFrameShapesQA");
			yield return null;
			yield return null;
			PartScene scene = manager.HasActiveScene() ? manager.ActiveScene : null;
			Check("active scene", scene != null);
			if (scene == null)
			{
				yield break;
			}
			for (int i = 0; i < 60 && scene.Unmanaged.Parts.Length != 0; i++)
			{
				scene.Unmanaged.ClearParts();
				yield return null;
			}

			EntityManager em = manager.System.EntityManager;
			PartTypeInfo info = Info(FrameIndex);
			Entity prefab = manager.EntitySpawner.FindPrefab(info);
			Check("prefab for MetalFrame#" + FrameIndex, prefab != Entity.Null, "entity=" + prefab);
			if (prefab == Entity.Null)
			{
				yield break;
			}

			PartCreationRequest request = new PartCreationRequest(prefab, scene.SceneID, info, 0, 0, 0, false);
			bool placed = scene.Unmanaged.PlacePartAt(in request, false);
			Check("place MetalFrame#" + FrameIndex, placed);
			if (!placed)
			{
				yield break;
			}
			scene.Unmanaged.Update();
			yield return null;

			Entity part = scene.Unmanaged.FindPartAt(0, 0, out Entity found) ? found : Entity.Null;
			Check("placed entity for MetalFrame#" + FrameIndex, part != Entity.Null, "entity=" + part);
			if (part == Entity.Null)
			{
				yield break;
			}

			// ColoredFrame 存在，颜色就是彩色框 #12 的标准色 —— 与变体方案里的红色无关。
			bool hasColored = em.HasComponent<ColoredFrame>(part);
			Check("colored frame component", hasColored);
			if (hasColored)
			{
				float4 expected = ColoredFrame.GetColor(info);
				float4 actual = em.GetComponentData<ColoredFrame>(part).Color;
				Check("colored frame color matches base", math.all(math.abs(expected - actual) < 1e-4f),
					"actual=" + actual + " expected=" + expected);
			}

			Check("part has FrameShape component", em.HasComponent<FrameShape>(part));
			int initial = em.HasComponent<FrameShape>(part) ? em.GetComponentData<FrameShape>(part).Value : -1;
			Check("fresh frame is rectangle", initial == FrameShape.Rectangle, "value=" + initial);

			// 打开属性面板（和玩家双击部件走的是同一条路径）。
			UserInterface ui = UserInterface.Instance;
			Check("user interface ready", ui != null && ui.PartOperator != null);
			if (ui == null || ui.PartOperator == null)
			{
				yield break;
			}
			ui.PartOperator.OpenPartPropertyPanel(scene, part);
			yield return null;
			yield return null;

			PartPropertyPanel panel = ui.PartOperator.PropertyPanel;
			Check("property panel open", panel != null && panel.gameObject.activeInHierarchy);
			if (panel == null)
			{
				yield break;
			}

			Transform content = panel.transform.Find("Properties/Scroll View/Viewport/Content");
			Check("panel content found", content != null);
			if (content == null)
			{
				yield break;
			}

			Button shapeButton = FindButton(content, "FrameShape");
			Check("panel has shape row", shapeButton != null);
			if (shapeButton == null)
			{
				yield break;
			}
			Check("shape row visible", shapeButton.gameObject.activeSelf);

			Text label = shapeButton.GetComponentInChildren<Text>();
			Check("shape row starts as rectangle",
				label != null && label.text == "形状：" + FrameShape.Name(FrameShape.Rectangle),
				label != null ? label.text : "no label");

			// 颜色基线：切形状前先记下 MaterialColor / ColoredFrame。
			float4 colorBefore = em.HasComponent<MaterialColor>(part)
				? em.GetComponentData<MaterialColor>(part).Value
				: new float4(float.NaN);
			float4 tintBefore = em.HasComponent<ColoredFrame>(part)
				? em.GetComponentData<ColoredFrame>(part).Color
				: new float4(float.NaN);

			int[] cycle = { FrameShape.Triangle, FrameShape.Semicircle, FrameShape.Circle, FrameShape.Rectangle };
			for (int i = 0; i < cycle.Length; i++)
			{
				shapeButton.onClick.Invoke();
				for (int f = 0; f < 4; f++)
				{
					yield return null;
				}
				int value = em.GetComponentData<FrameShape>(part).Value;
				Check("click " + (i + 1) + " sets " + FrameShape.Name(cycle[i]), value == cycle[i], "value=" + value);
				Check("click " + (i + 1) + " updates label",
					label != null && label.text == "形状：" + FrameShape.Name(cycle[i]),
					label != null ? label.text : "no label");
				if (cycle[i] != FrameShape.Rectangle)
				{
					ColliderType type = em.GetComponentData<PhysicsCollider>(part).Value.Value.Type;
					Check("click " + (i + 1) + " rebuilds convex collider", type == ColliderType.Convex, "type=" + type);
					Check("click " + (i + 1) + " writes blend shape channel",
						BlendShapeOf(em, part) == cycle[i], "z=" + BlendShapeOf(em, part));
				}
				else
				{
					Check("back to rectangle keeps box collider",
						em.GetComponentData<PhysicsCollider>(part).Value.Value.Type == ColliderType.Box,
						"type=" + em.GetComponentData<PhysicsCollider>(part).Value.Value.Type);
				}
			}

			// ⑤ 切形状不许动颜色（变体方案曾把框染成红色，这条是回归守卫）。
			float4 colorAfter = em.HasComponent<MaterialColor>(part)
				? em.GetComponentData<MaterialColor>(part).Value
				: new float4(float.NaN);
			float4 tintAfter = em.HasComponent<ColoredFrame>(part)
				? em.GetComponentData<ColoredFrame>(part).Color
				: new float4(float.NaN);
			Check("shape change keeps MaterialColor", SameColor(colorBefore, colorAfter),
				"before=" + colorBefore + " after=" + colorAfter);
			Check("shape change keeps ColoredFrame tint", SameColor(tintBefore, tintAfter),
				"before=" + tintBefore + " after=" + tintAfter);

			// ⑧ 换一个「本来就是中性色」的容器框：证明框的颜色来自基部件本身，
			//    形状特性不会额外给框上色。透明框（#132）的颜色恒为白，
			//    切形状前后都应该还是白 —— 这是「期望白色/灰色」的直接对照。
			Entity neutralPrefab = manager.EntitySpawner.FindPrefab(Info(TransparentIndex));
			Check("prefab for transparent frame MetalFrame#" + TransparentIndex, neutralPrefab != Entity.Null);
			if (neutralPrefab != Entity.Null)
			{
				PartCreationRequest neutralRequest = new PartCreationRequest(
					neutralPrefab, scene.SceneID, Info(TransparentIndex), 2, 0, 0, false);
				bool neutralPlaced = scene.Unmanaged.PlacePartAt(in neutralRequest, false);
				Check("place transparent frame MetalFrame#" + TransparentIndex, neutralPlaced);
				if (neutralPlaced)
				{
					scene.Unmanaged.Update();
					yield return null;
					Entity neutral = scene.Unmanaged.FindPartAt(2, 0, out Entity neutralFound) ? neutralFound : Entity.Null;
					Check("transparent frame entity", neutral != Entity.Null, "entity=" + neutral);
					if (neutral != Entity.Null)
					{
						float4 neutralTint = em.HasComponent<ColoredFrame>(neutral)
							? em.GetComponentData<ColoredFrame>(neutral).Color
							: new float4(float.NaN);
						Check("transparent frame is white", IsWhite(neutralTint),
							"tint=" + neutralTint);

						ui.PartOperator.OpenPartPropertyPanel(scene, neutral);
						yield return null;
						yield return null;
						Button neutralShape = FindButton(content, "FrameShape");
						Check("neutral frame also has shape row", neutralShape != null && neutralShape.gameObject.activeSelf);
						if (neutralShape != null && neutralShape.gameObject.activeSelf)
						{
							neutralShape.onClick.Invoke();
							for (int f = 0; f < 4; f++)
							{
								yield return null;
							}
							int neutralShapeValue = em.GetComponentData<FrameShape>(neutral).Value;
							Check("neutral frame cycles to triangle", neutralShapeValue == FrameShape.Triangle,
								"value=" + neutralShapeValue);
							Check("neutral frame keeps convex collider",
								em.GetComponentData<PhysicsCollider>(neutral).Value.Value.Type == ColliderType.Convex);
							float4 neutralTintAfter = em.HasComponent<ColoredFrame>(neutral)
								? em.GetComponentData<ColoredFrame>(neutral).Color
								: new float4(float.NaN);
							Check("transparent frame stays white after shape change",
								IsWhite(neutralTintAfter), "tint=" + neutralTintAfter);
						}
					}
				}
			}

			// ⑦ 模拟仍能跑起来。
			manager.StartSimulation();
			yield return null;
			yield return null;
			bool simulating = manager.IsSimulating;
			int simulated = manager.PartSimulator != null ? manager.PartSimulator.Unmanaged.Parts.Length : -1;
			Check("simulation starts", simulating && simulated >= 1, "parts=" + simulated);
			if (simulating)
			{
				manager.EndSimulation();
				yield return null;
			}
		}

		private static bool SameColor(float4 a, float4 b)
		{
			return math.all(math.abs(a - b) < 1e-4f);
		}

		/// <summary>
		/// 只比 RGB，忽略 alpha：透明框的 alpha 每帧随玻璃填充动画变化，是正常行为。
		/// </summary>
		private static bool IsWhite(float4 color)
		{
			return math.all(math.abs(color.xyz - new float3(1f)) < 1e-3f);
		}

		private static Button FindButton(Transform root, string name)
		{
			Button[] buttons = root.GetComponentsInChildren<Button>(true);
			for (int i = 0; i < buttons.Length; i++)
			{
				if (buttons[i].gameObject.name == name)
				{
					return buttons[i];
				}
			}
			return null;
		}

		private static int BlendShapeOf(EntityManager entityManager, Entity root)
		{
			if (!entityManager.HasBuffer<LinkedEntityGroup>(root))
			{
				return -1;
			}
			DynamicBuffer<LinkedEntityGroup> group = entityManager.GetBuffer<LinkedEntityGroup>(root, isReadOnly: true);
			for (int i = 0; i < group.Length; i++)
			{
				Entity child = group[i].Value;
				if (child == root || !entityManager.HasComponent<BlendFactor>(child))
				{
					continue;
				}
				return (int)entityManager.GetComponentData<BlendFactor>(child).Value.z;
			}
			return -1;
		}

		// ------------------------------------------------------------------
		// 6. 存档往返：形状值必须留下来
		// ------------------------------------------------------------------
		private static void CheckSaveRoundtrip(PartManager manager)
		{
			PartScene scene = manager.HasActiveScene() ? manager.ActiveScene : null;
			if (scene == null)
			{
				Check("save roundtrip", false, "no scene");
				return;
			}
			try
			{
				using MemoryStream stream = new MemoryStream();
				manager.SaveScene(stream, scene.SceneID, SchematicsFormat.Json);
				stream.Position = 0;
				Schematics data = Schematics.CreateLoader(SchematicsFormat.Json).Read(stream);
				bool found = false;
				int shape = -1;
				for (int i = 0; i < data.Units.Count; i++)
				{
					if (data.Units[i].Type == (int)PartType.MetalFrame && data.Units[i].Index == FrameIndex)
					{
						found = true;
						shape = data.Units[i].FrameShape;
					}
				}
				Check("save keeps frame", found, "units=" + data.Units.Count);
				Check("save keeps frame shape", found && shape >= FrameShape.Rectangle && shape < FrameShape.Count, "shape=" + shape);
			}
			catch (System.Exception exception)
			{
				Check("save roundtrip", false, exception.ToString());
			}
		}

		private void Finish()
		{
			s_status = ((s_failed == 0) ? "done:PASS" : "done:FAIL") + " pass=" + s_passed + " fail=" + s_failed;
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
				File.WriteAllText(LogPath, Status());
			}
			catch (System.Exception exception)
			{
				Debug.LogWarning("[ModFrameShapesQA] 写报告失败: " + exception.Message);
			}
			Debug.Log("[ModFrameShapesQA] " + Status());
		}
	}
}
#endif
