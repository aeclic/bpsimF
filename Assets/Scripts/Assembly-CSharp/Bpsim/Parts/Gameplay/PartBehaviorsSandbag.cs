using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

namespace Bpsim.Parts.Gameplay
{
	/// <summary>
	/// 沙袋的「点击放手」，对应 BPSS 901-17.0 <c>Sandbag.OnTouch()</c> → <c>Drop()</c>。
	/// </summary>
	/// <remarks>
	/// <para>
	/// 原版 <c>Drop()</c> 做四件事：<c>m_dropped = true</c>、销毁 <c>SpringJoint</c>（连带
	/// <c>LineRenderer</c> 绳线）、给上级件 <c>AddForce(5f * Vector3.up, Impulse)</c>、
	/// 给自己 <c>AddForce(-4f * Vector3.up, Impulse)</c>、切到 <c>DroppedSandbag</c> 层。
	/// </para>
	/// <para>
	/// ECS 侧「销毁弹簧」= 把 <see cref="Sandbag.Anchor" /> 清成 <see cref="Entity.Null" />
	/// （<c>SandbagSystem</c> 见 Anchor 为空就不再施加拉力）；两个 <c>Impulse</c> 直接换算成
	/// 速度增量 <c>impulse * InverseMass</c>（这正是 Unity <c>ForceMode.Impulse</c> 的定义）。
	/// 层切换在 bpsim 里没有对应物（碰撞过滤走 <c>CollisionFilter</c>），只记状态。
	/// </para>
	/// <para>
	/// BPSS 的 <c>GetTriggerButtonInfo()</c> 只在 <c>IsAttached()</c> 时才给出按钮，
	/// 所以放手之后本 behavior 把自己标成 <c>Enabled = false</c>，再次触发直接忽略。
	/// </para>
	/// </remarks>
	public sealed class SandbagBehavior : PartBehavior
	{
		public override void Init()
		{
			if (Em.HasComponent<Sandbag>(Part))
			{
				Enabled = !Em.GetComponentData<Sandbag>(Part).Dropped;
			}
		}

		public override void Trigger()
		{
			if (!Em.HasComponent<Sandbag>(Part))
			{
				return;
			}
			Sandbag bag = Em.GetComponentData<Sandbag>(Part);
			if (bag.Dropped)
			{
				return;
			}
			Entity anchor = bag.Anchor;
			bag.Dropped = true;
			bag.Anchor = Entity.Null;
			Em.SetComponentData(Part, bag);
			if (anchor != Entity.Null)
			{
				ApplyImpulse(anchor, new float3(0f, Sandbag.DropImpulseOnAnchor, 0f));
				ApplyImpulse(Part, new float3(0f, Sandbag.DropImpulseOnSelf, 0f));
			}
			Enabled = false;
			Manager.Log("sandbag#" + Info.PartIndex + " dropped anchor=" + anchor);
		}

		public override string Describe()
		{
			if (!Em.HasComponent<Sandbag>(Part))
			{
				return "sandbag (component missing)";
			}
			Sandbag bag = Em.GetComponentData<Sandbag>(Part);
			return "sandbag balloons=" + bag.Balloons + " dropped=" + bag.Dropped + " anchor=" + bag.Anchor + " max=" + bag.MaxDistance;
		}

		/// <summary>Unity <c>ForceMode.Impulse</c>：速度增量 = 冲量 × 逆质量。</summary>
		private void ApplyImpulse(Entity entity, float3 impulse)
		{
			if (entity == Entity.Null || !Em.HasComponent<PhysicsVelocity>(entity) || !Em.HasComponent<PhysicsMass>(entity))
			{
				return;
			}
			PhysicsMass mass = Em.GetComponentData<PhysicsMass>(entity);
			if (mass.InverseMass <= 0f)
			{
				return;
			}
			PhysicsVelocity velocity = Em.GetComponentData<PhysicsVelocity>(entity);
			velocity.Linear += impulse * mass.InverseMass;
			Em.SetComponentData(entity, velocity);
		}
	}
}
