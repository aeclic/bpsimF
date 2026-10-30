using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace Bpsim.Parts.Simulation
{
	/// <summary>
	/// 鸟蛋族（<see cref="PartType.Egg" /> 索引 0-5）的 ECS 实现，对应 BPSS 901-17.0
	/// <c>Egg.cs</c> 与 <c>AlienEgg.cs</c>。
	/// </summary>
	/// <remarks>
	/// <para>
	/// 原版把「开关状态」和「已经改了谁」都塞在 MonoBehaviour 字段里，并在
	/// <c>SetEnabled</c> 里同步修改别的刚体 / Collider / Joint。ECS 版把这个过程拆成两半：
	/// 行为层（<c>EggBehavior</c>）只翻转 <see cref="Egg.Enabled" />，本系统每物理帧
	/// 把 <see cref="Egg.Applied" /> 与它对齐 —— 要还原的原值写进蛋实体自己的
	/// <see cref="EggEffect" /> 缓冲区，因此实体的创建/销毁/存档都自动带着状态。
	/// </para>
	/// <para>
	/// 五类效果与 BPSS 的逐条对应：
	/// </para>
	/// <list type="bullet">
	/// <item>重力蛋：<c>FixedUpdate</c> 对全场刚体施加 <c>force / d³ · m_self · m_other · d⃗</c>，
	/// 非重力蛋的对方还要吃反作用力（牛顿第三定律）。</item>
	/// <item>失重蛋：把同连通块所有部件的 <c>useGravity</c> 关掉；斜向朝向时改走
	/// 「无质量」分支 —— 质量 × 0.001、速度 × √1000（保持动能）。</item>
	/// <item>穿透蛋：关掉同连通块所有碰撞体的 <c>enabled</c>。</item>
	/// <item>虚空蛋：每帧把半径内的碰撞体关掉、离开半径的恢复（半径按旋转方向取表）。</item>
	/// <item>异形胶蛋：把同连通块所有关节的 <c>breakForce</c> 设成 ∞（这里是
	/// <c>PhysicsJoint</c> 的冲量事件阈值）。</item>
	/// </list>
	/// </remarks>
	[UpdateInGroup(typeof(PartSimulationSystemGroup))]
	internal partial class EggSystem : SystemBase
	{
		/// <summary>动能守恒时速度的缩放比：√(1 / 0.001)。</summary>
		private const float MasslessSpeedFactor = 31.6227766f;

		/// <summary>重力蛋单步（0.02 s）速度增量上限，防止极端距离/质量把物理世界打崩。</summary>
		private const float MaxGravityDeltaVelocity = 40f;

		private EntityQuery m_eggQuery;

		private ComponentLookup<PartTransform> m_transformLookup;

		private ComponentLookup<PartTypeValue> m_typeLookup;

		private ComponentLookup<LocalToWorld> m_worldLookup;

		private ComponentLookup<PartConnectedComponent> m_connectedLookup;

		private ComponentLookup<PartExtensionComponent> m_extensionLookup;

		private ComponentLookup<PhysicsVelocity> m_velocityLookup;

		private ComponentLookup<PhysicsMass> m_massLookup;

		private ComponentLookup<PhysicsCollider> m_colliderLookup;

		private ComponentLookup<PhysicsGravityFactor> m_gravityLookup;

		private ComponentLookup<PhysicsConstrainedBodyPair> m_pairLookup;

		private ComponentLookup<PhysicsJoint> m_jointLookup;

		/// <summary>
		/// 当前蛋的 <see cref="EggEffect" /> 工作副本。所有效果的增删都先在这份
		/// 临时列表里做，最后一次性写回实体缓冲区 —— 因为还原效果要 <c>RemoveComponent</c> /
		/// 添加 <c>PhysicsGravityFactor</c> 这类结构变更，会让 <c>DynamicBuffer</c> 的
		/// 内部指针失效（GearboxSystem 曾因同样原因抛过 ObjectDisposedException）。
		/// </summary>
		private NativeList<EggEffect> m_effects;

		/// <summary>
		/// 当前蛋所属连通块的成员快照。必须在做任何结构变更（<c>AddComponentData</c> /
		/// <c>RemoveComponent</c>）之前一次性收集 —— 结构变更会让
		/// <c>ComponentLookup&lt;PartConnectedComponent&gt;</c> 失效，之后再用它判断
		/// 「这个部件是否同块」会漏掉一部分成员（实测只有第一个部件被改到）。
		/// </summary>
		private NativeList<Entity> m_componentParts;

		/// <summary>与 <see cref="m_componentParts" /> 一一对应的连通块索引快照。</summary>
		private NativeList<int> m_componentIndexes;

		protected override void OnCreate()
		{
			base.OnCreate();
			EntityQueryBuilder builder = new EntityQueryBuilder(Allocator.Temp);
			builder = builder.WithAllRW<Egg>();
			builder = builder.WithAll<PartTag>();
			// 部件预制体（PartEntityFactory 里 prefab:true 建的那批）同样带 Egg + PartTag，
			// 而且它们的连通块索引是 0，正好和场景里第一个部件的索引撞上 —— 不排除的话，
			// 预制体蛋会去关真实部件的碰撞体（实测 Ghost 蛋的碰撞体在 Bind 之前就是关的）。
			builder = builder.WithNone<Prefab>();
			m_eggQuery = builder.Build(ref base.CheckedStateRef);
			m_transformLookup = base.GetComponentLookup<PartTransform>(isReadOnly: true);
			m_typeLookup = base.GetComponentLookup<PartTypeValue>(isReadOnly: true);
			m_worldLookup = base.GetComponentLookup<LocalToWorld>(isReadOnly: true);
			m_connectedLookup = base.GetComponentLookup<PartConnectedComponent>(isReadOnly: true);
			m_extensionLookup = base.GetComponentLookup<PartExtensionComponent>(isReadOnly: true);
			m_velocityLookup = base.GetComponentLookup<PhysicsVelocity>();
			m_massLookup = base.GetComponentLookup<PhysicsMass>();
			m_colliderLookup = base.GetComponentLookup<PhysicsCollider>();
			m_gravityLookup = base.GetComponentLookup<PhysicsGravityFactor>();
			m_pairLookup = base.GetComponentLookup<PhysicsConstrainedBodyPair>(isReadOnly: true);
			m_jointLookup = base.GetComponentLookup<PhysicsJoint>();
			m_effects = new NativeList<EggEffect>(8, Allocator.Persistent);
			m_componentParts = new NativeList<Entity>(16, Allocator.Persistent);
			m_componentIndexes = new NativeList<int>(16, Allocator.Persistent);
		}

		protected override void OnDestroy()
		{
			if (m_effects.IsCreated)
			{
				m_effects.Dispose();
			}
			if (m_componentParts.IsCreated)
			{
				m_componentParts.Dispose();
			}
			if (m_componentIndexes.IsCreated)
			{
				m_componentIndexes.Dispose();
			}
			base.OnDestroy();
		}

		protected override void OnUpdate()
		{
			PartManager manager = PartManager.Instance;
			if (manager == null || !manager.IsSimulating || manager.PartSimulator == null)
			{
				return;
			}
			if (m_eggQuery.IsEmptyIgnoreFilter)
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
			m_connectedLookup.Update(ref base.CheckedStateRef);
			m_extensionLookup.Update(ref base.CheckedStateRef);
			m_velocityLookup.Update(ref base.CheckedStateRef);
			m_massLookup.Update(ref base.CheckedStateRef);
			m_colliderLookup.Update(ref base.CheckedStateRef);
			m_gravityLookup.Update(ref base.CheckedStateRef);
			m_pairLookup.Update(ref base.CheckedStateRef);
			m_jointLookup.Update(ref base.CheckedStateRef);
			Reference<PartSimulatorUnmanaged> simulator = manager.PartSimulator.UnmanagedRef;
			m_componentParts.Clear();
			m_componentIndexes.Clear();
			NativeList<Entity> simulatorParts = simulator.Value.Parts;
			// 世界上同时存在两份蛋实体：PartScene 里的场景实体，和 InstantiatePartsJob
			// 拷进模拟器的那一份。m_eggQuery 是世界级查询，两份都会命中，所以必须在这里按
			// 模拟器部件表过滤。
			// 不过滤的后果：场景实体的 PartConnectedComponent.Index 与模拟器里的索引毫无关系，
			// 却会被拿去和 m_componentParts 比对 —— 一旦撞上同一个数字，Alien 蛋就会把"别人的"
			// 关节写成 ∞，并把原值记进场景实体自己的 EggEffect buffer（模拟器副本永远看不到，
			// 也就永远还不回来）；等模拟器里那颗蛋真正 Reconcile 时关节已是 ∞，ApplyAlienGlue
			// 全部跳过（recorded=0），点击关闭时 restoreAll count=0，关节永久松不开。
			using NativeParallelHashSet<Entity> simulatedParts = new NativeParallelHashSet<Entity>(simulatorParts.Length + 1, Allocator.Temp);
			for (int k = 0; k < simulatorParts.Length; k++)
			{
				Entity part = simulatorParts[k];
				simulatedParts.Add(part);
				if (m_connectedLookup.HasComponent(part))
				{
					m_componentParts.Add(part);
					m_componentIndexes.Add(m_connectedLookup[part].Index);
				}
			}
			using NativeArray<Entity> eggs = m_eggQuery.ToEntityArray(Allocator.Temp);
			for (int i = 0; i < eggs.Length; i++)
			{
				Entity entity = eggs[i];
				if (!simulatedParts.Contains(entity) || !EntityManager.HasComponent<Egg>(entity))
				{
					continue;
				}
				Egg egg = EntityManager.GetComponentData<Egg>(entity);
				if (!egg.Bound)
				{
					Bind(entity, ref egg);
				}
				DynamicBuffer<EggEffect> buffer = EntityManager.GetBuffer<EggEffect>(entity);
				m_effects.Clear();
				m_effects.AddRange(buffer.AsNativeArray());
				buffer.Clear();
				switch (egg.Kind)
				{
				case EggKind.Gravity:
					if (egg.Enabled)
					{
						ApplyGravity(entity, simulator, deltaTime);
					}
					break;
				case EggKind.Void:
					ApplyVoid(entity, ref egg, simulator);
					break;
				case EggKind.Massless:
				case EggKind.Ghost:
				case EggKind.Alien:
					Reconcile(entity, ref egg, simulator);
					break;
				}
				EntityManager.SetComponentData(entity, egg);
				DynamicBuffer<EggEffect> written = EntityManager.GetBuffer<EggEffect>(entity);
				for (int j = 0; j < m_effects.Length; j++)
				{
					written.Add(m_effects[j]);
				}
			}
		}

		/// <summary>
		/// 复现 BPSS <c>Egg.PostInitialize()</c> / <c>AlienEgg.PostInitialize()</c>：
		/// 重力蛋把自身质量改成 50；失重蛋/穿透蛋/异形胶蛋开局就打开；虚空蛋按朝向决定。
		/// </summary>
		private void Bind(Entity entity, ref Egg egg)
		{
			egg.Bound = true;
			int rotation = m_transformLookup.HasComponent(entity) ? m_transformLookup[entity].Rotation : 0;
			egg.VoidRadius = Egg.DefaultVoidRadius(rotation);
			egg.VoidDefaultOn = Egg.IsOrthogonal(rotation);
			switch (egg.Kind)
			{
			case EggKind.Gravity:
				SetMass(entity, Egg.GravityMass);
				break;
			case EggKind.Massless:
			case EggKind.Ghost:
			case EggKind.Alien:
				egg.Enabled = true;
				break;
			case EggKind.Void:
				egg.Enabled = egg.VoidDefaultOn;
				break;
			}
			string mask = "none";
			if (EntityManager.HasComponent<PhysicsCollider>(entity))
			{
				PhysicsCollider pc = EntityManager.GetComponentData<PhysicsCollider>(entity);
				mask = pc.Value.IsCreated ? ("0x" + pc.Value.Value.GetCollisionFilter().CollidesWith.ToString("X8")) : "uncreated";
			}
			UnityEngine.Debug.Log("[EggSystem] bind kind=" + egg.Kind + " rot=" + rotation + " enabled=" + egg.Enabled
				+ " part=" + entity.Index + "/" + entity.Version
				+ " prefab=" + EntityManager.HasComponent<Prefab>(entity)
				+ " mask=" + mask);
		}

		private void SetMass(Entity entity, float mass)
		{
			if (!m_colliderLookup.HasComponent(entity) || !m_massLookup.HasComponent(entity))
			{
				return;
			}
			PhysicsCollider collider = m_colliderLookup[entity];
			if (!collider.Value.IsCreated)
			{
				return;
			}
			EntityManager.SetComponentData(entity, PhysicsMass.CreateDynamic(collider.Value.Value.MassProperties, mass));
		}

		/// <summary>重力蛋：BPSS <c>Egg.FixedUpdate()</c> 的 <c>case 1</c>。</summary>
		private void ApplyGravity(Entity eggEntity, Reference<PartSimulatorUnmanaged> simulator, float deltaTime)
		{
			if (!m_velocityLookup.HasComponent(eggEntity) || !m_massLookup.HasComponent(eggEntity))
			{
				return;
			}
			float3 position = m_worldLookup[eggEntity].Position;
			float selfInverse = m_massLookup[eggEntity].InverseMass;
			if (!math.isfinite(selfInverse) || selfInverse <= 0f)
			{
				return;
			}
			float selfMass = 1f / selfInverse;
			NativeList<Entity> parts = simulator.Value.Parts;
			float3 reaction = float3.zero;
			for (int i = 0; i < parts.Length; i++)
			{
				Entity other = parts[i];
				if (other == eggEntity || !m_velocityLookup.HasComponent(other) || !m_massLookup.HasComponent(other))
				{
					continue;
				}
				float3 delta = position - m_worldLookup[other].Position;
				float distance = math.length(delta);
				if (distance >= Egg.GravityOuterRadius)
				{
					continue;
				}
				// 无限质量（kinematic / 静态）的刚体推不动：BPSS 里对 kinematic 刚体 AddForce 也是
				// 无效的，而这里如果按 1/InverseMass 反算质量会得到 1e6，力大 6 个数量级，
				// 直接把物理世界打到 integrity check 失败（后续所有场景读不到碰撞体）。
				float otherInverse = m_massLookup[other].InverseMass;
				if (!math.isfinite(otherInverse) || otherInverse <= 0f)
				{
					continue;
				}
				distance = math.max(distance, Egg.GravityInnerRadius);
				float otherMass = 1f / otherInverse;
				float coefficient = Egg.GravityForce / (distance * distance * distance) * selfMass * otherMass;
				float3 force = coefficient * delta;
				PhysicsVelocity velocity = m_velocityLookup[other];
				// 单步速度增量限幅：BPSS 用 PhysX 能吞下 400 N 级的持续推力，Unity Physics 的
				// 完整性检查会把由此累积的超高速度判成损坏，进而停掉整个物理世界。
				float3 deltaVelocity = force * (deltaTime * otherInverse);
				deltaVelocity = math.clamp(deltaVelocity, -MaxGravityDeltaVelocity, MaxGravityDeltaVelocity);
				velocity.Linear += deltaVelocity;
				m_velocityLookup[other] = velocity;
				// BPSS：对方也是重力蛋（索引 1）时不加反作用力，否则会互相抵消成 0。
				if (!IsGravityEgg(other))
				{
					reaction -= force;
				}
			}
			if (math.lengthsq(reaction) > 0f)
			{
				PhysicsVelocity self = m_velocityLookup[eggEntity];
				float3 selfDelta = reaction * (deltaTime * selfInverse);
				selfDelta = math.clamp(selfDelta, -MaxGravityDeltaVelocity, MaxGravityDeltaVelocity);
				self.Linear += selfDelta;
				m_velocityLookup[eggEntity] = self;
			}
		}

		private bool IsGravityEgg(Entity entity)
		{
			if (!m_typeLookup.HasComponent(entity))
			{
				return false;
			}
			return Egg.GetKind(m_typeLookup[entity].Value) == EggKind.Gravity;
		}

		/// <summary>虚空蛋：BPSS <c>Egg.FixedUpdate()</c> 的 <c>case 4</c>。</summary>
		private void ApplyVoid(Entity eggEntity, ref Egg egg, Reference<PartSimulatorUnmanaged> simulator)
		{
			NativeList<EggEffect> effects = m_effects;
			float3 position = m_worldLookup[eggEntity].Position;
			float radius = math.max(0f, egg.VoidRadius);
			float radiusSq = radius * radius;
			if (!egg.Enabled)
			{
				// 关掉时把所有被虚化的碰撞体还原（BPSS SetEnabled(false) 的 case 4 分支）。
				if (CountVoidEffects(effects) > 0)
				{
					UnityEngine.Debug.Log("[EggSystem] voidRestore effects=" + effects.Length + DescribeKinds(effects));
				}
				for (int i = effects.Length - 1; i >= 0; i--)
				{
					if (effects[i].Kind == EggEffectKind.VoidCollider)
					{
						RestoreCollider(effects[i].Target, effects[i].Filter);
						effects.RemoveAt(i);
					}
				}
				return;
			}
			// 先恢复离开半径的，再把仍在半径内的重新记一遍（BPSS 是清表后整体重扫）。
			for (int i = effects.Length - 1; i >= 0; i--)
			{
				EggEffect effect = effects[i];
				if (effect.Kind != EggEffectKind.VoidCollider)
				{
					continue;
				}
				if (!EntityManager.Exists(effect.Target))
				{
					effects.RemoveAt(i);
					continue;
				}
				float3 delta = m_worldLookup[effect.Target].Position - position;
				if (math.dot(delta, delta) >= radiusSq)
				{
					RestoreCollider(effect.Target, effect.Filter);
					effects.RemoveAt(i);
				}
			}
			NativeList<Entity> parts = simulator.Value.Parts;
			for (int j = 0; j < parts.Length; j++)
			{
				Entity target = parts[j];
				if (!EntityManager.HasComponent<PhysicsCollider>(target))
				{
					continue;
				}
				float3 delta2 = m_worldLookup[target].Position - position;
				if (math.dot(delta2, delta2) >= radiusSq)
				{
					continue;
				}
				if (IsRecorded(effects, EggEffectKind.VoidCollider, target))
				{
					continue;
				}
				DisableCollider(target, EggEffectKind.VoidCollider, effects);
			}
		}

		private bool IsRecorded(NativeList<EggEffect> effects, EggEffectKind kind, Entity target)
		{
			for (int i = 0; i < effects.Length; i++)
			{
				if (effects[i].Kind == kind && effects[i].Target == target)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// 一键式效果（失重 / 穿透 / 异形胶）的幂等调和：把 <see cref="Egg.Applied" />
		/// 对齐到 <see cref="Egg.Enabled" />。
		/// </summary>
		private void Reconcile(Entity eggEntity, ref Egg egg, Reference<PartSimulatorUnmanaged> simulator)
		{
			if (egg.Applied == egg.Enabled)
			{
				return;
			}
			UnityEngine.Debug.Log("[EggSystem] reconcile kind=" + egg.Kind + " ->" + egg.Enabled + " effects=" + m_effects.Length);
			egg.Applied = egg.Enabled;
			NativeList<EggEffect> effects = m_effects;
			if (!egg.Enabled)
			{
				RestoreAll(effects);
				return;
			}
			int component = m_connectedLookup.HasComponent(eggEntity) ? m_connectedLookup[eggEntity].Index : -1;
			switch (egg.Kind)
			{
			case EggKind.Massless:
			{
				int rotation = m_transformLookup.HasComponent(eggEntity) ? m_transformLookup[eggEntity].Rotation : 0;
				if (Egg.IsDiagonal(rotation))
				{
					ApplyMassless(component, simulator, effects);
				}
				else
				{
					ApplyGravityOff(component, simulator, effects);
				}
				break;
			}
			case EggKind.Ghost:
				ApplyGhost(component, simulator, effects);
				break;
			case EggKind.Alien:
				ApplyAlienGlue(component, simulator, effects);
				break;
			}
		}

		/// <summary>失重蛋（正交朝向）：关掉同连通块所有部件的重力。</summary>
		private void ApplyGravityOff(int component, Reference<PartSimulatorUnmanaged> simulator, NativeList<EggEffect> effects)
		{
			NativeList<Entity> parts = simulator.Value.Parts;
			int added = 0;
			for (int i = 0; i < parts.Length; i++)
			{
				Entity part = parts[i];
				// 循环里会 AddComponentData（结构变更），所以不能用缓存的 lookup 判存在性。
				if (!BelongsTo(part, component) || !EntityManager.HasComponent<PhysicsVelocity>(part))
				{
					continue;
				}
				if (EntityManager.HasComponent<PhysicsGravityFactor>(part))
				{
					// 本来就有 PhysicsGravityFactor（非默认值），不改动。
					continue;
				}
				EntityManager.AddComponentData(part, new PhysicsGravityFactor
				{
					Value = 0f
				});
				effects.Add(new EggEffect
				{
					Kind = EggEffectKind.Gravity,
					Target = part
				});
				added++;
			}
			if (added > 0)
			{
				UnityEngine.Debug.Log("[EggSystem] gravityOff comp=" + component + " added=" + added + " parts=" + parts.Length);
				RefreshLookups();
			}
		}

		/// <summary>无质量蛋（斜向朝向）：质量 × 0.001，速度 × √1000 保持动能。</summary>
		private void ApplyMassless(int component, Reference<PartSimulatorUnmanaged> simulator, NativeList<EggEffect> effects)
		{
			NativeList<Entity> parts = simulator.Value.Parts;
			for (int i = 0; i < parts.Length; i++)
			{
				Entity part = parts[i];
				if (!BelongsTo(part, component) || !EntityManager.HasComponent<PhysicsMass>(part))
				{
					continue;
				}
				PhysicsMass mass = EntityManager.GetComponentData<PhysicsMass>(part);
				if (!math.isfinite(mass.InverseMass) || mass.InverseMass <= 0f)
				{
					continue;
				}
				effects.Add(new EggEffect
				{
					Kind = EggEffectKind.Mass,
					Target = part,
					InverseMass = mass.InverseMass,
					InverseInertia = mass.InverseInertia
				});
				mass.InverseMass /= Egg.MasslessFactor;
				mass.InverseInertia /= Egg.MasslessFactor;
				EntityManager.SetComponentData(part, mass);
				ScaleKinetic(part, MasslessSpeedFactor);
			}
		}

		/// <summary>穿透蛋：关掉同连通块所有部件的碰撞体。</summary>
		private void ApplyGhost(int component, Reference<PartSimulatorUnmanaged> simulator, NativeList<EggEffect> effects)
		{
			NativeList<Entity> parts = simulator.Value.Parts;
			int ghostHit = 0;
			for (int i = 0; i < parts.Length; i++)
			{
				Entity part = parts[i];
				if (!BelongsTo(part, component) || !EntityManager.HasComponent<PhysicsCollider>(part))
				{
					continue;
				}
				if (DisableCollider(part, EggEffectKind.Collider, effects))
				{
					ghostHit++;
				}
			}
			if (ghostHit > 0)
			{
				UnityEngine.Debug.Log("[EggSystem] ghost comp=" + component + " parts=" + parts.Length + " hit=" + ghostHit);
			}
		}

		/// <summary>异形胶蛋：同连通块的关节不可断（冲量事件阈值设成 ∞）。</summary>
		private void ApplyAlienGlue(int component, Reference<PartSimulatorUnmanaged> simulator, NativeList<EggEffect> effects)
		{
			NativeList<Entity> joints = simulator.Value.Joints;
			int recordedBefore = effects.Length;
			for (int i = 0; i < joints.Length; i++)
			{
				Entity jointEntity = joints[i];
				if (!EntityManager.Exists(jointEntity) || !EntityManager.HasComponent<PhysicsConstrainedBodyPair>(jointEntity) || !EntityManager.HasComponent<PhysicsJoint>(jointEntity))
				{
					continue;
				}
				PhysicsConstrainedBodyPair pair = EntityManager.GetComponentData<PhysicsConstrainedBodyPair>(jointEntity);
				if (!BelongsTo(pair.EntityA, component) && !BelongsTo(pair.EntityB, component))
				{
					continue;
				}
				PhysicsJoint joint = EntityManager.GetComponentData<PhysicsJoint>(jointEntity);
				int count = joint.GetConstraintCount();
				for (int c = 0; c < count; c++)
				{
					Constraint constraint = joint[c];
					if (!IsBreakable(constraint) || math.any(math.abs(constraint.MaxImpulse) == float.PositiveInfinity))
					{
						continue;
					}
					// BPSS 记的是 joint.breakForce 原值；PhysicsJoint 没有 getter，
					// 所以逐条约束自己记（每根焊接关节最多 3 条非马达约束）。
					effects.Add(new EggEffect
					{
						Kind = EggEffectKind.Joint,
						Target = jointEntity,
						ConstraintIndex = c,
						MaxImpulse = constraint.MaxImpulse
					});
					constraint.MaxImpulse = new float3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
					joint[c] = constraint;
				}
				EntityManager.SetComponentData(jointEntity, joint);
			}
			UnityEngine.Debug.Log("[EggSystem] alienGlue comp=" + component + " joints=" + joints.Length
				+ " recorded=" + (effects.Length - recordedBefore) + " total=" + effects.Length);
		}

		/// <summary>
		/// <c>Constraint.IsNonMotorizedConstraint</c> 是 internal，这里照同样的判据重写一份：
		/// 只有线性/角约束会按 <c>MaxImpulse</c> 抛冲量事件（马达约束的 <c>MaxImpulse</c>
		/// 是每步能施加的最大冲量，语义完全不同，不能一起改成 ∞）。
		/// </summary>
		private static bool IsBreakable(Constraint constraint)
		{
			return constraint.Type == ConstraintType.Linear || constraint.Type == ConstraintType.Angular;
		}

		private bool BelongsTo(Entity part, int component)
		{
			if (component < 0)
			{
				return false;
			}
			// 查本帧开头拍的快照，而不是 m_connectedLookup —— 后者在
			// AddComponentData / RemoveComponent 之后就失效了。
			for (int i = 0; i < m_componentParts.Length; i++)
			{
				if (m_componentParts[i] == part)
				{
					return m_componentIndexes[i] == component;
				}
			}
			return false;
		}

		private void ScaleKinetic(Entity part, float factor)
		{
			// 用 EntityManager 而不是 m_velocityLookup：调用方常常刚做过
			// AddComponentData / RemoveComponent（结构变更），此时缓存的
			// ComponentLookup 已经过期，HasComponent 会返回错误结果。
			if (!EntityManager.Exists(part) || !EntityManager.HasComponent<PhysicsVelocity>(part))
			{
				return;
			}
			PhysicsVelocity velocity = EntityManager.GetComponentData<PhysicsVelocity>(part);
			velocity.Linear *= factor;
			velocity.Angular *= factor;
			EntityManager.SetComponentData(part, velocity);
		}

		/// <summary>把碰撞体的 <c>CollidesWith</c> 清零并记录原过滤条件。</summary>
		private bool DisableCollider(Entity target, EggEffectKind kind, NativeList<EggEffect> effects)
		{
			if (!EntityManager.Exists(target) || !EntityManager.HasComponent<PhysicsCollider>(target))
			{
				return false;
			}
			PhysicsCollider collider = EntityManager.GetComponentData<PhysicsCollider>(target);
			if (!collider.Value.IsCreated)
			{
				return false;
			}
			CollisionFilter filter = collider.Value.Value.GetCollisionFilter();
			if (filter.CollidesWith == 0)
			{
				return false;
			}
			effects.Add(new EggEffect
			{
				Kind = kind,
				Target = target,
				Filter = filter
			});
			// 场景里的部件是从预制体 Instantiate 出来的，PhysicsCollider.Value 是
			// BlobAssetReference（指针），实例与预制体、与所有同类部件共享同一份 blob。
			// 直接 SetCollisionFilter 会把这颗蛋的效果扩散到预制体和别的部件上 ——
			// 实测后果：第一次关掉之后，预制体也被改坏，下一次进场景新蛋一出生
			// 碰撞体就已经是关的，而且它自己记录不到「原过滤条件」，永远还原不回来。
			// 所以先给这个实体克隆一份 blob 再改；克隆体跟实体同寿命（不 Dispose，
			// 因为组件还指着它），每个实体最多克隆一次。
			BlobAssetReference<Unity.Physics.Collider> own = collider.Value.Value.Clone();
			collider.Value = own;
			EntityManager.SetComponentData(target, collider);
			filter.CollidesWith = 0;
			own.Value.SetCollisionFilter(filter);
			return true;
		}

		private void RestoreCollider(Entity target, CollisionFilter filter)
		{
			if (!EntityManager.Exists(target) || !EntityManager.HasComponent<PhysicsCollider>(target))
			{
				return;
			}
			PhysicsCollider collider = EntityManager.GetComponentData<PhysicsCollider>(target);
			if (!collider.Value.IsCreated)
			{
				return;
			}
			collider.Value.Value.SetCollisionFilter(filter);
		}

		/// <summary>关掉效果时把所有改动还原。</summary>
		/// <summary>诊断用：把效果缓冲区打成 " kind->entityIndex" 串。</summary>
		private static string DescribeKinds(NativeList<EggEffect> effects)
		{
			string text = string.Empty;
			for (int i = 0; i < effects.Length; i++)
			{
				text += " " + effects[i].Kind + "->" + effects[i].Target.Index;
			}
			return text;
		}

		private static int CountVoidEffects(NativeList<EggEffect> effects)
		{
			int count = 0;
			for (int i = 0; i < effects.Length; i++)
			{
				if (effects[i].Kind == EggEffectKind.VoidCollider)
				{
					count++;
				}
			}
			return count;
		}

		/// <summary>
		/// 刷新所有缓存的 lookup。
		/// 本系统在同一帧里会对实体做结构变更（<c>AddComponentData</c> / <c>RemoveComponent</c>），
		/// 一旦发生，OnUpdate 开头 Update 过的 ComponentLookup 就全部失效，继续用会抛
		/// <c>ObjectDisposedException: ... invalidated by a structural change</c>；
		/// 更糟的是异常发生在写回 <c>egg.Bound</c> 之前，下一帧会重新 Bind、重新抛，
		/// 于是每帧刷屏（实测把 Editor.log 撑到 71 MB、编辑器卡死）。
		/// 所以每处结构变更之后都要调一次。
		/// </summary>
		private void RefreshLookups()
		{
			m_transformLookup.Update(ref base.CheckedStateRef);
			m_typeLookup.Update(ref base.CheckedStateRef);
			m_worldLookup.Update(ref base.CheckedStateRef);
			m_connectedLookup.Update(ref base.CheckedStateRef);
			m_extensionLookup.Update(ref base.CheckedStateRef);
			m_velocityLookup.Update(ref base.CheckedStateRef);
			m_massLookup.Update(ref base.CheckedStateRef);
			m_colliderLookup.Update(ref base.CheckedStateRef);
			m_gravityLookup.Update(ref base.CheckedStateRef);
			m_pairLookup.Update(ref base.CheckedStateRef);
			m_jointLookup.Update(ref base.CheckedStateRef);
		}

		private void RestoreAll(NativeList<EggEffect> effects)
		{
			UnityEngine.Debug.Log("[EggSystem] restoreAll count=" + effects.Length + DescribeKinds(effects));
			for (int i = effects.Length - 1; i >= 0; i--)
			{
				EggEffect effect = effects[i];
				switch (effect.Kind)
				{
				case EggEffectKind.Collider:
				case EggEffectKind.VoidCollider:
					RestoreCollider(effect.Target, effect.Filter);
					break;
				case EggEffectKind.Gravity:
					// EntityManager 而非 m_gravityLookup：本轮已经对别的实体做过
					// RemoveComponent，缓存的 lookup 过期会把「组件还在」误判成「已没了」，
					// 于是第二个部件的重力永远关不回来（沙袋式的静默失败）。
					if (EntityManager.Exists(effect.Target) && EntityManager.HasComponent<PhysicsGravityFactor>(effect.Target))
					{
						EntityManager.RemoveComponent<PhysicsGravityFactor>(effect.Target);
					}
					break;
				case EggEffectKind.Mass:
					if (EntityManager.Exists(effect.Target) && EntityManager.HasComponent<PhysicsMass>(effect.Target))
					{
						PhysicsMass mass = EntityManager.GetComponentData<PhysicsMass>(effect.Target);
						mass.InverseMass = effect.InverseMass;
						mass.InverseInertia = effect.InverseInertia;
						EntityManager.SetComponentData(effect.Target, mass);
						ScaleKinetic(effect.Target, 1f / MasslessSpeedFactor);
					}
					break;
				case EggEffectKind.Joint:
					if (EntityManager.Exists(effect.Target) && EntityManager.HasComponent<PhysicsJoint>(effect.Target))
					{
						PhysicsJoint joint = EntityManager.GetComponentData<PhysicsJoint>(effect.Target);
						if (effect.ConstraintIndex >= 0 && effect.ConstraintIndex < joint.GetConstraintCount())
						{
							Constraint constraint = joint[effect.ConstraintIndex];
							constraint.MaxImpulse = effect.MaxImpulse;
							joint[effect.ConstraintIndex] = constraint;
							EntityManager.SetComponentData(effect.Target, joint);
						}
					}
					break;
				}
			}
			effects.Clear();
			// 上面可能有 RemoveComponent<PhysicsGravityFactor>（结构变更）。
			RefreshLookups();
		}
	}
}
// hot-compile probe
