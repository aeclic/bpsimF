#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Bpsim.Collections;
using Bpsim.Parts;
using Bpsim.Parts.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using UnityEngine;

namespace Bpsim.QA
{
	/// <summary>
	/// BPIR 14 列 CSV 导入的验收探针。
	///
	///   invoke_static type=Bpsim.QA.BpirImportQA method=Run    args=[]
	///   invoke_static type=Bpsim.QA.BpirImportQA method=Status args=[]
	///
	/// 覆盖：
	/// 1. 解析 <c>savefile/Level_Sandbox_04_4</c>：287 行 → 286 件可用（第 74 行坐标溢出被拒）；
	/// 2. 六层分布与存档一致，且 layer 列真的进了建造层；
	/// 3. 成员串映射：offset / rotZ / stretch / @shape 落到 <see cref="PartFreeTransform" /> 与 FrameShape；
	///    没有载体的键（<c>@layer</c> 等）必须出现在告警里，不能静默丢；
	/// 4. 整份存档真的能放进场景（放置数与解析数一致）；
	/// 5. 跨层连接规则：同格优先、无同格才连层 1 四邻域、方向不对不连
	///    （这是「不该连的连上了」的回归测试）。
	///
	/// 报告写到 &lt;project&gt;/Logs/bpsim-bpirimportqa.log。
	/// </summary>
	public sealed class BpirImportQA : MonoBehaviour
	{
		private const string SaveFileRelativePath = "savefile/Level_Sandbox_04_4";

		private static BpirImportQA s_instance;

		private static readonly StringBuilder s_report = new StringBuilder();

		private static string s_status = "idle";

		private static int s_passed;

		private static int s_failed;

		/// <summary>
		/// <see cref="RunChecks" /> 开始前备份的模拟设置。跨层断言依赖真的 weld 出关节，
		/// 所以会临时强制 CreateJoints；跑完必须还原，别把玩家的设置改掉。
		/// </summary>
		private static SimulationSettings s_savedSettings;

		public static string Run()
		{
			BpirImportQA runner = Ensure();
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

		private static BpirImportQA Ensure()
		{
			if (s_instance != null)
			{
				return s_instance;
			}
			GameObject gameObject = new GameObject("[BpirImportQA]");
			gameObject.hideFlags = HideFlags.DontSave;
			UnityEngine.Object.DontDestroyOnLoad(gameObject);
			s_instance = gameObject.AddComponent<BpirImportQA>();
			return s_instance;
		}

		private static void Check(string name, bool ok, string detail = "")
		{
			if (ok)
			{
				s_passed++;
				s_report.Append("PASS ");
			}
			else
			{
				s_failed++;
				s_report.Append("FAIL ");
			}
			s_report.Append(name);
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

			// 跨层断言要靠「焊接出来的连通块」判断，所以必须确保模拟真的会建关节、
			// 而且建出来的关节不会在读取之前就断掉。
			// 1) 用户的 UserSettings 可能把 CreateJoints 关掉（老版本存档不会覆盖默认值，
			//    但设置面板可以改），那样 BuildJointsAndComponentsJob 根本不会跑；
			// 2) 默认的 ConnectionStrengthFactor 只有 4，乘上 fixedDeltaTime 后
			//    maxImpulse 只有 0.08，层 2 的零件悬空挂一秒就被重力扯断，
			//    PartSimulationSystem.RemoveJointsJob 会把关节全部销毁，Joints 变 0。
			// 这里按 PartGameplayQA.Variants 的既有范式备份再强制，结束时恢复。
			SimulationSettings settings = UserSettings.Instance.SimulationSettings;
			s_savedSettings = new SimulationSettings();
			s_savedSettings.Update(settings);
			settings.CreateJoints = true;
			settings.MegaStructure = false;
			settings.InfiniteConnectionStrength = true;
			settings.GravityX = 0f;
			settings.GravityY = 0f;

			string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", SaveFileRelativePath));
			Check("savefile exists", File.Exists(path), path);
			if (!File.Exists(path))
			{
				Finish();
				yield break;
			}

			// ---- 1. 解析 ----
			SchematicsBpirCsvLoader loader = SchematicsBpirCsvLoader.Default;
			Schematics schematics = null;
			Exception parseError = null;
			try
			{
				using Stream stream = File.OpenRead(path);
				schematics = loader.Read(stream);
			}
			catch (Exception exception)
			{
				parseError = exception;
			}
			Check("parse without exception", parseError == null, parseError?.ToString() ?? string.Empty);
			if (schematics == null)
			{
				Finish();
				yield break;
			}
			Check("parsed rows", loader.LastParsedRows == 286, "parsed=" + loader.LastParsedRows + " skipped=" + loader.LastSkippedRows + " members=" + loader.LastMemberRows);
			Check("skipped row is out-of-range", loader.LastSkippedRows == 1, "skipped=" + loader.LastSkippedRows);
			Check("unit count", schematics.Units.Count == 286, "units=" + schematics.Units.Count);
			bool warnedCoord = false;
			bool warnedLayer = false;
			StringBuilder warningDump = new StringBuilder();
			foreach (string warning in loader.LastWarnings)
			{
				if (warning.Contains("超出"))
				{
					warnedCoord = true;
				}
				if (warning.Contains("@layer"))
				{
					warnedLayer = true;
				}
				if (warningDump.Length < 400)
				{
					warningDump.Append(warning).Append(" / ");
				}
			}
			Check("warns about the broken row", warnedCoord, warningDump.ToString());
			Check("@layer reported as unmapped", warnedLayer, warningDump.ToString());

			// ---- 2. 层分布 ----
			int[] expectedLayers = { 121, 40, 38, 35, 14, 38 };
			int[] parsedLayers = new int[7];
			foreach (Schematics.Unit unit in schematics.Units)
			{
				if (unit.Layer >= 1 && unit.Layer <= 6)
				{
					parsedLayers[unit.Layer]++;
				}
			}
			bool layersMatch = true;
			StringBuilder layerText = new StringBuilder();
			for (int layer = 1; layer <= 6; layer++)
			{
				layerText.Append('l').Append(layer).Append('=').Append(parsedLayers[layer]).Append(' ');
				if (parsedLayers[layer] != expectedLayers[layer - 1])
				{
					layersMatch = false;
				}
			}
			Check("layer histogram", layersMatch, layerText.ToString());

			// ---- 3. 成员串映射 ----
			// 存档第 178 行写的是 @offY=1.3，但 BPIR PartPhysicsOverride.SetOffsetY 本身会把
			// 偏移 clamp 到 -1..1（反编译源码 :2039/:2050 与 DecodeMembers :996/:1000），
			// 所以原版读进来也是 1.0。断言要按 clamp 后的值写。
			Schematics.Unit unit178 = FindUnit(schematics, 143, 630);
			Check("stretched frame offsets", Near(unit178.OffsetY, 1f) && Near(unit178.ScaleX, 15f) && Near(unit178.ScaleY, 4f) && unit178.FrameShape == 2, Describe(unit178));
			Schematics.Unit unit32 = FindUnit(schematics, 113, 633);
			Check("rotZ folded into free rotation", Near(unit32.RotationDegrees, -90f), Describe(unit32));
			Schematics.Unit first = schematics.Units[0];
			Check("first row imported", first.Type == 6 && first.Index == 10 && first.X == 107 && first.Y == 633 && first.Layer == 2, Describe(first));

			// ---- 4. 整份存档放进场景 ----
			PartScene scene = LoadEmptySceneSelected(manager, "BpirImportQA");
			yield return null;
			yield return null;
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
			Exception loadError = null;
			PartScene loaded = null;
			try
			{
				using Stream stream = File.OpenRead(path);
				loaded = LoadSceneSelected(manager, stream, "Level_Sandbox_04_4", SchematicsFormat.BpirCsv);
			}
			catch (Exception exception)
			{
				loadError = exception;
			}
			yield return null;
			yield return null;
			Check("load without exception", loadError == null, loadError?.ToString() ?? string.Empty);
			if (loaded == null)
			{
				Check("loaded scene", false);
				Finish();
				yield break;
			}
			int placed = loaded.Unmanaged.Parts.Length;
			Check("placed part count", placed == 286, "placed=" + placed + " parsed=" + schematics.Units.Count);
			bool layerCountsMatch = true;
			StringBuilder placedLayerText = new StringBuilder();
			for (int layer = 1; layer <= 6; layer++)
			{
				int count = loaded.Unmanaged.CountPartsOnLayer(layer);
				placedLayerText.Append('l').Append(layer).Append('=').Append(count).Append(' ');
				if (count != expectedLayers[layer - 1])
				{
					layerCountsMatch = false;
				}
			}
			Check("placed layer histogram", layerCountsMatch, placedLayerText.ToString());
			bool placed178 = loaded.Unmanaged.FindPartAt(143, 630, 1, out Entity entity178);
			PartFreeTransform free178 = placed178 ? loaded.Unmanaged.GetPartFreeTransform(entity178) : PartFreeTransform.Identity;
			Check("placed part keeps free transform", placed178 && Near(free178.Offset.y, 1f) && Near(free178.Scale.x, 15f) && Near(free178.Scale.y, 4f), "found=" + placed178 + " offset=" + free178.Offset + " scale=" + free178.Scale);
			yield return null;

			// ---- 5. 跨层连接规则（回归「不该连的连上了」）----
			yield return CheckCrossLayerRules(manager);
			Finish();
		}

		/// <summary>
		/// 四个受控场景，覆盖 BPIR <c>Contraption.ConnectPart</c> 的分层连接规则。
		/// 每个场景都从空场景重新搭，用 <see cref="PartSimulatorUnmanaged.ConnectedComponents" />
		/// 的条数判断「连上了还是没连」。
		/// </summary>
		private IEnumerator CheckCrossLayerRules(PartManager manager)
		{
			// (a) 同格跨层必须连：层 1 (0,0) 机架 + 层 2 (0,0) 机架。
			yield return ResetScene(manager, "BpirCrossLayerSameCell");
			PartScene scene = manager.ActiveScene;
			if (scene == null)
			{
				Check("cross layer same cell", false, "no scene");
				yield break;
			}
			PlacePart(scene, manager, new PartTypeInfo(PartType.MetalFrame, 0), 0, 0, 0, 1);
			PlacePart(scene, manager, new PartTypeInfo(PartType.MetalFrame, 0), 0, 0, 0, 2);
			yield return StartAndMeasure(manager, "cross layer same cell", 1);

			// (b) 方向不匹配的四邻域跨层不能连：层 1 (1,0) 机架 + 层 2 (0,0) 弹簧(rot 0 = 上下)。
			//     旧实现「无条件焊接四邻域」会把它连成一个刚体，这里必须保持 2 个组件。
			yield return ResetScene(manager, "BpirCrossLayerDirection");
			scene = manager.ActiveScene;
			if (scene == null)
			{
				Check("cross layer direction gate", false, "no scene");
				yield break;
			}
			PlacePart(scene, manager, new PartTypeInfo(PartType.MetalFrame, 0), 1, 0, 0, 1);
			PlacePart(scene, manager, new PartTypeInfo(PartType.Spring, 0), 0, 0, 0, 2);
			yield return StartAndMeasure(manager, "cross layer direction gate", 2);

			// (c) 没有同格时回退连层 1 四邻域：层 1 (1,0) 机架 + 层 2 (0,0) 机架。
			yield return ResetScene(manager, "BpirCrossLayerFallback");
			scene = manager.ActiveScene;
			if (scene == null)
			{
				Check("cross layer neighbour fallback", false, "no scene");
				yield break;
			}
			PlacePart(scene, manager, new PartTypeInfo(PartType.MetalFrame, 0), 1, 0, 0, 1);
			PlacePart(scene, manager, new PartTypeInfo(PartType.MetalFrame, 0), 0, 0, 0, 2);
			yield return StartAndMeasure(manager, "cross layer neighbour fallback", 1);

			// (d) 同格存在时不再扫四邻域：层 1 (0,0) 与 (2,0) 机架（互不相邻），层 2 (0,0) 机架。
			//     层 2 只连 (0,0)，不应另外把 (2,0) 也拉进来；总数必须是 2 个组件。
			yield return ResetScene(manager, "BpirCrossLayerNoSweep");
			scene = manager.ActiveScene;
			if (scene == null)
			{
				Check("cross layer same cell only", false, "no scene");
				yield break;
			}
			PlacePart(scene, manager, new PartTypeInfo(PartType.MetalFrame, 0), 0, 0, 0, 1);
			PlacePart(scene, manager, new PartTypeInfo(PartType.MetalFrame, 0), 2, 0, 0, 1);
			PlacePart(scene, manager, new PartTypeInfo(PartType.MetalFrame, 0), 0, 0, 0, 2);
			yield return StartAndMeasure(manager, "cross layer same cell only", 2);

			// (e) 对照组：同层相邻的两块机架。这一条不涉及任何跨层逻辑，只走
			//     CanConnectTo 的同层四邻域分支；如果它也焊不上（jointsEarly=0），
			//     说明问题不在跨层规则，而在建关节那条路径（扩展数据 / 强度 / CreateJoints）。
			yield return ResetScene(manager, "BpirSameLayerControl");
			scene = manager.ActiveScene;
			if (scene == null)
			{
				Check("same layer control", false, "no scene");
				yield break;
			}
			PlacePart(scene, manager, new PartTypeInfo(PartType.MetalFrame, 0), 0, 0, 0, 1);
			PlacePart(scene, manager, new PartTypeInfo(PartType.MetalFrame, 0), 1, 0, 0, 1);
			yield return StartAndMeasure(manager, "same layer control", 1);
		}

		/// <summary>
		/// <see cref="PartManager.LoadEmptyScene" /> 只会创建场景并登记，不会把它设为当前场景；
		/// 切换是由 UI 页签（<c>SceneTabView</c>）或 <c>LevelSession.LoadSceneFile</c> 做的。
		/// QA 需要自己接住 <c>SceneLoaded</c> 再把新场景选中，否则后面的 <c>StartSimulation</c>
		/// 会跑在上一个（可能已被清空的）场景上。
		/// </summary>
		private static PartScene LoadEmptySceneSelected(PartManager manager, string name)
		{
			PartScene created = null;
			Action<PartScene> handler = delegate(PartScene scene)
			{
				created = scene;
			};
			manager.SceneLoaded += handler;
			try
			{
				manager.LoadEmptyScene(name);
			}
			finally
			{
				manager.SceneLoaded -= handler;
			}
			if (created != null)
			{
				manager.SelectScene(created.SceneID);
			}
			return created;
		}

		/// <summary>
		/// 同 <see cref="LoadEmptySceneSelected" />：读档后把新场景选中再返回。
		/// </summary>
		private static PartScene LoadSceneSelected(PartManager manager, Stream stream, string name, SchematicsFormat format)
		{
			PartScene created = null;
			Action<PartScene> handler = delegate(PartScene scene)
			{
				created = scene;
			};
			manager.SceneLoaded += handler;
			try
			{
				manager.LoadScene(stream, name, format);
			}
			finally
			{
				manager.SceneLoaded -= handler;
			}
			if (created != null)
			{
				manager.SelectScene(created.SceneID);
			}
			return created;
		}

		private IEnumerator ResetScene(PartManager manager, string name)
		{
			if (manager.IsSimulating)
			{
				manager.EndSimulation();
				yield return null;
			}
			// LoadEmptyScene 只把场景登记进 sceneMap 并触发 SceneLoaded，不会切换 activeScene
			// （切换由 UI 的 tab / LevelSession.LoadSceneFile 负责）。QA 里必须自己接住新建场景
			// 再 SelectScene，否则 StartSimulation 跑的还是上一个（已被清空的）场景，
			// 读出来的 simulator.Parts 会是 0。
			LoadEmptySceneSelected(manager, name);
			yield return null;
			yield return null;
			PartScene scene = manager.HasActiveScene() ? manager.ActiveScene : null;
			if (scene == null)
			{
				yield break;
			}
			for (int i = 0; i < 60 && scene.Unmanaged.Parts.Length != 0; i++)
			{
				scene.Unmanaged.ClearParts();
				yield return null;
			}
		}

		private IEnumerator StartAndMeasure(PartManager manager, string label, int expectedComponents)
		{
			PartScene scene = manager.HasActiveScene() ? manager.ActiveScene : null;
			if (scene == null)
			{
				Check(label, false, "no scene");
				yield break;
			}
			int placed = scene.Unmanaged.Parts.Length;
			Check(label + ": parts placed", placed > 0, "placed=" + placed);
			string sceneDump = "sceneID=" + scene.SceneID + " activeID=" + ((manager.ActiveScene != null) ? manager.ActiveScene.SceneID : -1) + " sceneParts=" + placed + " simulating=" + manager.IsSimulating;
			Exception startError = null;
			try
			{
				manager.StartSimulation();
			}
			catch (Exception exception)
			{
				startError = exception;
			}
			// StartSimulation 里的 BuildJointsAndComponentsJob 是同步 Run 的，关节实体由
			// 紧随其后的 commandBuffer.Playback 当场建出来 —— 所以这一行必须放在第一次
			// yield 之前：此时读到的数就是「到底建了没有」，不受之后物理断关节的影响。
			EntityManager em = manager.System.World.EntityManager;
			int jointsEarly = em.CreateEntityQuery(ComponentType.ReadOnly<PartJointInfo>()).CalculateEntityCount();
			int jointsListEarly = manager.PartSimulator.Unmanaged.Joints.Length;
			// BuildJointsAndComponentsJob 末尾会写 ConnectedComponents；这一行同样必须在
			// 第一次 yield 之前读，才能确认 job 到底跑没跑、并查集有没有合并成功。
			int componentsEarly = manager.PartSimulator.Unmanaged.ConnectedComponents.Length;
			yield return null;
			yield return null;
			if (startError != null || !manager.IsSimulating)
			{
				Check(label, false, "StartSimulation failed: " + (startError?.Message ?? "not simulating"));
				yield break;
			}
			// 巨构模式会让 PartSimulator.Run 传 CreateJoints = !mega，一个关节实体都不建，
			// 连通块退化成「每件一块」。把当前生效的设置和刚体组件一起打出来，
			// 才能分清「规则没生效」和「根本没进建关节的分支」。
			SimulationSettings live = UserSettings.Instance.SimulationSettings;
			string settingsDump = "createJoints=" + live.CreateJoints + " mega=" + live.MegaStructure
				+ " infinite=" + live.InfiniteConnectionStrength + " gravity=" + live.GravityX + "," + live.GravityY;
			int withBody = 0;
			for (int i = 0; i < manager.PartSimulator.Unmanaged.Parts.Length; i++)
			{
				Entity simPart = manager.PartSimulator.Unmanaged.Parts[i];
				if (em.Exists(simPart) && em.HasComponent<Unity.Physics.PhysicsCollider>(simPart)) { withBody++; }
			}
			settingsDump += " partsWithCollider=" + withBody;
			// 焊接读的是 Simulator.PartMap（BuildPartsJob 铺的）。如果它是空的，
			// 四邻域 TryGet 全部落空，一个关节都不会建 —— 与规则无关。
			// 这里把每个件的四邻域命中情况和 CanConnectTo 的结果一起打出来。
			settingsDump += " | map=";
			PartSimulatorUnmanaged probeSim = manager.PartSimulator.Unmanaged;
			PartAspect.Lookup aspectLookup = new PartAspect.Lookup(ref manager.System.CheckedStateRef, true);
			for (int i = 0; i < probeSim.Parts.Length; i++)
			{
				Entity simPart = probeSim.Parts[i];
				if (!em.Exists(simPart)) { continue; }
				PartTransform transform = em.GetComponentData<PartTransform>(simPart);
				int partLayer = PartBuildLayerUtility.GetLayer(em, simPart);
				int hits = 0;
				string cellText = "";
				for (int d = 0; d < 4; d++)
				{
					BitDirection dir = BitDirectionExtensions.FromIndex(d);
					(int dx, int dy) = dir.ToVector();
					if (!probeSim.PartMap.TryGet(transform.X + dx, transform.Y + dy, partLayer, out PartGridInfo cell)) { continue; }
					hits++;
					cellText += dir + "{" + (cell.HasPartContainer ? "C" : "") + (cell.HasPart ? "P" : "") + "}";
					Entity other = cell.HasPartContainer ? cell.PartContainer : cell.Part;
					if (other != Entity.Null && other != simPart)
					{
						PartAspect a = aspectLookup[simPart];
						PartAspect b = aspectLookup[other];
						cellText += "can=" + PartSimulator.CanConnectTo(simPart, in a, other, in b, dir);
					}
				}
				settingsDump += "(" + transform.X + "," + transform.Y + ")L" + partLayer + "hits=" + hits + cellText + " ";
			}
			// PartSimulationSystem 要在之后若干帧里才会把关节收进 Simulator.Joints；
			// FreeTransformQA 同样是等约 1 秒后才断言连通块，这里保持一致。
			for (float waited = 0f; waited < 1f; waited += 0.1f)
			{
				yield return new WaitForSeconds(0.1f);
			}

			PartSimulatorUnmanaged simulator = manager.PartSimulator.Unmanaged;
			int simParts = simulator.Parts.Length;
			Check(label + ": simulator parts", simParts == placed, sceneDump + " simParts=" + simParts);

			// 直接用运行时那套并查集算连通块（PartSimulation.FindConnectedComponents 读的是
			// Simulator.Joints 里的 PhysicsConstrainedBodyPair，也就是运行中真正焊接出来的结果）。
			// 不读 PartConnectedComponent：那个组件只有 Dirty 时才会被重算，而部件是从 prefab
			// 实例化来的，初值是 0，读它会得到「全是同一个连通块」的假象。
			ComponentLookup<PhysicsConstrainedBodyPair> pairLookup = manager.System.GetComponentLookup<PhysicsConstrainedBodyPair>(true);
			NativeDisjointSet disjointSet = PartSimulation.FindConnectedComponents(manager.PartSimulator.UnmanagedRef, pairLookup);
			NativeArray<int> indexes = disjointSet.GetComponentIndexes(Allocator.Temp, out int distinctComponents);
			string layout = DescribeSimulatorParts(simulator, em, indexes);
			int joints = simulator.Joints.Length;
			// 关节实体是 BuildJointsAndComponentsJob 用 EntityCommandBuffer 建出来的；运行时
			// 这些实体由 PartSimulationSystem 收进 Simulator.Joints。两个数字分开报，才能分清
			// 「没焊」和「焊了但运行系统没接管」。
			int jointEntities = em.CreateEntityQuery(ComponentType.ReadOnly<PartJointInfo>()).CalculateEntityCount();
			Check(label, distinctComponents == expectedComponents,
				"distinctComponents=" + distinctComponents + " expected=" + expectedComponents
				+ " joints=" + joints + " jointEntities=" + jointEntities + " jointsEarly=" + jointsEarly
				+ " jointsListEarly=" + jointsListEarly
				+ " componentsEarly=" + componentsEarly
				+ " simParts=" + simParts + " " + settingsDump + " | " + layout);
			indexes.Dispose();
			disjointSet.Dispose();
			manager.EndSimulation();
			yield return null;
		}

		private static void PartConnectedComponentLookupProbe(PartSimulatorUnmanaged simulator, EntityManager entityManager, out int withComponent, out int distinctComponents)
		{
			withComponent = 0;
			distinctComponents = 0;
			System.Collections.Generic.HashSet<int> seen = new System.Collections.Generic.HashSet<int>();
			for (int i = 0; i < simulator.Parts.Length; i++)
			{
				Entity part = simulator.Parts[i];
				if (!entityManager.Exists(part) || !entityManager.HasComponent<PartConnectedComponent>(part))
				{
					continue;
				}
				withComponent++;
				seen.Add(entityManager.GetComponentData<PartConnectedComponent>(part).Index);
			}
			distinctComponents = seen.Count;
		}

		/// <summary>
		/// 把模拟里每个部件的「坐标 / 结构层 / 连通块编号」拼成一行，断言失败时一眼能看出
		/// 到底是哪两个零件被焊到了一起。
		/// </summary>
		private static string DescribeSimulatorParts(PartSimulatorUnmanaged simulator, EntityManager entityManager, NativeArray<int> componentIndexes)
		{
			StringBuilder builder = new StringBuilder();
			for (int i = 0; i < simulator.Parts.Length; i++)
			{
				Entity part = simulator.Parts[i];
				if (!entityManager.Exists(part))
				{
					continue;
				}
				PartTransform transform = entityManager.GetComponentData<PartTransform>(part);
				if (builder.Length > 0)
				{
					builder.Append("; ");
				}
				builder.Append('(').Append(transform.X).Append(',').Append(transform.Y).Append(")L")
					.Append(PartBuildLayerUtility.GetLayer(entityManager, part)).Append("=c").Append(componentIndexes[i]);
			}
			return builder.ToString();
		}

		private static void PlacePart(PartScene scene, PartManager manager, PartTypeInfo info, int x, int y, int rotation, int layer)
		{
			Entity prefab = manager.EntitySpawner.FindPrefab(info);
			if (prefab == Entity.Null)
			{
				Check("prefab " + info, false, "FindPrefab returned Null");
				return;
			}
			PartCreationRequest request = new PartCreationRequest(prefab, scene.SceneID, info, x, y, rotation, false).WithLayer(layer);
			if (!scene.Unmanaged.PlacePartAt(in request, false))
			{
				Check("place " + info + " @" + x + "," + y + " l" + layer, false, "PlacePartAt returned false");
			}
		}

		private static Schematics.Unit FindUnit(Schematics schematics, int x, int y)
		{
			foreach (Schematics.Unit unit in schematics.Units)
			{
				if (unit.X == x && unit.Y == y)
				{
					return unit;
				}
			}
			return default(Schematics.Unit);
		}

		private static string Describe(Schematics.Unit unit)
		{
			return "type=" + unit.Type + " index=" + unit.Index + " @" + unit.X + "," + unit.Y + " rot=" + unit.Rotation
				+ " rotZ=" + unit.RotationDegrees + " offset=" + unit.OffsetX + "," + unit.OffsetY
				+ " scale=" + unit.ScaleX + "," + unit.ScaleY + " layer=" + unit.Layer + " shape=" + unit.FrameShape;
		}

		private static bool Near(float a, float b)
		{
			return Mathf.Abs(a - b) <= 0.002f;
		}

		private void Finish()
		{
			if (s_savedSettings != null && UserSettings.Instance != null)
			{
				UserSettings.Instance.SimulationSettings.Update(s_savedSettings);
				s_savedSettings = null;
			}
			s_status = ((s_failed == 0) ? "done:PASS" : "done:FAIL") + " pass=" + s_passed + " fail=" + s_failed;
			string logPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "bpsim-bpirimportqa.log"));
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(logPath));
				File.WriteAllText(logPath, Status());
			}
			catch (Exception exception)
			{
				Debug.LogWarning("[BpirImportQA] 写报告失败: " + exception.Message);
			}
			Debug.Log("[BpirImportQA] " + Status());
		}
	}
}
#endif
