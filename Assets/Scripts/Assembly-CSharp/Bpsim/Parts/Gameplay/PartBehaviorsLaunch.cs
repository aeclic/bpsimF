using System;
using System.Collections.Generic;
using Bpsim.Parts.Simulation;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

namespace Bpsim.Parts.Gameplay
{
	/// <summary>
	/// 推力部件的型号表：BPSS 901-17.0 把 CokeBottle / SodaBottle / Rocket / RedRocket
	/// 四族都挂在同一个 <c>Rocket</c>（BasePropulsion）脚本上，区别全在 prefab 上序列化的
	/// 字段和 <c>Initialize()</c> 乘进去的 INSettings 倍率：
	/// <code>
	/// prefab                     m_boostForce  m_maximumSpeed  ISettingB 倍率          实际
	/// Part_SodaBottle_01..04     35            10              CokeSoda 2.0 / 2.0      70 / 20
	/// Part_CokeBottle_01..04     35            10              CokeSoda 2.0 / 2.0      70 / 20
	/// Part_SodaBottle_05(alien)  150           150             AlienSoda 200 / 200     200 / 200
	/// Part_CokeBottle_05(alien)  35            25              AlienCoke 150 / ∞       150 / ∞
	/// Part_Rocket_01..,RedRocket 50            18              Rocket 2.0 / ∞          100 / ∞
	/// </code>
	/// bpsim 没有 INSettings，所以这里直接落 B 档（ISettingsB.json，即实际对局用的那份）乘完的结果。
	/// 汽水/可乐的 5 个皮肤 = PartIndex 0..4（Regular/Common/Rare/Epic/Legendary），
	/// 只有第 5 个（4）是外星版，力和限速都换表。
	/// </summary>
	internal readonly struct LaunchProfile
	{
		/// <summary>BPSS 里 RocketSpeed / AlienCokeSpeedValue 是 Infinity，见 <see cref="LaunchBehavior.UnlimitedSpeedCap"/>。</summary>
		public readonly float Force;

		public readonly float SpeedLimit;

		public LaunchProfile(float force, float speedLimit)
		{
			Force = force;
			SpeedLimit = speedLimit;
		}

		public static LaunchProfile Resolve(PartType type, int index)
		{
			switch (type)
			{
			case PartType.SodaBottle:
				// AlienSodaForceValue / AlienSodaSpeedValue = 200 / 200。
				return (index == 4) ? new LaunchProfile(200f, 200f) : new LaunchProfile(70f, 20f);
			case PartType.CokeBottle:
				// AlienCokeForceValue = 150，AlienCokeSpeedValue = ∞。
				return (index == 4) ? new LaunchProfile(150f, LaunchBehavior.UnlimitedSpeedCap) : new LaunchProfile(70f, 20f);
			case PartType.Rocket:
			case PartType.RedRocket:
				// RocketForce 2.0 → 100，RocketSpeed = ∞。
				return new LaunchProfile(100f, float.PositiveInfinity);
			default:
				return new LaunchProfile(0f, 0f);
			}
		}
	}

	/// <summary>
	/// 汽水 / 可乐 / 火箭 / 红火箭的推进行为（BPSS <c>Rocket.cs</c> 的 bpsim 等价物）。
	/// </summary>
	/// <remarks>
	/// <para>方向：BPSS prefab 上 <c>m_direction = (1,0,0)</c>，FixedUpdate 里
	/// <c>AddForceAtPosition(force * transform.TransformDirection(m_direction), pos, ForceMode.Force)</c>，
	/// 即沿部件局部 +X 推进；<c>EffectDirection()</c> 也是 <c>Rotate(Direction.Right, m_gridRotation)</c>。
	/// bpsim 的旧实现推的是 <c>Up()</c>（局部 +Y），而这四族的贴图全是横躺的，等于往天上怼。</para>
	/// <para>力度：BPSS 施加的是力（加速度 = 力 / 质量），所以这里按
	/// <c>ComponentThrust.TotalMass</c> 把力折算成整块焊件的 Δv，而不是直接累加固定速度。</para>
	/// <para>开关：ISettingsB 的 <c>SwitchableCokeSodaRocket = true</c>，此时 BPSS 的
	/// <c>OnTouch</c> 只做 <c>m_enabled = !m_enabled</c>，FixedUpdate 在 <c>m_enabled</c> 为真期间
	/// 一直加力（<c>m_ignitionTime</c>/<c>m_boostDuration</c>/<c>m_boostEndDuration</c> 只影响
	/// 粒子与 <c>m_boostUsed</c> 的可视化，不参与力的门控）。所以 bpsim 这里是「点击开关、
	/// 开着就一直出力」，不是旧实现的一次性点燃后烧完报废。</para>
	/// </remarks>
	public sealed partial class LaunchBehavior : PartBehavior
	{
		/// <summary>
		/// BPSS B 档的火箭和异形可乐不设推进速度上限。
		/// </summary>
		public const float UnlimitedSpeedCap = float.PositiveInfinity;

		private float m_force;

		private float m_speedLimit;

		private float m_elapsed;

		public override void Init()
		{
			LaunchProfile profile = LaunchProfile.Resolve(Info.PartType, Info.PartIndex);
			m_force = profile.Force;
			m_speedLimit = profile.SpeedLimit;
			Enabled = false;
			ResetGuidance();
		}

		public override void Trigger()
		{
			Enabled = !Enabled;
			ResetGuidance();
			if (Enabled)
			{
				m_elapsed = 0f;
			}
			Manager.Log("launch: " + (Enabled ? "on " : "off ") + Info.PartType + "#" + Info.PartIndex
				+ " force=" + m_force.ToString("F1") + " limit=" + m_speedLimit.ToString("F1"));
		}

		public override void Tick(float deltaTime)
		{
			if (!Enabled)
			{
				return;
			}
			m_elapsed += deltaTime;
			if (TickGuidance(deltaTime)) return;
			if (m_force <= 0f)
			{
				return;
			}
			float3 direction = PushDirection();
			float alongSpeed = math.dot(PartGameplayUtil.LinearVelocity(Part), direction);
			if (m_speedLimit > 0f && alongSpeed >= m_speedLimit)
			{
				// BPSS Rocket.LimitForceForSpeed 是「超过 m_maximumSpeed 后 force/(1+超出量)」的软衰减：
				// 原版是 2D 俯视图、阻力大，软衰减刚好把速度按在上限附近。bpsim 的阻尼小得多，
				// 同样的公式会一路漂到上限的 1.6 倍（实测 70/0.5kg 的汽水冲到 31.5，上限 20），
				// 所以这里把 m_maximumSpeed 兑现成真正的上限：到顶这一步就不再加力，
				// 靠阻尼掉回上限以下时自动续上，速度在上限附近来回而不是持续爬升。
				return;
			}
			int component = PartGameplayUtil.ConnectedComponent(Part);
			if (component < 0)
			{
				// 没进任何连通块（独立件）：只推自己。
				if (ComponentThrust.Movable(Part))
				{
					float inverseMass = PartGameplayUtil.Em.GetComponentData<PhysicsMass>(Part).InverseMass;
					if (inverseMass > 0f)
					{
						PartGameplayUtil.AddVelocity(Part, direction * (m_force * deltaTime * inverseMass));
					}
				}
				return;
			}
			float mass = ThrustBatch.MassOf(component);
			if (mass <= 0f)
			{
				// 整块焊件都是静态锚（木框）或零质量，推不动。
				return;
			}
			// 只登记，不在这里写速度：一 tick 里可能有上百个汽水同时开着，
			// 每个都遍历整台车写一遍会把帧率打死（见 ThrustBatch 的说明）。
			ThrustBatch.Add(component, direction * (m_force * deltaTime / mass));
		}

		/// <summary>
		/// 每 tick 的推力批处理。
		/// </summary>
		/// <remarks>
		/// <para>BPSS 的 <c>AddForce</c> 加在 contraption 的**一个**刚体上，所以「车上装 100 瓶汽水
		/// 全开」的代价和装 1 瓶一样，都是 O(1)。bpsim 每个部件一个刚体、靠关节连成一体，
		/// 要把 Δv 平摊给整块焊件才能不被关节撕开 —— 但原来那版是**每个瓶子的 Tick 各遍历一遍
		/// 全部部件**，即 O(开启瓶数 × 部件数)，而且循环里每取一件都要走
		/// <c>EntityManager</c> 的 <c>Exists/HasComponent/GetComponentData</c>：</para>
		/// <code>
		/// 不朽荣光：114 个汽水 × 1648 个部件 × 约 6 次 EntityManager 调用 ≈ 每 tick 110 万次
		/// </code>
		/// <para>实测现象就是「点燃汽水后过一会儿帧率掉到几乎不动」（编辑器 Play 里），
		/// 而「bpsim1号机」一瓶汽水都没有所以看不出问题。</para>
		/// <para>现在改成两段式：<c>FixedUpdate</c> 里先 <see cref="Begin" />，
		/// 各瓶只把自己的 Δv 累加到所属连通块上，最后 <see cref="Apply" /> 一次性
		/// 遍历部件、按块写一次速度。每 tick 的托管层调用从 110 万次降到约 5 千次。</para>
		/// </remarks>
		internal static class ThrustBatch
		{
			private static int s_stamp;
			private static int s_builtStamp = -1;
			private static int s_count;
			private static Entity[] s_parts = Array.Empty<Entity>();
			private static int[] s_component = Array.Empty<int>();
			private static readonly Dictionary<int, float> s_mass = new Dictionary<int, float>();
			private static readonly Dictionary<int, float3> s_delta = new Dictionary<int, float3>();
			private static readonly List<int> s_touched = new List<int>();

			/// <summary>一 tick 开始时调用：翻新批次标记，清掉上一 tick 的累加值。</summary>
			public static void Begin()
			{
				s_stamp++;
				s_delta.Clear();
				s_touched.Clear();
			}

			/// <summary>某连通块的可动总质量（kg）。0 表示整块都是静态锚。</summary>
			public static float MassOf(int component)
			{
				EnsureBuilt();
				float mass;
				return s_mass.TryGetValue(component, out mass) ? mass : 0f;
			}

			/// <summary>把某瓶这一步的 Δv 累加到它所属连通块上。</summary>
			public static void Add(int component, float3 delta)
			{
				if (component < 0)
				{
					return;
				}
				float3 current;
				if (s_delta.TryGetValue(component, out current))
				{
					s_delta[component] = current + delta;
					return;
				}
				s_delta.Add(component, delta);
				s_touched.Add(component);
			}

			/// <summary>一 tick 结束时调用：每个连通块只遍历一次部件、只写一次速度。</summary>
			public static void Apply()
			{
				if (s_touched.Count == 0)
				{
					return;
				}
				EnsureBuilt();
				for (int i = 0; i < s_count; i++)
				{
					float3 delta;
					if (!s_delta.TryGetValue(s_component[i], out delta))
					{
						continue;
					}
					PartGameplayUtil.AddVelocity(s_parts[i], delta);
				}
			}

			/// <summary>
			/// 每 tick 重建一次「部件 → 连通块 / 块质量」索引，O(部件数) 且只做一次。
			/// 不能跨 tick 缓存：拆件、焊缝断裂都会换连通块编号。
			/// </summary>
			private static void EnsureBuilt()
			{
				if (s_builtStamp == s_stamp)
				{
					return;
				}
				s_builtStamp = s_stamp;
				s_mass.Clear();
				s_count = 0;
				PartManager manager = PartManager.Instance;
				if (manager == null || manager.PartSimulator == null)
				{
					return;
				}
				PartSimulatorUnmanaged simulator = manager.PartSimulator.Unmanaged;
				int length = simulator.Parts.Length;
				if (s_parts.Length < length)
				{
					s_parts = new Entity[length];
					s_component = new int[length];
				}
				EntityManager em = PartGameplayUtil.Em;
				for (int i = 0; i < length; i++)
				{
					Entity entity = simulator.Parts[i];
					s_parts[i] = entity;
					s_component[i] = -1;
					if (!ComponentThrust.Movable(entity))
					{
						continue;
					}
					int component = PartGameplayUtil.ConnectedComponent(entity);
					if (component < 0)
					{
						continue;
					}
					s_component[i] = component;
					float inverseMass = em.GetComponentData<PhysicsMass>(entity).InverseMass;
					if (inverseMass > 0f)
					{
						float mass;
						s_mass.TryGetValue(component, out mass);
						s_mass[component] = mass + 1f / inverseMass;
					}
				}
				s_count = length;
			}
		}

		/// <summary>
		/// 推力方向 = 零件在网格里的朝向（BPSS 用 <c>Rotate(Direction.Right, m_gridRotation)</c>）。
		/// 不用 <c>Forward()</c>（LocalToWorld/LocalTransform）的原因：部件的物理实体与渲染实体是分开的，
		/// 网格朝向记在 <see cref="PartTransform" /> 上，实体旋转只在 PartTransformSystem 里逐帧同步，
		/// 二者在放置当帧、或 LocalToWorld 还没刷新时并不一致（实测放下 rot=1 的部件读 LocalToWorld 仍是 0°）。
		/// </summary>
		private float3 PushDirection()
		{
			return math.rotate(PartVariantPhysics.Rotation(Part), new float3(1, 0, 0));
		}

		public override string Describe()
		{
			float3 direction = PushDirection();
			float3 velocity = PartGameplayUtil.LinearVelocity(Part);
			return "launch enabled=" + Enabled + " force=" + m_force.ToString("F1")
				+ " limit=" + m_speedLimit.ToString("F1")
				+ " target=" + Target.Index + " guidance=" + LastGuidanceForce
				+ " t=" + m_elapsed.ToString("F2")
				+ " dir=(" + direction.x.ToString("F2") + "," + direction.y.ToString("F2") + ")"
				+ " v=(" + velocity.x.ToString("F2") + "," + velocity.y.ToString("F2") + ")";
		}
	}
}
