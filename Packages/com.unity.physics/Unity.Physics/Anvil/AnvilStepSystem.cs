// Anvil Physics (box3d) — ECS step system.
//
// 接管方式与 Havok 同构：不改 Unity.Physics 包内那四个 public 系统，只把「推进 MotionDatas」
// 这件事换成 box3d。系统链顺序：
//   PhysicsSystemGroup
//     ├─ PhysicsInitializeGroup   ← BuildPhysicsWorld 照常构建 PhysicsWorld（collider/transform 来源）
//     ├─ PhysicsSimulationGroup   ← Havok/Unity.Physics 的 picker 在这里按 SimulationType 开关它们自己的链
//     ├─ AnvilStepSystem (本文件) ← Anvil 模式下自己 step，并把结果写回 PhysicsWorld
//     └─ ExportPhysicsWorld       ← 照常把 MotionDatas → LocalTransform / PhysicsVelocity
//
// 因为 Havok 的 PhysicsSimulationPickerSystem 对未知的 SimulationType 走 default 分支（只关闭旧链、
// 不启用任何链），切到 AnvilPhysics 时它会把 Unity.Physics / Havok 的四系统链关掉，正好让位给我们。
using System.Diagnostics;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;

namespace Bpsim.Physics.Anvil
{
	/// <summary>
	/// 挂 Anvil 后端的单步驱动系统。仅在 <c>PhysicsStep.SimulationType == AnvilPhysics</c> 时执行。
	/// </summary>
	[UpdateInGroup(typeof(PhysicsSystemGroup))]
	[UpdateAfter(typeof(PhysicsSimulationGroup))]
	[UpdateBefore(typeof(ExportPhysicsWorld))]
	public partial class AnvilStepSystem : SystemBase
	{
		/// <summary>全局唯一世界实例，供 raycast / 状态查询直接取用。</summary>
		public static AnvilWorld Anvil { get; private set; }

		/// <summary>最近一步的原生 profile（单位毫秒）。</summary>
		public static b3Profile LastProfile { get; private set; }

		/// <summary>本帧是否由 Anvil 驱动。</summary>
		public static bool Active { get; private set; }

		/// <summary>累计步数，基准/自检用。</summary>
		public static int StepCount { get; private set; }

		/// <summary>最近一步的分段墙钟耗时（毫秒）：ECS → box3d 同步。</summary>
		public static double LastSyncToMs { get; private set; }

		/// <summary>最近一步的分段墙钟耗时（毫秒）：box3d 原生 step。</summary>
		public static double LastStepMs { get; private set; }

		/// <summary>最近一步的分段墙钟耗时（毫秒）：box3d → ECS 同步。</summary>
		public static double LastSyncFromMs { get; private set; }

		/// <summary>最近一步的分段墙钟耗时（毫秒）：Unity.Physics broadphase 树刷新。</summary>
		public static double LastTreeMs { get; private set; }

		private static double Elapsed(long a, long b)
		{
			return (b - a) * 1000.0 / Stopwatch.Frequency;
		}

		/// <summary>
		/// 是否每步刷新 Unity.Physics 的 dynamic broadphase 树。
		/// </summary>
		/// <remarks>
		/// box3d 自己的 broadphase 已经是最新的，这个刷新只为让仍走
		/// <c>PhysicsWorldSingleton.CollisionWorld.CastRay</c> 的下游代码（AutoGun / Wheel / Guidance 等）
		/// 看到正确结果。它在 2k 刚体上要 7~8 ms（Unity.Physics 的树是毫秒级重建），
		/// 是 Anvil 后端在 bpsim 里的主要固定开销，所以做成可关：
		/// 关掉后射线查询会读到上一步的位姿（对打点类玩法足够），省下这段时间。
		/// </remarks>
		public static bool SyncBroadphaseTree { get; set; } = true;

		/// <summary>
		/// box3d 求解线程数（0 = 用 <see cref="AnvilWorld.WorkerCount"/> 的默认值 1）。
		/// 必须在 <c>AnvilWorld</c> 创建前设置才生效；原生基准显示 4 worker 相对 1 worker 有约 2.9× 加速。
		/// </summary>
		public static int WorkerCount { get; set; }

		/// <summary>
		/// box3d 是否允许刚体休眠（null = 用 <see cref="AnvilWorld.EnableSleep"/> 的默认值 true）。
		/// 关掉后堆叠场景全程活跃，测出来的是真实求解成本而不是「几乎全睡」的空转。
		/// </summary>
		public static bool? EnableSleep { get; set; }

		protected override void OnCreate()
		{
			Active = false;
			StepCount = 0;
		}

		protected override void OnDestroy()
		{
			Anvil?.Dispose();
			Anvil = null;
			Active = false;
			LastProfile = default;
		}

		protected override void OnUpdate()
		{
			SimulationType type = SimulationType.UnityPhysics;
			float3 gravity = new float3(0f, -9.81f, 0f);

			using (EntityQuery query = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PhysicsStep>()))
			{
				if (query.TryGetSingleton(out PhysicsStep step))
				{
					type = step.SimulationType;
					gravity = step.Gravity;
				}
			}

			Active = type == SimulationType.AnvilPhysics;
			if (!Active)
			{
				LastProfile = default;
				return;
			}

			if (Anvil == null || !Anvil.IsCreated)
			{
				Anvil = new AnvilWorld();
				if (WorkerCount > 0) Anvil.WorkerCount = WorkerCount;
				if (EnableSleep.HasValue) Anvil.EnableSleep = EnableSleep.Value;
				Anvil.Create();
			}
			else if (WorkerCount > 0 && Anvil.WorkerCount != WorkerCount)
			{
				Anvil.SetWorkerCount(WorkerCount);
			}

			Anvil.SetGravity(gravity);

			PhysicsWorld physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().PhysicsWorld;
			if (physicsWorld.NumDynamicBodies == 0 && physicsWorld.NumStaticBodies == 0)
			{
				LastProfile = default;
				return;
			}

			float timeStep = SystemAPI.Time.DeltaTime;
			if (timeStep <= 0f) timeStep = 0.02f;

			long t0 = Stopwatch.GetTimestamp();
			Anvil.SyncToNative(ref physicsWorld);
			Anvil.SyncJointsToNative(ref physicsWorld, timeStep);
			long t1 = Stopwatch.GetTimestamp();
			LastProfile = Anvil.Step(timeStep);
			long t2 = Stopwatch.GetTimestamp();
			Anvil.SyncFromNative(ref physicsWorld);
			long t3 = Stopwatch.GetTimestamp();

			// 让 Broadphase 跟着新位姿走，否则 CastRay / Overlap 读到的还是上一帧的树。
			if (SyncBroadphaseTree)
			{
				physicsWorld.CollisionWorld.UpdateDynamicTree(ref physicsWorld, timeStep, gravity);
			}
			long t4 = Stopwatch.GetTimestamp();

			LastSyncToMs = Elapsed(t0, t1);
			LastStepMs = Elapsed(t1, t2);
			LastSyncFromMs = Elapsed(t2, t3);
			LastTreeMs = Elapsed(t3, t4);
			StepCount++;
		}

		/// <summary>用 box3d 自己的 broadphase 做射线；未挂 Anvil 后端时返回 false。</summary>
		public static bool CastRay(float3 origin, float3 translation, float maxFraction,
			out float fraction, out float3 point, out float3 normal)
		{
			fraction = 1f;
			point = origin;
			normal = new float3(0f, 1f, 0f);
			if (Anvil == null || !Anvil.IsCreated) return false;
			return Anvil.CastRay(origin, translation, maxFraction, out fraction, out point, out normal);
		}

		/// <summary>
		/// 读取本步 box3d 报告的关节断裂事件（阈值超限），映射回 Joint Entity。
		/// 供 PartSimulationSystem 等下游消费者在 Anvil 后端下替代 IHavokImpulseEventsJobExtensions。
		/// </summary>
		public static int CollectJointEvents(Unity.Collections.NativeList<Entity> output)
		{
			output.Clear();
			if (Anvil == null || !Anvil.IsCreated) return 0;
			return Anvil.CollectJointEvents(output);
		}
	}
}
