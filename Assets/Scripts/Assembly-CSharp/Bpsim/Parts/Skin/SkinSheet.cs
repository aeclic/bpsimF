using Unity.Entities;
using Unity.Mathematics;

namespace Bpsim.Parts.Skin
{
	/// <summary>
	/// 蒙皮部件（Besiege 的 BuildEdgeBlock 风格曲面）的可编辑数据。
	/// </summary>
	/// <remarks>
	/// 参照 BPRC 的 <c>SkinPartGeometry</c>，但把「挂在 MonoBehaviour 上的
	/// CustomParams 字典」换成了纯 blittable 的 <see cref="IComponentData"/>：
	/// 四角坐标、四边弯曲量、贴图模式/平铺/颜色全部是组件字段，几何由
	/// <c>SkinSheetSystem</c> 统一重建，没有任何 MonoBehaviour 持有形状状态。
	/// 这样蒙皮的形状就是「一块可序列化的数据」，能被 Baker / 存档 / QA 直接读写。
	/// </remarks>
	public struct SkinSheet : IComponentData
	{
		/// <summary>左下角（部件本地坐标）。</summary>
		public float2 BottomLeft;

		/// <summary>右下角。</summary>
		public float2 BottomRight;

		/// <summary>右上角。</summary>
		public float2 TopRight;

		/// <summary>左上角。</summary>
		public float2 TopLeft;

		/// <summary>
		/// 四边的法向弯曲量（0 下 / 1 上 / 2 右 / 3 左），单位是部件本地长度。
		/// 对应 BPRC 的 <c>SkinBottomCurve / SkinTopCurve / SkinRightCurve / SkinLeftCurve</c>。
		/// </summary>
		public float4 Curve;

		/// <summary>四边中点的切向偏移（BPRC 的 <c>SkinEdgeAlong0..3</c>）。</summary>
		public float4 Along;

		/// <summary>
		/// 贴图上取用的子矩形（<c>x, y, width, height</c>，UV 空间 0..1，左下为原点）。
		/// </summary>
		/// <remarks>
		/// 原版蒙皮可以「图集局部裁剪」，即从一张图集里只取一小块贴到板子上；
		/// 少了这个字段整张图集就会被整块铺到曲面上（看起来就是「蒙皮变成一整张图集」）。
		/// 默认 <c>(0, 0, 1, 1)</c> 表示整张贴图。
		/// </remarks>
		public float4 UVRect;

		/// <summary>0 = 拉伸（整张图铺满曲面）；1 = 平铺（按世界尺寸重复）。</summary>
		public int TextureMode;

		public float TileX;

		public float TileY;

		/// <summary>顶点色（材质 <c>_Color</c> 之外再乘一遍，与 BPRC 的 SkinColor* 对应）。</summary>
		public float4 Color;

		/// <summary>贴图注册表里的句柄；-1 表示没有贴图（白板）。</summary>
		public int TextureHandle;

		/// <summary>
		/// 形状版本号。任何参数改动都必须自增，<c>SkinSheetSystem</c> 据此判断
		/// 是否需要重建网格与凸包；比逐字段比较便宜，也不怕浮点误差。
		/// </summary>
		public int Version;
	}

	/// <summary>
	/// 蒙皮网格的运行时句柄（托管侧缓存里的索引 + 已构建的版本号）。
	/// </summary>
	/// <remarks>
	/// Unity 的 <c>Mesh</c> 是托管对象，不能塞进组件，所以 ECS 侧只存一个整数
	/// 句柄，真正的网格/材质放在 <see cref="SkinSheetRegistry"/> 里。
	/// 渲染由 <c>SkinSheetRenderSystem</c> 每帧 <c>Graphics.RenderMesh</c> 提交，
	/// 不走 <c>SceneRenderSystem</c> 的图集四边形批处理（那条管线只能画单张
	/// 图集 sprite，画不了 625 顶点的自由曲面）。
	/// 句柄的回收由 <c>SkinSheetSystem</c> 每帧「点名」完成：仍然存在于世界里的实体
	/// 把自己的句柄标记一次，一帧没被点到的句柄就释放（见 <see cref="SkinSheetRegistry.BeginFrame"/>）。
	/// </remarks>
	public struct SkinSheetMesh : IComponentData
	{
		/// <summary>托管注册表句柄；-1 = 还没建。</summary>
		public int Handle;

		/// <summary>已构建到哪个 <see cref="SkinSheet.Version"/>。</summary>
		public int BuiltVersion;

		/// <summary>构建时的世界缩放（部件被缩放后贴图平铺密度要跟着变）。</summary>
		public float2 BuiltScale;
	}

	/// <summary>把 <see cref="SkinSheet"/> 的四条边索引写成可读常量。</summary>
	public static class SkinSheetEdges
	{
		public const int Bottom = 0;

		public const int Top = 1;

		public const int Right = 2;

		public const int Left = 3;
	}

	/// <summary>构造默认蒙皮数据的工厂（部件烘焙与「恢复默认」共用）。</summary>
	public static class SkinSheetDefaults
	{
		/// <summary>一格宽的布面蒙皮：外观与部件列表中的蒙皮图标一致。</summary>
		public static SkinSheet Create()
		{
			return new SkinSheet
			{
				BottomLeft = SkinSheetGeometry.DefaultCorner(0),
				BottomRight = SkinSheetGeometry.DefaultCorner(1),
				TopRight = SkinSheetGeometry.DefaultCorner(2),
				TopLeft = SkinSheetGeometry.DefaultCorner(3),
				Curve = float4.zero,
				Along = float4.zero,
				UVRect = new float4(0f, 0f, 1f, 1f),
				TextureMode = 0,
				TileX = 1f,
				TileY = 1f,
				Color = new float4(1f, 1f, 1f, 1f),
				TextureHandle = SkinSheetRegistry.Instance.GetDefaultTextureHandle(),
				Version = 1
			};
		}
	}
}
