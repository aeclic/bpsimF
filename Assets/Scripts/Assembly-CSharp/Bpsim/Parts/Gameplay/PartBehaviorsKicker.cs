using System.Collections.Generic;
using Bpsim.Parts.Simulation;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

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
		private readonly Dictionary<Entity, Entity> m_revoluteLinks = new Dictionary<Entity, Entity>();
		private readonly List<Entity> m_revoluteStale = new List<Entity>();
		private const int RevoluteVariantIndex = 9000;
		public bool Fired => m_fired;
		public int LinkCount => m_links.Count;
		private bool Elastic => Info.PartIndex == 2 || Info.PartIndex == 4;
		private bool IsRevoluteVariant => Info.PartIndex == RevoluteVariantIndex;
		public override void Init()
		{
			Enabled = false;
			m_enclosed = IsEnclosed();
			if (IsRevoluteVariant) EnsureRevoluteLinks();
		}
		public override void Trigger()
		{
			if (IsRevoluteVariant || Info.PartIndex == 3 || m_enclosed || (m_fired && Info.PartIndex == 0)) return;
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
			if (IsRevoluteVariant)
			{
				m_enclosed = IsEnclosed();
				if (m_enclosed) EnsureRevoluteLinks();
				else RemoveRevoluteLinks();
				return;
			}
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
		private bool IsEnclosed()
		{
			if (!Em.HasComponent<PartContainerValue>(Part)) return false;
			Entity container = Em.GetComponentData<PartContainerValue>(Part).Value;
			return PartGameplayUtil.Alive(container) && BasePart.IsContainer(PartGameplayUtil.TypeInfo(container));
		}
		private void EnsureRevoluteLinks()
		{
			if (!IsRevoluteVariant || !m_enclosed || !PartGameplayUtil.TryGetGrid(Part, out int gridX, out int gridY, out _, out _)) return;
			byte layer = PartBuildLayerUtility.GetLayer(Em, Part);
			m_revoluteStale.Clear();
			foreach (KeyValuePair<Entity, Entity> link in m_revoluteLinks)
			{
				if (!PartGameplayUtil.Alive(link.Key) || !Em.Exists(link.Value)) m_revoluteStale.Add(link.Key);
			}
			for (int i = 0; i < 4; i++)
			{
				int2 offset = i switch
				{
					0 => new int2(1, 0),
					1 => new int2(0, 1),
					2 => new int2(-1, 0),
					_ => new int2(0, -1)
				};
				if (!PartGameplayUtil.Simulator.PartMap.TryGet(gridX + offset.x, gridY + offset.y, layer, out PartGridInfo grid)) continue;
				Entity target = grid.HasPart ? grid.Part : grid.PartContainer;
				if (!CanRevoluteLink(target) || m_revoluteLinks.ContainsKey(target)) continue;
				Entity joint = CreateRevoluteJoint(target, new float3(gridX + offset.x, gridY + offset.y, PartGameplayUtil.Position(Part).z));
				if (joint != Entity.Null) m_revoluteLinks.Add(target, joint);
			}
			foreach (Entity target in m_revoluteStale)
			{
				if (m_revoluteLinks.TryGetValue(target, out Entity joint)) TNTDamage.BreakJoint(joint);
				m_revoluteLinks.Remove(target);
			}
			if (m_revoluteStale.Count > 0) TNTDamage.RefreshTopology();
		}
		private bool CanRevoluteLink(Entity target)
		{
			return target != Entity.Null && target != Part && PartGameplayUtil.Alive(target)
				&& Em.HasComponent<PhysicsMass>(target) && Em.HasComponent<LocalTransform>(target)
				&& Em.HasComponent<PhysicsWorldIndex>(target);
		}
		private Entity CreateRevoluteJoint(Entity target, float3 anchorWorld)
		{
			EntityManager em = Em;
			float3 sourcePosition = PartGameplayUtil.Position(Part);
			float3 targetPosition = PartGameplayUtil.Position(target);
			quaternion sourceRotation = PartGameplayUtil.Rotation(Part);
			quaternion targetRotation = PartGameplayUtil.Rotation(target);
			RigidTransform sourceTransform = new RigidTransform(sourceRotation, sourcePosition);
			RigidTransform targetTransform = new RigidTransform(targetRotation, targetPosition);
			RigidTransform targetFromSource = math.mul(math.inverse(targetTransform), sourceTransform);
			BodyFrame frameA = new BodyFrame
			{
				Position = math.transform(math.inverse(sourceTransform), anchorWorld),
				Axis = new float3(0f, 0f, 1f),
				PerpendicularAxis = new float3(1f, 0f, 0f)
			};
			BodyFrame frameB = new BodyFrame
			{
				Position = math.transform(targetFromSource, frameA.Position),
				Axis = math.rotate(targetFromSource.rot, frameA.Axis),
				PerpendicularAxis = math.rotate(targetFromSource.rot, frameA.PerpendicularAxis)
			};
			Entity joint = em.CreateEntity();
			em.SetName(joint, "RevoluteSeparatorJoint");
			em.AddSharedComponent(joint, new PhysicsWorldIndex(0u));
			em.AddComponentData(joint, PhysicsJoint.CreateHinge(frameA, frameB));
			em.AddComponentData(joint, new PhysicsConstrainedBodyPair(Part, target, false));
			em.AddComponentData(joint, new PartJointInfo { Type = PartJointType.Normal, State = 1 });
			PartGameplayUtil.Simulator.Joints.Add(joint);
			TNTDamage.RefreshTopology();
			return joint;
		}
		private void RemoveRevoluteLinks()
		{
			if (m_revoluteLinks.Count == 0) return;
			foreach (Entity joint in m_revoluteLinks.Values) TNTDamage.BreakJoint(joint);
			m_revoluteLinks.Clear();
			TNTDamage.RefreshTopology();
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
		public override void Dispose() { m_links.Clear(); RemoveRevoluteLinks(); m_originalParts.Clear(); }
		public override string Describe() => "kicker skin=" + Info.PartIndex + " fired=" + m_fired + " enabled=" + Enabled + " links=" + m_links.Count + " revolute=" + m_revoluteLinks.Count;
	}
}
