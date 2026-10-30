using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;
using Bpsim.Parts;

namespace Bpsim.Parts.Simulation
{
	/// <summary>
	/// 机械闸门门板的半透明表现，对应 BPSS 901-17.0 <c>MechanicalGate.FixedUpdate()</c> 里的
	/// <c>m_gateRenderer.material.color = (r, g, b, GateOpacity)</c>。
	/// </summary>
	/// <remarks>
	/// <para>
	/// 为什么单独一个系统、且放在表现组：<see cref="Unity.Rendering.MaterialColor" /> 每帧都会被
	/// <c>ColoredFrameSystem</c>（SimulationSystemGroup）按「未染色的部件一律刷成 (1,1,1,1)」重写，
	/// 在仿真组里写 alpha 会被下一帧立刻盖掉。表现组晚于仿真组、又早于
	/// <c>SceneRenderSystem</c> 把颜色拷进 GPU 缓冲，所以在这里写才是有效且唯一的时机。
	/// </para>
	/// <para>
	/// 只改 alpha（w），rgb 留给 <c>ColoredFrameSystem</c> 的染色逻辑，互不干扰。
	/// </para>
	/// </remarks>
	[UpdateInGroup(typeof(PresentationSystemGroup))]
	[UpdateBefore(typeof(SceneRenderSystem))]
	internal partial class MechanicalGateVisualSystem : SystemBase
	{
		private EntityQuery m_gateQuery;

		private ComponentLookup<GateVisual> m_visualLookup;

		protected override void OnCreate()
		{
			EntityQueryBuilder builder = new EntityQueryBuilder(Allocator.Temp);
			builder = builder.WithAll<MechanicalGate>();
			builder = builder.WithAll<PartTag>();
			builder = builder.WithAll<LinkedEntityGroup>();
			m_gateQuery = builder.Build(ref base.CheckedStateRef);
			m_visualLookup = base.CheckedStateRef.GetComponentLookup<GateVisual>(isReadOnly: true);
		}

		protected override void OnUpdate()
		{
			m_visualLookup.Update(ref base.CheckedStateRef);
			EntityManager entityManager = base.EntityManager;
			using NativeArray<Entity> gates = m_gateQuery.ToEntityArray(Allocator.Temp);
			for (int i = 0; i < gates.Length; i++)
			{
				Entity root = gates[i];
				MechanicalGate gate = entityManager.GetComponentData<MechanicalGate>(root);
				DynamicBuffer<LinkedEntityGroup> group = entityManager.GetBuffer<LinkedEntityGroup>(root, isReadOnly: true);
				for (int j = 1; j < group.Length; j++)
				{
					Entity child = group[j].Value;
					if (!m_visualLookup.HasComponent(child) || m_visualLookup[child].Collider)
					{
						continue;
					}
					if (!entityManager.HasComponent<MaterialColor>(child))
					{
						continue;
					}
					MaterialColor color = entityManager.GetComponentData<MaterialColor>(child);
					if (color.Value.w == gate.Opacity)
					{
						continue;
					}
					color.Value.w = gate.Opacity;
					entityManager.SetComponentData(child, color);
				}
			}
		}
	}
}
