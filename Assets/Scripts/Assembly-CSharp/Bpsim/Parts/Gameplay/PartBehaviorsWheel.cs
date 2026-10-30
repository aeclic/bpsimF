using Unity.Entities;

namespace Bpsim.Parts.Gameplay
{
	/// <summary>
	/// 轮子的开关行为。真正的推力、射线与视觉滚动在 ECS 系统
	/// <c>Bpsim.Parts.Simulation.WheelSystem</c> 里，这里只负责「玩家点击 → 切换 <see cref="Wheel.Enabled" />」。
	/// </summary>
	/// <remarks>
	/// BPSS 的 <c>MotorWheel.OnTouch()</c> 就是 <c>SetEnabled(!m_enabled)</c>；
	/// <c>StickyWheel</c> / <c>OffRoadWheel</c> 继承同一套。<c>CartWheel</c> / <c>NormalWheel</c> /
	/// <c>SmallWheel</c>（除动力款 #7）没有 <c>HasOnOffToggle</c>，所以它们不会走到这里。
	/// </remarks>
	public sealed class WheelBehavior : PartBehavior
	{
		public override void Init()
		{
			// 开局默认关闭，与 BPSS 的 m_enabled 默认 false 一致。
			if (Em.HasComponent<Wheel>(Part))
			{
				Wheel wheel = Em.GetComponentData<Wheel>(Part);
				Enabled = wheel.Enabled;
			}
		}

		public override void Trigger()
		{
			if (!Em.HasComponent<Wheel>(Part))
			{
				return;
			}
			Wheel wheel = Em.GetComponentData<Wheel>(Part);
			wheel.Enabled = !wheel.Enabled;
			// BPSS 在 SetEnabled 里同时把推力斜坡清零，重新开启时从 0 爬升。
			wheel.ThrottleTimer = 0f;
			wheel.Throttle = 0f;
			Em.SetComponentData(Part, wheel);
			Enabled = wheel.Enabled;
		}

		public override string Describe()
		{
			if (!Em.HasComponent<Wheel>(Part))
			{
				return "wheel enabled=" + Enabled + " (component missing)";
			}
			Wheel wheel = Em.GetComponentData<Wheel>(Part);
			return "wheel " + wheel.Kind + " enabled=" + wheel.Enabled
				+ " power=" + wheel.PowerFactor.ToString("F2")
				+ " maxSpeed=" + wheel.MaximumSpeed.ToString("F2")
				+ " throttle=" + wheel.Throttle.ToString("F2")
				+ " contact=" + wheel.HasContact
				+ " spin=" + wheel.SpinSpeed.ToString("F2");
		}
	}

	/// <summary>
	/// 齿轮箱的开关行为。BPSS 的 <c>Gearbox.OnTouch()</c> 只翻转 <c>activated</c> 并换贴图，
	/// 倒车由同一焊接组件里的轮子读取状态后自行取反。
	/// </summary>
	public sealed class GearboxBehavior : PartBehavior
	{
		public override void Init()
		{
			if (Em.HasComponent<Gearbox>(Part))
			{
				Enabled = Em.GetComponentData<Gearbox>(Part).Activated;
			}
		}

		public override void Trigger()
		{
			if (!Em.HasComponent<Gearbox>(Part))
			{
				return;
			}
			Gearbox gearbox = Em.GetComponentData<Gearbox>(Part);
			gearbox.Activated = !gearbox.Activated;
			gearbox.AchievementSent = true;
			Em.SetComponentData(Part, gearbox);
			Enabled = gearbox.Activated;
		}

		public override string Describe()
		{
			if (!Em.HasComponent<Gearbox>(Part))
			{
				return "gearbox activated=" + Enabled + " (component missing)";
			}
			return "gearbox activated=" + Em.GetComponentData<Gearbox>(Part).Activated;
		}
	}
}
