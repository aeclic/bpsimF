using Unity.Entities;

namespace Bpsim.Parts
{
	/// <summary>
	/// 建造图层（对应 BPIR 的 L1–L6 / BPRC 的 <c>ContraptionLayers</c>）：1 是公共层，
	/// 2..16 是可选结构层。
	///
	/// 规则与 BPIR 一致：
	/// * 每一层有独立的占位表，所以不同层可以叠在同一格；
	/// * 同层四邻域可以焊接；层 2..N 额外可以连到「正下方」层 1 的同格零件；
	/// * 不同结构层之间互不焊接；
	/// * 渲染按层号抬高 z（层号越大越靠前），视觉上形成堆叠；
	/// * 建造、擦除、拖动只作用于当前活动层；活动层 0 表示「全部（查看）」，只读。
	/// </summary>
	public struct PartBuildLayer : IComponentData
	{
		public const byte MinLayer = 1;

		public const byte MaxLayer = 16;

		/// <summary>
		/// 新场景默认层数。对齐 BPIR：其 <c>INFeature.LayerCount</c> 默认值就是 6，
		/// 而 <c>LayerPanelUI.GetMaxLayer()</c> 又硬夹到 6，所以 BPIR 的建造页固定是 6 层。
		/// <see cref="MaxLayer" /> 仍然保留 16 作为硬上限（旧存档里已有的更高层不受影响）。
		/// </summary>
		public const byte DefaultLayerCount = 6;

		/// <summary>每高一层向相机方向前移的世界单位（与 BPIR 的 2 * 0.01 一致）。</summary>
		public const float DepthStep = 0.02f;

		public byte Value;

		public static PartBuildLayer Default => new PartBuildLayer
		{
			Value = MinLayer
		};

		public static byte Clamp(int layer)
		{
			if (layer < MinLayer)
			{
				return MinLayer;
			}
			if (layer > MaxLayer)
			{
				return MaxLayer;
			}
			return (byte)layer;
		}
	}

	public static class PartBuildLayerUtility
	{
		public static byte GetLayer(EntityManager entityManager, Entity entity)
		{
			if (entity != Entity.Null && entityManager.Exists(entity) && entityManager.HasComponent<PartBuildLayer>(entity))
			{
				return PartBuildLayer.Clamp(entityManager.GetComponentData<PartBuildLayer>(entity).Value);
			}
			return PartBuildLayer.MinLayer;
		}

		public static void SetLayer(EntityManager entityManager, Entity entity, int layer)
		{
			if (entity == Entity.Null || !entityManager.Exists(entity))
			{
				return;
			}
			PartBuildLayer value = new PartBuildLayer
			{
				Value = PartBuildLayer.Clamp(layer)
			};
			if (entityManager.HasComponent<PartBuildLayer>(entity))
			{
				entityManager.SetComponentData(entity, value);
			}
			else
			{
				entityManager.AddComponentData(entity, value);
			}
		}

		public static void EnsureLayer(EntityManager entityManager, Entity entity)
		{
			if (entity != Entity.Null && entityManager.Exists(entity) && !entityManager.HasComponent<PartBuildLayer>(entity))
			{
				entityManager.AddComponentData(entity, PartBuildLayer.Default);
			}
		}
	}
}
