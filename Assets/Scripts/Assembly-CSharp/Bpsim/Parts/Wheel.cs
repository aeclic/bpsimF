using Unity.Entities;
using Unity.Mathematics;

namespace Bpsim.Parts
{
	/// <summary>
	/// 轮子的行为分类，对应 BPSS 901-17.0 里四套不同的 Update/FixedUpdate 实现。
	/// </summary>
	public enum WheelKind : byte
	{
		/// <summary>CartWheel / NormalWheel / SmallWheel：只做视觉滚动，不施加任何力（BPSS <c>CartWheel.cs</c>）。</summary>
		Free = 0,

		/// <summary>MotorWheel：受引擎功率驱动的主动轮（BPSS <c>MotorWheel.cs</c>）。</summary>
		Motor = 1,

		/// <summary>StickyWheel：主动轮 + 贴地阻尼 + 吸附地面的额外法向力（BPSS <c>StickyWheel.cs</c>）。</summary>
		Sticky = 2,

		/// <summary>OffRoadWheel：BPSS 里独立的 <c>OffRoadWheel.cs</c>，在 bpsim 里是 MotorWheel 的索引 7。</summary>
		OffRoad = 3
	}

	/// <summary>
	/// 轮子运行时状态。字段与 BPSS 的 <c>MotorWheel</c> / <c>StickyWheel</c> / <c>CartWheel</c>
	/// 私有字段一一对应，区别只在于这里由 ECS 系统 <c>WheelSystem</c> 每物理帧统一驱动。
	/// </summary>
	/// <remarks>
	/// 由 <see cref="PartEntityFactory"/> 在创建 CartWheel / NormalWheel / SmallWheel /
	/// MotorWheel / StickyWheel 实体时挂载，参数在 <see cref="PartEntityFactory.CreatePart"/>
	/// 里按 <see cref="PartTypeInfo"/> 写入（对应 BPSS prefab 上的 m_force / m_stickiness / m_drag）。
	/// </remarks>
	public struct Wheel : IComponentData, IQueryTypeParameter
	{
		/// <summary>行为分类。</summary>
		public WheelKind Kind;

		/// <summary>玩家是否开启了这台轮子（对应 BPSS <c>m_enabled</c>）。</summary>
		public bool Enabled;

		/// <summary>是否已经解析出 pivot 子实体与半径（首帧绑定一次）。</summary>
		public bool Bound;

		/// <summary>BPSS prefab 上的 <c>m_force</c>：推力基数，实际推力再乘引擎功率系数。</summary>
		public float Force;

		/// <summary>BPSS <c>m_stickiness</c>：吸附地面的法向力（只有 StickyWheel 用）。</summary>
		public float Stickiness;

		/// <summary>BPSS <c>m_drag</c>：贴地阻尼系数（只有 StickyWheel 用）。</summary>
		public float Drag;

		/// <summary>物理半径，取自根碰撞体里的球半径（BPSS 用 <c>GetComponent&lt;SphereCollider&gt;().radius</c>）。</summary>
		public float Radius;

		/// <summary>周长，视觉滚动用。</summary>
		public float Circumference;

		/// <summary>当前引擎功率下的速度上限（BPSS <c>m_maximumSpeed = 15f * enginePowerFactor</c>）。</summary>
		public float MaximumSpeed;

		/// <summary>当前引擎功率下的最大推力（BPSS <c>m_maximumForce = m_force * enginePowerFactor</c>）。</summary>
		public float MaximumForce;

		/// <summary>本帧解析出的引擎功率系数。</summary>
		public float PowerFactor;

		/// <summary>推力斜坡计时器，0..1（BPSS <c>m_thrustTimer</c>）。</summary>
		public float ThrottleTimer;

		/// <summary>带符号推力斜坡 <c>pow(timer, 0.4)</c>，齿轮箱开启时取负（倒车）。</summary>
		public float Throttle;

		/// <summary>视觉滚动速度（BPSS <c>m_spinSpeed</c>），未接地时由速度投影得到。</summary>
		public float SpinSpeed;

		/// <summary>累计滚动角，单位度（BPSS <c>m_angle</c>）。</summary>
		public float Angle;

		/// <summary>射线探测方向（BPSS <c>m_lastContactDirection</c>），首帧用轮子本地 -Y。</summary>
		public float3 LastContactDirection;

		/// <summary>地面切向（BPSS <c>m_lastForceDirection</c> = cross(normal, forward)）。</summary>
		public float3 LastForceDirection;

		/// <summary>本帧射线是否命中地面（BPSS <c>m_hasContact</c>）。</summary>
		public bool HasContact;

		/// <summary>是否接地（BPSS <c>m_grounded</c>，决定音效与冰面分支）。</summary>
		public bool Grounded;

		/// <summary>
		/// 累计接地帧数。BPSS 没有这个字段，它是给验收用的：轮子加速后会短暂弹离地面，
		/// 只看某一帧的 <see cref="HasContact" /> 会把「真的开动过」误判成「从没接触过」。
		/// </summary>
		public int ContactTicks;

		/// <summary>轮辐子实体（<c>WheelPivot</c>），按角度真转。</summary>
		public Entity Pivot;

		/// <summary>假轮子实体（<c>FakeWheelPivot</c>），只做 <c>sin(2θ)*8°</c> 摆动。</summary>
		public Entity FakePivot;

		/// <summary>
		/// 按部件类型生成默认参数。数值取自 BPSS 901-17.0 的 prefab：
		/// MotorWheel 01-07 <c>m_force = 50</c>、08（OffRoadWheel）<c>m_force = 65</c>，
		/// StickyWheel <c>m_force = 100 / m_stickiness = 350 / m_drag = 2</c>。
		/// </summary>
		public static Wheel Create(PartTypeInfo info)
		{
			Wheel wheel = default(Wheel);
			wheel.LastContactDirection = float3.zero;
			wheel.LastForceDirection = new float3(1f, 0f, 0f);
			switch (info.PartType)
			{
			case PartType.MotorWheel:
				// bpsim 把 BPSS 的 OffRoadWheel 收在 MotorWheel 的索引 7（Innovation 模板）。
				wheel.Kind = ((info.PartIndex == 7) ? WheelKind.OffRoad : WheelKind.Motor);
				wheel.Force = ((info.PartIndex == 7) ? 65f : 50f);
				break;
			case PartType.StickyWheel:
				wheel.Kind = WheelKind.Sticky;
				wheel.Force = 100f;
				wheel.Stickiness = 350f;
				wheel.Drag = 2f;
				break;
			case PartType.SmallWheel when info.PartIndex == 7:
				// BPSS 的 Part_SmallWheel_08_SET 挂的是 MotorWheel.cs（m_force = 50），
				// 不是 CartWheel.cs —— 小轮子族里唯一带动力的一档。
				wheel.Kind = WheelKind.Motor;
				wheel.Force = 50f;
				break;
			case PartType.CartWheel:
			case PartType.NormalWheel:
			case PartType.SmallWheel:
				wheel.Kind = WheelKind.Free;
				wheel.Force = 0f;
				break;
			default:
				wheel.Kind = WheelKind.Free;
				break;
			}
			wheel.Radius = 0.45f;
			wheel.Circumference = math.PI * 2f * wheel.Radius;
			return wheel;
		}

		/// <summary>这台轮子是否吃引擎功率（决定点击是否可开关）。</summary>
		public bool IsPowered
		{
			get
			{
				return Kind == WheelKind.Motor || Kind == WheelKind.Sticky || Kind == WheelKind.OffRoad;
			}
		}
	}

	/// <summary>
	/// 标记部件 prefab 里名字为 <c>WheelPivot</c> / <c>FakeWheelPivot</c> 的子实体，
	/// 让 <c>WheelSystem</c> 不必靠（发行版里为空的）实体名字去找视觉滚动目标。
	/// </summary>
	public struct WheelPivot : IComponentData
	{
		/// <summary>true = <c>FakeWheelPivot</c>（只摆动），false = <c>WheelPivot</c>（真转）。</summary>
		public bool Fake;
	}
}
