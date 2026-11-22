using System.Collections.Generic;
using Bpsim.Collections;
using Bpsim.Physics;
using Bpsim.Physics.Anvil;
using Havok.Physics;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;
using FloatRange = Unity.Physics.Math.FloatRange;

namespace Bpsim.Parts.Simulation
{
	internal struct HingePlateDamping : IComponentData { }

	internal struct HingePlateCollisionNetwork : IComponentData
	{
		public int Group;
		public byte HalfLayer;
		public bool IsHalf => HalfLayer != 0;
	}

	internal static class HingePlateRuntime
	{
		public static void Initialize(EntityManager manager, ref PartSimulatorUnmanaged simulator, SimulationSettings settings, float timeStep)
		{
			var plates = new List<Entity>();
			foreach (Entity part in simulator.Parts)
				if (manager.HasComponent<HingePlate>(part)) plates.Add(part);

			simulator.EnsurePlateOwners(2 * plates.Count, settings.CanHingePlateConnectComponents);
			foreach (Entity plate in plates) InitializeBody(manager, ref simulator, plate, settings, timeStep);
			Connect(manager, ref simulator, plates, settings, timeStep);
		}

		internal static void InitializeBody(EntityManager manager, ref PartSimulatorUnmanaged simulator, Entity root, SimulationSettings settings, float timeStep)
		{
			simulator.EnsurePlateOwners(2, settings.CanHingePlateConnectComponents);
			HingePlate plate = manager.GetComponentData<HingePlate>(root);
			int layout = HingePlate.NormalizeLayout(manager.GetComponentData<PartTransform>(root).Rotation);
			byte layer = PartBuildLayerUtility.GetLayer(manager, root);
			LocalTransform rootPose = manager.GetComponentData<LocalTransform>(root);
			float2 scale = PartFreeTransform.Sanitize(manager.GetComponentData<PartFreeTransform>(root)).Scale;
			CreateHalf(manager, ref simulator, root, plate.Left, rootPose, scale, layout, layer);
			CreateHalf(manager, ref simulator, root, plate.Right, rootPose, scale, layout, layer);
			manager.AddSharedComponent(root, new PhysicsWorldIndex(0u));
			manager.AddComponentData(root, PhysicsMass.CreateKinematic(MassProperties.UnitSphere));
			manager.AddComponentData(root, PhysicsVelocity.Zero);
			float anchorOffset = HingePlate.HalfOffset * scale.x;
			float impulse = BreakImpulse(manager, root, settings, timeStep);
			CreateJoint(manager, ref simulator, plate.Left, plate.Right, anchorOffset, 0, impulse, true);
			CreateJoint(manager, ref simulator, plate.Right, plate.Left, -anchorOffset, 0, impulse, true);
		}

		internal static void ConnectGenerated(EntityManager manager, ref PartSimulatorUnmanaged simulator, List<Entity> generated, SimulationSettings settings, float timeStep)
		{
			var plates = new List<Entity>(generated.Count);
			foreach (Entity part in generated)
				if (manager.HasComponent<HingePlate>(part)) plates.Add(part);
			if (plates.Count != 0) Connect(manager, ref simulator, plates, settings, timeStep, new HashSet<Entity>(generated));
		}

		private static void Connect(EntityManager manager, ref PartSimulatorUnmanaged simulator, List<Entity> plates, SimulationSettings settings, float timeStep,
			HashSet<Entity> generation = null)
		{
			using var indices = new NativeParallelHashMap<Entity, int>(plates.Count, Allocator.Temp);
			using var networks = new NativeDisjointSet(plates.Count, Allocator.Temp);
			for (int index = 0; index < plates.Count; index++) indices.Add(plates[index], index);

			for (int index = 0; settings.CreateJoints && index < plates.Count; index++)
			{
				Entity root = plates[index];
				HingePlate plate = manager.GetComponentData<HingePlate>(root);
				PartTransform grid = manager.GetComponentData<PartTransform>(root);
				PartTypeInfo sourceType = manager.GetComponentData<PartTypeValue>(root).BaseValue;
				float anchorOffset = HingePlate.HalfOffset * SuspensionRuntime.Scale(manager, root).x;
				float impulse = BreakImpulse(manager, root, settings, timeStep);

				int layer = PartBuildLayerUtility.GetLayer(manager, root);
				foreach (int2 offset in HingePlate.Neighbours)
				{
					if (!simulator.PartMap.TryGet(grid.X + offset.x, grid.Y + offset.y, layer, out PartGridInfo slot)) continue;
					Entity target = slot.HasPart && manager.HasComponent<HingePlate>(slot.Part) ? slot.Part
						: (slot.HasPartContainer ? slot.PartContainer : slot.Part);
					if (target == Entity.Null || (generation != null && !generation.Contains(target)) || !manager.HasComponent<PhysicsMass>(target)) continue;
					PartTypeInfo targetType = manager.GetComponentData<PartTypeValue>(target).BaseValue;
					int targetLayout = manager.GetComponentData<PartTransform>(target).Rotation;
					bool targetIsPlate = indices.TryGetValue(target, out int targetIndex);
					for (int side = 0; side < 2; side++)
					{
						bool right = side != 0;
						if (!HingePlate.TryConnect(sourceType, grid.Rotation, targetType, targetLayout, offset, right, out bool targetRight, out int angle)) continue;
						Entity targetBody = targetIsPlate ? manager.GetComponentData<HingePlate>(target).Half(targetRight) : target;
						CreateJoint(manager, ref simulator, plate.Half(right), targetBody, right ? anchorOffset : -anchorOffset,
							angle, impulse, settings.StableHingePlate);
						if (targetIsPlate) networks.Union(index, targetIndex);
					}
				}
			}

			using var groups = networks.GetComponentIndexes(Allocator.Temp, out int groupCount);
			int firstGroup = simulator.AllocatePlateCollisionGroups(groupCount);
			for (int index = 0; index < plates.Count; index++)
			{
				Entity root = plates[index];
				HingePlate plate = manager.GetComponentData<HingePlate>(root);
				int group = firstGroup + groups[index];
				var network = new HingePlateCollisionNetwork { Group = group, HalfLayer = PartBuildLayerUtility.GetLayer(manager, root) };
				manager.SetComponentData(plate.Left, network);
				manager.SetComponentData(plate.Right, network);
				Entity frame = manager.GetComponentData<PartContainerValue>(root).Value;
				if (frame != Entity.Null) manager.AddComponentData(frame, new HingePlateCollisionNetwork { Group = group });
			}
		}

		private static float BreakImpulse(EntityManager manager, Entity root, SimulationSettings settings, float timeStep)
		{
			float strength = manager.GetComponentData<PartExtensionComponent>(root).Value.ConnectionStrength;
			return settings.InfiniteConnectionStrength ? float.PositiveInfinity : 3f * strength * settings.ConnectionStrengthFactor * timeStep;
		}

		private static void CreateHalf(EntityManager manager, ref PartSimulatorUnmanaged simulator, Entity owner,
			Entity half, LocalTransform root, float2 visualScale, int layout, byte layer)
		{
			LocalTransform pose = manager.GetComponentData<LocalTransform>(half);
			float2 scale = visualScale * (root.Scale * pose.Scale);
			pose.Position.y = HingePlate.Height(layout);
			pose.Position = root.Position + math.rotate(root.Rotation, pose.Position * new float3(visualScale, 1f) * root.Scale);
			pose.Rotation = math.mul(root.Rotation, pose.Rotation);
			pose.Scale *= root.Scale;
			manager.RemoveComponent<Parent>(half);
			manager.SetComponentData(half, pose);
			SuspensionRuntime.Set(manager, half, (WorldTransform)pose);
			SuspensionRuntime.Set(manager, half, new PostTransformScale { Value = float3x3.Scale(visualScale.x, visualScale.y, 1f) });
			var collider = manager.GetComponentData<PhysicsCollider>(half);
			if (math.any(math.abs(scale - 1f) > 1e-4f))
			{
				collider.Value = PartPhysics.CreateScaled(collider.Value, scale);
				manager.SetComponentData(half, collider);
				simulator.TrackScaledCollider(collider.Value);
			}
			PhysicsMass mass = PhysicsMass.CreateDynamic(collider.Value.Value.MassProperties, HingePlate.HalfMass);
			mass.InverseInertia = new float3(0f, 0f, mass.InverseInertia.z);
			manager.AddComponentData(half, mass);
			manager.AddComponentData(half, PhysicsVelocity.Zero);
			manager.AddComponentData(half, new PhysicsDamping { Linear = 0.2f, Angular = 0.05f });
			manager.AddComponentData(half, new HingePlateCollisionNetwork { HalfLayer = layer });
			manager.AddComponentData(half, Constraint2D.CreateFromTransform(pose));
			manager.AddSharedComponent(half, new PhysicsWorldIndex(0u));
			PartPhysics.EnableGraphicalInterpolation(manager, half, new RigidTransform(pose.Rotation, pose.Position));
			simulator.RegisterPlateHalf(half, owner);
		}

		private static void CreateJoint(EntityManager manager, ref PartSimulatorUnmanaged simulator, Entity source, Entity target,
			float anchorOffset, int angle, float impulse, bool fixedJoint)
		{
			LocalTransform sourcePose = manager.GetComponentData<LocalTransform>(source);
			LocalTransform targetPose = manager.GetComponentData<LocalTransform>(target);
			var anchor = new float3(anchorOffset, 0f, 0f);
			RigidTransform targetFromSource = math.mul(math.inverse(new RigidTransform(targetPose.Rotation, targetPose.Position)),
				new RigidTransform(sourcePose.Rotation, sourcePose.Position));
			var frameA = new BodyFrame { Axis = new float3(0f, 0f, 1f), PerpendicularAxis = new float3(1f, 0f, 0f), Position = anchor };
			var frameB = new BodyFrame
			{
				Axis = math.rotate(targetFromSource.rot, frameA.Axis),
				PerpendicularAxis = math.rotate(targetFromSource.rot, frameA.PerpendicularAxis),
				Position = math.transform(targetFromSource, anchor)
			};
			PhysicsJoint joint = fixedJoint ? PhysicsJoint.CreateFixed(frameA, frameB)
				: PhysicsJoint.CreateLimitedHinge(frameA, frameB, new FloatRange(math.radians(angle - 15f), math.radians(angle + 15f)));
			joint.SetImpulseEventThresholdAllConstraints(impulse, float.PositiveInfinity);
			Entity entity = manager.CreateEntity();
			manager.AddComponentData(entity, joint);
			manager.AddSharedComponent(entity, new PhysicsWorldIndex(0u));
			manager.AddComponentData(entity, new PhysicsConstrainedBodyPair(source, target, false));
			manager.AddComponentData(entity, new PartJointInfo { Type = PartJointType.Normal, State = 1 });
			if (!fixedJoint) manager.AddComponent<HingePlateDamping>(entity);
			simulator.AddJoint(source, target, entity);
		}
	}

	[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
	[UpdateBefore(typeof(PhysicsSystemGroup))]
	internal partial class HingePlateDampingSystem : SystemBase
	{
		[BurstCompile]
		[WithAll(typeof(HingePlateDamping))]
		private partial struct DampJoints : IJobEntity
		{
			[ReadOnly] public ComponentLookup<PhysicsMass> Masses;
			public ComponentLookup<PhysicsVelocity> Velocities;

			private void Execute(in PhysicsConstrainedBodyPair pair)
			{
				if (!Masses.TryGetComponent(pair.EntityA, out var firstMass) || !Masses.TryGetComponent(pair.EntityB, out var secondMass)
					|| firstMass.InverseMass <= 0f || secondMass.InverseMass <= 0f) return;
				PhysicsVelocity first = Velocities[pair.EntityA];
				PhysicsVelocity second = Velocities[pair.EntityB];
				float3 delta = new float3(second.Linear.xy - first.Linear.xy, 0f);
				float factor = 0.2f / (firstMass.InverseMass + secondMass.InverseMass);
				float magnitude = factor * math.length(delta);
				float3 impulse = delta * (magnitude < 10f ? factor : 10f / magnitude);
				first.Linear += impulse * firstMass.InverseMass;
				second.Linear -= impulse * secondMass.InverseMass;
				Velocities[pair.EntityA] = first;
				Velocities[pair.EntityB] = second;
			}
		}

		protected override void OnCreate() => RequireForUpdate<HingePlateDamping>();

		protected override void OnUpdate()
		{
			Dependency = new DampJoints { Masses = GetComponentLookup<PhysicsMass>(true), Velocities = GetComponentLookup<PhysicsVelocity>() }.Schedule(Dependency);
		}
	}

	[UpdateInGroup(typeof(PhysicsSimulationGroup))]
	[UpdateAfter(typeof(PhysicsCreateBodyPairsGroup))]
	[UpdateBefore(typeof(PhysicsCreateContactsGroup))]
	internal partial class HingePlateCollisionSystem : SystemBase
	{
		private AnvilStepSystem m_AnvilStep;
		private ComponentLookup<HingePlateCollisionNetwork> m_Networks;
		private ComponentLookup<PartBuildLayer> m_Layers;

		private static AnvilCollisionTag ReadTag(Entity body, in ComponentLookup<HingePlateCollisionNetwork> networks,
			in ComponentLookup<PartBuildLayer> layers)
		{
			if (body == Entity.Null) return default;
			networks.TryGetComponent(body, out var network);
			byte layer = network.IsHalf ? network.HalfLayer
				: layers.TryGetComponent(body, out PartBuildLayer component) ? PartBuildLayer.Clamp(component.Value) : (byte)0;
			return new AnvilCollisionTag(network.Group, layer, network.IsHalf);
		}

		private AnvilCollisionTag GetAnvilTag(Entity body) => ReadTag(body, m_Networks, m_Layers);

		[BurstCompile]
		private struct FilterPairs : IBodyPairsJob
		{
			[ReadOnly] public ComponentLookup<HingePlateCollisionNetwork> Networks;
			[ReadOnly] public ComponentLookup<PartBuildLayer> Layers;

			public void Execute(ref ModifiableBodyPair pair)
			{
				if (!ReadTag(pair.EntityA, Networks, Layers).Allows(ReadTag(pair.EntityB, Networks, Layers))) pair.Disable();
			}
		}

		protected override void OnCreate() => RequireForUpdate<HingePlateCollisionNetwork>();

		protected override void OnStartRunning()
		{
			m_AnvilStep = World.GetExistingSystemManaged<AnvilStepSystem>();
			if (m_AnvilStep == null) return;
			m_AnvilStep.GetCollisionTag = GetAnvilTag;
			m_AnvilStep.EnableWarmStarting = false;
		}

		protected override void OnStopRunning()
		{
			if (m_AnvilStep == null) return;
			m_AnvilStep.GetCollisionTag = null;
			m_AnvilStep.EnableWarmStarting = true;
			m_AnvilStep = null;
		}

		protected override void OnUpdate()
		{
			m_Networks = GetComponentLookup<HingePlateCollisionNetwork>(true);
			m_Layers = GetComponentLookup<PartBuildLayer>(true);
			if (SystemAPI.TryGetSingleton<PhysicsStep>(out var step) && step.SimulationType == SimulationType.AnvilPhysics)
			{
				EntityManager.CompleteDependencyBeforeRO<HingePlateCollisionNetwork>();
				EntityManager.CompleteDependencyBeforeRO<PartBuildLayer>();
				return;
			}

			var physics = SystemAPI.GetSingletonRW<PhysicsWorldSingleton>();
			Dependency = new FilterPairs
			{
				Networks = m_Networks,
				Layers = m_Layers
			}.Schedule(
				SystemAPI.GetSingleton<SimulationSingleton>(), ref physics.ValueRW.PhysicsWorld, Dependency);
		}
	}
}
