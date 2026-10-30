using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

namespace Bpsim.Parts.Simulation
{
	/// <summary>
	/// 标记：该部件的 <see cref="PhysicsCollider"/> blob 归模拟独占。
	/// <para>
	/// 只有独占 blob 才能安全地按连通块改写 <see cref="CollisionFilter"/>：<c>Instantiate</c> 出来的
	/// 部件共用 prefab 的 <c>BlobAssetReference&lt;Collider&gt;</c>（实测 1648 个部件只有 25 份 blob），
	/// 而过滤是存在 blob 里的，直接改会连累同类型的所有部件。见 <see cref="PartCollisionFilter.IsolateColliders"/>。
	/// </para>
	/// </summary>
	public struct PartIsolatedCollider : IComponentData
	{
	}

	/// <summary>
	/// 同一个焊接连通块里的部件之间不需要碰撞：它们已经被 fixed joint 焊死，
	/// 唯一会「碰」到彼此的是 Unity.Physics 为防穿透而按速度膨胀 AABB 生成的推测接触
	/// （<c>MotionVelocity.CalculateExpansion</c>：Linear = 线速度 * 步长）。
	/// 1648 个部件的载具高速旋转时切向线速度可达几百，AABB 每帧被膨胀好几个格子，
	/// 位置相近的部件两两重叠，宽相位对数和推测接触数量一起爆炸 ——
	/// 这就是「载具转得越快越卡」的直接来源（实测 ω=16 时 20~70ms/帧，关掉部件间碰撞后 5ms）。
	///
	/// <para>
	/// Unity.Physics 的 <see cref="CollisionFilter"/> 只有 32 位，这里把位 0 留给地形/关卡/道具，
	/// 位 1..30 分配给连通块（按 <c>componentIndex % 30</c> 取模）：部件只与「自己那块位之外」的东西碰撞。
	/// 同一连通块内的部件不再互相生成碰撞对；不同连通块（被拉断后的碎片）照常互撞。
	/// </para>
	/// </summary>
	public static class PartCollisionFilter
	{
		/// <summary>位 0 保留给地形、关卡几何体、道具（它们仍用 <see cref="CollisionFilter.Default"/>）。</summary>
		public const uint TerrainCategory = 1u;

		/// <summary>可用作连通块类别的位数（位 1..30）。</summary>
		public const int CategoryCount = 30;

		/// <summary>按连通块索引算出该部件应使用的碰撞过滤。</summary>
		public static CollisionFilter FilterFor(int componentIndex)
		{
			if (componentIndex >= 0)
			{
				// 取模而不是「超范围就退化成全碰」：连通块数量经常上百（载具被拉断后），
				// 退化成全碰会让断开的碎片重新按速度膨胀 AABB、把卡顿带回来。
				// 取模的代价是「不同连通块但落到同一类别」的碎片之间不再碰撞，
				// 这对高速碎片的观感影响很小，但保住了「同一连通块内部永不互碰」这条硬保证。
				uint belongsTo = 1u << (componentIndex % CategoryCount + 1);
				return new CollisionFilter
				{
					BelongsTo = belongsTo,
					CollidesWith = ~belongsTo,
					GroupIndex = 0
				};
			}
			return new CollisionFilter
			{
				BelongsTo = TerrainCategory,
				CollidesWith = uint.MaxValue,
				GroupIndex = 0
			};
		}

		/// <summary>
		/// 给每个模拟部件准备一份**独占**的碰撞体 blob，返回新建的数量。
		/// <para>
		/// 共享 blob（同一 prefab 实例化出来的部件）只克隆一次不划算 —— 逐部件克隆后
		/// <see cref="Unity.Physics.Collider.SetCollisionFilter"/> 才只影响自己。
		/// 已经独占的（例如按自由缩放重建过的）只打标记，不重复分配。
		/// </para>
		/// </summary>
		public static unsafe int IsolateColliders(EntityManager entityManager, PartSimulatorUnmanaged simulator)
		{
			NativeList<Entity> parts = simulator.Parts;
			Dictionary<IntPtr, int> references = new Dictionary<IntPtr, int>(parts.Length);
			for (int i = 0; i < parts.Length; i++)
			{
				Entity entity = parts[i];
				if (entity == Entity.Null || !entityManager.Exists(entity) || !entityManager.HasComponent<PhysicsCollider>(entity))
				{
					continue;
				}
				PhysicsCollider physicsCollider = entityManager.GetComponentData<PhysicsCollider>(entity);
				if (!physicsCollider.Value.IsCreated)
				{
					continue;
				}
				IntPtr key = (IntPtr)physicsCollider.Value.GetUnsafePtr();
				references.TryGetValue(key, out int count);
				references[key] = count + 1;
			}
			int created = 0;
			for (int i = 0; i < parts.Length; i++)
			{
				Entity entity = parts[i];
				if (entity == Entity.Null || !entityManager.Exists(entity) || !entityManager.HasComponent<PhysicsCollider>(entity) || entityManager.HasComponent<PartIsolatedCollider>(entity))
				{
					continue;
				}
				PhysicsCollider physicsCollider = entityManager.GetComponentData<PhysicsCollider>(entity);
				if (!physicsCollider.Value.IsCreated)
				{
					continue;
				}
				IntPtr key = (IntPtr)physicsCollider.Value.GetUnsafePtr();
				if (references[key] > 1)
				{
					BlobAssetReference<Unity.Physics.Collider> isolated = PartPhysics.CreateScaled(physicsCollider.Value, new float2(1f, 1f));
					if (!isolated.IsCreated)
					{
						// 形状暂不支持重建（例如 MeshCollider）：保留共享碰撞体，也就不给它分类别，
						// 否则会改写同 blob 的所有部件。
						continue;
					}
					entityManager.SetComponentData(entity, new PhysicsCollider
					{
						Value = isolated
					});
					simulator.TrackScaledCollider(isolated);
					created++;
				}
				entityManager.AddComponent<PartIsolatedCollider>(entity);
			}
			return created;
		}

		/// <summary>把过滤写回所有模拟部件，返回真正改动的数量。</summary>
		public static int Apply(EntityManager entityManager, PartSimulatorUnmanaged simulator)
		{
			NativeList<Entity> parts = simulator.Parts;
			int changed = 0;
			for (int i = 0; i < parts.Length; i++)
			{
				Entity entity = parts[i];
				if (entity == Entity.Null || !entityManager.Exists(entity))
				{
					continue;
				}
				if (!entityManager.HasComponent<PhysicsCollider>(entity) || !entityManager.HasComponent<PartConnectedComponent>(entity))
				{
					continue;
				}
				if (!entityManager.HasComponent<PartIsolatedCollider>(entity))
				{
					// 共享 blob：改过滤会连累同 prefab 的所有部件，宁可让它按旧行为碰撞。
					continue;
				}
				PhysicsCollider physicsCollider = entityManager.GetComponentData<PhysicsCollider>(entity);
				if (!physicsCollider.Value.IsCreated)
				{
					continue;
				}
				int componentIndex = entityManager.GetComponentData<PartConnectedComponent>(entity).Index;
				CollisionFilter filter = FilterFor(componentIndex);
				ref Unity.Physics.Collider collider = ref physicsCollider.Value.Value;
				CollisionFilter current = collider.GetCollisionFilter();
				if (current.BelongsTo == filter.BelongsTo && current.CollidesWith == filter.CollidesWith)
				{
					continue;
				}
				// CompoundCollider 的实现会把过滤同时写到根和所有子碰撞体上。
				collider.SetCollisionFilter(filter);
				changed++;
			}
			return changed;
		}

		/// <summary>
		/// 在 job 里（Burst）按连通块刷新碰撞过滤。关节被拉断后连通块会重新切分，
		/// 断开的碎片必须恢复互相碰撞，所以这次刷新紧跟在连通块重算之后。
		/// 索引直接用重算时算出的那一份，避免在同一个 job 里把刚写进组件的值再从
		/// ComponentLookup 读回来（读写可见性在 Burst 下不保证）。
		/// 只处理独占 blob 的部件（<see cref="PartIsolatedCollider"/>），共享 blob 一律不动。
		/// </summary>
		public static void ApplyInJob(NativeList<Entity> parts, NativeArray<int> componentIndexes, ref ComponentLookup<PhysicsCollider> colliderLookup, ref ComponentLookup<PartIsolatedCollider> isolatedLookup)
		{
			for (int i = 0; i < parts.Length; i++)
			{
				Entity entity = parts[i];
				if (entity == Entity.Null || !colliderLookup.HasComponent(entity))
				{
					continue;
				}
				if (!isolatedLookup.HasComponent(entity))
				{
					continue;
				}
				PhysicsCollider physicsCollider = colliderLookup[entity];
				if (!physicsCollider.Value.IsCreated)
				{
					continue;
				}
				CollisionFilter filter = FilterFor(componentIndexes[i]);
				ref Unity.Physics.Collider collider = ref physicsCollider.Value.Value;
				CollisionFilter current = collider.GetCollisionFilter();
				if (current.BelongsTo == filter.BelongsTo && current.CollidesWith == filter.CollidesWith)
				{
					continue;
				}
				collider.SetCollisionFilter(filter);
			}
		}

		/// <summary>统计「碰撞过滤与连通块不一致」的部件数，用来验证断链后过滤是否及时刷新。</summary>
		public static int CountMismatches(EntityManager entityManager, PartSimulatorUnmanaged simulator)
		{
			NativeList<Entity> parts = simulator.Parts;
			int mismatches = 0;
			for (int i = 0; i < parts.Length; i++)
			{
				Entity entity = parts[i];
				if (entity == Entity.Null || !entityManager.Exists(entity))
				{
					continue;
				}
				if (!entityManager.HasComponent<PhysicsCollider>(entity) || !entityManager.HasComponent<PartConnectedComponent>(entity))
				{
					continue;
				}
				if (!entityManager.HasComponent<PartIsolatedCollider>(entity))
				{
					continue;
				}
				PhysicsCollider physicsCollider = entityManager.GetComponentData<PhysicsCollider>(entity);
				if (!physicsCollider.Value.IsCreated)
				{
					continue;
				}
				int componentIndex = entityManager.GetComponentData<PartConnectedComponent>(entity).Index;
				CollisionFilter expected = FilterFor(componentIndex);
				CollisionFilter current = physicsCollider.Value.Value.GetCollisionFilter();
				if (current.BelongsTo != expected.BelongsTo || current.CollidesWith != expected.CollidesWith)
				{
					mismatches++;
				}
			}
			return mismatches;
		}

		/// <summary>诊断用：统计部件 / collider / 连通块组件的覆盖情况和过滤类别分布。</summary>
		public static string Describe(EntityManager entityManager, PartSimulatorUnmanaged simulator)
		{
			NativeList<Entity> parts = simulator.Parts;
			int existing = 0;
			int withCollider = 0;
			int withComponent = 0;
			int uncreatedCollider = 0;
			int isolated = 0;
			int multiBit = 0;
			int noBit = 0;
			int[] perCategory = new int[CategoryCount + 1];
			for (int i = 0; i < parts.Length; i++)
			{
				Entity entity = parts[i];
				if (entity == Entity.Null || !entityManager.Exists(entity))
				{
					continue;
				}
				existing++;
				if (entityManager.HasComponent<PartConnectedComponent>(entity))
				{
					withComponent++;
				}
				if (entityManager.HasComponent<PartIsolatedCollider>(entity))
				{
					isolated++;
				}
				if (!entityManager.HasComponent<PhysicsCollider>(entity))
				{
					continue;
				}
				withCollider++;
				PhysicsCollider physicsCollider = entityManager.GetComponentData<PhysicsCollider>(entity);
				if (!physicsCollider.Value.IsCreated)
				{
					uncreatedCollider++;
					continue;
				}
				uint belongsTo = physicsCollider.Value.Value.GetCollisionFilter().BelongsTo;
				int bit = -1;
				int bitCount = 0;
				for (int k = 0; k < 32; k++)
				{
					if ((belongsTo & (1u << k)) != 0)
					{
						bit = k;
						bitCount++;
					}
				}
				if (bitCount == 0)
				{
					noBit++;
				}
				else if (bitCount > 1)
				{
					multiBit++;
				}
				else if (bit >= 0 && bit <= CategoryCount)
				{
					perCategory[bit]++;
				}
			}
			string histogram = "";
			int nonEmpty = 0;
			for (int k = 0; k <= CategoryCount; k++)
			{
				if (perCategory[k] > 0)
				{
					nonEmpty++;
					if (nonEmpty <= 8)
					{
						histogram += $" bit{k}={perCategory[k]}";
					}
				}
			}
			return $"coverage: parts={parts.Length} existing={existing} collider={withCollider} component={withComponent} isolated={isolated} uncreatedCollider={uncreatedCollider} filters nonEmptyCategories={nonEmpty} multiBit={multiBit} zeroBit={noBit}{histogram}";
		}

		/// <summary>把过滤恢复成默认值（部件与所有东西碰撞），只用于对照测试。</summary>
		public static int Reset(EntityManager entityManager, PartSimulatorUnmanaged simulator)
		{
			NativeList<Entity> parts = simulator.Parts;
			int changed = 0;
			for (int i = 0; i < parts.Length; i++)
			{
				Entity entity = parts[i];
				if (entity == Entity.Null || !entityManager.Exists(entity) || !entityManager.HasComponent<PhysicsCollider>(entity) || !entityManager.HasComponent<PartIsolatedCollider>(entity))
				{
					continue;
				}
				PhysicsCollider physicsCollider = entityManager.GetComponentData<PhysicsCollider>(entity);
				if (!physicsCollider.Value.IsCreated)
				{
					continue;
				}
				ref Unity.Physics.Collider collider = ref physicsCollider.Value.Value;
				CollisionFilter current = collider.GetCollisionFilter();
				if (current.BelongsTo == CollisionFilter.Default.BelongsTo && current.CollidesWith == CollisionFilter.Default.CollidesWith)
				{
					continue;
				}
				collider.SetCollisionFilter(CollisionFilter.Default);
				changed++;
			}
			return changed;
		}
	}
}
