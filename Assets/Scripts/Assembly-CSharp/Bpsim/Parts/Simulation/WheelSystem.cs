using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace Bpsim.Parts.Simulation
{
	/// <summary>
	/// 轮子族（CartWheel / NormalWheel / SmallWheel / MotorWheel / StickyWheel / OffRoadWheel）的
	/// 物理与视觉驱动，按 BPSS 901-17.0 的 <c>CartWheel.cs</c> / <c>MotorWheel.cs</c> /
	/// <c>StickyWheel.cs</c> / <c>OffRoadWheel.cs</c> 逐条重构为 ECS 系统。
	/// </summary>
	/// <remarks>
	/// <para>
	/// BPSS 侧每个轮子是一个 MonoBehaviour，自己 <c>Physics.Raycast</c> 找地面、自己
	/// <c>AddForceAtPosition</c>、自己在 <c>Update</c> 里转 <c>WheelPivot</c>。这里把同一份状态
	/// 收进 <see cref="Wheel" /> 组件，由本系统在 <see cref="PartSimulationSystemGroup" />
	/// 里统一处理：射线用 <see cref="CollisionWorld.CastRay" />，施力用冲量
	/// （<c>ForceMode.Force</c> 的等价值 = 力 × dt，作用在命中点上）。
	/// </para>
	/// <para>
	/// 与 BPSS 的两处有意偏离，均记录在 <c>BPSIM_WHEEL_GEARBOX_PORT.md</c>：
	/// 1) BPSS 的推力方向取 <c>Vector3.Cross(hit.normal, Vector3.forward)</c>（世界 +Z），
	///    在网格对齐的建造里等价于「世界 +X」。bpsim 支持自由旋转，这里改成
	///    「轮子本地 +X 投影到接触面」，未旋转时与 BPSS 完全一致，旋转后方向仍然正确。
	/// 2) BPSS 的 <c>m_lastContactDirection</c> 来自 <c>OnCollisionStay</c> 的接触点；ECS 里没有
	///    等价的逐接触点回调，这里改成「上一帧命中的接触方向」，首帧回退到轮子本地 -Y。
	/// </para>
	/// </remarks>
	[UpdateInGroup(typeof(PartSimulationSystemGroup))]
	internal partial class WheelSystem : SystemBase
	{
		/// <summary>BPSS <c>MotorWheel.m_maximumSpeed = 15f * enginePowerFactor</c>。</summary>
		private const float SpeedPerPower = 15f;

		/// <summary>BPSS 未接地分支的滚动衰减 <c>m_spinSpeed *= 0.98f</c>。</summary>
		private const float AirborneSpinDecay = 0.98f;

		/// <summary>射线命中自身碰撞体时，最多把起点往前推几次。</summary>
		private const int MaxSelfSkip = 8;

		private EntityQuery m_wheelQuery;

		private EntityQuery m_gearboxQuery;

		private ComponentLookup<LocalTransform> m_localTransformLookup;

		private ComponentLookup<LocalToWorld> m_localToWorldLookup;

		private ComponentLookup<WheelPivot> m_pivotLookup;

		private ComponentLookup<PhysicsCollider> m_colliderLookup;

		private ComponentLookup<PartConnectedComponent> m_connectedLookup;

		private ComponentLookup<PhysicsVelocity> m_velocityLookup;

		private ComponentLookup<PhysicsMass> m_massLookup;

		private BufferLookup<LinkedEntityGroup> m_groupLookup;

		protected override void OnCreate()
		{
			EntityQueryBuilder builder = new EntityQueryBuilder(Allocator.Temp);
			builder = builder.WithAllRW<Wheel>();
			builder = builder.WithAll<PartTag>();
			builder = builder.WithAll<PartConnectedComponent>();
			builder = builder.WithAll<LinkedEntityGroup>();
			m_wheelQuery = builder.Build(ref base.CheckedStateRef);
			builder = new EntityQueryBuilder(Allocator.Temp);
			builder = builder.WithAllRW<Gearbox>();
			builder = builder.WithAll<PartConnectedComponent>();
			m_gearboxQuery = builder.Build(ref base.CheckedStateRef);
			m_localTransformLookup = base.CheckedStateRef.GetComponentLookup<LocalTransform>();
			m_localToWorldLookup = base.CheckedStateRef.GetComponentLookup<LocalToWorld>(isReadOnly: true);
			m_pivotLookup = base.CheckedStateRef.GetComponentLookup<WheelPivot>(isReadOnly: true);
			m_colliderLookup = base.CheckedStateRef.GetComponentLookup<PhysicsCollider>(isReadOnly: true);
			m_connectedLookup = base.CheckedStateRef.GetComponentLookup<PartConnectedComponent>(isReadOnly: true);
			m_velocityLookup = base.CheckedStateRef.GetComponentLookup<PhysicsVelocity>();
			m_massLookup = base.CheckedStateRef.GetComponentLookup<PhysicsMass>(isReadOnly: true);
			m_groupLookup = base.CheckedStateRef.GetBufferLookup<LinkedEntityGroup>(isReadOnly: true);
		}

		protected override void OnUpdate()
		{
			PartManager manager = PartManager.Instance;
			if (manager == null || !manager.IsSimulating || manager.PartSimulator == null)
			{
				return;
			}
			if (!SystemAPI.TryGetSingleton(out PhysicsWorldSingleton physicsWorld))
			{
				// 物理世界还没建起来（例如场景里一个带碰撞体的部件都没有）。
				return;
			}
			float deltaTime = World.Time.DeltaTime;
			if (deltaTime <= 0f)
			{
				return;
			}
			Reference<PartSimulatorUnmanaged> simulator = manager.PartSimulator.UnmanagedRef;
			m_localTransformLookup.Update(ref base.CheckedStateRef);
			m_localToWorldLookup.Update(ref base.CheckedStateRef);
			m_pivotLookup.Update(ref base.CheckedStateRef);
			m_colliderLookup.Update(ref base.CheckedStateRef);
			m_connectedLookup.Update(ref base.CheckedStateRef);
			m_velocityLookup.Update(ref base.CheckedStateRef);
			m_massLookup.Update(ref base.CheckedStateRef);
			m_groupLookup.Update(ref base.CheckedStateRef);
			CollisionWorld collisionWorld = physicsWorld.CollisionWorld;
			using NativeParallelHashSet<int> reversingComponents = CollectReversingComponents();
			using NativeArray<Entity> wheels = m_wheelQuery.ToEntityArray(Allocator.Temp);
			for (int i = 0; i < wheels.Length; i++)
			{
				Tick(wheels[i], deltaTime, collisionWorld, simulator, reversingComponents);
			}
		}

		/// <summary>
		/// 收集「已开启齿轮箱」的焊接组件索引。BPSS 的轮子每帧问
		/// <c>contraption.ConnectedToGearbox(this) &amp;&amp; GetGearbox(this).IsEnabled()</c>，
		/// 等价于「同一焊接组件里存在一个 Activated 的 Gearbox」。
		/// </summary>
		private NativeParallelHashSet<int> CollectReversingComponents()
		{
			NativeParallelHashSet<int> result = new NativeParallelHashSet<int>(8, Allocator.Temp);
			using NativeArray<Entity> gearboxes = m_gearboxQuery.ToEntityArray(Allocator.Temp);
			for (int i = 0; i < gearboxes.Length; i++)
			{
				Entity entity = gearboxes[i];
				if (!EntityManager.GetComponentData<Gearbox>(entity).Activated || !m_connectedLookup.HasComponent(entity))
				{
					continue;
				}
				int index = m_connectedLookup[entity].Index;
				if (index >= 0)
				{
					result.Add(index);
				}
			}
			return result;
		}

		private void Tick(Entity entity, float deltaTime, CollisionWorld collisionWorld, Reference<PartSimulatorUnmanaged> simulator, NativeParallelHashSet<int> reversingComponents)
		{
			Wheel wheel = EntityManager.GetComponentData<Wheel>(entity);
			if (!wheel.Bound)
			{
				Bind(ref wheel, entity);
			}
			if (wheel.Radius <= 0f)
			{
				wheel.Radius = 0.45f;
			}
			wheel.Circumference = math.PI * 2f * wheel.Radius;
			int component = (m_connectedLookup.HasComponent(entity) ? m_connectedLookup[entity].Index : (-1));
			bool reversing = component >= 0 && reversingComponents.Contains(component);
			wheel.PowerFactor = (wheel.IsPowered ? GetPowerFactor(component, simulator) : 1f);
			wheel.MaximumForce = wheel.Force * wheel.PowerFactor;
			wheel.MaximumSpeed = SpeedPerPower * wheel.PowerFactor;
			if (!m_localToWorldLookup.HasComponent(entity))
			{
				EntityManager.SetComponentData(entity, wheel);
				return;
			}
			LocalToWorld world = m_localToWorldLookup[entity];
			float3 position = world.Position;
			quaternion rotation = world.Rotation;
			float3 pivotPosition = ((wheel.Pivot != Entity.Null && m_localToWorldLookup.HasComponent(wheel.Pivot)) ? m_localToWorldLookup[wheel.Pivot].Position : position);
			UpdateThrottle(ref wheel, deltaTime, reversing);
			RaycastGround(entity, ref wheel, rotation, pivotPosition, collisionWorld, out RaycastHit hit);
			if (wheel.HasContact)
			{
				ApplyContactForce(entity, ref wheel, deltaTime, position, pivotPosition, hit);
			}
			else if (wheel.IsPowered && wheel.Enabled)
			{
				wheel.SpinSpeed = (reversing ? (0f - wheel.MaximumSpeed) : wheel.MaximumSpeed);
			}
			else
			{
				wheel.SpinSpeed *= AirborneSpinDecay;
			}
			UpdateVisual(ref wheel, rotation, deltaTime);
			EntityManager.SetComponentData(entity, wheel);
		}

		/// <summary>
		/// 首帧绑定：在 <see cref="LinkedEntityGroup" /> 里找打了 <see cref="WheelPivot" /> 标记的子实体。
		/// 不能靠实体名字 —— 发行版里 <c>EntityManager.GetName</c> 恒为空串。
		/// </summary>
		private void Bind(ref Wheel wheel, Entity entity)
		{
			wheel.Bound = true;
			wheel.Pivot = Entity.Null;
			wheel.FakePivot = Entity.Null;
			if (!m_groupLookup.HasBuffer(entity))
			{
				return;
			}
			DynamicBuffer<LinkedEntityGroup> group = m_groupLookup[entity];
			for (int i = 1; i < group.Length; i++)
			{
				Entity child = group[i].Value;
				if (!m_pivotLookup.HasComponent(child))
				{
					continue;
				}
				if (m_pivotLookup[child].Fake)
				{
					wheel.FakePivot = child;
				}
				else
				{
					wheel.Pivot = child;
				}
			}
			// BPSS 在 Start() 里从「有 WheelPivot 就用它、否则用 FakeWheelPivot」的
			// SphereCollider 读半径（CartWheel 0.36 / NormalWheel 0.33 / SmallWheel 0.18），
			// MotorWheel 与 StickyWheel 的球在根物体上（0.45，即 Create() 的默认值）。
			float radius = PivotRadius(wheel.Pivot);
			if (radius <= 0f)
			{
				radius = PivotRadius(wheel.FakePivot);
			}
			if (radius <= 0f)
			{
				// MotorWheel / StickyWheel / OffRoadWheel 的球在根物体上
				// （BPSS 也是 GetComponent<SphereCollider>().radius，OffRoadWheel 为 0.9）。
				radius = PivotRadius(entity);
			}
			if (radius > 0f)
			{
				wheel.Radius = radius;
				wheel.Circumference = math.PI * 2f * radius;
			}
		}

		/// <summary>读 <c>WheelPivot</c> 子实体自带球碰撞体的半径（没有则返回 0）。</summary>
		private unsafe float PivotRadius(Entity pivot)
		{
			if (pivot == Entity.Null || !m_colliderLookup.HasComponent(pivot))
			{
				return 0f;
			}
			BlobAssetReference<Collider> blob = m_colliderLookup[pivot].Value;
			if (!blob.IsCreated)
			{
				return 0f;
			}
			Collider* collider = (Collider*)blob.GetUnsafePtr();
			if (collider == null || collider->Type != ColliderType.Sphere)
			{
				return 0f;
			}
			return ((SphereCollider*)collider)->Radius;
		}

		/// <summary>BPSS <c>FixedUpdate</c> 开头的推力斜坡（含齿轮箱倒车）。</summary>
		private static void UpdateThrottle(ref Wheel wheel, float deltaTime, bool reversing)
		{
			if (!wheel.Enabled || !wheel.IsPowered)
			{
				wheel.ThrottleTimer = 0f;
				wheel.Throttle = 0f;
				return;
			}
			wheel.ThrottleTimer = math.min(wheel.ThrottleTimer + deltaTime, 1f);
			wheel.Throttle = math.pow(wheel.ThrottleTimer, 0.4f);
			if (reversing)
			{
				wheel.Throttle = 0f - wheel.Throttle;
			}
		}

		/// <summary>
		/// 复现 BPSS 的 <c>Physics.Raycast(pivot.position, m_lastContactDirection, m_radius + 0.1f)</c>。
		/// </summary>
		/// <remarks>
		/// BPSS 只排除 <c>SupportCollider</c> 一个碰撞体；这里要排除整台轮子（根 + 全部子物体），
		/// 因为 <see cref="PartEntityFactory.BuildCompoundCollider" /> 把子碰撞体合并进根实体、
		/// 且 <c>CompoundFromChild</c> 恒为单位阵，射线起点（<c>WheelPivot</c>）落在自己的
		/// 球形碰撞体内部，单次 <c>CastRay</c> 拿到的第一个命中很可能就是自己。
		/// 用 <see cref="AllHitsCollector{T}" /> 一次取回整条射线上的全部命中，
		/// 再取「第一个不属于本部件」的最近命中，避免「推进起点重投」在半径内反复命中自己。
		/// </remarks>
		private void RaycastGround(Entity entity, ref Wheel wheel, quaternion rotation, float3 pivotPosition, CollisionWorld collisionWorld, out RaycastHit hit)
		{
			hit = default(RaycastHit);
			float3 direction = ((math.lengthsq(wheel.LastContactDirection) > 1E-08f) ? math.normalize(wheel.LastContactDirection) : math.mul(rotation, new float3(0f, -1f, 0f)));
			float distance = wheel.Radius + 0.1f;
			wheel.HasContact = false;
			wheel.Grounded = false;
			RaycastInput input = new RaycastInput
			{
				Start = pivotPosition,
				End = pivotPosition + direction * distance,
				// 巨构模式下整车是唯一一个「自己」的碰撞体，它被打了独占类别位
				// （MegaStructure.VehicleCategory），这里直接把它整棵复合体剪掉，
				// 射线就只剩下真正的地面/外部物体，不必再收集一串自命中再逐个过滤。
				Filter = MegaStructure.Active ? new CollisionFilter
				{
					BelongsTo = uint.MaxValue,
					CollidesWith = ~MegaStructure.VehicleCategory
				} : CollisionFilter.Default
			};
			NativeList<RaycastHit> hits = new NativeList<RaycastHit>(8, Allocator.Temp);
			try
			{
				AllHitsCollector<RaycastHit> collector = new AllHitsCollector<RaycastHit>(1f, ref hits);
				if (!collisionWorld.CastRay(input, ref collector))
				{
					return;
				}
				bool found = false;
				float bestFraction = float.MaxValue;
				for (int i = 0; i < hits.Length; i++)
				{
					RaycastHit candidate = hits[i];
					if (candidate.Fraction >= bestFraction || BelongsToPart(candidate.Entity, entity))
					{
						continue;
					}
					bestFraction = candidate.Fraction;
					hit = candidate;
					found = true;
				}
				if (!found)
				{
					return;
				}
			}
			finally
			{
				hits.Dispose();
			}
			wheel.HasContact = true;
			wheel.Grounded = true;
			wheel.ContactTicks++;
			wheel.LastContactDirection = math.normalize(hit.Position - pivotPosition);
			wheel.LastForceDirection = math.cross(math.normalize(hit.SurfaceNormal), new float3(0f, 0f, 1f));
		}

		/// <summary>命中的实体是否属于这台轮子自己（根或任一子物体）。</summary>
		private bool BelongsToPart(Entity candidate, Entity root)
		{
			if (candidate == root)
			{
				return true;
			}
			// 巨构优化：整车只有一个刚体，轮子自己的部件已经被并进复合碰撞体，
			// 射线打到的会是整车刚体实体，所以也要当成「自己」跳过。
			if (MegaStructure.IsVehicleBody(candidate))
			{
				return true;
			}
			if (!m_groupLookup.HasBuffer(root))
			{
				return false;
			}
			DynamicBuffer<LinkedEntityGroup> group = m_groupLookup[root];
			for (int i = 1; i < group.Length; i++)
			{
				if (group[i].Value == candidate)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>BPSS 命中分支：沿接触切线施加推力（+ StickyWheel 的阻尼与吸附力）。</summary>
		private void ApplyContactForce(Entity entity, ref Wheel wheel, float deltaTime, float3 position, float3 pivotPosition, RaycastHit hit)
		{
			float3 normal = math.normalize(hit.SurfaceNormal);
			float3 driveAxis = math.mul(m_localToWorldLookup[entity].Rotation, new float3(1f, 0f, 0f));
			float3 tangent = driveAxis - normal * math.dot(driveAxis, normal);
			if (math.lengthsq(tangent) < 1E-06f)
			{
				return;
			}
			tangent = math.normalize(tangent);
			float3 velocity = (m_velocityLookup.HasComponent(entity) ? m_velocityLookup[entity].Linear : float3.zero);
			float speed = math.dot(velocity, tangent);
			// BPSS 命中分支里先 m_spinSpeed = 0f，随后 Update() 用
			// 「if (m_spinSpeed == 0f) m_spinSpeed = SpeedInDirection(m_lastForceDirection)」把真实地面速度读回来，
			// 所以滚动视觉始终反映实际车速。这里做同样的回读。
			wheel.SpinSpeed = 0f;
			float3 forceDirection = wheel.LastForceDirection;
			if (math.lengthsq(forceDirection) > 1E-08f)
			{
				wheel.SpinSpeed = math.dot(velocity, math.normalize(forceDirection));
			}
			if (wheel.Enabled && wheel.IsPowered && wheel.MaximumSpeed > 0f && speed < wheel.MaximumSpeed && speed > 0f - wheel.MaximumSpeed)
			{
				float falloff = math.pow(1f - math.abs(speed) / wheel.MaximumSpeed, 0.5f);
				float force = wheel.Throttle * wheel.MaximumForce * falloff;
				float3 impulse = tangent * (force * deltaTime);
				ApplyImpulseAtPoint(entity, impulse, hit.Position);
				Entity hitEntity = hit.Entity;
				if (hitEntity != Entity.Null && hitEntity != entity)
				{
					ApplyImpulseAtPoint(hitEntity, -impulse, hit.Position);
				}
			}
			if (wheel.Kind == WheelKind.Sticky)
			{
				ApplyStickyForce(entity, ref wheel, deltaTime, normal, pivotPosition, hit);
			}
		}

		/// <summary>BPSS <c>StickyWheel</c> 的两段额外力：贴地阻尼 + 吸附地面的法向力。</summary>
		private void ApplyStickyForce(Entity entity, ref Wheel wheel, float deltaTime, float3 normal, float3 pivotPosition, RaycastHit hit)
		{
			float3 midpoint = (pivotPosition + hit.Position) * 0.5f;
			float3 dragAxis = math.cross(normal, new float3(0f, 0f, -1f));
			float3 velocity = (m_velocityLookup.HasComponent(entity) ? m_velocityLookup[entity].Linear : float3.zero);
			float dragSpeed = math.dot(velocity, dragAxis);
			ApplyImpulseAtPoint(entity, dragAxis * (wheel.Drag * dragSpeed * deltaTime), midpoint);
			if (wheel.Stickiness <= 0f)
			{
				return;
			}
			// BPSS 只在命中静态地面（colliderRigidbody == null）时才加吸附力。
			if (hit.Entity != Entity.Null && m_velocityLookup.HasComponent(hit.Entity))
			{
				return;
			}
			ApplyImpulseAtPoint(entity, normal * (wheel.Stickiness * deltaTime), midpoint);
		}

		/// <summary>
		/// 复现 BPSS <c>rigidbody.AddForceAtPosition(force, point, ForceMode.Force)</c>：
		/// 力 × dt 转成冲量，同时施加线性与角冲量。
		/// </summary>
		private void ApplyImpulseAtPoint(Entity entity, float3 impulse, float3 point)
		{
			if (!m_velocityLookup.HasComponent(entity) || !m_massLookup.HasComponent(entity))
			{
				return;
			}
			PhysicsMass mass = m_massLookup[entity];
			if (mass.InverseMass <= 0f)
			{
				return;
			}
			PhysicsVelocity velocity = m_velocityLookup[entity];
			velocity.Linear += impulse * mass.InverseMass;
			if (m_localToWorldLookup.HasComponent(entity))
			{
				LocalToWorld world = m_localToWorldLookup[entity];
				float3 centerOfMass = math.transform(world.Value, mass.CenterOfMass);
				float3 angular = math.cross(point - centerOfMass, impulse);
				velocity.Angular += math.rotate(math.inverse(world.Rotation), angular) * mass.InverseInertia;
			}
			m_velocityLookup[entity] = velocity;
		}

		/// <summary>BPSS <c>Update()</c>：按滚动速度转 <c>WheelPivot</c>，<c>FakeWheelPivot</c> 只摆动。</summary>
		private void UpdateVisual(ref Wheel wheel, quaternion rotation, float deltaTime)
		{
			if (wheel.Circumference > 0f)
			{
				wheel.Angle += -360f * wheel.SpinSpeed / wheel.Circumference * deltaTime;
			}
			float z = ZDegrees(rotation);
			if (wheel.Pivot != Entity.Null && m_localTransformLookup.HasComponent(wheel.Pivot))
			{
				LocalTransform transform = m_localTransformLookup[wheel.Pivot];
				transform.Rotation = quaternion.RotateZ(math.radians(0f - z + wheel.Angle));
				m_localTransformLookup[wheel.Pivot] = transform;
			}
			if (wheel.FakePivot != Entity.Null && m_localTransformLookup.HasComponent(wheel.FakePivot))
			{
				float swing = math.sin(2f * wheel.Angle * (math.PI / 180f)) * 8f;
				LocalTransform transform = m_localTransformLookup[wheel.FakePivot];
				transform.Rotation = quaternion.RotateZ(math.radians(0f - z + swing));
				m_localTransformLookup[wheel.FakePivot] = transform;
			}
		}

		/// <summary>
		/// 复现 Unity 的 <c>transform.rotation.eulerAngles.z</c>。Unity 的欧拉角顺序是 ZXY，
		/// 旋转矩阵为 <c>Ry * Rx * Rz</c>，其 z 分量 = <c>atan2(m10, m11)</c>。
		/// </summary>
		private static float ZDegrees(quaternion rotation)
		{
			float4 q = rotation.value;
			float m10 = 2f * (q.x * q.y + q.w * q.z);
			float m11 = 1f - 2f * (q.x * q.x + q.z * q.z);
			return math.degrees(math.atan2(m10, m11));
		}

		/// <summary>
		/// 与 <c>FanPropellerSystem.GetPowerFactor</c> 相同的公式（BPSS
		/// <c>Contraption.GetEnginePowerFactor</c>）：焊接组件内 EnginePower / PowerConsumption。
		/// </summary>
		private static float GetPowerFactor(int componentIndex, Reference<PartSimulatorUnmanaged> simulator)
		{
			if (componentIndex < 0 || componentIndex >= simulator.Value.ConnectedComponents.Length)
			{
				return 1f;
			}
			ConnectedComponentInfo info = simulator.Value.ConnectedComponents[componentIndex];
			float factor = 0f;
			if (info.PowerConsumption > 1f)
			{
				factor = math.min(info.EnginePower / info.PowerConsumption, 20f);
			}
			else if (info.EnginePower > 0f)
			{
				factor = 1f;
			}
			return math.pow(factor, (factor > 1f) ? 0.585f : 0.75f);
		}
	}
}
