using Bpsim.Parts.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

namespace Bpsim.Parts.Gameplay
{
	public sealed partial class LaunchBehavior
	{
		public Entity Target { get; private set; }
		public float3 LastGuidanceForce { get; private set; }
		public float3 GuidanceImpulse { get; private set; }
		public float TrackingDistance = 320f;
		public float TrackingAngle = 360f;
		public float TrackingForce = 200f;
		public float AvoidanceDistance = 320f;
		public float AvoidanceAngle = 90f;
		private float m_trackingError = float.NaN;
		private void ResetGuidance() { Target = Entity.Null; m_trackingError = float.NaN; LastGuidanceForce = float3.zero; GuidanceImpulse = float3.zero; }
		private bool TickGuidance(float dt)
		{
			LastGuidanceForce = float3.zero;
			if ((Info.PartIndex != 1 && Info.PartIndex != 3) || !PartVariantPhysics.IsAssembly(Part))
			{
				ResetGuidance();
				return false;
			}
			if (Info.PartType == PartType.Rocket) LastGuidanceForce = Avoidance();
			else if (Info.PartType == PartType.RedRocket) LastGuidanceForce = Tracking();
			else return false;
			PartVariantPhysics.Force(Part, LastGuidanceForce, dt);
			GuidanceImpulse += LastGuidanceForce * dt;
			return true;
		}
		private float Priority(Entity part)
		{
			int component = PartGameplayUtil.ConnectedComponent(part);
			var components = PartGameplayUtil.Simulator.ConnectedComponents;
			int count = component >= 0 && component < components.Length ? components[component].PartCount : 1;
			return count / math.max(1, math.distance(PartVariantPhysics.Position(Part).xy, PartVariantPhysics.Position(part).xy));
		}
		private float3 Tracking()
		{
			float3 origin = PartVariantPhysics.Position(Part), forward = PushDirection();
			float threshold = math.cos(math.radians(math.clamp(TrackingAngle, 0, 360) * .5f));
			Entity best = Entity.Null;
			float bestPriority = 0;
			bool currentVisible = false;
			float oldPriority = PartGameplayUtil.Alive(Target) ? Priority(Target) * 1.5f : 0;
			foreach (Entity candidate in PartGameplayUtil.Simulator.Parts)
			{
				if (!PartGameplayUtil.Alive(candidate) || MarkerTeams.SameTeam(Part, candidate)
					|| PartGameplayUtil.TypeInfo(candidate).PartType == PartType.Rope) continue;
				float3 delta = PartVariantPhysics.Position(candidate) - origin; delta.z = 0;
				float distance = math.length(delta);
				if (distance >= math.max(0, TrackingDistance) || math.dot(delta, forward) <= threshold * distance) continue;
				if (candidate == Target) currentVisible = true;
				float priority = Priority(candidate);
				if (priority > bestPriority && priority > oldPriority) { best = candidate; bestPriority = priority; }
			}
			if (!currentVisible) Target = Entity.Null;
			if (best != Entity.Null && best != Target) { Target = best; m_trackingError = float.NaN; }
			if (!PartGameplayUtil.Alive(Target)) { m_trackingError = float.NaN; return float3.zero; }
			float3 relative = PartGameplayUtil.LinearVelocity(Target) - PartGameplayUtil.LinearVelocity(Part); relative.z = 0;
			float3 separation = PartVariantPhysics.Position(Target) - origin; separation.z = 0;
			if (Info.PartIndex == 1)
			{
				float distance = math.max(1, math.length(separation));
				float3 force = 8 * relative + 16 * (distance > 16 ? 1 : 16 / distance) * separation;
				return force * math.min(1, math.max(0, TrackingForce) / math.max(math.length(force), 1E-5f));
			}
			float3 desired = relative + 2 * separation;
			float error = math.atan2(math.cross(forward, desired).z, math.dot(forward, desired));
			float amount = float.IsNaN(m_trackingError) ? 0 : math.clamp(1600 * PartVariantPhysics.Wrap(error - m_trackingError) + 400 * error, -TrackingForce, TrackingForce);
			m_trackingError = error;
			return PartVariantPhysics.Perpendicular(forward) * amount;
		}
		private float3 Avoidance()
		{
			using (var query = Em.CreateEntityQuery(ComponentType.ReadOnly<PhysicsWorldSingleton>()))
			{
				if (query.IsEmptyIgnoreFilter) return float3.zero;
				var world = query.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
				float3 forward = PushDirection();
				using (var hits = new NativeList<RaycastHit>(Allocator.Temp))
				{
					if (Info.PartIndex == 1)
					{
						if (!Obstacle(world, hits, forward, out float distance, out float3 otherVelocity)) return float3.zero;
						float closing = math.dot(PartGameplayUtil.LinearVelocity(Part) - otherVelocity, forward) + 8;
						if (closing <= 0) return float3.zero;
						float gap = distance > 4 ? distance - 3 : 1;
						float arrival = gap / closing;
						if (arrival >= 2) return float3.zero;
						float amount = 8 * (arrival < 1 ? closing * closing / (2 * gap) : -gap / 2 + closing);
						return -forward * math.min(amount, 400);
					}
					int nearest = -1;
					float bestDistance = float.PositiveInfinity;
					float3 closestDirection = forward;
					for (int i = 0; i < 6; i++)
					{
						float3 ray = math.rotate(quaternion.RotateZ(math.radians(AvoidanceAngle * (i / 5f - .5f))), forward);
						if (Obstacle(world, hits, ray, out float distance, out _) && distance < bestDistance)
						{ nearest = i; bestDistance = distance; closestDirection = ray; }
					}
					if (nearest < 0) return float3.zero;
					float speed = math.dot(PartGameplayUtil.LinearVelocity(Part), closestDirection) + 8;
					if (speed <= 0) return float3.zero;
					float time = (bestDistance > 4 ? bestDistance - 3 : 1) / speed;
					if (time >= 2) return float3.zero;
					float steering = math.min(400, 150 * (time < 1 ? 1 / time : -time + 2));
					return PartVariantPhysics.Perpendicular(forward) * steering * (nearest < 3 ? 1 : -1);
				}
			}
		}
		private bool Obstacle(CollisionWorld world, NativeList<RaycastHit> hits, float3 direction, out float distance, out float3 velocity)
		{
			hits.Clear(); distance = float.PositiveInfinity; velocity = float3.zero;
			float range = math.max(0, AvoidanceDistance);
			var input = new RaycastInput { Start = PartVariantPhysics.Position(Part), End = PartVariantPhysics.Position(Part) + direction * range, Filter = CollisionFilter.Default };
			var collector = new AllHitsCollector<RaycastHit>(1, ref hits);
			if (!world.CastRay(input, ref collector)) return false;
			bool detectParts = false;
			if (Em.HasComponent<PartContainerValue>(Part))
			{
				Entity container = Em.GetComponentData<PartContainerValue>(Part).Value;
				detectParts = PartGameplayUtil.Alive(container) && !PartGameplayUtil.TypeInfo(container).BelongsTo(BasePart.TransparentFrames);
			}
			foreach (var hit in hits)
			{
				Entity entity = hit.Entity;
				if (entity == Part || MegaStructure.IsVehicleBody(entity)) continue;
				if (Em.HasComponent<PartTypeValue>(entity) && (!detectParts || PartGameplayUtil.ConnectedComponent(entity) == PartGameplayUtil.ConnectedComponent(Part)
					|| !MarkerTeams.SameTeam(Part, entity))) continue;
				if (hit.Fraction * range >= distance) continue;
				distance = hit.Fraction * range;
				velocity = PartGameplayUtil.LinearVelocity(entity);
			}
			return !float.IsPositiveInfinity(distance);
		}
	}
}
