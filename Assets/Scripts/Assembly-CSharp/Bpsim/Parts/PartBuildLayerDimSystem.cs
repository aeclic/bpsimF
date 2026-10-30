using Bpsim.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

namespace Bpsim.Parts
{
	/// <summary>
	/// 建造图层的视觉降噪（对应 BPIR 的 <c>ContraptionLayerDim</c>）：
	/// 非活动图层的部件压暗，活动图层保持原色。
	///
	/// ECS 版不直接改 <see cref="MaterialColor"/>（<c>ColoredFrameSystem</c> 每帧会重写它），
	/// 而是给每个渲染实体写一份 <see cref="PartLayerTint"/> 系数，
	/// 由 <c>SceneRenderSystem</c> 在填充 GPU 颜色缓冲时做 <c>MaterialColor × Tint</c>。
	/// 因此切换活动层只改系数，不碰共享材质，也不会逐帧累积变暗；模拟时全部恢复白色。
	/// </summary>
	[UpdateInGroup(typeof(PresentationSystemGroup))]
	internal partial class PartBuildLayerDimSystem : SystemBase
	{
		private static readonly float4 s_fullTint = new float4(1f, 1f, 1f, 1f);

		// BPIR 的 DimTint 是 (0.85,0.88,0.95) 再乘 0.3 的乘色；不透明 URP 用 RGB 直接压暗。
		private static readonly float4 s_dimTint = new float4(0.55f, 0.58f, 0.65f, 1f);

		private EntityQuery m_query;

		private int m_lastActiveLayer = int.MinValue;

		private float m_lastApplyTime;

		protected override void OnCreate()
		{
			m_query = GetEntityQuery(ComponentType.ReadOnly<PartTag>(), ComponentType.ReadOnly<PartBuildLayer>(), ComponentType.ReadOnly<LinkedEntityGroup>());
		}

		protected override void OnUpdate()
		{
			PartManager manager = PartManager.Instance;
			if (manager == null || !manager.HasActiveScene())
			{
				return;
			}
			if (manager.IsSimulating)
			{
				if (m_lastActiveLayer >= 0)
				{
					m_lastActiveLayer = -1;
					Apply(0);
				}
				return;
			}
			int activeLayer = manager.ActiveScene.Unmanaged.ActiveLayer;
			bool layerChanged = activeLayer != m_lastActiveLayer;
			float now = UnityEngine.Time.unscaledTime;
			if (!layerChanged && now - m_lastApplyTime < 0.25f)
			{
				return;
			}
			m_lastActiveLayer = activeLayer;
			m_lastApplyTime = now;
			Apply(activeLayer);
		}

		private void Apply(int activeLayer)
		{
			EntityManager entityManager = base.EntityManager;
			using NativeArray<Entity> roots = m_query.ToEntityArray(Allocator.Temp);
			foreach (Entity root in roots)
			{
				int layer = PartBuildLayer.Clamp(entityManager.GetComponentData<PartBuildLayer>(root).Value);
				float4 tint = ((activeLayer <= 0 || layer == activeLayer) ? s_fullTint : s_dimTint);
				DynamicBuffer<LinkedEntityGroup> group = entityManager.GetBuffer<LinkedEntityGroup>(root, isReadOnly: true);
				for (int i = 0; i < group.Length; i++)
				{
					Entity renderEntity = group[i].Value;
					if (!entityManager.HasComponent<MaterialColor>(renderEntity))
					{
						continue;
					}
					if (!entityManager.HasComponent<PartLayerTint>(renderEntity))
					{
						entityManager.AddComponentData(renderEntity, new PartLayerTint
						{
							Value = tint
						});
					}
					else if (!entityManager.GetComponentData<PartLayerTint>(renderEntity).Value.Equals(tint))
					{
						entityManager.SetComponentData(renderEntity, new PartLayerTint
						{
							Value = tint
						});
					}
				}
			}
		}
	}
}
