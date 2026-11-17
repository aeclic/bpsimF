using Bpsim.Parts.Gameplay;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using FloatRange = Unity.Physics.Math.FloatRange;

namespace Bpsim.Parts.Simulation
{
	[UpdateInGroup(typeof(PartSimulationSystemGroup))]
	internal partial class BalloonSystem : SystemBase
	{
		private EntityQuery m_balloons, m_bodies, m_ownedJoints, m_balancers;
		internal void InitializeParts()
		{
			var parts = PartManager.Instance.PartSimulator.Unmanaged.Parts;
			for (int i = 0; i < parts.Length; i++)
			{
				Entity part = parts[i];
				if (!EntityManager.HasComponent<Balloon>(part)) continue;
				var balloon = EntityManager.GetComponentData<Balloon>(part);
				if (!balloon.Initialized) { InitializeBody(part); balloon.Initialized = true; EntityManager.SetComponentData(part, balloon); }
			}
		}
		protected override void OnCreate()
		{
			m_balloons = GetEntityQuery(ComponentType.ReadWrite<Balloon>(), ComponentType.ReadOnly<PartTag>(), ComponentType.ReadOnly<LocalTransform>());
			m_bodies = GetEntityQuery(ComponentType.ReadWrite<BalloonBody>(), ComponentType.ReadOnly<Balloon>(), ComponentType.ReadOnly<PhysicsVelocity>());
			m_ownedJoints = GetEntityQuery(ComponentType.ReadOnly<SuspensionJoint>());
			m_balancers = GetEntityQuery(ComponentType.ReadWrite<BalloonBalancer>());
		}
		protected override void OnUpdate()
		{
			var manager = PartManager.Instance;
			if (manager == null || !manager.IsSimulating || !(World.Time.DeltaTime > 0)) return;
			Dependency.Complete();
			using (var parts = m_balloons.ToEntityArray(Allocator.Temp))
				foreach (Entity part in parts)
				{
					if (!SuspensionRuntime.IsSimulationPart(EntityManager, part)) continue;
					var balloon = EntityManager.GetComponentData<Balloon>(part);
					if (balloon.Initialized) continue;
					InitializeBody(part);
					balloon.Initialized = true; EntityManager.SetComponentData(part, balloon);
				}
			using (var bodies = m_bodies.ToEntityArray(Allocator.Temp))
				foreach (Entity part in bodies)
				{
					if (!SuspensionRuntime.IsSimulationPart(EntityManager, part)) continue;
					if (!EntityManager.GetComponentData<Balloon>(part).Initialized) continue;
					BalloonBody body = EntityManager.GetComponentData<BalloonBody>(part);
					if (body.Popped) continue;
					if (body.PopRequest)
					{
						body.Popped = true;
						Release(part, ref body);
						EntityManager.SetComponentData(part, body);
						PartGameplayManager.Instance.Retire(part);
						continue;
					}
					if (body.Target != Entity.Null && (!EntityManager.Exists(body.Target) || !EntityManager.Exists(body.TetherJoint))) Release(part, ref body);
					var velocity = EntityManager.GetComponentData<PhysicsVelocity>(part);
					float force = SuspensionMath.LimitBalloonForce(body.Force, velocity.Linear.y, body.MaximumSpeed);
					velocity.Linear.y += force * World.Time.DeltaTime * EntityManager.GetComponentData<PhysicsMass>(part).InverseMass;
					EntityManager.SetComponentData(part, velocity);
					EntityManager.SetComponentData(part, body);
				}
			ReconcileBalancers();
			using var joints = m_ownedJoints.ToEntityArray(Allocator.Temp);
			foreach (Entity joint in joints)
			{
				var owner = EntityManager.GetComponentData<SuspensionJoint>(joint);
				if (owner.WorldOnly && !EntityManager.Exists(owner.Owner)) EntityManager.DestroyEntity(joint);
			}
		}
		private void ReconcileBalancers()
		{
			var counts = new NativeParallelHashMap<Entity, int>(math.max(1, m_bodies.CalculateEntityCount()), Allocator.Temp);
			try
			{
			using var bodies = m_bodies.ToEntityArray(Allocator.Temp);
			foreach (Entity part in bodies)
			{
				if (!SuspensionRuntime.IsSimulationPart(EntityManager, part)) continue;
				var body = EntityManager.GetComponentData<BalloonBody>(part);
				if (body.Popped || !body.RegisteredBalance || !EntityManager.Exists(body.Target)) continue;
				counts.TryGetValue(body.Target, out int count); counts[body.Target] = count + 1;
			}
			using var targets = m_balancers.ToEntityArray(Allocator.Temp);
			foreach (Entity target in targets)
			{
				var balance = EntityManager.GetComponentData<BalloonBalancer>(target);
				counts.TryGetValue(target, out balance.BalloonCount);
				if (balance.BalloonCount == 0 && EntityManager.Exists(balance.Joint))
				{
					EntityManager.DestroyEntity(balance.Joint); balance.Joint = Entity.Null;
				}
				EntityManager.SetComponentData(target, balance);
			}
			}
			finally { counts.Dispose(); }
		}
		private void InitializeBody(Entity part)
		{
			var info = EntityManager.GetComponentData<PartTypeValue>(part).Value;
			var profile = BpleSuspensionProfiles.Get(info);
			var pose = EntityManager.GetComponentData<LocalTransform>(part);
			BalloonBody body = EntityManager.GetComponentData<BalloonBody>(part); // keep an early input request
			SuspensionRuntime.SpawnSiblings(EntityManager, part, profile.count);
			SuspensionRuntime.ApplyPhysics(EntityManager, part);
			SuspensionRuntime.SelectSingleVisualization(EntityManager, part);
			body.Force = profile.force * BpleSuspensionProfiles.Settings.balloonForce;
			body.MaximumSpeed = profile.maxSpeed; body.PartIndex = profile.index;
			body.CollisionUnpoppable = BpleSuspensionProfiles.Settings.unpoppableBalloon && profile.index == 7;
			body.Target = SuspensionRuntime.FindTarget(EntityManager, part, true);
			int ordinal = EntityManager.HasComponent<SuspensionSibling>(part)
				? EntityManager.GetComponentData<SuspensionSibling>(part).Ordinal : profile.count;
			var grid = EntityManager.GetComponentData<PartTransform>(part);
			var random = new Random(math.hash(new uint4((uint)grid.X, (uint)grid.Y, (uint)profile.index, (uint)ordinal)) | 1u);
			if (EntityManager.Exists(body.Target))
			{
				var target = EntityManager.GetComponentData<LocalTransform>(body.Target);
				bool pig = EntityManager.GetComponentData<PartTypeValue>(body.Target).BaseType == PartType.Pig;
				float3 remote = target.Position + (pig ? float3.zero : new float3(0, .5f, 0));
				body.ConnectedAnchor = SuspensionRuntime.ToLocal(EntityManager, body.Target, remote);
				body.LocalAnchor = new float3(0, -.5f * SuspensionRuntime.Scale(EntityManager, part).y, 0);
				body.MaximumDistance = random.NextFloat(.8f, 1.2f) * math.max(0, math.distance(target.Position, pose.Position) - .5f) + (pig ? .3f : 0);
				body.TetherJoint = SuspensionRuntime.CreateTether(EntityManager, part, body.Target, body.LocalAnchor, body.ConnectedAnchor, body.MaximumDistance);
				AddBalancer(body.Target); body.RegisteredBalance = true;
				SuspensionRuntime.Set(EntityManager, part, new SuspensionRope { Target = body.Target, LocalAnchor = body.LocalAnchor,
					ConnectedAnchor = body.ConnectedAnchor, Width = .025f, Visible = true });
			}
			// Deterministic in-plane scatter keeps the original bounded random spread without crossing build layers.
			pose.Position.x += random.NextFloat(-.5f, .5f);
			SuspensionRuntime.ResetPose(EntityManager, part, pose);
			EntityManager.SetComponentData(part, body);
		}
		private void AddBalancer(Entity target)
		{
			var balance = EntityManager.HasComponent<BalloonBalancer>(target) ? EntityManager.GetComponentData<BalloonBalancer>(target) : default;
			if (!EntityManager.Exists(balance.Joint))
			{
				var pose = EntityManager.GetComponentData<LocalTransform>(target);
				var mass = EntityManager.GetComponentData<PhysicsMass>(target);
				// BPLE StartContraption configures every balloon's balancer to 20 before running.
				SuspensionMath.SpringSoftness(20f, 0f, mass.InverseInertia.z, out float frequency, out float damping);
				var constraints = new FixedList512Bytes<Constraint>();
				constraints.Add(Constraint.Twist(0, new FloatRange(-math.PI / 180f, 0), frequency, damping));
				constraints.Add(Constraint.Hinge(0));
				var physics = new PhysicsJoint
				{
					BodyAFromJoint = new BodyFrame { Axis = new float3(0, 0, 1), PerpendicularAxis = new float3(0, 1, 0) },
					BodyBFromJoint = new BodyFrame { Axis = math.rotate(pose.Rotation, new float3(0, 0, 1)),
						PerpendicularAxis = math.rotate(pose.Rotation, new float3(0, 1, 0)) }
				};
				physics.SetConstraints(constraints);
				balance.Joint = EntityManager.CreateEntity();
				EntityManager.AddComponentData(balance.Joint, physics);
				EntityManager.AddComponentData(balance.Joint, new PhysicsConstrainedBodyPair(target, Entity.Null, false));
				EntityManager.AddComponentData(balance.Joint, new PartJointInfo { Type = PartJointType.Normal, State = 1 });
				EntityManager.AddComponentData(balance.Joint, new SuspensionJoint { Owner = target, WorldOnly = true });
				EntityManager.AddSharedComponent(balance.Joint, new PhysicsWorldIndex(0u));
			}
			balance.BalloonCount++; SuspensionRuntime.Set(EntityManager, target, balance);
		}
		private void Release(Entity part, ref BalloonBody body)
		{
			SuspensionRuntime.RemoveTether(body.TetherJoint);
			if (body.RegisteredBalance && EntityManager.Exists(body.Target) && EntityManager.HasComponent<BalloonBalancer>(body.Target))
			{
				var balance = EntityManager.GetComponentData<BalloonBalancer>(body.Target);
				balance.BalloonCount = math.max(0, balance.BalloonCount - 1);
				if (balance.BalloonCount == 0 && EntityManager.Exists(balance.Joint))
				{
					EntityManager.DestroyEntity(balance.Joint); balance.Joint = Entity.Null;
				}
				EntityManager.SetComponentData(body.Target, balance);
			}
			body.RegisteredBalance = false; body.Target = Entity.Null; body.TetherJoint = Entity.Null;
			if (EntityManager.HasComponent<SuspensionRope>(part))
			{
				var rope = EntityManager.GetComponentData<SuspensionRope>(part); rope.Visible = false;
				EntityManager.SetComponentData(part, rope);
			}
		}
		internal void ClearRuntimeResources()
		{
			EntityManager.CompleteAllTrackedJobs();
			using var joints = m_ownedJoints.ToEntityArray(Allocator.Temp);
			foreach (Entity joint in joints)
				if (EntityManager.GetComponentData<SuspensionJoint>(joint).WorldOnly) EntityManager.DestroyEntity(joint);
		}
		protected override void OnDestroy() => ClearRuntimeResources();
	}
}
