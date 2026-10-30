namespace Bpsim.Parts
{
	/// <summary>
	/// 部件落位后的自动朝向策略，等价于 BPSS 的 <c>BasePart.AutoAlignType</c>。
	/// </summary>
	/// <remarks>
	/// BPSS 里这个值主要来自 prefab 的序列化字段 <c>m_autoAlign</c>（脚本 Awake 只覆盖了少数几个类），
	/// bpsim 的部件模板没有对应字段，所以在这里用一张静态表还原。
	/// 取值依据是对 BPSS 901-17.0 全部 prefab 的 <c>m_autoAlign:</c> 统计：
	/// <c>0</c>(None)=153、<c>1</c>(Rotate)=174、<c>2</c>(FlipVertically)=17、<c>-1</c>=1。
	/// 其中 FlipVertically 的 17 个恰好是机翼/尾翼（WoodenWings 01-05、WoodenTail 01-04、
	/// MetalWings 01-04、MetalTail 01-04），Rotate 覆盖的是轮子、推进器、瓶子、气球、弹簧等。
	/// </remarks>
	public enum PartAutoAlignMode
	{
		/// <summary>不自动对齐（保持玩家放置时的朝向）。</summary>
		None = 0,

		/// <summary>按相邻的连接源旋转，让自己的连接端子指向对方。</summary>
		Rotate = 1,

		/// <summary>按左右相邻的机架决定是否左右镜像（机翼/尾翼）。</summary>
		FlipVertically = 2
	}

	/// <summary>
	/// 自动对齐策略表与机架判定。纯静态查表，可以在 Burst job 里直接调用。
	/// </summary>
	public static class PartAutoAlign
	{
		/// <summary>取部件的自动对齐策略；未列出的部件一律 <see cref="PartAutoAlignMode.None" />。</summary>
		public static PartAutoAlignMode GetMode(PartTypeInfo info)
		{
			switch (info.PartType)
			{
			case PartType.Wings:
			case PartType.Tailplane:
			case PartType.MetalWing:
			case PartType.MetalTail:
				return PartAutoAlignMode.FlipVertically;
			case PartType.Bellows:
			case PartType.CartWheel:
			case PartType.NormalWheel:
			case PartType.SmallWheel:
			case PartType.MotorWheel:
			case PartType.StickyWheel:
			case PartType.CokeBottle:
			case PartType.SodaBottle:
			case PartType.Rocket:
			case PartType.RedRocket:
			case PartType.Umbrella:
			case PartType.PoweredUmbrella:
			case PartType.Fan:
			case PartType.Propeller:
			case PartType.Rotor:
			case PartType.GrapplingHook:
			case PartType.Spring:
			case PartType.SpringBoxingGlove:
			case PartType.TimeBomb:
			case PartType.Gearbox:
			case PartType.Pumpkin:
			case PartType.PointLight:
			case PartType.SpotLight:
			case PartType.Sandbag:
			case PartType.Sandbag2:
			case PartType.Sandbag3:
			case PartType.Balloon:
			case PartType.Balloons2:
			case PartType.Balloons3:
			case PartType.TNT:
			case PartType.Kicker:
			case PartType.JetEngine:
			case PartType.MechanicalPart:
				return PartAutoAlignMode.Rotate;
			default:
				return PartAutoAlignMode.None;
			}
		}

		/// <summary>
		/// 是否机架件（木框 / 铁框）。BPSS 的 <c>IsChassisPart</c> 在自动对齐里只用来判断机翼该朝哪边镜像。
		/// </summary>
		public static bool IsChassis(PartTypeInfo info)
		{
			return info.PartType == PartType.WoodenFrame || info.PartType == PartType.MetalFrame;
		}

		/// <summary>
		/// 求解「让 <paramref name="terminals" /> 这一组连接端子指向 <paramref name="target" />」所需的朝向。
		/// </summary>
		/// <param name="terminals">部件未旋转时的连接端子掩码（<c>PartExtensionData.ConnectionDirection</c>）。</param>
		/// <param name="currentRotation">当前朝向（玩家放置时选的那个）。</param>
		/// <param name="target">希望端子指向的世界方向（从本件指向邻居）。</param>
		/// <param name="eightWay">八向部件（每步 45°），候选步长按 2 递增。</param>
		/// <param name="rotation">求解结果；失败时等于 <paramref name="currentRotation" />。</param>
		/// <remarks>
		/// 候选步长按「离当前朝向最近」排序（0 → +1 → −1 → 2 步），所以端子本来就朝着邻居时
		/// 会原样保留玩家的选择，只有确实连不上才拧。BPSS 的 <c>AutoAlignRotation</c> 用
		/// <c>(target − source + 4) % 4</c> 直接算出唯一角度，但它依赖每个部件声明一个
		/// <c>JointConnectionDirection</c> 枚举；bpsim 的扩展表只存位掩码，且允许 <c>Any</c>，
		/// 所以这里改成「在掩码里搜索最小步长」，对 <c>Any</c> 自然退化成不动。
		/// </remarks>
		public static bool TrySolveRotation(BitDirection terminals, int currentRotation, BitDirection target, bool eightWay, out int rotation)
		{
			rotation = currentRotation;
			if (terminals == BitDirection.None || target == BitDirection.None)
			{
				return false;
			}
			int steps = (eightWay ? 8 : 4);
			int current = ((currentRotation % steps) + steps) % steps;
			for (int i = 0; i < 4; i++)
			{
				int delta;
				if (eightWay)
				{
					// 八向部件每步 45°，掩码仍按 90° 折叠，所以只能取偶数步。
					delta = ((i == 1) ? 2 : ((i == 2) ? (-2) : ((i == 3) ? 4 : 0)));
				}
				else
				{
					delta = ((i == 1) ? 1 : ((i == 2) ? (-1) : ((i == 3) ? 2 : 0)));
				}
				int candidate = (((current + delta) % steps) + steps) % steps;
				if ((terminals.Rotate(candidate) & target) != 0)
				{
					rotation = candidate;
					return true;
				}
			}
			return false;
		}
	}
}
