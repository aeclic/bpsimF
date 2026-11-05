// Anvil Physics (box3d) — 接触事件单例 + 每步填充系统。
//
// 用法（Anvil 后端下替代 ICollisionEventsJob）：
//   if (SystemAPI.TryGetSingleton<AnvilContactsSingleton>(out var events))
//   {
//       for (int i = 0; i < events.Count; i++)
//       {
//           AnvilContactEvent e = events.Events[i];
//           ...
//       }
//   }
//
// 与 Unity.Physics 的差异（必须知道）：
//   * 没有 ColliderKey：box3d 的 compound 被拆成多个 shape 挂在同一 body 上，不产生子形状 key。
//   * Impulse 是该接触点本步的法向冲量总量（Box3D 的 normalImpulse），不是 EstimatedImpulse 的估算值。
//   * 只含「真的产生了接触」的 pair；传感器（isSensor）另有 sensor events，暂未接线。
using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using Unity.Physics.Systems;

namespace Bpsim.Physics.Anvil
{
	/// <summary>本步的 box3d 接触列表。Nested 容器由 AnvilContactEventSystem 负责分配/释放。</summary>
	public struct AnvilContactsSingleton : IComponentData
	{
		public NativeList<AnvilContactEvent> Events;
		/// <summary>本步收集到的条数（Events 的子区间长度）。</summary>
		public int Count;
		/// <summary>本步 box3d 的 world 是否处于激活（Anvil 后端）状态。</summary>
		public bool Active;
	}

	/// <summary>
	/// 在 AnvilStepSystem 之后、下游消费者（PartSimulationSystemGroup 等）之前，
	/// 把 box3d 的接触读成 AnvilContactsSingleton。
	/// </summary>
	[UpdateInGroup(typeof(PhysicsSystemGroup))]
	[UpdateAfter(typeof(AnvilStepSystem))]
	[UpdateBefore(typeof(ExportPhysicsWorld))]
	public partial class AnvilContactEventSystem : SystemBase
	{
		/// <summary>单例实体；确保整个 session 只有一个。</summary>
		Entity m_SingletonEntity;

		/// <summary>每步最多收集多少条接触（超出即丢弃，避免无限增长）。</summary>
		public int MaxContacts = 65536;

		/// <summary>
		/// 全局开关：关掉时本系统不采集（下游 TNT/电路/巨型结构在 Anvil 模式下就不会收到事件）。
		/// 默认开启；基准里可用 contacts=0 关掉以隔离纯物理开销。
		/// </summary>
		public static bool Collecting = true;

		protected override void OnCreate()
		{
			EnsureSingleton();
		}

		protected override void OnDestroy()
		{
			if (m_SingletonEntity != Entity.Null && EntityManager.Exists(m_SingletonEntity))
			{
				if (EntityManager.HasComponent<AnvilContactsSingleton>(m_SingletonEntity))
				{
					AnvilContactsSingleton data = EntityManager.GetComponentData<AnvilContactsSingleton>(m_SingletonEntity);
					if (data.Events.IsCreated) data.Events.Dispose();
				}
				EntityManager.DestroyEntity(m_SingletonEntity);
			}
			m_SingletonEntity = Entity.Null;
		}

		void EnsureSingleton()
		{
			if (m_SingletonEntity != Entity.Null && EntityManager.Exists(m_SingletonEntity)) return;

			using (EntityQuery query = EntityManager.CreateEntityQuery(ComponentType.ReadWrite<AnvilContactsSingleton>()))
			{
				if (query.CalculateEntityCount() > 0)
				{
					m_SingletonEntity = query.GetSingletonEntity();
					return;
				}
			}

			m_SingletonEntity = EntityManager.CreateEntity();
			EntityManager.SetName(m_SingletonEntity, "[AnvilContactsSingleton]");
			EntityManager.AddComponentData(m_SingletonEntity, new AnvilContactsSingleton
			{
				Events = new NativeList<AnvilContactEvent>(1024, Allocator.Persistent),
				Count = 0,
				Active = false,
			});
		}

		protected override void OnUpdate()
		{
			EnsureSingleton();

			AnvilContactsSingleton data = EntityManager.GetComponentData<AnvilContactsSingleton>(m_SingletonEntity);
			if (!data.Events.IsCreated)
			{
				data.Events = new NativeList<AnvilContactEvent>(1024, Allocator.Persistent);
			}

			data.Count = 0;
			data.Active = AnvilStepSystem.Active;

			AnvilWorld anvil = AnvilStepSystem.Anvil;
			if (Collecting && data.Active && anvil != null && anvil.IsCreated)
			{
				using (EntityQuery query = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PhysicsWorldSingleton>()))
				{
					if (query.CalculateEntityCount() > 0)
					{
						Unity.Physics.PhysicsWorld world = query.GetSingleton<PhysicsWorldSingleton>().PhysicsWorld;
						int capacity = System.Math.Min(MaxContacts, System.Math.Max(64, world.NumDynamicBodies * 8));
						int collected = anvil.CollectContactEvents(data.Events, capacity);
						int count = System.Math.Min(collected, data.Events.Length);

						// body 索引 → Entity（动态在前、静态在后，与 PhysicsWorld 的布局一致）
						for (int i = 0; i < count; i++)
						{
							AnvilContactEvent e = data.Events[i];
							e.EntityA = Resolve(world, anvil, e.EntityA);
							e.EntityB = Resolve(world, anvil, e.EntityB);
							data.Events[i] = e;
						}
						data.Count = count;
					}
				}
			}

			EntityManager.SetComponentData(m_SingletonEntity, data);
		}

		/// <summary>body 索引（dynamic 前、static 后）→ Entity。越界返回 Null。</summary>
		static Entity Resolve(Unity.Physics.PhysicsWorld world, AnvilWorld anvil, Entity placeholder)
		{
			// CollectContactEvents 把 b3BodyId.index1 暂存在 EntityA/EntityB 的 Index 字段里
			// （1-based；0 表示无效）。这里换算成 PhysicsWorld 的 body 索引再取 Entity。
			int index = anvil.BodyIndexFromNativeIndex(placeholder.Index);
			if (index < 0) return Entity.Null;
			int numDynamic = world.NumDynamicBodies;
			if (index < numDynamic)
			{
				Unity.Physics.RigidBody body = world.DynamicBodies[index];
				return body.Entity;
			}
			int staticIndex = index - numDynamic;
			if (staticIndex >= 0 && staticIndex < world.NumStaticBodies)
			{
				Unity.Physics.RigidBody body = world.StaticBodies[staticIndex];
				return body.Entity;
			}
			return Entity.Null;
		}
	}
}
