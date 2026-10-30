namespace Bpsim.Parts
{
	public static class PartTypeExtensions
	{
		public static string GetAliasName(this PartType partType)
		{
			return partType switch
			{
				PartType.Unknown => "未知", 
				PartType.Pig => "雀斑猪", 
				PartType.TimeBomb => "定时炸弹", 
				PartType.KingPig => "猪王", 
				PartType.Egg => "鸟蛋", 
				PartType.WoodenFrame => "木框", 
				PartType.MetalFrame => "铁框", 
				PartType.CartWheel => "木轮", 
				PartType.NormalWheel => "铁轮", 
				PartType.SmallWheel => "小铁轮", 
				PartType.MotorWheel => "驱动轮", 
				PartType.Bellows => "风箱", 
				PartType.Fan => "风扇", 
				PartType.Propeller => "小螺旋桨", 
				PartType.Rotor => "大螺旋桨", 
				PartType.CokeBottle => "可乐瓶", 
				PartType.SodaBottle => "汽水瓶", 
				PartType.Rocket => "蓝火箭", 
				PartType.RedRocket => "红火箭", 
				PartType.EngineSmall => "小发动机", 
				PartType.Engine => "中发动机", 
				PartType.EngineBig => "大发动机", 
				PartType.Umbrella => "雨伞", 
				PartType.PoweredUmbrella => "驱动伞", 
				PartType.Spring => "弹簧", 
				PartType.TNT => "TNT", 
				PartType.Rope => "绳子", 
				PartType.Balloon => "气球", 
				PartType.Balloons2 => "二气球", 
				PartType.Balloons3 => "三气球", 
				PartType.Sandbag => "沙袋", 
				PartType.Sandbag2 => "二沙袋", 
				PartType.Sandbag3 => "三沙袋", 
				PartType.Wings => "木机翼", 
				PartType.Tailplane => "木尾翼", 
				PartType.MetalWing => "铁机翼", 
				PartType.MetalTail => "铁尾翼", 
				PartType.SpringBoxingGlove => "拳套", 
				PartType.StickyWheel => "吸盘轮", 
				PartType.GrapplingHook => "吸盘枪", 
				PartType.Pumpkin => "南瓜", 
				PartType.Kicker => "分离器", 
				PartType.Gearbox => "变速箱", 
				PartType.GoldenPig => "金猪", 
				PartType.PointLight => "点光源", 
				PartType.SpotLight => "聚光灯", 
				PartType.JetEngine => "喷气引擎", 
				PartType.ElectricalPart => "电路部件", 
				PartType.MechanicalPart => "机械部件", 
				PartType.SkinPart => "蒙皮", 
				PartType.All => "任意", 
				_ => "未知", 
			};
		}
	}
}
