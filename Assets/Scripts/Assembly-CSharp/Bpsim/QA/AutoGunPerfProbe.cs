#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Bpsim.Parts;
using Bpsim.Parts.Gameplay;
using Bpsim.Parts.Simulation;
using Bpsim.UI;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

namespace Bpsim.QA
{
	/// <summary>
	/// Frame-pacing probe for the machine gun (AutoGun): fires the real gun in a real
	/// simulated scene and reports frame times, GC pressure and where the shells explode.
	/// </summary>
	/// <remarks>
	/// The gameplay QA only asserts counters ("shots happened, something exploded"), which
	/// cannot tell a smooth 60 fps burst from a hitchy one, nor whether the shell died at the
	/// muzzle instead of flying to the target. This probe measures both.
	///   invoke_static type=Bpsim.QA.AutoGunPerfProbe method=Run args=["extra=0"]
	///   invoke_static type=Bpsim.QA.AutoGunPerfProbe method=Status
	/// spec: extra=N  -> place N extra MetalFrames (a heavier contraption)
	///       target=1  -> keep the MetalFrame target at 5,0 (default 1)
	///       touch=raw -> 每帧把「按住」转发给模拟层（修复前 SimulationInterface 的行为）
	///       touch=edge-> 每帧只喂指针状态、由 SimulationInterface 自己取按下边缘（修复后）
	///       touch=both-> 两种模式背靠背各跑一次（推荐：一次命令就能对比）
	/// 两者的对比就是要复现/验证的 bug：raw 模式下一发都打不出来，edge 模式正常连发。
	/// </remarks>
	public class AutoGunPerfProbe : MonoBehaviour
	{
		private static AutoGunPerfProbe s_instance;

		private static readonly StringBuilder s_report = new StringBuilder();

		private static string s_status = "idle";

		private static Entity s_ground;

		private static Camera s_probeCamera;

		private static GameObject s_probeCameraObject;

		public static string Status()
		{
			return s_status + "\n" + s_report;
		}

		public static string Run(string spec)
		{
			AutoGunPerfProbe probe = Ensure();
			s_report.Length = 0;
			s_status = "running";
			probe.StartCoroutine(probe.RunRoutine(spec));
			return "started: " + spec;
		}

		private static AutoGunPerfProbe Ensure()
		{
			if (s_instance != null)
			{
				return s_instance;
			}
			GameObject gameObject = new GameObject("[AutoGunPerfProbe]");
			gameObject.hideFlags = HideFlags.DontSave;
			UnityEngine.Object.DontDestroyOnLoad(gameObject);
			s_instance = gameObject.AddComponent<AutoGunPerfProbe>();
			return s_instance;
		}

		private static void Line(string text)
		{
			s_report.AppendLine(text);
			Debug.Log("[AutoGunPerf] " + text);
		}

		private IEnumerator RunRoutine(string spec)
		{
			yield return StartCoroutine(RunCore(spec));
			if (PartManager.Instance != null && PartManager.Instance.IsSimulating)
			{
				PartManager.Instance.EndSimulation();
			}
			DestroyGround();
			s_status = "done";
		}

		private IEnumerator RunCore(string spec)
		{
			int extra = ParseKey(spec, "extra", 0);
			bool withTarget = ParseKey(spec, "target", 1) != 0;
			PartManager manager = PartManager.Instance;
			if (manager == null)
			{
				s_status = "error: no PartManager";
				yield break;
			}
			Line("=== autogun perf probe: " + spec + " extra=" + extra + " target=" + withTarget + " ===");
			if (manager.IsSimulating)
			{
				manager.EndSimulation();
				yield return null;
			}
			DestroyGround();
			manager.LoadEmptyScene("AutoGunPerf");
			yield return null;
			PartScene scene = manager.ActiveScene;
			if (scene == null)
			{
				s_status = "error: no scene";
				yield break;
			}
			Place(scene, PartType.WoodenFrame, 0, 0, 0);
			Place(scene, PartType.WoodenFrame, 0, 1, 0);
			Place(scene, PartType.GrapplingHook, 6, 2, 0);
			if (withTarget)
			{
				Place(scene, PartType.MetalFrame, 0, 5, 0);
			}
			for (int i = 0; i < extra; i++)
			{
				Place(scene, PartType.MetalFrame, 0, i % 12 - 6, -(i / 12) - 2);
			}
			CreateGround(-0.5f);
			scene.Unmanaged.Update();
			yield return null;
			manager.StartSimulation();
			yield return null;
			yield return null;
			Entity gun = FindPart(PartType.GrapplingHook, 6);
			if (gun == Entity.Null)
			{
				s_status = "error: gun not found";
				yield break;
			}
			Line("gun pos=" + Fmt(PartGameplayUtil.Position(gun)));
			Line("timestep=" + UserSettings.Instance.SimulationSettings.FixedTimeStep.ToString("F4", CultureInfo.InvariantCulture)
				+ " parts=" + manager.PartSimulator.Unmanaged.Parts.Length);
			yield return Measure(0.8f, "warmup", Entity.Null, float3.zero, null, "none", Vector2.zero);
			AutoGunProjectileSystem.ResetProbes();
			yield return Measure(1.2f, "idle", Entity.Null, float3.zero, null, "none", Vector2.zero);
			float3 muzzle = PartGameplayUtil.Position(gun);
			string touch = ParseWord(spec, "touch", "none");
			Vector2 screenPoint = Vector2.zero;
			if (touch != "none")
			{
				Camera camera = EnsureProbeCamera(muzzle);
				if (camera == null)
				{
					Line("touch=" + touch + ": no camera, falling back to direct trigger");
					touch = "none";
				}
				else
				{
					screenPoint = camera.WorldToScreenPoint(new Vector3(muzzle.x, muzzle.y, 0f));
				}
			}
			if (touch == "both")
			{
				// 一次跑完修复前后的两种分发方式，方便直接对比。
				yield return StartCoroutine(RunTouchPhase("raw", gun, muzzle, screenPoint));
				yield return StartCoroutine(RunTouchPhase("edge", gun, muzzle, screenPoint));
			}
			else if (touch != "none")
			{
				yield return StartCoroutine(RunTouchPhase(touch, gun, muzzle, screenPoint));
			}
			else
			{
				int triggersBefore = CountTriggers();
				int spawnedBefore = AutoGunProjectileSpawner.SpawnCount;
				Line("gun before: " + GunState(gun));
				Line("--- fire (touch=none) ---");
				PartSimulation.ProcessTouch(gun);
				yield return Measure(2.5f, "firing", gun, muzzle, null, "none", Vector2.zero);
				int triggers = CountTriggers() - triggersBefore;
				Line("triggers=" + triggers + " gun after: " + GunState(gun));
				Line("spawned=" + AutoGunProjectileSpawner.SpawnCount
					+ " (during firing " + (AutoGunProjectileSpawner.SpawnCount - spawnedBefore) + ")"
					+ " exploded=" + AutoGunProjectileSystem.ExplosionCount
					+ " drawnProjectiles=" + AutoGunProjectileSystem.DrawnProjectiles
					+ " drawnFlashes=" + AutoGunProjectileSystem.DrawnFlashes);
			}
			DestroyProbeCamera();
			PartGameplayManager gameplay = PartGameplayManager.Instance;
			if (gameplay != null)
			{
				Line("behaviors: " + gameplay.Describe());
			}
		}

		/// <summary>
		/// 跑一轮「按住不放」的触摸测试：先把机炮归零到关闭，再按 <paramref name="mode" /> 每帧喂输入。
		/// </summary>
		/// <remarks>
		/// 期间把 <see cref="SimulationInterface" /> 组件关掉：它自己的 <c>Update</c> 每帧会用<b>真实</b>
		/// 指针状态调一次 <c>ProcessTouch</c>，探针没有真的按住鼠标，于是每一帧都会把按下状态清成 false，
		/// 注入的「按住」就变成了「每帧新按下」。关掉组件后，由探针独占这条状态机，测的仍是同一段逻辑。
		/// </remarks>
		private IEnumerator RunTouchPhase(string mode, Entity gun, float3 muzzle, Vector2 screenPoint)
		{
			SimulationInterface simulationInterface = SimulationInterface.Instance;
			PartGameplayManager gameplay = PartGameplayManager.Instance;
			if (mode == "edge" && simulationInterface == null)
			{
				Line("[edge] no SimulationInterface (scene not simulating?), skipped");
				yield break;
			}
			if (simulationInterface != null)
			{
				simulationInterface.enabled = false;
				// 先把按下状态清回「松开」，两种模式从同一状态起跑。
				simulationInterface.ProcessTouch(new SimulationInterface.PointerData(pressed: false, screenPoint, Vector2.zero, 0f, Time.unscaledTime));
			}
			if (gameplay != null && gameplay.IsEnabled(gun))
			{
				gameplay.Trigger(gun);
			}
			AutoGunProjectileSystem.ResetProbes();
			int spawnedBefore = AutoGunProjectileSpawner.SpawnCount;
			int triggersBefore = CountTriggers();
			Line("--- " + mode + " --- start: " + GunState(gun));
			yield return Measure(2f, mode, gun, muzzle, null, mode, screenPoint);
			ReleaseTouch(mode, screenPoint);
			Line("[" + mode + "] triggers=" + (CountTriggers() - triggersBefore)
				+ " spawned=" + (AutoGunProjectileSpawner.SpawnCount - spawnedBefore)
				+ " exploded=" + AutoGunProjectileSystem.ExplosionCount
				+ " drawn=" + AutoGunProjectileSystem.DrawnProjectiles
				+ " gun: " + GunState(gun));
			if (simulationInterface != null)
			{
				simulationInterface.enabled = true;
			}
		}

		/// <summary>
		/// 按当前模式每帧喂一次输入，模拟 <c>SimulationInterface.Update</c> 的触摸轮询。
		/// </summary>
		private static void DispatchTouch(string touch, Vector2 screenPoint)
		{
			PartManager manager = PartManager.Instance;
			if (manager == null || !manager.IsSimulating)
			{
				return;
			}
			if (touch == "raw")
			{
				// 修复前的行为：UI 每帧把「按住」直接转发给模拟层。
				PartSimulation.ProcessTouch(manager.PartSimulator.UnmanagedRef, screenPoint);
				return;
			}
			if (touch == "edge")
			{
				// 修复后的行为：UI 只喂指针状态，由它自己取按下边缘。
				SimulationInterface simulationInterface = SimulationInterface.Instance;
				if (simulationInterface != null)
				{
					simulationInterface.ProcessTouch(new SimulationInterface.PointerData(pressed: true, screenPoint, Vector2.zero, 0f, Time.unscaledTime));
				}
			}
		}

		/// <summary>松开指针，把 <c>SimulationInterface</c> 里的按下状态清掉（否则下一轮永远等不到按下边缘）。</summary>
		private static void ReleaseTouch(string touch, Vector2 screenPoint)
		{
			if (touch != "edge")
			{
				return;
			}
			SimulationInterface simulationInterface = SimulationInterface.Instance;
			if (simulationInterface != null)
			{
				simulationInterface.ProcessTouch(new SimulationInterface.PointerData(pressed: false, screenPoint, Vector2.zero, 0f, Time.unscaledTime));
			}
		}

		/// <summary>机炮当前状态（<c>Describe()</c> 里带 enabled/shots/hits）。</summary>
		private static string GunState(Entity gun)
		{
			PartGameplayManager gameplay = PartGameplayManager.Instance;
			if (gameplay == null)
			{
				return "no gameplay";
			}
			PartBehavior behavior = gameplay.Find(gun);
			return behavior == null ? "no behavior" : behavior.Describe();
		}

		/// <summary>
		/// 统计机炮被触发（<c>Trigger()</c>）了多少次。
		/// </summary>
		/// <remarks>
		/// <c>PartGameplayManager.Trigger(Entity)</c> 那句 <c>Debug.Log</c> 只进 Unity 控制台，
		/// 进 <c>m_log</c> 的是 <c>AutoGunBehavior.Trigger()</c> 里的 <c>Manager.Log("autogun: ...")</c>，
		/// 所以这里数的是 <c>"autogun: "</c>。
		/// </remarks>
		private static int CountTriggers()
		{
			PartGameplayManager gameplay = PartGameplayManager.Instance;
			if (gameplay == null)
			{
				return 0;
			}
			string text = gameplay.LogText();
			int count = 0;
			int index = 0;
			while ((index = text.IndexOf("autogun: ", index, StringComparison.Ordinal)) >= 0)
			{
				count++;
				index += 9;
			}
			return count;
		}

		/// <summary>
		/// 拿一个能 <c>WorldToScreenPoint</c> 的相机；场景里没有 <see cref="SceneCamera" /> 时临时造一个。
		/// </summary>
		/// <remarks>
		/// <c>PartSimulation.ProcessTouch(Reference&lt;PartSimulatorUnmanaged&gt;, Vector2)</c> 内部用
		/// <c>SceneCamera.Instance.Camera.ScreenToWorldPoint</c>，所以走这条路径必须有相机。
		/// 造出来的对象在 <see cref="DestroyProbeCamera" /> 里销毁（<c>UnitySingleton.OnDestroy</c> 会把 Instance 清回 null）。
		/// </remarks>
		private static Camera EnsureProbeCamera(float3 focus)
		{
			if (SceneCamera.Instance != null && SceneCamera.Instance.Camera != null)
			{
				return SceneCamera.Instance.Camera;
			}
			if (s_probeCamera != null)
			{
				return s_probeCamera;
			}
			s_probeCameraObject = new GameObject("[AutoGunPerfProbeCamera]");
			s_probeCameraObject.hideFlags = HideFlags.DontSave;
			s_probeCameraObject.transform.position = new Vector3(focus.x, focus.y, -10f);
			Camera camera = s_probeCameraObject.AddComponent<Camera>();
			camera.orthographic = true;
			camera.orthographicSize = 6f;
			s_probeCameraObject.AddComponent<SceneCamera>();
			s_probeCamera = camera;
			return s_probeCamera;
		}

		private static void DestroyProbeCamera()
		{
			if (s_probeCameraObject == null)
			{
				return;
			}
			UnityEngine.Object.Destroy(s_probeCameraObject);
			s_probeCameraObject = null;
			s_probeCamera = null;
		}

		/// <summary>
		/// Samples every frame for <paramref name="seconds"/> and logs a summary line.
		/// </summary>
		private IEnumerator Measure(float seconds, string label, Entity gun, float3 muzzle, Action<string> sink, string touch, Vector2 screenPoint)
		{
			List<float> deltas = new List<float>(512);
			int gc0 = GC.CollectionCount(0);
			long lastMemory = GC.GetTotalMemory(false);
			long maxAlloc = 0L;
			long totalAlloc = 0L;
			int maxLive = 0;
			int explosions = AutoGunProjectileSystem.ExplosionCount;
			List<float> explosionDistances = new List<float>();
			EntityManager entityManager = PartGameplayUtil.Em;
			EntityQuery projectileQuery = default(EntityQuery);
			if (entityManager.World != null && entityManager.World.IsCreated)
			{
				projectileQuery = entityManager.CreateEntityQuery(typeof(AutoGunProjectile));
			}
			float started = Time.realtimeSinceStartup;
			int frames = 0;
			while (Time.realtimeSinceStartup - started < seconds)
			{
				yield return null;
				frames++;
				DispatchTouch(touch, screenPoint);
				deltas.Add(Time.unscaledDeltaTime);
				if (projectileQuery != default(EntityQuery))
				{
					int live = projectileQuery.CalculateEntityCount();
					if (live > maxLive)
					{
						maxLive = live;
					}
				}
				long memory = GC.GetTotalMemory(false);
				long alloc = memory - lastMemory;
				if (alloc > 0L)
				{
					totalAlloc += alloc;
					if (alloc > maxAlloc)
					{
						maxAlloc = alloc;
					}
				}
				lastMemory = memory;
				if (gun != Entity.Null && AutoGunProjectileSystem.ExplosionCount != explosions)
				{
					explosions = AutoGunProjectileSystem.ExplosionCount;
					explosionDistances.Add(math.distance(AutoGunProjectileSystem.LastExplosion, muzzle));
				}
			}
			if (projectileQuery != default(EntityQuery))
			{
				projectileQuery.Dispose();
			}
			int gcCount = GC.CollectionCount(0) - gc0;
			string summary = Summarize(label, deltas, frames, seconds, gcCount, totalAlloc, maxAlloc, maxLive, explosionDistances);
			Line(summary);
			if (sink != null)
			{
				sink(summary);
			}
		}

		private static string Summarize(string label, List<float> deltas, int frames, float seconds, int gcCount, long totalAlloc, long maxAlloc, int maxLive, List<float> explosionDistances)
		{
			if (deltas.Count == 0)
			{
				return "[" + label + "] no frames";
			}
			float rawMax = 0f;
			float rawMin = float.MaxValue;
			for (int i = 0; i < deltas.Count; i++)
			{
				if (deltas[i] > rawMax)
				{
					rawMax = deltas[i];
				}
				if (deltas[i] < rawMin)
				{
					rawMin = deltas[i];
				}
			}
			deltas.Sort();
			float p50 = deltas[deltas.Count / 2] * 1000f;
			float p95 = deltas[System.Math.Min(deltas.Count - 1, (int)(deltas.Count * 0.95f))] * 1000f;
			float fps = frames / Mathf.Max(0.001f, seconds);
			int spikes = 0;
			for (int i = 0; i < deltas.Count; i++)
			{
				float ms = deltas[i] * 1000f;
				if (ms > p50 * 2.5f && ms > 40f)
				{
					spikes++;
				}
			}
			StringBuilder builder = new StringBuilder();
			builder.Append('[').Append(label).Append("] frames=").Append(frames)
				.Append(" fps=").Append(fps.ToString("F1", CultureInfo.InvariantCulture))
				.Append(" frame ms min/p50/p95/max=").Append((rawMin * 1000f).ToString("F2", CultureInfo.InvariantCulture))
				.Append('/').Append(p50.ToString("F2", CultureInfo.InvariantCulture))
				.Append('/').Append(p95.ToString("F2", CultureInfo.InvariantCulture))
				.Append('/').Append((rawMax * 1000f).ToString("F2", CultureInfo.InvariantCulture))
				.Append(" spikes=").Append(spikes)
				.Append(" gc0=").Append(gcCount)
				.Append(" alloc=").Append((totalAlloc / 1024L).ToString(CultureInfo.InvariantCulture)).Append("KB")
				.Append(" maxAlloc/frame=").Append((maxAlloc / 1024L).ToString(CultureInfo.InvariantCulture)).Append("KB")
				.Append(" maxLiveProjectiles=").Append(maxLive);
			if (explosionDistances.Count > 0)
			{
				float minDistance = float.MaxValue;
				for (int i = 0; i < explosionDistances.Count; i++)
				{
					if (explosionDistances[i] < minDistance)
					{
						minDistance = explosionDistances[i];
					}
				}
				builder.Append(" explosions=").Append(explosionDistances.Count)
					.Append(" firstDist=").Append(explosionDistances[0].ToString("F2", CultureInfo.InvariantCulture))
					.Append(" minDist=").Append(minDistance.ToString("F2", CultureInfo.InvariantCulture))
					.Append(" lastDist=").Append(explosionDistances[explosionDistances.Count - 1].ToString("F2", CultureInfo.InvariantCulture));
			}
			return builder.ToString();
		}

		private static string Fmt(float3 value)
		{
			return "(" + value.x.ToString("F2", CultureInfo.InvariantCulture) + "," + value.y.ToString("F2", CultureInfo.InvariantCulture) + ")";
		}

		private static string ParseWord(string spec, string key, string fallback)
		{
			if (string.IsNullOrEmpty(spec))
			{
				return fallback;
			}
			string[] parts = spec.Split(',', ';', ' ');
			for (int i = 0; i < parts.Length; i++)
			{
				string part = parts[i].Trim();
				int num = part.IndexOf('=');
				if (num > 0 && part.Substring(0, num).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
				{
					string word = part.Substring(num + 1).Trim();
					if (word.Length != 0)
					{
						return word;
					}
				}
			}
			return fallback;
		}

		private static int ParseKey(string spec, string key, int fallback)
		{
			if (string.IsNullOrEmpty(spec))
			{
				return fallback;
			}
			string[] parts = spec.Split(',', ';', ' ');
			for (int i = 0; i < parts.Length; i++)
			{
				string part = parts[i].Trim();
				int eq = part.IndexOf('=');
				if (eq > 0 && part.Substring(0, eq).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
				{
					int value;
					if (int.TryParse(part.Substring(eq + 1).Trim(), out value))
					{
						return value;
					}
				}
			}
			return fallback;
		}

		private static Entity FindPart(PartType type, int index)
		{
			PartManager manager = PartManager.Instance;
			if (manager == null || manager.PartSimulator == null)
			{
				return Entity.Null;
			}
			EntityManager entityManager = PartGameplayUtil.Em;
			PartSimulatorUnmanaged simulator = manager.PartSimulator.Unmanaged;
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

		private static bool Place(PartScene scene, PartType type, int index, int x, int y, bool overlay = false)
		{
			PartManager manager = PartManager.Instance;
			PartTypeInfo info = new PartTypeInfo(type, index);
			Entity prefab = manager.EntitySpawner.FindPrefab(info);
			if (prefab == Entity.Null)
			{
				Debug.LogWarning("[AutoGunPerf] no prefab " + type + "#" + index);
				return false;
			}
			return scene.Unmanaged.PlacePartAt(new PartCreationRequest(prefab, scene.SceneID, info, x, y, 0, false), overlay);
		}

		private static Entity CreateGround(float topY)
		{
			DestroyGround();
			EntityManager entityManager = PartGameplayUtil.Em;
			if (entityManager.World == null || !entityManager.World.IsCreated)
			{
				return Entity.Null;
			}
			Entity entity = entityManager.CreateEntity();
			float3 center = new float3(0f, topY - 0.5f, 0f);
			entityManager.AddComponentData(entity, LocalTransform.FromPosition(center));
			entityManager.AddComponentData(entity, new LocalToWorld
			{
				Value = float4x4.Translate(center)
			});
			entityManager.AddComponentData(entity, new PhysicsCollider
			{
				Value = PartPhysics.CreateBox(float3.zero, new float3(160f, 1f, 8f))
			});
			entityManager.AddSharedComponent(entity, new PhysicsWorldIndex(0));
			s_ground = entity;
			return entity;
		}

		private static void DestroyGround()
		{
			if (s_ground == Entity.Null)
			{
				return;
			}
			EntityManager entityManager = PartGameplayUtil.Em;
			if (entityManager.World != null && entityManager.World.IsCreated && entityManager.Exists(s_ground))
			{
				entityManager.DestroyEntity(s_ground);
			}
			s_ground = Entity.Null;
		}
	}
}

#endif
