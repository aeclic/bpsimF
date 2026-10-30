using Bpsim.Collections;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Bpsim.Parts.Simulation
{
	/// <summary>
	/// 燃料系统，对应 BPSS 901-17.0 的 <c>FuelSystem.cs</c>。
	/// </summary>
	/// <remarks>
	/// <para>
	/// BPSS 的做法是：把「燃料箱 / 燃料管 / 耗油件」挑出来，用并查集按「管子的邻居规则」
	/// 聚成若干燃料连通组；每步先汇总每组的总需求与总可供给量，再按
	/// <c>min(可供给 / 需求, 1)</c> 的比例把油分给每个耗油件、按同样比例从每个燃料箱抽走，
	/// 最后让同一焊接组件里的 <c>Engine</c> 部件给燃料箱缓慢回油。
	/// </para>
	/// <para>
	/// 这里保持完全相同的算法，但按 ECS 的方式拆成两段：
	/// </para>
	/// <list type="bullet">
	/// <item><description>
	/// <b>拓扑重建</b>（管理侧，低频）：只在零件增减、阀门开关、或每隔一段时间跑一次，
	/// 用 <see cref="NativeDisjointSet"/> 建组、把同组成员在 <c>NativeList&lt;Entity&gt;</c>
	/// 里排成连续区间，并写好每组的起止下标。这样每步的分油作业可以按组并行，
	/// 组之间没有任何共享写入。
	/// </description></item>
	/// <item><description>
	/// <b>分油</b>（Burst 作业，每步）：<see cref="DistributeJob"/> 以「组」为并行单位，
	/// 每组只碰自己的成员，因此不需要任何锁或原子加。
	/// </description></item>
	/// </list>
	/// <para>
	/// 与 BPSS 的差异只有两处，都是 bpsim 没有对应概念造成的：
	/// ① BPSS 用 <c>Contraption.FindPartJoints</c> + <c>m_enclosedPart</c> 判断燃料箱连着谁，
	/// 这里换成「同一焊接组件 + 网格四邻域（装着燃料箱的框按框的格子算）」；
	/// ② BPSS 燃料管的自动型号会按四个邻居换 sprite，这里交给
	/// <c>PartGameplayUtil.SetSprite</c> + <c>FuelTubeSystem</c>（见 PartBehaviorsFuel.cs）。
	/// </para>
	/// </remarks>
	[UpdateInGroup(typeof(PartSimulationSystemGroup))]
	internal partial class FuelSystem : SystemBase
	{
		/// <summary>BPSS <c>FuelBox.SupplyFuelAmount</c>：<c>min(amount, 1 * fixedDeltaTime)</c>。</summary>
		private const float SupplyRate = 1f;

		/// <summary>BPSS <c>FuelBox.RefuelingAmount</c>：<c>0.5f * fixedDeltaTime</c>。</summary>
		private const float RefuelRate = 0.5f;

		/// <summary>BPSS <c>FuelSystem.FixedUpdate</c>：每个 <c>Engine</c> 部件给同焊接组件 <c>+0.2 * dt</c>。</summary>
		private const float EngineRefuelRate = 0.2f;

		/// <summary>拓扑重建的最长间隔（秒）。焊接关系变化不一定会改动零件数量，靠这个兜底。</summary>
		private const float RebuildInterval = 2f;

		/// <summary>
		/// 分油作业：一个组一个并行槽，组内串行汇总 + 串行分配。
		/// </summary>
		[BurstCompile]
		private struct DistributeJob : IJobParallelFor
		{
			[ReadOnly]
			public NativeArray<Entity> Members;

			/// <summary>组 g 的成员是 <c>[GroupStart[g], GroupStart[g + 1])</c>。</summary>
			[ReadOnly]
			public NativeArray<int> GroupStart;

			[ReadOnly]
			public ComponentLookup<PartConnectedComponent> ConnectedLookup;

			/// <summary>焊接组件 → 该组件里的 Engine 部件数量（回油用）。</summary>
			[ReadOnly]
			public NativeParallelHashMap<int, int> EngineCount;

			/// <summary>焊接组件 → 该组件里的燃料箱数量（回油平摊用）。</summary>
			[ReadOnly]
			public NativeParallelHashMap<int, int> ContainerCount;

			// 并行作业里要写 ComponentLookup / NativeArray，必须显式关掉
			// "每个 i 只能碰自己那份" 的静态检查，否则调度时直接抛
			// InvalidOperationException: ... is not declared [ReadOnly] in a IJobParallelFor job。
			// 这里安全的前提是：组成员按组连续存放，每个 group 只写自己那一组的实体与 States[group]，
			// 而一个实体只会属于一个组（Rebuild 里的并查集分组）。
			[NativeDisableParallelForRestriction]
			public ComponentLookup<FuelConsumer> ConsumerLookup;

			[NativeDisableParallelForRestriction]
			public ComponentLookup<FuelContainer> ContainerLookup;

			[NativeDisableParallelForRestriction]
			public NativeArray<FuelGroupState> States;

			public float DeltaTime;

			public void Execute(int group)
			{
				int start = GroupStart[group];
				int end = GroupStart[group + 1];
				float required = 0f;
				float expected = 0f;
				int consumers = 0;
				int containers = 0;
				float supplyCap = SupplyRate * DeltaTime;
				// 第一遍：汇总需求与可供给量。BPSS 的 FixedUpdate 也是先扫一遍再分配。
				for (int i = start; i < end; i++)
				{
					Entity entity = Members[i];
					if (ConsumerLookup.HasComponent(entity))
					{
						FuelConsumer consumer = ConsumerLookup[entity];
						if (consumer.Requesting != 0)
						{
							required += math.max(consumer.Required, 0f);
							consumers++;
						}
					}
					if (ContainerLookup.HasComponent(entity))
					{
						FuelContainer container = ContainerLookup[entity];
						expected += math.min(math.max(container.Amount, 0f), supplyCap);
						containers++;
					}
				}
				float ratio = 0f;
				if (required > 0f)
				{
					ratio = math.saturate(expected / required);
				}
				float realSupply = 0f;
				float refuel = 0f;
				// 第二遍：按比例分配并抽油。燃料箱按各自「这一步最多能供多少」的比例平摊，
				// 与 BPSS 的 SupplyFuel(realSupply * SupplyFuelAmount / expectedSupply) 一致。
				for (int j = start; j < end; j++)
				{
					Entity entity = Members[j];
					if (ConsumerLookup.HasComponent(entity))
					{
						FuelConsumer consumer = ConsumerLookup[entity];
						float supplied = 0f;
						if (consumer.Requesting != 0)
						{
							supplied = ratio * math.max(consumer.Required, 0f);
							realSupply += supplied;
						}
						consumer.Supplied = supplied;
						ConsumerLookup[entity] = consumer;
					}
					if (!ContainerLookup.HasComponent(entity))
					{
						continue;
					}
					FuelContainer container = ContainerLookup[entity];
					float amount = math.max(container.Amount, 0f);
					float drained = 0f;
					if (expected > 0f && ratio > 0f && amount > 0f)
					{
						float share = math.min(amount, supplyCap) / expected;
						drained = ratio * required * share;
						// 留着正数下限，避免浮点误差把油量写成 -0.0001 之后再也升不起来。
						container.Amount = math.max(amount - drained, 0f);
					}
					container.SuppliedStep = drained;
					// Engine 部件回油：BPSS 是「同焊接组件里每个 Engine 贡献 0.2*dt，
					// 再由该组件的所有燃料箱平分」，单次增量被 RefuelingAmount 夹住。
					if (ConnectedLookup.HasComponent(entity))
					{
						int component = ConnectedLookup[entity].Index;
						if (EngineCount.TryGetValue(component, out int engines) && engines > 0 && ContainerCount.TryGetValue(component, out int boxes) && boxes > 0)
						{
							refuel = math.min(EngineRefuelRate * DeltaTime * engines / boxes, RefuelRate * DeltaTime);
							if (refuel > 0f && container.Amount < container.Capacity)
							{
								container.Amount = math.min(container.Amount + refuel, container.Capacity);
							}
						}
					}
					ContainerLookup[entity] = container;
				}
				States[group] = new FuelGroupState
				{
					Consumers = consumers,
					Containers = containers,
					Required = required,
					ExpectedSupply = expected,
					RealSupply = realSupply,
					Ratio = ratio
				};
			}
		}

		/// <summary>最近一次分油后每组的统计；<c>null</c> 表示还没有燃料部件。</summary>
		private NativeArray<FuelGroupState> m_states;

		/// <summary>按组连续排列的燃料部件。</summary>
		private NativeList<Entity> m_members;

		/// <summary>长度 = 组数 + 1，前缀和形式的组边界。</summary>
		private NativeArray<int> m_groupStart;

		private NativeParallelHashMap<Entity, int> m_memberSlots;

		private NativeParallelHashMap<int, int> m_engineCount;

		private NativeParallelHashMap<int, int> m_containerCount;

		private NativeList<Entity> m_scratchParts;

		private ComponentLookup<FuelConsumer> m_consumerLookup;

		private ComponentLookup<FuelContainer> m_containerLookup;

		private ComponentLookup<PartConnectedComponent> m_connectedLookup;

		private ComponentLookup<PartTransform> m_transformLookup;

		private ComponentLookup<PartBuildLayer> m_layerLookup;

		private ComponentLookup<ContainedPart> m_containedLookup;

		private int m_groupCount;

		private float m_rebuildTimer = RebuildInterval;

		private static int s_dirty = 1;

		/// <summary>当前世界里的燃料系统，供 behavior 读 <see cref="GetState"/>。</summary>
		public static FuelSystem Instance { get; private set; }

		/// <summary>标记燃料拓扑需要重建（零件增删、阀门开关、装拆燃料箱时调用）。</summary>
		public static void MarkDirty()
		{
			s_dirty = 1;
		}

		/// <summary>读取某个燃料组的最近一步统计。</summary>
		public bool TryGetState(int group, out FuelGroupState state)
		{
			state = default(FuelGroupState);
			if (!m_states.IsCreated || group < 0 || group >= m_groupCount)
			{
				return false;
			}
			state = m_states[group];
			return true;
		}

		/// <summary>
		/// 立即重建燃料拓扑。阀门 / 燃料箱在点击的同一帧改变了连通性时，
		/// 紧接着的 <c>HasFuelSource()</c> 不该读到上一步的旧分组，所以由 behavior 主动催一次。
		/// 只在玩家点击这种低频路径上调用，不影响每步的分配开销。
		/// </summary>
		public void RebuildNow()
		{
			PartManager manager = PartManager.Instance;
			if (manager == null || !manager.IsSimulating || manager.PartSimulator == null)
			{
				return;
			}
			m_consumerLookup.Update(ref base.CheckedStateRef);
			m_containerLookup.Update(ref base.CheckedStateRef);
			m_connectedLookup.Update(ref base.CheckedStateRef);
			m_transformLookup.Update(ref base.CheckedStateRef);
			m_layerLookup.Update(ref base.CheckedStateRef);
			m_containedLookup.Update(ref base.CheckedStateRef);
			s_dirty = 0;
			m_rebuildTimer = RebuildInterval;
			m_lastPartCount = manager.PartSimulator.Unmanaged.Parts.Length;
			Rebuild(manager);
		}

		protected override void OnCreate()
		{
			base.OnCreate();
			Instance = this;
			m_members = new NativeList<Entity>(64, Allocator.Persistent);
			m_scratchParts = new NativeList<Entity>(64, Allocator.Persistent);
			m_groupStart = new NativeArray<int>(1, Allocator.Persistent);
			m_groupStart[0] = 0;
			m_states = new NativeArray<FuelGroupState>(0, Allocator.Persistent);
			m_memberSlots = new NativeParallelHashMap<Entity, int>(64, Allocator.Persistent);
			m_engineCount = new NativeParallelHashMap<int, int>(16, Allocator.Persistent);
			m_containerCount = new NativeParallelHashMap<int, int>(16, Allocator.Persistent);
			m_consumerLookup = CheckedStateRef.GetComponentLookup<FuelConsumer>(isReadOnly: false);
			m_containerLookup = CheckedStateRef.GetComponentLookup<FuelContainer>(isReadOnly: false);
			m_connectedLookup = CheckedStateRef.GetComponentLookup<PartConnectedComponent>(isReadOnly: true);
			m_transformLookup = CheckedStateRef.GetComponentLookup<PartTransform>(isReadOnly: true);
			m_layerLookup = CheckedStateRef.GetComponentLookup<PartBuildLayer>(isReadOnly: true);
			m_containedLookup = CheckedStateRef.GetComponentLookup<ContainedPart>(isReadOnly: true);
			m_groupCount = 0;
		}

		protected override void OnDestroy()
		{
			base.OnDestroy();
			if (Instance == this)
			{
				Instance = null;
			}
			if (m_members.IsCreated)
			{
				m_members.Dispose();
			}
			if (m_scratchParts.IsCreated)
			{
				m_scratchParts.Dispose();
			}
			if (m_groupStart.IsCreated)
			{
				m_groupStart.Dispose();
			}
			if (m_states.IsCreated)
			{
				m_states.Dispose();
			}
			if (m_memberSlots.IsCreated)
			{
				m_memberSlots.Dispose();
			}
			if (m_engineCount.IsCreated)
			{
				m_engineCount.Dispose();
			}
			if (m_containerCount.IsCreated)
			{
				m_containerCount.Dispose();
			}
		}

		protected override void OnUpdate()
		{
			PartManager manager = PartManager.Instance;
			if (manager == null || !manager.IsSimulating || manager.PartSimulator == null)
			{
				return;
			}
			m_consumerLookup.Update(ref base.CheckedStateRef);
			m_containerLookup.Update(ref base.CheckedStateRef);
			m_connectedLookup.Update(ref base.CheckedStateRef);
			m_transformLookup.Update(ref base.CheckedStateRef);
			m_layerLookup.Update(ref base.CheckedStateRef);
			m_containedLookup.Update(ref base.CheckedStateRef);
			float deltaTime = World.Time.DeltaTime;
			m_rebuildTimer -= deltaTime;
			int partCount = manager.PartSimulator.Unmanaged.Parts.Length;
			if (s_dirty != 0 || m_rebuildTimer <= 0f || partCount != m_lastPartCount)
			{
				s_dirty = 0;
				m_rebuildTimer = RebuildInterval;
				m_lastPartCount = partCount;
				Rebuild(manager);
			}
			if (m_groupCount == 0 || m_members.Length == 0)
			{
				return;
			}
			DistributeJob job = new DistributeJob
			{
				Members = m_members.AsArray(),
				GroupStart = m_groupStart,
				ConnectedLookup = m_connectedLookup,
				EngineCount = m_engineCount,
				ContainerCount = m_containerCount,
				ConsumerLookup = m_consumerLookup,
				ContainerLookup = m_containerLookup,
				States = m_states,
				DeltaTime = deltaTime
			};
			base.Dependency = job.Schedule(m_groupCount, 1, base.Dependency);
		}

		private int m_lastPartCount = -1;

		/// <summary>
		/// 重建燃料连通图。只在拓扑可能变化时调用，所以这里允许用管理侧代码
		/// （并查集、网格查询、结构变更都发生在低频路径上）。
		/// </summary>
		private void Rebuild(PartManager manager)
		{
			m_memberSlots.Clear();
			m_engineCount.Clear();
			m_containerCount.Clear();
			m_members.Clear();
			m_scratchParts.Clear();
			m_groupCount = 0;
			EntityManager em = EntityManager;
			ref PartSimulatorUnmanaged simulator = ref manager.PartSimulator.Unmanaged;
			NativeList<Entity> parts = simulator.Parts;
			// 装着燃料箱的框：容器 → 被装进去的件。燃料箱自己不在网格里占格，
			// 所以判断「燃料箱挨着谁」时要用它所在框的格子（对应 BPSS 的 m_enclosedPart）。
			using NativeParallelHashMap<Entity, Entity> containerOf = new NativeParallelHashMap<Entity, Entity>(parts.Length, Allocator.Temp);
			using NativeParallelHashMap<Entity, Entity> containedOf = new NativeParallelHashMap<Entity, Entity>(parts.Length, Allocator.Temp);
			for (int i = 0; i < parts.Length; i++)
			{
				Entity part = parts[i];
				if (!em.Exists(part) || !em.HasComponent<ContainedPart>(part))
				{
					continue;
				}
				Entity contained = em.GetComponentData<ContainedPart>(part).Value;
				if (contained != Entity.Null)
				{
					containerOf.TryAdd(contained, part);
					containedOf.TryAdd(part, contained);
				}
			}
			// 第一遍：挑出燃料部件，并统计每个焊接组件里的 Engine 数量（回油用）。
			for (int j = 0; j < parts.Length; j++)
			{
				Entity part2 = parts[j];
				if (!em.Exists(part2))
				{
					continue;
				}
				bool hasContainer = em.HasComponent<FuelContainer>(part2);
				bool hasConsumer = em.HasComponent<FuelConsumer>(part2);
				bool hasTube = em.HasComponent<FuelTubeLink>(part2);
				if (!hasContainer && !hasConsumer && !hasTube)
				{
					continue;
				}
				m_memberSlots.TryAdd(part2, m_scratchParts.Length);
				m_scratchParts.Add(part2);
			}
			int count = m_scratchParts.Length;
			if (count == 0)
			{
				m_groupStart[0] = 0;
				m_states.Dispose();
				m_states = new NativeArray<FuelGroupState>(0, Allocator.Persistent);
				// 没有燃料部件时把残留的分组标记清掉，避免 behavior 读到过期组号。
				for (int k = 0; k < parts.Length; k++)
				{
					Entity part3 = parts[k];
					if (em.Exists(part3) && em.HasComponent<FuelGroupIndex>(part3))
					{
						em.SetComponentData(part3, new FuelGroupIndex
						{
							Value = -1
						});
					}
				}
				return;
			}
			// 第二遍：统计 Engine / 燃料箱数量，并建立并查集。
			using NativeDisjointSet disjointSet = new NativeDisjointSet(count, Allocator.Temp);
			// 注意：这几个数组必须用普通局部变量而不是 using 声明。
			// `using` 声明出来的变量是只读的，`array[i] = v` 会报 CS1654
			// （「Cannot modify members of ... because it is a 'using variable'」）。
			NativeArray<int> connectedIndex = new NativeArray<int>(count, Allocator.Temp);
			NativeArray<int> gridX = new NativeArray<int>(count, Allocator.Temp);
			NativeArray<int> gridY = new NativeArray<int>(count, Allocator.Temp);
			NativeArray<int> rotation = new NativeArray<int>(count, Allocator.Temp);
			NativeArray<int> layerOf = new NativeArray<int>(count, Allocator.Temp);
			for (int l = 0; l < count; l++)
			{
				Entity part4 = m_scratchParts[l];
				connectedIndex[l] = em.HasComponent<PartConnectedComponent>(part4) ? em.GetComponentData<PartConnectedComponent>(part4).Index : -1;
				Entity anchor = containerOf.TryGetValue(part4, out Entity container) ? container : part4;
				if (em.HasComponent<PartTransform>(anchor))
				{
					PartTransform transform = em.GetComponentData<PartTransform>(anchor);
					gridX[l] = transform.X;
					gridY[l] = transform.Y;
					rotation[l] = transform.Rotation;
				}
				layerOf[l] = em.HasComponent<PartBuildLayer>(anchor) ? PartBuildLayer.Clamp(em.GetComponentData<PartBuildLayer>(anchor).Value) : PartBuildLayer.MinLayer;
				if (em.HasComponent<FuelContainer>(part4))
				{
					int component = connectedIndex[l];
					m_containerCount.TryGetValue(component, out int boxes);
					m_containerCount[component] = boxes + 1;
				}
				if (em.HasComponent<PartTypeValue>(part4) && em.GetComponentData<PartTypeValue>(part4).Value.PartType == PartType.Engine)
				{
					int component2 = connectedIndex[l];
					m_engineCount.TryGetValue(component2, out int engines);
					m_engineCount[component2] = engines + 1;
				}
			}
			// 第三遍：四邻域连边。管子按自身轴向限制方向，阀门关了就不导通，
			// 而且只有同一个焊接组件里的部件才会被连起来（与 BPSS 的
			// FuelTube.GetConnectedParts 里 connectedPart.ConnectedComponent == base.ConnectedComponent 一致）。
			for (int m = 0; m < count; m++)
			{
				Entity part5 = m_scratchParts[m];
				for (int d = 0; d < 4; d++)
				{
					int dx = ((d == 0) ? 1 : ((d == 2) ? (-1) : 0));
					int dy = ((d == 1) ? 1 : ((d == 3) ? (-1) : 0));
					if (!simulator.PartMap.TryGet(gridX[m] + dx, gridY[m] + dy, layerOf[m], out PartGridInfo info))
					{
						continue;
					}
					if (!TryResolveMember(em, containedOf, info, out int other) || other <= m)
					{
						continue;
					}
					if (connectedIndex[m] != connectedIndex[other])
					{
						continue;
					}
					if (!TubeAllows(em, part5, rotation[m], dx, dy) || !TubeAllows(em, m_scratchParts[other], rotation[other], -dx, -dy))
					{
						continue;
					}
					disjointSet.Union(m, other);
				}
			}
			// 第四遍：把并查集的根压成 0..groupCount-1 的组号，并把成员排成连续区间。
			using NativeParallelHashMap<int, int> rootToGroup = new NativeParallelHashMap<int, int>(count, Allocator.Temp);
			NativeArray<int> groupOf = new NativeArray<int>(count, Allocator.Temp);
			NativeArray<int> groupCounts = new NativeArray<int>(count, Allocator.Temp);
			for (int n = 0; n < count; n++)
			{
				int root = disjointSet.FindSet(n);
				if (!rootToGroup.TryGetValue(root, out int group))
				{
					group = m_groupCount++;
					rootToGroup.Add(root, group);
				}
				groupOf[n] = group;
				groupCounts[group] = groupCounts[group] + 1;
			}
			m_groupStart.Dispose();
			m_groupStart = new NativeArray<int>(m_groupCount + 1, Allocator.Persistent);
			m_groupStart[0] = 0;
			for (int p = 0; p < m_groupCount; p++)
			{
				m_groupStart[p + 1] = m_groupStart[p] + groupCounts[p];
			}
			NativeArray<int> cursor = new NativeArray<int>(m_groupCount, Allocator.Temp);
			for (int q = 0; q < m_groupCount; q++)
			{
				cursor[q] = m_groupStart[q];
			}
			m_members.ResizeUninitialized(count);
			for (int r = 0; r < count; r++)
			{
				int group2 = groupOf[r];
				int slot = cursor[group2]++;
				m_members[slot] = m_scratchParts[r];
			}
			m_states.Dispose();
			m_states = new NativeArray<FuelGroupState>(m_groupCount, Allocator.Persistent);
			// 把组号写回实体，behavior / QA 靠它读本组统计。
			for (int s = 0; s < count; s++)
			{
				Entity part6 = m_scratchParts[s];
				FuelGroupIndex value = new FuelGroupIndex
				{
					Value = groupOf[s]
				};
				if (em.HasComponent<FuelGroupIndex>(part6))
				{
					em.SetComponentData(part6, value);
				}
				else
				{
					em.AddComponentData(part6, value);
				}
			}
			// 这几个数组没有用 using 声明（见上），必须在这里成对释放。
			connectedIndex.Dispose();
			gridX.Dispose();
			gridY.Dispose();
			rotation.Dispose();
			layerOf.Dispose();
			groupOf.Dispose();
			groupCounts.Dispose();
			cursor.Dispose();
		}

		/// <summary>
		/// 网格格子里可能同时有「容器件」和「普通件」，燃料箱还可能装在容器件里，
		/// 所以候选实体有四个来源，取第一个真正的燃料部件。
		/// <paramref name="containedOf"/> 提供「容器 → 被装进去的件」的反查方向。
		/// </summary>
		private bool TryResolveMember(EntityManager em, NativeParallelHashMap<Entity, Entity> containedOf, PartGridInfo info, out int member)
		{
			member = -1;
			if (info.Part != Entity.Null && m_memberSlots.TryGetValue(info.Part, out member))
			{
				return true;
			}
			if (info.PartContainer != Entity.Null)
			{
				if (m_memberSlots.TryGetValue(info.PartContainer, out member))
				{
					return true;
				}
				if (containedOf.TryGetValue(info.PartContainer, out Entity contained) && m_memberSlots.TryGetValue(contained, out member))
				{
					return true;
				}
			}
			if (info.Part != Entity.Null && containedOf.TryGetValue(info.Part, out Entity contained2) && m_memberSlots.TryGetValue(contained2, out member))
			{
				return true;
			}
			return false;
		}

		/// <summary>
		/// 管子能否朝这个方向导通。BPSS 的普通/阀门管只沿自身轴（网格旋转 0 = 横向、
		/// 1 = 纵向）连，自动管四向都连；阀门关掉就断开。
		/// </summary>
		private bool TubeAllows(EntityManager em, Entity part, int partRotation, int dx, int dy)
		{
			if (!em.HasComponent<FuelTubeLink>(part))
			{
				return true;
			}
			FuelTubeLink tube = em.GetComponentData<FuelTubeLink>(part);
			if (tube.Kind == (byte)FuelTubeKind.Valve && tube.Open == 0)
			{
				return false;
			}
			if (tube.Kind == (byte)FuelTubeKind.Auto)
			{
				return true;
			}
			bool horizontal = dy == 0 && dx != 0;
			bool axisHorizontal = (partRotation & 1) == 0;
			return horizontal == axisHorizontal;
		}
	}
}
