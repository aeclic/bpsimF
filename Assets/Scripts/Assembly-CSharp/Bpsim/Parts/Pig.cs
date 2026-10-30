using Unity.Entities;
using Unity.Mathematics;

namespace Bpsim.Parts
{
	/// <summary>
	/// 猪族（Pig / KingPig / GoldenPig）的运动参数种类。
	/// BPSS 侧是三份几乎相同的 MonoBehaviour，差别只在「阻尼模型」和「表情/音效」：
	/// <list type="bullet">
	/// <item><description><c>Pig.FixedUpdate</c>：速度 &lt; 1 m/s 时把 drag / angularDrag 抬到
	/// <c>0.2 + 2.5 * (1 - speed)</c>，否则回落 <c>0.2 / 0.05</c>——慢速时像陷在地里，被推一下才滚得动。</description></item>
	/// <item><description><c>KingPig.Initialize</c>：固定 <c>drag = 0.5</c>、<c>angularDrag = 1f</c>。</description></item>
	/// <item><description><c>GoldenPig.Initialize</c>：同样固定 <c>0.5 / 1f</c>，外加滚动音效（bpsim 无音频层）。</description></item>
	/// </list>
	/// 三者都是 <c>CanBeEnclosed() =&gt; true</c>，而 bpsim 的 <see cref="BasePart.CanBeContained"/> 默认就允许
	/// 装进框里，所以这里不需要额外标记。
	/// </summary>
	public enum PigFamily : byte
	{
		/// <summary>普通猪：速度相关阻尼（Pig.FixedUpdate）。</summary>
		Pig = 0,

		/// <summary>猪王：固定阻尼 0.5 / 1（KingPig.Initialize）。</summary>
		KingPig = 1,

		/// <summary>金猪：固定阻尼 0.5 / 1（GoldenPig.Initialize）。</summary>
		GoldenPig = 2
	}

	/// <summary>
	/// 猪族零件标记。<see cref="Simulation.PigMotionSystem"/> 每步据此写 <see cref="Unity.Physics.PhysicsDamping"/>。
	/// 阻尼做成组件数据（而不是每帧手动缩放速度），因为 Unity.Physics 的 <c>PhysicsDamping</c> 就是声明式阻尼，
	/// 由求解器在速度积分里应用，不需要我们在托管侧遍历刚体。
	/// </summary>
	public struct PigMotion : IComponentData
	{
		/// <summary>阻尼模型种类，见 <see cref="PigFamily"/>。</summary>
		public PigFamily Family;

		/// <summary>运行期观测到的最大速度（BPSS 用来选表情，这里只作为 QA/调试证据）。</summary>
		public float PeakSpeed;

		/// <summary>当前写进 <see cref="Unity.Physics.PhysicsDamping"/> 的线阻尼，回显给 <c>Describe()</c>。</summary>
		public float Linear;

		/// <summary>当前写进的角阻尼。</summary>
		public float Angular;
	}

	/// <summary>
	/// BPSS <c>PartLevitation</c> 的等价物：每步对刚体施加一个恒定向上的力。
	/// 原版两个 SerializeField（<c>force</c> / <c>enclosedForce</c>）的值存在 AssetBundle 的 prefab 里，
	/// bpsim 没有那份资源，所以默认不挂到任何 prefab 上，只提供组件 + 系统；
	/// 需要时（QA 或后续补数据）用 <c>AddComponentData</c> 显式给。
	/// </summary>
	public struct PartLevitation : IComponentData
	{
		/// <summary>自由状态下的向上推力（牛顿）。</summary>
		public float Force;

		/// <summary>被装进框里之后的向上推力（BPSS 的 <c>enclosedForce</c>）。</summary>
		public float EnclosedForce;
	}
}
