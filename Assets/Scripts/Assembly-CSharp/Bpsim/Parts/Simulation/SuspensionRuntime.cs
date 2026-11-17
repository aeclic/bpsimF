using Bpsim.Parts.Gameplay;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Extensions;
using Unity.Physics.GraphicsIntegration;
using Unity.Rendering;
using Unity.Transforms;
using FloatRange = Unity.Physics.Math.FloatRange;

namespace Bpsim.Parts.Simulation
{
	/// <summary>Shared ECS assembly operations; no per-part MonoBehaviour or proximity-force path.</summary>
	internal static class SuspensionRuntime
	{
		internal static bool IsBalloon(PartType type) => type == PartType.Balloon || type == PartType.Balloons2 || type == PartType.Balloons3;
		internal static bool IsSuspension(PartType type) => IsBalloon(type) || Sandbag.IsSandbag(type);
		internal static bool IsSimulationPart(EntityManager em, Entity part) => em.HasComponent<PartSceneID>(part)
			&& em.GetComponentData<PartSceneID>(part).Value == PartManager.SimulationSceneID;
		internal static float RuntimeMass(PartTypeInfo info) => IsBalloon(PartVariants.Resolve(info).PartType) ? .1f : BpleSuspensionProfiles.Get(info).mass;
		internal static void ConfigureMasses(NativeParallelHashMap<PartTypeInfo, PartExtensionData> map)
		{
			using var keys = map.GetKeyArray(Allocator.Temp);
			foreach (var key in keys)
			{
				if (!IsSuspension(PartVariants.Resolve(key).PartType)) continue;
				var data = map[key]; data.Mass = RuntimeMass(key); map[key] = data;
			}
		}
		internal static void RegisterPendingJoints(EntityManager em)
		{
			using var query = em.CreateEntityQuery(ComponentType.ReadWrite<PartJointInfo>(), ComponentType.ReadOnly<PhysicsConstrainedBodyPair>());
			using var joints = query.ToEntityArray(Allocator.Temp);
			foreach (Entity joint in joints)
			{
				var info = em.GetComponentData<PartJointInfo>(joint);
				if (info.State != 0) continue;
				var pair = em.GetComponentData<PhysicsConstrainedBodyPair>(joint);
				if (!IsSimulationPart(em, pair.EntityA) || !IsSimulationPart(em, pair.EntityB)) continue;
				info.State = 1; em.SetComponentData(joint, info);
				PartManager.Instance.PartSimulator.Unmanaged.Joints.Add(joint);
			}
		}
		internal static void Set<T>(EntityManager em, Entity part, T value) where T : unmanaged, IComponentData
		{
			if (em.HasComponent<T>(part)) em.SetComponentData(part, value); else em.AddComponentData(part, value);
		}
		internal static float2 Scale(EntityManager em, Entity part)
		{
			float2 scale = em.HasComponent<PartFreeTransform>(part) ? em.GetComponentData<PartFreeTransform>(part).Scale : new float2(1f);
			return math.abs(scale * em.GetComponentData<LocalTransform>(part).Scale);
		}
		internal static float3 Point(EntityManager em, Entity part, float3 anchor)
		{
			var pose = em.GetComponentData<LocalTransform>(part);
			// Physics collider/anchors already contain free-scale. Never multiply PostTransformScale twice.
			return pose.Position + math.rotate(pose.Rotation, anchor);
		}
		internal static float3 ToLocal(EntityManager em, Entity part, float3 worldPoint)
		{
			var pose = em.GetComponentData<LocalTransform>(part);
			return math.rotate(math.inverse(pose.Rotation), worldPoint - pose.Position);
		}
		internal static void ResetPose(EntityManager em, Entity part, LocalTransform pose)
		{
			Set(em, part, pose); Set(em, part, (WorldTransform)pose);
			Set(em, part, Bpsim.Physics.Constraint2D.CreateFromTransform(pose));
			if (em.HasComponent<PhysicsGraphicalInterpolationBuffer>(part))
				Set(em, part, new PhysicsGraphicalInterpolationBuffer { PreviousTransform = new RigidTransform(pose.Rotation, pose.Position),
					PreviousVelocity = em.GetComponentData<PhysicsVelocity>(part) });
		}
		internal static Entity FindTarget(EntityManager em, Entity part, bool balloon)
		{
			var manager = PartManager.Instance;
			var grid = em.GetComponentData<PartTransform>(part);
			var settings = BpleSuspensionProfiles.Settings;
			bool rotate = balloon ? settings.rotatableBalloon : settings.rotatableSandbag;
			int distance = balloon ? settings.balloonConnectionDistance : settings.sandbagConnectionDistance;
			int dx = 0, dy = 1;
			if (rotate)
			{
				switch ((grid.Rotation % 4 + 4) % 4)
				{
					case 1: dx = -1; dy = 0; break;
					case 2: dy = -1; break;
					case 3: dx = 1; dy = 0; break;
				}
			}
			if (balloon) { dx = -dx; dy = -dy; }
			byte layer = PartBuildLayerUtility.GetLayer(em, part);
			for (int i = 1; i <= distance; i++)
			{
				if (!manager.PartSimulator.Unmanaged.PartMap.TryGet(grid.X + i * dx, grid.Y + i * dy, layer, out var slot)) continue;
				Entity candidate = slot.HasPart ? slot.Part : slot.PartContainer;
				if (candidate == part || !em.Exists(candidate) || !em.HasComponent<PhysicsMass>(candidate) || !em.HasComponent<PartTypeValue>(candidate)) continue;
				PartType type = em.GetComponentData<PartTypeValue>(candidate).BaseType;
				if (type == PartType.WoodenFrame || type == PartType.MetalFrame || type == PartType.Pig || (balloon && type == PartType.Kicker)) return candidate;
			}
			return Entity.Null;
		}
		internal static void SpawnSiblings(EntityManager em, Entity source, int count)
		{
			if (count <= 1 || em.HasComponent<SuspensionSibling>(source)) return;
			var manager = PartManager.Instance;
			var info = em.GetComponentData<PartTypeValue>(source).Value;
			Entity prefab = manager.EntitySpawner.FindPrefab(info);
			var pose = em.GetComponentData<LocalTransform>(source);
			var grid = em.GetComponentData<PartTransform>(source);
			for (int ordinal = count - 1; ordinal >= 1; ordinal--)
			{
				Entity child = em.Instantiate(prefab);
				em.SetName(child, "RuntimeSuspension_" + info.PartType + "_" + ordinal);
				Set(em, child, em.GetComponentData<PartTypeValue>(source));
				Set(em, child, grid);
				Set(em, child, pose);
				Set(em, child, (WorldTransform)pose);
				Set(em, child, em.GetComponentData<PartSceneID>(source));
				Set(em, child, em.GetComponentData<PartBuildLayer>(source));
				Set(em, child, new PartContainerValue { Value = Entity.Null });
				Set(em, child, new ContainedPart { Value = Entity.Null });
				if (em.HasComponent<PartFreeTransform>(source)) Set(em, child, em.GetComponentData<PartFreeTransform>(source));
				if (em.HasComponent<PostTransformScale>(source)) Set(em, child, em.GetComponentData<PostTransformScale>(source));
				Set(em, child, new SuspensionSibling { Source = source, Ordinal = ordinal });
				if (Sandbag.IsSandbag(info.PartType))
				{
					Sandbag bag = Sandbag.Create(info); bag.Balloons = ordinal;
					Sandbag.GetHangParameters(ordinal, out bag.HangOffset, out bag.MaxDistance); Set(em, child, bag);
				}
				ApplyPhysics(em, child);
				manager.PartSimulator.Unmanaged.Parts.Add(child);
				if (PartGameplayManager.Instance?.IsRunning == true) PartGameplayManager.Instance.RegisterSpawnedPart(child);
			}
			TNTDamage.RefreshTopology();
		}
		internal static unsafe void ApplyPhysics(EntityManager em, Entity part)
		{
			var info = em.GetComponentData<PartTypeValue>(part).Value;
			bool balloon = IsBalloon(PartVariants.Resolve(info).PartType);
			float2 scale = Scale(em, part);
			var collider = PartPhysics.CreateSphere(balloon ? float3.zero : new float3(0, -.1f * scale.y, 0),
				(balloon ? .5f : .13f) * math.max(1E-4f, math.cmax(scale)));
			if (balloon) ((Collider*)collider.GetUnsafePtr())->SetCollisionResponse(CollisionResponsePolicy.CollideRaiseCollisionEvents);
			if (em.HasComponent<PhysicsCollider>(part))
			{
				var previous = em.GetComponentData<PhysicsCollider>(part).Value;
				if (previous.IsCreated) collider.Value.SetCollisionFilter(previous.Value.GetCollisionFilter());
			}
			Set(em, part, new PhysicsCollider { Value = collider });
			PartManager.Instance.PartSimulator.Unmanaged.TrackScaledCollider(collider);
			if (!em.HasComponent<PartIsolatedCollider>(part)) em.AddComponent<PartIsolatedCollider>(part);
			Set(em, part, PhysicsMass.CreateDynamic(collider.Value.MassProperties, RuntimeMass(info)));
			if (!em.HasComponent<PhysicsVelocity>(part)) em.AddComponentData(part, PhysicsVelocity.Zero);
			Set(em, part, new PhysicsDamping { Linear = balloon ? 2f : 1f, Angular = balloon ? .5f : 10f });
			Set(em, part, new PhysicsGravityFactor { Value = 1f });
			Set(em, part, Bpsim.Physics.Constraint2D.CreateFromTransform(em.GetComponentData<LocalTransform>(part)));
			if (!em.HasComponent<PhysicsWorldIndex>(part)) em.AddSharedComponent(part, new PhysicsWorldIndex(0u));
			if (!em.HasComponent<PhysicsGraphicalSmoothing>(part))
			{
				var pose = em.GetComponentData<LocalTransform>(part);
				Set(em, part, default(PhysicsGraphicalSmoothing));
				Set(em, part, new PhysicsGraphicalInterpolationBuffer { PreviousTransform = new RigidTransform(pose.Rotation, pose.Position),
					PreviousVelocity = em.GetComponentData<PhysicsVelocity>(part) });
				if (!em.HasComponent<PropagateLocalToWorld>(part)) em.AddComponent<PropagateLocalToWorld>(part);
			}
		}
		internal static Entity CreateTether(EntityManager em, Entity part, Entity target, float3 local, float3 remote, float distance)
		{
			float inverseMass = em.GetComponentData<PhysicsMass>(part).InverseMass + em.GetComponentData<PhysicsMass>(target).InverseMass;
			SuspensionMath.SpringSoftness(100f, 10f, inverseMass, out float frequency, out float damping);
			var joint = PhysicsJoint.CreateLimitedDistance(local, remote, new FloatRange(0, math.max(0, distance)));
			var constraints = joint.GetConstraints(); var soft = constraints[0];
			soft.SpringFrequency = frequency; soft.SpringDamping = damping; constraints[0] = soft; joint.SetConstraints(constraints);
			Entity entity = em.CreateEntity();
			em.AddComponentData(entity, joint);
			em.AddComponentData(entity, new PhysicsConstrainedBodyPair(part, target, false));
			em.AddComponentData(entity, new PartJointInfo { Type = PartJointType.Normal, State = 1 });
			em.AddComponentData(entity, new SuspensionJoint { Owner = part });
			em.AddSharedComponent(entity, new PhysicsWorldIndex(0u));
			PartManager.Instance.PartSimulator.Unmanaged.Joints.Add(entity);
			TNTDamage.RefreshTopology();
			return entity;
		}
		internal static void RemoveTether(Entity joint)
		{
			TNTDamage.BreakJoint(joint); TNTDamage.RefreshTopology();
		}
		internal static void AddImpulse(EntityManager em, Entity part, float3 impulse)
		{
			if (!em.Exists(part) || !em.HasComponent<PhysicsVelocity>(part) || !em.HasComponent<PhysicsMass>(part)) return;
			var velocity = em.GetComponentData<PhysicsVelocity>(part);
			velocity.Linear += impulse * em.GetComponentData<PhysicsMass>(part).InverseMass; em.SetComponentData(part, velocity);
		}
		internal static void SelectSingleVisualization(EntityManager em, Entity part)
		{
			if (!em.HasComponent<LinkedEntityGroup>(part)) return;
			var group = em.GetBuffer<LinkedEntityGroup>(part);
			using var entities = new NativeList<Entity>(group.Length, Allocator.Temp);
			for (int i = 0; i < group.Length; i++) entities.Add(group[i].Value);
			foreach (Entity child in entities)
			{
				if (!em.HasComponent<PartRenderInfo>(child)) continue;
				Entity current = child; bool found = false, visible = false;
				for (int depth = 0; depth < 16 && em.Exists(current); depth++)
				{
					if (em.HasComponent<SuspensionVisual>(current)) { found = true; visible = em.GetComponentData<SuspensionVisual>(current).Actual; break; }
					if (!em.HasComponent<Parent>(current)) break;
					current = em.GetComponentData<Parent>(current).Value;
				}
				if (!found) continue;
				if (visible && em.HasComponent<DisableRendering>(child)) em.RemoveComponent<DisableRendering>(child);
				if (!visible && !em.HasComponent<DisableRendering>(child)) em.AddComponent<DisableRendering>(child);
			}
		}
	}
}
