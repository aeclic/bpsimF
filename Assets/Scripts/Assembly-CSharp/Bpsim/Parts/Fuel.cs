using Unity.Entities;

namespace Bpsim.Parts
{
	/// <summary>
	/// 燃料管类型，对应 BPSS <c>FuelTube.TubeType</c>。
	/// </summary>
	public enum FuelTubeKind : byte
	{
		/// <summary>普通管：只沿自身轴向导通。</summary>
		Common = 0,
		/// <summary>自动管：四个方向都导通，并按邻居自动换图/转角度。</summary>
		Auto = 1,
		/// <summary>阀门：可点击开关，关掉即断开。</summary>
		Valve = 2
	}

	/// <summary>
	/// 燃料箱（BPSS <c>FuelBox</c> 的容器一侧）。
	/// </summary>
	/// <remarks>
	/// <para>
	/// BPSS 里燃料箱是「装在铁框里」的零件，靠 <c>Contraption.FindPartJoints</c> 找到铁框、
	/// 再取铁框的 <c>m_enclosedPart</c> 来确定自己连着谁。bpsim 没有 <c>m_enclosedPart</c> 的
	/// 逐一对应物，但有等价的两条信息：<see cref="ContainedPart"/>（框指向被装进去的件）
	/// 与 <see cref="PartConnectedComponent"/>（焊接组件）。燃料连通图因此按「同一焊接组件 +
	/// 网格四邻域」重建，见 <c>Parts/Simulation/FuelSystem.cs</c>。
	/// </para>
	/// <para>
	/// 数值直接照抄 BPSS：容量 4（斜向无限型号是 1e10）、供油速率
	/// <c>min(amount, 1 * fixedDeltaTime)</c>、质量 <c>2 + min(fuel, 4)</c>。
	/// </para>
	/// </remarks>
	public struct FuelContainer : IComponentData
	{
		/// <summary>当前油量。</summary>
		public float Amount;

		/// <summary>容量上限（BPSS 的 <c>m_maxFuelAmount</c>）。</summary>
		public float Capacity;

		/// <summary>本步供出的油量，只给液面/质量这类视觉物理用。</summary>
		public float SuppliedStep;

		/// <summary>非 0 表示无限油型号（BPSS 把容量设成 1e10 的那几个朝向）。</summary>
		public byte Infinite;
	}

	/// <summary>
	/// 耗油部件（BPSS <c>IFuelConsumer</c>，实现者只有 <c>JetEngine</c>）。
	/// </summary>
	/// <remarks>
	/// <c>Required</c> 由耗油方每步写入（喷气发动机按其推力换算），
	/// <c>Supplied</c> 由 <c>FuelSystem</c> 回填，耗油方据此判断「这一步有没有油」。
	/// 发动机没油时必须自己熄灭（BPSS <c>ConsumeFuel</c> 里 <c>SetEnabled(false)</c>），
	/// 因为燃料系统不该替零件决定开关状态。
	/// </remarks>
	public struct FuelConsumer : IComponentData
	{
		/// <summary>本步想要的油量（每步由耗油方改写）。</summary>
		public float Required;

		/// <summary>本步实际拿到的油量（燃料系统回填）。</summary>
		public float Supplied;

		/// <summary>非 0 表示本步在耗油（关闭的发动机不占配额）。</summary>
		public byte Requesting;
	}

	/// <summary>
	/// 燃料管（BPSS <c>FuelTube</c>）：决定燃料连通图怎么连。
	/// </summary>
	public struct FuelTubeLink : IComponentData
	{
		/// <summary>见 <see cref="FuelTubeKind"/>。</summary>
		public byte Kind;

		/// <summary>阀门是否打开（只有 <see cref="FuelTubeKind.Valve"/> 用）。</summary>
		public byte Open;
	}

	/// <summary>
	/// 燃料部件所属的连通组编号（-1 表示没接入任何组）。
	/// </summary>
	public struct FuelGroupIndex : IComponentData
	{
		public int Value;
	}

	/// <summary>
	/// 单个燃料连通组的每步统计，供调试面板 / QA 读取。
	/// </summary>
	public struct FuelGroupState
	{
		public int Consumers;

		public int Containers;

		public float Required;

		public float ExpectedSupply;

		public float RealSupply;

		/// <summary>本步实际供油比例 <c>min(ExpectedSupply / Required, 1)</c>。</summary>
		public float Ratio;
	}

	/// <summary>
	/// 燃料箱的液面子物体标记（子物体名 <c>FuelSprite</c>）。
	/// 液面高度按 <c>FuelContainer.Amount / Capacity</c> 缩放 + 下移，对应 BPSS
	/// <c>FuelBox</c> 对 <c>m_fuelSprite</c> 的 localScale/localPosition 操作。
	/// 做成标记组件而不是运行时按名字找子物体：那需要每帧遍历 LinkedEntityGroup 并比较
	/// FixedString，纯属把构建期信息留到运行期解析。
	/// </summary>
	public struct FuelLevelVisual : IComponentData
	{
	}

	/// <summary>
	/// 燃料管的贴图子物体标记（子物体名 <c>TubeVisual</c>）。
	/// 自动管要按邻居组合换图，换图就是写这个实体上的 <c>SpriteID</c>。
	/// </summary>
	public struct FuelTubeVisual : IComponentData
	{
	}
}
