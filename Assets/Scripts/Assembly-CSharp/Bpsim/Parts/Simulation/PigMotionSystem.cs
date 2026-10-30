using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

namespace Bpsim.Parts.Simulation
{
	/// <summary>
	/// 猪族运动系统：把 BPSS 三份 MonoBehaviour 里的「阻尼模型」搬成 ECS。
	/// </summary>
	/// <remarks>
	/// <para>
	/// BPSS 用的是 Unity 的 <c>Rigidbody.drag / angularDrag</c>。在 ECS 侧对应物就是
	/// <see cref="PhysicsDamping"/>——它由求解器在速度积分时直接使用，所以我们只要把数值写对，
	/// 不需要每帧手动缩放 <see cref="PhysicsVelocity"/>（那样会和焊接约束、重力积分打架）。
	/// </para>
	/// <para>
	/// 普通猪的阻尼是<b>速度相关</b>的（<c>0.2 + 2.5 * (1 - speed)</c>，仅在 speed &lt; 1 时），
	/// 因此每步都要重算；猪王 / 金猪是常数，但同样走同一条路径，避免两套代码。
	/// 猪的数量很少（一局几十只），这个作业的开销可以忽略。
	/// </para>
	/// <para>
	/// 另附 <see cref="PartLevitation"/> 的等价实现：每步给刚体加一个恒定的向上冲量。
	/// 原版用 <c>Rigidbody.AddForce(Vector3.up * force, ForceMode.Force)</c>，
	/// 也就是每步 <c>force * dt / mass</c> 的速度增量。
	/// </para>
	/// </remarks>
	[UpdateInGroup(typeof(PartSimulationSystemGroup))]
	internal partial class PigMotionSystem : SystemBase
	{
		/// <summary>BPSS Pig.FixedUpdate：慢速区上限（m/s）。</summary>
		private const float SlowSpeed = 1f;

		/// <summary>BPSS Pig.FixedUpdate：慢速区的基线阻尼。</summary>
		private const float PigBaseLinear = 0.2f;

		/// <summary>BPSS Pig.FixedUpdate：快速区的角阻尼。</summary>
		private const float PigBaseAngular = 0.05f;

		/// <summary>BPSS Pig.FixedUpdate：慢速区的额外阻尼系数。</summary>
		private const float SlowBonus = 2.5f;

		/// <summary>BPSS KingPig / GoldenPig 的 Initialize 常量。</summary>
		private const float HeavyLinear = 0.5f;

		private const float HeavyAngular = 1f;

		/// <summary>每步重算猪族阻尼。</summary>
		[BurstCompile]
		private partial struct DampingJob : IJobEntity
		{
			public float DeltaTime;

			private void Execute(ref PigMotion motion, ref PhysicsDamping damping, in PhysicsVelocity velocity)
			{
				float speed = math.length(velocity.Linear);
				motion.PeakSpeed = math.max(motion.PeakSpeed, speed);
				float linear;
				float angular;
				if (motion.Family == PigFamily.Pig)
				{
					if (speed < SlowSpeed)
					{
						float bonus = SlowBonus * (1f - speed);
						linear = PigBaseLinear + bonus;
						angular = PigBaseLinear + bonus;
					}
					else
					{
						linear = PigBaseLinear;
						angular = PigBaseAngular;
					}
				}
				else
				{
					linear = HeavyLinear;
					angular = HeavyAngular;
				}
				if (math.abs(damping.Linear - linear) > 1E-05f || math.abs(damping.Angular - angular) > 1E-05f)
				{
					damping.Linear = linear;
					damping.Angular = angular;
				}
				motion.Linear = damping.Linear;
				motion.Angular = damping.Angular;
			}
		}

		/// <summary>每步给带 <see cref="PartLevitation"/> 的刚体加向上冲量。</summary>
		[BurstCompile]
		private partial struct LevitationJob : IJobEntity
		{
			public float DeltaTime;

			private void Execute(in PartLevitation levitation, in PhysicsMass mass, ref PhysicsVelocity velocity)
			{
				// 静态体（InverseMass = 0）加了也没用，直接跳过。
				if (mass.InverseMass <= 0f)
				{
					return;
				}
				float force = levitation.Force;
				velocity.Linear += new float3(0f, force * mass.InverseMass * DeltaTime, 0f);
			}
		}

		protected override void OnUpdate()
		{
			PartManager manager = PartManager.Instance;
			if (manager == null || !manager.IsSimulating || manager.PartSimulator == null)
			{
				return;
			}
			float deltaTime = World.Time.DeltaTime;
			base.Dependency = new DampingJob
			{
				DeltaTime = deltaTime
			}.ScheduleParallel(base.Dependency);
			base.Dependency = new LevitationJob
			{
				DeltaTime = deltaTime
			}.ScheduleParallel(base.Dependency);
		}
	}
}
