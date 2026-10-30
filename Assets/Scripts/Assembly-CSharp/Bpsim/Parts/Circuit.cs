using Unity.Entities;
using Unity.Mathematics;

namespace Bpsim.Parts
{
	/// <summary>
	/// 电路元件种类。<see cref="PartType.ElectricalPart"/>（0-48）由持久电路模型与 ECS 桥接统一求解。
	/// 索引映射照抄 BPSS 901-17.0 的 prefab 顺序（见 BPSIM_CIRCUIT_PORT.md）。
	/// </summary>
	public enum CircuitKind : byte
	{
		None = 0,
		/// <summary>导线（0-3）：固定导线支持直线、弯线和两条独立的交叉通道。</summary>
		Wire = 1,
		/// <summary>开关（4-5）：闭合时两端短接。</summary>
		Switch = 2,
		/// <summary>电阻（6-11）。</summary>
		Resistor = 3,
		/// <summary>电源 / 电压源（12-14）。</summary>
		PowerSource = 4,
		/// <summary>电容（15-17）。</summary>
		Capacitor = 5,
		/// <summary>电压表（18）。</summary>
		Voltmeter = 6,
		/// <summary>电流表（19）。</summary>
		Ammeter = 7,
		/// <summary>接地（20）：把所在节点接到地。</summary>
		Ground = 8,
		/// <summary>Vcc（21）：单端子 5V 电源，带内阻，点击开关。</summary>
		Vcc = 9,
		/// <summary>集成电路（22-37）。</summary>
		IntegratedCircuit = 10,
		/// <summary>接口（38-39）。</summary>
		Interface = 11,
		/// <summary>无线输电（40-41）。</summary>
		PowerTransmitter = 12,
		/// <summary>点电荷（42）。</summary>
		PointCharge = 13,
		/// <summary>电场发生器（43）。</summary>
		ElectricField = 14,
		/// <summary>磁场发生器（44）。</summary>
		MagneticField = 15,
		/// <summary>触发器（45、48）。</summary>
		PartTrigger = 16,
		/// <summary>压力传感器（46）。</summary>
		PressureSensor = 17,
		/// <summary>数字显示屏（47）。</summary>
		DigitalDisplay = 18,
	}

	/// <summary>
	/// 一个电路部件的 ECS 参数与读数。持久 CircuitDevice 保存实际电极、连接和电容/延迟状态，
	/// 求解结果回写到 <see cref="Voltage1"/>/<see cref="Voltage2"/>/<see cref="Current"/>，消费者（导线发光、
	/// 表头读数、闸门开合）只读这几个字段。
	/// </summary>
	public struct CircuitPart : IComponentData, IQueryTypeParameter
	{
		public CircuitKind Kind;

		public int Index;
		public byte Mode;
		public int DisplayValue;
		public float Pressure;
		public bool Invalid;
		public bool SolveFailed;
		public byte BurnedPorts;
		public bool Grounded1;
		public bool Grounded2;

		/// <summary>默认本地端子掩码；运行时 CircuitDevice 更新为世界方向掩码，建造连接使用 GetTerminals。</summary>
		public byte TerminalMask;

		/// <summary>开关 / Vcc / 闸门之类的开关状态。</summary>
		public bool Closed;

		/// <summary>主参数：电阻值、电容值、电源电压。</summary>
		public float ValueA;

		/// <summary>次参数：内阻。</summary>
		public float ValueB;

		/// <summary>电容电荷（C·V），跨帧累积。</summary>
		public double Charge;

		/// <summary>求解结果：第一端子对地电压。</summary>
		public float Voltage1;

		/// <summary>求解结果：第二端子对地电压。</summary>
		public float Voltage2;

		/// <summary>求解结果：流过元件的电流（A）。</summary>
		public float Current;

		/// <summary>两端电压差（V），表头与发光都用它。</summary>
		public float Voltage => Voltage1 - Voltage2;

		public bool IsIndicator => Kind == CircuitKind.Voltmeter || Kind == CircuitKind.Ammeter;

		/// <summary>按 BPSS 的 prefab 索引建元件（数值取自 ResistorPart/PowerSourcePart/CapacitorPart/VccPart 的默认表）。</summary>
		public static CircuitPart Create(PartTypeInfo info)
		{
			CircuitPart result = default(CircuitPart);
			result.Closed = false;
			int index = info.PartIndex;
			if (index < 0)
			{
				index = 0;
			}
			result.Index = index;
			if (index <= 3)
			{
				// Fixed wires use seven routing modes; their actual ports are built by CircuitDevice.
				result.Kind = CircuitKind.Wire;
				result.TerminalMask = (byte)((index == 1 || index == 3) ? BitDirection.LeftAndRight : BitDirection.Any);
			}
			else if (index == 4)
			{
				result.Kind = CircuitKind.Switch;
				result.TerminalMask = (byte)BitDirection.LeftAndRight;
			}
			else if (index == 5)
			{
				// Pole=Left, Throw1=Up, Throw2=Down; false selects Throw1.
				result.Kind = CircuitKind.Switch;
				result.TerminalMask = (byte)(BitDirection.Left | BitDirection.Up | BitDirection.Down);
			}
			else if (index <= 11)
			{
				result.Kind = CircuitKind.Resistor;
				result.TerminalMask = (byte)BitDirection.LeftAndRight;
				// ResistorPart.CurrentResistorType 0..5 → 0.01 / 0.1 / 1 / 10 / 100 / 1(可调)
				float[] table = { 0.01f, 0.1f, 1f, 10f, 100f, 1f };
				result.ValueA = table[math.clamp(index - 6, 0, 5)];
			}
			else if (index <= 14)
			{
				result.Kind = CircuitKind.PowerSource;
				result.TerminalMask = (byte)BitDirection.LeftAndRight;
				float voltage = index switch
				{
					12 => 1f,
					13 => 5f,
					_ => 50f,
				};
				result.ValueA = voltage;
				// PowerSourcePart: 内阻 = max(0.05, V / 500)
				result.ValueB = math.max(0.05f, voltage / 500f);
			}
			else if (index <= 17)
			{
				result.Kind = CircuitKind.Capacitor;
				result.TerminalMask = (byte)BitDirection.LeftAndRight;
				switch (index)
				{
				case 15:
					result.ValueA = 0.1f;
					result.ValueB = 1f;
					break;
				case 16:
					result.ValueA = 1f;
					result.ValueB = 0.1f;
					break;
				default:
					result.ValueA = 10f;
					result.ValueB = 0.01f;
					break;
				}
			}
			else if (index == 18 || index == 19)
			{
				result.Kind = (index == 18) ? CircuitKind.Voltmeter : CircuitKind.Ammeter;
				result.TerminalMask = (byte)BitDirection.LeftAndRight;
				// 表头内阻：电压表很高、电流表很低（BPSS IndicatorPart 的取值）
				result.ValueA = (index == 18) ? 1000000f : 0f;
			}
			else if (index == 20)
			{
				result.Kind = CircuitKind.Ground;
				result.TerminalMask = (byte)BitDirection.Up;
				result.ValueA = 0f;
			}
			else if (index == 21)
			{
				result.Kind = CircuitKind.Vcc;
				result.TerminalMask = (byte)BitDirection.Right;
				// 关闭：0V / 0Ω；打开：5V / 0.05Ω（VccPart.OnTouch）
				result.ValueA = 0f;
				result.ValueB = 0f;
			}
			else if (index <= 37)
			{
				result.Kind = CircuitKind.IntegratedCircuit;
				result.TerminalMask = (byte)BitDirection.Any;
			}
			else if (index <= 39)
			{
				result.Kind = CircuitKind.Interface;
				result.TerminalMask = (byte)BitDirection.Any;
			}
			else if (index <= 41)
			{
				result.Kind = CircuitKind.PowerTransmitter;
				result.TerminalMask = (byte)(index == 40 ? BitDirection.Down : BitDirection.Up);
			}
			else if (index == 42)
			{
				result.Kind = CircuitKind.PointCharge;
				result.TerminalMask = 0;
				result.ValueA = 20f;
				result.Closed = true;
			}
			else if (index == 43)
			{
				result.Kind = CircuitKind.ElectricField;
				result.TerminalMask = 0;
				result.ValueA = 2f;
				result.Closed = true;
			}
			else if (index == 44)
			{
				result.Kind = CircuitKind.MagneticField;
				result.TerminalMask = 0;
				result.ValueA = 0.02f;
				result.Closed = true;
			}
			else if (index == 45 || index == 48)
			{
				result.Kind = CircuitKind.PartTrigger;
				result.TerminalMask = (byte)BitDirection.Left;
			}
			else if (index == 46)
			{
				result.Kind = CircuitKind.PressureSensor;
				result.TerminalMask = (byte)BitDirection.LeftAndRight;
				result.ValueA = 10000f;
			}
			else
			{
				result.Kind = CircuitKind.DigitalDisplay;
				result.TerminalMask = (byte)BitDirection.Any;
			}
			return result;
		}

		/// <summary>端子掩码随部件旋转（BPSS 里是 <c>GetConnectionDirection().Rotate(gridRotation)</c>）。</summary>
		public static BitDirection RotateMask(byte mask, int rotation)
		{
			return ((BitDirection)mask).Rotate(rotation);
		}

		// Also used while building physical joints: electrical ports are independent
		// of the mechanical extension table (which deliberately declares None).
		public static BitDirection GetTerminals(int index, int rotation, bool flipped = false)
		{
			if (index == 1 || index == 3)
			{
				int mode = (rotation % 7 + 7) % 7;
				return mode <= 1 ? BitDirection.LeftAndRight.Rotate(mode)
					: mode <= 5 ? ((BitDirection)9).Rotate(mode - 2) : BitDirection.Any;
			}
			if (index == 38 || index == 39)
			{
				int mode = (rotation % 5 + 5) % 5;
				return mode == 0 ? BitDirection.Any : mode == 1 || mode == 3 ? BitDirection.LeftAndRight : BitDirection.UpAndDown;
			}
			BitDirection mask;
			if (index <= 3 || index == 47) mask = BitDirection.Any;
			else if (index == 5) mask = BitDirection.Left | BitDirection.Up | BitDirection.Down;
			else if (index == 20 || index == 41) mask = BitDirection.Up;
			else if (index == 21) mask = BitDirection.Right;
			else if (index == 40) mask = BitDirection.Down;
			else if (index >= 42 && index <= 44) mask = BitDirection.None;
			else if (index == 45 || index == 48) mask = BitDirection.Left;
			else if (index >= 22 && index <= 37)
			{
				int chip = index - 22;
				if (chip == 10 || chip == 11) mask = BitDirection.Left | BitDirection.Up | BitDirection.Down;
				else if (chip == 12) mask = BitDirection.LeftAndRight;
				else if (chip == 13 || (chip >= 2 && chip <= 9 && chip % 2 == 0)) mask = BitDirection.Right | BitDirection.Up | BitDirection.Down;
				else if (chip >= 2 && chip <= 9) mask = BitDirection.LeftAndRight | (flipped ? BitDirection.Down : BitDirection.Up);
				else mask = BitDirection.LeftAndRight;
			}
			else mask = BitDirection.LeftAndRight;
			return mask.Rotate(rotation);
		}

		public static int NormalizeRotation(int index, int rotation)
		{
			int count = index == 1 || index == 3 ? 7 : index == 38 || index == 39 ? 5 : index == 18 || index == 19 ? 8 : 4;
			return (rotation % count + count) % count;
		}
	}

	/// <summary>
	/// 开关类部件（Switch 索引 4-5、Vcc 索引 21）的两张状态贴图标记。
	/// BPSS 里是 <c>m_activeSprite.SetActive(closed)</c> / <c>m_inactiveSprite.SetActive(!closed)</c>，
	/// 这里照 <see cref="GearboxVisual"/> 的做法：建实体时按 GameObject 名打标记，
	/// 由 <see cref="Simulation.CircuitSystem"/> 切 <c>DisableRendering</c>。
	/// </summary>
	public struct SwitchVisual : IComponentData
	{
		/// <summary>true = ActiveSprite，false = InactiveSprite。</summary>
		public bool Active;
	}
}
