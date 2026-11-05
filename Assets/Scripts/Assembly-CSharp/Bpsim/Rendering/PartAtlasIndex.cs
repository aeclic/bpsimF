using Unity.Entities;

namespace Bpsim.Rendering
{
	/// <summary>
	/// 渲染实体所用图集的序号（0 = 原版 <c>Part_Texture_0</c>）。
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>SceneRenderSystem</c> 的批处理里，每个实例的 <c>_Rect/_UVRect/_Color</c> 是逐实例数据，
	/// 而贴图在材质上。Mod 想给变体换皮肤就必须让这条绘制命令指向另一张材质，
	/// 于是把「这个实体用哪张图集」记成组件，剔除阶段读它算分组，发命令时按分组选材质。
	/// </para>
	/// <para>
	/// 没有这个组件的实体一律按 0 处理，所以原版部件完全不受影响。
	/// </para>
	/// </remarks>
	public struct PartAtlasIndex : IComponentData
	{
		public int Value;

		public const int Default = 0;
	}
}
