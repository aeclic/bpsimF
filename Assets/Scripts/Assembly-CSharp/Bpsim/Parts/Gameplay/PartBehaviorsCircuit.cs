using Bpsim.Parts;
using Bpsim.Parts.Circuits;
using Unity.Entities;

namespace Bpsim.Parts.Gameplay
{
	/// <summary>
	/// 开关类部件的点击行为：Switch / SPDTSwitch（ElectricalPart 4-5）、Vcc（21）、
	/// 机械闸门（MechanicalPart 0-4）。
	///
	/// BPSS 侧对应 <c>SwitchPart.OnTouch</c> / <c>SPDTSwitchPart.OnTouch</c> / <c>VccPart.OnTouch</c> /
	/// <c>MechanicalGate.OnTouch</c>：它们都只翻转一个布尔状态，真正的电学效果由下一帧的
	/// <c>ElectricalSystem</c> 求解、机械效果由 <c>MechanicalGate</c> 自己驱动碰撞体。
	/// 这里同样只翻状态：<see cref="CircuitPart.Closed"/> 交给 <see cref="Simulation.CircuitSystem"/> 求解，
	/// <see cref="MechanicalGate.Enabled"/> 交给 <see cref="Simulation.MechanicalGateSystem"/> 展开/收回。
	/// </summary>
	public sealed class CircuitSwitchBehavior : PartBehavior
	{
		public override void Init()
		{
			if (Em.HasComponent<MechanicalGate>(Part))
			{
				Enabled = Em.GetComponentData<MechanicalGate>(Part).Enabled;
			}
			else if (Em.HasComponent<CircuitPart>(Part))
			{
				Enabled = Em.GetComponentData<CircuitPart>(Part).Closed;
			}
		}

		public override void Trigger()
		{
			// 机械闸门：BPSS <c>MechanicalGate.Toggle()</c> —— 翻转 enabled 并把缓动区间
			// 重设为 (当前长度 → 目标长度 或 0)。
			if (Em.HasComponent<MechanicalGate>(Part))
			{
				MechanicalGate gate = Em.GetComponentData<MechanicalGate>(Part);
				gate.Enabled = !gate.Enabled;
				gate.SetController(gate.Value, gate.Enabled ? gate.TargetLength : 0f);
				Em.SetComponentData(Part, gate);
				Enabled = gate.Enabled;
				Manager.Log(Describe());
				return;
			}
			if (!Em.HasComponent<CircuitPart>(Part))
			{
				return;
			}
			CircuitPart element = Em.GetComponentData<CircuitPart>(Part);
			if (Info.PartIndex == 11)
			{
				CircuitControls.Show(Part, false);
				return;
			}
			if (Info.PartIndex == 46 || Info.PartIndex == 47)
			{
				element.Mode = (byte)(element.Mode == 0 ? 1 : 0);
				Em.SetComponentData(Part, element);
				if (Info.PartIndex == 47 && element.Mode != 0) CircuitControls.Show(Part, true);
				Enabled = element.Mode != 0;
				return;
			}
			element.Closed = !element.Closed;
			Em.SetComponentData(Part, element);
			Enabled = element.Closed;
			Manager.Log(Describe());
		}

		public override string Describe()
		{
			return "circuit " + Info.PartType + "#" + Info.PartIndex + " closed=" + Enabled;
		}
	}
}
