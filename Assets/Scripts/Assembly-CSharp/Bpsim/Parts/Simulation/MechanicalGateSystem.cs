using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Collections;
using Unity.Rendering;
using Unity.Transforms;

namespace Bpsim.Parts.Simulation
{
	/// <summary>
	/// 机械闸门的展开/收回动画，对应 BPSS 901-17.0 的 <c>MechanicalGate.FixedUpdate()</c>。
	/// </summary>
	/// <remarks>
	/// <para>
	/// 原版每帧做四件事（<c>MechanicalGate.cs</c> 的 FixedUpdate）：
	/// </para>
	/// <list type="number">
	/// <item><c>m_controller.Update()</c>：<c>progress += delta</c> 夹到 0-1，
	/// <c>eased = 0.5(1 - cos(π·progress))</c>，<c>value = (1-eased)·start + eased·end</c>。</item>
	/// <item><c>GateCollider.localPosition = (0.5 + value·0.5, 0, 0)</c>、<c>localScale = (value·width, thickness, 1)</c>。</item>
	/// <item><c>GateRenderer</c> 同上，只是 z 固定 0.01，并把材质 alpha 设成 <c>GateOpacity</c>（默认 0.8）。</item>
	/// <item><c>m_gateCollider.enabled = value &gt; 0</c>。</item>
	/// </list>
	/// <para>
	/// ECS 侧的分工：子实体的 <see cref="LocalTransform" /> / <see cref="PostTransformScale" /> /
	/// <see cref="Unity.Rendering.MaterialColor" /> 直接写，和原版改 Transform 等价；
	/// 碰撞体不能这样缩放（Unity Physics 的复合碰撞体是烘焙出来的不可变 blob），
	/// 所以这里按「根件 1×1×1 + 门板」重建 <see cref="CompoundCollider" />，
	/// 并且只在 <see cref="BuildStep" /> 量化档位变化时才重建 —— 原版每次 FixedUpdate 都改
	/// BoxCollider 的 scale，PhysX 内部是连续更新的，ECS 侧重建整块复合体太贵，量化是必要折中。
	/// </para>
	/// </remarks>
	[UpdateInGroup(typeof(PartSimulationSystemGroup))]
	[UpdateBefore(typeof(PartPowerSystem))]
	internal partial class MechanicalGateSystem : SystemBase
	{
		/// <summary>碰撞体重建的长度量化步长（格）。0.05 格 ≈ 视觉上看不出台阶。</summary>
		private const float BuildStep = 0.05f;

		private EntityQuery m_gateQuery;

		private ComponentLookup<GateVisual> m_visualLookup;

		protected override void OnCreate()
		{
			EntityQueryBuilder builder = new EntityQueryBuilder(Allocator.Temp);
			builder = builder.WithAllRW<MechanicalGate>();
			builder = builder.WithAll<PartTag>();
			builder = builder.WithAll<LinkedEntityGroup>();
			m_gateQuery = builder.Build(ref base.CheckedStateRef);
			m_visualLookup = base.CheckedStateRef.GetComponentLookup<GateVisual>(isReadOnly: true);
		}

		protected override void OnUpdate()
		{
			PartManager manager = PartManager.Instance;
			if (manager == null || !manager.IsSimulating)
			{
				return;
			}
			m_visualLookup.Update(ref base.CheckedStateRef);
			float deltaTime = World.Time.DeltaTime;
			using NativeArray<Entity> gates = m_gateQuery.ToEntityArray(Allocator.Temp);
			for (int i = 0; i < gates.Length; i++)
			{
				Entity entity = gates[i];
				MechanicalGate gate = EntityManager.GetComponentData<MechanicalGate>(entity);
				if (gate.Advance(deltaTime))
				{
					EntityManager.SetComponentData(entity, gate);
				}
				// BPSS 的 FixedUpdate 每帧无条件写 transform / 材质 alpha（不只在动画期间），
				// 这里照做：一来门板不透明度要一直压着（别的系统可能重置 MaterialColor），
				// 二来碰撞体的「只在量化档位变化时重建」也因此有了每帧的兜底检查。
				UpdateVisuals(entity, gate);
				RebuildCollider(entity, ref gate);
			}
		}

		/// <summary>BPSS 直接改两个子物体的 Transform；这里写 ECS 的 LocalTransform + PostTransformScale。</summary>
		private void UpdateVisuals(Entity root, MechanicalGate gate)
		{
			DynamicBuffer<LinkedEntityGroup> group = EntityManager.GetBuffer<LinkedEntityGroup>(root, isReadOnly: true);
			float3 position = new float3(0.5f + gate.Value * 0.5f, 0f, 0f);
			float3 scale = new float3(gate.Value * gate.Width, gate.Thickness, 1f);
			// 先拷出子实体再改：循环里 AddComponentData 是结构变更，会让上面的
			// DynamicBuffer 句柄失效（ObjectDisposedException: invalidated by a structural change）。
			using NativeList<Entity> children = new NativeList<Entity>(group.Length, Allocator.Temp);
			for (int i = 1; i < group.Length; i++)
			{
				children.Add(group[i].Value);
			}
			for (int i = 0; i < children.Length; i++)
			{
				Entity child = children[i];
				if (!m_visualLookup.HasComponent(child))
				{
					continue;
				}
				bool isCollider = m_visualLookup[child].Collider;
				if (!EntityManager.HasComponent<LocalTransform>(child))
				{
					continue;
				}
				// BPSS: gateRenderer.localPosition = (localPos.x, localPos.y, 0.01f)
				float3 childPosition = position;
				if (!isCollider)
				{
					childPosition.z = 0.01f;
				}
				LocalTransform transform = EntityManager.GetComponentData<LocalTransform>(child);
				transform.Position = childPosition;
				transform.Scale = 1f;
				EntityManager.SetComponentData(child, transform);
				PostTransformScale post = new PostTransformScale
				{
					Value = float3x3.Scale(scale)
				};
				if (EntityManager.HasComponent<PostTransformScale>(child))
				{
					EntityManager.SetComponentData(child, post);
				}
				else
				{
					EntityManager.AddComponentData(child, post);
				}
				// 材质 alpha 不在这里写：ColoredFrameSystem 在 SimulationSystemGroup 里每帧把
				// 所有非染色件的 MaterialColor 重置成 (1,1,1,1)，仿真组里写会被它盖掉。
				// 见 MechanicalGateVisualSystem（表现组，晚于 SimulationSystemGroup）。
			}
		}

		/// <summary>
		/// 重建复合碰撞体：根件 1×1×1 的盒子 + 当前长度的门板。
		/// <c>value &lt;= 0</c> 时只留根件（等价于原版 <c>m_gateCollider.enabled = value &gt; 0</c>）。
		/// </summary>
		private void RebuildCollider(Entity root, ref MechanicalGate gate)
		{
			if (!EntityManager.HasComponent<PhysicsCollider>(root))
			{
				return;
			}
			float quantized = math.round(gate.Value / BuildStep) * BuildStep;
			if (math.abs(quantized - gate.BuiltLength) < 1E-4f)
			{
				return;
			}
			gate.BuiltLength = quantized;
			EntityManager.SetComponentData(root, gate);
			NativeList<CompoundCollider.ColliderBlobInstance> list = new NativeList<CompoundCollider.ColliderBlobInstance>(2, Allocator.Temp);
			// 子碰撞体的 blob 只用于这一次 Create：CompoundCollider.Create 会把子碰撞体
			// **复制**进自己的 blob（见 CompoundCollider.cs 注释 "The source colliders are copied
			// into the compound"），所以建完必须自己释放，否则每次展开/收回都会漏一块原生内存。
			BlobAssetReference<Collider> body = default(BlobAssetReference<Collider>);
			BlobAssetReference<Collider> blade = default(BlobAssetReference<Collider>);
			try
			{
				// 根件自身的 1×1×1 盒体（BPSS 预制体 Part_MechanicalPart_N 的 BoxCollider）。
				body = PartPhysics.CreateBox(float3.zero, new float3(1f, 1f, 1f));
				list.Add(new CompoundCollider.ColliderBlobInstance
				{
					CompoundFromChild = RigidTransform.identity,
					Collider = body,
					Entity = root
				});
				if (quantized > 0f)
				{
					// 门板：本地中心 (0.5 + value/2, 0, 0)，尺寸 (value·width, 0.5·thickness, 1)。
					// 预制体里 GateCollider 的 BoxCollider 是 1×0.5×1，靠 localScale 缩放，
					// 所以 y 方向要再乘 0.5。
					float3 center = new float3(0.5f + quantized * 0.5f, 0f, 0f);
					float3 size = new float3(quantized * gate.Width, 0.5f * gate.Thickness, 1f);
					blade = PartPhysics.CreateBox(float3.zero, size);
					list.Add(new CompoundCollider.ColliderBlobInstance
					{
						CompoundFromChild = new RigidTransform(quaternion.identity, center),
						Collider = blade,
						Entity = root
					});
				}
				BlobAssetReference<Collider> compound = CompoundCollider.Create(list.AsArray());
				EntityManager.SetComponentData(root, new PhysicsCollider
				{
					Value = compound
				});
				// 旧复合体延后一次重建再释放：换组件的那一帧物理系统还要用它做缓存替换。
				if (gate.PendingDispose.IsCreated)
				{
					gate.PendingDispose.Dispose();
				}
				gate.PendingDispose = gate.OwnedCompound;
				gate.OwnedCompound = compound;
				EntityManager.SetComponentData(root, gate);
			}
			finally
			{
				list.Dispose();
				if (body.IsCreated)
				{
					body.Dispose();
				}
				if (blade.IsCreated)
				{
					blade.Dispose();
				}
			}
		}
	}
}
