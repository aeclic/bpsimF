using System.Collections.Generic;
using Bpsim.Parts.Simulation;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

namespace Bpsim.Parts.Gameplay
{
	public sealed partial class KickerBehavior
	{
		private bool m_fired;
		private float m_cooling;
		private bool m_enclosed;
		private readonly List<Entity> m_originalParts = new List<Entity>();
		private readonly Dictionary<Entity, Entity> m_links = new Dictionary<Entity, Entity>();
		private readonly List<Entity> m_stale = new List<Entity>();
		public bool Fired => m_fired;
		public int LinkCount => m_links.Count;
		private bool Elastic => Info.PartIndex == 2 || Info.PartIndex == 4;
		public override void Init()
		{
			Enabled = false;
			m_enclosed = Em.HasComponent<PartContainerValue>(Part) && PartGameplayUtil.Alive(Em.GetComponentData<PartContainerValue>(Part).Value);
		}
		public override void Trigger()
		{
			if (Info.PartIndex == 3 || m_enclosed || (m_fired && Info.PartIndex == 0)) return;
			if (!m_fired)
			{
				m_fired = true;
				// Only fixed welds belonging to this separator are released; ropes/hinges survive.
				foreach (Entity joint in PartGameplayUtil.Simulator.Joints)
				{
					if (!Em.Exists(joint) || !Em.HasComponent<PhysicsJoint>(joint)) continue;
					var pair = Em.GetComponentData<PhysicsConstrainedBodyPair>(joint);
					if (pair.EntityA != Part && pair.EntityB != Part) continue;
					if (!IsFixedJoint(joint)) continue;
					Entity other = pair.EntityA == Part ? pair.EntityB : pair.EntityA;
					if (IsEnclosedSeparator(other)) continue;
					if (!m_originalParts.Contains(other)) m_originalParts.Add(other);
					TNTDamage.BreakJoint(joint);
				}
				if (MegaStructure.Active) MegaStructure.RequestDetach(Part);
				TNTDamage.RefreshTopology();
				return;
			}
			Enabled = !Enabled;
			if (!Enabled) RemoveLinks();
		}
		private bool IsEnclosedSeparator(Entity other)
		{
			if (PartGameplayUtil.TypeInfo(other).PartType == PartType.Kicker && Em.HasComponent<PartContainerValue>(other)
				&& PartGameplayUtil.Alive(Em.GetComponentData<PartContainerValue>(other).Value)) return true;
			return Em.HasComponent<ContainedPart>(other) && PartGameplayUtil.Alive(Em.GetComponentData<ContainedPart>(other).Value)
				&& PartGameplayUtil.TypeInfo(Em.GetComponentData<ContainedPart>(other).Value).PartType == PartType.Kicker;
		}
		private void RemoveLinks()
		{
			foreach (Entity joint in m_links.Values) TNTDamage.BreakJoint(joint);
			m_links.Clear();
		}
		public override void Tick(float dt)
		{
			if (!Enabled || m_enclosed) return;
			m_stale.Clear();
			foreach (var link in m_links)
				if (!PartGameplayUtil.Alive(link.Key) || !Em.Exists(link.Value)
					|| (Elastic && math.distance(PartVariantPhysics.Position(Part), PartVariantPhysics.Position(link.Key)) >= 64f)) m_stale.Add(link.Key);
			foreach (Entity part in m_stale)
			{
				TNTDamage.BreakJoint(m_links[part]);
				m_links.Remove(part);
			}
			if (Info.PartIndex == 1)
			{
				m_cooling -= dt;
				if (m_cooling > 0 || m_links.Count >= 2) return;
				foreach (Entity other in PartGameplayUtil.Simulator.Parts)
				{
					if (other == Part || m_links.ContainsKey(other) || !PartGameplayUtil.Alive(other)
						|| (PartGameplayUtil.TypeInfo(other).PartType == PartType.Rope && PartVariantPhysics.Mass(other) <= 0)
						|| HasFixedLink(other)) continue;
					if (math.distancesq(PartVariantPhysics.Position(Part).xy, PartVariantPhysics.Position(other).xy) > 1.44f) continue;
					var settings = UserSettings.Instance.SimulationSettings;
					float strength = Em.GetComponentData<PartExtensionComponent>(Part).Value.ConnectionStrength
						+ Em.GetComponentData<PartExtensionComponent>(other).Value.ConnectionStrength;
					Entity joint = PartGameplayUtil.CreateFixedJoint(Part, PartVariantPhysics.Position(Part), PartVariantPhysics.Rotation(Part),
						other, PartVariantPhysics.Position(other), PartVariantPhysics.Rotation(other),
						settings.InfiniteConnectionStrength ? float.PositiveInfinity : strength * settings.ConnectionStrengthFactor * dt);
					Register(joint);
					m_links.Add(other, joint);
					m_cooling = .2f;
					break;
				}
			}
			else if (Elastic)
			{
				foreach (Entity other in m_originalParts)
				{
					if (!PartGameplayUtil.Alive(other)) continue;
					float3 delta = PartVariantPhysics.Position(Part) - PartVariantPhysics.Position(other); delta.z = 0;
					float distance = math.length(delta);
					if (distance >= 64 || distance < 1E-5f) continue;
					if (!m_links.ContainsKey(other)) m_links.Add(other, CreateLogicalLink(other));
					float selfMass = PartVariantPhysics.Mass(Part), otherMass = PartVariantPhysics.Mass(other);
					if (selfMass <= 0 || otherMass <= 0) continue;
					float3 relative = PartGameplayUtil.LinearVelocity(Part) - PartGameplayUtil.LinearVelocity(other);
					float weight = distance > 32 ? (64 - distance) / 32 : 1;
					if (Info.PartIndex == 2)
					{
						float speed = math.clamp(math.dot(delta, relative) / distance * weight, -20, 20);
						float stretch = math.clamp((distance - 1) * weight, -.4f, .4f);
						float3 correction = delta / distance * (speed + stretch / dt);
						PartGameplayUtil.AddVelocity(Part, -correction * (otherMass / (selfMass + otherMass)));
						PartGameplayUtil.AddVelocity(other, correction * (selfMass / (selfMass + otherMass)));
					}
					else
					{
						float3 correction = (32 * relative + 128 * delta) * weight;
						float step = dt * math.min(1, 250 / math.max(math.length(correction) * otherMass, 1E-5f));
						PartGameplayUtil.AddVelocity(Part, -correction * (step * otherMass / selfMass));
						PartGameplayUtil.AddVelocity(other, correction * step);
					}
				}
			}
		}
		private bool IsFixedJoint(Entity joint)
		{
			if (!Em.Exists(joint) || !Em.HasComponent<PhysicsJoint>(joint)) return false;
			var constraints = Em.GetComponentData<PhysicsJoint>(joint).GetConstraints();
			if (constraints.Length != 2) return false;
			bool linear = false, angular = false;
			foreach (var constraint in constraints)
			{
				if (constraint.Dimension != 3 || constraint.Min != 0 || constraint.Max != 0) return false;
				linear |= constraint.Type == ConstraintType.Linear;
				angular |= constraint.Type == ConstraintType.Angular;
			}
			return linear && angular;
		}
		private bool HasFixedLink(Entity other)
		{
			foreach (Entity joint in PartGameplayUtil.Simulator.Joints)
			{
				if (!IsFixedJoint(joint)) continue;
				var pair = Em.GetComponentData<PhysicsConstrainedBodyPair>(joint);
				if ((pair.EntityA == Part && pair.EntityB == other) || (pair.EntityB == Part && pair.EntityA == other)) return true;
			}
			return false;
		}
		private Entity CreateLogicalLink(Entity other)
		{
			// Empty constraints carry logical connectivity without welding the elastic bodies together.
			Entity joint = Em.CreateEntity();
			Em.SetName(joint, "SeparatorLogicalLink");
			Em.AddComponentData(joint, default(PhysicsJoint));
			Em.AddComponentData(joint, new PhysicsConstrainedBodyPair(Part, other, false));
			Em.AddComponentData(joint, new PartJointInfo { State = 1 });
			Em.AddSharedComponent(joint, new PhysicsWorldIndex(0));
			PartGameplayUtil.Simulator.Joints.Add(joint);
			TNTDamage.RefreshTopology();
			return joint;
		}
		private void Register(Entity joint)
		{
			Em.SetComponentData(joint, new PartJointInfo { State = 1 });
			PartGameplayUtil.Simulator.Joints.Add(joint);
			TNTDamage.RefreshTopology();
		}
		public override void Dispose() { m_links.Clear(); m_originalParts.Clear(); }
		public override string Describe() => "kicker skin=" + Info.PartIndex + " fired=" + m_fired + " enabled=" + Enabled + " links=" + m_links.Count;
	}
}
