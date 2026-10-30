using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

namespace Bpsim.Parts
{
	/// <summary>
	/// BPSS 901-17.0 的「特殊鸟蛋」种类（<c>Egg.cs</c> 按 <c>customPartIndex</c> 分派）。
	/// </summary>
	public enum EggKind : byte
	{
		/// <summary>普通蛋：没有任何主动效果。</summary>
		Plain = 0,
		/// <summary>重力蛋（01 = 索引 1）：对全场刚体施加万有引力。</summary>
		Gravity = 1,
		/// <summary>失重蛋（索引 2）：关掉同连通块的重力；斜向朝向时是无质量蛋。</summary>
		Massless = 2,
		/// <summary>穿透蛋（索引 3）：关掉同连通块的全部碰撞体。</summary>
		Ghost = 3,
		/// <summary>虚空蛋（索引 4）：每帧关掉半径内一切碰撞体。</summary>
		Void = 4,
		/// <summary>异形胶蛋（索引 5，BPSS <c>AlienEgg</c>）：同连通块的所有关节不可断。</summary>
		Alien = 5
	}

	/// <summary>
	/// 鸟蛋族的 ECS 状态（BPSS 901-17.0 <c>Egg.cs</c> / <c>AlienEgg.cs</c> 的重构）。
	/// </summary>
	/// <remarks>
	/// <para>
	/// 原版把状态藏在 <c>m_enabled</c> / <c>m_colliders</c> / <c>m_rigidBodyData</c> /
	/// <c>m_massData</c> 这些 MonoBehaviour 字段里，并在 <c>SetEnabled</c> 里直接改
	/// 别的刚体与 Collider。<b>ECS 版把「想要的状态」留在组件上</b>：
	/// 行为层只翻转 <see cref="Enabled" />，由 <c>EggSystem</c> 把
	/// <see cref="Applied" /> 对齐到 <see cref="Enabled" />（幂等调和），
	/// 需要还原的原值存进蛋实体自己的 <see cref="EggEffect" /> 缓冲区。
	/// </para>
	/// <para>
	/// 索引与 BPSS 的对应关系（bpsim 的 <c>Part_Egg_0..5</c> = BPSS 的 <c>Part_Egg_01..06</c>）：
	/// 0 普通、1 重力、2 失重/无质量、3 穿透、4 虚空、5 异形胶。
	/// BPSS 的 <c>m_isSpecialEgg = customPartIndex is 1..4</c>，索引 5 由
	/// <c>AlienEgg</c> 子类接管，两者合起来正好是 1..5 可点击。
	/// </para>
	/// </remarks>
	public struct Egg : IComponentData, IQueryTypeParameter
	{
		/// <summary>重力蛋自身的质量（<c>INFeature.GravityEggMass</c> 默认 50）。</summary>
		public const float GravityMass = 50f;

		/// <summary>引力系数（<c>INFeature.GravityEggForce</c> 默认 256）。</summary>
		public const float GravityForce = 256f;

		/// <summary>内半径：距离更近时按此值算，避免除以 0 导致的无限大（默认 4）。</summary>
		public const float GravityInnerRadius = 4f;

		/// <summary>外半径：超过此距离不施力（<c>Infinity</c>）。</summary>
		public const float GravityOuterRadius = float.PositiveInfinity;

		/// <summary>无质量蛋的质量倍率（原质量的千分之一）。</summary>
		public const float MasslessFactor = 0.001f;

		public EggKind Kind;

		/// <summary>首帧初始化（复现 BPSS <c>PostInitialize</c>）是否已完成。</summary>
		public bool Bound;

		/// <summary>玩家想要的状态（点击翻转）。</summary>
		public bool Enabled;

		/// <summary>一键式效果（失重/穿透/异形胶）当前是否已经写进物理组件。</summary>
		public bool Applied;

		/// <summary>虚空蛋的虚化半径（按旋转方向取默认值，BPSS 可用 <c>VoidRadius</c> 参数覆盖）。</summary>
		public float VoidRadius;

		/// <summary>虚空蛋是否默认开启（BPSS <c>VoidDefaultOn</c>：正交朝向为真）。</summary>
		public bool VoidDefaultOn;

		public static EggKind GetKind(PartTypeInfo info)
		{
			if (info.PartType != PartType.Egg)
			{
				return EggKind.Plain;
			}
			switch (info.PartIndex)
			{
			case 1:
				return EggKind.Gravity;
			case 2:
				return EggKind.Massless;
			case 3:
				return EggKind.Ghost;
			case 4:
				return EggKind.Void;
			case 5:
				return EggKind.Alien;
			default:
				return EggKind.Plain;
			}
		}

		public static Egg Create(PartTypeInfo info)
		{
			Egg egg = default(Egg);
			egg.Kind = GetKind(info);
			egg.VoidRadius = 8f;
			egg.VoidDefaultOn = true;
			// 初值按 BPSS PostInitialize 的语义给（2/3 立刻 OnTouch 打开、5 也立刻打开、
			// 4 在正交朝向下默认打开、1 与 0 默认关闭）。旋转要等到 PlacePartAt 才知道，
			// 所以虚空蛋的真实初值在 EggSystem.Bind 里用同一套判定再算一次。
			egg.Enabled = GetDefaultEnabled(egg.Kind, 0);
			return egg;
		}

		/// <summary>
		/// BPSS <c>Egg.PostInitialize()</c> / <c>AlienEgg.PostInitialize()</c> 决定的初始开关状态。
		/// </summary>
		/// <remarks>
		/// 行为层（<c>EggBehavior.Init</c>）也要用它 —— 行为是在 <c>StartSimulation</c> 时建的，
		/// 早于 <c>EggSystem.Bind</c>，直接读 <see cref="Enabled" /> 会读到 <c>Create</c> 时的
		/// 默认朝向，导致虚空蛋按钮亮着但实际没开。
		/// </remarks>
		public static bool GetDefaultEnabled(EggKind kind, int rotation)
		{
			switch (kind)
			{
			case EggKind.Massless:
			case EggKind.Ghost:
			case EggKind.Alien:
				return true;
			case EggKind.Void:
				return IsOrthogonal(rotation);
			default:
				return false;
			}
		}

		/// <summary>
		/// 按旋转方向的默认虚化半径（BPSS <c>Egg.DefaultVoidRadius()</c>）：
		/// 45°/90° = 4，135°/180° = 2，225°/270° = 12，其余（含 0°/315°）= 8。
		/// </summary>
		public static float DefaultVoidRadius(int rotation)
		{
			switch (rotation & 7)
			{
			case 1:
			case 4:
				return 4f;
			case 2:
			case 5:
				return 2f;
			case 3:
			case 6:
				return 12f;
			default:
				return 8f;
			}
		}

		/// <summary>斜向朝向（45/135/225/315）。bpsim 的蛋是四向件，只有导入的蓝图才可能出现。</summary>
		public static bool IsDiagonal(int rotation)
		{
			return (rotation & 7) >= 4;
		}

		/// <summary>BPSS <c>VoidDefaultOn()</c>：只有正交朝向（0/90/180/270）才默认开启。</summary>
		public static bool IsOrthogonal(int rotation)
		{
			return (rotation & 7) < 4;
		}
	}

	/// <summary>蛋对「别的实体」做过的改动种类，用于关闭时精确还原。</summary>
	public enum EggEffectKind : byte
	{
		/// <summary>穿透蛋关掉的碰撞体（存原 CollisionFilter）。</summary>
		Collider = 0,
		/// <summary>虚空蛋每帧关掉的碰撞体（存原 CollisionFilter）。</summary>
		VoidCollider = 1,
		/// <summary>失重蛋关掉的重力（只记录我们加过 PhysicsGravityFactor 的实体）。</summary>
		Gravity = 2,
		/// <summary>无质量蛋改过的质量（存原 InverseMass / InverseInertia）。</summary>
		Mass = 3,
		/// <summary>异形胶蛋改过的关节阈值（存原 MaxImpulse）。</summary>
		Joint = 4
	}

	/// <summary>
	/// 蛋对其它实体施加过、需要还原的效果。放在蛋实体自己的动态缓冲区里，
	/// 而不是系统里的托管字典 —— 这样存档/销毁/重建时状态自然跟着实体走。
	/// </summary>
	public struct EggEffect : IBufferElementData
	{
		public EggEffectKind Kind;

		public Entity Target;

		/// <summary>Collider 类效果的原碰撞过滤。</summary>
		public CollisionFilter Filter;

		/// <summary>Mass 类效果的原逆质量。</summary>
		public float InverseMass;

		/// <summary>Mass 类效果的原逆惯性。</summary>
		public float3 InverseInertia;

		/// <summary>Joint 类效果里被改的约束下标（0-2）。</summary>
		public int ConstraintIndex;

		/// <summary>Joint 类效果的原冲量事件阈值。</summary>
		public float3 MaxImpulse;
	}
}
