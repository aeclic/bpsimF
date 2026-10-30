using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

namespace Bpsim.Parts.Simulation
{
	/// <summary>
	/// BPSS <c>FixedPumpkinManager</c> 的 ECS 版：把「有已固定南瓜」的整个焊接组件
	/// 冻成静态体，南瓜关掉时还原。
	/// </summary>
	/// <remarks>
	/// <para>
	/// 原版每次 <c>ConnectedComponentsChanged</c> 或点南瓜时置 <c>NeedsUpdate</c>，
	/// 然后遍历全部零件、按 <c>StrictConnectedComponent</c> 决定哪些刚体要 <c>FreezeAll</c>。
	/// 这里同样只在脏标记置位时重算（点南瓜属于玩家操作，频率极低），
	/// 平时这个系统一帧只做一次 <c>if (!dirty) return;</c>。
	/// </para>
	/// <para>
	/// 冻结方式与原版的差异：Unity.Physics 里没有 <c>RigidbodyConstraints</c>，
	/// 静态体的定义是 <c>PhysicsMass.InverseMass == 0 &amp;&amp; InverseInertia == 0</c>，
	/// 所以直接把这两个值清零并清速度即可；还原时从零件自己身上的
	/// <see cref="PumpkinFrozen"/> 读回原始值。原版还会把层切到 <c>Ground</c>、
	/// 让零件 <c>Sleep()</c>，这两件事在 bpsim 里没有对应语义（碰撞过滤是位掩码，
	/// 没有 Ground 层概念；速度清零后求解器自然不会再推它）。
	/// </para>
	/// </remarks>
	[UpdateInGroup(typeof(PartSimulationSystemGroup))]
	internal partial class FixedPumpkinSystem : SystemBase
	{
		/// <summary>拓扑或南瓜状态变化时由 behavior / 场景切换置位。</summary>
		private static int s_dirty = 1;

		private NativeParallelHashSet<int> m_components;

		private NativeParallelHashMap<int, Entity> m_sources;

		private NativeList<Entity> m_scratch;

		public static void MarkDirty()
		{
			s_dirty = 1;
		}

		protected override void OnCreate()
		{
			m_components = new NativeParallelHashSet<int>(64, Allocator.Persistent);
			m_sources = new NativeParallelHashMap<int, Entity>(64, Allocator.Persistent);
			m_scratch = new NativeList<Entity>(256, Allocator.Persistent);
		}

		protected override void OnDestroy()
		{
			m_components.Dispose();
			m_sources.Dispose();
			m_scratch.Dispose();
		}

		protected override void OnUpdate()
		{
			PartManager manager = PartManager.Instance;
			if (manager == null || !manager.IsSimulating || manager.PartSimulator == null)
			{
				return;
			}
			if (s_dirty == 0)
			{
				return;
			}
			s_dirty = 0;
			EntityManager em = EntityManager;
			ref PartSimulatorUnmanaged simulator = ref manager.PartSimulator.Unmanaged;
			NativeList<Entity> parts = simulator.Parts;
			m_components.Clear();
			m_sources.Clear();
			m_scratch.Clear();
			// 第一遍：记下「有已固定南瓜」的焊接组件号，以及是哪个南瓜负责的。
			for (int i = 0; i < parts.Length; i++)
			{
				Entity part = parts[i];
				if (!em.Exists(part) || !em.HasComponent<PumpkinPart>(part))
				{
					continue;
				}
				if (em.GetComponentData<PumpkinPart>(part).Enabled == 0)
				{
					continue;
				}
				if (!em.HasComponent<PartConnectedComponent>(part))
				{
					continue;
				}
				int component = em.GetComponentData<PartConnectedComponent>(part).Index;
				m_components.Add(component);
				m_sources.TryAdd(component, part);
			}
			// 第二遍：按组件号冻结 / 解冻，并回写每个南瓜的 Applied 标记。
			int frozen = 0;
			int restored = 0;
			for (int j = 0; j < parts.Length; j++)
			{
				Entity part2 = parts[j];
				if (!em.Exists(part2))
				{
					continue;
				}
				int component2 = em.HasComponent<PartConnectedComponent>(part2) ? em.GetComponentData<PartConnectedComponent>(part2).Index : -1;
				bool wantFrozen = component2 >= 0 && m_components.Contains(component2);
				bool isFrozen = em.HasComponent<PumpkinFrozen>(part2);
				if (wantFrozen && !isFrozen)
				{
					Entity source = m_sources.TryGetValue(component2, out Entity entity) ? entity : Entity.Null;
					if (Freeze(em, part2, source))
					{
						frozen++;
					}
				}
				else if (!wantFrozen && isFrozen)
				{
					Unfreeze(em, part2);
					restored++;
				}
				if (em.HasComponent<PumpkinPart>(part2))
				{
					PumpkinPart pumpkin = em.GetComponentData<PumpkinPart>(part2);
					byte applied = (byte)((pumpkin.Enabled != 0 && wantFrozen) ? 1 : 0);
					if (pumpkin.Applied != applied)
					{
						pumpkin.Applied = applied;
						em.SetComponentData(part2, pumpkin);
					}
				}
			}
			UnityEngine.Debug.Log("[FixedPumpkin] components=" + m_components.Count() + " frozen=" + frozen + " restored=" + restored + " parts=" + parts.Length);
		}

		private static bool Freeze(EntityManager em, Entity part, Entity source)
		{
			if (!em.HasComponent<PhysicsMass>(part))
			{
				return false;
			}
			PhysicsMass mass = em.GetComponentData<PhysicsMass>(part);
			// 已经是静态体（逆质量为 0）就不用再碰它，但照样打标记，
			// 否则解冻时会把它误当成「动过的零件」还原。
			em.AddComponentData(part, new PumpkinFrozen
			{
				InverseMass = mass.InverseMass,
				InverseInertia = mass.InverseInertia,
				Source = source
			});
			if (mass.InverseMass <= 0f)
			{
				return false;
			}
			mass.InverseMass = 0f;
			mass.InverseInertia = float3.zero;
			em.SetComponentData(part, mass);
			if (em.HasComponent<PhysicsVelocity>(part))
			{
				em.SetComponentData(part, default(PhysicsVelocity));
			}
			return true;
		}

		private static void Unfreeze(EntityManager em, Entity part)
		{
			PumpkinFrozen frozen = em.GetComponentData<PumpkinFrozen>(part);
			em.RemoveComponent<PumpkinFrozen>(part);
			if (!em.HasComponent<PhysicsMass>(part) || frozen.InverseMass <= 0f)
			{
				return;
			}
			PhysicsMass mass = em.GetComponentData<PhysicsMass>(part);
			mass.InverseMass = frozen.InverseMass;
			mass.InverseInertia = frozen.InverseInertia;
			em.SetComponentData(part, mass);
		}
	}
}
