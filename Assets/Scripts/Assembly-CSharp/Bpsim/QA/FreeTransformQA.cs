#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Text;
using Bpsim.Parts;
using Bpsim.Parts.Simulation;
using Bpsim.UI;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

namespace Bpsim.QA
{
	/// <summary>
	/// 无网格自由变换（平移/旋转/缩放）的 Play 模式验收探针。
	///
	///   invoke_static type=Bpsim.QA.FreeTransformQA method=Run    args=[]
	///   invoke_static type=Bpsim.QA.FreeTransformQA method=Status args=[]
	///
	/// 覆盖四层：
	/// 1. <see cref="PartFreeTransform" /> 的默认值与归一化；
	/// 2. <see cref="PartSceneUnmanaged" /> 的自由变换入口只改 PartFreeTransform、不动网格表；
	/// 3. <see cref="Schematics" /> 的 12 列序列化与旧 6 列读数兼容；
	/// 4. <see cref="PartGizmoMath" /> 的屏幕空间命中（中心/角/边/箭头/圆环）。
	/// 报告写到 &lt;project&gt;/Logs/bpsim-freetransformqa.log。
	/// </summary>
	public sealed class FreeTransformQA : MonoBehaviour
	{
		private static FreeTransformQA s_instance;

		private static readonly StringBuilder s_report = new StringBuilder();

		private static string s_status = "idle";

		private static int s_passed;

		private static int s_failed;

		public static string Run()
		{
			FreeTransformQA runner = Ensure();
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

		private static FreeTransformQA Ensure()
		{
			if (s_instance != null)
			{
				return s_instance;
			}
			GameObject gameObject = new GameObject("[FreeTransformQA]");
			gameObject.hideFlags = HideFlags.DontSave;
			Object.DontDestroyOnLoad(gameObject);
			s_instance = gameObject.AddComponent<FreeTransformQA>();
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

			CheckDefaultsAndSanitize();
			CheckGizmoMath();
			CheckPartPicking();
			CheckScaledColliders();

			manager.LoadEmptyScene("FreeTransformQA");
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
			Entity first = PlaceFrame(scene, manager, 0, 0);
			Entity second = PlaceFrame(scene, manager, 4, 0);
			yield return null;
			Check("seed parts", first != Entity.Null && second != Entity.Null, "parts=" + scene.Unmanaged.Parts.Length);
			if (first == Entity.Null || second == Entity.Null)
			{
				Finish();
				yield break;
			}

			EntityManager entityManager = manager.System.EntityManager;
			PartTransform firstTransform = entityManager.GetComponentData<PartTransform>(first);
			Entity beforeMove = first;
			PartFreeTransform before = scene.Unmanaged.GetPartFreeTransform(first);
			Check("free defaults identity", before.IsIdentity, before.ToString());

			bool moved = scene.Unmanaged.TryMovePartFree(first, new float2(0.37f, -0.24f));
			PartFreeTransform afterMove = scene.Unmanaged.GetPartFreeTransform(first);
			PartTransform afterMoveTransform = entityManager.GetComponentData<PartTransform>(first);
			Check("move free", moved && Near(afterMove.Offset.x, 0.37f) && Near(afterMove.Offset.y, -0.24f), afterMove.ToString());
			Check("move free leaves grid", afterMoveTransform.X == 0 && afterMoveTransform.Y == 0 && scene.Unmanaged.FindPartGridAt(0, 0, out var gridAtOrigin) && (gridAtOrigin.HasPart || gridAtOrigin.HasPartContainer), "grid=(" + afterMoveTransform.X + "," + afterMoveTransform.Y + ")");

			bool rotated = scene.Unmanaged.TryRotatePartFree(first, 33f);
			PartFreeTransform afterRotate = scene.Unmanaged.GetPartFreeTransform(first);
			Check("rotate free", rotated && Near(afterRotate.RotationDegrees, 33f), "angle=" + afterRotate.RotationDegrees);

			bool scaled = scene.Unmanaged.TryScalePartFree(first, new float2(1.5f, 0.75f));
			PartFreeTransform afterScale = scene.Unmanaged.GetPartFreeTransform(first);
			Check("scale free", scaled && Near(afterScale.Scale.x, 1.5f) && Near(afterScale.Scale.y, 0.75f), afterScale.ToString());
			bool clamped = scene.Unmanaged.TryScalePartFree(first, new float2(100f, 0.001f));
			PartFreeTransform afterClamp = scene.Unmanaged.GetPartFreeTransform(first);
			Check("scale clamps", clamped && Near(afterClamp.Scale.x, PartFreeTransform.MaxScale) && Near(afterClamp.Scale.y, PartFreeTransform.MinScale), "clamped=" + afterClamp);
			scene.Unmanaged.TryScalePartFree(first, new float2(1.5f, 0.75f));
			yield return null;
			yield return null;
			EntityManager compositionManager = manager.System.EntityManager;
			LocalTransform composed = compositionManager.GetComponentData<LocalTransform>(first);
			PostTransformScale composedScale = compositionManager.GetComponentData<PostTransformScale>(first);
			float composedAngle = Mathf.Rad2Deg * 2f * Mathf.Atan2(composed.Rotation.value.z, composed.Rotation.value.w);
			Check("ecs composes offset", Near(composed.Position.x, 0.37f) && Near(composed.Position.y, -0.24f), "pos=" + composed.Position);
			Check("ecs composes angle", Near(composedAngle, 33f), "angle=" + composedAngle);
			Check("ecs composes scale", Near(composedScale.Value.c0.x, 1.5f) && Near(composedScale.Value.c1.y, 0.75f) && Near(composedScale.Value.c2.z, 1f), "c0=" + composedScale.Value.c0 + " c1=" + composedScale.Value.c1);

			// 铁框这类部件的视觉就在部件根实体上（ManagedPart 物体自己带 SpriteRenderer），
			// 所以它天然跟着根的 PostTransformScale 走；真正会丢缩放的是外观挂在子实体上的
			// 部件（拳套、TNT 的 Visualization），见 CheckChildVisualScale。

			Check("get bounds", scene.Unmanaged.TryGetPartsBounds(out var boundsMin, out var boundsMax) && boundsMax.x > boundsMin.x && boundsMax.y > boundsMin.y, "min=" + boundsMin + " max=" + boundsMax);

			bool translatedAll = scene.Unmanaged.TryTranslateAllPartsFree(new float2(1.25f, -0.5f));
			PartFreeTransform firstAll = scene.Unmanaged.GetPartFreeTransform(first);
			PartFreeTransform secondAll = scene.Unmanaged.GetPartFreeTransform(second);
			Check("translate all free", translatedAll && Near(firstAll.Offset.x, 1.62f) && Near(firstAll.Offset.y, -0.74f) && Near(secondAll.Offset.x, 1.25f) && Near(secondAll.Offset.y, -0.5f), "first=" + firstAll + " second=" + secondAll);
			PartTransform stillOrigin = entityManager.GetComponentData<PartTransform>(first);
			Check("translate all leaves grid", stillOrigin.X == 0 && stillOrigin.Y == 0, "grid=(" + stillOrigin.X + "," + stillOrigin.Y + ")");

			// 绕原点转 90°：A 世界 (1.62,-0.74) -> (0.74,1.62)；B (5.25,-0.5) -> (0.5,5.25)
			bool rotatedAll = scene.Unmanaged.TryRotateAllPartsFree(90f, float2.zero);
			PartFreeTransform firstRotated = scene.Unmanaged.GetPartFreeTransform(first);
			PartFreeTransform secondRotated = scene.Unmanaged.GetPartFreeTransform(second);
			Check("rotate all free", rotatedAll && Near(firstRotated.Offset.x, 0.74f) && Near(firstRotated.Offset.y, 1.62f) && Near(secondRotated.Offset.x, -3.5f) && Near(secondRotated.Offset.y, 5.25f), "first=" + firstRotated + " second=" + secondRotated);
			Check("rotate all adds angle", Near(firstRotated.RotationDegrees, 123f), "angle=" + firstRotated.RotationDegrees);

			bool scaledAll = scene.Unmanaged.TryScaleAllPartsFree(2f, float2.zero);
			PartFreeTransform firstScaled = scene.Unmanaged.GetPartFreeTransform(first);
			Check("scale all free", scaledAll && Near(firstScaled.Offset.x, 1.48f) && Near(firstScaled.Offset.y, 3.24f) && Near(firstScaled.Scale.x, 3f) && Near(firstScaled.Scale.y, 1.5f), firstScaled.ToString());

			// 视觉拾取：此时 A 的格坐标还是 (0,0)，但视觉中心已被整车平移/旋转/缩放搬到 (1.48,3.24)。
			// 建造工具必须按后者命中，而不能要求点回旧格——这正是用户报的「平移后要点原位置」。
			bool pickedAtVisual = scene.Unmanaged.TryFindPartAtWorld(new float2(1.48f, 3.24f), out Entity pickedVisualPart);
			Check("pick follows visual center", pickedAtVisual && pickedVisualPart == first, "picked=" + pickedVisualPart);
			bool pickedAtOldGrid = scene.Unmanaged.TryFindPartAtWorld(float2.zero, out Entity pickedOldGridPart);
			Check("pick ignores old grid", !pickedAtOldGrid || pickedOldGridPart != first, "picked=" + pickedOldGridPart);

			// --- 序列化 ---
			using (NativeArray<Schematics.Unit> units = new NativeArray<Schematics.Unit>(scene.Unmanaged.Parts.Length, Allocator.Persistent))
			{
				PartSceneJobs.SaveParts(scene.Unmanaged.Parts, units).Schedule().Complete();
				Schematics.Unit saved = default;
				bool found = false;
				for (int i = 0; i < units.Length; i++)
				{
					if (units[i].X == 0 && units[i].Y == 0)
					{
						saved = units[i];
						found = true;
					}
				}
				Check("save parts has free fields", found && Near(saved.OffsetX, 1.48f) && Near(saved.OffsetY, 3.24f) && Near(saved.RotationDegrees, 123f) && Near(saved.ScaleX, 3f) && Near(saved.ScaleY, 1.5f), "unit=" + saved.OffsetX + "," + saved.OffsetY + "," + saved.RotationDegrees + "," + saved.ScaleX + "," + saved.ScaleY);

				Schematics source = Schematics.FromNative(units);
				Check("csv 12 columns", CsvHasTwelveColumns(source), "");
				Schematics csvBack = RoundTrip(source, SchematicsFormat.Csv);
				Check("csv roundtrip", csvBack != null && csvBack.Units.Count == source.Units.Count && Near(csvBack.Units[0].OffsetX, source.Units[0].OffsetX) && Near(csvBack.Units[0].ScaleY, source.Units[0].ScaleY), csvBack == null ? "read failed" : csvBack.Units.Count.ToString());

				Schematics jsonBack = RoundTrip(source, SchematicsFormat.Json);
				Check("json roundtrip", jsonBack != null && jsonBack.Units.Count == source.Units.Count && Near(jsonBack.Units[0].RotationDegrees, source.Units[0].RotationDegrees), jsonBack == null ? "read failed" : "");

				Schematics legacyJson = ReadRaw("{\"units\":[{\"x\":1,\"y\":2,\"type\":0,\"index\":0,\"rotation\":0,\"flipped\":false}]}", SchematicsFormat.Json);
				Check("legacy json normalizes scale", legacyJson != null && legacyJson.Units.Count == 1 && Near(legacyJson.Units[0].ScaleX, 1f) && Near(legacyJson.Units[0].ScaleY, 1f), legacyJson == null ? "read failed" : legacyJson.Units[0].ToString());

				Schematics legacyCsv = ReadRaw("0,0,1,2,0,0\n", SchematicsFormat.Csv);
				Check("legacy csv normalizes scale", legacyCsv != null && legacyCsv.Units.Count == 1 && Near(legacyCsv.Units[0].ScaleX, 1f), legacyCsv == null ? "read failed" : "");

				// --- 撤销 ---
				using (BuildUndoHistory history = new BuildUndoHistory())
				{
					history.Record(scene, "free move");
					scene.Unmanaged.TryMovePartFree(first, new float2(9f, 9f));
					yield return null;
					Check("history undo restores free", history.Undo(scene), "");
					yield return null;
					yield return null;
					Entity restored = scene.Unmanaged.FindPartAt(0, 0, out Entity foundPart) ? foundPart : Entity.Null;
					PartFreeTransform restoredFree = restored != Entity.Null ? scene.Unmanaged.GetPartFreeTransform(restored) : PartFreeTransform.Identity;
					Check("undo keeps offset/scale", restored != Entity.Null && Near(restoredFree.Offset.x, 1.48f) && Near(restoredFree.Offset.y, 3.24f) && Near(restoredFree.Scale.x, 3f), restored == Entity.Null ? "part missing" : restoredFree.ToString());
				}
			}

			Check("entity ids survived", beforeMove != Entity.Null && firstTransform.X == 0, "");

			// 真正的回归：缩小后的载具进模拟后碰撞体必须跟着缩，否则相邻部件互相穿插、
			// 求解器把连接冲断（用户报的"缩放后连接会坏掉"）。
			// 用户报的：整车缩放时拳套 / TNT 这类「外观在子实体上」的部件在建造界面不跟着缩。
			yield return CheckChildVisualScale(manager);
			yield return CheckScaledSimulation(manager);
			Finish();
		}

		/// <summary>
		/// 端到端：外观挂在子实体上的部件（拳套 / TNT 这类，视觉在 <c>Visualization</c> 子物体上）
		/// 在建造期必须跟着根的自由缩放变大小。Unity.Transforms 默认的子物体路径只认父
		/// <c>WorldTransform</c> 的等比 <c>Scale</c>，根的 <c>PostTransformScale</c> 会被丢掉，
		/// 所以部件根实体必须带 <c>PropagateLocalToWorld</c> 才会走矩阵路径——模拟期本来就加，
		/// 建造期以前没加，于是「建造界面不缩、运行界面正常」。
		/// </summary>
		private static IEnumerator CheckChildVisualScale(PartManager manager)
		{
			if (manager.IsSimulating)
			{
				manager.EndSimulation();
				yield return null;
			}
			manager.LoadEmptyScene("FreeTransformChildScale");
			yield return null;
			yield return null;
			PartScene scene = manager.HasActiveScene() ? manager.ActiveScene : null;
			if (scene == null)
			{
				Check("child scale scene", false, "没有活动场景");
				yield break;
			}
			for (int i = 0; i < 60 && scene.Unmanaged.Parts.Length != 0; i++)
			{
				scene.Unmanaged.ClearParts();
				yield return null;
			}
			Entity tnt = PlacePart(scene, manager, new PartTypeInfo(PartType.TNT, 0), 0, 0);
			yield return null;
			yield return null;
			Check("child scale part", tnt != Entity.Null, "parts=" + scene.Unmanaged.Parts.Length);
			if (tnt == Entity.Null)
			{
				yield break;
			}
			EntityManager entityManager = scene.Unmanaged.EntityManager;
			float2 before = MeasureChildScale(entityManager, tnt);
			Check("child scale baseline", before.x > 0.01f && Near(before.x / before.y, 1f), "child=" + before + " " + DescribeChildren(entityManager, tnt));
			scene.Unmanaged.TryScalePartFree(tnt, new float2(2f, 1f));
			yield return null;
			yield return null;
			float2 after = MeasureChildScale(entityManager, tnt);
			Check("child visual follows free scale", after.x > 0.01f && Near(after.x / after.y, 2f) && Near(after.x / before.x, 2f), "before=" + before + " after=" + after + " " + DescribeChildren(entityManager, tnt));
		}

		/// <summary>
		/// 端到端：两格铁框整体缩到一半后进模拟。断言
		/// (1) 建造期碰撞体不变（缩放只在模拟期烘进几何）；
		/// (2) 模拟实体的碰撞体现在是原尺寸的一半；
		/// (3) 跑一小段时间后两个部件仍然是同一个连通块（连接没被冲断）。
		/// </summary>
		private static IEnumerator CheckScaledSimulation(PartManager manager)
		{
			if (manager.IsSimulating)
			{
				manager.EndSimulation();
				yield return null;
			}
			manager.LoadEmptyScene("FreeTransformScaledSim");
			yield return null;
			yield return null;
			PartScene scene = manager.HasActiveScene() ? manager.ActiveScene : null;
			if (scene == null)
			{
				Check("scaled sim scene", false, "没有活动场景");
				yield break;
			}
			for (int i = 0; i < 60 && scene.Unmanaged.Parts.Length != 0; i++)
			{
				scene.Unmanaged.ClearParts();
				yield return null;
			}
			Entity a = PlaceFrame(scene, manager, 0, 0);
			Entity b = PlaceFrame(scene, manager, 1, 0);
			yield return null;
			Check("scaled sim parts", a != Entity.Null && b != Entity.Null, "parts=" + scene.Unmanaged.Parts.Length);
			if (a == Entity.Null || b == Entity.Null)
			{
				yield break;
			}

			EntityManager entityManager = scene.Unmanaged.EntityManager;
			float3 baseExtents = MeasureColliderExtents(entityManager, a);
			float rootScale = entityManager.HasComponent<LocalTransform>(a) ? entityManager.GetComponentData<LocalTransform>(a).Scale : 1f;
			scene.Unmanaged.TryScaleAllPartsFree(0.5f, float2.zero);
			scene.Unmanaged.Update();
			yield return null;
			yield return null;
			PartFreeTransform freeA = scene.Unmanaged.GetPartFreeTransform(a);
			Check("scaled sim scale applied", Near(freeA.Scale.x, 0.5f) && Near(freeA.Scale.y, 0.5f), "scale=" + freeA.Scale.x + "," + freeA.Scale.y);
			float3 buildExtents = MeasureColliderExtents(entityManager, a);
			Check("scaled sim keeps build collider", Near(buildExtents.x, baseExtents.x) && Near(buildExtents.y, baseExtents.y), "build=" + buildExtents + " base=" + baseExtents);

			manager.StartSimulation();
			yield return null;
			yield return null;
			if (!manager.IsSimulating)
			{
				Check("scaled sim started", false, "模拟没起来");
				yield break;
			}
			PartSimulatorUnmanaged simulator = manager.PartSimulator.Unmanaged;
			Entity simA = simulator.Parts.Length > 0 ? simulator.Parts[0] : Entity.Null;
			float3 simExtents = MeasureColliderExtents(scene.Unmanaged.EntityManager, simA);
			Check("sim collider follows free scale", Near(simExtents.x, baseExtents.x * 0.5f * rootScale) && Near(simExtents.y, baseExtents.y * 0.5f * rootScale) && Near(simExtents.z, baseExtents.z), "sim=" + simExtents + " build=" + baseExtents + " rootScale=" + rootScale);

			for (float waited = 0f; waited < 1f; waited += 0.1f)
			{
				yield return new WaitForSeconds(0.1f);
			}
			Check("scaled sim joints hold", simulator.ConnectedComponents.Length == 1, "components=" + simulator.ConnectedComponents.Length + " joints=" + simulator.Joints.Length);
			manager.EndSimulation();
			yield return null;
		}

		/// <summary>
		/// 取部件根实体下第一个渲染子实体的 <c>LocalToWorld</c> 两个轴长度（大在前）。
		/// 建造期整车缩放是否真的作用到外观，就看这个值有没有跟着根的自由缩放变。
		/// 子物体相对根的朝向可能是 90° 的整数倍，两个轴会互换，所以排序后再比。
		/// </summary>
		private static float2 MeasureChildScale(EntityManager entityManager, Entity root)
		{
			if (root == Entity.Null || !entityManager.HasComponent<LinkedEntityGroup>(root))
			{
				return float2.zero;
			}
			DynamicBuffer<LinkedEntityGroup> group = entityManager.GetBuffer<LinkedEntityGroup>(root);
			for (int i = 0; i < group.Length; i++)
			{
				Entity child = group[i].Value;
				if (child == root || !entityManager.HasComponent<PartRenderInfo>(child) || !entityManager.HasComponent<LocalToWorld>(child))
				{
					continue;
				}
				float4x4 matrix = entityManager.GetComponentData<LocalToWorld>(child).Value;
				float x = math.length(matrix.c0.xyz);
				float y = math.length(matrix.c1.xyz);
				if (x > 0.0001f && y > 0.0001f)
				{
					return new float2(math.max(x, y), math.min(x, y));
				}
			}
			return float2.zero;
		}

		/// <summary>诊断用：子实体数量 / 带渲染标记的数量 / 前几个子实体的矩阵轴长。</summary>
		private static string DescribeChildren(EntityManager entityManager, Entity root)
		{
			if (root == Entity.Null || !entityManager.HasComponent<LinkedEntityGroup>(root))
			{
				return "group=none";
			}
			DynamicBuffer<LinkedEntityGroup> group = entityManager.GetBuffer<LinkedEntityGroup>(root);
			int rendered = 0;
			string sample = "";
			for (int i = 0; i < group.Length; i++)
			{
				Entity child = group[i].Value;
				bool hasRender = entityManager.HasComponent<PartRenderInfo>(child);
				if (hasRender)
				{
					rendered++;
				}
				if (i > 0 && i <= 3 && entityManager.HasComponent<LocalToWorld>(child))
				{
					float4x4 m = entityManager.GetComponentData<LocalToWorld>(child).Value;
					sample += "[" + i + " r=" + hasRender + " x=" + math.length(m.c0.xyz) + " y=" + math.length(m.c1.xyz) + "]";
				}
			}
			string childBuffer = entityManager.HasComponent<Child>(root) ? ("child=" + entityManager.GetBuffer<Child>(root).Length) : "child=none";
			float rootX = 0f;
			float rootY = 0f;
			if (entityManager.HasComponent<LocalToWorld>(root))
			{
				float4x4 rootMatrix = entityManager.GetComponentData<LocalToWorld>(root).Value;
				rootX = math.length(rootMatrix.c0.xyz);
				rootY = math.length(rootMatrix.c1.xyz);
			}
			return "name=" + entityManager.GetName(root) + " group=" + group.Length + " rendered=" + rendered + " rootRender=" + entityManager.HasComponent<PartRenderInfo>(root) + " " + childBuffer + " propagate=" + entityManager.HasComponent<PropagateLocalToWorld>(root) + " rootAxes=(" + rootX + "," + rootY + ")" + sample;
		}

		private static float3 MeasureColliderExtents(EntityManager entityManager, Entity entity)
		{
			if (entity == Entity.Null || !entityManager.HasComponent<PhysicsCollider>(entity))
			{
				return float3.zero;
			}
			BlobAssetReference<Unity.Physics.Collider> collider = entityManager.GetComponentData<PhysicsCollider>(entity).Value;
			if (!collider.IsCreated)
			{
				return float3.zero;
			}
			return collider.Value.CalculateAabb().Extents;
		}

		private static void CheckDefaultsAndSanitize()
		{
			PartFreeTransform identity = PartFreeTransform.Identity;
			Check("identity scale", Near(identity.Scale.x, 1f) && Near(identity.Scale.y, 1f) && identity.IsIdentity, identity.ToString());
			PartFreeTransform dirty = default(PartFreeTransform);
			PartFreeTransform clean = PartFreeTransform.Sanitize(dirty);
			Check("sanitize zero scale", Near(clean.Scale.x, 1f) && Near(clean.Scale.y, 1f), clean.ToString());
			PartFreeTransform wild = new PartFreeTransform
			{
				Offset = new float2(float.NaN, float.PositiveInfinity),
				RotationDegrees = float.NaN,
				Scale = new float2(100f, -1f)
			};
			PartFreeTransform wildClean = PartFreeTransform.Sanitize(wild);
			Check("sanitize non-finite", Near(wildClean.Offset.x, 0f) && Near(wildClean.Offset.y, 0f) && Near(wildClean.RotationDegrees, 0f) && Near(wildClean.Scale.x, PartFreeTransform.MaxScale) && Near(wildClean.Scale.y, 1f), wildClean.ToString());
		}

		private static void CheckGizmoMath()
		{
			PartTransform transform = new PartTransform
			{
				X = 0,
				Y = 0,
				Rotation = 0,
				Flipped = false
			};
			PartFreeTransform free = new PartFreeTransform
			{
				Offset = float2.zero,
				RotationDegrees = 0f,
				Scale = new float2(1f, 1f)
			};
			GizmoFrame frame = GizmoFrame.ForPart(transform, free);
			Check("gizmo frame pivot", Near(frame.Pivot.x, 0f) && Near(frame.Pivot.y, 0f) && !frame.Machine, frame.Pivot.ToString());
			Check("gizmo ring radius", Near(frame.RingRadius, 1.05f) && Near(frame.ArrowLength, 1.6f), "ring=" + frame.RingRadius + " arrow=" + frame.ArrowLength);
			Check("gizmo corners", Near(frame.Corner(2).x, 0.5f) && Near(frame.Corner(2).y, 0.5f) && Near(frame.Corner(0).x, -0.5f), frame.Corner(0).ToString());
			GizmoFrame rotatedFrame = GizmoFrame.ForPart(transform, new PartFreeTransform
			{
				Offset = float2.zero,
				RotationDegrees = 90f,
				Scale = new float2(1f, 1f)
			});
			rotatedFrame.Axes(out var axisX, out var axisY);
			Check("gizmo axes rotate", Near(axisX.x, 0f) && Near(axisX.y, 1f) && Near(axisY.x, -1f), "x=" + axisX + " y=" + axisY);
			Check("delta angle wraps", Near(PartGizmoMath.DeltaAngle(math.radians(350f), math.radians(10f)), math.radians(20f)), "");
			Check("sense signs", Near(PartGizmoMath.AxisSign(0).x, -1f) && Near(PartGizmoMath.AxisSign(2).y, 1f) && Near(PartGizmoMath.EdgeSign(1).x, -1f), "");

			GameObject cameraObject = new GameObject("[FreeTransformQA Camera]");
			Camera camera = cameraObject.AddComponent<Camera>();
			camera.orthographic = true;
			camera.orthographicSize = 5f;
			camera.transform.position = new Vector3(0f, 0f, -10f);
			camera.transform.rotation = Quaternion.identity;
			Vector2 centerScreen = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
			bool centerHit = PartGizmoMath.HitTest(centerScreen, camera, in frame, true, 1f, out var centerHandle);
			Check("hit center", centerHit && centerHandle.Handle == GizmoHandle.FreeMove, centerHandle.Handle.ToString());
			Vector2 cornerScreen = PartGizmoMath.Project(camera, frame.Corner(2));
			bool cornerHit = PartGizmoMath.HitTest(cornerScreen, camera, in frame, true, 1f, out var cornerHandle);
			Check("hit corner", cornerHit && cornerHandle.Handle == GizmoHandle.ScaleCorner && cornerHandle.Index == 2, cornerHandle.Handle + " " + cornerHandle.Index);
			float pixelsPerUnit = Screen.height / (camera.orthographicSize * 2f);
			Vector2 ringScreen = PartGizmoMath.Project(camera, frame.Pivot) + new Vector2(0.70710677f, 0.70710677f) * (frame.RingRadius * pixelsPerUnit);
			bool ringHit = PartGizmoMath.HitTest(ringScreen, camera, in frame, true, 1f, out var ringHandle);
			Check("hit ring", ringHit && ringHandle.Handle == GizmoHandle.Rotate, ringHandle.Handle.ToString());
			Vector2 arrowScreen = PartGizmoMath.Project(camera, frame.Pivot + new float2(frame.ArrowLength * 0.8f, 0f));
			bool arrowHit = PartGizmoMath.HitTest(arrowScreen, camera, in frame, false, 1f, out var arrowHandle);
			Check("hit x arrow", arrowHit && arrowHandle.Handle == GizmoHandle.MoveX, arrowHandle.Handle.ToString());
			GizmoFrame machineFrame = GizmoFrame.ForMachine(new float2(-2f, -1f), new float2(2f, 1f));
			Vector2 machineCorner = PartGizmoMath.Project(camera, machineFrame.Corner(1));
			bool machineHit = PartGizmoMath.HitTest(machineCorner, camera, in machineFrame, true, 1f, out var machineHandle);
			Check("hit machine corner", machineHit && machineHandle.Handle == GizmoHandle.ScaleCorner && machineFrame.Machine, machineHandle.Handle.ToString());
			Object.Destroy(cameraObject);
		}

		/// <summary>
		/// 建造拾取的纯几何：命中形状 = 格坐标 + 自由偏移，再按「网格角 + 自由角」反旋转、按自由缩放反缩放，
		/// 最后与 <see cref="BasePart.GetGridRect" /> 的局部矩形比较。大件（KingPig 3×2）的矩形不是以格原点为中心，
		/// 旋转后也必须跟着转。
		/// </summary>
		private static void CheckPartPicking()
		{
			PartTypeInfo frame = new PartTypeInfo(PartType.MetalFrame, 0);
			PartTransform frameAt = new PartTransform
			{
				X = 2,
				Y = 3,
				Rotation = 0,
				Flipped = false
			};
			PartFreeTransform identity = PartFreeTransform.Identity;
			Check("pick | cell center", PartPicking.Contains(in frameAt, in identity, frame, new float2(2f, 3f)), "");
			Check("pick | cell edge", PartPicking.Contains(in frameAt, in identity, frame, new float2(2.49f, 2.51f)), "");
			Check("pick | outside cell", !PartPicking.Contains(in frameAt, in identity, frame, new float2(2.6f, 3f)), "");

			PartFreeTransform offsetFree = new PartFreeTransform
			{
				Offset = new float2(1.25f, -0.5f),
				RotationDegrees = 0f,
				Scale = new float2(1f, 1f)
			};
			Check("pick | follows offset", PartPicking.Contains(in frameAt, in offsetFree, frame, new float2(3.25f, 2.5f)) && !PartPicking.Contains(in frameAt, in offsetFree, frame, new float2(2f, 3f)), "offset=" + offsetFree.Offset);

			PartFreeTransform halfScale = new PartFreeTransform
			{
				Offset = float2.zero,
				RotationDegrees = 0f,
				Scale = new float2(0.5f, 0.5f)
			};
			Check("pick | follows scale", PartPicking.Contains(in frameAt, in halfScale, frame, new float2(2.2f, 3f)) && !PartPicking.Contains(in frameAt, in halfScale, frame, new float2(2.4f, 3f)), "scale=" + halfScale.Scale);

			PartTypeInfo pig = new PartTypeInfo(PartType.KingPig, 0);
			PartTransform pigAt = new PartTransform
			{
				X = 0,
				Y = 0,
				Rotation = 0,
				Flipped = false
			};
			Check("pick | large part rect", PartPicking.Contains(in pigAt, in identity, pig, new float2(1.4f, 1.4f)) && !PartPicking.Contains(in pigAt, in identity, pig, new float2(1.4f, 1.6f)) && !PartPicking.Contains(in pigAt, in identity, pig, new float2(-1.6f, 0.5f)), "");
			PartFreeTransform quarterTurn = new PartFreeTransform
			{
				Offset = float2.zero,
				RotationDegrees = 90f,
				Scale = new float2(1f, 1f)
			};
			Check("pick | large part rotated", PartPicking.Contains(in pigAt, in quarterTurn, pig, new float2(-0.8f, 0.8f)) && !PartPicking.Contains(in pigAt, in quarterTurn, pig, new float2(1.4f, 1.4f)), "");
		}

		/// <summary>
		/// 模拟期碰撞体缩放（<see cref="PartPhysics.CreateScaled" />）。
		///
		/// 部件根实体始终带 PostTransformScale，Unity.Physics 因此把 RigidBody.Scale 当成 1
		/// （缩放被认为已经烘进几何），模拟期的碰撞体永远是 prefab 原尺寸。缩小载具时部件间距变小、
		/// 碰撞体不变，互相穿插被求解器弹开，固定关节冲量超阈就断 —— 表现就是"缩放后连接坏掉"。
		/// 所以进入模拟前必须按自由缩放重建几何：这里断言几何、中心、半径乃至质量分布都跟着缩。
		/// </summary>
		private static void CheckScaledColliders()
		{
			BlobAssetReference<Unity.Physics.Collider> box = PartPhysics.CreateBox(new float3(0f, 0.5f, 0f), new float3(1f, 1f, 1f));
			Aabb boxBounds = box.Value.CalculateAabb();
			float boxVolume = box.Value.MassProperties.Volume;

			BlobAssetReference<Unity.Physics.Collider> half = PartPhysics.CreateScaled(box, new float2(0.5f, 0.5f));
			Check("scaled box type", half.IsCreated && half.Value.Type == ColliderType.Box, half.IsCreated ? half.Value.Type.ToString() : "not created");
			Aabb halfBounds = half.Value.CalculateAabb();
			float3 halfCenter = 0.5f * (halfBounds.Min + halfBounds.Max);
			Check("scaled box bounds", Near(halfBounds.Extents.x, 0.5f) && Near(halfBounds.Extents.y, 0.5f) && Near(halfBounds.Extents.z, 1f) && Near(halfCenter.y, 0.25f), "extents=" + halfBounds.Extents + " center=" + halfCenter);
			Check("scaled box mass", Near(half.Value.MassProperties.Volume, boxVolume * 0.25f), "volume=" + half.Value.MassProperties.Volume + " base=" + boxVolume);

			BlobAssetReference<Unity.Physics.Collider> aniso = PartPhysics.CreateScaled(box, new float2(2f, 0.25f));
			Aabb anisoBounds = aniso.Value.CalculateAabb();
			float3 anisoCenter = 0.5f * (anisoBounds.Min + anisoBounds.Max);
			Check("scaled box non-uniform", Near(anisoBounds.Extents.x, 2f) && Near(anisoBounds.Extents.y, 0.25f) && Near(anisoBounds.Extents.z, 1f) && Near(anisoCenter.y, 0.125f), "extents=" + anisoBounds.Extents + " center=" + anisoCenter);

			BlobAssetReference<Unity.Physics.Collider> sphere = PartPhysics.CreateSphere(new float3(0.1f, 0f, 0f), 0.4f);
			BlobAssetReference<Unity.Physics.Collider> scaledSphere = PartPhysics.CreateScaled(sphere, new float2(0.5f, 0.5f));
			Aabb sphereBounds = scaledSphere.Value.CalculateAabb();
			float3 sphereCenter = 0.5f * (sphereBounds.Min + sphereBounds.Max);
			Check("scaled sphere", Near(sphereBounds.Extents.x, 0.4f) && Near(sphereBounds.Extents.y, 0.4f) && Near(sphereCenter.x, 0.05f), "extents=" + sphereBounds.Extents + " center=" + sphereCenter);

			BlobAssetReference<Unity.Physics.Collider> capsule = PartPhysics.CreateCapsule(new float3(0f, -0.5f, 0f), new float3(0f, 0.5f, 0f), 0.1f);
			BlobAssetReference<Unity.Physics.Collider> scaledCapsule = PartPhysics.CreateScaled(capsule, new float2(1f, 3f));
			Aabb capsuleBounds = capsule.Value.CalculateAabb();
			Aabb scaledCapsuleBounds = scaledCapsule.Value.CalculateAabb();
			Check("scaled capsule", Near(capsuleBounds.Extents.y, 1.2f) && Near(scaledCapsuleBounds.Extents.y, 3.2f) && Near(scaledCapsuleBounds.Extents.x, 0.2f), "extents=" + scaledCapsuleBounds.Extents + " base=" + capsuleBounds.Extents);

			// 复合碰撞体是 prefab 的常态（铁框就有 134 个子件）：子件相对位移与子几何都要一起缩。
			NativeArray<CompoundCollider.ColliderBlobInstance> children = new NativeArray<CompoundCollider.ColliderBlobInstance>(2, Allocator.Temp);
			try
			{
				children[0] = new CompoundCollider.ColliderBlobInstance
				{
					CompoundFromChild = new RigidTransform(quaternion.identity, new float3(-0.5f, 0f, 0f)),
					Collider = PartPhysics.CreateBox(float3.zero, new float3(1f, 1f, 1f)),
					Entity = Entity.Null
				};
				children[1] = new CompoundCollider.ColliderBlobInstance
				{
					CompoundFromChild = new RigidTransform(quaternion.identity, new float3(0.5f, 0f, 0f)),
					Collider = PartPhysics.CreateBox(float3.zero, new float3(1f, 1f, 1f)),
					Entity = Entity.Null
				};
				BlobAssetReference<Unity.Physics.Collider> compound = CompoundCollider.Create(children);
				children[0].Collider.Dispose();
				children[1].Collider.Dispose();
				Aabb compoundBounds = compound.Value.CalculateAabb();
				float compoundVolume = compound.Value.MassProperties.Volume;
				BlobAssetReference<Unity.Physics.Collider> scaledCompound = PartPhysics.CreateScaled(compound, new float2(0.5f, 0.5f));
				Check("scaled compound type", scaledCompound.IsCreated && scaledCompound.Value.Type == ColliderType.Compound, scaledCompound.IsCreated ? scaledCompound.Value.Type.ToString() : "not created");
				Aabb scaledBounds = scaledCompound.Value.CalculateAabb();
				Check("scaled compound bounds", Near(scaledBounds.Extents.x, compoundBounds.Extents.x * 0.5f) && Near(scaledBounds.Extents.y, compoundBounds.Extents.y * 0.5f) && Near(scaledBounds.Extents.z, compoundBounds.Extents.z), "extents=" + scaledBounds.Extents + " base=" + compoundBounds.Extents);
				Check("scaled compound mass", Near(scaledCompound.Value.MassProperties.Volume, compoundVolume * 0.25f), "volume=" + scaledCompound.Value.MassProperties.Volume + " base=" + compoundVolume);
				scaledCompound.Dispose();
				compound.Dispose();
			}
			finally
			{
				children.Dispose();
			}

			scaledCapsule.Dispose();
			capsule.Dispose();
			scaledSphere.Dispose();
			sphere.Dispose();
			aniso.Dispose();
			half.Dispose();
			box.Dispose();
		}

		private static bool CsvHasTwelveColumns(Schematics schematics)
		{
			using MemoryStream stream = new MemoryStream();
			Schematics.CreateLoader(SchematicsFormat.Csv).Write(stream, schematics);
			string text = Encoding.UTF8.GetString(stream.ToArray());
			string line = text.Split('\n')[0];
			return line.Split(',').Length == 12;
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

		private static Entity PlaceFrame(PartScene scene, PartManager manager, int x, int y)
		{
			return PlacePart(scene, manager, new PartTypeInfo(PartType.MetalFrame, 0), x, y);
		}

		private static Entity PlacePart(PartScene scene, PartManager manager, PartTypeInfo info, int x, int y)
		{
			Entity prefab = manager.EntitySpawner.FindPrefab(info);
			if (prefab == Entity.Null)
			{
				return Entity.Null;
			}
			PartCreationRequest request = new PartCreationRequest(prefab, scene.SceneID, info, x, y, 0, false);
			if (!scene.Unmanaged.PlacePartAt(in request, false))
			{
				return Entity.Null;
			}
			return scene.Unmanaged.FindPartAt(x, y, out var part) ? part : Entity.Null;
		}

		private static bool Near(float value, float expected)
		{
			return Mathf.Abs(value - expected) <= 0.02f;
		}

		private void Finish()
		{
			s_status = ((s_failed == 0) ? "done:PASS" : "done:FAIL") + " pass=" + s_passed + " fail=" + s_failed;
			string logPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "bpsim-freetransformqa.log"));
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(logPath));
				File.WriteAllText(logPath, Status());
			}
			catch (System.Exception exception)
			{
				Debug.LogWarning("[FreeTransformQA] 写报告失败: " + exception.Message);
			}
			Debug.Log("[FreeTransformQA] " + Status());
		}
	}
}
#endif
