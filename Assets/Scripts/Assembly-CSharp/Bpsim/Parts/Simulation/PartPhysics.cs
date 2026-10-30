using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.GraphicsIntegration;
using Unity.Transforms;

namespace Bpsim.Parts.Simulation
{
	public static class PartPhysics
	{
		public static BlobAssetReference<Collider> CreateBox(float3 center, float3 size)
		{
			return BoxCollider.Create(new BoxGeometry
			{
				Center = center,
				Orientation = quaternion.identity,
				Size = size,
				BevelRadius = math.min(ConvexHullGenerationParameters.Default.BevelRadius, 0.5f * math.cmin(size))
			});
		}

		public static BlobAssetReference<Collider> CreateSphere(float3 center, float radius)
		{
			return SphereCollider.Create(new SphereGeometry
			{
				Center = center,
				Radius = radius
			});
		}

		public static BlobAssetReference<Collider> CreateCapsule(float3 point0, float3 point1, float radius)
		{
			return CapsuleCollider.Create(new CapsuleGeometry
			{
				Vertex0 = point0,
				Vertex1 = point1,
				Radius = radius
			});
		}

		/// <summary>
		/// 按自由缩放（<see cref="Bpsim.Parts.PartFreeTransform.Scale"/>）重新烘焙一份碰撞体。
		/// <para>
		/// Unity.Physics 只在实体**没有** <see cref="Unity.Transforms.PostTransformScale"/> 时才把
		/// <see cref="Unity.Transforms.LocalTransform"/>.Scale 传给 RigidBody（见
		/// PhysicsWorldBuilder.CreateRigidBodies），而本工程的部件根实体始终带 PostTransformScale
		/// （自由缩放走渲染矩阵），所以模拟期 RigidBody.Scale 恒为 1 —— 碰撞体永远是 prefab 原尺寸。
		/// 缩小时相邻部件间距变小而碰撞体不变，求解器把互相穿插的部件弹开，固定关节冲量超阈值就断，
		/// 表现就是"缩放后连接坏掉"。这里把缩放直接烘进几何，绕开该限制。
		/// </para>
		/// <para>
		/// 复合碰撞体（一个 prefab 常带几十上百个 collider）递归处理：子件的相对位移按同一个
		/// 缩放矩阵拉伸，子几何也各自缩放。返回 <c>default</c> 表示该形状暂不支持缩放，
		/// 调用方应保留原碰撞体。
		/// </para>
		/// </summary>
		public static unsafe BlobAssetReference<Collider> CreateScaled(BlobAssetReference<Collider> source, float2 scale)
		{
			if (!source.IsCreated)
			{
				return default(BlobAssetReference<Collider>);
			}
			return CreateScaled((Collider*)source.GetUnsafePtr(), scale);
		}

		private static unsafe BlobAssetReference<Collider> CreateScaled(Collider* source, float2 scale)
		{
			if (source == null)
			{
				return default(BlobAssetReference<Collider>);
			}
			switch (source->Type)
			{
			case ColliderType.Box:
			{
				BoxCollider* box = (BoxCollider*)source;
				BoxGeometry geometry = box->Geometry;
				geometry.Center = ScaleXY(geometry.Center, scale);
				geometry.Size = new float3(geometry.Size.x * math.abs(scale.x), geometry.Size.y * math.abs(scale.y), geometry.Size.z);
				geometry.BevelRadius = math.min(ConvexHullGenerationParameters.Default.BevelRadius, 0.5f * math.cmin(geometry.Size));
				return BoxCollider.Create(geometry, box->GetCollisionFilter(), box->Material);
			}
			case ColliderType.Sphere:
			{
				SphereCollider* sphere = (SphereCollider*)source;
				SphereGeometry geometry = sphere->Geometry;
				geometry.Center = ScaleXY(geometry.Center, scale);
				geometry.Radius = geometry.Radius * MinScale(scale);
				return SphereCollider.Create(geometry, sphere->GetCollisionFilter(), sphere->Material);
			}
			case ColliderType.Capsule:
			{
				CapsuleCollider* capsule = (CapsuleCollider*)source;
				CapsuleGeometry geometry = capsule->Geometry;
				geometry.Vertex0 = ScaleXY(geometry.Vertex0, scale);
				geometry.Vertex1 = ScaleXY(geometry.Vertex1, scale);
				geometry.Radius = geometry.Radius * MinScale(scale);
				return CapsuleCollider.Create(geometry, capsule->GetCollisionFilter(), capsule->Material);
			}
			case ColliderType.Compound:
			{
				CompoundCollider* compound = (CompoundCollider*)source;
				int numChildren = compound->NumChildren;
				if (numChildren <= 0)
				{
					return default(BlobAssetReference<Collider>);
				}
				NativeArray<CompoundCollider.ColliderBlobInstance> children =
					new NativeArray<CompoundCollider.ColliderBlobInstance>(numChildren, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
				int created = 0;
				for (int i = 0; i < numChildren; i++)
				{
					ref CompoundCollider.Child child = ref compound->Children[i];
					BlobAssetReference<Collider> scaledChild = CreateScaled(child.Collider, scale);
					if (!scaledChild.IsCreated)
					{
						// 有子件缩放不了就整体放弃，整体保留原碰撞体，绝不产出"缩了一半"的形状。
						for (int j = 0; j < created; j++)
						{
							children[j].Collider.Dispose();
						}
						children.Dispose();
						return default(BlobAssetReference<Collider>);
					}
					RigidTransform fromChild = child.CompoundFromChild;
					fromChild.pos = ScaleXY(fromChild.pos, scale);
					children[i] = new CompoundCollider.ColliderBlobInstance
					{
						CompoundFromChild = fromChild,
						Collider = scaledChild,
						Entity = child.Entity
					};
					created = i + 1;
				}
				// CompoundCollider.Create 会把子碰撞体拷进自己的 blob，所以临时子 blob 必须释放。
				BlobAssetReference<Collider> result = CompoundCollider.Create(children);
				for (int i = 0; i < numChildren; i++)
				{
					children[i].Collider.Dispose();
				}
				children.Dispose();
				return result;
			}
			default:
				return default(BlobAssetReference<Collider>);
			}
		}

		private static float3 ScaleXY(float3 value, float2 scale)
		{
			return new float3(value.x * scale.x, value.y * scale.y, value.z);
		}

		private static float MinScale(float2 scale)
		{
			return math.min(math.abs(scale.x), math.abs(scale.y));
		}

		public static PhysicsJoint CreateFixedJoint(BodyFrame bodyFrameA, BodyFrame bodyFrameB)
		{
			return PhysicsJoint.CreateFixed(bodyFrameA, bodyFrameB);
		}

		public static void AddCollider(EntityManager entityManager, Entity entity, BlobAssetReference<Collider> collider)
		{
			entityManager.AddComponentData(entity, new PhysicsCollider
			{
				Value = collider
			});
		}

		public static void AddCollider(EntityCommandBuffer commandBuffer, Entity entity, BlobAssetReference<Collider> collider)
		{
			commandBuffer.AddComponent(entity, new PhysicsCollider
			{
				Value = collider
			});
		}

		/// <summary>
		/// Opts a simulated part into Unity.Physics' graphical interpolation.
		/// <para>
		/// The physics world runs on a fixed timestep while the game renders at a variable
		/// framerate. At low time scales (the HUD time slider) several rendered frames fall
		/// between two physics ticks, so <see cref="Unity.Transforms.LocalToWorld"/> - which is
		/// what <see cref="Bpsim.Parts.SceneRenderSystem"/> uploads every frame - would only
		/// change on the tick frames. That is what made slow motion look like it was stuttering.
		/// </para>
		/// <para>
		/// <see cref="PhysicsGraphicalSmoothing"/> lets
		/// <see cref="Unity.Physics.GraphicsIntegration.SmoothRigidBodiesGraphicalMotion"/>
		/// own the entity's LocalToWorld matrix, and
		/// <see cref="PhysicsGraphicalInterpolationBuffer"/> makes it interpolate between the two
		/// most recent ticks instead of extrapolating. <see cref="Unity.Transforms.PropagateLocalToWorld"/>
		/// makes the part's child (renderer) entities inherit that interpolated matrix - without it
		/// children are derived from the non-interpolated <see cref="Unity.Transforms.WorldTransform"/>
		/// and keep stuttering.
		/// </para>
		/// </summary>
		public static void EnableGraphicalInterpolation(EntityManager entityManager, Entity entity, RigidTransform transform)
		{
			entityManager.AddComponentData(entity, default(PhysicsGraphicalSmoothing));
			entityManager.AddComponentData(entity, new PhysicsGraphicalInterpolationBuffer
			{
				PreviousTransform = transform,
				PreviousVelocity = PhysicsVelocity.Zero
			});
			entityManager.AddComponentData(entity, default(PropagateLocalToWorld));
		}

		/// <inheritdoc cref="EnableGraphicalInterpolation(EntityManager, Entity, RigidTransform)"/>
		public static void EnableGraphicalInterpolation(EntityCommandBuffer commandBuffer, Entity entity, RigidTransform transform)
		{
			commandBuffer.AddComponent(entity, default(PhysicsGraphicalSmoothing));
			commandBuffer.AddComponent(entity, new PhysicsGraphicalInterpolationBuffer
			{
				PreviousTransform = transform,
				PreviousVelocity = PhysicsVelocity.Zero
			});
			commandBuffer.AddComponent(entity, default(PropagateLocalToWorld));
		}

		public static void AddRigidbody(EntityManager entityManager, Entity entity, bool kinematic, MassProperties massProperties, float mass, float linearDamping, float angularDamping)
		{
			if (!kinematic)
			{
				entityManager.AddComponentData(entity, PhysicsMass.CreateDynamic(massProperties, mass));
			}
			else
			{
				entityManager.AddComponentData(entity, PhysicsMass.CreateKinematic(massProperties));
			}
			entityManager.AddComponentData(entity, PhysicsVelocity.Zero);
			entityManager.AddComponentData(entity, new PhysicsDamping
			{
				Linear = linearDamping,
				Angular = angularDamping
			});
		}

		public static void AddRigidbody(EntityCommandBuffer commandBuffer, Entity entity, bool kinematic, MassProperties massProperties, float mass, float linearDamping, float angularDamping)
		{
			if (!kinematic)
			{
				commandBuffer.AddComponent(entity, PhysicsMass.CreateDynamic(massProperties, mass));
			}
			else
			{
				commandBuffer.AddComponent(entity, PhysicsMass.CreateKinematic(massProperties));
			}
			commandBuffer.AddComponent(entity, PhysicsVelocity.Zero);
			commandBuffer.AddComponent(entity, new PhysicsDamping
			{
				Linear = linearDamping,
				Angular = angularDamping
			});
		}
	}
}
