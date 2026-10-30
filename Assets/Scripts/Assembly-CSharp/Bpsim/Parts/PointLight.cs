using Unity.Entities;

namespace Bpsim.Parts
{
	/// <summary>
	/// BPSS <c>PointLight</c> 的 <c>activeSprite</c> / <c>inactiveSprite</c> 两套图之一。
	/// </summary>
	/// <remarks>
	/// 原版是 MonoBehaviour 上的两个 <c>GameObject</c> 字段，<c>OnTouch</c> 里直接
	/// <c>SetActive</c>。bpsim 是 ECS，运行时拿不到 GameObject 引用，所以建实体时按
	/// 子物体名打标记（<c>PartEntityFactory.CreateChildMarker</c>），之后由玩法行为
	/// 遍历 <c>LinkedEntityGroup</c> 增删 <see cref="Unity.Rendering.DisableRendering"/>。
	/// 这套分工和 <see cref="SwitchVisual"/>、<see cref="GearboxVisual"/> 一致。
	/// </remarks>
	public struct PointLightVisual : IComponentData
	{
		/// <summary>true = <c>ActivePointLightSprite</c>（点亮时显示），false = <c>InactivePointLightSprite</c>。</summary>
		public bool Active;

		/// <summary>
		/// true = <c>*FullPointLightSprite</c>（BPSS <c>AutoControlLight.m_activeFullSprite</c> /
		/// <c>m_inactiveFullSprite</c>）。只有 AutoControlLight（PartIndex 6）有这一对，
		/// 且只有斜向型号（<c>m_gridRotation &gt;= Deg_45</c>）用它，方型号用普通那一对。
		/// </summary>
		public bool Full;
	}

	/// <summary>
	/// BPSS <c>SpotLight.m_lightConeContainer</c>（光锥容器）的标记。
	/// </summary>
	/// <remarks>
	/// prefab 里这个 <c>Container</c> 的 localScale 是 (0,0,0)——原版 <c>Awake</c> 里
	/// <c>localScale = Vector3.one * m_lightConeTargetScale</c> 才把它撑出来，第一次点亮前
	/// 光锥本来就不可见。名字 <c>Container</c> 在整个 GameObject 目录里只出现在
	/// <c>PartGroup_45_SpotLight.prefab</c>，所以可以按名字安全翻译成组件。
	/// </remarks>
	public struct SpotLightCone : IComponentData
	{
	}
}
