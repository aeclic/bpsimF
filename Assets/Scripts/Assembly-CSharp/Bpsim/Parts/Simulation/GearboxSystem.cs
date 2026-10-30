using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;

namespace Bpsim.Parts.Simulation
{
	/// <summary>
	/// 齿轮箱的视觉与状态维护，对应 BPSS 901-17.0 的 <c>Gearbox.cs</c>。
	/// </summary>
	/// <remarks>
	/// <para>
	/// BPSS 的 <c>Gearbox</c> 只有三件事：一个 <c>activated</c> 布尔、两张状态贴图的
	/// <c>SetActive</c> 切换、以及点击时播放 <c>gearboxSwitch</c> 音效。它自己<b>不产生力</b> ——
	/// 倒车是同一焊接组件里的 <c>MotorWheel</c> / <c>StickyWheel</c> / <c>OffRoadWheel</c>
	/// 每帧查 <c>GetGearbox(this).IsEnabled()</c> 后把推力取反实现的。
	/// </remarks>
	/// <para>
	/// 因此这里也保持同样的分工：本系统只负责「状态 → 视觉」这一段，
	/// 推力取反由 <see cref="WheelSystem" /> 汇总同一焊接组件里已开启的齿轮箱来做。
	/// </para>
	/// </remarks>
	[UpdateInGroup(typeof(PartSimulationSystemGroup))]
	internal partial class GearboxSystem : SystemBase
	{
		private EntityQuery m_gearboxQuery;

		private ComponentLookup<GearboxVisual> m_visualLookup;

		protected override void OnCreate()
		{
			EntityQueryBuilder builder = new EntityQueryBuilder(Allocator.Temp);
			builder = builder.WithAllRW<Gearbox>();
			builder = builder.WithAll<PartTag>();
			builder = builder.WithAll<LinkedEntityGroup>();
			m_gearboxQuery = builder.Build(ref base.CheckedStateRef);
			m_visualLookup = base.CheckedStateRef.GetComponentLookup<GearboxVisual>(isReadOnly: true);
		}

		protected override void OnUpdate()
		{
			PartManager manager = PartManager.Instance;
			if (manager == null || !manager.IsSimulating)
			{
				return;
			}
			m_visualLookup.Update(ref base.CheckedStateRef);
			using NativeArray<Entity> gearboxes = m_gearboxQuery.ToEntityArray(Allocator.Temp);
			// 先把「该隐藏 / 该显示」的子实体收进临时表，再统一做结构变更。
			// 直接在 DynamicBuffer 上循环里 Add/RemoveComponent 会让缓冲区句柄失效
			// （ObjectDisposedException: ... invalidated by a structural change）。
			using NativeList<Entity> toShow = new NativeList<Entity>(8, Allocator.Temp);
			using NativeList<Entity> toHide = new NativeList<Entity>(8, Allocator.Temp);
			for (int i = 0; i < gearboxes.Length; i++)
			{
				Entity entity = gearboxes[i];
				bool activated = EntityManager.GetComponentData<Gearbox>(entity).Activated;
				DynamicBuffer<LinkedEntityGroup> group = EntityManager.GetBuffer<LinkedEntityGroup>(entity, isReadOnly: true);
				for (int j = 1; j < group.Length; j++)
				{
					Entity child = group[j].Value;
					if (!m_visualLookup.HasComponent(child))
					{
						continue;
					}
					// BPSS: activeSprite.SetActive(activated); inactiveSprite.SetActive(!activated)
					bool visible = m_visualLookup[child].Active == activated;
					bool disabled = EntityManager.HasComponent<DisableRendering>(child);
					if (visible && disabled)
					{
						toShow.Add(child);
					}
					else if (!visible && !disabled)
					{
						toHide.Add(child);
					}
				}
			}
			for (int i = 0; i < toShow.Length; i++)
			{
				EntityManager.RemoveComponent<DisableRendering>(toShow[i]);
			}
			for (int i = 0; i < toHide.Length; i++)
			{
				EntityManager.AddComponent<DisableRendering>(toHide[i]);
			}
		}
	}
}
