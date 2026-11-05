// Anvil Physics (box3d) — managed world wrapper.
//
// 这一层负责把 Unity.Physics 的 PhysicsWorld 与 box3d 的 native world 做双向同步：
//   ECS/PhysicsWorld  --(每帧写入 transform/velocity/mass)-->  box3d
//   box3d             --(step 后批量回读)------------------>  PhysicsWorld.MotionDatas/MotionVelocities
// 回写后由 Unity.Physics 自带的 ExportPhysicsWorld 把结果搬到 LocalTransform/PhysicsVelocity，
// 因此下游（查询、事件、渲染同步）不需要任何改动。
//
// 索引约定：m_MotionBodies[i] 对应 PhysicsWorld.MotionDatas[i]（即 DynamicBodies[i]），
// m_StaticBodies[i] 对应 PhysicsWorld.StaticBodies[i]。每帧用 Entity + Collider 指针校验，
// 不匹配则重建该 body（部件增删会导致索引漂移）。
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;

namespace Bpsim.Physics.Anvil
{
	public unsafe class AnvilWorld : IDisposable
	{
		const int k_MinCapacity = 256;

		b3WorldId m_WorldId;
		bool m_Created;
		int m_MotionCount;
		int m_StaticCount;

		NativeArray<b3BodyId> m_MotionBodies;
		NativeArray<Entity> m_MotionEntities;
		NativeArray<IntPtr> m_MotionColliders;
		NativeArray<float> m_MotionScales;

		NativeArray<b3BodyId> m_StaticBodies;
		NativeArray<Entity> m_StaticEntities;
		NativeArray<IntPtr> m_StaticColliders;
		NativeArray<float> m_StaticScales;

		NativeArray<b3WorldTransform> m_ScratchTransforms;
		NativeArray<b3Vec3> m_ScratchLinear;
		NativeArray<b3Vec3> m_ScratchAngular;
		NativeArray<b3AnvilContact> m_ScratchContacts;
		NativeArray<b3Vec3> m_ScratchPoints;

		// 上次从 box3d 读回的位姿/速度，用来判断 ECS 侧是否被外部改写（瞬移/改速度）。
		NativeArray<int> m_AwakeIndices;
		NativeArray<b3WorldTransform> m_LastTransforms;
		NativeArray<b3Vec3> m_LastLinear;
		NativeArray<b3Vec3> m_LastAngular;

		// 需要上传的 body 索引（压缩后），以及对应的 b3BodyId 暂存。
		readonly List<int> m_UploadIndices = new List<int>();
		NativeArray<b3BodyId> m_UploadBodies;

		// 射线全命中暂存（anvil_CastRayAll）。
		NativeArray<b3AnvilRayHit> m_ScratchRayHits;
		int m_RayHitCapacity = 256;

		// 关节：索引对齐 PhysicsWorld.Joints（BodyPair 语义见 PhysicsWorldBuilder）
		NativeArray<b3JointId> m_JointIds;
		NativeArray<int> m_JointIndexA;
		NativeArray<int> m_JointIndexB;
		NativeArray<Entity> m_JointEntities;
		NativeArray<byte> m_JointVersions;
		NativeArray<float> m_JointForceThreshold;
		NativeParallelHashMap<int, Entity> m_JointIdToEntity;

		Allocator m_Allocator = Allocator.Persistent;

		public int WorkerCount = 1;
		public int SubStepCount = 1;
		public float ContactHertz = 60f;
		public float ContactDampingRatio = 10f;
		public bool EnableSleep = true;
		public bool EnableContinuous = true;

		public bool IsCreated => m_Created;
		public b3WorldId WorldId => m_WorldId;

		public int MotionCapacity => m_MotionBodies.IsCreated ? m_MotionBodies.Length : 0;
		public int StaticCapacity => m_StaticBodies.IsCreated ? m_StaticBodies.Length : 0;

		// ── 生命周期 ────────────────────────────────────────────────────────────

		public void Create()
		{
			if (m_Created) return;

			b3WorldDef def = AnvilNative.b3DefaultWorldDef();
			def.gravity = new b3Vec3(0f, -9.81f, 0f);
			def.workerCount = (uint)math.max(1, WorkerCount);
			def.enableSleep = (byte)(EnableSleep ? 1 : 0);
			def.enableContinuous = (byte)(EnableContinuous ? 1 : 0);
			def.contactHertz = ContactHertz;
			def.contactDampingRatio = ContactDampingRatio;

			m_WorldId = AnvilNative.b3CreateWorld(ref def);
			if (m_WorldId.IsNull)
				throw new InvalidOperationException("Anvil: b3CreateWorld failed (box3d.dll missing or ABI mismatch).");

			m_Created = true;
		}

		public void Destroy()
		{
			if (!m_Created) return;
			AnvilNative.b3DestroyWorld(m_WorldId);
			m_WorldId = default;
			m_Created = false;
		}

		public void Dispose()
		{
			Destroy();
			ReleaseMeshes();

			DisposeArray(ref m_MotionBodies);
			DisposeArray(ref m_MotionEntities);
			DisposeArray(ref m_MotionColliders);
			DisposeArray(ref m_MotionScales);
			DisposeArray(ref m_StaticBodies);
			DisposeArray(ref m_StaticEntities);
			DisposeArray(ref m_StaticColliders);
			DisposeArray(ref m_StaticScales);
			DisposeArray(ref m_ScratchTransforms);
			DisposeArray(ref m_ScratchLinear);
			DisposeArray(ref m_ScratchAngular);
			DisposeArray(ref m_ScratchContacts);
			DisposeArray(ref m_ScratchPoints);
			DisposeArray(ref m_LastTransforms);
			DisposeArray(ref m_LastLinear);
			DisposeArray(ref m_LastAngular);
			DisposeArray(ref m_UploadBodies);
			DisposeArray(ref m_AwakeIndices);
			DisposeArray(ref m_ScratchRayHits);
			m_UploadIndices.Clear();
			DisposeJoints();
		}

		static void DisposeArray<T>(ref NativeArray<T> array) where T : struct
		{
			if (array.IsCreated) array.Dispose();
			array = default;
		}

		public void SetGravity(float3 gravity)
		{
			if (!m_Created) return;
			AnvilNative.b3World_SetGravity(m_WorldId, new b3Vec3(gravity.x, gravity.y, gravity.z));
		}

		public void SetWorkerCount(int workers)
		{
			WorkerCount = math.max(1, workers);
			if (m_Created) AnvilNative.b3World_SetWorkerCount(m_WorldId, WorkerCount);
		}

		// ── 容量 ────────────────────────────────────────────────────────────────

		void EnsureMotionCapacity(int needed)
		{
			if (m_MotionBodies.IsCreated && m_MotionBodies.Length >= needed) return;
			int capacity = math.max(k_MinCapacity, math.ceilpow2(needed));
			Grow(ref m_MotionBodies, capacity);
			Grow(ref m_MotionEntities, capacity);
			Grow(ref m_MotionColliders, capacity);
			Grow(ref m_MotionScales, capacity);
			Grow(ref m_ScratchTransforms, capacity);
			Grow(ref m_ScratchLinear, capacity);
			Grow(ref m_ScratchAngular, capacity);
			Grow(ref m_LastTransforms, capacity);
			Grow(ref m_LastLinear, capacity);
			Grow(ref m_LastAngular, capacity);
			Grow(ref m_AwakeIndices, capacity);
		}

		void EnsureUploadCapacity(int needed)
		{
			if (m_UploadBodies.IsCreated && m_UploadBodies.Length >= needed) return;
			Grow(ref m_UploadBodies, math.max(k_MinCapacity, math.ceilpow2(needed)));
		}

		void EnsureStaticCapacity(int needed)
		{
			if (m_StaticBodies.IsCreated && m_StaticBodies.Length >= needed) return;
			int capacity = math.max(k_MinCapacity, math.ceilpow2(needed));
			Grow(ref m_StaticBodies, capacity);
			Grow(ref m_StaticEntities, capacity);
			Grow(ref m_StaticColliders, capacity);
			Grow(ref m_StaticScales, capacity);
		}

		void EnsureContactCapacity(int needed)
		{
			if (m_ScratchContacts.IsCreated && m_ScratchContacts.Length >= needed) return;
			int capacity = math.max(k_MinCapacity, math.ceilpow2(needed));
			Grow(ref m_ScratchContacts, capacity);
		}

		void EnsurePointCapacity(int needed)
		{
			if (m_ScratchPoints.IsCreated && m_ScratchPoints.Length >= needed) return;
			int capacity = math.max(64, math.ceilpow2(needed));
			Grow(ref m_ScratchPoints, capacity);
		}

		void Grow<T>(ref NativeArray<T> array, int capacity) where T : struct
		{
			var next = new NativeArray<T>(capacity, m_Allocator);
			if (array.IsCreated)
			{
				int copy = math.min(array.Length, capacity);
				NativeArray<T>.Copy(array, next, copy);
				array.Dispose();
			}
			array = next;
		}

		// ── 同步：PhysicsWorld → box3d ──────────────────────────────────────────

		/// <summary>
		/// 把当前 PhysicsWorld 的刚体集合与运动状态送进 box3d。返回被复用的动态体数量。
		/// 必须在 box3d step 之前调用（即 BuildPhysicsWorld 之后）。
		/// </summary>
		public int SyncToNative(ref PhysicsWorld world)
		{
			if (!m_Created) Create();

			int numDynamic = world.NumDynamicBodies;
			int numStatic = world.NumStaticBodies;
			EnsureMotionCapacity(math.max(1, numDynamic));
			EnsureStaticCapacity(math.max(1, numStatic));

			// 裸指针访问：索引器的 job safety 检查在 2k 刚体上累计到毫秒级。
			MotionData* motionDatas = (MotionData*)world.MotionDatas.GetUnsafePtr();
			MotionVelocity* motionVelocities = (MotionVelocity*)world.MotionVelocities.GetUnsafePtr();
			RigidBody* dynamicBodies = (RigidBody*)world.DynamicBodies.GetUnsafePtr();
			RigidBody* staticBodies = (RigidBody*)world.StaticBodies.GetUnsafePtr();

			// 动态体。
			//
			// 关键性能/正确性约定：动态体的位姿与速度**以 box3d 为权威**。
			// 每帧无条件 b3Body_SetTransform 会把 broadphase 变脏、把刚体强行唤醒，
			// 既抹掉 sleeping（性能）又让 step 结果被上一帧的旧位姿覆盖（正确性）。
			// 因此这里只做「反向检测」：只有当 ECS 侧的值与 box3d 上次报给我们的值不一致
			// （= 外部逻辑瞬移/改速度/新生成）时，才把它写回 box3d。
			bool trackLast = m_LastTransforms.IsCreated && m_LastTransforms.Length >= numDynamic;
			b3WorldTransform* lastTransforms = trackLast ? (b3WorldTransform*)m_LastTransforms.GetUnsafePtr() : null;
			b3Vec3* lastLinearArr = trackLast ? (b3Vec3*)m_LastLinear.GetUnsafePtr() : null;
			b3Vec3* lastAngularArr = trackLast ? (b3Vec3*)m_LastAngular.GetUnsafePtr() : null;

			b3BodyId* motionBodies = (b3BodyId*)m_MotionBodies.GetUnsafePtr();
			Entity* motionEntities = (Entity*)m_MotionEntities.GetUnsafePtr();
			IntPtr* motionColliders = (IntPtr*)m_MotionColliders.GetUnsafePtr();
			float* motionScales = (float*)m_MotionScales.GetUnsafePtr();

			int writable = 0;
			for (int i = 0; i < numDynamic; i++)
			{
				RigidBody body = dynamicBodies[i];
				IntPtr colliderPtr = body.Collider.IsCreated ? (IntPtr)body.Collider.GetUnsafePtr() : IntPtr.Zero;

				if (!motionBodies[i].IsNull && motionEntities[i] == body.Entity && motionColliders[i] == colliderPtr)
				{
					if (!trackLast || NeedsUploadFast(lastTransforms, lastLinearArr, lastAngularArr, i, body.WorldFromBody, motionVelocities[i]))
					{
						m_UploadIndices.Add(writable);
						m_ScratchTransforms[writable] = ToWorldTransform(body.WorldFromBody);
						m_ScratchLinear[writable] = ToVec3(motionVelocities[i].LinearVelocity);
						m_ScratchAngular[writable] = ToVec3(motionVelocities[i].AngularVelocity);
						writable++;
					}
					continue;
				}

				// 新刚体或索引漂移：重建
				if (!motionBodies[i].IsNull)
				{
					AnvilNative.b3DestroyBody(motionBodies[i]);
					motionBodies[i] = default;
				}

				b3BodyId id = CreateDynamicBody(body, motionDatas[i], motionVelocities[i]);
				motionBodies[i] = id;
				motionEntities[i] = body.Entity;
				motionColliders[i] = colliderPtr;
				motionScales[i] = body.Scale;

				m_UploadIndices.Add(writable);
				m_ScratchTransforms[writable] = ToWorldTransform(body.WorldFromBody);
				m_ScratchLinear[writable] = ToVec3(motionVelocities[i].LinearVelocity);
				m_ScratchAngular[writable] = ToVec3(motionVelocities[i].AngularVelocity);
				writable++;
			}

			// 末尾多余的旧 body（数量下降）
			for (int i = numDynamic; i < m_MotionBodies.Length; i++)
			{
				if (motionBodies[i].IsNull) break;
				AnvilNative.b3DestroyBody(motionBodies[i]);
				motionBodies[i] = default;
				motionEntities[i] = Entity.Null;
			}

			// 静态体：只在新建/漂移时重建，位置每帧不写（静态不会被积分）
			b3BodyId* staticBodyIds = (b3BodyId*)m_StaticBodies.GetUnsafePtr();
			Entity* staticEntities = (Entity*)m_StaticEntities.GetUnsafePtr();
			IntPtr* staticColliders = (IntPtr*)m_StaticColliders.GetUnsafePtr();
			float* staticScales = (float*)m_StaticScales.GetUnsafePtr();

			for (int i = 0; i < numStatic; i++)
			{
				RigidBody body = staticBodies[i];
				IntPtr colliderPtr = body.Collider.IsCreated ? (IntPtr)body.Collider.GetUnsafePtr() : IntPtr.Zero;
				if (!staticBodyIds[i].IsNull && staticEntities[i] == body.Entity && staticColliders[i] == colliderPtr)
					continue;

				if (!staticBodyIds[i].IsNull)
				{
					AnvilNative.b3DestroyBody(staticBodyIds[i]);
					staticBodyIds[i] = default;
				}

				b3BodyId id = CreateStaticBody(body);
				staticBodyIds[i] = id;
				staticEntities[i] = body.Entity;
				staticColliders[i] = colliderPtr;
				staticScales[i] = body.Scale;
			}

			for (int i = numStatic; i < m_StaticBodies.Length; i++)
			{
				if (staticBodyIds[i].IsNull) break;
				AnvilNative.b3DestroyBody(staticBodyIds[i]);
				staticBodyIds[i] = default;
				staticEntities[i] = Entity.Null;
			}

			if (writable > 0)
			{
				// 只上传「确实变了」的那些体（m_UploadIndices），避免整批写导致全体唤醒。
				EnsureUploadCapacity(writable);
				b3BodyId* uploadBodies = (b3BodyId*)m_UploadBodies.GetUnsafePtr();
				for (int k = 0; k < writable; k++)
				{
					uploadBodies[k] = motionBodies[m_UploadIndices[k]];
				}
				b3WorldTransform* transforms = (b3WorldTransform*)m_ScratchTransforms.GetUnsafePtr();
				b3Vec3* linear = (b3Vec3*)m_ScratchLinear.GetUnsafePtr();
				b3Vec3* angular = (b3Vec3*)m_ScratchAngular.GetUnsafePtr();
				AnvilNative.anvil_WriteBatchFast(m_WorldId, uploadBodies, writable, transforms, linear, angular);
			}
			m_UploadIndices.Clear();

			m_MotionCount = numDynamic;
			m_StaticCount = numStatic;
			return numDynamic;
		}

		/// <summary>
		/// ECS 侧的值是否已经偏离 box3d 上次报回的值（= 外部逻辑改写了位姿/速度/或刚生成）。
		/// 容差取位置 1e-4、速度 1e-3 —— 足以容忍浮点回传误差，又不会漏掉真实瞬移。
		/// </summary>
		/// <summary>
		/// 与 <see cref="NeedsUpload(int, in RigidTransform, in MotionVelocity)"/> 等价，但由调用方
		/// 传入已缓存的裸指针 —— 逐体调用 GetUnsafePtr 在 10k 刚体上是毫秒级开销。
		/// </summary>
		static bool NeedsUploadFast(b3WorldTransform* lastTransforms, b3Vec3* lastLinear, b3Vec3* lastAngular,
			int index, in RigidTransform worldFromBody, in MotionVelocity velocity)
		{
			b3WorldTransform last = lastTransforms[index];
			float3 lastPos = new float3(last.p.x, last.p.y, last.p.z);
			if (math.distancesq(lastPos, worldFromBody.pos) > 1e-8f) return true;

			quaternion lastRot = new quaternion(last.q.x, last.q.y, last.q.z, last.q.w);
			if (math.abs(math.dot(lastRot.value, worldFromBody.rot.value)) < 1f - 1e-6f) return true;

			b3Vec3 ll = lastLinear[index];
			if (math.distancesq(new float3(ll.x, ll.y, ll.z), velocity.LinearVelocity) > 1e-6f) return true;

			b3Vec3 la = lastAngular[index];
			if (math.distancesq(new float3(la.x, la.y, la.z), velocity.AngularVelocity) > 1e-6f) return true;

			return false;
		}

		static bool ReuseOk(NativeArray<Entity> entities, NativeArray<IntPtr> colliders, int index, Entity entity, IntPtr collider)
		{
			return entities[index] == entity && colliders[index] == collider;
		}

		b3BodyId CreateDynamicBody(in RigidBody body, in MotionData motionData, in MotionVelocity motionVelocity)
		{
			b3BodyDef def = AnvilNative.b3DefaultBodyDef();
			def.type = (int)(motionVelocity.IsKinematic ? b3BodyType.b3_kinematicBody : b3BodyType.b3_dynamicBody);
			def.position = ToVec3(body.WorldFromBody.pos);
			def.rotation = ToQuat(body.WorldFromBody.rot);
			def.linearVelocity = ToVec3(motionVelocity.LinearVelocity);
			def.angularVelocity = ToVec3(motionVelocity.AngularVelocity);
			def.linearDamping = motionData.LinearDamping;
			def.angularDamping = motionData.AngularDamping;
			def.enableSleep = (byte)(EnableSleep ? 1 : 0);

			b3BodyId id = AnvilNative.b3CreateBody(m_WorldId, ref def);
			if (id.IsNull) return id;

			ApplyMass(id, motionData, motionVelocity);
			AddShapes(id, body, false);
			return id;
		}

		b3BodyId CreateStaticBody(in RigidBody body)
		{
			b3BodyDef def = AnvilNative.b3DefaultBodyDef();
			def.type = (int)b3BodyType.b3_staticBody;
			def.position = ToVec3(body.WorldFromBody.pos);
			def.rotation = ToQuat(body.WorldFromBody.rot);
			def.enableSleep = 0;

			b3BodyId id = AnvilNative.b3CreateBody(m_WorldId, ref def);
			if (id.IsNull) return id;

			AddShapes(id, body, true);
			return id;
		}

		void ApplyMass(b3BodyId id, in MotionData motionData, in MotionVelocity motionVelocity)
		{
			if (motionVelocity.InverseMass == 0f)
			{
				// 静态/运动学：box3d 侧由 body type 决定，无需质量数据
				return;
			}

			float3x3 principal = float3x3.identity;
			float3 inverseInertia = new float3(
				motionVelocity.InverseInertia.x,
				motionVelocity.InverseInertia.y,
				motionVelocity.InverseInertia.z);
			float3 inertiaPrincipal = new float3(
				inverseInertia.x > 0f ? 1f / inverseInertia.x : 0f,
				inverseInertia.y > 0f ? 1f / inverseInertia.y : 0f,
				inverseInertia.z > 0f ? 1f / inverseInertia.z : 0f);

			// Unity.Physics 的主轴空间 → body 空间
			float3x3 rotation = math.mul(new float3x3(motionData.BodyFromMotion.rot), principal);
			float3x3 inertiaMatrix = math.mul(math.mul(rotation, new float3x3(
				inertiaPrincipal.x, 0f, 0f,
				0f, inertiaPrincipal.y, 0f,
				0f, 0f, inertiaPrincipal.z)), math.transpose(rotation));

			var massData = new b3MassData
			{
				mass = 1f / motionVelocity.InverseMass,
				center = ToVec3(motionData.BodyFromMotion.pos),
				inertia = new b3Matrix3
				{
					cx = new b3Vec3(inertiaMatrix.c0.x, inertiaMatrix.c0.y, inertiaMatrix.c0.z),
					cy = new b3Vec3(inertiaMatrix.c1.x, inertiaMatrix.c1.y, inertiaMatrix.c1.z),
					cz = new b3Vec3(inertiaMatrix.c2.x, inertiaMatrix.c2.y, inertiaMatrix.c2.z),
				},
			};
			AnvilNative.b3Body_SetMassData(id, massData);
		}

		// ── 形状 ────────────────────────────────────────────────────────────────

		void AddShapes(b3BodyId bodyId, in RigidBody body, bool isStatic)
		{
			if (!body.Collider.IsCreated) return;
			Collider* collider = (Collider*)body.Collider.GetUnsafePtr();
			if (collider == null) return;
			float scale = body.Scale > 0f ? body.Scale : 1f;
			AddShapeRecursive(bodyId, collider, scale, RigidTransform.identity, isStatic);
		}

		void AddShapeRecursive(b3BodyId bodyId, Collider* collider, float scale, RigidTransform offset, bool isStatic)
		{
			if (collider == null) return;

			switch (collider->Type)
			{
				case ColliderType.Sphere:
				{
					SphereGeometry geometry = ((SphereCollider*)collider)->Geometry;
					var sphere = new b3Sphere
					{
						center = ToVec3(TransformPoint(offset, geometry.Center * scale)),
						radius = math.max(1e-4f, geometry.Radius * scale),
					};
					b3ShapeDef def = BuildShapeDef(collider);
					AnvilNative.b3CreateSphereShape(bodyId, ref def, ref sphere);
					break;
				}
				case ColliderType.Capsule:
				{
					CapsuleGeometry geometry = ((CapsuleCollider*)collider)->Geometry;
					var capsule = new b3Capsule
					{
						center1 = ToVec3(TransformPoint(offset, geometry.Vertex0 * scale)),
						center2 = ToVec3(TransformPoint(offset, geometry.Vertex1 * scale)),
						radius = math.max(1e-4f, geometry.Radius * scale),
					};
					b3ShapeDef def = BuildShapeDef(collider);
					AnvilNative.b3CreateCapsuleShape(bodyId, ref def, ref capsule);
					break;
				}
				case ColliderType.Box:
				{
					BoxGeometry geometry = ((BoxCollider*)collider)->Geometry;
					float3 size = geometry.Size * scale;
					RigidTransform boxTransform = math.mul(offset, new RigidTransform(geometry.Orientation, geometry.Center * scale));
					CreateBoxHull(bodyId, collider, boxTransform, size);
					break;
				}
				case ColliderType.Convex:
				{
					ConvexCollider* convex = (ConvexCollider*)collider;
					// ConvexHull 是 public 类型，靠 AssemblyInfo 的 InternalsVisibleTo 访问
					var hull = convex->ConvexHull;
					int count = hull.Vertices.Length;
					if (count < 4) return;
					EnsurePointCapacity(count);
					for (int i = 0; i < count; i++)
					{
						float3 v = hull.Vertices[i];
						m_ScratchPoints[i] = ToVec3(TransformPoint(offset, v * scale));
					}
					CreateHullShape(bodyId, collider, m_ScratchPoints, count);
					break;
				}
				case ColliderType.Mesh:
				{
					MeshCollider* meshCollider = (MeshCollider*)collider;
					CreateMeshShape(bodyId, collider, meshCollider, offset, scale, isStatic);
					break;
				}
				case ColliderType.Compound:
				{
					CompoundCollider* compound = (CompoundCollider*)collider;
					int children = compound->NumChildren;
					var accessor = compound->Children;
					for (int i = 0; i < children; i++)
					{
						CompoundCollider.Child child = accessor[i];
						RigidTransform childOffset = math.mul(offset, child.CompoundFromChild);
						AddShapeRecursive(bodyId, child.Collider, scale, childOffset, isStatic);
					}
					break;
				}
				default:
					// Cylinder / Terrain / Triangle / Quad 不参与本工程的部件碰撞
					break;
			}
		}

		b3ShapeDef BuildShapeDef(Collider* collider)
		{
			b3ShapeDef def = AnvilNative.b3DefaultShapeDef();
			CollisionFilter filter = collider->GetCollisionFilter();
			def.filter.categoryBits = filter.BelongsTo;
			def.filter.maskBits = filter.CollidesWith;
			def.filter.groupIndex = filter.GroupIndex;

			// GetMaterial 是 public，靠 AssemblyInfo 的 InternalsVisibleTo 访问
			Material material = collider->GetMaterial(ColliderKey.Empty);
			def.baseMaterial.friction = material.Friction;
			def.baseMaterial.restitution = material.Restitution;

			CollisionResponsePolicy response = collider->GetCollisionResponse();
			bool isTrigger = response == CollisionResponsePolicy.RaiseTriggerEvents;

			// 只有明确要求发碰撞事件的碰撞体才开事件，避免全局额外开销
			def.isSensor = (byte)(isTrigger ? 1 : 0);
			def.enableSensorEvents = (byte)(isTrigger ? 1 : 0);
			def.enableContactEvents = (byte)(response == CollisionResponsePolicy.CollideRaiseCollisionEvents ? 1 : 0);
			def.enableHitEvents = 0;
			def.updateBodyMass = 0; // 质量由 PhysicsMass 显式写入
			return def;
		}

		void CreateBoxHull(b3BodyId bodyId, Collider* collider, RigidTransform transform, float3 size)
		{
			float3 half = math.max(size * 0.5f, new float3(1e-4f));
			// bpsim 的盒带 BevelRadius（圆角盒），box3d 的 hull 是尖盒；
			// 这里按“外接尺寸”建模，保留尺寸与位置，圆角差异在文档里注明。
			EnsurePointCapacity(8);
			float3 corners = half;
			for (int i = 0; i < 8; i++)
			{
				float3 local = new float3(
					(i & 1) != 0 ? corners.x : -corners.x,
					(i & 2) != 0 ? corners.y : -corners.y,
					(i & 4) != 0 ? corners.z : -corners.z);
				m_ScratchPoints[i] = ToVec3(TransformPoint(transform, local));
			}
			CreateHullShape(bodyId, collider, m_ScratchPoints, 8);
		}

		void CreateHullShape(b3BodyId bodyId, Collider* collider, NativeArray<b3Vec3> points, int count)
		{
			b3Vec3* ptr = (b3Vec3*)points.GetUnsafePtr();
			// 第三个参数是 maxVertexCount；传 0 会被 b3ClampInt 夹到最小值 4，
			// 8 个盒角点就会被简化成四面体（体积/内接半径全错，刚体会穿地）。
			IntPtr hull = AnvilNative.b3CreateHull((IntPtr)ptr, count, count);
			if (hull == IntPtr.Zero) return;
			try
			{
				b3ShapeDef def = BuildShapeDef(collider);
				AnvilNative.b3CreateHullShape(bodyId, ref def, hull);
			}
			finally
			{
				// b3CreateHullShape 内部 b3AddHullToDatabase 会克隆，原始 hull 可立即释放
				AnvilNative.b3DestroyHull(hull);
			}
		}

		void CreateMeshShape(b3BodyId bodyId, Collider* collider, MeshCollider* meshCollider, RigidTransform offset, float scale, bool isStatic)
		{
			// 地形网格只挂在静态体上（box3d 的 mesh shape 也只在静态体上稳定）
			if (!isStatic) return;

			Mesh mesh = meshCollider->Mesh;
			var sections = mesh.Sections;
			int sectionCount = sections.Length;
			if (sectionCount == 0) return;

			int vertexTotal = 0, triangleTotal = 0;
			for (int s = 0; s < sectionCount; s++)
			{
				var section = sections[s];
				vertexTotal += section.Vertices.Length;
				var flags = section.PrimitiveFlags;
				for (int p = 0; p < flags.Length; p++)
					triangleTotal += (flags[p] & Mesh.PrimitiveFlags.IsTriangle) != 0 ? 1 : 2;
			}
			if (vertexTotal == 0 || triangleTotal == 0) return;

			var vertices = new NativeArray<b3Vec3>(vertexTotal, Allocator.Temp);
			var indices = new NativeArray<int>(triangleTotal * 3, Allocator.Temp);
			try
			{
				int vertexBase = 0, indexCursor = 0;
				for (int s = 0; s < sectionCount; s++)
				{
					var section = sections[s];
					var sectionVertices = section.Vertices;
					int sectionVertexCount = sectionVertices.Length;
					for (int v = 0; v < sectionVertexCount; v++)
						vertices[vertexBase + v] = ToVec3(TransformPoint(offset, sectionVertices[v] * scale));

					var flags = section.PrimitiveFlags;
					var primitives = section.PrimitiveVertexIndices;
					int primitiveCount = math.min(flags.Length, primitives.Length);
					for (int p = 0; p < primitiveCount; p++)
					{
						Mesh.PrimitiveVertexIndices pv = primitives[p];
						int a = vertexBase + pv.A, b = vertexBase + pv.B, c = vertexBase + pv.C;
						indices[indexCursor++] = a; indices[indexCursor++] = b; indices[indexCursor++] = c;
						if ((flags[p] & Mesh.PrimitiveFlags.IsTriangle) == 0)
						{
							int d = vertexBase + pv.D;
							indices[indexCursor++] = a; indices[indexCursor++] = c; indices[indexCursor++] = d;
						}
					}
					vertexBase += sectionVertexCount;
				}

				var meshDef = new b3MeshDef
				{
					vertices = (IntPtr)vertices.GetUnsafePtr(),
					stride = IntPtr.Zero,
					indices = (IntPtr)indices.GetUnsafePtr(),
					materialIndices = IntPtr.Zero,
					weldTolerance = 0f,
					vertexCount = vertexTotal,
					triangleCount = indexCursor / 3,
					weldVertices = 0,
					useMedianSplit = 1,
					identifyEdges = 0,
					clockWiseWinding = 0,
				};

				IntPtr meshData = AnvilNative.b3CreateMesh(ref meshDef, IntPtr.Zero, 0);
				if (meshData == IntPtr.Zero) return;
				b3ShapeDef def = BuildShapeDef(collider);
				AnvilNative.b3CreateMeshShape(bodyId, ref def, meshData, new b3Vec3(1f, 1f, 1f));
				// b3CreateMeshShape 会保存 b3MeshData 指针，所以不能在这里销毁。
				// 交由 AnvilWorld 在重建静态体时统一回收（见 m_MeshHandles）。
				TrackMesh(meshData);
			}
			finally
			{
				vertices.Dispose();
				indices.Dispose();
			}
		}

		readonly System.Collections.Generic.List<IntPtr> m_MeshHandles = new System.Collections.Generic.List<IntPtr>();

		void TrackMesh(IntPtr mesh)
		{
			m_MeshHandles.Add(mesh);
		}

		void ReleaseMeshes()
		{
			for (int i = 0; i < m_MeshHandles.Count; i++) AnvilNative.b3DestroyMesh(m_MeshHandles[i]);
			m_MeshHandles.Clear();
		}

		// ── step ────────────────────────────────────────────────────────────────

		/// <summary>跑一步 box3d，并返回原生 profile（毫秒）。</summary>
		public b3Profile Step(float timeStep)
		{
			if (!m_Created) return default;
			b3Profile profile;
			AnvilNative.anvil_StepProfile(m_WorldId, timeStep, SubStepCount, &profile);
			return profile;
		}

		/// <summary>把 box3d 的结果写回 PhysicsWorld（随后 ExportPhysicsWorld 会同步到 ECS）。</summary>
		public void SyncFromNative(ref PhysicsWorld world)
		{
			if (!m_Created) return;
			int numDynamic = world.NumDynamicBodies;
			if (numDynamic == 0) return;
			if (numDynamic > m_MotionBodies.Length) numDynamic = m_MotionBodies.Length;

			b3BodyId* bodies = (b3BodyId*)m_MotionBodies.GetUnsafePtr();
			b3WorldTransform* transforms = (b3WorldTransform*)m_ScratchTransforms.GetUnsafePtr();
			b3Vec3* linear = (b3Vec3*)m_ScratchLinear.GetUnsafePtr();
			b3Vec3* angular = (b3Vec3*)m_ScratchAngular.GetUnsafePtr();
			b3WorldTransform* lastReported = m_LastTransforms.IsCreated ? (b3WorldTransform*)m_LastTransforms.GetUnsafePtr() : null;
			int* awakeIndices = m_AwakeIndices.IsCreated ? (int*)m_AwakeIndices.GetUnsafePtr() : null;

			// 只回读「本步真的会有变化」的刚体：box3d 睡眠中的刚体没有 body state，
			// 位姿也不会动，ECS 侧保留上一帧的值即可。大批量堆稳的场景下这条路径能省掉
			// 绝大部分回写（也是 2000+ 刚体时的主要开销）。
			int changed = 0;
			if (lastReported != null && awakeIndices != null)
			{
				changed = AnvilNative.anvil_ReadAwakeFast(m_WorldId, bodies, numDynamic, lastReported,
					awakeIndices, transforms, linear, angular, m_AwakeIndices.Length);
			}
			else
			{
				AnvilNative.anvil_ReadBatchFast(m_WorldId, bodies, numDynamic, transforms, linear, angular);
				changed = numDynamic;
				for (int i = 0; i < changed; i++) awakeIndices[i] = i;
			}

			// 裸指针批量写回：NativeArray 索引器每次访问都要过一遍 job safety 检查，
			// 2000 个刚体 x 约 7 次访问 = 1.4 万次原子检查，实测就要 1 ms 量级。
			MotionData* motionDatas = (MotionData*)world.MotionDatas.GetUnsafePtr();
			MotionVelocity* motionVelocities = (MotionVelocity*)world.MotionVelocities.GetUnsafePtr();
			bool track = m_LastTransforms.IsCreated && m_LastTransforms.Length >= numDynamic;
			b3WorldTransform* lastTransforms = track ? (b3WorldTransform*)m_LastTransforms.GetUnsafePtr() : null;
			b3Vec3* lastLinear = track ? (b3Vec3*)m_LastLinear.GetUnsafePtr() : null;
			b3Vec3* lastAngular = track ? (b3Vec3*)m_LastAngular.GetUnsafePtr() : null;

			for (int k = 0; k < changed; k++)
			{
				int i = awakeIndices[k];
				b3WorldTransform t = transforms[k];
				RigidTransform worldFromBody = new RigidTransform(
					new quaternion(t.q.x, t.q.y, t.q.z, t.q.w),
					new float3(t.p.x, t.p.y, t.p.z));

				MotionData md = motionDatas[i];
				// box3d 的 body transform 就是 world-from-body
				md.WorldFromMotion = math.mul(worldFromBody, md.BodyFromMotion);
				// box3d 的质心即其 body 原点 —— 与 md.BodyFromMotion.pos 对齐
				motionDatas[i] = md;

				MotionVelocity mv = motionVelocities[i];
				mv.LinearVelocity = new float3(linear[k].x, linear[k].y, linear[k].z);
				mv.AngularVelocity = new float3(angular[k].x, angular[k].y, angular[k].z);
				motionVelocities[i] = mv;

				// 记住 box3d 的权威值，供下一帧 NeedsUpload 判断 ECS 是否被外部改写。
				if (track)
				{
					lastTransforms[i] = t;
					lastLinear[i] = linear[k];
					lastAngular[i] = angular[k];
				}
			}
		}

		// ── 查询 ────────────────────────────────────────────────────────────────

		/// <summary>
		/// 直接用 box3d 做一次射线投射。返回 false 表示未命中。
		/// </summary>
		public bool CastRay(float3 origin, float3 translation, float maxFraction, out float fraction, out float3 point, out float3 normal)
		{
			fraction = 1f;
			point = origin;
			normal = new float3(0f, 1f, 0f);
			if (!m_Created) return false;

			b3QueryFilter filter = default;
			filter.categoryBits = ulong.MaxValue;
			filter.maskBits = ulong.MaxValue;
			b3RayResult result = AnvilNative.b3World_CastRayClosest(m_WorldId, ToVec3(origin), ToVec3(translation), filter);
			if (result.hit == 0) return false;

			fraction = math.saturate(result.fraction);
			point = new float3(result.point.x, result.point.y, result.point.z);
			normal = new float3(result.normal.x, result.normal.y, result.normal.z);
			return true;
		}

		/// <summary>
		/// 一次取回射线上<b>全部</b>命中，按距离升序写入 <paramref name="output" />。
		/// </summary>
		/// <remarks>
		/// 三个消费点（PartBehaviorsGuidance / AutoGunProjectileSystem / WheelSystem）都用
		/// <c>AllHitsCollector</c> 拿整条射线的命中再自己剔除「属于本部件」的那些，
		/// 因为射线起点常常落在自己的复合碰撞体内部，<c>CastRayClosest</c> 只给最近一个不够用。
		/// box3d 的 compound 被拆成多个 shape 挂在同一 body 上，因此同一 body 可能在一次
		/// 射线里出现多次 —— 这里每个 body 只保留最近的命中，与 Unity.Physics 的
		/// 「一个根碰撞体一个 key」一致。
		/// </remarks>
		public int CastRayAll(NativeList<RaycastHit> output, float3 origin, float3 translation, uint collidesWith = uint.MaxValue, uint belongsTo = uint.MaxValue)
		{
			output.Clear();
			if (!m_Created) return 0;

			int capacity = math.max(64, m_RayHitCapacity);
			if (!m_ScratchRayHits.IsCreated || m_ScratchRayHits.Length < capacity)
			{
				Grow(ref m_ScratchRayHits, math.ceilpow2(capacity));
			}

			b3AnvilRayHit* hits = (b3AnvilRayHit*)m_ScratchRayHits.GetUnsafePtr();
			// box3d 的查询过滤是双向匹配：(shape.category & query.mask) && (shape.mask & query.category)
			// 与 Unity.Physics 的 (shape.BelongsTo & query.CollidesWith) && (shape.CollidesWith & query.BelongsTo)
			// 一一对应，因此 mask↔CollidesWith、category↔BelongsTo 直接换位。
			int count = AnvilNative.anvil_CastRayAll(m_WorldId, ToVec3(origin), ToVec3(translation),
				belongsTo, collidesWith, hits, m_ScratchRayHits.Length);
			if (count <= 0) return 0;

			for (int i = 0; i < count; i++)
			{
				b3AnvilRayHit h = hits[i];
				Entity entity = EntityFromNativeIndex(h.bodyIndex);
				if (entity == Entity.Null) continue;

				bool duplicate = false;
				for (int k = 0; k < output.Length; k++)
				{
					if (output[k].Entity == entity) { duplicate = true; break; }
				}
				if (duplicate) continue;

				output.Add(new RaycastHit
				{
					Fraction = h.fraction,
					RigidBodyIndex = BodyIndexFromNativeIndex(h.bodyIndex),
					ColliderKey = ColliderKey.Empty,
					Entity = entity,
					Position = new float3(h.point.x, h.point.y, h.point.z),
					SurfaceNormal = new float3(h.normal.x, h.normal.y, h.normal.z),
				});
			}

			// box3d 的回调不保证顺序；三个消费点都按 Fraction 取最近，这里统一升序省掉一次扫描。
			for (int i = 1; i < output.Length; i++)
			{
				RaycastHit item = output[i];
				int j = i - 1;
				while (j >= 0 && output[j].Fraction > item.Fraction)
				{
					output[j + 1] = output[j];
					j--;
				}
				output[j + 1] = item;
			}

			return output.Length;
		}

		/// <summary>b3BodyId.index1 → Entity。越界返回 Entity.Null。</summary>
		public Entity EntityFromNativeIndex(int nativeIndex)
		{
			int index = nativeIndex - 1;
			if (index < 0) return Entity.Null;
			if (index < m_MotionCount) return m_MotionEntities[index];
			int staticIndex = index - m_MotionCount;
			if (staticIndex < m_StaticCount) return m_StaticEntities[staticIndex];
			return Entity.Null;
		}

		// ── 关节 ────────────────────────────────────────────────────────────────

		public int NumJoints => m_JointIds.IsCreated ? m_JointIds.Length : 0;

		void EnsureJointCapacity(int needed)
		{
			if (m_JointIds.IsCreated && m_JointIds.Length >= needed) return;
			int capacity = math.max(k_MinCapacity, math.ceilpow2(math.max(1, needed)));
			Grow(ref m_JointIds, capacity);
			Grow(ref m_JointIndexA, capacity);
			Grow(ref m_JointIndexB, capacity);
			Grow(ref m_JointEntities, capacity);
			Grow(ref m_JointVersions, capacity);
			Grow(ref m_JointForceThreshold, capacity);
			if (!m_JointIdToEntity.IsCreated)
				m_JointIdToEntity = new NativeParallelHashMap<int, Entity>(capacity, m_Allocator);
		}

		void DisposeJoints()
		{
			if (m_Created)
			{
				for (int i = 0; i < m_JointIds.Length; i++)
				{
					if (!m_JointIds[i].IsNull) AnvilNative.b3DestroyJoint(m_JointIds[i]);
				}
			}
			DisposeArray(ref m_JointIds);
			DisposeArray(ref m_JointIndexA);
			DisposeArray(ref m_JointIndexB);
			DisposeArray(ref m_JointEntities);
			DisposeArray(ref m_JointVersions);
			DisposeArray(ref m_JointForceThreshold);
			if (m_JointIdToEntity.IsCreated) m_JointIdToEntity.Dispose();
			m_JointIdToEntity = default;
		}

		/// <summary>
		/// 把 PhysicsWorld.Joints（全是 fixed/weld：bpsim 只生成 PhysicsJoint.CreateFixed）
		/// 同步成 box3d 的 b3_weldJoint。索引对齐 Joints 数组，BodyPair 里 >= NumDynamicBodies
		/// 的索引按 PhysicsWorldBuilder 的约定是静态体（index - NumDynamicBodies）。
		/// </summary>
		public int SyncJointsToNative(ref PhysicsWorld world, float timeStep)
		{
			if (!m_Created) Create();

			int numJoints = world.NumJoints;
			EnsureJointCapacity(math.max(1, numJoints));

			Joint* joints = (Joint*)world.Joints.GetUnsafePtr();
			int numDynamic = world.NumDynamicBodies;
			int numStatic = world.NumStaticBodies;

			// 与 SyncToNative/SyncFromNative 同理：用裸指针避开索引器的 job safety 检查。
			b3JointId* jointIds = (b3JointId*)m_JointIds.GetUnsafePtr();
			int* jointIndexA = (int*)m_JointIndexA.GetUnsafePtr();
			int* jointIndexB = (int*)m_JointIndexB.GetUnsafePtr();
			Entity* jointEntities = (Entity*)m_JointEntities.GetUnsafePtr();
			byte* jointVersions = (byte*)m_JointVersions.GetUnsafePtr();
			float* jointThresholds = (float*)m_JointForceThreshold.GetUnsafePtr();

			for (int i = 0; i < numJoints; i++)
			{
				Joint joint = joints[i];
				int indexA = joint.BodyPair.BodyIndexA;
				int indexB = joint.BodyPair.BodyIndexB;

				if (indexA < 0 || indexB < 0)
				{
					DestroyJointAt(i);
					continue;
				}

				b3BodyId bodyA = BodyIdForIndex(indexA, numDynamic, numStatic);
				b3BodyId bodyB = BodyIdForIndex(indexB, numDynamic, numStatic);
				if (bodyA.IsNull || bodyB.IsNull)
				{
					DestroyJointAt(i);
					continue;
				}

				// 线性冲量阈值：box3d 比的是「力」= 冲量 / dt，故阈值要除以 dt。
				// 注意 Joint 是运行期结构（不是 authoring 的 PhysicsJoint），
				// 其约束块是 internal 的 ConstraintBlock3；本项目代码在 Unity.Physics 程序集内，可直接取。
				float threshold = float.MaxValue;
				ConstraintBlock3 block = joint.Constraints;
				if (block.Length > 0)
				{
					// fixed joint 只有一条 Linear 约束；取第一条的 MaxImpulse.x 即 PartSimulator 传入的 maxImpulse。
					Constraint c = block.GetConstraints()[0];
					if (!float.IsInfinity(c.MaxImpulse.x) && c.MaxImpulse.x > 0f)
						threshold = c.MaxImpulse.x / math.max(1e-5f, timeStep);
				}

				byte version = joint.Version;
				if (!jointIds[i].IsNull
					&& jointEntities[i] == joint.Entity
					&& jointVersions[i] == version
					&& jointIndexA[i] == indexA
					&& jointIndexB[i] == indexB
					&& math.abs(jointThresholds[i] - threshold) <= 1e-3f * math.max(1f, threshold))
				{
					continue;
				}

				DestroyJointAt(i);

				b3WeldJointDef def = AnvilNative.b3DefaultWeldJointDef();
				def.baseDef.bodyIdA = bodyA;
				def.baseDef.bodyIdB = bodyB;
				// 方向约定（两个引擎一致，直接赋值）：
				//   Unity.Physics 的 AFromJoint = "A from joint"，即把 joint 空间的点映射到 body A 空间，
				//     也就是 joint frame 在 body A 局部空间里的位姿；
				//   box3d 的 localFrameA 在 weld_joint.c 里这样用：
				//     frameA.q = bodyA.q * localFrameA.q; frameA.p = rotate(bodyA.q, localFrameA.p - centerA)
				//   同样是「joint frame 在 body A 局部空间里的位姿」。
				// 两者语义相同 → 直接赋值；取逆会让锚点镜像到 body 另一侧，部件会被拽穿地面。
				def.baseDef.localFrameA = ToTransform(joint.AFromJoint);
				def.baseDef.localFrameB = ToTransform(joint.BFromJoint);
				def.baseDef.forceThreshold = threshold;
				def.baseDef.torqueThreshold = float.MaxValue;
				def.baseDef.collideConnected = (byte)(joint.EnableCollision != 0 ? 1 : 0);
				// hertz = 0 → 用世界默认 constraintSoftness（最刚），与 PhysicsJoint 的默认弹簧一致
				def.linearHertz = 0f;
				def.angularHertz = 0f;

				b3JointId id = AnvilNative.b3CreateWeldJoint(m_WorldId, ref def);
				jointIds[i] = id;
				jointIndexA[i] = indexA;
				jointIndexB[i] = indexB;
				jointEntities[i] = joint.Entity;
				jointVersions[i] = version;
				jointThresholds[i] = threshold;

				if (!id.IsNull) m_JointIdToEntity.TryAdd(id.index1, joint.Entity);
			}

			for (int i = numJoints; i < m_JointIds.Length; i++)
			{
				if (jointIds[i].IsNull) break;
				DestroyJointAt(i);
			}

			return numJoints;
		}

		void DestroyJointAt(int i)
		{
			if (m_JointIds[i].IsNull) return;
			m_JointIdToEntity.Remove(m_JointIds[i].index1);
			AnvilNative.b3DestroyJoint(m_JointIds[i]);
			m_JointIds[i] = default;
			m_JointEntities[i] = Entity.Null;
		}

		b3BodyId BodyIdForIndex(int index, int numDynamic, int numStatic)
		{
			if (index < numDynamic)
				return index < m_MotionBodies.Length ? m_MotionBodies[index] : default;

			int staticIndex = index - numDynamic;
			if (staticIndex < 0 || staticIndex >= numStatic || staticIndex >= m_StaticBodies.Length)
				return default;
			return m_StaticBodies[staticIndex];
		}

		/// <summary>
		/// 读回 box3d 的关节事件（阈值超限的关节），把 jointId 映射回 Joint Entity。
		/// 返回写入 output 的条数。
		/// </summary>
		public unsafe int CollectJointEvents(NativeList<Entity> output)
		{
			output.Clear();
			if (!m_Created || !m_JointIdToEntity.IsCreated) return 0;

			b3JointEvents events = AnvilNative.b3World_GetJointEvents(m_WorldId);
			if (events.count <= 0 || events.jointEvents == IntPtr.Zero) return 0;

			b3JointEvent* items = (b3JointEvent*)events.jointEvents;
			int written = 0;
			for (int i = 0; i < events.count; i++)
			{
				if (!m_JointIdToEntity.TryGetValue(items[i].jointId.index1, out Entity entity)) continue;
				output.Add(entity);
				written++;
			}
			return written;
		}

		// ── 接触事件 ────────────────────────────────────────────────────────────

		/// <summary>
		/// 收集本步所有接触点，输出 body-index 对（相对 MotionDatas 索引）。
		/// 结果可能重复，调用方用 hash set 去重。
		/// </summary>

		/// <summary>
		/// 收集本步所有接触，直接写成 AnvilContactEvent，并把 body 索引（0..NumDynamic-1 为动态，
		/// NumDynamic.. 为静态）暂存在 EntityA.Index / EntityB.Index 里，由调用方映射成 Entity。
		/// </summary>
		public int CollectContactEvents(NativeList<AnvilContactEvent> output, int capacity)
		{
			output.Clear();
			if (!m_Created || capacity <= 0) return 0;

			// 快路径：直接遍历 world 的接触数组，每个 manifold 只输出一个代表点。
			// bodyA/bodyB 里存的是 box3d 的 b3BodyId.index1（= bodyIndex + 1），
			// 调用方用 LookupEntity 换成 Entity。
			int max = math.max(64, capacity);
			if (!m_ScratchContacts.IsCreated || m_ScratchContacts.Length < max)
			{
				Grow(ref m_ScratchContacts, math.ceilpow2(max));
			}

			b3AnvilContact* contacts = (b3AnvilContact*)m_ScratchContacts.GetUnsafePtr();
			int written = AnvilNative.anvil_CollectAllContacts(m_WorldId, contacts, m_ScratchContacts.Length);

			for (int i = 0; i < written && output.Length < capacity; i++)
			{
				b3AnvilContact c = m_ScratchContacts[i];
				output.Add(new AnvilContactEvent
				{
					EntityA = new Entity { Index = c.bodyA },
					EntityB = new Entity { Index = c.bodyB },
					Position = new float3(c.point.x, c.point.y, c.point.z),
					Normal = new float3(c.normal.x, c.normal.y, c.normal.z),
					Impulse = c.normalImpulse,
				});
			}
			return output.Length;
		}

		/// <summary>
		/// b3BodyId.index1（1-based）→ PhysicsWorld 里的 body 索引；越界返回 -1。
		/// 动态体在 [0, NumDynamic)，静态体在 [NumDynamic, NumDynamic + NumStatic)。
		/// </summary>
		public int BodyIndexFromNativeIndex(int nativeIndex)
		{
			int index = nativeIndex - 1;
			if (index < 0) return -1;

			int numDynamic = m_MotionCount;
			if (index < numDynamic) return index;

			int staticIndex = index - numDynamic;
			if (staticIndex < m_StaticCount) return numDynamic + staticIndex;
			return -1;
		}

				public int CollectContacts(NativeList<AnvilContactPair> output, int capacity)
		{
			output.Clear();
			if (!m_Created || capacity <= 0) return 0;
			EnsureContactCapacity(capacity);

			b3BodyId* bodies = (b3BodyId*)m_MotionBodies.GetUnsafePtr();
			b3AnvilContact* contacts = (b3AnvilContact*)m_ScratchContacts.GetUnsafePtr();
			int written = AnvilNative.anvil_CollectContacts(bodies, math.min(MotionCapacity, m_MotionBodies.Length), contacts, capacity);

			for (int i = 0; i < written; i++)
			{
				b3AnvilContact c = m_ScratchContacts[i];
				output.Add(new AnvilContactPair
				{
					BodyA = c.bodyA,
					BodyB = c.bodyB,
					Point = new float3(c.point.x, c.point.y, c.point.z),
					Normal = new float3(c.normal.x, c.normal.y, c.normal.z),
					Impulse = c.normalImpulse,
				});
			}
			return written;
		}

		public b3Counters GetCounters()
		{
			if (!m_Created) return default;
			return AnvilNative.b3World_GetCounters(m_WorldId);
		}

		// ── 工具 ────────────────────────────────────────────────────────────────

		public static b3Vec3 ToVec3(float3 value) => new b3Vec3(value.x, value.y, value.z);

		public static b3Quat ToQuat(quaternion value) => new b3Quat(value.value.x, value.value.y, value.value.z, value.value.w);

		public static b3WorldTransform ToWorldTransform(RigidTransform transform) => new b3WorldTransform
		{
			p = ToVec3(transform.pos),
			q = ToQuat(transform.rot),
		};

		/// <summary>MTransform（Joint.AFromJoint / BFromJoint）→ b3Transform。</summary>
		public static b3Transform ToTransform(Unity.Physics.Math.MTransform transform) => new b3Transform
		{
			p = ToVec3(transform.Translation),
			q = ToQuat(new quaternion(transform.Rotation)),
		};

		static float3 TransformPoint(RigidTransform transform, float3 point) => math.transform(transform, point);
	}

	/// <summary>托管侧的接触点（去重前）。</summary>
	public struct AnvilContactPair
	{
		public int BodyA;
		public int BodyB;
		public float3 Point;
		public float3 Normal;
		public float Impulse;
	}
}
