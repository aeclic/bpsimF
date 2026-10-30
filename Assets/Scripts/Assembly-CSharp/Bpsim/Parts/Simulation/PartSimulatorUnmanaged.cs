using Bpsim.Physics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace Bpsim.Parts.Simulation
{
	public struct PartSimulatorUnmanaged
	{
		private NativeList<Entity> m_parts;

		private NativeList<Entity> m_joints;

		private PartGridMap<PartGridInfo> m_partMap;

		private NativeList<ConnectedComponentInfo> m_connectedComponents;

		/// <summary>
		/// 模拟期按自由缩放重新烘焙出来的碰撞体 blob。它们不在实体之外任何地方登记，
		/// 所以必须由模拟自己持有并在 <see cref="Dispose"/> 时释放，否则每次进入模拟都漏一批。
		/// </summary>
		private NativeList<BlobAssetReference<Collider>> m_scaledColliders;

		public NativeList<Entity> Parts => m_parts;

		public NativeList<Entity> Joints => m_joints;

		public PartGridMap<PartGridInfo> PartMap => m_partMap;

		public NativeList<ConnectedComponentInfo> ConnectedComponents => m_connectedComponents;

		public void Initialize(Reference<PartSceneUnmanaged> partScene)
		{
			int num = math.max(partScene.Value.Parts.Length, 256);
			m_parts = new NativeList<Entity>(num, Allocator.Persistent);
			m_joints = new NativeList<Entity>(num, Allocator.Persistent);
			m_partMap = new PartGridMap<PartGridInfo>(num, Allocator.Persistent);
			m_connectedComponents = new NativeList<ConnectedComponentInfo>(64, Allocator.Persistent);
			m_scaledColliders = new NativeList<BlobAssetReference<Collider>>(num, Allocator.Persistent);
		}

		/// <summary>
		/// 接管一个模拟期重建的碰撞体 blob 的所有权（见 <see cref="PartPhysics.CreateScaled"/>）。
		/// </summary>
		public void TrackScaledCollider(BlobAssetReference<Collider> collider)
		{
			if (collider.IsCreated)
			{
				m_scaledColliders.Add(in collider);
			}
		}

		public Entity FindFirstPart(ComponentLookup<PartTypeValue> partTypeLookup, PartType partType, int partIndex = -1)
		{
			for (int i = 0; i < m_parts.Length; i++)
			{
				Entity entity = m_parts[i];
				PartTypeValue partTypeValue = partTypeLookup[entity];
				if (partTypeValue.Type == partType && (partIndex == -1 || partTypeValue.Index == partIndex))
				{
					return entity;
				}
			}
			return Entity.Null;
		}

		public Entity FindLastPart(ComponentLookup<PartTypeValue> partTypeLookup, PartType partType, int partIndex = -1)
		{
			for (int num = m_parts.Length - 1; num >= 0; num--)
			{
				Entity entity = m_parts[num];
				PartTypeValue partTypeValue = partTypeLookup[entity];
				if (partTypeValue.Type == partType && (partIndex == -1 || partTypeValue.Index == partIndex))
				{
					return entity;
				}
			}
			return Entity.Null;
		}

		public static void AddPhysicsComponents(EntityCommandBuffer commandBuffer, ComponentLookup<LocalTransform> localTransformLookup, ComponentLookup<PhysicsCollider> physicsColliderLookup, Entity entity, bool kinematic, float mass, float linearDamping, float angularDamping)
		{
			commandBuffer.AddSharedComponent(entity, new PhysicsWorldIndex(0u));
			MassProperties massProperties = physicsColliderLookup[entity].Value.Value.MassProperties;
			PartPhysics.AddRigidbody(commandBuffer, entity, kinematic, massProperties, mass, linearDamping, angularDamping);
			LocalTransform transform = localTransformLookup[entity];
			commandBuffer.AddComponent(entity, Constraint2D.CreateFromTransform(transform));
			// 低倍速下每个物理步之间会隔好几个渲染帧，靠 Unity.Physics 的图形插值把 LocalToWorld
			// 补成连续运动（等价于 Besiege 里刚体的 RigidbodyInterpolation.Interpolate）。
			PartPhysics.EnableGraphicalInterpolation(commandBuffer, entity, new RigidTransform(transform.Rotation, transform.Position));
		}

		public static void AddFixedJoint(EntityCommandBuffer commandBuffer, ComponentLookup<WorldTransform> worldTransformLookup, Entity entityA, Entity entityB, float maxImpulse, bool enableCollision)
		{
			WorldTransform worldTransform = worldTransformLookup[entityA];
			WorldTransform worldTransform2 = worldTransformLookup[entityB];
			RigidTransform t = new RigidTransform(worldTransform.Rotation, worldTransform.Position);
			RigidTransform t2 = new RigidTransform(worldTransform2.Rotation, worldTransform2.Position);
			RigidTransform b = new RigidTransform
			{
				rot = quaternion.identity,
				pos = 0.5f * (worldTransform.Position + worldTransform2.Position)
			};
			RigidTransform transform = math.mul(math.inverse(t), b);
			PhysicsJoint component = PartPhysics.CreateFixedJoint(bodyFrameB: new BodyFrame(math.mul(math.inverse(t2), b)), bodyFrameA: new BodyFrame(transform));
			component.SetImpulseEventThresholdAllConstraints(maxImpulse, float.PositiveInfinity);
			Entity e = commandBuffer.CreateEntity();
			commandBuffer.SetName(e, (FixedString64Bytes)"JointEntity");
			commandBuffer.AddSharedComponent(e, new PhysicsWorldIndex(0u));
			commandBuffer.AddComponent(e, component);
			commandBuffer.AddComponent(e, new PhysicsConstrainedBodyPair(entityA, entityB, enableCollision));
			commandBuffer.AddComponent(e, new PartJointInfo
			{
				Type = PartJointType.Normal,
				State = 0
			});
		}

		public void AddJoint(Entity entityA, Entity entityB, Entity jointEntity)
		{
			m_joints.Add(in jointEntity);
		}

		public void Dispose(EntityManager entityManager)
		{
			foreach (Entity part in m_parts)
			{
				entityManager.DestroyEntity(part);
			}
			foreach (Entity joint in m_joints)
			{
				entityManager.DestroyEntity(joint);
			}
			m_parts.Dispose();
			m_joints.Dispose();
			m_partMap.Dispose();
			m_connectedComponents.Dispose();
			for (int i = 0; i < m_scaledColliders.Length; i++)
			{
				BlobAssetReference<Collider> collider = m_scaledColliders[i];
				if (collider.IsCreated)
				{
					collider.Dispose();
				}
			}
			m_scaledColliders.Dispose();
		}
	}
}
