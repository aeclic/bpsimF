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
using System.Runtime.InteropServices;
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

		// 默认 def 是纯值类型结构体：从 box3d 读一次即可复用。原来每建 1 个
		// body / shape / joint 都要 P/Invoke 取默认值，建 2k 刚体场景时是数千次
		// 跨托管/原生调用 —— 「开始模拟时大量卡顿」的主要来源。
		static b3WorldDef s_WorldDef;
		static b3BodyDef s_BodyDef;
		static b3ShapeDef s_ShapeDef;
		static b3WeldJointDef s_WeldJointDef;
		static bool s_DefsCached;
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		delegate float MaterialCombineCallback(float valueA, ulong materialA, float valueB, ulong materialB);
		// Native workers may call these callbacks. Keep delegates alive and callbacks pure.
		static readonly MaterialCombineCallback s_FrictionMixer = CombineFriction;
		static readonly MaterialCombineCallback s_RestitutionMixer = CombineRestitution;

		static void CacheDefs()
		{
			if (s_DefsCached) return;
			s_WorldDef = AnvilNative.b3DefaultWorldDef();
			s_BodyDef = AnvilNative.b3DefaultBodyDef();
			s_ShapeDef = AnvilNative.b3DefaultShapeDef();
			s_WeldJointDef = AnvilNative.b3DefaultWeldJointDef();
			s_WorldDef.frictionCallback = Marshal.GetFunctionPointerForDelegate(s_FrictionMixer);
			s_WorldDef.restitutionCallback = Marshal.GetFunctionPointerForDelegate(s_RestitutionMixer);
			s_DefsCached = true;
		}

		[AOT.MonoPInvokeCallback(typeof(MaterialCombineCallback))]
		static float CombineFriction(float a, ulong materialA, float b, ulong materialB) =>
			CombineCoefficient(a, b, (int)math.max((uint)(materialA & 0xff), (uint)(materialB & 0xff)));

		[AOT.MonoPInvokeCallback(typeof(MaterialCombineCallback))]
		static float CombineRestitution(float a, ulong materialA, float b, ulong materialB) =>
			CombineCoefficient(a, b, (int)math.max((uint)((materialA >> 8) & 0xff), (uint)((materialB >> 8) & 0xff)));

		static float CombineCoefficient(float a, float b, int policy)
		{
			switch ((Material.CombinePolicy)policy)
			{
				case Material.CombinePolicy.GeometricMean: return math.sqrt(a * b);
				case Material.CombinePolicy.Minimum: return math.min(a, b);
				case Material.CombinePolicy.Maximum: return math.max(a, b);
				case Material.CombinePolicy.ArithmeticMean: return (a + b) * 0.5f;
				default: return 0f;
			}
		}

		b3WorldId m_WorldId;
		bool m_Created;
		int m_MotionCount;
		int m_StaticCount;

		NativeArray<b3BodyId> m_MotionBodies;
		NativeArray<Entity> m_MotionEntities;
		NativeArray<IntPtr> m_MotionColliders;
		NativeArray<float> m_MotionScales;
		NativeArray<byte> m_MotionColliderVersions;
		NativeArray<MotionProperties> m_MotionProperties;
		NativeArray<byte> m_PendingDamping;

		NativeArray<b3BodyId> m_StaticBodies;
		NativeArray<Entity> m_StaticEntities;
		NativeArray<IntPtr> m_StaticColliders;
		NativeArray<float> m_StaticScales;
		NativeArray<byte> m_StaticColliderVersions;
		NativeArray<RigidTransform> m_StaticTransforms;
		List<IntPtr>[] m_StaticMeshHandles = Array.Empty<List<IntPtr>>();
		int m_BuildingStaticIndex = -1;
		readonly Dictionary<int, int> m_NativeBodyIndices = new Dictionary<int, int>();
		float m_LastTimeStep;
		float3 m_Gravity = new float3(0f, -9.81f, 0f);
		bool m_GravityChanged;

		struct MotionProperties
		{
			public RigidTransform BodyFromMotion;
			public float3 InverseInertia;
			public float InverseMass;
			public float GravityFactor;

			public bool IsKinematic => InverseMass == 0f && !math.any(InverseInertia);
			public bool HasSameMass(in MotionProperties other) => InverseMass == other.InverseMass
				&& math.all(InverseInertia == other.InverseInertia) && BodyFromMotion.Equals(other.BodyFromMotion);

			public MotionProperties(in MotionData data, in MotionVelocity velocity)
			{
				BodyFromMotion = data.BodyFromMotion;
				InverseInertia = velocity.InverseInertia;
				InverseMass = velocity.InverseMass;
				GravityFactor = velocity.GravityFactor;
			}
		}

		NativeArray<b3WorldTransform> m_ScratchTransforms;
		NativeArray<b3Vec3> m_ScratchLinear;
		NativeArray<b3Vec3> m_ScratchAngular;
		NativeArray<b3AnvilContact> m_ScratchContacts;
		NativeArray<b3Vec3> m_ScratchPoints;

		// 上次从 box3d 读回的位姿/速度，用来判断 ECS 侧是否被外部改写（瞬移/改速度）。
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
		NativeArray<JointSettings> m_JointSettings;
		NativeArray<Joint> m_JointDescriptors;
		NativeArray<float3> m_JointLinearImpulse;
		NativeArray<float3> m_JointAngularImpulse;
		int m_JointCount;
		readonly Dictionary<ulong, FrameContact> m_FrameContacts = new Dictionary<ulong, FrameContact>(ContactPairComparer.Instance);
		bool m_HasSubStepContacts;

		sealed class ContactPairComparer : IEqualityComparer<ulong>
		{
			public static readonly ContactPairComparer Instance = new ContactPairComparer();
			public bool Equals(ulong a, ulong b) => a == b;
			public int GetHashCode(ulong value)
			{
				// UInt64's default high-XOR-low hash floods buckets for adjacent/grid body
				// IDs. Avalanche the full pair, retaining exact equality and sampling order.
				unchecked
				{
					value ^= value >> 33;
					value *= 0xff51afd7ed558ccdUL;
					value ^= value >> 33;
					value *= 0xc4ceb9fe1a85ec53UL;
					value ^= value >> 33;
					return (int)(value ^ (value >> 32));
				}
			}
		}

		struct JointSettings
		{
			public b3BodyId BodyA, BodyB;
			public Entity Entity;
			public byte Version, EnableCollision, EventMask;
			public Constraint Linear, Angular;
			public bool HasLinear, HasAngular;
			public RigidTransform FrameA, FrameB;

			public bool NeedsLinearEvents => (EventMask & 1) != 0;
			public bool NeedsAngularEvents => (EventMask & 2) != 0;
			public bool Matches(in JointSettings other) => SameBody(BodyA, other.BodyA) && SameBody(BodyB, other.BodyB)
				&& Entity == other.Entity && Version == other.Version && EnableCollision == other.EnableCollision
				&& FrameA.Equals(other.FrameA) && FrameB.Equals(other.FrameB)
				&& HasLinear == other.HasLinear && HasAngular == other.HasAngular
				&& (!HasLinear || Linear.Equals(other.Linear)) && (!HasAngular || Angular.Equals(other.Angular));
		}

		struct FrameContact
		{
			public AnvilContactEvent Event;
			public float PeakImpulse;
		}

		static bool SameBody(b3BodyId a, b3BodyId b) => a.index1 == b.index1 && a.world0 == b.world0 && a.generation == b.generation;

		Allocator m_Allocator = Allocator.Persistent;

		public int WorkerCount = 1;
		public int SubStepCount = 4;
		public float ContactHertz = 60f;
		public float ContactDampingRatio = 10f;
		public bool EnableSleep = true;
		public bool EnableContinuous = true;

		public bool IsCreated => m_Created;
		public b3WorldId WorldId => m_WorldId;

		/// <summary>当前是否持有 body；模拟间动态体清零时用于判定是否需要整体重建。</summary>
		public bool HasBodies => m_MotionCount > 0 || m_StaticCount > 0;

		public int MotionCapacity => m_MotionBodies.IsCreated ? m_MotionBodies.Length : 0;
		public int StaticCapacity => m_StaticBodies.IsCreated ? m_StaticBodies.Length : 0;

		// ── 生命周期 ────────────────────────────────────────────────────────────

		public void Create()
		{
			if (m_Created) return;

			CacheDefs();
			b3WorldDef def = s_WorldDef;
			// Gravity is applied once before the outer step, like Unity's solver.
			def.gravity = b3Vec3.zero;
			def.workerCount = (uint)math.max(1, WorkerCount);
			def.enableSleep = (byte)(EnableSleep ? 1 : 0);
			def.enableContinuous = (byte)(EnableContinuous ? 1 : 0);
			def.contactHertz = ContactHertz;
			def.contactDampingRatio = ContactDampingRatio;
			// Match supplied Unity velocities; the native defaults cap free momentum (F115).
			def.maximumLinearSpeed = float.MaxValue;

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
			ReleaseMeshes();
			m_MotionCount = m_StaticCount = 0;
			m_JointCount = 0;
			m_FrameContacts.Clear();
			m_HasSubStepContacts = false;
			m_NativeBodyIndices.Clear();
			m_UploadIndices.Clear();
			if (m_MotionBodies.IsCreated) UnsafeUtility.MemClear(m_MotionBodies.GetUnsafePtr(), m_MotionBodies.Length * sizeof(b3BodyId));
			if (m_StaticBodies.IsCreated) UnsafeUtility.MemClear(m_StaticBodies.GetUnsafePtr(), m_StaticBodies.Length * sizeof(b3BodyId));
			if (m_JointIds.IsCreated) UnsafeUtility.MemClear(m_JointIds.GetUnsafePtr(), m_JointIds.Length * sizeof(b3JointId));
		}

		public void Dispose()
		{
			Destroy();
			ReleaseMeshes();

			DisposeArray(ref m_MotionBodies);
			DisposeArray(ref m_MotionEntities);
			DisposeArray(ref m_MotionColliders);
			DisposeArray(ref m_MotionScales);
			DisposeArray(ref m_MotionColliderVersions);
			DisposeArray(ref m_MotionProperties);
			DisposeArray(ref m_PendingDamping);
			DisposeArray(ref m_StaticBodies);
			DisposeArray(ref m_StaticEntities);
			DisposeArray(ref m_StaticColliders);
			DisposeArray(ref m_StaticScales);
			DisposeArray(ref m_StaticColliderVersions);
			DisposeArray(ref m_StaticTransforms);
			DisposeArray(ref m_ScratchTransforms);
			DisposeArray(ref m_ScratchLinear);
			DisposeArray(ref m_ScratchAngular);
			DisposeArray(ref m_ScratchContacts);
			DisposeArray(ref m_ScratchPoints);
			DisposeArray(ref m_LastTransforms);
			DisposeArray(ref m_LastLinear);
			DisposeArray(ref m_LastAngular);
			DisposeArray(ref m_UploadBodies);
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
			m_GravityChanged |= math.any(m_Gravity != gravity);
			m_Gravity = gravity;
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
			Grow(ref m_MotionColliderVersions, capacity);
			Grow(ref m_MotionProperties, capacity);
			Grow(ref m_PendingDamping, capacity);
			Grow(ref m_ScratchTransforms, capacity);
			Grow(ref m_ScratchLinear, capacity);
			Grow(ref m_ScratchAngular, capacity);
			Grow(ref m_LastTransforms, capacity);
			Grow(ref m_LastLinear, capacity);
			Grow(ref m_LastAngular, capacity);
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
			Grow(ref m_StaticColliderVersions, capacity);
			Grow(ref m_StaticTransforms, capacity);
			Array.Resize(ref m_StaticMeshHandles, capacity);
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
			byte* motionColliderVersions = (byte*)m_MotionColliderVersions.GetUnsafePtr();
			MotionProperties* properties = (MotionProperties*)m_MotionProperties.GetUnsafePtr();
			byte* pendingDamping = (byte*)m_PendingDamping.GetUnsafePtr();
			b3WorldTransform* scratchTransforms = (b3WorldTransform*)m_ScratchTransforms.GetUnsafePtr();
			b3Vec3* scratchLinear = (b3Vec3*)m_ScratchLinear.GetUnsafePtr();
			b3Vec3* scratchAngular = (b3Vec3*)m_ScratchAngular.GetUnsafePtr();
			bool topologyChanged = numDynamic != m_MotionCount || numStatic != m_StaticCount;

			int writable = 0;
			for (int i = 0; i < numDynamic; i++)
			{
				RigidBody body = dynamicBodies[i];
				IntPtr colliderPtr = body.Collider.IsCreated ? (IntPtr)body.Collider.GetUnsafePtr() : IntPtr.Zero;
				byte colliderVersion = colliderPtr == IntPtr.Zero ? (byte)0 : ((ColliderHeader*)colliderPtr)->Version;
				MotionProperties current = new MotionProperties(motionDatas[i], motionVelocities[i]);

				if (!motionBodies[i].IsNull && motionEntities[i] == body.Entity && motionColliders[i] == colliderPtr
					&& motionScales[i] == body.Scale && motionColliderVersions[i] == colliderVersion)
				{
					bool massChanged = !current.HasSameMass(properties[i]);
					if (current.IsKinematic != properties[i].IsKinematic)
						AnvilNative.b3Body_SetType(motionBodies[i], (int)(current.IsKinematic ? b3BodyType.b3_kinematicBody : b3BodyType.b3_dynamicBody));
					if (massChanged)
					{
						ApplyMass(motionBodies[i], motionDatas[i], motionVelocities[i]);
						AnvilNative.b3Body_SetAwake(motionBodies[i], 1);
					}
					if (current.GravityFactor != properties[i].GravityFactor)
					{
						AnvilNative.b3Body_SetGravityScale(motionBodies[i], current.GravityFactor);
						AnvilNative.b3Body_SetAwake(motionBodies[i], 1);
					}
					properties[i] = current;
					bool externalChange = !trackLast
						|| NeedsUploadFast(lastTransforms, lastLinearArr, lastAngularArr, i, body.WorldFromBody, motionVelocities[i]);
					if (massChanged || pendingDamping[i] != 0 || !trackLast
						|| externalChange)
					{
						// The raw batch writer cannot allocate a sleeping body's native state.
						if (externalChange) AnvilNative.b3Body_SetAwake(motionBodies[i], 1);
						// The compressed slot is not the source body's index (F103).
						m_UploadIndices.Add(i);
						scratchTransforms[writable] = ToWorldTransform(body.WorldFromBody);
						scratchLinear[writable] = ToVec3(motionVelocities[i].LinearVelocity);
						scratchAngular[writable] = ToVec3(math.rotate(motionDatas[i].WorldFromMotion.rot, motionVelocities[i].AngularVelocity));
						pendingDamping[i] = 0;
						writable++;
					}
					continue;
				}

				// 新刚体或索引漂移：重建
				if (!motionBodies[i].IsNull)
				{
					if (AnvilNative.b3Body_IsValid(motionBodies[i]) != 0)
						AnvilNative.b3DestroyBody(motionBodies[i]);
					motionBodies[i] = default;
				}

				b3BodyId id = CreateDynamicBody(body, motionDatas[i], motionVelocities[i]);
				topologyChanged = true;
				motionBodies[i] = id;
				motionEntities[i] = body.Entity;
				motionColliders[i] = colliderPtr;
				motionScales[i] = body.Scale;
				motionColliderVersions[i] = colliderVersion;
				properties[i] = current;
				pendingDamping[i] = 0;

				m_UploadIndices.Add(i);
				scratchTransforms[writable] = ToWorldTransform(body.WorldFromBody);
				scratchLinear[writable] = ToVec3(motionVelocities[i].LinearVelocity);
				scratchAngular[writable] = ToVec3(math.rotate(motionDatas[i].WorldFromMotion.rot, motionVelocities[i].AngularVelocity));
				writable++;
			}

			// 末尾多余的旧 body（数量下降）
			for (int i = numDynamic; i < m_MotionBodies.Length; i++)
			{
				if (motionBodies[i].IsNull) continue;
				if (AnvilNative.b3Body_IsValid(motionBodies[i]) != 0)
					AnvilNative.b3DestroyBody(motionBodies[i]);
				motionBodies[i] = default;
				motionEntities[i] = Entity.Null;
				topologyChanged = true;
			}

			// 静态体：只在新建/漂移时重建，位置每帧不写（静态不会被积分）
			b3BodyId* staticBodyIds = (b3BodyId*)m_StaticBodies.GetUnsafePtr();
			Entity* staticEntities = (Entity*)m_StaticEntities.GetUnsafePtr();
			IntPtr* staticColliders = (IntPtr*)m_StaticColliders.GetUnsafePtr();
			float* staticScales = (float*)m_StaticScales.GetUnsafePtr();
			byte* staticColliderVersions = (byte*)m_StaticColliderVersions.GetUnsafePtr();
			RigidTransform* staticTransforms = (RigidTransform*)m_StaticTransforms.GetUnsafePtr();

			for (int i = 0; i < numStatic; i++)
			{
				RigidBody body = staticBodies[i];
				IntPtr colliderPtr = body.Collider.IsCreated ? (IntPtr)body.Collider.GetUnsafePtr() : IntPtr.Zero;
				byte colliderVersion = colliderPtr == IntPtr.Zero ? (byte)0 : ((ColliderHeader*)colliderPtr)->Version;
				if (!staticBodyIds[i].IsNull && staticEntities[i] == body.Entity && staticColliders[i] == colliderPtr
					&& staticScales[i] == body.Scale && staticColliderVersions[i] == colliderVersion)
				{
					if (!staticTransforms[i].Equals(body.WorldFromBody))
					{
						AnvilNative.b3Body_SetTransform(staticBodyIds[i], ToVec3(body.WorldFromBody.pos), ToQuat(body.WorldFromBody.rot));
						staticTransforms[i] = body.WorldFromBody;
					}
					continue;
				}

				if (!staticBodyIds[i].IsNull)
				{
					if (AnvilNative.b3Body_IsValid(staticBodyIds[i]) != 0)
						AnvilNative.b3DestroyBody(staticBodyIds[i]);
					staticBodyIds[i] = default;
					ReleaseMeshesAt(i);
				}

				b3BodyId id = CreateStaticBody(body, i);
				topologyChanged = true;
				staticBodyIds[i] = id;
				staticEntities[i] = body.Entity;
				staticColliders[i] = colliderPtr;
				staticScales[i] = body.Scale;
				staticColliderVersions[i] = colliderVersion;
				staticTransforms[i] = body.WorldFromBody;
			}

			for (int i = numStatic; i < m_StaticBodies.Length; i++)
			{
				if (staticBodyIds[i].IsNull) continue;
				if (AnvilNative.b3Body_IsValid(staticBodyIds[i]) != 0)
					AnvilNative.b3DestroyBody(staticBodyIds[i]);
				staticBodyIds[i] = default;
				staticEntities[i] = Entity.Null;
				topologyChanged = true;
				ReleaseMeshesAt(i);
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
			if (topologyChanged)
			{
				// Native IDs are pool handles, not PhysicsWorld array ordinals (F108).
				m_NativeBodyIndices.Clear();
				for (int i = 0; i < numDynamic; i++)
					if (!motionBodies[i].IsNull) m_NativeBodyIndices[motionBodies[i].index1] = i;
				for (int i = 0; i < numStatic; i++)
					if (!staticBodyIds[i].IsNull) m_NativeBodyIndices[staticBodyIds[i].index1] = numDynamic + i;
			}
			return numDynamic;
		}

		/// <summary>
		/// ECS 侧的值是否已经偏离 box3d 上次报回的值（= 外部逻辑改写了位姿/速度/或刚生成）。
		/// Poses allow round-trip arithmetic noise; exported velocities compare exactly so small forces
		/// are not lost. Cached angular velocities are in Unity motion space, not native world space.
		/// </summary>
		static bool NeedsUploadFast(b3WorldTransform* lastTransforms, b3Vec3* lastLinear, b3Vec3* lastAngular,
			int index, in RigidTransform worldFromBody, in MotionVelocity velocity)
		{
			b3WorldTransform last = lastTransforms[index];
			float3 lastPos = new float3(last.p.x, last.p.y, last.p.z);
			if (math.distancesq(lastPos, worldFromBody.pos) > 1e-8f) return true;

			quaternion lastRot = new quaternion(last.q.x, last.q.y, last.q.z, last.q.w);
			if (math.min(math.distancesq(lastRot.value, worldFromBody.rot.value),
				math.distancesq(lastRot.value, -worldFromBody.rot.value)) > 1e-12f) return true;

			b3Vec3 ll = lastLinear[index];
			if (!math.all(new float3(ll.x, ll.y, ll.z) == velocity.LinearVelocity)) return true;

			b3Vec3 la = lastAngular[index];
			if (!math.all(new float3(la.x, la.y, la.z) == velocity.AngularVelocity)) return true;

			return false;
		}

		static bool ReuseOk(NativeArray<Entity> entities, NativeArray<IntPtr> colliders, int index, Entity entity, IntPtr collider)
		{
			return entities[index] == entity && colliders[index] == collider;
		}

		b3BodyId CreateDynamicBody(in RigidBody body, in MotionData motionData, in MotionVelocity motionVelocity)
		{
			b3BodyDef def = s_BodyDef;
			def.type = (int)(motionVelocity.IsKinematic ? b3BodyType.b3_kinematicBody : b3BodyType.b3_dynamicBody);
			def.position = ToVec3(body.WorldFromBody.pos);
			def.rotation = ToQuat(body.WorldFromBody.rot);
			def.linearVelocity = ToVec3(motionVelocity.LinearVelocity);
			def.angularVelocity = ToVec3(math.rotate(motionData.WorldFromMotion.rot, motionVelocity.AngularVelocity));
			// Unity damps after integration/constraints; applying native damping as well double-counts it.
			def.linearDamping = 0f;
			def.angularDamping = 0f;
			def.gravityScale = motionVelocity.GravityFactor;
			def.allowFastRotation = 1;
			def.enableSleep = (byte)(EnableSleep ? 1 : 0);

			b3BodyId id = AnvilNative.b3CreateBody(m_WorldId, ref def);
			if (id.IsNull) throw new InvalidOperationException("Anvil: dynamic body creation failed for " + body.Entity);

			AddShapes(id, body, false);
			ApplyMass(id, motionData, motionVelocity);
			return id;
		}

		b3BodyId CreateStaticBody(in RigidBody body, int staticIndex)
		{
			b3BodyDef def = s_BodyDef;
			def.type = (int)b3BodyType.b3_staticBody;
			def.position = ToVec3(body.WorldFromBody.pos);
			def.rotation = ToQuat(body.WorldFromBody.rot);
			def.enableSleep = 0;

			b3BodyId id = AnvilNative.b3CreateBody(m_WorldId, ref def);
			if (id.IsNull) throw new InvalidOperationException("Anvil: static body creation failed for " + body.Entity);

			m_BuildingStaticIndex = staticIndex;
			try
			{
				AddShapes(id, body, true);
			}
			finally
			{
				m_BuildingStaticIndex = -1;
			}
			return id;
		}

		void ApplyMass(b3BodyId id, in MotionData motionData, in MotionVelocity motionVelocity)
		{
			float3 inverseInertia = new float3(
				motionVelocity.InverseInertia.x,
				motionVelocity.InverseInertia.y,
				motionVelocity.InverseInertia.z);
			float3 inertiaPrincipal = new float3(
				inverseInertia.x > 0f ? 1f / inverseInertia.x : 0f,
				inverseInertia.y > 0f ? 1f / inverseInertia.y : 0f,
				inverseInertia.z > 0f ? 1f / inverseInertia.z : 0f);
			if (math.any(inverseInertia > 0f) && !math.all(inverseInertia > 0f))
			{
				// Zero inverse inertia means an infinite moment, not a zero moment (F109).
				// The native API only inverts nonsingular matrices. A well-conditioned finite
				// approximation retains free-axis response instead of disabling every axis.
				float infiniteMoment = math.max(1e-6f, math.cmax(inertiaPrincipal)) * 1e4f;
				inertiaPrincipal = math.select(new float3(infiniteMoment), inertiaPrincipal, inverseInertia > 0f);
			}

			// Unity.Physics 的主轴空间 → body 空间
			float3x3 rotation = new float3x3(motionData.BodyFromMotion.rot);
			float3x3 inertiaMatrix = math.mul(math.mul(rotation, new float3x3(
				inertiaPrincipal.x, 0f, 0f,
				0f, inertiaPrincipal.y, 0f,
				0f, 0f, inertiaPrincipal.z)), math.transpose(rotation));

			var massData = new b3MassData
			{
				mass = motionVelocity.InverseMass > 0f ? 1f / motionVelocity.InverseMass : 0f,
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
					AddConvexHull(bodyId, collider, ref convex->ConvexHull, scale, offset);
					break;
				}
				case ColliderType.Cylinder:
				{
					CylinderCollider* cylinder = (CylinderCollider*)collider;
					AddConvexHull(bodyId, collider, ref cylinder->ConvexHull, scale, offset);
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
						// Child.Collider is self-relative: copying Child would relocate its pointer.
						ref CompoundCollider.Child child = ref accessor[i];
						RigidTransform childFrame = child.CompoundFromChild;
						childFrame.pos *= scale;
						RigidTransform childOffset = math.mul(offset, childFrame);
						AddShapeRecursive(bodyId, child.Collider, scale, childOffset, isStatic);
					}
					break;
				}
				default:
					// Terrain / standalone Triangle / Quad require dedicated native geometry.
					break;
			}
		}

		void AddConvexHull(b3BodyId bodyId, Collider* collider, ref ConvexHull hull, float scale, RigidTransform offset)
		{
			// Blob array offsets are relative to hull's actual address, not a stack copy (F117).
			var vertices = hull.Vertices;
			int count = vertices.Length;
			if (count < 4) return;
			EnsurePointCapacity(count);
			for (int i = 0; i < count; i++)
				m_ScratchPoints[i] = ToVec3(TransformPoint(offset, vertices[i] * scale));
			CreateHullShape(bodyId, collider, m_ScratchPoints, count);
		}

		b3ShapeDef BuildShapeDef(Collider* collider)
		{
			b3ShapeDef def = s_ShapeDef;
			CollisionFilter filter = collider->GetCollisionFilter();
			def.filter.categoryBits = filter.BelongsTo;
			def.filter.maskBits = filter.CollidesWith;
			def.filter.groupIndex = filter.GroupIndex;

			// GetMaterial 是 public，靠 AssemblyInfo 的 InternalsVisibleTo 访问
			Material material = collider->GetMaterial(ColliderKey.Empty);
			def.baseMaterial.friction = material.Friction;
			def.baseMaterial.restitution = material.Restitution;
			def.baseMaterial.userMaterialId = (ulong)material.FrictionCombinePolicy
				| ((ulong)material.RestitutionCombinePolicy << 8);

			CollisionResponsePolicy response = collider->GetCollisionResponse();
			bool isTrigger = response == CollisionResponsePolicy.RaiseTriggerEvents;

			// 只有明确要求发碰撞事件的碰撞体才开事件，避免全局额外开销
			// None still participates in queries, but must not transfer collision momentum.
			def.isSensor = (byte)(isTrigger || response == CollisionResponsePolicy.None ? 1 : 0);
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

			ref Mesh mesh = ref meshCollider->Mesh;
			var sections = mesh.Sections;
			int sectionCount = sections.Length;
			if (sectionCount == 0) return;

			int vertexTotal = 0, triangleTotal = 0;
			for (int s = 0; s < sectionCount; s++)
			{
				ref var section = ref sections[s];
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
					ref var section = ref sections[s];
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
			if (m_BuildingStaticIndex < 0 || m_BuildingStaticIndex >= m_StaticMeshHandles.Length)
				throw new InvalidOperationException("Anvil: mesh created outside static body construction.");
			if (m_StaticMeshHandles[m_BuildingStaticIndex] == null)
				m_StaticMeshHandles[m_BuildingStaticIndex] = new List<IntPtr>();
			m_StaticMeshHandles[m_BuildingStaticIndex].Add(mesh);
		}

		void ReleaseMeshes()
		{
			for (int i = 0; i < m_StaticMeshHandles.Length; i++) ReleaseMeshesAt(i);
		}

		void ReleaseMeshesAt(int staticIndex)
		{
			if (staticIndex < 0 || staticIndex >= m_StaticMeshHandles.Length) return;
			List<IntPtr> meshes = m_StaticMeshHandles[staticIndex];
			if (meshes == null) return;
			for (int i = 0; i < meshes.Count; i++) AnvilNative.b3DestroyMesh(meshes[i]);
			meshes.Clear();
			m_StaticMeshHandles[staticIndex] = null;
		}

		// ── step ────────────────────────────────────────────────────────────────

		/// <summary>跑一步 box3d，并返回原生 profile（毫秒）。</summary>
		public b3Profile Step(float timeStep)
		{
			if (!m_Created) return default;
			if (!math.isfinite(timeStep) || timeStep <= 0f) throw new ArgumentOutOfRangeException(nameof(timeStep));
			if (SubStepCount < 1) throw new ArgumentOutOfRangeException(nameof(SubStepCount));
			m_LastTimeStep = timeStep;
			bool hasGravity = math.any(m_Gravity != float3.zero);
			MotionProperties* motionProperties = m_MotionCount > 0 ? (MotionProperties*)m_MotionProperties.GetUnsafePtr() : null;
			b3BodyId* motionBodies = m_MotionCount > 0 ? (b3BodyId*)m_MotionBodies.GetUnsafePtr() : null;
			for (int i = 0; (hasGravity || m_GravityChanged) && i < m_MotionCount; i++)
			{
				MotionProperties properties = motionProperties[i];
				if (properties.InverseMass <= 0f || properties.GravityFactor == 0f) continue;
				if (m_GravityChanged) AnvilNative.b3Body_SetAwake(motionBodies[i], 1);
				if (hasGravity)
				{
					float3 impulse = m_Gravity * (timeStep * properties.GravityFactor / properties.InverseMass);
					// Do not wake resting bodies solely to reapply their balanced gravity load.
					AnvilNative.b3Body_ApplyLinearImpulseToCenter(motionBodies[i], ToVec3(impulse), 0);
				}
			}
			m_GravityChanged = false;
			if (m_JointCount > 0)
			{
				UnsafeUtility.MemClear(m_JointLinearImpulse.GetUnsafePtr(), m_JointCount * sizeof(float3));
				UnsafeUtility.MemClear(m_JointAngularImpulse.GetUnsafePtr(), m_JointCount * sizeof(float3));
			}
			m_FrameContacts.Clear();
			m_HasSubStepContacts = SubStepCount > 1;
			// The world is exclusively stepped after ECS dependencies complete. Check each
			// buffer once here instead of repeating NativeArray safety checks per joint/substep.
			b3JointId* jointIds = m_JointCount > 0 ? (b3JointId*)m_JointIds.GetUnsafePtr() : null;
			JointSettings* jointSettings = m_JointCount > 0 ? (JointSettings*)m_JointSettings.GetUnsafePtr() : null;
			float3* linearImpulses = m_JointCount > 0 ? (float3*)m_JointLinearImpulse.GetUnsafePtr() : null;
			float3* angularImpulses = m_JointCount > 0 ? (float3*)m_JointAngularImpulse.GetUnsafePtr() : null;
			b3Profile profile = default;
			float h = timeStep / SubStepCount;
			for (int subStep = 0; subStep < SubStepCount; subStep++)
			{
				b3Profile subProfile;
				AnvilNative.anvil_StepProfile(m_WorldId, h, 1, &subProfile);
				float* total = (float*)&profile;
				float* sample = (float*)&subProfile;
				for (int i = 0; i < sizeof(b3Profile) / sizeof(float); i++) total[i] += sample[i];
				for (int i = 0; i < m_JointCount; i++)
				{
					b3JointId id = jointIds[i];
					if (id.IsNull) continue;
					ref JointSettings settings = ref jointSettings[i];
					// Native getters return reaction force = impulse / h. Sum impulses,
					// not peak substep forces, so solver substeps cannot change break strength.
					if (settings.NeedsLinearEvents)
					{
						b3Vec3 force = AnvilNative.b3Joint_GetConstraintForce(id);
						linearImpulses[i] += new float3(force.x, force.y, force.z) * h;
					}
					if (settings.NeedsAngularEvents)
					{
						b3Vec3 torque = AnvilNative.b3Joint_GetConstraintTorque(id);
						angularImpulses[i] += new float3(torque.x, torque.y, torque.z) * h;
					}
				}
				if (m_HasSubStepContacts) CollectSubStepContacts();
			}
			return profile;
		}

		void CollectSubStepContacts()
		{
			EnsureContactCapacity(math.max(64, AnvilNative.b3World_GetCounters(m_WorldId).contactCount));
			b3AnvilContact* contacts = (b3AnvilContact*)m_ScratchContacts.GetUnsafePtr();
			int count = AnvilNative.anvil_CollectAllContacts(m_WorldId, contacts, m_ScratchContacts.Length);
			for (int i = 0; i < count; i++)
			{
				b3AnvilContact c = contacts[i];
				ulong key = ((ulong)(uint)c.bodyA << 32) | (uint)c.bodyB;
				bool exists = m_FrameContacts.TryGetValue(key, out FrameContact value);
				if (!exists || c.normalImpulse > value.PeakImpulse)
				{
					value.Event.EntityA = new Entity { Index = c.bodyA };
					value.Event.EntityB = new Entity { Index = c.bodyB };
					value.Event.Position = new float3(c.point.x, c.point.y, c.point.z);
					value.Event.Normal = new float3(c.normal.x, c.normal.y, c.normal.z);
					value.PeakImpulse = c.normalImpulse;
				}
				value.Event.Impulse += c.normalImpulse;
				m_FrameContacts[key] = value;
			}
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
			// Read sleeping velocities too: a sleep transition can zero velocity without changing
			// the last reported pose. Omitting it leaves stale ECS momentum and damping state.
			AnvilNative.anvil_ReadBatchFast(m_WorldId, bodies, numDynamic, transforms, linear, angular);

			// 裸指针批量写回：NativeArray 索引器每次访问都要过一遍 job safety 检查，
			// 2000 个刚体 x 约 7 次访问 = 1.4 万次原子检查，实测就要 1 ms 量级。
			MotionData* motionDatas = (MotionData*)world.MotionDatas.GetUnsafePtr();
			MotionVelocity* motionVelocities = (MotionVelocity*)world.MotionVelocities.GetUnsafePtr();
			bool track = m_LastTransforms.IsCreated && m_LastTransforms.Length >= numDynamic;
			b3WorldTransform* lastTransforms = track ? (b3WorldTransform*)m_LastTransforms.GetUnsafePtr() : null;
			b3Vec3* lastLinear = track ? (b3Vec3*)m_LastLinear.GetUnsafePtr() : null;
			b3Vec3* lastAngular = track ? (b3Vec3*)m_LastAngular.GetUnsafePtr() : null;
			byte* pendingDamping = (byte*)m_PendingDamping.GetUnsafePtr();

			for (int i = 0; i < numDynamic; i++)
			{
				b3WorldTransform t = transforms[i];
				if (track && SameTransform(t, lastTransforms[i]) && IsZero(linear[i]) && IsZero(angular[i])
					&& IsZero(lastLinear[i]) && IsZero(lastAngular[i])
					&& !math.any(motionVelocities[i].LinearVelocity) && !math.any(motionVelocities[i].AngularVelocity))
				{
					pendingDamping[i] = 0;
					continue;
				}
				RigidTransform worldFromBody = new RigidTransform(
					new quaternion(t.q.x, t.q.y, t.q.z, t.q.w),
					new float3(t.p.x, t.p.y, t.p.z));

				MotionData md = motionDatas[i];
				// box3d 的 body transform 就是 world-from-body
				md.WorldFromMotion = math.mul(worldFromBody, md.BodyFromMotion);
				// BodyFromMotion carries the scaled COM and principal-inertia frame.
				motionDatas[i] = md;

				MotionVelocity mv = motionVelocities[i];
				float3 rawLinear = new float3(linear[i].x, linear[i].y, linear[i].z);
				float3 rawAngular = math.rotate(math.inverse(md.WorldFromMotion.rot), new float3(angular[i].x, angular[i].y, angular[i].z));
				mv.LinearVelocity = rawLinear * math.clamp(1f - md.LinearDamping * m_LastTimeStep, 0f, 1f);
				mv.AngularVelocity = rawAngular * math.clamp(1f - md.AngularDamping * m_LastTimeStep, 0f, 1f);
				motionVelocities[i] = mv;
				// Flush the post-integration damping before the next native step, without
				// confusing that correction with externally applied small impulses.
				pendingDamping[i] = (byte)(math.any(mv.LinearVelocity != rawLinear) || math.any(mv.AngularVelocity != rawAngular) ? 1 : 0);

				// 记住 box3d 的权威值，供下一帧 NeedsUpload 判断 ECS 是否被外部改写。
				if (track)
				{
					lastTransforms[i] = t;
					lastLinear[i] = ToVec3(mv.LinearVelocity);
					lastAngular[i] = ToVec3(mv.AngularVelocity);
				}
			}
		}

		static bool IsZero(b3Vec3 v) => v.x == 0f && v.y == 0f && v.z == 0f;
		static bool SameTransform(in b3WorldTransform a, in b3WorldTransform b) =>
			a.p.x == b.p.x && a.p.y == b.p.y && a.p.z == b.p.z
			&& a.q.x == b.q.x && a.q.y == b.q.y && a.q.z == b.q.z && a.q.w == b.q.w;

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

				int duplicate = -1;
				for (int k = 0; k < output.Length; k++)
				{
					if (output[k].Entity == entity) { duplicate = k; break; }
				}
				if (duplicate >= 0 && output[duplicate].Fraction <= h.fraction) continue;

				RaycastHit hit = new RaycastHit
				{
					Fraction = h.fraction,
					RigidBodyIndex = BodyIndexFromNativeIndex(h.bodyIndex),
					ColliderKey = ColliderKey.Empty,
					Entity = entity,
					Position = new float3(h.point.x, h.point.y, h.point.z),
					SurfaceNormal = new float3(h.normal.x, h.normal.y, h.normal.z),
				};
				if (duplicate >= 0) output[duplicate] = hit;
				else output.Add(hit);
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
			int index = BodyIndexFromNativeIndex(nativeIndex);
			if (index < 0) return Entity.Null;
			if (index < m_MotionCount) return m_MotionEntities[index];
			int staticIndex = index - m_MotionCount;
			if (staticIndex < m_StaticCount) return m_StaticEntities[staticIndex];
			return Entity.Null;
		}

		// ── 关节 ────────────────────────────────────────────────────────────────

		public int NumJoints => m_JointCount;

		void EnsureJointCapacity(int needed)
		{
			if (m_JointIds.IsCreated && m_JointIds.Length >= needed) return;
			int capacity = math.max(k_MinCapacity, math.ceilpow2(math.max(1, needed)));
			Grow(ref m_JointIds, capacity);
			Grow(ref m_JointSettings, capacity);
			Grow(ref m_JointDescriptors, capacity);
			Grow(ref m_JointLinearImpulse, capacity);
			Grow(ref m_JointAngularImpulse, capacity);
		}

		void DisposeJoints()
		{
			if (m_Created && m_JointIds.IsCreated)
			{
				for (int i = 0; i < m_JointIds.Length; i++)
				{
					if (!m_JointIds[i].IsNull && AnvilNative.b3Joint_IsValid(m_JointIds[i]) != 0)
						AnvilNative.b3DestroyJoint(m_JointIds[i], 0);
				}
			}
			DisposeArray(ref m_JointIds);
			DisposeArray(ref m_JointSettings);
			DisposeArray(ref m_JointDescriptors);
			DisposeArray(ref m_JointLinearImpulse);
			DisposeArray(ref m_JointAngularImpulse);
			m_JointCount = 0;
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
			JointSettings* settings = (JointSettings*)m_JointSettings.GetUnsafePtr();
			Joint* descriptors = (Joint*)m_JointDescriptors.GetUnsafePtr();
			b3BodyId* motionBodies = m_MotionBodies.IsCreated ? (b3BodyId*)m_MotionBodies.GetUnsafePtr() : null;
			b3BodyId* staticBodies = m_StaticBodies.IsCreated ? (b3BodyId*)m_StaticBodies.GetUnsafePtr() : null;
			int motionCapacity = m_MotionBodies.Length, staticCapacity = m_StaticBodies.Length;

			for (int i = 0; i < numJoints; i++)
			{
				Joint joint = joints[i];
				int indexA = joint.BodyPair.BodyIndexA;
				int indexB = joint.BodyPair.BodyIndexB;

				if (indexA < 0 || indexB < 0 || indexA == indexB
					|| (indexA >= numDynamic && indexB >= numDynamic))
				{
					DestroyJointAt(i);
					continue;
				}

				b3BodyId bodyA = BodyIdForIndex(indexA, numDynamic, numStatic, motionBodies, staticBodies, motionCapacity, staticCapacity);
				b3BodyId bodyB = BodyIdForIndex(indexB, numDynamic, numStatic, motionBodies, staticBodies, motionCapacity, staticCapacity);
				if (bodyA.IsNull || bodyB.IsNull
					|| AnvilNative.b3Body_IsValid(bodyA) == 0
					|| AnvilNative.b3Body_IsValid(bodyB) == 0)
				{
					DestroyJointAt(i);
					continue;
				}

				// Joint/ConstraintBlock3 contain only inline value data, not self-relative
				// collider blobs. Exact source bytes include frames, atoms, thresholds,
				// collision, version and entity. Keep all native validity/generation checks.
				if (!jointIds[i].IsNull && AnvilNative.b3Joint_IsValid(jointIds[i]) != 0
					&& SameBody(settings[i].BodyA, bodyA) && SameBody(settings[i].BodyB, bodyB)
					&& UnsafeUtility.MemCmp(joints + i, descriptors + i, sizeof(Joint)) == 0)
					continue;

				JointSettings current = ReadJointSettings(joint, bodyA, bodyB);
				if (!jointIds[i].IsNull
					&& AnvilNative.b3Joint_IsValid(jointIds[i]) != 0
					&& settings[i].Matches(current))
				{
					UnsafeUtility.MemCpy(descriptors + i, joints + i, sizeof(Joint));
					continue;
				}

				if (!jointIds[i].IsNull && AnvilNative.b3Joint_IsValid(jointIds[i]) != 0
					&& settings[i].EnableCollision != current.EnableCollision)
				{
					// Recreating a weld alone does not refilter unmoved shape proxies.
					// The setter queues new pairs on enable and removes old contacts on disable.
					AnvilNative.b3Joint_SetCollideConnected(jointIds[i], (byte)(current.EnableCollision != 0 ? 1 : 0));
				}
				DestroyJointAt(i);

				b3WeldJointDef def = s_WeldJointDef;
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
				// Native scalar/magnitude force thresholds are not Unity's per-component
				// impulse thresholds. Evaluate full-frame impulses in CollectJointEvents instead.
				def.baseDef.forceThreshold = float.MaxValue;
				def.baseDef.torqueThreshold = float.MaxValue;
				def.baseDef.collideConnected = (byte)(joint.EnableCollision != 0 ? 1 : 0);
				// Weld hertz=0 means fallback, not "most rigid". Use authored springs directly;
				// a missing/zero-frequency atom falls back to zero softness (disabled), not 60 Hz.
				def.baseDef.constraintHertz = 0f;
				def.linearHertz = current.HasLinear ? current.Linear.SpringFrequency : 0f;
				def.angularHertz = current.HasAngular ? current.Angular.SpringFrequency : 0f;
				def.linearDampingRatio = current.HasLinear ? current.Linear.SpringDamping : 0f;
				def.angularDampingRatio = current.HasAngular ? current.Angular.SpringDamping : 0f;
				if (current.HasLinear && current.HasAngular
					&& current.Linear.SpringFrequency == Constraint.DefaultSpringFrequency
					&& current.Angular.SpringFrequency == Constraint.DefaultSpringFrequency
					&& current.Linear.SpringDamping == Constraint.DefaultSpringDamping
					&& current.Angular.SpringDamping == Constraint.DefaultSpringDamping)
				{
					// Unity's enormous default constants encode its per-iteration rigid constraint,
					// not a physical 74 kHz spring. Native positive hertz keeps bias enabled even in
					// the relaxation pass, which pumps energy into off-centre rigid assemblies.
					def.baseDef.constraintHertz = s_WeldJointDef.baseDef.constraintHertz;
					def.baseDef.constraintDampingRatio = s_WeldJointDef.baseDef.constraintDampingRatio;
					def.linearHertz = def.angularHertz = 0f;
				}

				b3JointId id = AnvilNative.b3CreateWeldJoint(m_WorldId, ref def);
				if (id.IsNull) throw new InvalidOperationException("Anvil: weld creation failed for " + joint.Entity);
				jointIds[i] = id;
				settings[i] = current;
				UnsafeUtility.MemCpy(descriptors + i, joints + i, sizeof(Joint));
			}

			for (int i = numJoints; i < m_JointIds.Length; i++)
			{
				if (jointIds[i].IsNull) continue;
				DestroyJointAt(i);
			}

			m_JointCount = numJoints;
			return numJoints;
		}

		static JointSettings ReadJointSettings(in Joint joint, b3BodyId bodyA, b3BodyId bodyB)
		{
			var settings = new JointSettings
			{
				BodyA = bodyA, BodyB = bodyB, Entity = joint.Entity, Version = joint.Version,
				EnableCollision = joint.EnableCollision,
				FrameA = new RigidTransform(new quaternion(joint.AFromJoint.Rotation), joint.AFromJoint.Translation),
				FrameB = new RigidTransform(new quaternion(joint.BFromJoint.Rotation), joint.BFromJoint.Translation)
			};
			var atoms = joint.Constraints.GetConstraints();
			for (int i = 0; i < atoms.Length; i++)
			{
				Constraint c = atoms[i];
				if (!math.all(c.ConstrainedAxes) || c.Min != 0f || c.Max != 0f
					|| !math.isfinite(c.SpringFrequency) || c.SpringFrequency < 0f
					|| !math.isfinite(c.SpringDamping) || c.SpringDamping < 0f)
					throw new NotSupportedException("Anvil: only fixed/fully locked joint atoms are supported: " + joint.Entity);
				if (c.Type == ConstraintType.Linear && !settings.HasLinear)
				{
					settings.Linear = c;
					settings.HasLinear = true;
				}
				else if (c.Type == ConstraintType.Angular && !settings.HasAngular)
				{
					settings.Angular = c;
					settings.HasAngular = true;
				}
				else throw new NotSupportedException("Anvil: unsupported joint atom instead of silently welding it: " + joint.Entity);
			}
			// Derive once per sync, including edits made without a joint-version increment.
			if (settings.HasLinear && math.any(math.isfinite(settings.Linear.MaxImpulse))) settings.EventMask |= 1;
			if (settings.HasAngular && math.any(math.isfinite(settings.Angular.MaxImpulse))) settings.EventMask |= 2;
			return settings;
		}

		void DestroyJointAt(int i)
		{
			if (m_JointIds[i].IsNull) return;
			b3JointId id = m_JointIds[i];
			// 句柄可能已被 b3DestroyBody 隐式释放（body 销毁时其关节一并销毁）；
			// 只有仍然有效的句柄才能交给 b3DestroyJoint，否则会 SIGSEGV（二次模拟崩溃）。
			if (AnvilNative.b3Joint_IsValid(id) != 0)
				AnvilNative.b3DestroyJoint(id, 1);
			m_JointIds[i] = default;
			m_JointSettings[i] = default;
			m_JointDescriptors[i] = default;
			m_JointLinearImpulse[i] = float3.zero;
			m_JointAngularImpulse[i] = float3.zero;
		}

		static b3BodyId BodyIdForIndex(int index, int numDynamic, int numStatic,
			b3BodyId* motionBodies, b3BodyId* staticBodies, int motionCapacity, int staticCapacity)
		{
			if (index < 0) return default;
			if (index < numDynamic)
				return motionBodies != null && index < motionCapacity ? motionBodies[index] : default;

			int staticIndex = index - numDynamic;
			if (staticBodies == null || staticIndex < 0 || staticIndex >= numStatic || staticIndex >= staticCapacity)
				return default;
			return staticBodies[staticIndex];
		}

		/// <summary>
		/// Evaluate accumulated full-frame impulses using the authored per-axis event thresholds.
		/// 返回写入 output 的条数。
		/// </summary>
		public unsafe int CollectJointEvents(NativeList<Entity> output)
		{
			output.Clear();
			if (!m_Created || m_JointCount == 0) return 0;
			b3JointId* jointIds = (b3JointId*)m_JointIds.GetUnsafePtr();
			JointSettings* jointSettings = (JointSettings*)m_JointSettings.GetUnsafePtr();
			float3* linearImpulses = (float3*)m_JointLinearImpulse.GetUnsafePtr();
			float3* angularImpulses = (float3*)m_JointAngularImpulse.GetUnsafePtr();
			for (int i = 0; i < m_JointCount; i++)
			{
				if (jointIds[i].IsNull) continue;
				ref JointSettings settings = ref jointSettings[i];
				bool exceeded = settings.NeedsLinearEvents && math.any(math.abs(linearImpulses[i]) > settings.Linear.MaxImpulse);
				if (!exceeded && settings.NeedsAngularEvents)
				{
					float3 angularImpulse = angularImpulses[i];
					// A zero vector is unchanged by a valid joint frame; do not look up and
					// rotate every resting/torque-free joint. Negative/zero thresholds still compare.
					if (math.any(angularImpulse != float3.zero))
					{
						quaternion bodyRotation = quaternion.identity;
						if (m_NativeBodyIndices.TryGetValue(settings.BodyB.index1, out int index))
						{
							if (index < m_MotionCount)
							{
								b3Quat q = m_LastTransforms[index].q;
								bodyRotation = new quaternion(q.x, q.y, q.z, q.w);
							}
							else bodyRotation = m_StaticTransforms[index - m_MotionCount].rot;
						}
						angularImpulse = math.rotate(math.inverse(math.mul(bodyRotation, settings.FrameB.rot)), angularImpulse);
					}
					exceeded = math.any(math.abs(angularImpulse) > settings.Angular.MaxImpulse);
				}
				if (exceeded) output.Add(settings.Entity);
			}
			return output.Length;
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
			if (m_HasSubStepContacts)
			{
				foreach (FrameContact value in m_FrameContacts.Values)
				{
					if (output.Length >= capacity) break;
					output.Add(value.Event);
				}
				return output.Length;
			}

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
			return m_NativeBodyIndices.TryGetValue(nativeIndex, out int index) ? index : -1;
		}

				public int CollectContacts(NativeList<AnvilContactPair> output, int capacity)
		{
			output.Clear();
			if (!m_Created || capacity <= 0) return 0;
			EnsureContactCapacity(capacity);

			b3BodyId* bodies = (b3BodyId*)m_MotionBodies.GetUnsafePtr();
			b3AnvilContact* contacts = (b3AnvilContact*)m_ScratchContacts.GetUnsafePtr();
			int written = AnvilNative.anvil_CollectContacts(bodies, m_MotionCount, contacts, capacity);

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
