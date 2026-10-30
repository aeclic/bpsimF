#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Bpsim.Parts;
using Bpsim.Parts.Gameplay;
using Bpsim.Parts.Skin;
using Bpsim.UI;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

namespace Bpsim.QA
{
	/// <summary>
	/// Play-mode 验收：蒙皮部件（<see cref="PartType.SkinPart"/>）从预制体烘焙到
	/// ECS 形状组件、网格重建、凸包碰撞体、以及参数改动触发重建的完整链路。
	/// </summary>
	/// <remarks>
	/// 驱动方式（DshBridge）：
	///   invoke_static type=Bpsim.QA.SkinSheetQA method=Run args=[]
	///   invoke_static type=Bpsim.QA.SkinSheetQA method=Status args=[]
	///
	/// 之所以要单独一个 QA 而不是塞进 <c>PartGameplayQA</c>：那边的用例都是
	/// 「行为」（吸盘枪、机炮、轮子）跑在模拟里的，而蒙皮这里的重点是
	/// **建造期的形状数据 → 网格 → 碰撞体**这条没有物理行为的链路，
	/// 需要断言的是组件值和 Mesh 顶点数，不是运动结果。
	/// </remarks>
	public sealed class SkinSheetQA : MonoBehaviour
	{
		private static SkinSheetQA s_instance;

		private static readonly StringBuilder s_report = new StringBuilder();

		private static string s_status = "idle";

		private static bool s_running;

		public static string LogPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "bpsim-skinqa.log"));

		private static SkinSheetQA Ensure()
		{
			if (s_instance != null)
			{
				return s_instance;
			}
			GameObject gameObject = new GameObject("[SkinSheetQA]");
			gameObject.hideFlags = HideFlags.HideAndDontSave;
			UnityEngine.Object.DontDestroyOnLoad(gameObject);
			s_instance = gameObject.AddComponent<SkinSheetQA>();
			return s_instance;
		}

		public static string Run()
		{
			if (s_running)
			{
				return "busy: " + s_status;
			}
			// 先建宿主再置忙：不在 Play Mode 时 <see cref="Ensure" /> 会抛
			// DontDestroyOnLoad 异常，若已置 s_running=true 就会永久卡住，之后每次 Run 都只回 busy。
			SkinSheetQA runner = Ensure();
			s_running = true;
			s_status = "running";
			s_report.Length = 0;
			runner.StartCoroutine(runner.RunScenario());
			return "started";
		}

		public static string Status()
		{
			return s_status + "\n" + s_report;
		}

		private static void Line(string text)
		{
			s_report.Append(text).Append('\n');
			Debug.Log("[SkinQA] " + text);
		}

		private IEnumerator RunScenario()
		{
			int failures = 0;
			PartManager manager = PartManager.Instance;
			if (manager == null)
			{
				Line("FAIL: no PartManager");
				Finish(1);
				yield break;
			}

			// 1) 目录里必须真的有 SkinPart#0，否则选择器不会出按钮。
			bool listed = manager.Factory.PartCollection.TryFindParts(PartType.SkinPart, out var entry);
			int variantCount = 0;
			if (listed && entry != null)
			{
				variantCount = entry.Data.Count;
			}
			Line("catalog: listed=" + listed + " variants=" + variantCount);
			if (!listed || variantCount < 1)
			{
				failures++;
			}

			// 2) 预制体必须能被 EntitySpawner 找到（PartResources 里的引用正确）。
			PartTypeInfo info = new PartTypeInfo(PartType.SkinPart, 0);
			Entity prefab = manager.EntitySpawner.FindPrefab(info);
			Line("prefab: " + (prefab == Entity.Null ? "MISSING" : prefab.ToString()));
			if (prefab == Entity.Null)
			{
				failures++;
				Finish(failures);
				yield break;
			}

			// 3) 放进空场景，检查烘焙出来的形状组件。
			if (manager.IsSimulating)
			{
				manager.EndSimulation();
				yield return null;
			}
			manager.LoadEmptyScene("SkinQA");
			yield return null;
			PartScene scene = manager.ActiveScene;
			if (scene == null)
			{
				Line("FAIL: no active scene");
				Finish(1);
				yield break;
			}
			bool placed = scene.Unmanaged.PlacePartAt(new PartCreationRequest(prefab, scene.SceneID, info, 0, 0, 0, false), overlay: false);
			scene.Unmanaged.Update();
			yield return null;
			Line("placed: " + placed);
			if (!placed)
			{
				failures++;
				Finish(failures);
				yield break;
			}

			Entity part = FindSkinPart(manager);
			Line("entity: " + (part == Entity.Null ? "MISSING" : part.ToString()));
			if (part == Entity.Null)
			{
				failures++;
				Finish(failures);
				yield break;
			}

			EntityManager em = PartGameplayUtil.Em;
			bool hasSheet = em.HasComponent<SkinSheet>(part);
			bool hasMesh = em.HasComponent<SkinSheetMesh>(part);
			bool hasCollider = em.HasComponent<PhysicsCollider>(part);
			Line("components: sheet=" + hasSheet + " mesh=" + hasMesh + " collider=" + hasCollider);
			if (!hasSheet || !hasMesh)
			{
				failures++;
				Finish(failures);
				yield break;
			}

			SkinSheet sheet = em.GetComponentData<SkinSheet>(part);
			Line("defaults: bl=" + Fmt(sheet.BottomLeft) + " tr=" + Fmt(sheet.TopRight) + " version=" + sheet.Version + " tex=" + sheet.TextureHandle);

			// 4) 系统必须已经建好网格（SkinSheetSystem 在 SimulationSystemGroup 里跑）。
			yield return null;
			yield return null;
			SkinSheetMesh mesh = em.GetComponentData<SkinSheetMesh>(part);
			Mesh unityMesh = SkinSheetRegistry.Instance.GetMesh(mesh.Handle);
			int vertexCount = ((unityMesh != null) ? unityMesh.vertexCount : (-1));
			Line("mesh: handle=" + mesh.Handle + " vertices=" + vertexCount + " expected=" + SkinSheetMeshBuilder.VisualVertexCount + " builtVersion=" + mesh.BuiltVersion);
			if (vertexCount != SkinSheetMeshBuilder.VisualVertexCount)
			{
				failures++;
			}

			// 5) 碰撞体必须是凸包（不是预制体里那块占位 Box）。
			string colliderType = "none";
			if (hasCollider)
			{
				ColliderType type = em.GetComponentData<PhysicsCollider>(part).Value.Value.Type;
				colliderType = type.ToString();
				if (type != ColliderType.Convex)
				{
					failures++;
				}
			}
			Line("colliderType: " + colliderType);

			// 6) 改一个角点：网格必须按 Version 重建，且换下来的旧碰撞体不能泄漏到崩溃。
			Bounds before = ((unityMesh != null) ? unityMesh.bounds : default(Bounds));
			sheet.TopRight += new float2(0.25f, 0.5f);
			sheet.Version++;
			em.SetComponentData(part, sheet);
			yield return null;
			yield return null;
			SkinSheetMesh rebuilt = em.GetComponentData<SkinSheetMesh>(part);
			Mesh rebuiltMesh = SkinSheetRegistry.Instance.GetMesh(rebuilt.Handle);
			Bounds after = ((rebuiltMesh != null) ? rebuiltMesh.bounds : default(Bounds));
			bool grew = after.size.y > before.size.y + 0.2f;
			Line("rebuild: builtVersion=" + rebuilt.BuiltVersion + " sizeY " + before.size.y.ToString("F3") + " -> " + after.size.y.ToString("F3") + " grew=" + grew);
			if (!grew || rebuilt.BuiltVersion != sheet.Version)
			{
				failures++;
			}

			// 7) 渲染：PresentationSystemGroup 里的 SkinSheetRenderSystem 必须真的把网格提交出去。
			//    这一步只看计数器，不依赖截图 —— 蒙皮走的是 Graphics.RenderMesh 而非
			//    SceneRenderSystem 的批处理管线，很容易出现「网格建好了但没人画」。
			yield return null;
			int drawn = SkinSheetRenderSystem.LastDrawn;
			int drawnFrame = SkinSheetRenderSystem.LastDrawnFrame;
			bool drawing = drawn >= 1 && Time.frameCount - drawnFrame <= 3;
			Line("render: drawn=" + drawn + " frameAge=" + (Time.frameCount - drawnFrame));
			if (!drawing)
			{
				failures++;
			}

			// 8) 按钮贴图覆盖率：所有「可触发」的 (类型, 索引) 都必须能在 PartButtonList 里查到 sprite。
			//    这直接对应玩家反馈的「按钮只有底板、上面贴图没显示」。
			int checkedButtons = 0;
			List<string> missing = new List<string>();
			AuditButtonSprites(ref checkedButtons, missing);
			Line("buttons: checked=" + checkedButtons + " missing=" + missing.Count + (missing.Count == 0 ? "" : (" -> " + string.Join(",", missing))));
			if (missing.Count > 0)
			{
				failures++;
			}

			// 9) 进模拟再退出来：模拟期间不该有人去改碰撞体（会撕裂物理世界）。
			manager.StartSimulation();
			yield return new WaitForSeconds(0.1f);
			bool simulating = manager.IsSimulating;
			manager.EndSimulation();
			yield return null;
			Line("simulation: entered=" + simulating);
			if (!simulating)
			{
				failures++;
			}

			Line(failures == 0 ? "result: PASS" : ("result: FAIL failures=" + failures));
			Finish(failures);
		}

		private static string Fmt(float2 v)
		{
			return "(" + v.x.ToString("F2") + "," + v.y.ToString("F2") + ")";
		}

		/// <summary>
		/// 枚举目录里所有「可触发」的部件变体，逐个核对 <see cref="PartButtonList" /> 能否给出贴图。
		/// </summary>
		/// <remarks>
		/// 扇叶 / 螺旋桨 / 旋翼的按钮索引取自部件的 <c>Rotation</c>（八向放置时是 0-7），
		/// 其余类型取自 <c>PartIndex</c>，这里按同一规则生成 (类型, 索引)。
		/// </remarks>
		private static void AuditButtonSprites(ref int checkedButtons, List<string> missing)
		{
			// 不要求 PartButtonList.Instance：SimulationInterface 只在实际显示 HUD 时才实例化它，
			// 空场景里为 null。这里直接按同一份登记表自建映射来核对。
			Dictionary<PartButtonInfo, PartButtonList.ButtonSpriteInfo> map = PartButtonList.BuildSpriteInfoMap(
				CoreManager.Instance.Resources.LoadAsset<Texture2D>("PartButton_Texture"));
			var collection = PartManager.Instance.Factory.PartCollection;
			for (int raw = 1; raw < (int)PartType.Max; raw++)
			{
				PartType partType = (PartType)raw;
				if (!collection.TryFindParts(partType, out var entry) || entry == null)
				{
					continue;
				}
				if (partType == PartType.Fan || partType == PartType.Propeller || partType == PartType.Rotor)
				{
					for (int rotation = 0; rotation < 8; rotation++)
					{
						CheckButton(partType, rotation, map, ref checkedButtons, missing);
					}
					continue;
				}
				int variants = entry.Data.Count;
				for (int index = 0; index < variants; index++)
				{
					if (!BasePart.IsTriggerable(new PartTypeInfo(partType, index)))
					{
						continue;
					}
					CheckButton(partType, index, map, ref checkedButtons, missing);
				}
			}
		}

		private static void CheckButton(PartType partType, int partIndex, Dictionary<PartButtonInfo, PartButtonList.ButtonSpriteInfo> map, ref int checkedButtons, List<string> missing)
		{
			checkedButtons++;
			PartButtonInfo info = new PartButtonInfo(PartButtonType.Trigger, 0, partType, partIndex, -1);
			if (!PartButtonList.HasSprite(map, info))
			{
				missing.Add(partType + "#" + partIndex);
			}
		}

		private static Entity FindSkinPart(PartManager manager)
		{
			EntityManager em = PartGameplayUtil.Em;
			if (em.World == null || !em.World.IsCreated)
			{
				return Entity.Null;
			}
			using EntityQuery query = em.CreateEntityQuery(ComponentType.ReadOnly<PartTypeValue>());
			using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
			for (int i = 0; i < entities.Length; i++)
			{
				if (em.GetComponentData<PartTypeValue>(entities[i]).Type == PartType.SkinPart)
				{
					return entities[i];
				}
			}
			return Entity.Null;
		}

		private static void Finish(int failures)
		{
			s_status = ((failures == 0) ? "done:PASS" : ("done:FAIL:" + failures));
			s_running = false;
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
				File.WriteAllText(LogPath, s_status + "\n" + s_report);
			}
			catch (IOException)
			{
			}
		}
	}
}
#endif
