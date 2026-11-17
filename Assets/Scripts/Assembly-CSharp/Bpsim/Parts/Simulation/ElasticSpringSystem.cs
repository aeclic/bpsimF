using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using FloatRange = Unity.Physics.Math.FloatRange;

namespace Bpsim.Parts.Simulation
{
	/// <summary>APK-style passive elastic joint, endpoint ownership and presentation.</summary>
	[UpdateInGroup(typeof(PartSimulationSystemGroup))]
	[UpdateBefore(typeof(EggSystem))]
	internal partial class ElasticSpringSystem : SystemBase
	{
		private EntityQuery m_springs, m_endpoints, m_ownedJoints;
		private NativeList<BlobAssetReference<Collider>> m_colliders;
		internal int OwnedColliderCount => m_colliders.Length;
		internal void InitializeParts()
		{
			var manager = PartManager.Instance;
			foreach (Entity part in manager.PartSimulator.Unmanaged.Parts)
			{
				if (!EntityManager.HasComponent<ElasticSpring>(part) || !EntityManager.HasComponent<PhysicsMass>(part)) continue;
				var state = EntityManager.GetComponentData<ElasticSpring>(part);
				if (!state.Initialized) { ConnectInitial(manager, part, ref state); UpdateVisualization(part, ref state); EntityManager.SetComponentData(part, state); }
			}
		}
		protected override void OnCreate()
		{
			m_springs = GetEntityQuery(ComponentType.ReadWrite<ElasticSpring>(), ComponentType.ReadOnly<PartTag>(),
				ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<PhysicsMass>());
			m_endpoints = GetEntityQuery(ComponentType.ReadOnly<SpringEndpoint>());
			m_ownedJoints = GetEntityQuery(ComponentType.ReadOnly<SpringOwnedJoint>());
			m_colliders = new NativeList<BlobAssetReference<Collider>>(Allocator.Persistent);
		}
		protected override void OnUpdate()
		{
			PartManager manager = PartManager.Instance;
			if (manager == null || !manager.IsSimulating || manager.PartSimulator == null)
			{
				if (m_colliders.Length > 0 || !m_endpoints.IsEmptyIgnoreFilter) ClearRuntimeResources();
				return;
			}
			if (!(World.Time.DeltaTime > 0f)) return;
			Dependency.Complete();
			using var springs = m_springs.ToEntityArray(Allocator.Temp);
			foreach (Entity part in springs)
			{
				if (!SuspensionRuntime.IsSimulationPart(EntityManager, part)) continue;
				ElasticSpring state = EntityManager.GetComponentData<ElasticSpring>(part);
				if (!state.Initialized) ConnectInitial(manager, part, ref state);
				if (!EntityManager.Exists(state.Joint) || !EntityManager.Exists(state.ConnectedBody))
				{
					RemoveEndpoint(ref state);
					CreateEndpoint(part, ref state);
					state.Joint = CreateSpringJoint(part, state.ConnectedBody, state);
					state.AppliedBreakImpulse = -1f;
				}
				RefreshBreakThreshold(ref state);
				UpdateVisualization(part, ref state);
				EntityManager.SetComponentData(part, state);
			}
			// A generated/retired owner is not necessarily removed through PartSimulator.Dispose.
			using var endpoints = m_endpoints.ToEntityArray(Allocator.Temp);
			foreach (Entity endpoint in endpoints)
			{
				Entity owner = EntityManager.GetComponentData<SpringEndpoint>(endpoint).Owner;
				if (!EntityManager.Exists(owner)) EntityManager.DestroyEntity(endpoint);
			}
			using var joints = m_ownedJoints.ToEntityArray(Allocator.Temp);
			foreach (Entity joint in joints)
				if (!EntityManager.Exists(EntityManager.GetComponentData<SpringOwnedJoint>(joint).Owner))
					EntityManager.DestroyEntity(joint);
		}
		private void ConnectInitial(PartManager manager, Entity part, ref ElasticSpring state)
		{
			state.Initialized = true;
			LocalTransform pose = EntityManager.GetComponentData<LocalTransform>(part);
			state.HalfLength = .5f * math.abs(pose.Scale);
			if (EntityManager.HasComponent<PartFreeTransform>(part))
				state.HalfLength *= math.abs(EntityManager.GetComponentData<PartFreeTransform>(part).Scale.y);
			if (EntityManager.HasComponent<LinkedEntityGroup>(part))
			{
				var children = EntityManager.GetBuffer<LinkedEntityGroup>(part);
				foreach (var child in children)
					if (EntityManager.HasComponent<SpringVisual>(child.Value)) state.Visualization = child.Value;
			}
			// Only the lower elastic port changes. The upper fixed weld is still the support.
			float3 down = math.rotate(pose.Rotation, new float3(0, -1, 0));
			foreach (Entity joint in manager.PartSimulator.Unmanaged.Joints)
			{
				if (!EntityManager.Exists(joint) || !EntityManager.HasComponent<PhysicsJoint>(joint)) continue;
				var pair = EntityManager.GetComponentData<PhysicsConstrainedBodyPair>(joint);
				Entity other = pair.EntityA == part ? pair.EntityB : pair.EntityB == part ? pair.EntityA : Entity.Null;
				if (other == Entity.Null || !EntityManager.HasComponent<LocalTransform>(other) ||
					!EntityManager.HasComponent<PhysicsMass>(other)) continue;
				LocalTransform otherPose = EntityManager.GetComponentData<LocalTransform>(other);
				float3 delta = otherPose.Position - pose.Position;
				if (math.dot(delta, down) <= .01f || math.dot(math.normalizesafe(delta), down) < .8f) continue;
				state.ConnectedBody = other;
				state.ConnectedAnchor = math.rotate(math.inverse(otherPose.Rotation),
					pose.Position + down * state.HalfLength - otherPose.Position);
				PhysicsJoint elastic = BuildJoint(part, other, state);
				// Reorient the constrained pair with the body frames; either source may have built the weld.
				EntityManager.SetComponentData(joint, new PhysicsConstrainedBodyPair(part, other, false));
				EntityManager.SetComponentData(joint, elastic);
				if (!EntityManager.HasComponent<SpringOwnedJoint>(joint))
					EntityManager.AddComponentData(joint, new SpringOwnedJoint { Owner = part });
				state.Joint = joint;
				return;
			}
			CreateEndpoint(part, ref state);
			state.Joint = CreateSpringJoint(part, state.ConnectedBody, state);
		}
		private void CreateEndpoint(Entity part, ref ElasticSpring state)
		{
			LocalTransform pose = EntityManager.GetComponentData<LocalTransform>(part);
			float3 position = pose.Position + math.rotate(pose.Rotation, new float3(0, -state.HalfLength, 0));
			var collider = PartPhysics.CreateSphere(float3.zero, .1f);
			m_colliders.Add(collider);
			Entity endpoint = EntityManager.CreateEntity();
			EntityManager.SetName(endpoint, "SpringEndpoint");
			LocalTransform transform = LocalTransform.FromPositionRotation(position, pose.Rotation);
			EntityManager.AddComponentData(endpoint, transform);
			EntityManager.AddComponentData(endpoint, (WorldTransform)transform);
			EntityManager.AddComponentData(endpoint, new LocalToWorld { Value = float4x4.TRS(position, pose.Rotation, new float3(1)) });
			EntityManager.AddComponentData(endpoint, new PhysicsCollider { Value = collider });
			EntityManager.AddComponentData(endpoint, PhysicsMass.CreateDynamic(collider.Value.MassProperties, .1f));
			EntityManager.AddComponentData(endpoint, EntityManager.HasComponent<PhysicsVelocity>(part)
				? EntityManager.GetComponentData<PhysicsVelocity>(part) : PhysicsVelocity.Zero);
			EntityManager.AddComponentData(endpoint, new PhysicsDamping { Linear = .2f });
			EntityManager.AddComponentData(endpoint, new PhysicsGravityFactor { Value = 1f });
			EntityManager.AddComponentData(endpoint, Bpsim.Physics.Constraint2D.CreateFromTransform(transform));
			EntityManager.AddSharedComponent(endpoint, new PhysicsWorldIndex(0u));
			EntityManager.AddComponentData(endpoint, new SpringEndpoint { Owner = part });
			state.ConnectedBody = endpoint;
			state.ConnectedAnchor = float3.zero;
			state.OwnsEndpoint = true;
		}
		private Entity CreateSpringJoint(Entity part, Entity other, ElasticSpring state)
		{
			Entity joint = EntityManager.CreateEntity();
			EntityManager.AddComponentData(joint, BuildJoint(part, other, state));
			EntityManager.AddComponentData(joint, new PhysicsConstrainedBodyPair(part, other, false));
			EntityManager.AddComponentData(joint, new SpringOwnedJoint { Owner = part });
			// State=1 opts out of PartSimulationSystem's automatic part-graph registration.
			EntityManager.AddComponentData(joint, new PartJointInfo { Type = PartJointType.Normal, State = 1 });
			EntityManager.AddSharedComponent(joint, new PhysicsWorldIndex(0u));
			// Endpoints are not parts: never put these pairs in simulator.Joints / the part union-find.
			return joint;
		}
		internal PhysicsJoint BuildJoint(Entity part, Entity other, ElasticSpring state)
		{
			float inverseMass = EntityManager.GetComponentData<PhysicsMass>(part).InverseMass
				+ EntityManager.GetComponentData<PhysicsMass>(other).InverseMass;
			float reducedMass = inverseMass > 1E-6f ? 1f / inverseMass : 1f;
			float frequency = math.sqrt(250f / reducedMass) / (2f * math.PI);
			float damping = 20f / (2f * math.sqrt(250f * reducedMass));
			float3 anchor = new float3(0, -state.HalfLength, 0);
			PhysicsJoint result;
			if (state.Stable)
			{
				result = PhysicsJoint.CreateLimitedDistance(anchor, state.ConnectedAnchor, new FloatRange(0f, 0f));
				var constraints = result.GetConstraints();
				var soft = constraints[0]; soft.SpringFrequency = frequency; soft.SpringDamping = damping;
				constraints[0] = soft; result.SetConstraints(constraints);
			}
			else
			{
				quaternion selfRotation = EntityManager.GetComponentData<LocalTransform>(part).Rotation;
				quaternion otherRotation = EntityManager.GetComponentData<LocalTransform>(other).Rotation;
				float3 axis = math.rotate(selfRotation, new float3(0, 1, 0));
				float3 perpendicular = math.rotate(selfRotation, new float3(1, 0, 0));
				result = PhysicsJoint.CreatePrismatic(
					new BodyFrame { Position = anchor, Axis = new float3(0, 1, 0), PerpendicularAxis = new float3(1, 0, 0) },
					new BodyFrame { Position = state.ConnectedAnchor, Axis = math.rotate(math.inverse(otherRotation), axis),
						PerpendicularAxis = math.rotate(math.inverse(otherRotation), perpendicular) }, new FloatRange(-.1f, .1f));
				var constraints = result.GetConstraints();
				var soft = constraints[1]; soft.SpringFrequency = frequency; soft.SpringDamping = damping;
				constraints[1] = soft; result.SetConstraints(constraints);
			}
			return result;
		}
		private void RefreshBreakThreshold(ref ElasticSpring state)
		{
			if (!EntityManager.Exists(state.Joint)) return;
			PhysicsJoint joint = EntityManager.GetComponentData<PhysicsJoint>(state.Joint);
			var settings = UserSettings.Instance.SimulationSettings;
			float impulse = settings.InfiniteConnectionStrength ? float.PositiveInfinity
				: state.BreakForce * settings.ConnectionStrengthFactor * World.Time.DeltaTime;
			if (state.AppliedBreakImpulse == impulse) return;
			// Alien glue may raise an existing joint to infinity. Do not overwrite
			// another ECS effect; once it restores the finite baseline we can refresh it.
			if (state.AppliedBreakImpulse >= 0f && !float.IsPositiveInfinity(state.AppliedBreakImpulse))
			{
				foreach (var constraint in joint.GetConstraints())
					if (constraint.Type == ConstraintType.Linear && float.IsPositiveInfinity(constraint.MaxImpulse.x)) return;
			}
			joint.SetImpulseEventThresholdAllConstraints(new float3(impulse), new float3(float.PositiveInfinity));
			EntityManager.SetComponentData(state.Joint, joint);
			state.AppliedBreakImpulse = impulse;
		}
		private void UpdateVisualization(Entity part, ref ElasticSpring state)
		{
			if (!EntityManager.Exists(state.ConnectedBody)) return;
			LocalTransform pose = EntityManager.GetComponentData<LocalTransform>(part);
			LocalTransform other = EntityManager.GetComponentData<LocalTransform>(state.ConnectedBody);
			float3 first = pose.Position + math.rotate(pose.Rotation, new float3(0, state.HalfLength, 0));
			float3 second = other.Position + math.rotate(other.Rotation, state.ConnectedAnchor);
			float3 delta = second - first;
			state.Length = math.length(delta);
			if (!EntityManager.Exists(state.Visualization) || !EntityManager.HasComponent<LocalTransform>(state.Visualization)) return;
			var visual = EntityManager.GetComponentData<SpringVisual>(state.Visualization);
			LocalTransform transform = EntityManager.GetComponentData<LocalTransform>(state.Visualization);
			float scale = math.max(2f * state.HalfLength, 1E-4f);
			float3 middle = math.rotate(math.inverse(pose.Rotation), .5f * (first + second) - pose.Position);
			float3x3 originalRoot = EntityManager.HasComponent<PostTransformScale>(part)
				? EntityManager.GetComponentData<PostTransformScale>(part).Value : float3x3.identity;
			transform.Position = math.mul(math.inverse(originalRoot), middle / math.max(math.abs(pose.Scale), 1E-4f));
			transform.Rotation = math.mul(math.inverse(pose.Rotation), quaternion.RotateZ(math.atan2(delta.y, delta.x) + math.PI * .5f));
			EntityManager.SetComponentData(state.Visualization, transform);
			SuspensionRuntime.Set(EntityManager, state.Visualization, new PostTransformScale
				{
					Value = math.mul(visual.OriginalScale, float3x3.Scale(new float3(1f, state.Length / scale, 1f)))
				});
		}
		private void RemoveEndpoint(ref ElasticSpring state)
		{
			if (!state.OwnsEndpoint) return;
			if (EntityManager.Exists(state.Joint)) EntityManager.DestroyEntity(state.Joint);
			if (EntityManager.Exists(state.ConnectedBody)) EntityManager.DestroyEntity(state.ConnectedBody);
			state.OwnsEndpoint = false;
		}
		internal void ClearRuntimeResources()
		{
			EntityManager.CompleteAllTrackedJobs();
			using var joints = m_ownedJoints.ToEntityArray(Allocator.Temp);
			foreach (Entity joint in joints)
			{
				var pair = EntityManager.GetComponentData<PhysicsConstrainedBodyPair>(joint);
				// Existing part-part joints are disposed by PartSimulator; only free-end joints are ours.
				if (EntityManager.HasComponent<SpringEndpoint>(pair.EntityB)) EntityManager.DestroyEntity(joint);
			}
			EntityManager.DestroyEntity(m_endpoints);
			for (int i = 0; i < m_colliders.Length; i++) if (m_colliders[i].IsCreated) m_colliders[i].Dispose();
			m_colliders.Clear();
		}
		protected override void OnDestroy()
		{
			ClearRuntimeResources();
			m_colliders.Dispose();
		}
	}
}
