using Unity.Entities;

namespace Bpsim.Parts.Gameplay
{
	/// <summary>
	/// BPSS <c>Pumpkin</c>（南瓜）：放下去就是「固定件」，点一下把整个焊接组件冻成静态体，
	/// 再点一下松开。
	/// </summary>
	/// <remarks>
	/// <para>
	/// 原版的行为全部依赖 <c>INFeature.FixedPumpkin</c> 开关（<c>INSettingsAExp.json</c> /
	/// <c>INSettingsB.json</c> 里是开着的）：关掉时南瓜只是装饰，<c>IsEnabled()</c> 恒 false、
	/// 点击无效。bpsim 没有这个设置项，按「开」处理。
	/// </para>
	/// <para>
	/// 两个开关项的分工：<c>FixedPumpkin</c> 决定南瓜能不能固定（见上）；
	/// <c>RotatablePumpkin</c> 只影响 <c>EffectDirection()</c> 与放置时的自动对齐，
	/// 那是建造期的朝向逻辑，跟这里的固定语义无关，所以 bpsim 只在注释里记下来。
	/// </para>
	/// <para>
	/// 真正改刚体的工作在 <see cref="Simulation.FixedPumpkinSystem"/> 里做，
	/// 这个类只负责点击翻转状态和置脏标记——保持 behavior 不直接搬动物理数据的惯例。
	/// </para>
	/// </remarks>
	public sealed class PumpkinBehavior : PartBehavior
	{
		public override void Init()
		{
			// BPSS Pumpkin.PostInitialize()：开启 FixedPumpkin 时 m_enabled = true，
			// 也就是「放下去的瞬间就已经固定」，玩家不需要先点一下。
			PumpkinPart pumpkin = new PumpkinPart
			{
				Enabled = 1,
				Applied = 0
			};
			if (Em.HasComponent<PumpkinPart>(Part))
			{
				Em.SetComponentData(Part, pumpkin);
			}
			else
			{
				Em.AddComponentData(Part, pumpkin);
			}
			Enabled = true;
			Simulation.FixedPumpkinSystem.MarkDirty();
		}

		public override void Trigger()
		{
			if (!Em.HasComponent<PumpkinPart>(Part))
			{
				return;
			}
			PumpkinPart pumpkin = Em.GetComponentData<PumpkinPart>(Part);
			pumpkin.Enabled = (byte)((pumpkin.Enabled != 0) ? 0 : 1);
			Em.SetComponentData(Part, pumpkin);
			Enabled = pumpkin.Enabled != 0;
			Simulation.FixedPumpkinSystem.MarkDirty();
			Manager.Log("pumpkin#" + Info.PartIndex + ": " + (Enabled ? "fixed (freeze component)" : "loose (restore mass)"));
		}

		public override string Describe()
		{
			byte applied = 0;
			if (Em.HasComponent<PumpkinPart>(Part))
			{
				applied = Em.GetComponentData<PumpkinPart>(Part).Applied;
			}
			return "pumpkin#" + Info.PartIndex + " enabled=" + Enabled + " applied=" + (applied != 0);
		}
	}
}
