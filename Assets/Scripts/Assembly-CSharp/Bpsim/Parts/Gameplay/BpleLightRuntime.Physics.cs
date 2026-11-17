using System.Collections.Generic;
using Bpsim.Parts.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

namespace Bpsim.Parts.Gameplay
{
	internal sealed partial class BpleLightRuntime
	{
		private sealed class Body
		{
			public Entity Entity;
			public Vector2 PreviousPosition, PreviousDirection;
			public BpleLightBodyBounds Properties;
			public float Mass;
			public bool Dynamic;
		}
		private readonly Dictionary<Entity, Body> m_bodies = new Dictionary<Entity, Body>();
		private readonly BpleLightCollision m_collision = new BpleLightCollision();
		private readonly List<LightImpulse> m_impulses = new List<LightImpulse>();
		private readonly List<Source> m_candidates = new List<Source>();
		private readonly struct LightImpulse
		{
			public readonly Source Light;
			public readonly Body Body;
			public readonly Vector2 Position, DeltaPosition, DeltaVelocity;
			public readonly float Torque, Cost;
			public LightImpulse(Source light, Body body, Vector2 position, Vector2 deltaPosition, Vector2 deltaVelocity, float torque, float cost)
			{ Light = light; Body = body; Position = position; DeltaPosition = deltaPosition; DeltaVelocity = deltaVelocity; Torque = torque; Cost = cost; }
		}

		private void CaptureBodies()
		{
			if (PartManager.Instance?.System == null) return;
			using (var query = Em.CreateEntityQuery(ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<PhysicsVelocity>(),
				ComponentType.ReadOnly<PhysicsCollider>(), ComponentType.ReadOnly<PhysicsMass>()))
			using (var entities = query.ToEntityArray(Allocator.Temp))
			{
				foreach (Entity entity in entities)
				{
					var local = Em.GetComponentData<LocalTransform>(entity);
					var mass = Em.GetComponentData<PhysicsMass>(entity);
					if (!m_bodies.TryGetValue(entity, out Body body)) m_bodies[entity] = body = new Body { Entity = entity };
					body.Dynamic = mass.InverseMass > 0;
					body.Mass = body.Dynamic ? 1 / mass.InverseMass : (Em.HasComponent<PartExtensionComponent>(entity)
						? Mathf.Max(.001f, Em.GetComponentData<PartExtensionComponent>(entity).Value.Mass) : 1);
					body.PreviousPosition = local.Position.xy;
					body.PreviousDirection = math.rotate(local.Rotation, new float3(1, 0, 0)).xy;
					if (Em.HasComponent<BpleLightBodyBounds>(entity)) body.Properties = Em.GetComponentData<BpleLightBodyBounds>(entity);
					if (!Em.HasComponent<BpleLightBodyBounds>(entity) || body.Properties.Bounds.R <= 0)
					{
						var bounds = Em.GetComponentData<PhysicsCollider>(entity).Value.Value.CalculateAabb();
						body.Properties.Bounds = new BpleLightBounds { X = bounds.Center.x, Y = bounds.Center.y,
							A = bounds.Extents.x * .5f, B = bounds.Extents.y * .5f, R = math.length(bounds.Extents.xy) * .5f };
						body.Properties.Friction = .6f;
					}
					if (Em.HasComponent<PartFreeTransform>(entity))
					{
						float2 scale = PartFreeTransform.Sanitize(Em.GetComponentData<PartFreeTransform>(entity)).Scale * local.Scale;
						body.Properties.Bounds.X *= scale.x; body.Properties.Bounds.Y *= scale.y;
						body.Properties.Bounds.A *= scale.x; body.Properties.Bounds.B *= scale.y;
						body.Properties.Bounds.R *= math.min(scale.x, scale.y);
					}
				}
			}
			m_removed.Clear();
			foreach (Entity entity in m_bodies.Keys) if (!Em.Exists(entity)) m_removed.Add(entity);
			foreach (Entity entity in m_removed) m_bodies.Remove(entity);
		}

		private void ApplyLightContacts(float dt)
		{
			m_impulses.Clear();
			foreach (Source source in m_sources.Values) source.Shape.Data = new BpleLightCollision.LightData {
				Position0 = source.PreviousPosition, Position1 = source.Position, Direction0 = source.PreviousDirection, Direction1 = source.Direction };
			foreach (Body body in m_bodies.Values)
			{
				if (!body.Dynamic || !Em.Exists(body.Entity)) continue;
				var local = Em.GetComponentData<LocalTransform>(body.Entity);
				Vector2 direction = math.rotate(local.Rotation, new float3(1, 0, 0)).xy;
				var bounds = body.Properties.Bounds;
				var motion = new BpleLightCollision.CCDData {
					Position0 = body.PreviousPosition + BpleLightMath.Transform2(new Vector2(bounds.X, bounds.Y), body.PreviousDirection),
					Position1 = (Vector2)local.Position.xy + BpleLightMath.Transform2(new Vector2(bounds.X, bounds.Y), direction),
					Direction0 = body.PreviousDirection, Direction1 = direction, Bounds = bounds, DeltaTime = dt };
				m_candidates.Clear();
				var alienComponents = new HashSet<int>();
				foreach (Source source in m_sources.Values)
				{
					if (!source.EntityEnabled || source.Shape.Type < 0) continue;
					if (source.Shape.Type == 4 && !alienComponents.Add(source.Component)) continue;
					if (IgnoreOwnCollision(source, body.Entity)) continue;
					m_candidates.Add(source);
				}
				Source previous = null;
				for (int iteration = 0; iteration < 4 && motion.DeltaTime > 1e-7f; iteration++)
				{
					Source closest = null;
					BpleLightCollision.TOIResult hit = default;
					foreach (Source source in m_candidates)
						if (source != previous && m_collision.Cast(source.Shape, ref motion, out var found)
							&& (closest == null || found.TimeOfImpact < hit.TimeOfImpact)) { closest = source; hit = found; }
					if (closest == null) break;
					previous = closest;
					m_impulses.Add(ContactImpulse(closest, body, ref motion, in hit));
					ContactCount++;
					if (UserSettings.Instance.SimulationSettings.BpleLightTriggerExplosions
						&& PartGameplayManager.Instance.Find(body.Entity) is TNTBehavior tnt) tnt.Explode();
				}
			}
			var costs = new Dictionary<(int, int), float>();
			foreach (LightImpulse impulse in m_impulses)
			{
				var key = (impulse.Light.Component, impulse.Light.Shape.Type);
				costs[key] = costs.TryGetValue(key, out float cost) ? cost + impulse.Cost : impulse.Cost;
			}
			foreach (LightImpulse impulse in m_impulses)
			{
				Source source = impulse.Light;
				float cost = costs[(source.Component, source.Shape.Type)];
				float ratio = cost > 0 ? Mathf.Clamp01(source.Electricity / cost) : 1;
				float scale = ratio * Defend(source.Position, impulse.Position, impulse.Cost * ratio * ratio, source.Electricity);
				float spent = impulse.Cost * ratio * ratio;
				if (source.Shape.Type == 4)
				{
					foreach (Source other in m_sources.Values) if (other.Shape.Type == 4 && other.Component == source.Component)
						other.Deficit -= spent / source.ComponentSize;
				}
				else source.Deficit -= spent;
				Entity entity = impulse.Body.Entity;
				if (!Em.Exists(entity)) continue;
				var pose = Em.GetComponentData<LocalTransform>(entity);
				pose.Position += new float3(impulse.DeltaPosition.x, impulse.DeltaPosition.y, 0) * scale;
				Em.SetComponentData(entity, pose);
				float3 delta = new float3(impulse.DeltaVelocity.x, impulse.DeltaVelocity.y, 0) * scale;
				if (UserSettings.Instance.SimulationSettings.BpleLightReaction)
				{
					float mass = PartVariantPhysics.Mass(source.Part);
					float reduced = impulse.Body.Mass * mass / Mathf.Max(.001f, impulse.Body.Mass + mass);
					PartGameplayUtil.AddVelocity(entity, delta * (reduced / impulse.Body.Mass));
					if (mass > 0) PartGameplayUtil.AddVelocity(source.Part, -delta * (reduced / mass));
				}
				else PartGameplayUtil.AddVelocity(entity, delta);
				PartVariantPhysics.Torque(entity, Mathf.Clamp(impulse.Torque, -1, 1), 1, true);
			}
		}

		private static bool IgnoreOwnCollision(Source source, Entity body) => source.EnclosedInto != Entity.Null
			&& Em.Exists(source.EnclosedInto) && Em.HasComponent<ColoredFrame>(source.EnclosedInto)
			&& Em.GetComponentData<ColoredFrame>(source.EnclosedInto).IsTransparent
			&& PartGameplayUtil.ConnectedComponent(body) == source.Component;

		internal static float PowerConsumption(float velocity, float mass)
		{
			float reduced = 2 * mass / (2 + mass), speed = Mathf.Abs(velocity);
			return .75f * (.5f * reduced * speed * speed + 8 * reduced * speed);
		}

		private static LightImpulse ContactImpulse(Source light, Body body, ref BpleLightCollision.CCDData data, in BpleLightCollision.TOIResult result)
		{
			float time = result.TimeOfImpact;
			float omega = PartVariantPhysics.AngularSpeed(body.Entity);
			float bounce = body.Properties.CombineBounce(0);
			Vector2 contact, deltaPosition, deltaVelocity;
			float torque, cost;
			if (light.Shape.Type <= 1)
			{
				float x = result.ContactNormal.x, y = result.ContactNormal.y;
				Vector2 relative = result.RelativeVelocity;
				Vector2 alignment = BpleLightMath.InvTransform2(data.Direction1, light.Shape.Data.Direction1);
				contact = BpleLightMath.Transform2(alignment, result.ContactPoint);
				if (data.Bounds.IsCircle) contact = new Vector2(0, -data.Bounds.R);
				bool alignedRect = (Mathf.Abs(alignment.x) < .01f || Mathf.Abs(alignment.y) < .01f) && data.Bounds.IsRect;
				float tangent = relative.x / data.DeltaTime, normal = relative.y / data.DeltaTime;
				float tangentAtContact = tangent - omega * contact.y, normalAtContact = normal + omega * contact.x;
				bool lowFriction = light.EnclosedInto != Entity.Null && Em.Exists(light.EnclosedInto)
					&& (Em.HasComponent<ColoredFrame>(light.EnclosedInto) || PartGameplayUtil.TypeInfo(light.EnclosedInto).Equals(BasePart.AlienMetalFrame));
				float friction = body.Properties.CombineFriction(lowFriction ? 0 : .7f, lowFriction);
				float slide = (tangentAtContact > 0 ? -1 : 1) * friction * Mathf.Abs(normalAtContact);
				if ((tangentAtContact + slide > 0) ^ (tangentAtContact > 0)) slide = -tangentAtContact;
				float correction = -(1 - time) * relative.y * (bounce + 1) - result.ContactSeparation;
				deltaPosition = new Vector2(x * correction, y * correction);
				float change = -normal * (bounce + 1);
				deltaVelocity = new Vector2(x * change + y * slide, y * change - x * slide);
				torque = -contact.x * (alignedRect ? 2 * omega * contact.x : normalAtContact) * (bounce + 1) - slide * contact.y;
				cost = PowerConsumption(normal, body.Mass);
				data.Position0 = data.Position0 * (1 - time) + data.Position1 * time;
			}
			else
			{
				Vector2 a = data.Position0 - light.Shape.Data.Position0, b = data.Position1 - light.Shape.Data.Position1;
				Vector2 normal = BpleLightMath.Transform2(result.ContactNormal, data.Direction1);
				float approach = BpleLightMath.Dot2(b - a, normal), tangent = -BpleLightMath.Cross2(b - a, normal);
				contact = BpleLightMath.Transform2(data.Direction1, result.ContactPoint);
				if (data.Bounds.IsCircle) contact = new Vector2(0, -data.Bounds.R);
				float speed = approach / data.DeltaTime, contactSpeed = speed + omega * contact.x;
				float separation = -result.ContactSeparation;
				float correction = -(1 - time) * approach * (bounce + 1) + separation;
				float change = -speed * (bounce + 1);
				if (light.Shape.Type == 3)
				{
					float curvature = .5f * tangent * tangent / (light.Shape.Length - light.Shape.HalfWidth);
					correction += (1 - time) * curvature; change += curvature / data.DeltaTime;
				}
				deltaPosition = normal * correction; deltaVelocity = normal * change;
				torque = -contact.x * (result.ContactCount == 2 ? 2 * omega * contact.x : contactSpeed) * (bounce + 1);
				cost = PowerConsumption(speed, body.Mass);
				data.Position0 = data.Position0 * (1 - time) + data.Position1 * time + separation * normal;
			}
			data.Position1 += deltaPosition;
			data.DeltaTime *= 1 - time; data.Time += (1 - data.Time) * time;
			return new LightImpulse(light, body, data.Position1, deltaPosition, deltaVelocity, torque, cost);
		}

		private float Defend(Vector2 from, Vector2 to, float work, float electricity)
		{
			var components = new Dictionary<int, List<Source>>();
			float total = 0;
			foreach (Source source in m_sources.Values)
			{
				if (!source.EntityEnabled || source.Shape.Type != 4 || source.Electricity <= 0) continue;
				float radius = source.Shape.Length - source.Shape.HalfWidth;
				if ((to - source.Position).sqrMagnitude >= radius * radius || (from - source.Position).sqrMagnitude <= radius * radius) continue;
				if (!components.TryGetValue(source.Component, out var list))
				{ components[source.Component] = list = new List<Source>(); total += Mathf.Sqrt(source.Electricity); }
				list.Add(source);
			}
			float fraction = electricity > 0 ? Mathf.Clamp(.4f * total / Mathf.Sqrt(electricity), 0, .7f) : .7f;
			if (total > 0)
				foreach (var list in components.Values)
					foreach (Source source in list) source.Deficit -= work * fraction / total * Mathf.Sqrt(source.Electricity) / list.Count;
			return 1 - fraction;
		}

		private void ApplyDecelerationLights(float dt)
		{
			var changes = new Dictionary<Entity, float3>();
			foreach (Source source in m_sources.Values)
			{
				if (source.Info.PartType != PartType.PointLight || source.Info.PartIndex != 5 || !source.Activated) continue;
				if (!PartVariantPhysics.IsAssembly(source.Part))
				{ PartGameplayManager.Instance.Find(source.Part)?.Trigger(); continue; }
				if (!m_bodies.TryGetValue(source.Part, out var emitter)) continue;
				Vector2 position = Em.GetComponentData<LocalTransform>(source.Part).Position.xy;
				float3 velocity = PartGameplayUtil.LinearVelocity(source.Part);
				foreach (Body body in m_bodies.Values)
				{
					if (body.Entity == source.Part || !Em.Exists(body.Entity) || (!emitter.Dynamic && !body.Dynamic)) continue;
					float distance = Vector2.Distance(position, Em.GetComponentData<LocalTransform>(body.Entity).Position.xy);
					if (distance >= 8) continue;
					// The original uses the emitter mass in both factors here.
					float coefficient = .75f * emitter.Mass * (distance > 4 ? 8 / distance - 1 : 1);
					float3 force = (PartGameplayUtil.LinearVelocity(body.Entity) - velocity) * coefficient;
					float magnitude = math.length(force.xy);
					if (magnitude > 150) force *= 150 / magnitude;
					if (emitter.Dynamic) changes[source.Part] = (changes.TryGetValue(source.Part, out var oldEmitter) ? oldEmitter : float3.zero) + force * (dt / emitter.Mass);
					if (body.Dynamic) changes[body.Entity] = (changes.TryGetValue(body.Entity, out var oldBody) ? oldBody : float3.zero) - force * (dt / body.Mass);
				}
			}
			foreach (var pair in changes) PartGameplayUtil.AddVelocity(pair.Key, pair.Value);
		}
	}
}
