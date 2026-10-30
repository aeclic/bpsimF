using Unity.Entities;
using Unity.Physics;

namespace Bpsim.Parts.Gameplay
{
	/// <summary>
	/// 猪族（Pig / KingPig / GoldenPig）的玩法外壳。
	/// </summary>
	/// <remarks>
	/// <para>
	/// BPSS 里这三份脚本各有 60~750 行，但真正影响物理的只有两件事：
	/// ① <c>Initialize</c> / <c>FixedUpdate</c> 里设置 <c>Rigidbody.drag</c> 与 <c>angularDrag</c>；
	/// ② <c>CanBeEnclosed() =&gt; true</c>（bpsim 的 <c>BasePart.CanBeContained</c> 默认就允许，无需移植）。
	/// 其余 90% 的代码是表情状态机、瞳孔跟随、帽子/面具换图、呼吸与滚动音效、
	/// 碰撞星星粒子——bpsim 既没有表情图集（<c>Part_SpriteMap_0.txt</c> 里只有每只猪一张静态脸），
	/// 也没有音频/粒子层，所以按「降级迁移」只保留物理部分。
	/// </para>
	/// <para>
	/// 阻尼本身由 <see cref="Simulation.PigMotionSystem"/> 每步写进
	/// <see cref="PhysicsDamping"/>，这里只负责挂组件和上报状态。
	/// </para>
	/// </remarks>
	public sealed class PigBehavior : PartBehavior
	{
		/// <summary>BPSS Pig.Initialize 里没有写死 drag，用的是 BasePart 的默认 0；真实值在 FixedUpdate 里逐帧决定。</summary>
		private PigFamily m_family;

		/// <summary>表情状态机在原版里至少有 13 个枚举值，bpsim 无对应美术，这里只记录「本可以切」的次数。</summary>
		private int m_expressionRequests;

		public override void Init()
		{
			m_family = FamilyOf(Info.PartType);

			// 阻尼模型要的组件：PhysicsDamping 由系统逐帧写；PigMotion 存种类与观测值。
			PhysicsDamping damping = new PhysicsDamping
			{
				Linear = (m_family == PigFamily.Pig) ? 0.2f : 0.5f,
				Angular = (m_family == PigFamily.Pig) ? 0.05f : 1f
			};
			if (Em.HasComponent<PhysicsDamping>(Part))
			{
				Em.SetComponentData(Part, damping);
			}
			else
			{
				Em.AddComponentData(Part, damping);
			}

			PigMotion motion = new PigMotion
			{
				Family = m_family,
				PeakSpeed = 0f,
				Linear = damping.Linear,
				Angular = damping.Angular
			};
			if (Em.HasComponent<PigMotion>(Part))
			{
				Em.SetComponentData(Part, motion);
			}
			else
			{
				Em.AddComponentData(Part, motion);
			}

			Enabled = true;
			Manager.Log("pig#" + Info.PartIndex + ": family=" + m_family
				+ " linear=" + damping.Linear + " angular=" + damping.Angular);
		}

		/// <summary>
		/// BPSS 的猪没有 <c>OnTouch</c>，点击不会改变物理状态；
		/// 原版点击只会让它「叫一声 / 换个表情」，这里退化成计数。
		/// </summary>
		public override void Trigger()
		{
			m_expressionRequests++;
			Manager.Log("pig#" + Info.PartIndex + ": expression request #" + m_expressionRequests
				+ " skipped (no expression art in bpsim)");
		}

		public override string Describe()
		{
			PigMotion motion = default(PigMotion);
			PhysicsDamping damping = default(PhysicsDamping);
			if (Em.HasComponent<PigMotion>(Part))
			{
				motion = Em.GetComponentData<PigMotion>(Part);
			}
			if (Em.HasComponent<PhysicsDamping>(Part))
			{
				damping = Em.GetComponentData<PhysicsDamping>(Part);
			}
			// 注意：bpsim 里「被装进框」的信息是挂在容器上的（ContainedPart 由框指向被装进去的件），
			// 被装方自己没有反向索引，所以这里不猜 enclosed 状态。
			return "pig#" + Info.PartIndex
				+ " family=" + m_family
				+ " damping=" + damping.Linear.ToString("F3") + "/" + damping.Angular.ToString("F3")
				+ " peak=" + motion.PeakSpeed.ToString("F2")
				+ " expressions=" + m_expressionRequests;
		}

		private static PigFamily FamilyOf(PartType type)
		{
			switch (type)
			{
			case PartType.KingPig:
				return PigFamily.KingPig;
			case PartType.GoldenPig:
				return PigFamily.GoldenPig;
			default:
				return PigFamily.Pig;
			}
		}
	}
}
