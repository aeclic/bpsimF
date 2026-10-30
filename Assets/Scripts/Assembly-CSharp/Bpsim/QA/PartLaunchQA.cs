#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Text;
using Bpsim.Parts;
using Bpsim.Parts.Gameplay;
using Bpsim.Parts.Simulation;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Bpsim.QA
{
	/// <summary>
	/// 汽水 / 火箭推力（<see cref="LaunchBehavior" />）的 Play 模式验收探针。
	///
	///   invoke_static type=Bpsim.QA.PartLaunchQA method=Run    args=["all"]
	///   invoke_static type=Bpsim.QA.PartLaunchQA method=Status args=[]
	///
	/// 覆盖：
	/// 1. 推力档案（按型号）—— 五种汽水皮肤、两种火箭皮肤各自解出 BPSS ISettingsB 乘完的 force/limit；
	/// 2. 触发后沿零件 local +X 出力，且被速度上限截断（不是无限加速）；
	/// 3. 焊在一起的汽水能推动整个组件（“小车装了汽水会跑”）；
	/// 4. 再次触发关掉推力、第三次触发重新打开（BPSS 的开关语义，不是一次性的）；
	/// 5. 旋转过的零件沿自身朝向出力，而不是永远世界 +X。
	/// 报告写到 &lt;project&gt;/Logs/bpsim-partlaunchqa.log。
	/// </summary>
	public sealed class PartLaunchQA : MonoBehaviour
	{
		private static PartLaunchQA s_instance;

		private static readonly StringBuilder s_report = new StringBuilder();

		private static string s_status = "idle";

		private static int s_passed;

		private static int s_failed;

		public static string Run(string mode = "all")
		{
			PartLaunchQA runner = Ensure();
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

		private static PartLaunchQA Ensure()
		{
			if (s_instance != null)
			{
				return s_instance;
			}
			GameObject gameObject = new GameObject("[PartLaunchQA]");
			gameObject.hideFlags = HideFlags.DontSave;
			Object.DontDestroyOnLoad(gameObject);
			s_instance = gameObject.AddComponent<PartLaunchQA>();
			return s_instance;
		}

		private static void Note(string text)
		{
			s_report.Append("INFO ").Append(text).AppendLine();
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

			CheckProfiles();
			CheckSkins();

			yield return ScenarioBottle(manager, "汽水 CokeBottle#0", PartType.CokeBottle, 0, 70f, 20f);
			yield return ScenarioBottle(manager, "可乐 SodaBottle#0", PartType.SodaBottle, 0, 70f, 20f);
			yield return ScenarioBottle(manager, "异星汽水 SodaBottle#4", PartType.SodaBottle, 4, 200f, 200f);
			yield return ScenarioBottle(manager, "火箭 Rocket#0", PartType.Rocket, 0, 100f, 18f);
			yield return ScenarioDirection(manager);
			yield return ScenarioSodaCart(manager);
			yield return ScenarioToggle(manager);

			Finish();
		}

		private void Finish()
		{
			if (PartManager.Instance != null && PartManager.Instance.IsSimulating)
			{
				PartManager.Instance.EndSimulation();
			}
			s_status = (s_failed == 0) ? "done" : "done-with-failures";
			try
			{
				string path = Path.Combine(Application.dataPath, "../Logs/bpsim-partlaunchqa.log");
				File.WriteAllText(path, s_status + "\n" + s_report);
			}
			catch
			{
				// 报告写不出去不影响判定。
			}
			Debug.Log("[PartLaunchQA] " + Status());
		}

		// ---------------------------------------------------------------- 档案

		private void CheckProfiles()
		{
			CheckProfile("CokeBottle#0", PartType.CokeBottle, 0, 70f, 20f);
			CheckProfile("CokeBottle#4 异星", PartType.CokeBottle, 4, 150f, LaunchBehavior.UnlimitedSpeedCap);
			CheckProfile("SodaBottle#0", PartType.SodaBottle, 0, 70f, 20f);
			CheckProfile("SodaBottle#4 异星", PartType.SodaBottle, 4, 200f, 200f);
			CheckProfile("Rocket#0", PartType.Rocket, 0, 100f, 18f);
			CheckProfile("RedRocket#0", PartType.RedRocket, 0, 100f, 18f);
		}

		private void CheckProfile(string label, PartType type, int index, float force, float limit)
		{
			LaunchProfile profile = LaunchProfile.Resolve(type, index);
			bool ok = math.abs(profile.Force - force) < 0.01f && math.abs(profile.SpeedLimit - limit) < 0.01f;
			Check("profile " + label, ok, "force=" + profile.Force.ToString("F1") + " limit=" + profile.SpeedLimit.ToString("F1")
				+ " 期望 " + force.ToString("F1") + "/" + limit.ToString("F1"));
		}

		private void CheckSkins()
		{
			CheckSkinFamily("CokeBottle", PartType.CokeBottle);
			CheckSkinFamily("SodaBottle", PartType.SodaBottle);
		}

		private void CheckSkinFamily(string label, PartType type)
		{
			PartManager manager = PartManager.Instance;
			int found = 0;
			for (int index = 0; index < 5; index++)
			{
				PartTypeInfo info = new PartTypeInfo(type, index);
				Entity prefab = manager.EntitySpawner.FindPrefab(info);
				if (prefab != Entity.Null)
				{
					found++;
				}
			}
			Check("skins " + label + " 0..4", found == 5, "找到 " + found + "/5 个预制体");
		}

		// ---------------------------------------------------------------- 场景

		private static IEnumerator ScenarioBottle(PartManager manager, string label, PartType type, int index, float force, float limit)
		{
			manager.LoadEmptyScene("LaunchQA");
			yield return null;
			yield return null;
			PartScene scene = manager.HasActiveScene() ? manager.ActiveScene : null;
			if (scene == null)
			{
				Check(label + " 场景", false, "没有活动场景");
				yield break;
			}
			if (!Place(scene, type, index, 0, 0, 0))
			{
				Check(label + " 放置", false, "被拒绝");
				yield break;
			}
			manager.StartSimulation();
			yield return null;
			yield return null;
			Entity part = FindPart(type, index);
			Check(label + " 放置", part != Entity.Null);
			if (part == Entity.Null)
			{
				manager.EndSimulation();
				yield break;
			}
			yield return new WaitForSeconds(0.2f);
			PartSimulation.ProcessTouch(part);
			yield return new WaitForSeconds(1f);
			float3 velocity = PartGameplayUtil.LinearVelocity(part);
			float3 position = PartGameplayUtil.Position(part);
			float mass = ComponentThrust.TotalMass(part);
			float expectedSpeed = (mass > 0.001f) ? math.min((force / mass) * 1f, limit) : limit;
			bool moving = velocity.x > expectedSpeed * 0.6f;
			// 上限是在「下一步加力之前」判定的，所以最后一 tick 还能再冲一点（force/mass*dt）。
			bool capped = velocity.x <= limit + 5f;
			Check(label + " 出力", moving && capped, "v.x=" + velocity.x.ToString("F2") + " x=" + position.x.ToString("F2")
				+ " mass=" + mass.ToString("F2") + " 期望≈" + expectedSpeed.ToString("F1") + " 上限 " + limit.ToString("F1"));
			manager.EndSimulation();
			yield return new WaitForSeconds(0.2f);
		}

		private static IEnumerator ScenarioDirection(PartManager manager)
		{
			manager.LoadEmptyScene("LaunchQA");
			yield return null;
			yield return null;
			PartScene scene = manager.HasActiveScene() ? manager.ActiveScene : null;
			if (scene == null)
			{
				Check("朝向 场景", false, "没有活动场景");
				yield break;
			}
			if (!Place(scene, PartType.CokeBottle, 0, 0, 0, 1))
			{
				Check("朝向 放置", false, "被拒绝");
				yield break;
			}
			manager.StartSimulation();
			yield return null;
			yield return null;
			Entity part = FindPart(PartType.CokeBottle, 0);
			Check("朝向 放置", part != Entity.Null);
			if (part != Entity.Null)
			{
				CheckGrid(part, "朝向", 1);
			}
			if (part == Entity.Null)
			{
				manager.EndSimulation();
				yield break;
			}
			yield return new WaitForSeconds(0.2f);
			PartSimulation.ProcessTouch(part);
			yield return new WaitForSeconds(1f);
			float3 velocity = PartGameplayUtil.LinearVelocity(part);
			bool upward = velocity.y > 8f && math.abs(velocity.x) < 2f;
			Check("朝向 旋转 90° 后向上出力", upward, "v=(" + velocity.x.ToString("F2") + "," + velocity.y.ToString("F2") + ")");
			manager.EndSimulation();
			yield return new WaitForSeconds(0.2f);
		}

		private static IEnumerator ScenarioSodaCart(PartManager manager)
		{
			manager.LoadEmptyScene("LaunchQA");
			yield return null;
			yield return null;
			PartScene scene = manager.HasActiveScene() ? manager.ActiveScene : null;
			if (scene == null)
			{
				Check("小车 场景", false, "没有活动场景");
				yield break;
			}
			bool placedBottle = Place(scene, PartType.CokeBottle, 0, 0, 0, 0);
			bool placedPassenger = Place(scene, PartType.SodaBottle, 1, 1, 0, 0);
			Check("小车 放置两件", placedBottle && placedPassenger);
			if (!placedBottle || !placedPassenger)
			{
				yield break;
			}
			manager.StartSimulation();
			yield return null;
			yield return null;
			Entity bottle = FindPart(PartType.CokeBottle, 0);
			Entity passenger = FindPart(PartType.SodaBottle, 1);
			if (bottle == Entity.Null || passenger == Entity.Null)
			{
				Check("小车 找到两件", false);
				manager.EndSimulation();
				yield break;
			}
			yield return new WaitForSeconds(0.2f);
			PartSimulation.ProcessTouch(bottle);
			yield return new WaitForSeconds(1f);
			float3 bottleVelocity = PartGameplayUtil.LinearVelocity(bottle);
			float3 passengerPosition = PartGameplayUtil.Position(passenger);
			bool together = passengerPosition.x > 0.5f && bottleVelocity.x > 5f;
			Check("小车 汽水推动整个组件", together, "bottle.v.x=" + bottleVelocity.x.ToString("F2") + " passenger.x=" + passengerPosition.x.ToString("F2"));
			manager.EndSimulation();
			yield return new WaitForSeconds(0.2f);
		}

		private static IEnumerator ScenarioToggle(PartManager manager)
		{
			manager.LoadEmptyScene("LaunchQA");
			yield return null;
			yield return null;
			PartScene scene = manager.HasActiveScene() ? manager.ActiveScene : null;
			if (scene == null)
			{
				Check("开关 场景", false, "没有活动场景");
				yield break;
			}
			if (!Place(scene, PartType.SodaBottle, 4, 0, 0, 0))
			{
				Check("开关 放置", false, "被拒绝");
				yield break;
			}
			manager.StartSimulation();
			yield return null;
			yield return null;
			Entity part = FindPart(PartType.SodaBottle, 4);
			Check("开关 放置", part != Entity.Null);
			if (part == Entity.Null)
			{
				manager.EndSimulation();
				yield break;
			}
			yield return new WaitForSeconds(0.2f);

			PartSimulation.ProcessTouch(part);
			yield return new WaitForSeconds(0.3f);
			float3 first = PartGameplayUtil.LinearVelocity(part);

			PartSimulation.ProcessTouch(part);
			yield return new WaitForSeconds(0.3f);
			float3 second = PartGameplayUtil.LinearVelocity(part);

			PartSimulation.ProcessTouch(part);
			yield return new WaitForSeconds(0.3f);
			float3 third = PartGameplayUtil.LinearVelocity(part);

			bool started = first.x > 20f;
			bool stopped = second.x <= first.x * 1.15f + 2f;
			bool resumed = third.x >= second.x + 20f;
			Check("开关 触发后出力", started, "v1.x=" + first.x.ToString("F2"));
			Check("开关 再触发关掉", stopped, "v2.x=" + second.x.ToString("F2"));
			Check("开关 三触发重新打开", resumed, "v3.x=" + third.x.ToString("F2"));
			manager.EndSimulation();
			yield return new WaitForSeconds(0.2f);
		}

		private static void CheckGrid(Entity part, string label, int expectRotation)
		{
			int x, y, rotation;
			bool flipped;
			bool hasGrid = PartGameplayUtil.TryGetGrid(part, out x, out y, out rotation, out flipped);
			quaternion q = PartGameplayUtil.Rotation(part);
			float angle = math.degrees(2f * math.atan2(q.value.z, q.value.w));
			EntityManager em = PartGameplayUtil.Em;
			float localAngle = em.HasComponent<Unity.Transforms.LocalTransform>(part)
				? math.degrees(2f * math.atan2(em.GetComponentData<Unity.Transforms.LocalTransform>(part).Rotation.value.z, em.GetComponentData<Unity.Transforms.LocalTransform>(part).Rotation.value.w))
				: float.NaN;
			Check(label + " 网格旋转", hasGrid && rotation == expectRotation, "grid=" + (hasGrid ? rotation.ToString() : "none") + " expect=" + expectRotation);
			// 实体旋转不是判定项：网格朝向记在 PartTransform 上，物理实体只在 PartTransformSystem 里同步，
			// 而在本项目的 managed DOTS fallback 下那一步不保证已经跑过（实测放下 rot=1 后 localLocalToWorld 仍是 0°）。
			// 推力方向按网格朝向算，所以这里只记录差异。
			Note(label + " 实体旋转 localToWorld=" + angle.ToString("F1") + " local=" + localAngle.ToString("F1") + " 期望≈" + (expectRotation * 90f).ToString("F0"));
		}

		// ---------------------------------------------------------------- 工具

		private static bool Place(PartScene scene, PartType type, int index, int x, int y, int rotation)
		{
			PartManager manager = PartManager.Instance;
			PartTypeInfo info = new PartTypeInfo(type, index);
			Entity prefab = manager.EntitySpawner.FindPrefab(info);
			if (prefab == Entity.Null)
			{
				Check("prefab " + type + "#" + index, false, "找不到预制体");
				return false;
			}
			return scene.Unmanaged.PlacePartAt(new PartCreationRequest(prefab, scene.SceneID, info, x, y, rotation, false), false);
		}

		private static Entity FindPart(PartType type, int index)
		{
			PartManager manager = PartManager.Instance;
			if (manager == null || manager.PartSimulator == null)
			{
				return Entity.Null;
			}
			PartSimulatorUnmanaged simulator = manager.PartSimulator.Unmanaged;
			EntityManager entityManager = PartGameplayUtil.Em;
			for (int i = 0; i < simulator.Parts.Length; i++)
			{
				Entity entity = simulator.Parts[i];
				if (!entityManager.Exists(entity) || !entityManager.HasComponent<PartTypeValue>(entity))
				{
					continue;
				}
				PartTypeValue value = entityManager.GetComponentData<PartTypeValue>(entity);
				if (value.Type == type && value.Index == index)
				{
					return entity;
				}
			}
			return Entity.Null;
		}
	}
}
#endif
