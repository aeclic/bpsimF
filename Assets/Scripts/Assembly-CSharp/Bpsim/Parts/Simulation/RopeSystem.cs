using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using Bpsim.Parts.Gameplay;

namespace Bpsim.Parts.Simulation
{
	/// <summary>
	/// 绳索的 ECS 重构（BPSS 901-17.0 <c>Rope.cs</c>）。
	/// </summary>
	/// <remarks>
	/// <para>
	/// BPSS 的绳子是 8 个 <c>RopeSegment</c> 刚体串成一条 <c>HingeJoint</c> 链：每节之间铰接，
	/// 两端的一节再铰接到左右两个部件上，<c>FixedUpdate</c> 里手写弹簧力维持长度，
	/// <c>Update</c> 里用 <c>LineRenderer</c> + Catmull-Rom 画 20 个采样点。
	/// </para>
	/// <para>
	/// 这里把「节的自由度」搬进 <see cref="RopeNode" /> 缓冲区（每个绳一个 buffer，8 个节点），
	/// 用 Verlet 积分 + 距离约束求解，不需要为每节创建实体和关节；只有两端节点钉在
	/// <see cref="Rope.LeftPart" /> / <see cref="Rope.RightPart" /> 上，并把约束残差换算成冲量
	/// 反推给这两个刚体 —— 这正是 BPSS <c>PullEndpoints()</c> 里
	/// <c>AddForceAtPosition(force, position)</c> 的作用。
	/// </para>
	/// <para>
	/// 断绳判定沿用 BPSS 的两档阈值：中间节点间距离 &gt; 1.25、端点 &gt; 1.75 自动断
	/// （<c>UnbreakableRope</c> 关闭时），玩家点击也会在最近节点处切断。
	/// </para>
	/// </remarks>
	[UpdateInGroup(typeof(PartSimulationSystemGroup))]
	internal partial class RopeSystem : SystemBase
	{
		/// <summary>距离约束的迭代次数。BPSS 靠 Unity 求解器迭代，这里手写 PBD。</summary>
		private const int SolverIterations = 24;

		/// <summary>端点被拉长时的等效刚度（BPSS：<c>Mathf.Clamp(750f * num4, 0f, 300f)</c>）。</summary>
		private const float EndpointStiffness = 750f;

		/// <summary>节点质量，BPSS <c>RopeSegment</c> 的 <c>m_Mass</c>。</summary>
		private const float NodeMass = 0.05f;

		/// <summary>画线用的采样点数（BPSS <c>lineRenderer.SetVertexCount(20)</c>）。</summary>
		private const int SplineSamples = 20;

		/// <summary>线在 z 轴上的位置，压在部件后面（BPSS 把节点 z 设成 -1）。</summary>
		private const float LineZ = -0.55f;

		private struct RopeVisual
		{
			public LineRenderer Line;

			public LineRenderer Second;
		}

		private EntityQuery m_ropeQuery;

		private ComponentLookup<PartTransform> m_transformLookup;

		private ComponentLookup<PartTypeValue> m_typeLookup;

		private ComponentLookup<LocalToWorld> m_worldLookup;

		private ComponentLookup<PhysicsVelocity> m_velocityLookup;

		private ComponentLookup<PhysicsMass> m_massLookup;

		private BufferLookup<RopeNode> m_nodeLookup;

		private readonly Dictionary<Entity, RopeVisual> m_visuals = new Dictionary<Entity, RopeVisual>();

		private readonly List<Entity> m_stale = new List<Entity>();

		protected override void OnCreate()
		{
			base.OnCreate();
			EntityQueryBuilder builder = new EntityQueryBuilder(Allocator.Temp);
			builder = builder.WithAllRW<Rope>();
			builder = builder.WithAll<PartTag>();
			m_ropeQuery = builder.Build(ref base.CheckedStateRef);
			m_transformLookup = base.GetComponentLookup<PartTransform>(isReadOnly: true);
			m_typeLookup = base.GetComponentLookup<PartTypeValue>(isReadOnly: true);
			m_worldLookup = base.GetComponentLookup<LocalToWorld>(isReadOnly: true);
			m_velocityLookup = base.GetComponentLookup<PhysicsVelocity>(isReadOnly: false);
			m_massLookup = base.GetComponentLookup<PhysicsMass>(isReadOnly: true);
			m_nodeLookup = base.GetBufferLookup<RopeNode>(isReadOnly: false);
		}

		protected override void OnDestroy()
		{
			base.OnDestroy();
			foreach (RopeVisual value in m_visuals.Values)
			{
				PartGameplayUtil.DestroyLine(value.Line);
				PartGameplayUtil.DestroyLine(value.Second);
			}
			m_visuals.Clear();
		}

		protected override void OnUpdate()
		{
			PartManager manager = PartManager.Instance;
			if (manager == null || !manager.IsSimulating || manager.PartSimulator == null)
			{
				ReleaseVisuals();
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
			m_velocityLookup.Update(ref base.CheckedStateRef);
			m_massLookup.Update(ref base.CheckedStateRef);
			m_nodeLookup.Update(ref base.CheckedStateRef);
			// 重力不在 PhysicsWorld 上（Unity.Physics 1.0 把它放在 PhysicsStep 单例里，
			// 而这里的 PhysicsStep 是 PartSimulator 按玩家设置创建的），直接读同一份设置。
			SimulationSettings simulationSettings = UserSettings.Instance.SimulationSettings;
			float3 gravity = new float3(simulationSettings.GravityX, simulationSettings.GravityY, 0f);
			using NativeArray<Entity> ropes = m_ropeQuery.ToEntityArray(Allocator.Temp);
			for (int i = 0; i < ropes.Length; i++)
			{
				Entity entity = ropes[i];
				if (!m_nodeLookup.HasBuffer(entity))
				{
					continue;
				}
				Rope rope = EntityManager.GetComponentData<Rope>(entity);
				DynamicBuffer<RopeNode> nodes = m_nodeLookup[entity];
				if (!rope.Built)
				{
					Build(entity, ref rope, nodes);
					EntityManager.SetComponentData(entity, rope);
					if (!rope.Built)
					{
						continue;
					}
				}
				if (rope.Cut)
				{
					// 断掉的绳子只剩两段垂着的残绳：不再施加张力，只做自由落体。
					Simulate(entity, ref rope, nodes, gravity, deltaTime, applyTension: false);
					continue;
				}
				Simulate(entity, ref rope, nodes, gravity, deltaTime, applyTension: true);
				EntityManager.SetComponentData(entity, rope);
			}
			UpdateVisuals(ropes);
		}

		/// <summary>
		/// 复现 BPSS <c>Rope.Create(leftPart, rightPart)</c>：确定两端部件，铺 8 个节点。
		/// </summary>
		/// <remarks>
		/// BPSS 从 <c>transform.position - 0.5f * transform.right</c> 起步，每节沿
		/// <c>Quaternion.AngleAxis(±74.4°, forward) * right</c> 前进 0.5——
		/// 也就是一节向左下、一节向右上地折，整条绳在 1 格里盘成锯齿。
		/// 这里照抄这个形状（初始形状决定第一次求解的收敛方向，不然绳子会突然弹开）。
		/// </remarks>
		private void Build(Entity entity, ref Rope rope, DynamicBuffer<RopeNode> nodes)
		{
			if (!m_transformLookup.HasComponent(entity) || !m_worldLookup.HasComponent(entity))
			{
				return;
			}
			PartTransform transform = m_transformLookup[entity];
			byte layer = PartBuildLayerUtility.GetLayer(EntityManager, entity);
			int rotation = ((transform.Rotation % 4) + 4) % 4;
			bool vertical = rotation % 2 == 0;
			int deltaX = vertical ? 0 : -1;
			int deltaY = vertical ? 1 : 0;
			rope.LeftPart = FindNeighbour(transform.X + deltaX, transform.Y + deltaY, layer, rotation);
			rope.RightPart = FindNeighbour(transform.X - deltaX, transform.Y - deltaY, layer, rotation);
			float3 center = m_worldLookup[entity].Position;
			quaternion worldRotation = m_worldLookup[entity].Rotation;
			float3 right = math.mul(worldRotation, new float3(1f, 0f, 0f));
			// BPSS 的折线是在世界空间里算的：绕世界 z 轴 ±74.4°，与绳子自身朝向无关，
			// 只有 90°/270° 的绳子额外再转 -90°（把锯齿转到竖直方向）。
			if (rotation % 2 != 0)
			{
				right = math.mul(quaternion.RotateZ(math.radians(-90f)), right);
			}
			int count = math.min(nodes.Length, Rope.SegmentCount);
			float3 position = center - 0.5f * right;
			float angle = -Rope.ZigZagDegrees;
			for (int i = 0; i < count; i++)
			{
				// BPSS 在 i == count-1 且右端有非绳部件时，把最后一节硬摆到
				// transform.position + 0.5 * transform.right（绳的两端正好跨过自己这一格）。
				if (i == count - 1 && rope.RightPart != Entity.Null)
				{
					position = center + 0.5f * right;
				}
				nodes[i] = new RopeNode
				{
					Position = position,
					Previous = position
				};
				float3 step = math.mul(quaternion.RotateZ(math.radians(angle)), right);
				position += 0.5f * step;
				angle = ((i % 2 != 0) ? (-Rope.ZigZagDegrees) : Rope.ZigZagDegrees);
			}
			// 记录两端节点相对各自部件的偏移（BPSS 的 HingeJoint.anchor 等价物）：
			// 之后部件怎么动，节点就跟着平移同样的量，链子不会被拽到部件中心去。
			rope.LeftAnchor = ((rope.LeftPart != Entity.Null && m_worldLookup.HasComponent(rope.LeftPart))
				? (nodes[0].Position - m_worldLookup[rope.LeftPart].Position)
				: float3.zero);
			rope.RightAnchor = ((rope.RightPart != Entity.Null && m_worldLookup.HasComponent(rope.RightPart))
				? (nodes[count - 1].Position - m_worldLookup[rope.RightPart].Position)
				: float3.zero);
			rope.Built = true;
			rope.Cut = false;
			rope.CutIndex = -1;
			rope.PreviousStretch = 0f;
		}

		/// <summary>找绳索某一侧可以连的部件：绳索自己（同朝向）/ 木框 / 铁框 / 弹踢器。</summary>
		private Entity FindNeighbour(int x, int y, byte layer, int rotation)
		{
			PartManager manager = PartManager.Instance;
			if (manager == null || manager.PartSimulator == null)
			{
				return Entity.Null;
			}
			if (!manager.PartSimulator.Unmanaged.PartMap.TryGet(x, y, layer, out PartGridInfo info))
			{
				return Entity.Null;
			}
			Entity candidate = (info.HasPart ? info.Part : info.PartContainer);
			if (candidate == Entity.Null || !m_typeLookup.HasComponent(candidate))
			{
				return Entity.Null;
			}
			PartTypeValue type = m_typeLookup[candidate];
			switch (type.Type)
			{
			case PartType.Rope:
				// 绳子可以串接，但必须同朝向（BPSS：m_gridRotation 不同就不连）。
				if (!m_transformLookup.HasComponent(candidate))
				{
					return Entity.Null;
				}
				return (((m_transformLookup[candidate].Rotation % 4) + 4) % 4 == rotation) ? candidate : Entity.Null;
			case PartType.WoodenFrame:
			case PartType.MetalFrame:
			case PartType.Kicker:
				return candidate;
			default:
				return Entity.Null;
			}
		}

		/// <summary>Verlet 积分 + 距离约束求解。</summary>
		private void Simulate(Entity entity, ref Rope rope, DynamicBuffer<RopeNode> nodes, float3 gravity, float deltaTime, bool applyTension)
		{
			int count = nodes.Length;
			if (count < 2)
			{
				return;
			}
			// 端点（钉在两个部件上的那两节）。
			bool leftPinned = rope.LeftPart != Entity.Null && m_worldLookup.HasComponent(rope.LeftPart);
			bool rightPinned = rope.RightPart != Entity.Null && m_worldLookup.HasComponent(rope.RightPart);
			float3 leftAnchor = (leftPinned ? (m_worldLookup[rope.LeftPart].Position + rope.LeftAnchor) : float3.zero);
			float3 rightAnchor = (rightPinned ? (m_worldLookup[rope.RightPart].Position + rope.RightAnchor) : float3.zero);
			// 端点速度（用于阻尼公式，BPSS 用两端节点的最大速度算 drag）。
			float maxSpeed = 0f;
			for (int i = 0; i < count; i++)
			{
				float3 velocity = (nodes[i].Position - nodes[i].Previous) / deltaTime;
				maxSpeed = math.max(maxSpeed, math.length(velocity));
			}
			// BPSS：rigidbody.drag = 0.5f + 6f / (1f + 4f * num * num)
			float drag = 0.5f + 6f / (1f + 4f * maxSpeed * maxSpeed);
			float damping = math.exp(0f - drag * deltaTime);
			float3 acceleration = gravity * (deltaTime * deltaTime);
			for (int j = 0; j < count; j++)
			{
				bool pinned = ((j == 0 && leftPinned) || (j == count - 1 && rightPinned));
				if (pinned)
				{
					float3 target = ((j == 0) ? leftAnchor : rightAnchor);
					nodes[j] = new RopeNode
					{
						Position = target,
						Previous = target
					};
					continue;
				}
				float3 current = nodes[j].Position;
				float3 velocity2 = current - nodes[j].Previous;
				nodes[j] = new RopeNode
				{
					Previous = current,
					Position = current + velocity2 * damping + acceleration
				};
			}
			// 距离约束（PBD）。被钉住的一端承担 0 权重，另一端吃满修正。
			for (int iteration = 0; iteration < SolverIterations; iteration++)
			{
				for (int k = 1; k < count; k++)
				{
					if (rope.Cut && k == rope.CutIndex)
					{
						continue;
					}
					bool aPinned = ((k - 1 == 0 && leftPinned) || (k - 1 == count - 1 && rightPinned));
					bool bPinned = ((k == 0 && leftPinned) || (k == count - 1 && rightPinned));
					if (aPinned && bPinned)
					{
						continue;
					}
					float3 a = nodes[k - 1].Position;
					float3 b = nodes[k].Position;
					float3 delta = b - a;
					float distance = math.length(delta);
					if (distance <= 1E-05f)
					{
						continue;
					}
					float error = distance - Rope.RestLength;
					float3 direction = delta / distance;
					float weightA = (aPinned ? 0f : (bPinned ? 1f : 0.5f));
					float weightB = 1f - weightA;
					RopeNode nodeA = nodes[k - 1];
					RopeNode nodeB = nodes[k];
					nodeA.Position = a + direction * (error * weightA);
					nodeB.Position = b - direction * (error * weightB);
					nodes[k - 1] = nodeA;
					nodes[k] = nodeB;
				}
			}
			if (!applyTension)
			{
				return;
			}
			// 端点张力：约束解完后剩下的拉伸量就是绳子受的力，按 BPSS 的
			// Clamp(750 * stretch, 0, 300) 换算成冲量反推给两端的部件。
			ApplyEndpointTension(rope.LeftPart, nodes[0].Position, nodes[1].Position, deltaTime);
			ApplyEndpointTension(rope.RightPart, nodes[count - 1].Position, nodes[count - 2].Position, deltaTime);
			// 超过阈值自动断绳（BPSS PullEndpoints 的 1.25 / 1.75 两档）。
			for (int l = 1; l < count; l++)
			{
				float length = math.length(nodes[l].Position - nodes[l - 1].Position);
				bool inner = (l > 1 && l < count - 2);
				float limit = (inner ? Rope.CutDistanceInner : Rope.CutDistanceEnd);
				if (length > limit)
				{
					Cut(entity, ref rope, math.clamp(l, 2, count - 2));
					return;
				}
			}
		}

		/// <summary>把端点处剩余的拉伸换算成冲量，推给挂在这头的部件。</summary>
		private void ApplyEndpointTension(Entity part, float3 endpoint, float3 neighbour, float deltaTime)
		{
			if (part == Entity.Null || !m_velocityLookup.HasComponent(part) || !m_massLookup.HasComponent(part))
			{
				return;
			}
			float3 delta = neighbour - endpoint;
			float distance = math.length(delta);
			if (distance <= Rope.RestLength + 1E-04f)
			{
				// 松弛状态（BPSS：num > 1.18f * num3 才开始拉，平时不施力）。
				return;
			}
			float stretch = math.min(distance - Rope.RestLength, Rope.StretchLimit);
			float force = math.clamp(EndpointStiffness * stretch, 0f, Rope.MaxEndpointForce);
			float3 direction = delta / distance;
			PhysicsMass mass = m_massLookup[part];
			if (mass.InverseMass <= 0f)
			{
				return;
			}
			PhysicsVelocity velocity = m_velocityLookup[part];
			velocity.Linear += direction * (force * deltaTime * mass.InverseMass);
			m_velocityLookup[part] = velocity;
		}

		private void Cut(Entity entity, ref Rope rope, int index)
		{
			rope.Cut = true;
			rope.CutIndex = index;
			if (m_visuals.TryGetValue(entity, out RopeVisual visual) && visual.Second == null)
			{
				visual.Second = PartGameplayUtil.CreateLine("RopeTether2", new Color(1f, 0.235f, 0.353f, 1f), 0.2f);
				visual.Second.positionCount = SplineSamples;
				m_visuals[entity] = visual;
			}
			UnityEngine.Debug.Log("[RopeSystem] rope cut at node " + index);
		}

		private void UpdateVisuals(NativeArray<Entity> ropes)
		{
			// 清掉已经不存在的绳子的线（场景切换/结束模拟）。
			m_stale.Clear();
			foreach (KeyValuePair<Entity, RopeVisual> visual2 in m_visuals)
			{
				if (!EntityManager.Exists(visual2.Key) || !m_nodeLookup.HasBuffer(visual2.Key))
				{
					m_stale.Add(visual2.Key);
				}
			}
			for (int i = 0; i < m_stale.Count; i++)
			{
				if (m_visuals.TryGetValue(m_stale[i], out RopeVisual value))
				{
					PartGameplayUtil.DestroyLine(value.Line);
					PartGameplayUtil.DestroyLine(value.Second);
				}
				m_visuals.Remove(m_stale[i]);
			}
			m_stale.Clear();
			for (int j = 0; j < ropes.Length; j++)
			{
				Entity entity = ropes[j];
				if (!m_nodeLookup.HasBuffer(entity))
				{
					continue;
				}
				if (!m_visuals.TryGetValue(entity, out RopeVisual visual3))
				{
					visual3 = new RopeVisual
					{
						Line = PartGameplayUtil.CreateLine("RopeTether", new Color(1f, 0.235f, 0.353f, 1f), 0.2f),
						Second = null
					};
					visual3.Line.positionCount = SplineSamples;
					m_visuals[entity] = visual3;
				}
				Rope rope2 = EntityManager.GetComponentData<Rope>(entity);
				DynamicBuffer<RopeNode> buffer = m_nodeLookup[entity];
				if (!rope2.Built || buffer.Length < 2)
				{
					continue;
				}
				if (rope2.Cut)
				{
					RenderSpline(visual3.Line, buffer, 0, math.clamp(rope2.CutIndex, 1, buffer.Length - 1));
					if (visual3.Second == null)
					{
						visual3.Second = PartGameplayUtil.CreateLine("RopeTether2", new Color(1f, 0.235f, 0.353f, 1f), 0.2f);
						visual3.Second.positionCount = SplineSamples;
						m_visuals[entity] = visual3;
					}
					RenderSpline(visual3.Second, buffer, math.clamp(rope2.CutIndex, 0, buffer.Length - 2), buffer.Length);
				}
				else
				{
					RenderSpline(visual3.Line, buffer, 0, buffer.Length);
					if (visual3.Second != null)
					{
						PartGameplayUtil.DestroyLine(visual3.Second);
						visual3.Second = null;
						m_visuals[entity] = visual3;
					}
				}
			}
		}

		/// <summary>用 Catmull-Rom 把节点插值成 <see cref="SplineSamples" /> 个采样点（BPSS <c>RenderRope</c>）。</summary>
		private static void RenderSpline(LineRenderer line, DynamicBuffer<RopeNode> nodes, int start, int end)
		{
			if (line == null)
			{
				return;
			}
			int count = end - start;
			if (count < 2)
			{
				line.enabled = false;
				return;
			}
			line.enabled = true;
			for (int i = 0; i < SplineSamples; i++)
			{
				float t = (float)i / (float)(SplineSamples - 1);
				float3 point = SampleSpline(nodes, start, end, t);
				line.SetPosition(i, new Vector3(point.x, point.y, LineZ));
			}
		}

		private static float3 SampleSpline(DynamicBuffer<RopeNode> nodes, int start, int end, float t)
		{
			int count = end - start;
			int segment = math.clamp((int)math.floor(t * (float)(count - 1)), 0, count - 1);
			float3 a = Nodes(nodes, start, end, segment - 1);
			float3 b = Nodes(nodes, start, end, segment);
			float3 c = Nodes(nodes, start, end, segment + 1);
			float3 d = Nodes(nodes, start, end, segment + 2);
			float local = t * (float)(count - 1) - (float)segment;
			return CatmullRom(a, b, c, d, local);
		}

		private static float3 Nodes(DynamicBuffer<RopeNode> nodes, int start, int end, int index)
		{
			int clamped = math.clamp(index, 0, end - start - 1);
			return nodes[start + clamped].Position;
		}

		/// <summary>与 BPSS <c>MathsUtil.CatmullRomInterpolate</c> 相同的参数式插值。</summary>
		private static float3 CatmullRom(float3 a, float3 b, float3 c, float3 d, float t)
		{
			float t2 = t * t;
			float t3 = t2 * t;
			return 0.5f * (2f * b + (c - a) * t + (2f * a - 5f * b + 4f * c - d) * t2 + (3f * b - a - 3f * c + d) * t3);
		}

		private void ReleaseVisuals()
		{
			if (m_visuals.Count == 0)
			{
				return;
			}
			foreach (RopeVisual value in m_visuals.Values)
			{
				PartGameplayUtil.DestroyLine(value.Line);
				PartGameplayUtil.DestroyLine(value.Second);
			}
			m_visuals.Clear();
		}
	}
}
