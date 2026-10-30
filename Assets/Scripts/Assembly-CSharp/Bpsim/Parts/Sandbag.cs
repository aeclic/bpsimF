using Unity.Entities;
using Unity.Mathematics;

namespace Bpsim.Parts
{
	/// <summary>
	/// 沙袋运行时状态，对应 BPSS 901-17.0 的 <c>Sandbag.cs</c>（234 行）。
	/// </summary>
	/// <remarks>
	/// <para>
	/// BPSS 侧的沙袋是唯一一类既不是焊接件、也不是普通自由刚体的部件：
	/// <c>Sandbag.IsIntegralPart()</c> 恒为 false，<c>PartExtensionMap</c> 里
	/// <c>isConnectionSource = false</c>，所以它不会被 <c>Contraption</c> 的焊接流程接进底盘。
	/// 它的连接方式是 <c>Initialize()</c> 里自己加的 <c>SpringJoint</c>：
	/// </para>
	/// <list type="bullet">
	/// <item>从自身格沿 <c>m_direction</c>（默认 up；<c>RotatableSandbag</c> 开启时按网格旋转）
	/// 向上 <c>SandbagConnectionDistance</c> 格内找第一个 <c>IsPartOfChassis()</c>
	/// 或 <c>Pig</c> 的部件当悬挂点；</item>
	/// <item>找不到就把 <c>m_dropped</c> 置 true（直接掉下去）；</item>
	/// <item><c>anchor = up * 0.5</c>（沙袋上的挂点）、<c>connectedAnchor</c> 默认值（= 上级件原点）、
	/// <c>minDistance = 0</c>、<c>spring = 100</c>、<c>damper = 10</c>，
	/// <c>maxDistance</c> 按气球数 0.5 / 0.55 / 0.65；</item>
	/// <item>刚体 <c>mass = m_mass</c>（沙袋 1.1 / 二沙袋 1.5 / 三沙袋 1.5）、<c>drag = 1</c>、
	/// <c>angularDrag = 10</c>、<c>constraints = 56</c>（锁 z 位移 + x/y 旋转 —— bpsim 里由
	/// 所有部件都带的 <see cref="Bpsim.Physics.Constraint2D" /> 承担）；</item>
	/// <item><c>OnTouch</c> → <c>Drop()</c>：销毁弹簧，给上级件 +5·up、给自己 −4·up 的
	/// <c>ForceMode.Impulse</c> 冲量（= 直接加速度），并切到 <c>DroppedSandbag</c> 层；</item>
	/// <item><c>Initialize()</c> 还会做一次可视化切换：关掉网格里的 <c>*Visualization</c>
	/// 替身、打开真正的 <c>SandbagVisualization</c>（N 个气球袋）。</item>
	/// </list>
	/// <para>
	/// ECS 侧把「弹簧关节」拆成两件事：状态留在本组件里（<see cref="Anchor" /> /
	/// <see cref="MaxDistance" /> / <see cref="Spring" /> / <see cref="Damper" />），
	/// 力由 <c>SandbagSystem</c> 每物理帧算成冲量施加（Unity Physics 没有 Unity 的 SpringJoint，
	/// 而弹簧本身是「超距才拉回」的单向约束，用冲量表达比造一条 Constraint 更贴近原版）。
	/// </para>
	/// </remarks>
	public struct Sandbag : IComponentData, IQueryTypeParameter
	{
		/// <summary>BPSS <c>INFeature.SandbagConnectionDistance</c> 的默认值（INDeclarationSettingsExp.json）。</summary>
		public const int DefaultConnectionDistance = 1;

		/// <summary>BPSS <c>INFeature.RotatableSandbag</c> 的默认值：false → 只向上找。</summary>
		/// <remarks>
		/// 这里刻意用 <c>static readonly</c> 而不是 <c>const</c>：<see cref="Simulation.SandbagSystem.Bind"/>
		/// 里有一条按该开关分支的“旋转沙袋”实现（BPSS 逐条复刻，留着以后开功能用）。
		/// 写成 const 会被 C# 编译期折叠，那条分支变成不可达代码并报 CS0162；
		/// static readonly 让编译器保留分支（运行时同样恒为 false，行为不变）。
		/// </remarks>
		public static readonly bool DefaultRotatable = false;

		/// <summary>BPSS <c>SpringJoint.spring</c>。</summary>
		public const float DefaultSpring = 100f;

		/// <summary>BPSS <c>SpringJoint.damper</c>。</summary>
		public const float DefaultDamper = 10f;

		/// <summary>BPSS <c>SpringJoint.anchor = Vector3.up * 0.5f</c>（沙袋上的挂点）。</summary>
		public const float AnchorOffset = 0.5f;

		/// <summary>BPSS <c>Drop()</c> 里给上级件的 <c>5f * Vector3.up</c> 瞬时冲量。</summary>
		public const float DropImpulseOnAnchor = 5f;

		/// <summary>BPSS <c>Drop()</c> 里给自己的 <c>-4f * Vector3.up</c> 瞬时冲量。</summary>
		public const float DropImpulseOnSelf = -4f;

		/// <summary>首帧绑定是否已完成（对应 BPSS <c>Initialize()</c> 跑过一次）。</summary>
		public bool Bound;

		/// <summary>BPSS <c>m_dropped</c>：true = 已经放手 / 一开始就没挂上。</summary>
		public bool Dropped;

		/// <summary>悬挂点实体（BPSS <c>SpringJoint.connectedBody</c>）。</summary>
		public Entity Anchor;

		/// <summary>BPSS <c>SpringJoint.maxDistance</c>（按气球数 0.5 / 0.55 / 0.65）。</summary>
		public float MaxDistance;

		/// <summary>BPSS <c>SpringJoint.spring</c>。</summary>
		public float Spring;

		/// <summary>BPSS <c>SpringJoint.damper</c>。</summary>
		public float Damper;

		/// <summary>
		/// 初始悬挂偏移：BPSS 把 <c>transform.position</c> 从「上级件位置 − up*0.5」
		/// 再叠一个按气球数选出的 <c>vector</c>（见 <see cref="GetHangParameters" />）。
		/// </summary>
		public float3 HangOffset;

		/// <summary>气球数量（BPSS prefab 上的 <c>m_numberOfBalloons</c>）：1 / 2 / 3。</summary>
		public int Balloons;

		/// <summary>沙袋族判定：Sandbag / Sandbag2 / Sandbag3。</summary>
		public static bool IsSandbag(PartType partType)
		{
			return partType == PartType.Sandbag || partType == PartType.Sandbag2 || partType == PartType.Sandbag3;
		}

		/// <summary>
		/// BPSS prefab 上的 <c>m_numberOfBalloons</c>（实测 <c>Part_Sandbag_01_SET</c> = 1、
		/// <c>Part_Sandbags2_01_SET</c> = 2、<c>Part_Sandbags3_01_SET</c> = 3）。
		/// </summary>
		public static int GetBalloonCount(PartType partType)
		{
			if (partType == PartType.Sandbag3)
			{
				return 3;
			}
			if (partType == PartType.Sandbag2)
			{
				return 2;
			}
			return 1;
		}

		/// <summary>
		/// 按气球数取 <c>(vector, maxDistance)</c>，逐字照抄 BPSS <c>Sandbag.Initialize()</c> 的 switch
		/// （注意它的 <c>default</c> 分支是 3 个气球的情形）。
		/// </summary>
		public static void GetHangParameters(int balloons, out float3 offset, out float maxDistance)
		{
			switch (balloons)
			{
			case 2:
				offset = new float3(0.35f, -0.3f, -0.02f);
				maxDistance = 0.55f;
				break;
			case 1:
				offset = new float3(-0.15f, -0.15f, -0.01f);
				maxDistance = 0.5f;
				break;
			default:
				offset = new float3(0f, -0.35f, -0.03f);
				maxDistance = 0.65f;
				break;
			}
		}

		/// <summary>建实体时写入初始状态（<c>PartEntityFactory.CreatePart</c> 调用）。</summary>
		public static Sandbag Create(PartTypeInfo info)
		{
			int balloons = GetBalloonCount(info.PartType);
			GetHangParameters(balloons, out float3 offset, out float maxDistance);
			return new Sandbag
			{
				Bound = false,
				Dropped = false,
				Anchor = Entity.Null,
				MaxDistance = maxDistance,
				Spring = DefaultSpring,
				Damper = DefaultDamper,
				HangOffset = offset,
				Balloons = balloons
			};
		}
	}

	/// <summary>
	/// 沙袋子物体上的可视化标记。BPSS <c>Sandbag.Awake()</c> 把
	/// <c>m_actualVisualizationNode</c>（真正 N 个气球袋的 <c>SandbagVisualization</c>）关掉、
	/// <c>m_gridVisualizationNode</c>（网格/图标替身 <c>Sandbag2Visualization</c> /
	/// <c>Sandbag3Visualization</c>）打开；<c>Initialize()</c> 再反过来。
	/// 也就是说游戏里应该只看见 <c>SandbagVisualization</c> 那一支。
	/// </summary>
	/// <remarks>
	/// bpsim 的 prefab 烘焙不看 <c>activeSelf</c>（<c>CreateChild</c> 会遍历所有子物体，
	/// <c>CreateRenderer</c> 只按 <c>Renderer.enabled</c> 决定是否加 <c>DisableRendering</c>），
	/// 所以两支在 bpsim 里会同时渲染 —— 必须由系统按这个标记把不该显示的那支关掉。
	/// </remarks>
	public struct SandbagVisual : IComponentData, IQueryTypeParameter
	{
		/// <summary>true = <c>SandbagVisualization</c>（Initialize 后要显示）；false = 网格替身（要隐藏）。</summary>
		public bool Actual;
	}
}
