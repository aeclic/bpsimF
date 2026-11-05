#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Bpsim.Parts.Simulation;
using Bpsim.Physics.Anvil;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Collider = Unity.Physics.Collider;
using Unity.Physics.Systems;
using Unity.Transforms;
using UnityEngine;

namespace Bpsim.QA
{
	/// <summary>
	/// 三后端物理基准：同一份合成场景（一块静态地面 + N 个动态盒）分别用
	/// Unity.Physics / Havok / Anvil(box3d) 跑固定步数，采样「单个物理 step」的墙钟耗时。
	/// </summary>
	/// <remarks>
	/// 计时点落在 <see cref="PhysicsSystemGroup" /> 的首尾两个探针系统上，因此每次
	/// MarkStart/MarkEnd 恰好对应一个固定物理步，与帧率、渲染负载无关。
	/// <para>
	///   invoke_static type=Bpsim.QA.AnvilBenchQA method=Run args=["bodies=2000;steps=240"]
	///   invoke_static type=Bpsim.QA.AnvilBenchQA method=Status
	/// </para>
	/// spec（分号分隔的 key=value）：
	///   bodies=   动态盒数量，默认 2000
	///   steps=    每个后端采样的物理步数，默认 240
	///   warmup=   每个后端切换后的预热步数，默认 60
	///   timestep= 固定步长，默认 0.02
	///   backends= 逗号分隔，默认 UnityPhysics,HavokPhysics,AnvilPhysics
	///   stack=    1 = 盒堆叠成塔（接触多），0 = 盒排成扁平面阵列（接触少），默认 1
	/// 结果同时写到 Logs/anvilbench.log。
	/// </remarks>
	public class AnvilBenchQA : MonoBehaviour
	{
		private const string LogFile = "Logs/anvilbench.log";

		private static AnvilBenchQA s_instance;

		private static readonly StringBuilder s_report = new StringBuilder();

		private static string s_status = "idle";

		// 探针系统把每个物理 step 的耗时塞进这里；空表示不在采样。
		private static List<double> s_samples;

		private static long s_stepStart;

		public static string Status()
		{
			return s_status + "\n" + s_report;
		}

		public static string Run(string spec)
		{
			AnvilBenchQA probe = Ensure();
			s_report.Length = 0;
			s_status = "running";
			probe.StartCoroutine(probe.RunRoutine(spec));
			return "started: " + spec;
		}

		public static string Reset()
		{
			s_report.Length = 0;
			s_status = "idle";
			return "reset";
		}

		// ---- 探针系统的回调 ----

		internal static void MarkStepStart()
		{
			if (s_samples == null)
			{
				return;
			}
			s_stepStart = System.Diagnostics.Stopwatch.GetTimestamp();
		}

		internal static void MarkStepEnd()
		{
			if (s_samples == null)
			{
				return;
			}
			long now = System.Diagnostics.Stopwatch.GetTimestamp();
			double ms = (now - s_stepStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
			s_samples.Add(ms);
		}

		private static AnvilBenchQA Ensure()
		{
			if (s_instance != null)
			{
				return s_instance;
			}
			GameObject gameObject = new GameObject("[AnvilBenchQA]");
			gameObject.hideFlags = HideFlags.DontSave;
			UnityEngine.Object.DontDestroyOnLoad(gameObject);
			s_instance = gameObject.AddComponent<AnvilBenchQA>();
			return s_instance;
		}

		private static void Line(string text)
		{
			s_report.AppendLine(text);
			Debug.Log("[AnvilBench] " + text);
		}

		private IEnumerator RunRoutine(string spec)
		{
			int bodies = ParseKey(spec, "bodies", 2000);
			int steps = ParseKey(spec, "steps", 240);
			int warmup = ParseKey(spec, "warmup", 60);
			bool stack = ParseKey(spec, "stack", 1) != 0;
			float timeStep = ParseFloat(spec, "timestep", 0.02f);
			string[] backends = ParseList(spec, "backends", "UnityPhysics,HavokPhysics,AnvilPhysics");

			World world = World.DefaultGameObjectInjectionWorld;
			if (world == null || !world.IsCreated)
			{
				s_status = "error: no default world";
				yield break;
			}
			EntityManager entityManager = world.EntityManager;

			Line("=== Anvil bench: bodies=" + bodies + " steps=" + steps + " warmup=" + warmup
				+ " timestep=" + timeStep.ToString("F4", CultureInfo.InvariantCulture)
				+ " stack=" + (stack ? 1 : 0) + " ===");
			Line("box3d capabilities: " + DescribeCapabilities());
			Line("burst/backend note: Editor 下本工程的 [BurstCompile] 全部以 managed 回退执行，"
				+ "Unity.Physics 与 Havok 都不是 release 原生性能；Anvil 走 box3d 原生 DLL，不受此限制。");

			float3 gravity = new float3(0f, -9.81f, 0f);
			FixedStepSimulationSystemGroup fixedStep = world.GetExistingSystemManaged<FixedStepSimulationSystemGroup>();
			float previousTimestep = -1f;
			if (fixedStep != null)
			{
				previousTimestep = fixedStep.Timestep;
				fixedStep.Timestep = timeStep;
			}

			Entity stepEntity = Entity.Null;
			SimulationType previousType = SimulationType.UnityPhysics;
			using (EntityQuery query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PhysicsStep>()))
			{
				if (query.CalculateEntityCount() > 0)
				{
					using (NativeArray<Entity> existing = query.ToEntityArray(Unity.Collections.Allocator.Temp))
					{
						stepEntity = existing[0];
					}
					previousType = entityManager.GetComponentData<PhysicsStep>(stepEntity).SimulationType;
				}
			}
			bool createdStep = false;
			if (stepEntity == Entity.Null)
			{
				stepEntity = entityManager.CreateEntity();
				entityManager.AddComponentData(stepEntity, PhysicsStep.Default);
				createdStep = true;
			}

			NativeList<Entity> spawned = new NativeList<Entity>(bodies + 8, Unity.Collections.Allocator.Persistent);
			try
			{
				CreateScene(entityManager, spawned, bodies, stack, ref gravity);

				for (int i = 0; i < backends.Length; i++)
				{
					string name = backends[i].Trim();
					if (name.Length == 0)
					{
						continue;
					}
					if (!TryParseSimulationType(name, out SimulationType type))
					{
						Line("[skip] unknown backend '" + name + "'");
						continue;
					}
					yield return StartCoroutine(RunBackend(world, entityManager, stepEntity, type, name,
						timeStep, warmup, steps, gravity));
				}
			}
			finally
			{
				s_samples = null;
				if (spawned.IsCreated)
				{
					for (int i = 0; i < spawned.Length; i++)
					{
						if (entityManager.Exists(spawned[i]))
						{
							entityManager.DestroyEntity(spawned[i]);
						}
					}
					spawned.Dispose();
				}
				if (createdStep && entityManager.Exists(stepEntity))
				{
					entityManager.DestroyEntity(stepEntity);
				}
				else if (!createdStep && entityManager.Exists(stepEntity))
				{
					PhysicsStep restore = entityManager.GetComponentData<PhysicsStep>(stepEntity);
					restore.SimulationType = previousType;
					entityManager.SetComponentData(stepEntity, restore);
				}
				if (fixedStep != null && previousTimestep > 0f)
				{
					fixedStep.Timestep = previousTimestep;
				}
			}

			Line("=== done ===");
			WriteLog();
			s_status = "done";
		}

		private IEnumerator RunBackend(World world, EntityManager entityManager, Entity stepEntity, SimulationType type,
			string name, float timeStep, int warmup, int steps, float3 gravity)
		{
			PhysicsStep step = entityManager.GetComponentData<PhysicsStep>(stepEntity);
			step.SimulationType = type;
			step.Gravity = gravity;
			entityManager.SetComponentData(stepEntity, step);
			// 让 picker / AnvilStepSystem 有机会完成链切换。
			yield return null;
			yield return null;

			s_samples = null;
			int warmupSeen = 0;
			float deadline = Time.realtimeSinceStartup + 30f;
			while (warmupSeen < warmup && Time.realtimeSinceStartup < deadline)
			{
				yield return null;
				warmupSeen++;
			}

			long mem0 = GC.GetTotalMemory(true);
			int gc0 = GC.CollectionCount(0);
			double wall0 = Time.realtimeSinceStartup;

			List<double> samples = new List<double>(steps + 16);
			s_stepStart = System.Diagnostics.Stopwatch.GetTimestamp();
			s_samples = samples;
			int guard = 0;
			while (samples.Count < steps && guard < steps * 40 + 4000)
			{
				yield return null;
				guard++;
				if (Time.realtimeSinceStartup - wall0 > 120f)
				{
					break;
				}
			}
			s_samples = null;

			double wall = Time.realtimeSinceStartup - wall0;
			long mem1 = GC.GetTotalMemory(false);
			int gcCount = GC.CollectionCount(0) - gc0;

			string line = Summarize(name, samples, wall, gcCount, mem1 - mem0);
			Line(line);

			if (type == SimulationType.AnvilPhysics && AnvilStepSystem.LastProfile.step >= 0f)
			{
				b3Profile p = AnvilStepSystem.LastProfile;
				Line("  anvil profile(ms): pairs=" + Fmt(p.pairs) + " collide=" + Fmt(p.collide)
					+ " solve=" + Fmt(p.solve) + " solverSetup=" + Fmt(p.solverSetup)
					+ " constraints=" + Fmt(p.constraints) + " integratePos=" + Fmt(p.integratePositions)
					+ " splitIslands=" + Fmt(p.splitIslands) + " transforms=" + Fmt(p.transforms));
				AnvilWorld anvil = AnvilStepSystem.Anvil;
				if (anvil != null && anvil.IsCreated)
				{
					b3Counters c = anvil.GetCounters();
					Line("  anvil counters: bodies=" + c.bodyCount + " shapes=" + c.shapeCount
						+ " contacts=" + c.contactCount + " joints=" + c.jointCount
						+ " islands=" + c.islandCount + " bytes=" + c.byteCount);
				}
			}

			using (EntityQuery query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PhysicsWorldSingleton>()))
			{
				if (query.CalculateEntityCount() > 0)
				{
					PhysicsWorld physicsWorld = query.GetSingleton<PhysicsWorldSingleton>().PhysicsWorld;
					Line("  world: dynamic=" + physicsWorld.NumDynamicBodies + " static=" + physicsWorld.NumStaticBodies
						+ " joints=" + physicsWorld.NumJoints);
				}
			}
		}

		private static void CreateScene(EntityManager entityManager, NativeList<Entity> spawned, int bodies, bool stack, ref float3 gravity)
		{
			// 地面：静态 Box，顶面在 y = 0。
			Entity ground = entityManager.CreateEntity();
			float3 groundCenter = new float3(0f, -0.5f, 0f);
			entityManager.AddComponentData(ground, LocalTransform.FromPosition(groundCenter));
			entityManager.AddComponentData(ground, new LocalToWorld { Value = float4x4.Translate(groundCenter) });
			entityManager.AddComponentData(ground, new PhysicsCollider
			{
				Value = PartPhysics.CreateBox(float3.zero, new float3(200f, 1f, 200f))
			});
			entityManager.AddSharedComponent(ground, new PhysicsWorldIndex(0));
			spawned.Add(ground);

			float3 half = new float3(0.25f, 0.25f, 0.25f);
			int columns = math.max(1, (int)math.sqrt(bodies));
			int rows = (bodies + columns - 1) / columns;
			for (int i = 0; i < bodies; i++)
			{
				float3 position;
				if (stack)
				{
					int layer = i / (columns * columns);
					int rest = i % (columns * columns);
					int cx = rest % columns;
					int cz = rest / columns;
					position = new float3(
						(cx - columns * 0.5f) * 0.6f,
						0.3f + layer * 0.55f,
						(cz - columns * 0.5f) * 0.6f);
				}
				else
				{
					int cx = i % columns;
					int cz = i / columns;
					position = new float3(
						(cx - columns * 0.5f) * 0.6f,
						0.3f,
						(cz - columns * 0.5f) * 0.6f);
				}

				Entity entity = entityManager.CreateEntity();
				entityManager.AddComponentData(entity, LocalTransform.FromPositionRotation(position, quaternion.identity));
				entityManager.AddComponentData(entity, new LocalToWorld
				{
					Value = float4x4.TRS(position, quaternion.identity, new float3(1f))
				});
				BlobAssetReference<Collider> box = PartPhysics.CreateBox(float3.zero, half * 2f);
				entityManager.AddComponentData(entity, new PhysicsCollider { Value = box });
				entityManager.AddComponentData(entity, PhysicsMass.CreateDynamic(box.Value.MassProperties, 1f));
				entityManager.AddComponentData(entity, new PhysicsVelocity());
				entityManager.AddComponentData(entity, new PhysicsDamping { Linear = 0.05f, Angular = 0.05f });
				entityManager.AddComponentData(entity, new PhysicsGravityFactor { Value = 1f });
				entityManager.AddSharedComponent(entity, new PhysicsWorldIndex(0));
				spawned.Add(entity);
			}
			_ = rows;
		}

		private static string Summarize(string name, List<double> samples, double wall, int gcCount, long memDelta)
		{
			if (samples == null || samples.Count == 0)
			{
				return name + ": no samples (wall=" + wall.ToString("F2", CultureInfo.InvariantCulture) + "s)";
			}
			samples.Sort();
			double min = samples[0];
			double max = samples[samples.Count - 1];
			double median = samples[samples.Count / 2];
			double p95 = samples[math.min(samples.Count - 1, (int)(samples.Count * 0.95))];
			double p99 = samples[math.min(samples.Count - 1, (int)(samples.Count * 0.99))];
			double sum = 0.0;
			for (int i = 0; i < samples.Count; i++)
			{
				sum += samples[i];
			}
			double mean = sum / samples.Count;
			return name + ": steps=" + samples.Count
				+ " median=" + Fmt(median) + "ms mean=" + Fmt(mean) + "ms p95=" + Fmt(p95)
				+ "ms p99=" + Fmt(p99) + "ms min=" + Fmt(min) + "ms max=" + Fmt(max)
				+ "ms wall=" + wall.ToString("F2", CultureInfo.InvariantCulture) + "s"
				+ " gc=" + gcCount + " gcBytes=" + memDelta;
		}

		private static string Fmt(double value)
		{
			return value.ToString("F3", CultureInfo.InvariantCulture);
		}

		private static string DescribeCapabilities()
		{
			try
			{
				int caps = AnvilNative.anvil_Capabilities();
				return "doublePrecision=" + ((caps & AnvilNative.CapabilityDoublePrecision) != 0 ? 1 : 0)
					+ " avx2=" + ((caps & AnvilNative.CapabilityAvx2) != 0 ? 1 : 0);
			}
			catch (Exception e)
			{
				return "unavailable (" + e.GetType().Name + ")";
			}
		}

		private static bool TryParseSimulationType(string name, out SimulationType type)
		{
			return Enum.TryParse(name, ignoreCase: true, out type);
		}

		private static void WriteLog()
		{
			try
			{
				string path = Path.Combine(Directory.GetCurrentDirectory(), LogFile);
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				File.WriteAllText(path, s_report.ToString());
			}
			catch (Exception e)
			{
				Debug.LogWarning("[AnvilBench] log write failed: " + e.Message);
			}
		}

		private static int ParseKey(string spec, string key, int fallback)
		{
			if (string.IsNullOrEmpty(spec))
			{
				return fallback;
			}
			string[] parts = spec.Split(';');
			for (int i = 0; i < parts.Length; i++)
			{
				string part = parts[i].Trim();
				int eq = part.IndexOf('=');
				if (eq <= 0)
				{
					continue;
				}
				if (string.Equals(part.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase)
					&& int.TryParse(part.Substring(eq + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
				{
					return value;
				}
			}
			return fallback;
		}

		private static float ParseFloat(string spec, string key, float fallback)
		{
			if (string.IsNullOrEmpty(spec))
			{
				return fallback;
			}
			string[] parts = spec.Split(';');
			for (int i = 0; i < parts.Length; i++)
			{
				string part = parts[i].Trim();
				int eq = part.IndexOf('=');
				if (eq <= 0)
				{
					continue;
				}
				if (string.Equals(part.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase)
					&& float.TryParse(part.Substring(eq + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
				{
					return value;
				}
			}
			return fallback;
		}

		private static string[] ParseList(string spec, string key, string fallback)
		{
			if (!string.IsNullOrEmpty(spec))
			{
				string[] parts = spec.Split(';');
				for (int i = 0; i < parts.Length; i++)
				{
					string part = parts[i].Trim();
					int eq = part.IndexOf('=');
					if (eq <= 0)
					{
						continue;
					}
					if (string.Equals(part.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
					{
						return part.Substring(eq + 1).Split(',');
					}
				}
			}
			return fallback.Split(',');
		}
	}

	/// <summary> 每个物理步开始时打点。位于 <see cref="PhysicsSystemGroup" /> 最前。 </summary>
	[UpdateInGroup(typeof(PhysicsSystemGroup), OrderFirst = true)]
	public partial class AnvilBenchStartSystem : SystemBase
	{
		protected override void OnUpdate()
		{
			AnvilBenchQA.MarkStepStart();
		}
	}

	/// <summary> 每个物理步结束时打点。位于 <see cref="PhysicsSystemGroup" /> 最后。 </summary>
	[UpdateInGroup(typeof(PhysicsSystemGroup), OrderLast = true)]
	public partial class AnvilBenchEndSystem : SystemBase
	{
		protected override void OnUpdate()
		{
			AnvilBenchQA.MarkStepEnd();
		}
	}
}
#endif
