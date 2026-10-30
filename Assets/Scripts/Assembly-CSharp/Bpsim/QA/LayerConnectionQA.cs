#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using Bpsim.Parts;
using Bpsim.UI;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Bpsim.QA
{
	/// <summary>
	/// 建造图层 + 连接规则 + 无网格虚线提示层的 Play 模式验收探针。
	///
	///   invoke_static type=Bpsim.QA.LayerConnectionQA method=Run    args=[]
	///   invoke_static type=Bpsim.QA.LayerConnectionQA method=Status args=[]
	///
	/// 覆盖：
	/// 1. <see cref="PartBuildLayer" /> 默认值与 Clamp；
	/// 2. 每层独立占位表：跨层同格可叠、同层同格拒绝、编辑只作用于活动层；
	/// 3. 层号进存档（CSV 12 列 / JSON 往返 / 旧 6 列与旧 JSON 归一为层 1）；
	/// 4. 撤销快照恢复层号；
	/// 5. 无网格模式隐藏网格并挂上虚线 overlay / 连接预览 / 图层面板。
	/// 报告写到 &lt;project&gt;/Logs/bpsim-layerconnectionqa.log。
	/// </summary>
	public sealed class LayerConnectionQA : MonoBehaviour
	{
		private static LayerConnectionQA s_instance;

		private static readonly StringBuilder s_report = new StringBuilder();

		private static string s_status = "idle";

		private static int s_passed;

		private static int s_failed;

		public static string Run()
		{
			LayerConnectionQA runner = Ensure();
			s_report.Length = 0;
			s_passed = 0;
			s_failed = 0;
			s_status = "running";
			runner.StartCoroutine(runner.RunChecks());
			return "started";
		}

		public static string Status()
		{
			return s_status + " | pass=" + s_passed + " fail=" + s_failed + "\n" + s_report;
		}

		/// <summary>截图用：展开右上角图层菜单。</summary>
		public static string ShowLayerPanel()
		{
			LayerPanelUI panel = Object.FindObjectOfType<LayerPanelUI>();
			if (panel == null)
			{
				return "missing";
			}
			panel.SetPanelVisible(true);
			return "ok";
		}

		/// <summary>截图用：收起右上角图层菜单。</summary>
		public static string HideLayerPanel()
		{
			LayerPanelUI panel = Object.FindObjectOfType<LayerPanelUI>();
			if (panel == null)
			{
				return "missing";
			}
			panel.SetPanelVisible(false);
			return "ok";
		}

		/// <summary>截图用：开关无网格（黄色虚线）模式。</summary>
		public static string SetGridless(bool enabled)
		{
			PartManager manager = PartManager.Instance;
			if (!manager.HasActiveScene())
			{
				return "no scene";
			}
			manager.ActiveScene.GridlessEnabled = enabled;
			return enabled ? "on" : "off";
		}

		/// <summary>截图用：把镜头拉到指定正交尺寸并回到原点，方便观察细线。</summary>
		public static string SetCameraZoom(float size)
		{
			if (SceneCamera.Instance == null)
			{
				return "no camera";
			}
			SceneCamera instance = SceneCamera.Instance;
			instance.ScaleTo(size);
			instance.MoveTo(Vector2.zero);
			return "zoom=" + instance.TargetSize;
		}

		/// <summary>截图用：搭三件跨层相邻部件，展示青色同层 / 橙色跨层连接预览。</summary>
		public static string SetupConnectionDemo()
		{
			PartManager manager = PartManager.Instance;
			if (manager == null)
			{
				return "no manager";
			}
			manager.LoadEmptyScene("LayerConnectionDemo");
			PartScene scene = manager.HasActiveScene() ? manager.ActiveScene : null;
			if (scene == null)
			{
				return "no scene";
			}
			for (int i = 0; i < 30 && scene.Unmanaged.Parts.Length != 0; i++)
			{
				scene.Unmanaged.ClearParts();
			}
			Entity a = PlaceFrame(scene, manager, 0, 0, 1);
			Entity b = PlaceFrame(scene, manager, 1, 0, 1);
			Entity c = PlaceFrame(scene, manager, 0, 0, 2);
			scene.GridlessEnabled = false;
			scene.Unmanaged.SetActiveLayer(2);
			return "a=" + a + " b=" + b + " c=" + c + " layer=" + scene.Unmanaged.ActiveLayer + " counts=" + scene.Unmanaged.CountPartsOnLayer(1) + "/" + scene.Unmanaged.CountPartsOnLayer(2);
		}

		/// <summary>调试：列出每个根部件的层号与渲染实体的 MaterialColor / PartLayerTint。</summary>
		public static string DumpTint()
		{
			World world = World.DefaultGameObjectInjectionWorld;
			if (world == null)
			{
				return "no world";
			}
			EntityManager em = world.EntityManager;
			EntityQuery query = em.CreateEntityQuery(ComponentType.ReadOnly<PartTag>(), ComponentType.ReadOnly<PartBuildLayer>(), ComponentType.ReadOnly<LinkedEntityGroup>());
			using NativeArray<Entity> roots = query.ToEntityArray(Allocator.Temp);
			System.Text.StringBuilder builder = new System.Text.StringBuilder();
			foreach (Entity root in roots)
			{
				builder.Append("root ").Append(root.Index).Append(" layer=").Append(em.GetComponentData<PartBuildLayer>(root).Value);
				DynamicBuffer<LinkedEntityGroup> group = em.GetBuffer<LinkedEntityGroup>(root, isReadOnly: true);
				for (int i = 0; i < group.Length; i++)
				{
					Entity child = group[i].Value;
					if (!em.HasComponent<Unity.Rendering.MaterialColor>(child))
					{
						continue;
					}
					float4 material = em.GetComponentData<Unity.Rendering.MaterialColor>(child).Value;
					float4 tint = em.HasComponent<Bpsim.Rendering.PartLayerTint>(child) ? em.GetComponentData<Bpsim.Rendering.PartLayerTint>(child).Value : new float4(-1f, -1f, -1f, -1f);
					builder.Append(" | ").Append(child.Index).Append(" m=").Append(material).Append(" t=").Append(tint);
				}
				builder.Append('\n');
			}
			return builder.ToString();
		}

		private static LayerConnectionQA Ensure()
		{
			if (s_instance != null)
			{
				return s_instance;
			}
			GameObject gameObject = new GameObject("[LayerConnectionQA]");
			gameObject.hideFlags = HideFlags.DontSave;
			Object.DontDestroyOnLoad(gameObject);
			s_instance = gameObject.AddComponent<LayerConnectionQA>();
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
			Check("layer defaults", PartBuildLayer.Default.Value == 1 && PartBuildLayer.Clamp(0) == 1 && PartBuildLayer.Clamp(17) == 16 && PartBuildLayer.MinLayer == 1 && PartBuildLayer.MaxLayer == 16, "clamp(0)=" + PartBuildLayer.Clamp(0) + " clamp(17)=" + PartBuildLayer.Clamp(17));

			manager.LoadEmptyScene("LayerConnectionQA");
			yield return null;
			yield return null;
			PartScene scene = manager.HasActiveScene() ? manager.ActiveScene : null;
			Check("active scene", scene != null);
			if (scene == null)
			{
				Finish();
				yield break;
			}
			for (int i = 0; i < 60 && scene.Unmanaged.Parts.Length != 0; i++)
			{
				scene.Unmanaged.ClearParts();
				yield return null;
			}
			Check("scene defaults", scene.Unmanaged.ActiveLayer == 1 && scene.Unmanaged.LayerCount >= 1, "active=" + scene.Unmanaged.ActiveLayer + " count=" + scene.Unmanaged.LayerCount);

			CheckOverlayComponents();

			// 无网格模式：普通网格隐藏，overlay 组件仍在画虚线。
			FieldInfo gridField = typeof(PartScene).GetField("m_grid", BindingFlags.Instance | BindingFlags.NonPublic);
			FieldInfo largeGridField = typeof(PartScene).GetField("m_largeGrid", BindingFlags.Instance | BindingFlags.NonPublic);
			GameObject grid = gridField?.GetValue(scene) as GameObject;
			GameObject largeGrid = largeGridField?.GetValue(scene) as GameObject;
			scene.GridlessEnabled = true;
			yield return null;
			yield return null;
			Check("gridless hides grid", grid != null && !grid.activeSelf && largeGrid != null && !largeGrid.activeSelf, "grid=" + (grid != null && grid.activeSelf) + " large=" + (largeGrid != null && largeGrid.activeSelf));
			scene.GridlessEnabled = false;
			yield return null;
			Check("grid restored", grid != null && grid.activeSelf, "grid=" + (grid != null && grid.activeSelf));

			Entity first = PlaceFrame(scene, manager, 0, 0, 1);
			Entity second = PlaceFrame(scene, manager, 0, 0, 2);
			yield return null;
			Check("seed parts", first != Entity.Null && second != Entity.Null && first != second, "first=" + first + " second=" + second);
			if (first == Entity.Null || second == Entity.Null)
			{
				Finish();
				yield break;
			}
			bool grid1 = scene.Unmanaged.FindPartGridAt(0, 0, 1, out var gridAt1);
			bool grid2 = scene.Unmanaged.FindPartGridAt(0, 0, 2, out var gridAt2);
			Check("cross layer overlap", grid1 && grid2 && (gridAt1.HasPart || gridAt1.HasPartContainer) && (gridAt2.HasPart || gridAt2.HasPartContainer), "layer1=" + grid1 + " layer2=" + grid2);
			Check("layer counts", scene.Unmanaged.CountPartsOnLayer(1) == 1 && scene.Unmanaged.CountPartsOnLayer(2) == 1 && scene.Unmanaged.LayerCount >= 2, "l1=" + scene.Unmanaged.CountPartsOnLayer(1) + " l2=" + scene.Unmanaged.CountPartsOnLayer(2));
			Entity duplicate = PlaceFrame(scene, manager, 0, 0, 2);
			Check("same layer rejects", duplicate == Entity.Null, "duplicate=" + duplicate);

			scene.Unmanaged.SetActiveLayer(2);
			Check("active layer 2", scene.Unmanaged.ActiveLayer == 2);
			bool movedLayer2 = scene.Unmanaged.TryMovePartContent(0, 0, 1, 0);
			bool layer1Kept = scene.Unmanaged.FindPartGridAt(0, 0, 1, out var keptGrid) && (keptGrid.HasPart || keptGrid.HasPartContainer);
			bool layer2Moved = scene.Unmanaged.FindPartGridAt(1, 0, 2, out _);
			Check("edit only active layer", movedLayer2 && layer1Kept && layer2Moved, "moved=" + movedLayer2 + " layer1Kept=" + layer1Kept + " layer2Moved=" + layer2Moved);
			bool movedBack = scene.Unmanaged.TryMovePartContent(1, 0, 0, 0);
			Check("move back on layer 2", movedBack && scene.Unmanaged.FindPartGridAt(0, 0, 2, out _), "movedBack=" + movedBack);
			bool relayer = scene.Unmanaged.TrySetPartLayer(second, 3);
			bool atLayer3 = scene.Unmanaged.FindPartGridAt(0, 0, 3, out _);
			bool layer2Empty = !scene.Unmanaged.FindPartGridAt(0, 0, 2, out _);
			Check("relayer part", relayer && atLayer3 && layer2Empty && scene.Unmanaged.GetPartLayer(second) == 3, "relayer=" + relayer + " at3=" + atLayer3 + " layer2Empty=" + layer2Empty + " layer=" + scene.Unmanaged.GetPartLayer(second));
			scene.Unmanaged.SetActiveLayer(3);
			bool moveOnLayer3 = scene.Unmanaged.TryMovePartContent(0, 0, 1, 0);
			Check("move only active layer", moveOnLayer3 && scene.Unmanaged.FindPartGridAt(1, 0, 3, out _), "moved=" + moveOnLayer3);

			// 存档：层号进 Unit；CSV 13 列（含后加的 FrameShape）；各格式往返保留。
			using NativeArray<Schematics.Unit> units = new NativeArray<Schematics.Unit>(scene.Unmanaged.Parts.Length, Allocator.Persistent);
			PartSceneJobs.SaveParts(scene.Unmanaged.Parts, units).Schedule().Complete();
			bool savedLayer3 = FindUnit(units, 1, 0) is { } movedUnit && movedUnit.Layer == 3;
			bool savedLayer1 = FindUnit(units, 0, 0) is { } baseUnit && baseUnit.Layer == 1;
			Check("save layer fields", savedLayer3 && savedLayer1, "units=" + units.Length);
			Schematics source = Schematics.FromNative(units);
			// 列数：type,index,x,y,rotation,flipped,offsetX,offsetY,rotationDegrees,scaleX,scaleY,layer,frameShape = 13。
			// （FrameShape 是后加的最后一列，读侧 array.Length >= 13 才解析，旧 12 列存档仍然可用。）
			Check("csv 13 columns", CsvColumnCount(source) == 13, "columns=" + CsvColumnCount(source));
			Schematics csv = RoundTrip(source, SchematicsFormat.Csv);
			Schematics json = RoundTrip(source, SchematicsFormat.Json);
			Check("csv roundtrip layer", csv != null && csv.Units.Count == source.Units.Count && csv.Units[0].Layer == source.Units[0].Layer, FirstLayerText(csv));
			Check("json roundtrip layer", json != null && json.Units.Count == source.Units.Count && json.Units[0].Layer == source.Units[0].Layer, FirstLayerText(json));
			Schematics legacyCsv = ReadRaw("0,0,1,0,0,0\n", SchematicsFormat.Csv);
			Check("legacy csv layer 1", legacyCsv != null && legacyCsv.Units.Count == 1 && legacyCsv.Units[0].Layer == 1, FirstLayerText(legacyCsv));
			Schematics legacyJson = ReadRaw("{\"units\":[{\"x\":0,\"y\":0,\"type\":0,\"index\":0,\"rotation\":0,\"flipped\":false}]}", SchematicsFormat.Json);
			Check("legacy json layer 1", legacyJson != null && legacyJson.Units.Count == 1 && legacyJson.Units[0].Layer == 1, FirstLayerText(legacyJson));

			// 撤销快照恢复层号。
			using (BuildUndoHistory history = new BuildUndoHistory())
			{
				history.Record(scene, "layer qa");
				scene.Unmanaged.TrySetPartLayer(first, 4);
				Check("before undo layer", scene.Unmanaged.GetPartLayer(first) == 4, "layer=" + scene.Unmanaged.GetPartLayer(first));
				bool undone = history.Undo(scene);
				for (int i = 0; i < 4; i++)
				{
					yield return null;
				}
				bool restored = scene.Unmanaged.FindPartGridAt(1, 0, 3, out _) && scene.Unmanaged.FindPartGridAt(0, 0, 1, out _);
				Check("undo restores layer", undone && restored, "undone=" + undone + " restored=" + restored);
			}

			Finish();
		}

		private static void CheckOverlayComponents()
		{
			BuildToolController controller = Object.FindObjectOfType<BuildToolController>();
			Check("build controller", controller != null);
			if (controller == null)
			{
				return;
			}
			Check("gridless overlay installed", controller.GetComponent<GridlessOverlay>() != null);
			Check("connection preview installed", controller.GetComponent<PartConnectionPreview>() != null);
			Check("layer panel installed", controller.GetComponent<LayerPanelUI>() != null);
		}

		private static Entity PlaceFrame(PartScene scene, PartManager manager, int x, int y, int layer)
		{
			PartTypeInfo info = new PartTypeInfo(PartType.MetalFrame, 0);
			Entity prefab = manager.EntitySpawner.FindPrefab(info);
			if (prefab == Entity.Null)
			{
				return Entity.Null;
			}
			PartCreationRequest request = new PartCreationRequest(prefab, scene.SceneID, info, x, y, 0, false).WithLayer(layer);
			if (!scene.Unmanaged.PlacePartAt(in request, false))
			{
				return Entity.Null;
			}
			return scene.Unmanaged.FindPartAt(x, y, layer, out var part) ? part : Entity.Null;
		}

		private static Schematics.Unit? FindUnit(NativeArray<Schematics.Unit> units, int x, int y)
		{
			for (int i = 0; i < units.Length; i++)
			{
				if (units[i].X == x && units[i].Y == y)
				{
					return units[i];
				}
			}
			return null;
		}

		private static int CsvColumnCount(Schematics schematics)
		{
			using MemoryStream stream = new MemoryStream();
			Schematics.CreateLoader(SchematicsFormat.Csv).Write(stream, schematics);
			string text = Encoding.UTF8.GetString(stream.ToArray());
			return text.Split('\n')[0].Split(',').Length;
		}

		private static Schematics RoundTrip(Schematics source, SchematicsFormat format)
		{
			try
			{
				using MemoryStream stream = new MemoryStream();
				Schematics.CreateLoader(format).Write(stream, source);
				stream.Position = 0L;
				return Schematics.CreateLoader(format).Read(stream);
			}
			catch (System.Exception exception)
			{
				Check("roundtrip " + format, false, exception.Message);
				return null;
			}
		}

		private static Schematics ReadRaw(string text, SchematicsFormat format)
		{
			try
			{
				using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
				return Schematics.CreateLoader(format).Read(stream);
			}
			catch (System.Exception exception)
			{
				Check("read " + format, false, exception.Message);
				return null;
			}
		}

		private static string FirstLayerText(Schematics schematics)
		{
			if (schematics == null || schematics.Units.Count == 0)
			{
				return "empty";
			}
			return "layer=" + schematics.Units[0].Layer + " scale=" + schematics.Units[0].ScaleX + "," + schematics.Units[0].ScaleY;
		}

		private void Finish()
		{
			s_status = ((s_failed == 0) ? "done:PASS" : "done:FAIL") + " pass=" + s_passed + " fail=" + s_failed;
			string logPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "bpsim-layerconnectionqa.log"));
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(logPath));
				File.WriteAllText(logPath, Status());
			}
			catch (System.Exception exception)
			{
				Debug.LogWarning("[LayerConnectionQA] 写报告失败: " + exception.Message);
			}
			Debug.Log("[LayerConnectionQA] " + Status());
		}
	}
}
#endif
