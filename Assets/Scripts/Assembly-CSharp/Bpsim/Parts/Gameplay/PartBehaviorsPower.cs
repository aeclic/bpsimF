using System.Collections.Generic;
using Bpsim.Parts.Simulation;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;

namespace Bpsim.Parts.Gameplay
{
	/// <summary>
	/// Applies a velocity change to a whole welded assembly.  A bpsim part is one
	/// rigid body of a compound, so a force applied to the balloon body alone would
	/// be absorbed by the joints; BPSS instead pulls the whole contraption through
	/// the balloon's spring joint.  The extra damping factor keeps very large builds
	/// from levitating on a single balloon.
	/// </summary>
	internal static class ComponentThrust
	{
		/// <summary>
		/// A part only reacts to an impulse when it owns a dynamic body.  Kinematic
		/// anchors (WoodenFrame) and massless parts (the balloon template has mass 0)
		/// have InverseMass 0, so writing PhysicsVelocity into them does nothing.
		/// </summary>
		public static bool Movable(Entity entity)
		{
			EntityManager em = PartGameplayUtil.Em;
			if (!PartGameplayUtil.Alive(entity) || !em.HasComponent<PhysicsMass>(entity))
			{
				return false;
			}
			return em.GetComponentData<PhysicsMass>(entity).InverseMass > 0f;
		}

		/// <summary>Sum of the dynamic masses of the welded assembly around a part.</summary>
		public static float TotalMass(Entity part)
		{
			PartManager manager = PartManager.Instance;
			if (manager == null || manager.PartSimulator == null)
			{
				return 0f;
			}
			PartSimulatorUnmanaged simulator = manager.PartSimulator.Unmanaged;
			int id = PartGameplayUtil.ConnectedComponent(part);
			EntityManager em = PartGameplayUtil.Em;
			float mass = 0f;
			for (int i = 0; i < simulator.Parts.Length; i++)
			{
				Entity candidate = simulator.Parts[i];
				if (!Movable(candidate) || PartGameplayUtil.ConnectedComponent(candidate) != id)
				{
					continue;
				}
				float inverseMass = em.GetComponentData<PhysicsMass>(candidate).InverseMass;
				if (inverseMass > 0f)
				{
					mass += 1f / inverseMass;
				}
			}
			return mass;
		}

		/// <summary>Highest upward speed among the dynamic parts around a balloon.</summary>
		public static float NearbyRiseSpeed(Entity part, float radius)
		{
			PartManager manager = PartManager.Instance;
			if (manager == null || manager.PartSimulator == null)
			{
				return 0f;
			}
			PartSimulatorUnmanaged simulator = manager.PartSimulator.Unmanaged;
			float3 origin = PartGameplayUtil.Position(part);
			float radiusSq = radius * radius;
			float best = 0f;
			for (int i = 0; i < simulator.Parts.Length; i++)
			{
				Entity candidate = simulator.Parts[i];
				if (!Movable(candidate))
				{
					continue;
				}
				if (math.lengthsq(PartGameplayUtil.Position(candidate) - origin) > radiusSq)
				{
					continue;
				}
				best = math.max(best, PartGameplayUtil.LinearVelocity(candidate).y);
			}
			return best;
		}

		public static void Apply(Entity part, float3 delta)
		{
			PartManager manager = PartManager.Instance;
			if (manager == null || manager.PartSimulator == null)
			{
				return;
			}
			PartSimulatorUnmanaged simulator = manager.PartSimulator.Unmanaged;
			int id = PartGameplayUtil.ConnectedComponent(part);
			if (id < 0)
			{
				if (Movable(part))
				{
					PartGameplayUtil.AddVelocity(part, delta);
				}
				return;
			}
			int count = 0;
			for (int i = 0; i < simulator.Parts.Length; i++)
			{
				Entity candidate = simulator.Parts[i];
				if (Movable(candidate) && PartGameplayUtil.ConnectedComponent(candidate) == id)
				{
					count++;
				}
			}
			if (count <= 0)
			{
				return;
			}
			float scale = 1f / (1f + 0.15f * (count - 1));
			float3 scaled = delta * scale;
			for (int j = 0; j < simulator.Parts.Length; j++)
			{
				Entity candidate = simulator.Parts[j];
				if (Movable(candidate) && PartGameplayUtil.ConnectedComponent(candidate) == id)
				{
					PartGameplayUtil.AddVelocity(candidate, scaled);
				}
			}
		}

		/// <summary>
		/// Balloons cannot be welded in bpsim (their extension data has
		/// connectionDirection None and mass 0), so BPSS's "spring joint to the
		/// chassis" is emulated by lifting every dynamic part that sits right next to
		/// the balloon.
		/// </summary>
		public static void LiftNearby(Entity part, float radius, float3 delta)
		{
			PartManager manager = PartManager.Instance;
			if (manager == null || manager.PartSimulator == null)
			{
				return;
			}
			PartSimulatorUnmanaged simulator = manager.PartSimulator.Unmanaged;
			float3 origin = PartGameplayUtil.Position(part);
			float radiusSq = radius * radius;
			int count = 0;
			for (int i = 0; i < simulator.Parts.Length; i++)
			{
				Entity candidate = simulator.Parts[i];
				if (!Movable(candidate))
				{
					continue;
				}
				float3 deltaPosition = PartGameplayUtil.Position(candidate) - origin;
				if (math.lengthsq(deltaPosition) <= radiusSq)
				{
					count++;
				}
			}
			if (count <= 0)
			{
				return;
			}
			float scale = 1f / (1f + 0.15f * (count - 1));
			float3 scaled = delta * scale;
			for (int j = 0; j < simulator.Parts.Length; j++)
			{
				Entity candidate = simulator.Parts[j];
				if (!Movable(candidate))
				{
					continue;
				}
				float3 deltaPosition = PartGameplayUtil.Position(candidate) - origin;
				if (math.lengthsq(deltaPosition) <= radiusSq)
				{
					PartGameplayUtil.AddVelocity(candidate, scaled);
				}
			}
		}
	}

	/// <summary>
	/// BPSS Balloon / Balloons2 / Balloons3 (气球): each balloon body carries a
	/// constant upward lift force and its vertical speed is limited, so a cluster
	/// of balloons lifts the attached contraption.  BPSS hangs the balloon off the
	/// chassis with a spring joint; bpsim's balloon template has mass 0 and cannot
	/// be connected (connectionDirection None), so the lift is applied to the
	/// dynamic parts around it instead.
	/// </summary>
	public sealed class BalloonBehavior : PartBehavior
	{
		private const float Gravity = 9.81f;

		// Net lift per balloon bubble, expressed in g.  BPSS uses 10 N on a 0.1 kg
		// balloon with 2.0 drag, which settles around 1.5 g of net acceleration.
		private const float NetLift = 1.55f;

		private const float MaxRiseSpeed = 5.5f;

		// BPSS balloons are attached to the part they hang from; the bpsim grid uses
		// one cell per part, so anything within this radius counts as attached.
		private const float AttachRadius = 1.6f;

		// One filled balloon, two or three when the variant holds more bubbles.
		private float m_bubbles = 1f;

		private int m_ticks;

		public override void Init()
		{
			m_bubbles = Info.PartType switch
			{
				PartType.Balloons2 => 2f,
				PartType.Balloons3 => 3f,
				_ => 1f,
			};
			Enabled = true;
		}

		public override void Trigger()
		{
			// BPSS pops the balloon when it is touched; the button acts as a pop.
			Enabled = !Enabled;
			Manager.Log("balloon: " + (Enabled ? "inflated" : "popped") + " bubbles=" + m_bubbles);
		}

		public override void Tick(float deltaTime)
		{
			if (!Enabled)
			{
				return;
			}
			m_ticks++;
			// The balloon body itself is massless in bpsim, so its own velocity is
			// always zero; the speed limit must look at the parts it is lifting.
			float riseSpeed = ComponentThrust.NearbyRiseSpeed(Part, AttachRadius);
			if (riseSpeed >= MaxRiseSpeed)
			{
				return;
			}
			float acceleration = Gravity * NetLift * m_bubbles;
			float delta = acceleration * deltaTime;
			float remaining = MaxRiseSpeed - riseSpeed;
			if (delta > remaining)
			{
				delta = math.max(0f, remaining);
			}
			ComponentThrust.LiftNearby(Part, AttachRadius, new float3(0f, delta, 0f));
		}

		public override string Describe()
		{
			float riseSpeed = ComponentThrust.NearbyRiseSpeed(Part, AttachRadius);
			return "balloon bubbles=" + m_bubbles + " enabled=" + Enabled + " rise=" + riseSpeed.ToString("F2") + " ticks=" + m_ticks;
		}
	}

	/// <summary>
	/// BPSS Umbrella: canopy stroke and seven aerodynamic/control modes.
	/// Implementation lives in PartBehaviorsAero.cs.
	/// </summary>
	public sealed partial class UmbrellaBehavior : PartBehavior
	{
	}

	/// <summary>
	/// BPSS PoweredUmbrella (动力雨伞): same canopy, but the powered variant
	/// pushes the contraption upwards while it is switched on.
	/// </summary>
	/// <summary>
	/// Per-fixed-step cache of the per-connected-component power aggregation that
	/// PoweredUmbrellaBehavior needs (engine power / consumption / part count).
	///
	/// Without it, every powered umbrella rescans ALL parts three times per fixed
	/// step: with 92 umbrellas on a 10 000 part save that is ~2.8 M managed
	/// EntityManager lookups per step, and because a long frame lets Unity catch up
	/// ~16 fixed steps (Fixed Timestep 0.02, Maximum Allowed Timestep 0.333), it
	/// inflated one frame from ~1.5 s to ~20 s.
	///
	/// Semantics are unchanged: the aggregation only depends on component
	/// membership and the engine extension values, which cannot change inside a
	/// single fixed step, so one pass per step is exactly equivalent.
	/// </summary>
	public static class ComponentPowerCache
	{
		public struct Summary
		{
			public float EnginePower;
			public float Consumption;
			public int Parts;
		}

		/// <summary>False restores the old per-umbrella rescan behaviour (for A/B tests).</summary>
		public static bool Enabled = true;

		private static readonly Dictionary<int, Summary> s_map = new Dictionary<int, Summary>();
		private static int s_token = int.MinValue;
		private static int s_tokenNext;

		public static void SetEnabled(bool enabled)
		{
			Enabled = enabled;
		}

		public static string Status()
		{
			return "enabled=" + Enabled + " components=" + s_map.Count + " token=" + s_token + "/" + s_tokenNext;
		}

		/// <summary>Called once per fixed step by PartGameplayManager before ticking behaviours.</summary>
		public static void AdvanceToken()
		{
			s_tokenNext++;
		}

		public static bool TryGet(int index, out Summary summary)
		{
			summary = default(Summary);
			if (!Enabled)
			{
				return false;
			}
			PartManager manager = PartManager.Instance;
			if (manager == null || manager.PartSimulator == null)
			{
				return false;
			}
			if (s_token != s_tokenNext)
			{
				Rebuild(manager.PartSimulator.Unmanaged);
				s_token = s_tokenNext;
			}
			return s_map.TryGetValue(index, out summary);
		}

		private static void Rebuild(PartSimulatorUnmanaged simulator)
		{
			s_map.Clear();
			EntityManager em = PartGameplayUtil.Em;
			Unity.Collections.NativeList<Entity> parts = simulator.Parts;
			for (int i = 0; i < parts.Length; i++)
			{
				Entity candidate = parts[i];
				if (!PartGameplayUtil.Alive(candidate))
				{
					continue;
				}
				int index = PartGameplayUtil.ConnectedComponent(candidate);
				Summary summary;
				s_map.TryGetValue(index, out summary);
				summary.Parts++;
				if (em.HasComponent<PartExtensionComponent>(candidate))
				{
					PartExtensionComponent extension = em.GetComponentData<PartExtensionComponent>(candidate);
					summary.EnginePower += extension.Value.EnginePower;
					summary.Consumption += extension.Value.PowerConsumption;
				}
				s_map[index] = summary;
			}
		}
	}

	public sealed class PoweredUmbrellaBehavior : PartBehavior
	{
		private const float Gravity = 9.81f;

		// BPSS: m_force = 80 on a 0.2 kg canopy and m_maximumForce = m_force * power
		// factor, applied as a real force to the welded body.
		private const float MaxForce = 80f;

		private const float MaxLiftAcceleration = 40f;

		private const float MaxRiseSpeed = 4.5f;

		private const float MaxFallSpeed = 3.2f;

		private int m_ticks;

		private float m_power = 1f;

		private float m_enginePower;

		private float m_consumption;

		private int m_componentParts;

		/// <summary>
		/// BPSS gates the powered umbrella on the contraption's engine power
		/// (CanBeEnabled => m_hasPower).  The same aggregation exists in bpsim as
		/// ConnectedComponentInfo.EnginePower / PowerConsumption.
		/// </summary>
		private void RefreshPower()
		{
			PartManager manager = PartManager.Instance;
			if (manager == null || manager.PartSimulator == null)
			{
				m_power = 0f;
				return;
			}
			PartSimulatorUnmanaged simulator = manager.PartSimulator.Unmanaged;
			int index = PartGameplayUtil.ConnectedComponent(Part);
			EntityManager em = PartGameplayUtil.Em;
			// Sum the extension data of the whole assembly directly: the aggregated
			// ConnectedComponents list is only refreshed by the simulation step and is
			// empty on the frame the contraption is built.
			float enginePower = 0f;
			float consumption = 0f;
			int componentParts = 0;
			ComponentPowerCache.Summary powerSummary;
			if (ComponentPowerCache.TryGet(index, out powerSummary))
			{
				enginePower = powerSummary.EnginePower;
				consumption = powerSummary.Consumption;
				componentParts = powerSummary.Parts;
			}
			else
			{
				// Legacy path (rescan every part, per umbrella): only for A/B tests.
				for (int i = 0; i < simulator.Parts.Length; i++)
				{
					Entity candidate = simulator.Parts[i];
					if (!PartGameplayUtil.Alive(candidate) || PartGameplayUtil.ConnectedComponent(candidate) != index)
					{
						continue;
					}
					if (!em.HasComponent<PartExtensionComponent>(candidate))
					{
						continue;
					}
					PartExtensionComponent extension = em.GetComponentData<PartExtensionComponent>(candidate);
					enginePower += extension.Value.EnginePower;
					consumption += extension.Value.PowerConsumption;
				}
				for (int i = 0; i < simulator.Parts.Length; i++)
				{
					if (PartGameplayUtil.Alive(simulator.Parts[i]) && PartGameplayUtil.ConnectedComponent(simulator.Parts[i]) == index)
					{
						componentParts++;
					}
				}
			}
			// Engine parts declare connectionDirection None in the extension map, so the
			// joint graph may leave them in their own component.  BPSS treats the engine
			// as powering everything it is physically attached to, so fall back to a
			// spatial aggregation when the welded component has no engine of its own.
			if (enginePower <= 0f)
			{
				float3 origin = PartGameplayUtil.Position(Part);
				for (int i = 0; i < simulator.Parts.Length; i++)
				{
					Entity candidate = simulator.Parts[i];
					if (!PartGameplayUtil.Alive(candidate) || !em.HasComponent<PartExtensionComponent>(candidate))
					{
						continue;
					}
					if (math.distance(PartGameplayUtil.Position(candidate), origin) > 1.8f)
					{
						continue;
					}
					PartExtensionComponent extension = em.GetComponentData<PartExtensionComponent>(candidate);
					enginePower += extension.Value.EnginePower;
					consumption += extension.Value.PowerConsumption;
				}
			}
			m_enginePower = enginePower;
			m_consumption = consumption;
			m_componentParts = componentParts;
			float factor = 0f;
			if (consumption > 1f)
			{
				factor = math.min(enginePower / consumption, 20f);
			}
			else if (enginePower > 0f)
			{
				factor = 1f;
			}
			m_power = (factor > 1f) ? math.pow(factor, 0.75f) : factor;
		}

		public override void Init()
		{
			RefreshPower();
			Enabled = false;
		}

		public override void Trigger()
		{
			RefreshPower();
			if (m_power <= 0f)
			{
				Manager.Log("poweredumbrella: no engine power");
				return;
			}
			Enabled = !Enabled;
			Manager.Log("poweredumbrella: " + (Enabled ? "enabled" : "disabled") + " power=" + m_power.ToString("F2"));
		}

		public override void Tick(float deltaTime)
		{
			// The engine-power aggregation is only filled once the simulation has
			// stepped, which happens after Begin(), so keep it fresh here.
			RefreshPower();
			float3 velocity = PartGameplayUtil.LinearVelocity(Part);
			if (!Enabled)
			{
				return;
			}
			m_ticks++;
			if (velocity.y < -MaxFallSpeed)
			{
				ComponentThrust.Apply(Part, new float3(0f, math.min(-velocity.y - MaxFallSpeed, 8f * deltaTime), 0f));
				return;
			}
			if (velocity.y >= MaxRiseSpeed)
			{
				return;
			}
			// Real force on the assembly: a = F/m, so a heavy cart climbs slower and a
			// light one climbs faster, exactly like BPSS's AddForce(maximumForce).
			float mass = math.max(ComponentThrust.TotalMass(Part), 0.1f);
			float acceleration = math.min(MaxForce * m_power / mass, MaxLiftAcceleration);
			float delta = acceleration * deltaTime;
			float remaining = MaxRiseSpeed - velocity.y;
			ComponentThrust.Apply(Part, new float3(0f, math.min(delta, math.max(0f, remaining)), 0f));
		}

		public override string Describe()
		{
			float3 velocity = PartGameplayUtil.LinearVelocity(Part);
			return "poweredumbrella enabled=" + Enabled + " power=" + m_power.ToString("F2")
				+ " enginePower=" + m_enginePower.ToString("F1") + " consumption=" + m_consumption.ToString("F1")
				+ " parts=" + m_componentParts + " vy=" + velocity.y.ToString("F2") + " ticks=" + m_ticks;
		}
	}

	/// <summary>
	/// BPSS Spring (弹簧) and SpringBoxingGlove (拳套).  Both parts store energy
	/// and release it along their facing direction: the spring shoves everything
	/// in front of it, the boxing glove punches the part it points at.
	/// </summary>
	public sealed class SpringBehavior : PartBehavior
	{
		private const float Radius = 1.8f;

		private const float Strength = 9f;

		private const float PunchRange = 1.6f;

		private float m_cooldown;

		private int m_punches;

		public override void Trigger()
		{
			if (m_cooldown > 0f)
			{
				return;
			}
			m_cooldown = 0.35f;
			float3 forward = Up();
			if (Info.PartType == PartType.SpringBoxingGlove)
			{
				Entity target = PartGameplayManager.FindTargetInCone(Part, forward, PunchRange, 0.6f);
				if (target != Entity.Null)
				{
					PartGameplayUtil.AddVelocity(target, forward * Strength);
					PartGameplayUtil.AddVelocity(Part, -forward * (Strength * 0.25f));
					m_punches++;
					Manager.Log("boxingglove: punched " + target.Index);
					return;
				}
			}
			PartGameplayManager.ApplyRadialImpulse(Position() + forward * 0.5f, Radius, Strength, Part, Entity.Null);
			m_punches++;
			Manager.Log("spring: released towards (" + forward.x.ToString("F2") + "," + forward.y.ToString("F2") + ")");
		}

		public override void Tick(float deltaTime)
		{
			if (m_cooldown > 0f)
			{
				m_cooldown -= deltaTime;
			}
		}

		public override string Describe()
		{
			return (Info.PartType == PartType.SpringBoxingGlove ? "boxingglove" : "spring") + " punches=" + m_punches;
		}
	}

	/// <summary>
	/// BPSS Kicker: separator, automatic connector, elastic links and team marker.
	/// Implementation lives in PartBehaviorsKicker.cs.
	/// </summary>
	public sealed partial class KickerBehavior : PartBehavior
	{
	}

	/// <summary>
	/// BPSS TimeBomb: touch immediately fires a directional charge.
	/// </summary>
	public sealed class TimeBombBehavior : PartBehavior
	{
		private bool m_exploded;

		public override void Trigger()
		{
			if (m_exploded)
			{
				return;
			}
			m_exploded = true;
			Enabled = false;
			float3 center = PartVariantPhysics.Position(Part);
			PartGameplayUtil.TryGetGrid(Part, out _, out _, out int rotation, out _);
			float3 direction = math.rotate(PartGameplayUtil.GridRotation(rotation, true), new float3(0,-1,0));
			foreach (Entity target in PartGameplayUtil.Simulator.Parts)
			{
				if (target == Part || !PartGameplayUtil.Alive(target)) continue;
				float3 delta = PartVariantPhysics.Position(target) - center; delta.z = 0;
				float distance = math.length(delta), mass = PartVariantPhysics.Mass(target);
				if (distance < 1E-5f || distance > 48 || mass <= 0 || math.dot(delta / distance, direction) < .97f) continue;
				float d = math.max(distance, 1);
				float3 impulse;
				if (d < 5)
				{
					float strength = 56 * 25f;
					strength *= mass < 5.5f ? mass / d : mass < 25 ? (5.5f + .1f * (mass - 5.5f)) / d : 1 / d;
					impulse = delta * strength;
				}
				else
				{
					float strength = 270 * 25f / math.pow(d, 1.5f);
					strength *= mass < .1f ? mass : mass < .4f ? mass / .4f : 1;
					impulse = direction * strength;
				}
				TNTImpulse.Apply(target, impulse);
			}
			Manager.Retire(Part);
			Manager.Log("timebomb: directional explosion");
		}

		public override string Describe()
		{
			return "timebomb exploded=" + m_exploded;
		}
	}

	/// <summary>
	/// BPSS Bellows (风箱): a puffer that blasts a short burst of air in its
	/// facing direction.  The original uses a 0.5 s boost window with a
	/// 1-(1-t/0.5)^2 ramp, then a 0.3 s wait and a 0.3 s re-inflate before it can
	/// fire again; the visual squeezes to a 0.3 scale while deflated.
	/// </summary>
	public sealed class BellowsBehavior : PartBehavior
	{
		private const float BoostForce = 10f;

		private const float BoostDuration = 0.5f;

		private const float WaitDuration = 0.3f;

		private const float InflateDuration = 0.3f;

		private const float CompressedScale = 0.3f;

		private float m_elapsed;

		private bool m_boosting;

		private bool m_spent;

		public override void Trigger()
		{
			if (m_boosting || (m_spent && m_elapsed < BoostDuration + WaitDuration + InflateDuration))
			{
				return;
			}
			m_boosting = true;
			m_spent = true;
			m_elapsed = 0f;
			Enabled = true;
			Manager.Log("bellows: boost");
		}

		public override void Tick(float deltaTime)
		{
			if (!m_spent)
			{
				return;
			}
			m_elapsed += deltaTime;
			if (m_boosting)
			{
				if (m_elapsed > BoostDuration)
				{
					m_boosting = false;
					Enabled = false;
					return;
				}
				// Quadratic ramp: strongest right after the trigger, then fading.
				float t = m_elapsed / BoostDuration;
				float ramp = 1f - (1f - t) * (1f - t);
				// BPSS pushes along transform.TransformDirection(m_direction) with
				// m_direction = Vector3.up, i.e. the part's own local up axis.
				ComponentThrust.Apply(Part, Up() * (ramp * BoostForce * deltaTime));
			}
			else if (m_elapsed >= BoostDuration + WaitDuration + InflateDuration)
			{
				m_spent = false;
			}
		}

		public override string Describe()
		{
			float scale = 1f;
			if (m_spent)
			{
				if (m_elapsed < BoostDuration)
				{
					scale = math.lerp(1f, CompressedScale, m_elapsed / BoostDuration);
				}
				else if (m_elapsed < BoostDuration + WaitDuration)
				{
					scale = CompressedScale;
				}
				else
				{
					scale = math.lerp(CompressedScale, 1f, (m_elapsed - BoostDuration - WaitDuration) / InflateDuration);
				}
			}
			return "bellows boosting=" + m_boosting + " elapsed=" + m_elapsed.ToString("F2") + " scale=" + scale.ToString("F2");
		}
	}

	/// <summary>
	/// BPSS Contraption.GetEnginePowerFactor for the dynamic power system: sum the
	/// engine power and consumption of the welded assembly (with the same spatial
	/// fallback the powered umbrella needs, because engine parts declare
	/// connectionDirection None), then reduce the ratio to the force multiplier.
	/// </summary>
	internal static class PowerFactor
	{
		public static float Compute(Entity part, out float enginePower, out float consumption, out int componentParts)
		{
			enginePower = 0f;
			consumption = 0f;
			componentParts = 0;
			PartManager manager = PartManager.Instance;
			if (manager == null || manager.PartSimulator == null)
			{
				return 0f;
			}
			PartSimulatorUnmanaged simulator = manager.PartSimulator.Unmanaged;
			int index = PartGameplayUtil.ConnectedComponent(part);
			EntityManager em = PartGameplayUtil.Em;
			for (int i = 0; i < simulator.Parts.Length; i++)
			{
				Entity candidate = simulator.Parts[i];
				if (!PartGameplayUtil.Alive(candidate) || PartGameplayUtil.ConnectedComponent(candidate) != index)
				{
					continue;
				}
				componentParts++;
				if (!em.HasComponent<PartExtensionComponent>(candidate))
				{
					continue;
				}
				PartExtensionComponent extension = em.GetComponentData<PartExtensionComponent>(candidate);
				enginePower += extension.Value.EnginePower;
				consumption += extension.Value.PowerConsumption;
			}
			if (enginePower <= 0f)
			{
				float3 origin = PartGameplayUtil.Position(part);
				for (int j = 0; j < simulator.Parts.Length; j++)
				{
					Entity candidate = simulator.Parts[j];
					if (!PartGameplayUtil.Alive(candidate) || !em.HasComponent<PartExtensionComponent>(candidate))
					{
						continue;
					}
					if (math.distance(PartGameplayUtil.Position(candidate), origin) > 1.8f)
					{
						continue;
					}
					PartExtensionComponent extension = em.GetComponentData<PartExtensionComponent>(candidate);
					enginePower += extension.Value.EnginePower;
					consumption += extension.Value.PowerConsumption;
				}
			}
			float factor = 0f;
			if (consumption > 1f)
			{
				factor = math.min(enginePower / consumption, 20f);
			}
			else if (enginePower > 0f)
			{
				factor = 1f;
			}
			return (factor > 1f) ? math.pow(factor, 0.585f) : math.pow(factor, 0.75f);
		}
	}

	/// <summary>
	/// BPSS JetEngine (喷气发动机): 沿 transform.right 推进，而且**必须烧油**。
	/// </summary>
	/// <remarks>
	/// 对照 BPSS <c>JetEngine.cs</c>：
	/// <list type="bullet">
	/// <item><c>RequiredFuelAmount = (|force|/1000*0.2 + 0.1) * dt</c>，其中 0.1*dt 是
	/// "空转消耗"——没油时先扣掉这 0.1*dt 才能看出真正的燃料余量。</item>
	/// <item><c>ConsumeFuel(amount)</c>：<c>supplied = amount - 0.1*dt</c>，<c>&lt;= 0</c> 就
	/// <c>SetEnabled(false)</c>；否则推力按 <c>supplied / (required - 0.1*dt)</c> 缩放。</item>
	/// <item><c>IsTriggerable()</c> 要求所在燃料连通组里至少有一个燃料箱，所以没接油箱时
	/// 按钮点不动（这里在 <see cref="Trigger"/> 里同样拒绝开启）。</item>
	/// </list>
	/// 每步的 <c>Required</c> 写进 <c>FuelConsumer</c>，由 <c>FuelSystem</c> 的 Burst 作业
	/// 按连通组结算后回写 <c>Supplied</c>；两者差一个物理步（原版同样依赖脚本执行顺序）。
	/// </remarks>
	public sealed class JetEngineBehavior : PartBehavior
	{
		private const float Acceleration = 22f;

		private const float MaxSpeed = 30f;

		/// <summary>空转消耗率（BPSS 里那个 0.1 的系数）。</summary>
		private const float IdleRate = 0.1f;

		/// <summary>BPSS 的燃料需求按 |force|/1000*0.2 换算，这里的"力"用加速度代表。</summary>
		private const float ForcePerAcceleration = 0.2f / 1000f;

		private int m_dryTicks;

		/// <summary>
		/// 「刚点开、燃料系统还没为这一步结算」的宽限标记。
		/// </summary>
		/// <remarks>
		/// 驱动顺序是 MonoBehaviour.FixedUpdate（本 Tick 写 Required/Requesting）
		/// → FuelSystem（Burst 作业结算并回写 Supplied）。所以点开的**第一帧**读到的
		/// <c>Supplied</c> 必然是上一帧的 0，直接按 BPSS 的 <c>supplied &lt;= 0</c> 判定会
		/// 立刻自我熄火（实测 dryTicks=1）。这里只在「还没见过一次非零供给」时放行等待。
		/// </remarks>
		private bool m_awaitingSupply;

		public override void Init()
		{
			Enabled = false;
			if (!Em.HasComponent<FuelConsumer>(Part))
			{
				Em.AddComponentData(Part, default(FuelConsumer));
			}
			FuelSystem.MarkDirty();
		}

		public override void Trigger()
		{
			if (!Enabled)
			{
				// BPSS JetEngine.IsTriggerable()：组里没有燃料箱就不给开。
				if (!HasFuelSource())
				{
					Manager.Log("jetengine: no fuel source in group, refusing to start");
					return;
				}
			}
			Enabled = !Enabled;
			m_dryTicks = 0;
			m_awaitingSupply = Enabled;
			Manager.Log("jetengine: " + (Enabled ? "enabled" : "disabled"));
		}

		private bool HasFuelSource()
		{
			if (!Em.HasComponent<FuelGroupIndex>(Part) || FuelSystem.Instance == null)
			{
				return false;
			}
			return FuelSystem.Instance.TryGetState(Em.GetComponentData<FuelGroupIndex>(Part).Value, out FuelGroupState state)
				&& state.Containers > 0;
		}

		public override void Tick(float deltaTime)
		{
			if (!Em.HasComponent<FuelConsumer>(Part))
			{
				return;
			}
			FuelConsumer consumer = Em.GetComponentData<FuelConsumer>(Part);
			if (!Enabled)
			{
				if (consumer.Requesting != 0 || consumer.Required != 0f)
				{
					consumer.Requesting = 0;
					consumer.Required = 0f;
					Em.SetComponentData(Part, consumer);
				}
				return;
			}
			float required = (Acceleration * ForcePerAcceleration + IdleRate) * deltaTime;
			float supplied = consumer.Supplied;
			consumer.Requesting = 1;
			consumer.Required = required;
			Em.SetComponentData(Part, consumer);

			float idle = IdleRate * deltaTime;
			float usable = supplied - idle;
			if (usable <= 0f)
			{
				if (m_awaitingSupply)
				{
					// 第一帧 Supplied 还是上一帧的 0（见 m_awaitingSupply 注释），
					// 这一步只等结算结果，不推进、也不熄火。
					return;
				}
				m_dryTicks++;
				Enabled = false;
				consumer.Requesting = 0;
				consumer.Required = 0f;
				Em.SetComponentData(Part, consumer);
				Manager.Log("jetengine: out of fuel after " + m_dryTicks + " ticks, shutting down");
				return;
			}
			m_awaitingSupply = false;
			float3 forward = Forward();
			float speed = math.dot(PartGameplayUtil.LinearVelocity(Part), forward);
			if (speed >= MaxSpeed)
			{
				return;
			}
			float denominator = required - idle;
			float ratio = (denominator > 1E-06f) ? math.saturate(usable / denominator) : 1f;
			float accel = Acceleration * math.max(0f, 1f - speed / MaxSpeed) * ratio;
			PartGameplayUtil.AddVelocity(Part, forward * (accel * deltaTime));
		}

		public override string Describe()
		{
			float speed = math.dot(PartGameplayUtil.LinearVelocity(Part), Forward());
			FuelConsumer consumer = Em.HasComponent<FuelConsumer>(Part) ? Em.GetComponentData<FuelConsumer>(Part) : default(FuelConsumer);
			return "jetengine enabled=" + Enabled + " speed=" + speed.ToString("F2")
				+ " required=" + consumer.Required.ToString("F4")
				+ " supplied=" + consumer.Supplied.ToString("F4")
				+ " requesting=" + (consumer.Requesting != 0)
				+ " dryTicks=" + m_dryTicks;
		}
	}

	/// <summary>
	/// BPSS wings and tails: distinct lift curves with actual body rotation,
	/// mirrored orientation and relative airflow. See PartBehaviorsAero.cs.
	/// </summary>
	public sealed partial class WingBehavior : PartBehavior
	{
	}
}
