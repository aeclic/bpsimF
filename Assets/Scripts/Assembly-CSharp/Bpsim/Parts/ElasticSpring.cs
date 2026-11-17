using Unity.Entities;
using Unity.Mathematics;

namespace Bpsim.Parts
{
	/// <summary>Passive spring state. Runtime entities are owned by ElasticSpringSystem, not a behaviour.</summary>
	public struct ElasticSpring : IComponentData
	{
		public bool Initialized;
		public bool OwnsEndpoint;
		public bool Stable;
		public Entity ConnectedBody;
		public Entity Joint;
		public Entity Visualization;
		public float3 ConnectedAnchor;
		public float HalfLength;
		public float BreakForce;
		public float AppliedBreakImpulse;
		public float Length;
		public static ElasticSpring Create() => new ElasticSpring { BreakForce = 250f, AppliedBreakImpulse = -1f, HalfLength = .5f, Length = 1f };
	}
	public struct SpringEndpoint : IComponentData { public Entity Owner; }
	public struct SpringOwnedJoint : IComponentData { public Entity Owner; }
	public struct SpringVisual : IComponentData { public Entity Root; public float3x3 OriginalScale; }
}
