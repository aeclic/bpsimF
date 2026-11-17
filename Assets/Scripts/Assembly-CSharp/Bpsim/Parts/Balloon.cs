using Unity.Entities;
using Unity.Mathematics;

namespace Bpsim.Parts
{
	public struct Balloon : IComponentData
	{
		public bool Initialized;
	}
	/// <summary>One real balloon body, not an acceleration applied to neighboring parts.</summary>
	public struct BalloonBody : IComponentData
	{
		public Entity Target, TetherJoint;
		public float3 ConnectedAnchor, LocalAnchor;
		public float MaximumDistance, Force, MaximumSpeed;
		public int PartIndex;
		public bool PopRequest, Popped, RegisteredBalance, CollisionUnpoppable;
	}
	public struct BalloonBalancer : IComponentData
	{
		public Entity Joint;
		public int BalloonCount;
	}
	public struct BalloonPopSurface : IComponentData { }
	/// <summary>Runtime copy with the same source slot, not a second entry in its spatial-map cell.</summary>
	public struct SuspensionSibling : IComponentData { public Entity Source; public int Ordinal; }
	public struct SuspensionVisual : IComponentData { public bool Actual; }
	public struct SuspensionJoint : IComponentData { public Entity Owner; public bool WorldOnly; }
	public struct SuspensionRope : IComponentData
	{
		public Entity Target;
		public float3 LocalAnchor, ConnectedAnchor;
		public float Width;
		public bool Visible;
	}
	public static class SuspensionMath
	{
		// Raw APK BalloonSystem limiter: speed along the force direction, not total speed.
		public static float LimitBalloonForce(float force, float speed, float maximumSpeed)
			=> speed > maximumSpeed ? force / (1f + speed - maximumSpeed) : force;
		public static void SpringSoftness(float stiffness, float damping, float inverseMass,
			out float frequency, out float ratio)
		{
			float mass = inverseMass > 1E-6f ? 1f / inverseMass : 1f;
			frequency = math.sqrt(stiffness / mass) / (2f * math.PI);
			ratio = damping / (2f * math.sqrt(stiffness * mass));
		}
	}
}
