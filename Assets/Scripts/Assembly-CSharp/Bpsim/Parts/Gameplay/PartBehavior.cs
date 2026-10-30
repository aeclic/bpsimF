using Unity.Entities;
using Unity.Mathematics;

namespace Bpsim.Parts.Gameplay
{
	/// <summary>
	/// Runtime behaviour attached to one simulated part entity.
	/// The original bpsim build shipped a set of empty systems (RocketSystem,
	/// GrapplingHookSystem, PartPowerSystem, ...), so almost every non-propeller
	/// part was inert.  This layer supplies the behaviours that BPSS 901-17.0
	/// implements in BasePart subclasses (GrapplingHook / ExplodingGrapplingHook /
	/// AutoGun / multipart generators / AutoControlLight / DecelerationLight).
	/// </summary>
	public abstract class PartBehavior
	{
		public Entity Part;

		public PartTypeInfo Info;

		public PartGameplayKind Kind;

		public bool Enabled;

		protected PartGameplayManager Manager => PartGameplayManager.Instance;

		protected EntityManager Em => PartGameplayManager.Em;

		/// <summary>Called once right after the manager created the behaviour.</summary>
		public virtual void Init()
		{
		}

		public virtual void Trigger()
		{
		}

		public virtual void Tick(float deltaTime)
		{
		}

		public virtual void Dispose()
		{
		}

		public virtual string Describe()
		{
			return Kind + " " + Info.PartType + "#" + Info.PartIndex + " enabled=" + Enabled;
		}

		protected float3 Position()
		{
			return PartGameplayUtil.Position(Part);
		}

		protected quaternion Rotation()
		{
			return PartGameplayUtil.Rotation(Part);
		}

		protected float3 Forward()
		{
			return math.mul(Rotation(), new float3(1f, 0f, 0f));
		}

		protected float3 Down()
		{
			return math.mul(Rotation(), new float3(0f, -1f, 0f));
		}

		protected float3 Up()
		{
			return math.mul(Rotation(), new float3(0f, 1f, 0f));
		}
	}

	public enum PartGameplayKind
	{
		None = 0,
		GrappleHook = 1,
		AutoGun = 2,
		Generator = 3,
		AutoControlLight = 4,
		DecelerationLight = 5,
		Rocket = 6,
		TNT = 7,
		Balloon = 8,
		Umbrella = 9,
		PoweredUmbrella = 10,
		Spring = 11,
		Kicker = 12,
		TimeBomb = 13,
		Bellows = 14,
		MotorWheel = 15,
		JetEngine = 16,
		Wing = 17,
		StickyWheel = 18,
		OffRoadWheel = 19,
		Gearbox = 20,
		CircuitSwitch = 21,
		Sandbag = 22,
		Rope = 23,
		Egg = 24,
		PointLight = 25,
		SpotLight = 26,
		Pumpkin = 27,
		FuelBox = 28,
		FuelTube = 29,
		Pig = 30,
		ManualGun = 31
	}
}
