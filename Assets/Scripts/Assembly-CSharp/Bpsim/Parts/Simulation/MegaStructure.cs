using System.Collections.Generic;
using Bpsim.Collections;
using Bpsim.Physics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace Bpsim.Parts.Simulation
{
	/// <summary>
	/// 巨构优化（mega structure）：把整台载具当成**一个刚体**来模拟。
	///
	/// <para>
	/// 常规模式下每个部件都是独立刚体，相邻部件之间用 <c>PhysicsJoint</c>（fixed joint）焊住。
	/// 部件一多，全部焊接件会连成一个巨大的求解岛（2 万部件的「天使座」是 180832 根关节、
	/// 单个连通块），约束求解、接触生成、关节数据搬运都是超线性的，单帧能到数百毫秒。
	/// </para>
	///
	/// <para>
	/// 巨构模式换一条路：把所有部件的碰撞体塞进一个
	/// <see cref="Unity.Physics.CompoundCollider"/>，只保留**一个**动态刚体。
	/// 部件实体本身不再有 <c>PhysicsWorldIndex</c>，因此不参与物理求解；它们保留
	/// <c>PhysicsMass</c> / <c>PhysicsVelocity</c> / <c>PhysicsDamping</c>，作用只是
	/// 「记录本步应该受到的速度变化」。
	/// </para>
	///
	/// <para>每步的流程（见 <see cref="MegaStructureMirrorSystem"/> 与 <see cref="MegaStructureDriveSystem"/>）：</para>
	/// <list type="number">
	/// <item>物理步结束后把整车刚体的位姿/速度**镜像**到每个部件（写 LocalTransform 与
	/// 「期望速度」），于是玩法代码看到的部件位置、速度都是真的。</item>
	/// <item>玩法行为（引擎推力、轮子、推进器……）照旧往部件的 <c>PhysicsVelocity</c> 上叠 delta，
	/// 不需要改任何玩法代码 —— 这正是把部件留在 <see cref="PartGameplayUtil.AddVelocity"/> 链路上的原因。</item>
	/// <item>下个物理步开始前，把「实际速度 - 期望速度」还原成冲量（乘部件质量），
	/// 以**部件原来的位置**作为作用点加到整车刚体上：线性分量直接累加，角分量用
	/// <c>cross(r, impulse)</c> 累加成力矩。施力点/力矩臂/重心因此与常规模式一致。</item>
	/// </list>
	///
	/// <para>
	/// 部件被打到超过阈值时（<see cref="SimulationSettings.MegaStructureBreakSpeed"/>），
	/// 从复合体里摘掉、还给它独立刚体，于是「受击掉落」依然成立（可破坏南瓜同款语义）。
	/// </para>
	/// </summary>
	public static class MegaStructure
	{
		public static bool Active { get; private set; }

		public static Entity Body { get; private set; }

		public static float TotalMass { get; private set; }

		/// <summary>部件的掉落阈值（接触法向接近速度，m/s）。0 = 永不掉落。</summary>
		public static float BreakSpeed { get; private set; }

		/// <summary>最近一步收到的碰撞事件条数（诊断用；0 说明材质没开事件或根本没接触）。</summary>
		public static int LastEventCount;

		/// <summary>最近一步判定超阈值、被请求摘除的部件数（诊断用）。</summary>
		public static int LastHitCount;

		/// <summary>最近一步见到的最大法向接近速度（诊断用）。</summary>
		public static float LastMaxSpeed;

		/// <summary>本步**物理步进之前**的整车线速度（质心速度，世界空间）。撞击强度必须用它算。</summary>
		public static float3 LastBodyLinear;

		/// <summary>本步物理步进之前的整车角速度（世界空间）。部件在接触点的速度 = 线速度 + ω × r。</summary>
		public static float3 LastBodyAngularWorld;

		/// <summary>本步物理步进之前的整车朝向。</summary>
		public static quaternion LastBodyRotation;

		/// <summary>
		/// 上一次镜像部件时使用的整车线速度 —— 也就是 Drive 用来算「期望速度」的基准。
		///
		/// <para>
		/// 为什么要单独存一份而不是直接用当前整车速度：重力每步都会给整车加 Δv，而部件没有物理体、
		/// 它们的速度是上次镜像写进去的，天然滞后一步。若拿「当前整车速度」当期望值，
		/// 这个 Δv 就会被 Drive 当成玩家施力 → 生成反向冲量，正好把重力抵消掉
		/// （实测：整车以 -9.81 重力下落时速度一直卡在 -1.5 m/s，永远不会加速）。
		/// 用镜像基准就不会：重力只改整车速度、不改基准，两者差值不会被计入冲量。
		/// </para>
		/// </summary>
		public static float3 MirrorLinear;

		/// <summary>上一次镜像时的整车角速度（世界空间）。</summary>
		public static float3 MirrorAngularWorld;

		/// <summary>上一次镜像时的整车朝向。</summary>
		public static quaternion MirrorRotation;

		/// <summary>上一次镜像时的整车重心（世界坐标）。</summary>
		public static float3 MirrorCenterOfMass;

		/// <summary>是否已经镜像过至少一次（第一次物理步之前没有基准可用）。</summary>
		public static bool Mirrored;

		/// <summary>本次模拟累计收到的碰撞事件数（跨步累加，撞地这种一下子就没的事件靠它才看得见）。</summary>
		public static int TotalEvents;

		/// <summary>本次模拟见到的最大法向接近速度（跨步累加）。</summary>
		public static float PeakImpactSpeed;

		/// <summary>本次模拟累计摘除的部件数（跨步累加）。</summary>
		public static int DetachedTotal;

		/// <summary>清零跨步累加量（每次 Build 时调用）。</summary>
		internal static void ResetStats()
		{
			TotalEvents = 0;
			Mirrored = false;
			PeakImpactSpeed = 0f;
			DetachedTotal = 0;
			LastEventCount = 0;
			LastHitCount = 0;
			LastMaxSpeed = 0f;
		}

		public static int PartCount
		{
			get
			{
				return s_children.IsCreated ? s_children.Length : 0;
			}
		}

		/// <summary>复合体子件顺序表：下标 == <c>ColliderKey</c> 的子索引。</summary>
		private static NativeList<Entity> s_children;

		private static int s_childKeyBits;

		private static BlobAssetReference<Collider> s_compound;

		/// <summary>旧的复合体 blob 不能立刻释放（物理世界可能还引用着），先扔这儿。</summary>
		private static readonly List<BlobAssetReference<Collider>> s_graveyard = new List<BlobAssetReference<Collider>>();

		private static readonly List<Entity> s_detachQueue = new List<Entity>();

		public static NativeList<Entity> Children
		{
			get
			{
				return s_children;
			}
		}

		public static int ChildKeyBits
		{
			get
			{
				return s_childKeyBits;
			}
		}

		/// <summary>请求把某个部件从整车复合体里摘出来（下一物理步生效）。</summary>
		public static void RequestDetach(Entity part)
		{
			if (Active && part != Entity.Null && !s_detachQueue.Contains(part))
			{
				s_detachQueue.Add(part);
			}
		}

		/// <summary>
		/// 建整车刚体。必须在部件实体都已经存在、碰撞体缩放都已烘好之后调用
		/// （即 <see cref="PartSimulator.Run"/> 里 BuildRigidbodiesJob 的位置）。
		/// </summary>
		public static void Build(EntityManager entityManager, Reference<PartSimulatorUnmanaged> simulator, NativeParallelHashMap<PartTypeInfo, PartExtensionData> extensionMap, SimulationSettings settings)
		{
			Dispose(entityManager);
			System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
			BreakSpeed = math.max(0f, settings.MegaStructureBreakSpeed);
			NativeList<Entity> parts = simulator.Value.Parts;
			using NativeList<CompoundCollider.ColliderBlobInstance> children = new NativeList<CompoundCollider.ColliderBlobInstance>(parts.Length, Allocator.Temp);
			float totalMass = 0f;
			float3 centerOfMassSum = float3.zero;
			float centerOfMassVolume = 0f;
			for (int i = 0; i < parts.Length; i++)
			{
				Entity entity = parts[i];
				if (!entityManager.HasComponent<PhysicsCollider>(entity) || !entityManager.HasComponent<PartTypeValue>(entity))
				{
					continue;
				}
				PartTypeInfo value = entityManager.GetComponentData<PartTypeValue>(entity).Value;
				// 绳索这种本来就是 kinematic 的部件不并进复合体：它靠 RopeSystem 的 Verlet 求解移动，
				// 一旦变成整车刚体的一部分就没法再显示绳索摆动。
				if (PartSimulator.IsKinematic(value))
				{
					continue;
				}
				PhysicsCollider collider = entityManager.GetComponentData<PhysicsCollider>(entity);
				if (!collider.Value.IsCreated)
				{
					continue;
				}
				LocalTransform transform = entityManager.GetComponentData<LocalTransform>(entity);
				float mass = extensionMap.TryGetValue(value, out var extension) ? extension.Mass : 1f;
				if (value.PartType == PartType.Umbrella) mass = .5f;
				children.Add(new CompoundCollider.ColliderBlobInstance
				{
					CompoundFromChild = new RigidTransform(transform.Rotation, transform.Position),
					Collider = collider.Value,
					Entity = entity
				});
				totalMass += math.max(0f, mass);
				// 部件不再是独立刚体，但还是要有质量/速度组件：
				// 玩法施力走 PartGameplayUtil.AddVelocity（需要 PhysicsVelocity），
				// 轮子/沙袋/绳索也会用 PhysicsMass 算冲量，它们按组件查询、不看物理世界成员资格，
				// 所以只要组件在，这些系统照常工作，写进的 delta 由 MegaStructureDriveSystem 汇总成整车冲量。
				// 关键一步是**去掉 PhysicsWorldIndex**：Unity.Physics 只给带这个共享组件的实体建 body，
				// 去掉之后部件就从物理世界里消失（不再有 body/关节/接触），这才是省开销的来源。
				MassProperties massProperties = collider.Value.Value.MassProperties;
				// 质心：跟 CompoundCollider.BuildMassProperties 一样按**体积**加权
				// （引擎算复合体质量分布用的就是它，换成玩法质量会让整车质心与碰撞分布不符）。
				float volume = massProperties.Volume;
				centerOfMassSum += math.transform(new RigidTransform(transform.Rotation, transform.Position), massProperties.MassDistribution.Transform.pos) * volume;
				centerOfMassVolume += volume;
				PhysicsMass partPhysicsMass = PhysicsMass.CreateDynamic(massProperties, math.max(mass, 1E-05f));
				PhysicsDamping partDamping = new PhysicsDamping
				{
					Linear = 0.2f,
					Angular = 0f
				};
				if (entityManager.HasComponent<PhysicsWorldIndex>(entity))
				{
					// BuildRigidbodiesJob 已经给每个部件加过物理组件，这里必须是「改」不是「加」。
					entityManager.RemoveComponent<PhysicsWorldIndex>(entity);
				}
				if (entityManager.HasComponent<PhysicsMass>(entity))
				{
					entityManager.SetComponentData(entity, partPhysicsMass);
				}
				else
				{
					entityManager.AddComponentData(entity, partPhysicsMass);
				}
				if (entityManager.HasComponent<PhysicsVelocity>(entity))
				{
					entityManager.SetComponentData(entity, PhysicsVelocity.Zero);
				}
				else
				{
					entityManager.AddComponentData(entity, PhysicsVelocity.Zero);
				}
				if (entityManager.HasComponent<PhysicsDamping>(entity))
				{
					entityManager.SetComponentData(entity, partDamping);
				}
				else
				{
					entityManager.AddComponentData(entity, partDamping);
				}
				// MegaPartOffset / MegaPartDamage 放到「质心算完之后」再挂，
				// 因为偏移量要写成相对质心的坐标（见下面那段注释）。
			}
			long gatherMs = stopwatch.ElapsedMilliseconds;
			if (children.Length == 0)
			{
				return;
			}
			// 复合体子件的偏移改成「相对质心」，整车刚体的原点也放到质心上。
			// 直接把绝对坐标塞进复合体（刚体原点留在世界原点）会留下一个几百单位大的质心偏移：
			// PhysicsMass.Transform 的质心离实体原点很远 → 惯性张量按平行轴定理虚高一大截，
			// 而 Constraint2D 是对 PhysicsVelocity（质心速度）做 2D 压平的，与部件/渲染用的
			// 实体原点速度语义错位，实测会让整车自己漂移、并把角动量泵进去。
			float3 centerOfMass = centerOfMassVolume > 0f ? centerOfMassSum / centerOfMassVolume : float3.zero;
			// children 是 using 变量（只读），要按下标回写就先用一个可写副本。
			NativeList<CompoundCollider.ColliderBlobInstance> writeable = children;
			for (int j = 0; j < writeable.Length; j++)
			{
				CompoundCollider.ColliderBlobInstance child = writeable[j];
				child.CompoundFromChild.pos -= centerOfMass;
				writeable[j] = child;
				Entity part = child.Entity;
				if (!entityManager.HasComponent<PartTypeValue>(part) || !entityManager.HasComponent<LocalTransform>(part))
				{
					continue;
				}
				PartTypeInfo value = entityManager.GetComponentData<PartTypeValue>(part).Value;
				float partMass = math.max(extensionMap.TryGetValue(value, out var extension) ? extension.Mass : 1f, 1E-05f);
				LocalTransform partTransform = entityManager.GetComponentData<LocalTransform>(part);
				MegaPartOffset offset = new MegaPartOffset
				{
					Position = partTransform.Position - centerOfMass,
					Rotation = partTransform.Rotation,
					Mass = partMass
				};
				if (entityManager.HasComponent<MegaPartOffset>(part))
				{
					entityManager.SetComponentData(part, offset);
				}
				else
				{
					entityManager.AddComponentData(part, offset);
				}
				if (!entityManager.HasComponent<MegaPartDamage>(part))
				{
					entityManager.AddComponentData(part, new MegaPartDamage
					{
						PeakSpeed = 0f
					});
				}
			}
			s_children = new NativeList<Entity>(children.Length, Allocator.Persistent);
			for (int j = 0; j < children.Length; j++)
			{
				s_children.Add(children[j].Entity);
			}
			ResetStats();
			s_childKeyBits = ColliderKeyUtility.GetNumKeyBits(children.Length);
			s_compound = CompoundCollider.Create(children.AsArray());
			EnableCollisionEvents(s_compound);
			ApplyVehicleFilter(s_compound);
			long compoundMs = stopwatch.ElapsedMilliseconds;
			float massOfVehicle = math.max(totalMass, 0.001f);
			// 整车刚体从「质心」起步：子件的 CompoundFromChild 已经是相对质心的偏移，
			// 所以刚体的第 0 帧位姿就是质心位置（见上面那段关于质心偏移的注释）。
			Entity body = entityManager.CreateEntity();
			entityManager.SetName(body, "MegaVehicle");
			LocalTransform bodyTransform = new LocalTransform
			{
				Position = centerOfMass,
				Rotation = quaternion.identity,
				Scale = 1f
			};
			entityManager.AddSharedComponent(body, new PhysicsWorldIndex(0u));
			entityManager.AddComponentData(body, bodyTransform);
			entityManager.AddComponentData(body, new LocalToWorld
			{
				Value = float4x4.TRS(bodyTransform.Position, bodyTransform.Rotation, new float3(1f, 1f, 1f))
			});
			entityManager.AddComponentData(body, new PhysicsCollider
			{
				Value = s_compound
			});
			entityManager.AddComponentData(body, PhysicsMass.CreateDynamic(s_compound.Value.MassProperties, massOfVehicle));
			entityManager.AddComponentData(body, PhysicsVelocity.Zero);
			entityManager.AddComponentData(body, new PhysicsDamping
			{
				Linear = 0f,
				Angular = 0f
			});
			// 跟部件一样锁在 2D 平面里（ConstraintSystem 会在每步结束后把 z 位置与滚转压回去）。
			entityManager.AddComponentData(body, Constraint2D.CreateFromTransform(bodyTransform));
			entityManager.AddComponentData(body, new MegaVehicle
			{
				Body = body,
				TotalMass = massOfVehicle
			});
			Body = body;
			TotalMass = massOfVehicle;
			Active = true;
			// 镜像一遍，让部件在第一帧就有正确的姿态（刚体位姿是单位阵，部件不写会保持建造位姿，
			// 两者本来就一致；但重心/速度的期望值需要先写一次，否则第一步会被当成玩家在施力）。
			MirrorToParts(entityManager);
			stopwatch.Stop();
			UnityEngine.Debug.Log($"[MegaStructure] build: parts={children.Length} gather={gatherMs}ms compound={compoundMs - gatherMs}ms vehicle+mirror={stopwatch.ElapsedMilliseconds - compoundMs}ms total={stopwatch.ElapsedMilliseconds}ms");
		}

		/// <summary>整车刚体的重心（世界坐标）。部件的「期望速度」是重心处的速度 + ω × r。</summary>
		public static float3 GetCenterOfMass(EntityManager entityManager)
		{
			if (!entityManager.Exists(Body) || !entityManager.HasComponent<PhysicsMass>(Body) || !entityManager.HasComponent<LocalTransform>(Body))
			{
				return float3.zero;
			}
			LocalTransform transform = entityManager.GetComponentData<LocalTransform>(Body);
			PhysicsMass mass = entityManager.GetComponentData<PhysicsMass>(Body);
			return transform.Position + math.rotate(transform.Rotation, mass.CenterOfMass);
		}

		/// <summary>
		/// 把整车刚体的位姿与速度镜像到每个部件：位置/旋转按 <c>body * local</c> 算，
		/// 速度按刚体运动学算（重心速度 + ω×r），并把它们写成部件的「期望速度」。
		/// 写速度的时候顺手把玩家本步加的 delta 抹掉 —— 镜像发生在物理步之后、
		/// 玩法 FixedUpdate 之前，那时候部件速度里不该有任何残留。
		/// </summary>
		/// <summary>
		/// 外部直接改了整车速度之后必须调一次：把新速度镜像到部件，并刷新 Drive 的期望基准。
		/// 否则下一步 Drive 会把「部件还停在旧速度」当成玩家施力，反向抵消掉这次改动。
		/// </summary>
		public static void SyncMirror(EntityManager entityManager)
		{
			MirrorToParts(entityManager);
		}

		private static void MirrorToParts(EntityManager entityManager)
		{
			if (!entityManager.Exists(Body))
			{
				return;
			}
			LocalTransform bodyTransform = entityManager.GetComponentData<LocalTransform>(Body);
			PhysicsVelocity bodyVelocity = entityManager.GetComponentData<PhysicsVelocity>(Body);
			float3 centerOfMass = GetCenterOfMass(entityManager);
			float3 angular = math.rotate(bodyTransform.Rotation, bodyVelocity.Angular);
			quaternion inverseRotation = math.inverse(bodyTransform.Rotation);
			MirrorLinear = bodyVelocity.Linear;
			MirrorAngularWorld = angular;
			MirrorRotation = bodyTransform.Rotation;
			MirrorCenterOfMass = centerOfMass;
			Mirrored = true;
			for (int i = 0; i < s_children.Length; i++)
			{
				Entity part = s_children[i];
				if (!entityManager.HasComponent<MegaPartOffset>(part))
				{
					continue;
				}
				MegaPartOffset offset = entityManager.GetComponentData<MegaPartOffset>(part);
				LocalTransform transform = entityManager.GetComponentData<LocalTransform>(part);
				transform.Position = bodyTransform.Position + math.rotate(bodyTransform.Rotation, offset.Position);
				transform.Rotation = math.mul(bodyTransform.Rotation, offset.Rotation);
				entityManager.SetComponentData(part, transform);
				entityManager.SetComponentData(part, new PhysicsVelocity
				{
					Linear = bodyVelocity.Linear + math.cross(angular, transform.Position - centerOfMass),
					Angular = math.rotate(inverseRotation, bodyVelocity.Angular)
				});
			}
		}

		/// <summary>
		/// 处理掉落队列：把请求的部件从复合体里摘出来，还给它独立刚体。
		/// 由 <see cref="MegaStructureDriveSystem"/> 在物理步开始之前调用（那时改碰撞体最安全）。
		/// </summary>
		public static void FlushDetachQueue(EntityManager entityManager)
		{
			if (!Active || s_detachQueue.Count == 0)
			{
				return;
			}
			bool anyRemoved = false;
			for (int i = 0; i < s_detachQueue.Count; i++)
			{
				Entity part = s_detachQueue[i];
				if (!entityManager.Exists(part) || !entityManager.HasComponent<MegaPartOffset>(part))
				{
					continue;
				}
				MegaPartOffset offset = entityManager.GetComponentData<MegaPartOffset>(part);
				LocalTransform transform = entityManager.GetComponentData<LocalTransform>(part);
				PhysicsVelocity velocity = entityManager.GetComponentData<PhysicsVelocity>(part);
				entityManager.RemoveComponent<MegaPartOffset>(part);
				entityManager.RemoveComponent<MegaPartDamage>(part);
				int index = s_children.IndexOf(part);
				if (index >= 0)
				{
					s_children.RemoveAt(index);
					anyRemoved = true;
				}
				// 变回独立刚体：加回 PhysicsWorldIndex 就够，其余组件一直留着。
				bool hasCollider = entityManager.HasComponent<PhysicsCollider>(part);
				MassProperties massProperties = hasCollider ? entityManager.GetComponentData<PhysicsCollider>(part).Value.Value.MassProperties : MassProperties.UnitSphere;
				if (!entityManager.HasComponent<PhysicsMass>(part))
				{
					entityManager.AddComponentData(part, PhysicsMass.CreateDynamic(massProperties, math.max(offset.Mass, 1E-05f)));
				}
				if (!entityManager.HasComponent<PhysicsVelocity>(part))
				{
					entityManager.AddComponentData(part, PhysicsVelocity.Zero);
				}
				entityManager.SetComponentData(part, velocity);
				if (!entityManager.HasComponent<PhysicsDamping>(part))
				{
					entityManager.AddComponentData(part, new PhysicsDamping
					{
						Linear = 0.2f,
						Angular = 0f
					});
				}
				if (!entityManager.HasComponent<Constraint2D>(part))
				{
					entityManager.AddComponentData(part, Constraint2D.CreateFromTransform(transform));
				}
				entityManager.AddSharedComponent(part, new PhysicsWorldIndex(0u));
			}
			s_detachQueue.Clear();
			if (anyRemoved)
			{
				RebuildCompound(entityManager);
			}
		}

		/// <summary>按当前的 <see cref="s_children"/> 重建整车复合碰撞体。</summary>
		private static void RebuildCompound(EntityManager entityManager)
		{
			if (!entityManager.Exists(Body))
			{
				return;
			}
			if (s_children.Length == 0)
			{
				// 车全掉光了：把整车刚体收掉，剩下的部件各自飞。
				entityManager.DestroyEntity(Body);
				Body = Entity.Null;
				Active = false;
				return;
			}
			using NativeList<CompoundCollider.ColliderBlobInstance> children = new NativeList<CompoundCollider.ColliderBlobInstance>(s_children.Length, Allocator.Temp);
			int write = 0;
			for (int i = 0; i < s_children.Length; i++)
			{
				Entity part = s_children[i];
				if (!entityManager.HasComponent<PhysicsCollider>(part) || !entityManager.HasComponent<MegaPartOffset>(part))
				{
					continue;
				}
				PhysicsCollider collider = entityManager.GetComponentData<PhysicsCollider>(part);
				if (!collider.Value.IsCreated)
				{
					continue;
				}
				MegaPartOffset offset = entityManager.GetComponentData<MegaPartOffset>(part);
				children.Add(new CompoundCollider.ColliderBlobInstance
				{
					CompoundFromChild = new RigidTransform(offset.Rotation, offset.Position),
					Collider = collider.Value,
					Entity = part
				});
				s_children[write++] = part;
			}
			s_children.RemoveRange(write, s_children.Length - write);
			s_childKeyBits = ColliderKeyUtility.GetNumKeyBits(s_children.Length);
			BlobAssetReference<Collider> compound = CompoundCollider.Create(children.AsArray());
			EnableCollisionEvents(compound);
			ApplyVehicleFilter(compound);
			BlobAssetReference<Collider> old = s_compound;
			s_compound = compound;
			if (old.IsCreated)
			{
				s_graveyard.Add(old);
			}
			entityManager.SetComponentData(Body, new PhysicsCollider
			{
				Value = s_compound
			});
			// 质量分布跟着变，总质量也要跟着掉：掉件后整车变轻，否则再撞一次的手感就不对了。
			// 注意刚体原点不动（它还是原质心），新的质心偏移由 PhysicsMass.Transform 表达，
			// 镜像用的 MegaPartOffset 也正是相对这个原点存的，两边口径一致。
			float remainingMass = 0f;
			for (int i = 0; i < s_children.Length; i++)
			{
				if (entityManager.HasComponent<MegaPartOffset>(s_children[i]))
				{
					remainingMass += entityManager.GetComponentData<MegaPartOffset>(s_children[i]).Mass;
				}
			}
			TotalMass = remainingMass;
			if (entityManager.HasComponent<PhysicsMass>(Body))
			{
				entityManager.SetComponentData(Body, PhysicsMass.CreateDynamic(s_compound.Value.MassProperties, math.max(TotalMass, 0.001f)));
			}
		}

		public static void Dispose(EntityManager entityManager)
		{
			s_detachQueue.Clear();
			if (entityManager.World != null && Body != Entity.Null && entityManager.Exists(Body))
			{
				entityManager.DestroyEntity(Body);
			}
			Body = Entity.Null;
			Active = false;
			TotalMass = 0f;
			BreakSpeed = 0f;
			if (s_children.IsCreated)
			{
				s_children.Dispose();
			}
			if (s_compound.IsCreated)
			{
				s_compound.Dispose();
			}
			for (int i = 0; i < s_graveyard.Count; i++)
			{
				if (s_graveyard[i].IsCreated)
				{
					s_graveyard[i].Dispose();
				}
			}
			s_graveyard.Clear();
		}

		/// <summary>
		/// 让整车复合体的子碰撞体抛碰撞事件。
		///
		/// <para>
		/// Unity.Physics 默认材质（<c>Material.Default</c>）的响应是 <c>Collide</c>，**不抛事件**，
		/// 于是 <c>ICollisionEventsJob</c> 什么也收不到，「受击掉件」就永远不会触发。
		/// 两侧材质是「按位或」后再判定的，所以只要整车这边打开就够了，地面/撞它的东西不用改。
		/// </para>
		/// <para>
		/// 改的是<b>复合体 blob 自己的副本</b>：<c>CompoundCollider.Create</c> 会把每个子碰撞体
		/// MemCpy 进新 blob，所以这里改材质不会污染部件原本的、与 prefab/其它存档共享的 collider blob。
		/// </para>
		/// </summary>
		/// <summary>
		/// 整车刚体独占的碰撞类别位。
		///
		/// <para>
		/// <see cref="PartCollisionFilter"/> 用位 0 表示地形、位 1..30 表示连通块，位 31 空着，
		/// 这里正好拿它当「整车」标记。作用是让<b>射向整车自己的射线一次性被宽相位剪掉</b>：
		/// 巨构模式下车是<b>一个</b>带十几万子件的复合碰撞体，轮子的接地射线从车体内部往外打，
		/// 起点落在复合体 BVH 深处，裁剪前要在里面穿一整圈、还会收到一堆自命中。
		/// 有了这个位，轮子射线用 <c>CollidesWith = ~VehicleCategory</c> 就能整棵子树跳过。
		/// </para>
		/// <para>
		/// 注意 <c>CollidesWith</c> 也排除了自己这一类：整车不和「只属于这一位」的东西碰撞。
		/// 地面等外部物体用的是正常过滤（位 0 等），不受影响。
		/// </para>
		/// </summary>
		public const uint VehicleCategory = 2147483648u;

		/// <summary>整车复合体标记成独占类别位，让自身射线能被剪掉（见 <see cref="VehicleCategory"/>）。</summary>
		private static unsafe void ApplyVehicleFilter(BlobAssetReference<Collider> compound)
		{
			if (!compound.IsCreated)
			{
				return;
			}
			((Collider*)compound.GetUnsafePtr())->SetCollisionFilter(new CollisionFilter
			{
				BelongsTo = VehicleCategory,
				CollidesWith = ~VehicleCategory
			});
		}

		private static unsafe void EnableCollisionEvents(BlobAssetReference<Collider> compound)
		{
			if (!compound.IsCreated)
			{
				return;
			}
			// 传 ColliderKey.Empty 表示「复合体的所有子件」。
			((Collider*)compound.GetUnsafePtr())->SetCollisionResponse(CollisionResponsePolicy.CollideRaiseCollisionEvents);
		}

		/// <summary>整车刚体上还没有 PhysicsCollider（例如车全掉光）时为 false。</summary>
		public static bool IsVehicleBody(Entity entity)
		{
			return Active && entity != Entity.Null && entity == Body;
		}
	}

	/// <summary><see cref="ColliderKey"/> 的位数计算（与 <see cref="CompoundCollider"/> 内部公式一致）。</summary>
	internal static class ColliderKeyUtility
	{
		/// <summary>能装下 0..count-1 需要多少位；0 个子件时为 1（避免除零）。</summary>
		public static int GetNumKeyBits(int count)
		{
			int bits = 1;
			while ((1 << bits) < count)
			{
				bits++;
			}
			return math.clamp(bits, 1, 31);
		}
	}
}
