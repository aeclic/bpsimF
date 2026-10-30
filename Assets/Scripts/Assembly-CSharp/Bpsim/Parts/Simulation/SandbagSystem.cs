using Bpsim.Physics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using Unity.Transforms;

namespace Bpsim.Parts.Simulation
{
	/// <summary>
	/// 沙袋的悬挂弹簧 + 放手（BPSS 901-17.0 <c>Sandbag.cs</c> 的 ECS 重构）。
	/// </summary>
	/// <remarks>
	/// <para>
	/// 原版每个沙袋在 <c>Initialize()</c> 里给自己挂一个 Unity <c>SpringJoint</c>：
	/// 挂点 <c>up * 0.5</c>（沙袋局部），连接到上级部件原点，<c>minDistance = 0</c>、
	/// <c>maxDistance</c> 随气球数（0.5 / 0.55 / 0.65）、<c>spring = 100</c>、<c>damper = 10</c>。
	/// 它等价于「两点距离超过 <c>maxDistance</c> 才把沙袋拉回去」的单向弹簧约束。
	/// </para>
	/// <para>
	/// Unity Physics 没有 SpringJoint，用 Constraint 表达要改物理管线；
	/// 这里按 ECS 的做法把状态留在 <see cref="Sandbag" /> 组件里，每物理帧算成冲量施加：
	/// <c>F = spring·(d − maxDistance) + damper·v_rel</c>（只取正值，即只拉不推），
	/// 用 <c>PhysicsVelocity</c> 直接加冲量（与 <c>WheelSystem.ApplyImpulseAtPoint</c> 同一套换算）。
	/// 因为 <see cref="PartSimulationSystemGroup" /> 跑在 <c>AfterPhysicsSystemGroup</c>，
	/// 写入的冲量在下一物理步生效，和原版 <c>FixedUpdate</c> 里 <c>AddForce</c> 的时序一致。
	/// </para>
	/// <para>
	/// 首帧绑定（<c>Bind</c>）复现 <c>Initialize()</c> 的三件事：找悬挂点、切可视化、没挂上就放手。
	/// </para>
	/// </remarks>
	[UpdateInGroup(typeof(PartSimulationSystemGroup))]
	internal partial class SandbagSystem : SystemBase
	{
		private EntityQuery m_sandbagQuery;

		private ComponentLookup<PartTransform> m_transformLookup;

		private ComponentLookup<PartTypeValue> m_typeLookup;

		private ComponentLookup<LocalToWorld> m_worldLookup;

		private ComponentLookup<Parent> m_parentLookup;

		private ComponentLookup<PartRenderInfo> m_renderLookup;

		private ComponentLookup<PhysicsVelocity> m_velocityLookup;

		private ComponentLookup<PhysicsMass> m_massLookup;

		protected override void OnCreate()
		{
			base.OnCreate();
			EntityQueryBuilder builder = new EntityQueryBuilder(Allocator.Temp);
			builder = builder.WithAllRW<Sandbag>();
			builder = builder.WithAll<PartTag>();
			m_sandbagQuery = builder.Build(ref base.CheckedStateRef);
			m_transformLookup = base.GetComponentLookup<PartTransform>(isReadOnly: true);
			m_typeLookup = base.GetComponentLookup<PartTypeValue>(isReadOnly: true);
			m_worldLookup = base.GetComponentLookup<LocalToWorld>(isReadOnly: true);
			m_parentLookup = base.GetComponentLookup<Parent>(isReadOnly: true);
			m_renderLookup = base.GetComponentLookup<PartRenderInfo>(isReadOnly: true);
			m_velocityLookup = base.GetComponentLookup<PhysicsVelocity>(isReadOnly: false);
			m_massLookup = base.GetComponentLookup<PhysicsMass>(isReadOnly: true);
		}

		protected override void OnUpdate()
		{
			PartManager manager = PartManager.Instance;
			if (manager == null || !manager.IsSimulating || manager.PartSimulator == null)
			{
				return;
			}
			if (m_sandbagQuery.IsEmptyIgnoreFilter)
			{
				return;
			}
			float deltaTime = World.Time.DeltaTime;
			if (deltaTime <= 0f)
			{
				return;
			}
			m_transformLookup.Update(ref base.CheckedStateRef);
			m_typeLookup.Update(ref base.CheckedStateRef);
			m_worldLookup.Update(ref base.CheckedStateRef);
			m_parentLookup.Update(ref base.CheckedStateRef);
			m_renderLookup.Update(ref base.CheckedStateRef);
			m_velocityLookup.Update(ref base.CheckedStateRef);
			m_massLookup.Update(ref base.CheckedStateRef);
			Reference<PartSimulatorUnmanaged> simulator = manager.PartSimulator.UnmanagedRef;
			using NativeArray<Entity> sandbags = m_sandbagQuery.ToEntityArray(Allocator.Temp);
			for (int i = 0; i < sandbags.Length; i++)
			{
				Entity entity = sandbags[i];
				Sandbag bag = EntityManager.GetComponentData<Sandbag>(entity);
				if (!bag.Bound)
				{
					// 注意：Bind 里有结构性改动（DisableRendering），所以它排在
					// SetComponentData 之前、并且不碰任何 chunk 迭代。
					Bind(entity, ref bag, simulator);
				}
				if (!bag.Dropped)
				{
					ApplyTether(entity, ref bag, deltaTime);
				}
				EntityManager.SetComponentData(entity, bag);
			}
		}

		/// <summary>
		/// 复现 BPSS <c>Sandbag.Initialize()</c>：悬挂点搜索 + 可视化切换 + 「找不到就当成已放手」。
		/// </summary>
		private void Bind(Entity entity, ref Sandbag bag, Reference<PartSimulatorUnmanaged> simulator)
		{
			bag.Bound = true;
			bag.Anchor = Entity.Null;
			SwapVisualization(entity);
			if (!m_transformLookup.HasComponent(entity))
			{
				bag.Dropped = true;
				return;
			}
			PartTransform transform = m_transformLookup[entity];
			byte layer = PartBuildLayerUtility.GetLayer(EntityManager, entity);
			// BPSS：RotatableSandbag=false（默认）时 m_direction 恒为 up，只有开启旋转沙袋
			// 才按 m_gridRotation 把方向转成 −x / −y / +x。
			int dx = 0;
			int dy = 1;
			if (Sandbag.DefaultRotatable)
			{
				int rotation = ((transform.Rotation % 4) + 4) % 4;
				if (rotation == 1)
				{
					dx = -1;
					dy = 0;
				}
				else if (rotation == 2)
				{
					dx = 0;
					dy = -1;
				}
				else if (rotation == 3)
				{
					dx = 1;
					dy = 0;
				}
			}
			int distance = Sandbag.DefaultConnectionDistance;
			for (int i = 1; i <= distance && bag.Anchor == Entity.Null; i++)
			{
				if (!simulator.Value.PartMap.TryGet(transform.X + i * dx, transform.Y + i * dy, layer, out PartGridInfo info))
				{
					continue;
				}
				Entity candidate = (info.HasPart ? info.Part : info.PartContainer);
				if (candidate == Entity.Null || candidate == entity)
				{
					continue;
				}
				if (!IsChassisAnchor(candidate))
				{
					continue;
				}
				bag.Anchor = candidate;
			}
			if (bag.Anchor == Entity.Null)
			{
				// BPSS：没有悬挂点 → m_dropped = true，并切到 DroppedSandbag 层。
				// bpsim 的碰撞过滤走 CollisionFilter，不做层切换，这里只记状态。
				bag.Dropped = true;
				return;
			}
			// BPSS Sandbag.Initialize()（挂上之后的那一段）：
			//   position = connectedPart.position - Vector3.up * 0.5f;
			//   ...
			//   transform.position = position + vector;
			// 也就是沙袋被瞬移到上级件下方半格再叠一个按气球数选的偏移。
			// 这里只改 LocalTransform（视觉/物理位置），不动 PartTransform —— 后者是网格身份，
			// 存档、焊接、蓝图都按它索引，挪了会破坏部件与格子的对应关系。
			if (m_worldLookup.HasComponent(bag.Anchor) && EntityManager.HasComponent<LocalTransform>(entity))
			{
				float3 anchorPosition = m_worldLookup[bag.Anchor].Position;
				float3 start = anchorPosition + new float3(0f, -Sandbag.AnchorOffset, 0f) + bag.HangOffset;
				LocalTransform local = EntityManager.GetComponentData<LocalTransform>(entity);
				local.Position = start;
				EntityManager.SetComponentData(entity, local);
			}
			// BPSS 给沙袋的刚体参数：drag = 1f、angularDrag = 10f。
			// bpsim 的统一装配用的是 0.2 / 0（PartSimulator.cs:120），沙袋要单独覆盖。
			if (EntityManager.HasComponent<PhysicsDamping>(entity))
			{
				EntityManager.SetComponentData(entity, new PhysicsDamping
				{
					Linear = 1f,
					Angular = 10f
				});
			}
		}

		/// <summary>
		/// BPSS <c>Initialize()</c> 的可视化切换：关掉 <c>*Visualization</c> 网格替身、
		/// 打开真正装着 N 个气球袋的 <c>SandbagVisualization</c>。
		/// </summary>
		/// <remarks>
		/// bpsim 的 prefab 烘焙不看 <c>activeSelf</c>，两支都会渲染，所以这里按
		/// <see cref="SandbagVisual" /> 标记整棵子树增删 <c>DisableRendering</c>。
		/// </remarks>
		private void SwapVisualization(Entity entity)
		{
			if (!EntityManager.HasComponent<LinkedEntityGroup>(entity))
			{
				return;
			}
			DynamicBuffer<LinkedEntityGroup> group = EntityManager.GetBuffer<LinkedEntityGroup>(entity, isReadOnly: true);
			// 先把「每个 SandbagVisual 节点」挑出来，再按祖先链判断归属：
			// 这样 SandbagVisualization (1..N) 这些没有标记的孙节点也会跟着父节点的显隐走。
			using NativeList<Entity> actualNodes = new NativeList<Entity>(2, Allocator.Temp);
			using NativeList<Entity> gridNodes = new NativeList<Entity>(2, Allocator.Temp);
			for (int i = 0; i < group.Length; i++)
			{
				Entity child = group[i].Value;
				if (!EntityManager.HasComponent<SandbagVisual>(child))
				{
					continue;
				}
				if (EntityManager.GetComponentData<SandbagVisual>(child).Actual)
				{
					actualNodes.Add(in child);
				}
				else
				{
					gridNodes.Add(in child);
				}
			}
			if (actualNodes.Length == 0 && gridNodes.Length == 0)
			{
				return;
			}
			using NativeList<Entity> show = new NativeList<Entity>(group.Length, Allocator.Temp);
			using NativeList<Entity> hide = new NativeList<Entity>(group.Length, Allocator.Temp);
			for (int i = 0; i < group.Length; i++)
			{
				Entity child = group[i].Value;
				if (!m_renderLookup.HasComponent(child))
				{
					continue;
				}
				bool underActual = IsUnderAny(child, actualNodes);
				bool underGrid = IsUnderAny(child, gridNodes);
				if (underActual && !underGrid)
				{
					show.Add(in child);
				}
				else if (underGrid && !underActual)
				{
					hide.Add(in child);
				}
			}
			for (int i = 0; i < show.Length; i++)
			{
				if (EntityManager.HasComponent<DisableRendering>(show[i]))
				{
					EntityManager.RemoveComponent<DisableRendering>(show[i]);
				}
			}
			for (int i = 0; i < hide.Length; i++)
			{
				if (!EntityManager.HasComponent<DisableRendering>(hide[i]))
				{
					EntityManager.AddComponent<DisableRendering>(hide[i]);
				}
			}
		}

		/// <summary>沿 <see cref="Parent" /> 链向上找，看 <paramref name="child" /> 是否在任一节点之下。</summary>
		private bool IsUnderAny(Entity child, NativeList<Entity> roots)
		{
			if (roots.Length == 0)
			{
				return false;
			}
			Entity current = child;
			// 深度上限只是防环保险：部件层级最多 4-5 层。
			for (int depth = 0; depth < 16; depth++)
			{
				for (int i = 0; i < roots.Length; i++)
				{
					if (roots[i] == current)
					{
						return true;
					}
				}
				if (!m_parentLookup.HasComponent(current))
				{
					return false;
				}
				Entity parent = m_parentLookup[current].Value;
				if (parent == Entity.Null || parent == current)
				{
					return false;
				}
				current = parent;
			}
			return false;
		}

		/// <summary>
		/// BPSS 的 <c>IsPartOfChassis()</c> 只对框架类返回 true，沙袋额外接受 <c>Pig</c>。
		/// bpsim 里对应木框 / 铁框 / 猪。
		/// </summary>
		private bool IsChassisAnchor(Entity entity)
		{
			if (!m_typeLookup.HasComponent(entity))
			{
				return false;
			}
			PartType partType = m_typeLookup[entity].Type;
			return partType == PartType.WoodenFrame || partType == PartType.MetalFrame || partType == PartType.Pig;
		}

		/// <summary>
		/// 每物理帧的弹簧冲量：只有「沙袋挂点」离「上级件挂点」超过 <c>maxDistance</c> 时才拉回。
		/// </summary>
		private void ApplyTether(Entity entity, ref Sandbag bag, float deltaTime)
		{
			Entity anchor = bag.Anchor;
			if (anchor == Entity.Null || !m_worldLookup.HasComponent(anchor) || !m_worldLookup.HasComponent(entity))
			{
				return;
			}
			if (!m_velocityLookup.HasComponent(entity))
			{
				return;
			}
			float3 sandbagPosition = m_worldLookup[entity].Position;
			float3 anchorPosition = m_worldLookup[anchor].Position;
			// 沙袋上的挂点是本地 up*0.5（BPSS springJoint.anchor）；上级件那侧 BPSS 没设
			// connectedAnchor，走 Unity 的默认值 = 上级件本地原点。
			float3 point = sandbagPosition + new float3(0f, Sandbag.AnchorOffset, 0f);
			float3 target = anchorPosition;
			float3 delta = point - target;
			float distance = math.length(delta);
			if (distance <= bag.MaxDistance || distance <= 1E-05f)
			{
				return;
			}
			float3 normal = delta / distance;
			float3 velocity = m_velocityLookup[entity].Linear;
			float3 anchorVelocity = (m_velocityLookup.HasComponent(anchor) ? m_velocityLookup[anchor].Linear : float3.zero);
			// 相对速度沿「远离」方向为正 → 阻尼项与弹簧项同向，都是把沙袋拉回来。
			float relative = math.dot(velocity - anchorVelocity, normal);
			float force = bag.Spring * (distance - bag.MaxDistance) + bag.Damper * relative;
			if (force <= 0f)
			{
				return;
			}
			float3 impulse = -normal * (force * deltaTime);
			ApplyImpulseAtPoint(entity, impulse, point);
			ApplyImpulseAtPoint(anchor, -impulse, anchorPosition);
		}

		/// <summary>
		/// 与 <c>WheelSystem.ApplyImpulseAtPoint</c> 相同的换算：
		/// 线性冲量 + 相对质心的角冲量（等价 BPSS 的 <c>AddForceAtPosition</c>）。
		/// </summary>
		private void ApplyImpulseAtPoint(Entity entity, float3 impulse, float3 point)
		{
			if (entity == Entity.Null || !m_velocityLookup.HasComponent(entity) || !m_massLookup.HasComponent(entity))
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
			if (m_worldLookup.HasComponent(entity))
			{
				LocalToWorld world = m_worldLookup[entity];
				float3 centerOfMass = math.transform(world.Value, mass.CenterOfMass);
				float3 angular = math.cross(point - centerOfMass, impulse);
				velocity.Angular += math.rotate(math.inverse(world.Rotation), angular) * mass.InverseInertia;
			}
			m_velocityLookup[entity] = velocity;
		}
	}
}
