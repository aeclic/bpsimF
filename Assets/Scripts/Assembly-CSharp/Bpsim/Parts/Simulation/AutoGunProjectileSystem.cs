using Bpsim.Parts.Gameplay;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
// UnityEngine 与 Unity.Physics 都有 RaycastHit，这里固定用物理世界的那个。
using RaycastHit = Unity.Physics.RaycastHit;

namespace Bpsim.Parts.Simulation
{
	/// <summary>
	/// 手动机炮（吸盘枪索引 5）与自动机炮（索引 6）共用的弹丸。
	/// </summary>
	/// <remarks>
	/// <para>
	/// BPSS 侧弹丸是 <c>ExplodingGrapplingHookProjectile</c>（MonoBehaviour + 刚体 + 协程）：
	/// 出生时沿机炮正前方 <c>AddForceAtPosition(m_force * GunProjectileSpeed, Impulse)</c>，
	/// 撞到东西就 <c>Explode()</c>，没撞到则 TTL 到点自爆。这里把同一份状态拆成
	/// 「数据组件 + 系统」：位置/速度/TTL/爆炸参数全在组件里，
	/// <see cref="AutoGunProjectileSystem" /> 每帧积分、用
	/// <see cref="CollisionWorld.CastRay" /> 做连续碰撞（弹速 20 m/s，逐帧点检测会穿模），
	/// 命中或超时就按 BPSS <c>AddExplosionForce</c> 的语义施加径向冲量并销毁实体。
	/// </para>
	/// <para>
	/// 弹丸<b>不带碰撞体</b>：它不参与物理求解，靠射线扫掠判定命中，因此既不会把载具顶开，
	/// 也不会被焊接关节当成刚体。视觉按 BPSS 的青色 <c>"&gt;"</c> 标记的语义来做
	/// 每个显示帧由 <see cref="AutoGunProjectilePresentationSystem"/> 绘制共用的箭头网格。
	/// </para>
	/// </remarks>
	public struct AutoGunProjectile : IComponentData
	{
		/// <summary>开火的机炮部件实体（爆炸时作为「不伤自己」的豁免对象）。</summary>
		public Entity Owner;

		/// <summary>弹丸当前位置（世界坐标，Z 恒为 0）。</summary>
		public float3 Position;

		/// <summary>当前速度（米/秒）。</summary>
		public float3 Velocity;

		/// <summary>已存活时间（秒）。</summary>
		public float Age;

		/// <summary>寿命（秒）：到点无论是否命中都自爆（BPSS <c>m_ttl</c>）。</summary>
		public float Ttl;

		/// <summary>爆炸半径（格）。</summary>
		public float Radius;

		/// <summary>爆炸在半径边缘的速度增量（米/秒），向中心线性放大。</summary>
		public float Strength;

		/// <summary>是否受重力影响；BPRC 手动机炮的 Hook_6 配置为 false。</summary>
		public bool Gravity;

		/// <summary>Use BPRC's impulse / max(distance, 1)^1.5 with body mass scaling.</summary>
		public bool ImpulseFalloff;
	}

	/// <summary>
	/// 机炮弹丸的积分、碰撞判定与爆炸结算。
	/// </summary>
	/// <remarks>固定物理步只更新弹丸；绘制在 PresentationSystemGroup 中逐显示帧执行。</remarks>
	[UpdateInGroup(typeof(PartSimulationSystemGroup))]
	internal partial class AutoGunProjectileSystem : SystemBase
	{
		private EntityQuery m_projectileQuery;

		private BufferLookup<LinkedEntityGroup> m_groupLookup;

		/// <summary>用来判断命中的部件是否与机炮同属一台载具（同一个连通块）。</summary>
		private ComponentLookup<PartConnectedComponent> m_componentLookup;

		/// <summary>扫掠用的命中缓冲：每帧复用，避免每发弹丸新建一个 NativeList。</summary>
		private NativeList<RaycastHit> m_hits;

		protected override void OnCreate()
		{
			EntityQueryBuilder builder = new EntityQueryBuilder(Allocator.Temp);
			builder = builder.WithAllRW<AutoGunProjectile>();
			m_projectileQuery = builder.Build(ref base.CheckedStateRef);
			m_groupLookup = base.CheckedStateRef.GetBufferLookup<LinkedEntityGroup>(isReadOnly: true);
			m_componentLookup = base.CheckedStateRef.GetComponentLookup<PartConnectedComponent>(isReadOnly: true);
			m_hits = new NativeList<RaycastHit>(16, Allocator.Persistent);
		}

		protected override void OnUpdate()
		{
			PartManager manager = PartManager.Instance;
			if (manager == null || !manager.IsSimulating || manager.PartSimulator == null)
			{
				return;
			}
			float deltaTime = World.Time.DeltaTime;
			if (deltaTime <= 0f)
			{
				return;
			}
			m_groupLookup.Update(ref base.CheckedStateRef);
			// 每帧刷新：其它系统（爆炸销毁弹丸等）会造成结构变化，OnCreate 缓存的
			// lookup 会失效并抛 ObjectDisposedException。
			m_componentLookup.Update(ref base.CheckedStateRef);
			SimulationSettings simulationSettings = UserSettings.Instance.SimulationSettings;
			float3 gravity = new float3(simulationSettings.GravityX, simulationSettings.GravityY, 0f);
			bool hasCollisionWorld = SystemAPI.TryGetSingleton(out PhysicsWorldSingleton physicsWorld);
			CollisionWorld collisionWorld = (hasCollisionWorld ? physicsWorld.CollisionWorld : default(CollisionWorld));
			using NativeArray<Entity> entities = m_projectileQuery.ToEntityArray(Allocator.Temp);
			for (int i = 0; i < entities.Length; i++)
			{
				Entity entity = entities[i];
				if (!EntityManager.Exists(entity))
				{
					continue;
				}
				// 上一发弹丸爆炸时会 DestroyEntity（结构变化），lookup 随之失效，必须重新取。
				m_groupLookup.Update(ref base.CheckedStateRef);
				m_componentLookup.Update(ref base.CheckedStateRef);
				AutoGunProjectile projectile = EntityManager.GetComponentData<AutoGunProjectile>(entity);
				if (projectile.Gravity)
				{
					projectile.Velocity += gravity * deltaTime;
				}
				float3 previous = projectile.Position;
				projectile.Position += projectile.Velocity * deltaTime;
				projectile.Age += deltaTime;
				bool explode;
				float3 impact;
				if (hasCollisionWorld && Sweep(projectile.Owner, previous, projectile.Position, collisionWorld, out RaycastHit hit))
				{
					explode = true;
					impact = hit.Position;
				}
				else if (projectile.Age >= projectile.Ttl)
				{
					explode = true;
					impact = projectile.Position;
				}
				else
				{
					explode = false;
					impact = default(float3);
				}
				if (explode)
				{
					Explode(entity, projectile, impact);
					continue;
				}
				EntityManager.SetComponentData(entity, projectile);
			}
		}

		/// <summary>
		/// 从 <paramref name="from" /> 扫到 <paramref name="to" />，返回第一个「不属于机炮自己」的命中。
		/// </summary>
		/// <remarks>
		/// 机炮是普通部件，根实体带着合并后的复合碰撞体，而弹丸出生点就在它的包围盒边上，
		/// 所以必须把整台机炮（根 + 全部子物体）都排除，否则第一帧就会命中自己。
		/// </remarks>
		private bool Sweep(Entity owner, float3 from, float3 to, CollisionWorld collisionWorld, out RaycastHit hit)
		{
			hit = default(RaycastHit);
			if (math.lengthsq(to - from) <= 1E-08f)
			{
				return false;
			}
			RaycastInput input = new RaycastInput
			{
				Start = from,
				End = to,
				Filter = CollisionFilter.Default
			};
			NativeList<RaycastHit> hits = m_hits;
			hits.Clear();
			AllHitsCollector<RaycastHit> collector = new AllHitsCollector<RaycastHit>(1f, ref hits);
			if (!collisionWorld.CastRay(input, ref collector))
			{
				return false;
			}
			float bestFraction = float.MaxValue;
			bool found = false;
			for (int i = 0; i < hits.Length; i++)
			{
				RaycastHit candidate = hits[i];
				if (candidate.Fraction >= bestFraction || BelongsToPart(candidate.Entity, owner))
				{
					continue;
				}
				bestFraction = candidate.Fraction;
				hit = candidate;
				found = true;
			}
			return found;
		}

		/// <summary>命中的实体是否属于给定部件（根或任一子物体），或与它同属一个连通块。</summary>
		/// <remarks>
		/// 除了机炮自身，还要跳过<b>同一台载具</b>上的其它部件：机炮通常挂在车体上，
		/// 炮口前方就是一块木框，若只排除机炮自己的子物体，弹丸会在炮口立刻爆炸，
		/// 玩家看到的就是「点了没反应」。BPSS 里弹丸只 <c>Physics.IgnoreCollision</c>
		/// 机炮本体，但那里弹丸是沿 <c>transform.right</c> 从车体表面外侧射出的，
		/// 而本工程的部件共享一个复合碰撞体、出生点必然贴着车体，所以按连通块排除更稳。
		/// </remarks>
		private bool BelongsToPart(Entity candidate, Entity root)
		{
			if (candidate == root)
			{
				return true;
			}
			// 巨构优化：整车只有一个刚体，部件本身不再是物理 body，
			// 射线/扫掠命中的是整车刚体实体，必须当成同一条车处理，否则弹丸会打自己车体。
			if (MegaStructure.IsVehicleBody(candidate))
			{
				return true;
			}
			if (m_groupLookup.HasBuffer(root))
			{
				DynamicBuffer<LinkedEntityGroup> group = m_groupLookup[root];
				for (int i = 0; i < group.Length; i++)
				{
					if (group[i].Value == candidate)
					{
						return true;
					}
				}
			}
			return SameConnectedComponent(candidate, root);
		}

		/// <summary>两个部件是否属于同一个连通块（同一条 <c>PartConnectedComponent.Index</c>）。</summary>
		private bool SameConnectedComponent(Entity a, Entity b)
		{
			if (!m_componentLookup.HasComponent(a) || !m_componentLookup.HasComponent(b))
			{
				return false;
			}
			int indexA = m_componentLookup[a].Index;
			int indexB = m_componentLookup[b].Index;
			return indexA >= 0 && indexA == indexB;
		}

		/// <summary>QA 探针：累计爆炸次数。</summary>
		internal static int ExplosionCount;

		/// <summary>QA 探针：最近一次爆炸的中心。</summary>
		internal static float3 LastExplosion;

		/// <summary>QA 探针：累计提交的弹丸绘制次数（用来确认子弹真的被画出来，而不是「生成了看不见」）。</summary>
		internal static int DrawnProjectiles;

		/// <summary>QA 探针：累计提交的爆炸光环绘制次数。</summary>
		internal static int DrawnFlashes;

		/// <summary>QA 探针：把计数清零（每个 QA 场景开始时调用）。</summary>
		internal static void ResetProbes()
		{
			ExplosionCount = 0;
			LastExplosion = default(float3);
			AutoGunProjectileSpawner.SpawnCount = 0;
			DrawnProjectiles = 0;
			DrawnFlashes = 0;
		}

		/// <summary>爆炸结算：径向冲量 + 光环，然后销毁弹丸。</summary>
		private void Explode(Entity entity, AutoGunProjectile projectile, float3 center)
		{
			if (projectile.ImpulseFalloff)
			{
				ApplyBprcExplosion(projectile, center);
			}
			else
			{
				PartGameplayManager.ApplyRadialImpulse(center, projectile.Radius, projectile.Strength, projectile.Owner, Entity.Null);
			}
			BpsimExplosionVisual.Spawn(center);
			TNTBehavior.FromProjectile(center, projectile.Radius);
			ExplosionCount++;
			LastExplosion = center;
			// 命中统计：爆炸范围内确实有别的部件才算命中（原版是 OnCollisionEnter 触发，
			// 这里改成「炸到了东西」，把 TTL 自爆且空爆的情况排除在 hits 之外）。
			PartGameplayManager manager = PartGameplayManager.Instance;
			if (manager != null && HasPartsInRadius(center, projectile.Radius, projectile.Owner) && manager.Find(projectile.Owner) is AutoGunBehavior gun)
			{
				gun.ReportHit();
			}
			EntityManager.DestroyEntity(entity);
		}

		private void ApplyBprcExplosion(in AutoGunProjectile projectile, float3 center)
		{
			PartSimulatorUnmanaged simulator = PartManager.Instance.PartSimulator.Unmanaged;
			for (int i = 0; i < simulator.Parts.Length; i++)
			{
				Entity part = simulator.Parts[i];
				if (!PartGameplayUtil.Alive(part) || !EntityManager.HasComponent<PhysicsMass>(part))
				{
					continue;
				}
				float3 delta = PartGameplayUtil.Position(part) - center;
				float distance = math.length(delta);
				float inverseMass = EntityManager.GetComponentData<PhysicsMass>(part).InverseMass;
				if (distance > projectile.Radius || distance < 1E-05f || inverseMass <= 0f)
				{
					continue;
				}
				float mass = 1f / inverseMass;
				float massScale = mass < 0.1f ? mass : (mass < 0.4f ? mass / 0.4f : 1f);
				float impulse = projectile.Strength / math.pow(math.max(distance, 1f), 1.5f);
				PartGameplayUtil.AddVelocity(part, delta / distance * (impulse * massScale * inverseMass));
			}
		}

		/// <summary>爆炸半径内是否有别的部件（不含机炮自己）。</summary>
		private static bool HasPartsInRadius(float3 center, float radius, Entity owner)
		{
			PartSimulatorUnmanaged simulator = PartManager.Instance.PartSimulator.Unmanaged;
			for (int i = 0; i < simulator.Parts.Length; i++)
			{
				Entity part = simulator.Parts[i];
				if (part == owner || !PartGameplayUtil.Alive(part))
				{
					continue;
				}
				if (math.distance(PartGameplayUtil.Position(part), center) <= radius)
				{
					return true;
				}
			}
			return false;
		}

		protected override void OnDestroy()
		{
			if (m_hits.IsCreated)
			{
				m_hits.Dispose();
			}
		}
	}

	/// <summary>手动机炮与自动机炮共用的弹丸生成入口。</summary>
	public static class AutoGunProjectileSpawner
	{
		/// <summary>BPSS <c>Hook_6.prefab</c> 上的 <c>m_ttl = 1</c>。</summary>
		public const float DefaultTtl = 1f;

		/// <summary>
		/// 爆炸半径：BPSS 是 <c>m_explosionRadius = 8</c>，但它按
		/// <c>AddExplosionForce(impulse / f^1.5, Impulse)</c> 除以质量结算；bpsim 的
		/// <see cref="PartGameplayManager.ApplyRadialImpulse" /> 直接叠加速度，8 格会把整台载具
		/// 掀飞，所以取与 TNT 同量级的 3.2 格。
		/// </summary>
		public const float DefaultRadius = 3.2f;

		/// <summary>边缘速度增量（米/秒），中心处线性放大到该值。</summary>
		public const float DefaultStrength = 14f;

		/// <summary>弹丸初速（米/秒）。BPSS 是 <c>m_force = 20</c> 的冲量，按 1 kg 折合 20 m/s。</summary>
		public const float DefaultSpeed = 20f;

		/// <summary>QA 探针：累计生成的弹丸数。</summary>
		internal static int SpawnCount;

		/// <summary>生成一发弹丸实体。返回 <see cref="Entity.Null" /> 表示当前世界没有 ECS 环境。</summary>
		public static Entity Spawn(Entity owner, float3 origin, float3 direction, float ttl = DefaultTtl, float radius = DefaultRadius, float strength = DefaultStrength, float speed = DefaultSpeed, bool gravity = true, bool inheritVelocity = true, bool impulseFalloff = false)
		{
			EntityManager em = PartGameplayUtil.Em;
			if (!em.Exists(owner))
			{
				return Entity.Null;
			}
			float3 forward = ((math.lengthsq(direction) > 1E-08f) ? math.normalize(direction) : new float3(1f, 0f, 0f));
			Entity entity = em.CreateEntity();
			SpawnCount++;
			em.AddComponentData(entity, new AutoGunProjectile
			{
				Owner = owner,
				Position = origin,
				Velocity = forward * speed + (inheritVelocity ? PartGameplayUtil.LinearVelocity(owner) : float3.zero),
				Age = 0f,
				Ttl = ttl,
				Radius = radius,
				Strength = strength,
				Gravity = gravity,
				ImpulseFalloff = impulseFalloff
			});
			return entity;
		}
	}
}
