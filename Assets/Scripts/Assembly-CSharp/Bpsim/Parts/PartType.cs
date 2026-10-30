namespace Bpsim.Parts
{
	public enum PartType
	{
		Unknown = 0,
		Pig = 1,
		TimeBomb = 2,
		KingPig = 3,
		Egg = 4,
		WoodenFrame = 5,
		MetalFrame = 6,
		CartWheel = 7,
		NormalWheel = 8,
		SmallWheel = 9,
		MotorWheel = 10,
		Bellows = 11,
		Fan = 12,
		Propeller = 13,
		Rotor = 14,
		CokeBottle = 15,
		SodaBottle = 16,
		Rocket = 17,
		RedRocket = 18,
		EngineSmall = 19,
		Engine = 20,
		EngineBig = 21,
		Umbrella = 22,
		PoweredUmbrella = 23,
		Spring = 24,
		TNT = 25,
		Rope = 26,
		Balloon = 27,
		Balloons2 = 28,
		Balloons3 = 29,
		Sandbag = 30,
		Sandbag2 = 31,
		Sandbag3 = 32,
		Wings = 33,
		Tailplane = 34,
		MetalWing = 35,
		MetalTail = 36,
		SpringBoxingGlove = 37,
		StickyWheel = 38,
		GrapplingHook = 39,
		Pumpkin = 40,
		Kicker = 41,
		Gearbox = 42,
		GoldenPig = 43,
		PointLight = 44,
		SpotLight = 45,
		JetEngine = 46,
		ElectricalPart = 47,
		MechanicalPart = 48,
		// 蒙皮（对照 BPRC SkinPart / SkinPartGeometry）。Besiege 的 BuildEdgeBlock 风格：
		// 四角可拖、四边可弯的一张布。形状是纯数据（SkinSheet 组件），网格与凸包由
		// SkinSheetSystem 统一重建 —— 不是挂在部件上的 MonoBehaviour。
		// 注意：燃料箱 / 燃料管没有自己的 PartType。BPSS 里它们本来就是
		// PartType.JetEngine（37）的皮肤：_03/_04/_06_SET 是 FuelTube.cs，_05_SET 是
		// FuelBox.cs，引擎本体只有 _01/_02。所以这里照原版合并进 JetEngine 索引，
		// 由 PartGameplayManager 按索引分派到 JetEngineBehavior / FuelTubeBehavior /
		// FuelBoxBehavior（见 Parts/Gameplay/PartGameplayManager.cs）。
		SkinPart = 49,
		Max = 50,
		All = -1
	}
}
