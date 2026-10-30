using Unity.Entities;

namespace Bpsim.Parts
{
	/// <summary>
	/// 齿轮箱开关状态。对应 BPSS 901-17.0 的 <c>Gearbox.cs</c>。
	/// </summary>
	/// <remarks>
	/// BPSS 的齿轮箱自己<b>不产生任何力</b>，它只是一个布尔状态位：同一焊接组件里的
	/// <c>MotorWheel</c> / <c>StickyWheel</c> / <c>OffRoadWheel</c> 每帧查
	/// <c>contraption.GetGearbox(this).IsEnabled()</c>，为真时把推力取反（倒车）。
	/// 这里保留同样的分工：齿轮箱只翻转 <see cref="Activated"/>，
	/// 由 <c>WheelSystem</c> 汇总成「哪些焊接组件处于倒车状态」再作用到轮子上。
	/// </remarks>
	public struct Gearbox : IComponentData, IQueryTypeParameter
	{
		/// <summary>BPSS <c>activated</c>：true = 已开启（同一组件内的轮子倒车）。</summary>
		public bool Activated;

		/// <summary>BPSS <c>achievementSent</c>：首次开启时发成就，避免重复触发。</summary>
		public bool AchievementSent;
	}

	/// <summary>
	/// 齿轮箱的两个状态贴图子实体。BPSS 在 <c>Awake</c> 里按 <c>activated</c> 对
	/// <c>activeSprite</c> / <c>inactiveSprite</c> 做 <c>SetActive</c>；这里改成给子实体打标记，
	/// 由 <c>GearboxSystem</c> 切换 <see cref="Unity.Entities.DisableRendering"/>。
	/// </summary>
	/// <remarks>
	/// 之所以不靠实体名字：<c>EntityManager.GetName</c> 只在编辑器下有效
	/// （<c>DOTS_DISABLE_DEBUG_NAMES</c> 时恒为空串），发行版里按名字找子物体会全部落空。
	/// </remarks>
	public struct GearboxVisual : IComponentData
	{
		/// <summary>true = <c>ActiveGearboxSprite</c>（齿轮箱开启时显示），false = <c>InactiveGearboxSprite</c>。</summary>
		public bool Active;
	}
}
