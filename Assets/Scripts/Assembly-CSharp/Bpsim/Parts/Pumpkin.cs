using Unity.Entities;
using Unity.Mathematics;

namespace Bpsim.Parts
{
	/// <summary>
	/// BPSS 的固定南瓜（<c>Pumpkin.cs</c> + <c>FixedPumpkinManager.cs</c>）。
	/// </summary>
	/// <remarks>
	/// <para>
	/// 原版南瓜的玩法价值全在「固定」上：玩家点一下南瓜，它所在的整个焊接组件就被
	/// 冻成地面——刚体 <c>constraints = FreezeAll</c>、<c>isKinematic = true</c>、
	/// 层切到 <c>Ground</c> 并 <c>Sleep()</c>，于是载具可以把它当支架/斜坡用；
	/// 再点一下恢复。没开 <c>INFeature.FixedPumpkin</c> 时南瓜就是个纯装饰件，
	/// <c>IsEnabled()</c> 恒为 false、点击无效。
	/// </para>
	/// <para>
	/// bpsim 没有 Unity 刚体的 <c>constraints</c>，对应做法是把 <see cref="Unity.Physics.PhysicsMass"/>
	/// 的 <c>InverseMass</c>/<c>InverseInertia</c> 清零并清掉速度——Unity.Physics 里
	/// 逆质量为零的刚体就是静态体，效果和 <c>FreezeAll</c> 一致。层切换没有对应物，
	/// 因为 bpsim 的碰撞过滤按 <c>CollisionFilter</c> 位掩码走、没有 Ground 层这个玩法概念。
	/// </para>
	/// </remarks>
	public struct PumpkinPart : IComponentData
	{
		/// <summary>BPSS <c>m_enabled</c>：南瓜是否处于「已固定」状态。</summary>
		public byte Enabled;

		/// <summary>本帧是否已经把它所在的焊接组件冻住（避免每帧重复冻结）。</summary>
		public byte Applied;
	}

	/// <summary>
	/// 挂在**被南瓜冻住**的零件上，用来还原。原版靠 <c>FixedPumpkinManager.m_data</c>
	/// 字典记住每个 Rigidbody 的原始 <c>constraints</c>；这里直接把原始逆质量写回零件自己身上，
	/// 少一层跨实体引用，也不会因为零件被删除而留下野指针。
	/// </summary>
	public struct PumpkinFrozen : IComponentData
	{
		public float InverseMass;

		public float3 InverseInertia;

		/// <summary>冻结它的那个南瓜，恢复时用来清 <see cref="PumpkinPart.Applied"/>。</summary>
		public Entity Source;
	}
}
