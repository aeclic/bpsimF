using Unity.Entities;
using Unity.Mathematics;

namespace Bpsim.Parts
{
	/// <summary>One physical bag. BPLE multi variants produce independent runtime siblings.</summary>
	public struct Sandbag : IComponentData, IQueryTypeParameter
	{
		public const int DefaultConnectionDistance = 10;
		public static readonly bool DefaultRotatable = true;
		public const float DefaultSpring = 100f;
		public const float DefaultDamper = 10f;
		public const float AnchorOffset = .5f;
		public const float DropImpulseOnAnchor = 5f;
		public const float DropImpulseOnSelf = -4f;
		public bool Bound, Dropped, DropRequest;
		public Entity Anchor, Joint;
		public float3 ConnectedAnchor, LineAnchor, HangOffset, InitialPosition;
		public float MaxDistance, Spring, Damper, Mass;
		public int Balloons;
		public static bool IsSandbag(PartType type) => type == PartType.Sandbag || type == PartType.Sandbag2 || type == PartType.Sandbag3;
		public static int GetBalloonCount(PartType type) => type == PartType.Sandbag2 ? 2 : type == PartType.Sandbag3 ? 3 : 1;
		public static void GetHangParameters(int count, out float3 offset, out float maximum)
		{
			switch (count)
			{
				case 1: offset = new float3(-.15f, -.15f, -.01f); maximum = .5f; break;
				case 2: offset = new float3(.35f, -.3f, -.02f); maximum = .55f; break;
				default: offset = new float3(0, -.35f, -.03f); maximum = .65f; break;
			}
		}
		public static Sandbag Create(PartTypeInfo info)
		{
			var profile = Gameplay.BpleSuspensionProfiles.Get(info);
			GetHangParameters(profile.count, out var offset, out float maximum);
			return new Sandbag { Balloons = profile.count, Mass = profile.mass, HangOffset = offset,
				MaxDistance = maximum, Spring = DefaultSpring, Damper = DefaultDamper };
		}
	}
	public struct SandbagVisual : IComponentData, IQueryTypeParameter { public bool Actual; }
}
