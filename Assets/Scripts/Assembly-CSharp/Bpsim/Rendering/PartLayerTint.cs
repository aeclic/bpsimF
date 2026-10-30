using Unity.Entities;
using Unity.Mathematics;

namespace Bpsim.Rendering
{
	/// <summary>
	/// 渲染实体的图层着色系数。由 <c>PartBuildLayerDimSystem</c> 写在每个渲染实体上，
	/// <c>SceneRenderSystem</c> 把 <c>MaterialColor × Value</c> 拷进 GPU 颜色缓冲。
	///
	/// 不在 <c>MaterialColor</c> 上直接乘，是因为 <c>ColoredFrameSystem</c> 每帧会重写该颜色；
	/// 独立系数既不会被覆盖，也不会逐帧累积变黑。
	/// </summary>
	public struct PartLayerTint : IComponentData
	{
		public float4 Value;

		public static PartLayerTint White => new PartLayerTint
		{
			Value = new float4(1f, 1f, 1f, 1f)
		};
	}
}
