using Bpsim.Parts.Simulation;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace Bpsim.Parts.Gameplay
{
	// Wind producers may accumulate world-space air velocity here. Aerodynamic parts consume it once per step.
	public struct PartAirflow : IComponentData { public float3 Velocity; }

	internal static class PartVariantPhysics
	{
		internal static float3 Position(Entity part) => PartGameplayUtil.Em.HasComponent<LocalTransform>(part)
			? PartGameplayUtil.Em.GetComponentData<LocalTransform>(part).Position : PartGameplayUtil.Position(part);
		internal static quaternion Rotation(Entity part) => PartGameplayUtil.Em.GetComponentData<LocalTransform>(part).Rotation;
		internal static float Mass(Entity part)
		{
			var em = PartGameplayUtil.Em;
			if (em.HasComponent<MegaPartOffset>(part)) return em.GetComponentData<MegaPartOffset>(part).Mass;
			if (!em.HasComponent<PhysicsMass>(part)) return 0f;
			float inverse = em.GetComponentData<PhysicsMass>(part).InverseMass;
			return inverse > 0f ? 1f / inverse : 0f;
		}
		internal static void Force(Entity part, float3 force, float dt, float3 offset = default)
		{
			float mass = Mass(part);
			if (mass <= 0f) return;
			PartGameplayUtil.AddVelocity(part, force * (dt / mass));
			float torque = math.cross(offset, force).z;
			if (torque != 0f) Torque(part, torque, dt);
		}
		internal static float AngularSpeed(Entity part)
		{
			var em = PartGameplayUtil.Em;
			if (!em.HasComponent<PhysicsVelocity>(part)) return 0f;
			var mass = em.GetComponentData<PhysicsMass>(part);
			return math.rotate(math.mul(Rotation(part), mass.Transform.rot), em.GetComponentData<PhysicsVelocity>(part).Angular).z;
		}
		internal static void Torque(Entity part, float torque, float dt, bool acceleration = false)
		{
			var em = PartGameplayUtil.Em;
			if (Mass(part) <= 0f || !em.HasComponent<PhysicsVelocity>(part)) return;
			var mass = em.GetComponentData<PhysicsMass>(part);
			var velocity = em.GetComponentData<PhysicsVelocity>(part);
			float3 local = math.rotate(math.inverse(math.mul(Rotation(part), mass.Transform.rot)), new float3(0, 0, torque * dt));
			velocity.Angular += acceleration ? local : local * mass.InverseInertia;
			em.SetComponentData(part, velocity);
		}
		internal static float3 AirVelocity(Entity part)
		{
			var em = PartGameplayUtil.Em;
			float3 wind = float3.zero;
			if (em.HasComponent<PartAirflow>(part))
			{
				wind = em.GetComponentData<PartAirflow>(part).Velocity;
				em.SetComponentData(part, default(PartAirflow));
			}
			float3 relative = PartGameplayUtil.LinearVelocity(part) - wind;
			relative.z = 0;
			return relative;
		}
		internal static bool IsAssembly(Entity part)
		{
			int id = PartGameplayUtil.ConnectedComponent(part);
			var components = PartGameplayUtil.Simulator.ConnectedComponents;
			return id >= 0 && id < components.Length && components[id].PartCount > 1;
		}
		internal static float Wrap(float radians) => math.atan2(math.sin(radians), math.cos(radians));
		internal static float3 Perpendicular(float3 value) => new float3(-value.y, value.x, 0);
	}
}
