using System.Collections.Generic;
using Bpsim.Parts;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;

namespace Bpsim.Parts.Simulation
{
	/// <summary>
	/// 巨构模式下把玩法施力折算回整车刚体。
	///
	/// <para>
	/// 部件不再参与物理求解，所以玩法行为往 <c>PhysicsVelocity</c> 上叠的 delta 不会被物理消费。
	/// 这个系统在**物理步开始之前**（<see cref="PhysicsInitializeGroup"/> 的最前面，
	/// 早于 <c>BuildPhysicsWorld</c> 抓取速度）把「部件当前速度 - 镜像给的期望速度」
	/// 还原成冲量，按部件原来的位置加在整车刚体上：线性分量求和后除以总质量，
	/// 角分量用 <c>r × P</c> 折算成绕重心的力矩。
	/// </para>
	/// <para>
	/// 这样推力、轮子、推进器仍然作用在「正确的点」上，力矩臂与常规模式一致，
	/// 转向/翻滚的响应不会因为刚体化而失真。
	/// </para>
	/// </summary>
	[UpdateInGroup(typeof(PhysicsInitializeGroup), OrderFirst = true)]
	public partial class MegaStructureDriveSystem : SystemBase
	{
		public int DiagnosticTicks { get; private set; }
		public float3 DiagnosticImpulse { get; private set; }
		public int DiagnosticStage { get; private set; }
		public int DiagnosticBodies { get; private set; }
		protected override void OnUpdate()
		{
			DiagnosticTicks++;
			DiagnosticStage = 0;
			if (!MegaStructure.Active)
			{
				return;
			}
			Entity body = MegaStructure.Body;
			DiagnosticStage = 1;
			EntityManager entityManager = base.EntityManager;
			if (!entityManager.Exists(body) || !entityManager.HasComponent<PhysicsVelocity>(body) || !entityManager.HasComponent<PhysicsMass>(body) || !entityManager.HasComponent<LocalTransform>(body))
			{
				return;
			}
			LocalTransform bodyTransform = entityManager.GetComponentData<LocalTransform>(body);
			DiagnosticStage = 2;
			PhysicsVelocity bodyVelocity = entityManager.GetComponentData<PhysicsVelocity>(body);
			PhysicsMass bodyMass = entityManager.GetComponentData<PhysicsMass>(body);
			float3 com = bodyTransform.Position + math.rotate(bodyTransform.Rotation, bodyMass.CenterOfMass);
			float3 bodyAngularWorld = math.rotate(bodyTransform.Rotation, bodyVelocity.Angular);
			// 期望速度的基准 = 上次镜像时的整车状态（不是当前状态）。
			// 当前状态里含着重力/碰撞刚改出来的 Δv，部件速度是上次镜像写的、天然滞后一步，
			// 拿当前状态当基准会把这个 Δv 当成玩家施力、生成反向冲量，正好抵消重力
			// （实测整车下落速度一直卡在 -1.5m/s）。详见 MegaStructure.MirrorLinear 的注释。
			float3 expectedLinear = MegaStructure.Mirrored ? MegaStructure.MirrorLinear : bodyVelocity.Linear;
			float3 expectedAngularWorld = MegaStructure.Mirrored ? MegaStructure.MirrorAngularWorld : bodyAngularWorld;
			quaternion expectedRotation = MegaStructure.Mirrored ? MegaStructure.MirrorRotation : bodyTransform.Rotation;
			float3 expectedCom = MegaStructure.Mirrored ? MegaStructure.MirrorCenterOfMass : com;
			// 这个系统在 PhysicsInitializeGroup 里跑，也就是「物理步进之前」——正好是记录
			// 撞击前速度的唯一时机。MegaStructureDamageSystem 在 AfterPhysicsSystemGroup，
			// 那时速度已经被求解器压扁（实测撞地 20m/s 只剩 0.04），拿它当撞击强度永远不达标。
			MegaStructure.LastBodyLinear = bodyVelocity.Linear;
			MegaStructure.LastBodyAngularWorld = bodyAngularWorld;
			MegaStructure.LastBodyRotation = bodyTransform.Rotation;
			float3 linearImpulse = float3.zero;
			float3 angularMomentum = float3.zero;
			float inverseTotalMass = 1f / math.max(MegaStructure.TotalMass, 0.001f);
			EntityQuery query = base.GetEntityQuery(ComponentType.ReadWrite<MegaPartOffset>(), ComponentType.ReadWrite<PhysicsVelocity>(), ComponentType.ReadOnly<PhysicsMass>());
			DiagnosticBodies = query.CalculateEntityCount();
			DiagnosticStage = 3;
			unsafe
			{
				// 用 IJobChunk 并行而不是 Entities.ForEach：这里必须同步算完（下面要立刻写整车速度），
				// 而且需要在同一遍里把部件的速度复位成期望值，避免同一 delta 被重复计入。
				// 各 chunk 各自累加到 LinearSums/AngularSums 的独立槽位，最后在主线程归约——
				// 单个 chunk 一个累加槽，比串行 IJob 快得多（两万个部件的逐实体读写是 cache 友好的顺序访问）。
				int chunkCount = query.CalculateChunkCountWithoutFiltering();
				NativeArray<float3> linearSums = new NativeArray<float3>(math.max(chunkCount, 1), Allocator.TempJob);
				NativeArray<float3> angularSums = new NativeArray<float3>(math.max(chunkCount, 1), Allocator.TempJob);
				DriveJob driveJob = new DriveJob
				{
					Offsets = base.GetComponentTypeHandle<MegaPartOffset>(true),
					Velocities = base.GetComponentTypeHandle<PhysicsVelocity>(false),
					Masses = base.GetComponentTypeHandle<PhysicsMass>(true),
					Transforms = base.GetComponentTypeHandle<LocalTransform>(true),
					BodyPosition = bodyTransform.Position,
					BodyRotation = expectedRotation,
					BodyLinear = expectedLinear,
					BodyAngularWorld = expectedAngularWorld,
					CenterOfMass = expectedCom,
					LinearSums = linearSums,
					AngularSums = angularSums
				};
				if (UnityEngine.Application.isEditor || !BurstCompiler.IsEnabled)
				{
					entityManager.CompleteAllTrackedJobs();
					// The existing managed Mega mirror uses the stable child list. Use
					// that list for drive as well in the player-DOTS Editor compatibility path.
					foreach (Entity part in MegaStructure.Children)
					{
						if (!entityManager.Exists(part) || !entityManager.HasComponent<MegaPartOffset>(part)) continue;
						MegaPartOffset offset = entityManager.GetComponentData<MegaPartOffset>(part);
						LocalTransform transform = entityManager.GetComponentData<LocalTransform>(part);
						PhysicsVelocity velocity = entityManager.GetComponentData<PhysicsVelocity>(part);
						PhysicsMass mass = entityManager.GetComponentData<PhysicsMass>(part);
						float3 expected = expectedLinear + math.cross(expectedAngularWorld, transform.Position - expectedCom);
						float3 deltaLinear = velocity.Linear - expected;
						float3 expectedPartAngular = math.rotate(math.inverse(expectedRotation), expectedAngularWorld);
						float3 deltaAngular = velocity.Angular - expectedPartAngular;
						float3 impulse = deltaLinear * math.max(offset.Mass, 1e-5f);
						linearImpulse += impulse;
						angularMomentum += math.cross(transform.Position - expectedCom, impulse);
						float3 inverse = mass.InverseInertia;
						float3 inertia = new float3(inverse.x > 0 ? 1 / inverse.x : 0,
							inverse.y > 0 ? 1 / inverse.y : 0, inverse.z > 0 ? 1 / inverse.z : 0);
						angularMomentum += math.rotate(transform.Rotation, inertia * deltaAngular);
						velocity.Linear = expected; velocity.Angular = expectedPartAngular;
						entityManager.SetComponentData(part, velocity);
					}
				}
				else
				{
					base.Dependency = driveJob.ScheduleParallel(query, base.Dependency);
					base.Dependency.Complete();
				}
				for (int i = 0; i < chunkCount; i++)
				{
					linearImpulse += linearSums[i];
					angularMomentum += angularSums[i];
				}
				linearSums.Dispose();
				angularSums.Dispose();
			}
			if (math.lengthsq(linearImpulse) <= 1E-09f && math.lengthsq(angularMomentum) <= 1E-09f)
			{
				return;
			}
			DiagnosticImpulse = linearImpulse;
			// 线性：P / 总质量。角：绕重心累计的角动量换回刚体局部惯性空间后除以惯性张量
			// （PhysicsVelocity.Angular 是本体局部空间，见 Integrator.IntegrateOrientation）。
			PhysicsVelocity result = bodyVelocity;
			result.Linear += linearImpulse * inverseTotalMass;
			float3 angularLocal = math.rotate(math.inverse(bodyTransform.Rotation), angularMomentum);
			result.Angular += angularLocal * bodyMass.InverseInertia;
			// 限制单步速度增量，避免极端叠加（例如几十个推进器同时点火）把车打飞。
			float maxLinear = 120f;
			float linearSpeed = math.length(result.Linear);
			if (linearSpeed > maxLinear)
			{
				result.Linear *= maxLinear / linearSpeed;
			}
			float maxAngular = 40f;
			float angularSpeed = math.length(result.Angular);
			if (angularSpeed > maxAngular)
			{
				result.Angular *= maxAngular / angularSpeed;
			}
			entityManager.SetComponentData(body, result);
		}

		[BurstCompile]
		private unsafe struct DriveJob : IJobChunk
		{

			[ReadOnly]
			public ComponentTypeHandle<MegaPartOffset> Offsets;

			public ComponentTypeHandle<PhysicsVelocity> Velocities;

			[ReadOnly]
			public ComponentTypeHandle<PhysicsMass> Masses;

			[ReadOnly]
			public ComponentTypeHandle<LocalTransform> Transforms;

			public float3 BodyPosition;

			public quaternion BodyRotation;

			public float3 BodyLinear;

			public float3 BodyAngularWorld;

			public float3 CenterOfMass;

			public NativeArray<float3> LinearSums;

			public NativeArray<float3> AngularSums;

			public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				float3 linearTotal = float3.zero;
				float3 angularTotal = float3.zero;
				{
					NativeArray<MegaPartOffset> offsets = chunk.GetNativeArray(ref Offsets);
					NativeArray<PhysicsVelocity> velocities = chunk.GetNativeArray(ref Velocities);
					NativeArray<PhysicsMass> masses = chunk.GetNativeArray(ref Masses);
					NativeArray<LocalTransform> transforms = chunk.GetNativeArray(ref Transforms);
					for (int i = 0; i < chunk.Count; i++)
					{
						MegaPartOffset offset = offsets[i];
						LocalTransform transform = transforms[i];
						PhysicsVelocity velocity = velocities[i];
						float partMass = math.max(offset.Mass, 1E-05f);
						// 期望速度 = 刚体运动学在部件位置上的速度（与 MegaStructure.MirrorToParts 完全一致）。
						float3 expectedLinear = BodyLinear + math.cross(BodyAngularWorld, transform.Position - CenterOfMass);
						float3 deltaLinear = velocity.Linear - expectedLinear;
						float3 deltaAngular = velocity.Angular - math.rotate(math.inverse(BodyRotation), BodyAngularWorld);
						float3 impulse = deltaLinear * partMass;
						linearTotal += impulse;
						float3 arm = transform.Position - CenterOfMass;
						angularTotal += math.cross(arm, impulse);
						// 角速度增量本身也是力矩贡献：先把局部角冲量转回世界再加进去。
						float3 localInertiaInverse = masses[i].InverseInertia;
						float3 inertia = new float3(
							localInertiaInverse.x > 0f ? 1f / localInertiaInverse.x : 0f,
							localInertiaInverse.y > 0f ? 1f / localInertiaInverse.y : 0f,
							localInertiaInverse.z > 0f ? 1f / localInertiaInverse.z : 0f);
						angularTotal += math.rotate(transform.Rotation, inertia * deltaAngular);
						// 复位成期望值：本步的 delta 已经算进整车冲量，不能留到下一步重复计入。
						velocity.Linear = expectedLinear;
						velocity.Angular = math.rotate(math.inverse(BodyRotation), BodyAngularWorld);
						velocities[i] = velocity;
					}
				}
				LinearSums[unfilteredChunkIndex] = linearTotal;
				AngularSums[unfilteredChunkIndex] = angularTotal;
			}
		}
	}

	/// <summary>
	/// 巨构模式下每步结束后把整车刚体的位姿/速度镜像到部件，并处理掉「该掉下来」的部件。
	/// 放在 <see cref="AfterPhysicsSystemGroup"/> 里（物理步与 ExportPhysicsWorld 都已完成），
	/// 这样之后运行的玩法/渲染看到的是最终位置。
	/// </summary>
	[UpdateInGroup(typeof(AfterPhysicsSystemGroup))]
	public partial class MegaStructureMirrorSystem : SystemBase
	{
		public int DiagnosticTicks { get; private set; }
		protected override void OnUpdate()
		{
			DiagnosticTicks++;
			if (!MegaStructure.Active)
			{
				return;
			}
			EntityManager entityManager = base.EntityManager;
			MegaStructure.FlushDetachQueue(entityManager);
			if (!MegaStructure.Active)
			{
				return;
			}
			Entity body = MegaStructure.Body;
			if (!entityManager.Exists(body) || !entityManager.HasComponent<LocalTransform>(body) || !entityManager.HasComponent<PhysicsVelocity>(body))
			{
				return;
			}
			LocalTransform bodyTransform = entityManager.GetComponentData<LocalTransform>(body);
			PhysicsVelocity bodyVelocity = entityManager.GetComponentData<PhysicsVelocity>(body);
			PhysicsMass bodyMass = entityManager.HasComponent<PhysicsMass>(body) ? entityManager.GetComponentData<PhysicsMass>(body) : default(PhysicsMass);
			float3 com = bodyTransform.Position + math.rotate(bodyTransform.Rotation, bodyMass.CenterOfMass);
			float3 angularWorld = math.rotate(bodyTransform.Rotation, bodyVelocity.Angular);
			quaternion inverseRotation = math.inverse(bodyTransform.Rotation);
			// 记录镜像基准：下一步 Drive 用它算「期望速度」，避免把重力等外力当成玩家施力。
			MegaStructure.MirrorLinear = bodyVelocity.Linear;
			MegaStructure.MirrorAngularWorld = angularWorld;
			MegaStructure.MirrorRotation = bodyTransform.Rotation;
			MegaStructure.MirrorCenterOfMass = com;
			MegaStructure.Mirrored = true;
			// 镜像走 IJobChunk 批处理：原来是两万个部件逐个 EntityManager.GetComponentData/SetComponentData
			// （每部件 3 次托管调用），20k 部件时单步要 20ms 以上，而这里纯粹是数组搬运。
			// 掉件的部件在 FlushDetachQueue 里已经被移除 MegaPartOffset，所以按组件查询天然排除了它们。
			EntityQuery query = GetEntityQuery(ComponentType.ReadOnly<MegaPartOffset>(), ComponentType.ReadWrite<LocalTransform>(), ComponentType.ReadWrite<PhysicsVelocity>());
			if (query.IsEmptyIgnoreFilter)
			{
				return;
			}
			MirrorJob job = new MirrorJob
			{
				Offsets = GetComponentTypeHandle<MegaPartOffset>(isReadOnly: true),
				Transforms = GetComponentTypeHandle<LocalTransform>(isReadOnly: false),
				Velocities = GetComponentTypeHandle<PhysicsVelocity>(isReadOnly: false),
				BodyPosition = bodyTransform.Position,
				BodyRotation = bodyTransform.Rotation,
				BodyLinear = bodyVelocity.Linear,
				BodyAngularLocal = bodyVelocity.Angular,
				AngularWorld = angularWorld,
				InverseRotation = inverseRotation,
				CenterOfMass = com
			};
			if (UnityEngine.Application.isEditor || !BurstCompiler.IsEnabled)
			{
				entityManager.CompleteAllTrackedJobs();
				MegaStructure.SyncMirror(entityManager);
			}
			else
			{
				base.Dependency = job.ScheduleParallel(query, base.Dependency);
				base.Dependency.Complete();
			}
		}

		[BurstCompile]
		private struct MirrorJob : IJobChunk
		{
			[ReadOnly]
			public ComponentTypeHandle<MegaPartOffset> Offsets;

			public ComponentTypeHandle<LocalTransform> Transforms;

			public ComponentTypeHandle<PhysicsVelocity> Velocities;

			public float3 BodyPosition;

			public quaternion BodyRotation;

			public float3 BodyLinear;

			public float3 BodyAngularLocal;

			public float3 AngularWorld;

			public quaternion InverseRotation;

			public float3 CenterOfMass;

			public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				NativeArray<MegaPartOffset> offsets = chunk.GetNativeArray(ref Offsets);
				NativeArray<LocalTransform> transforms = chunk.GetNativeArray(ref Transforms);
				NativeArray<PhysicsVelocity> velocities = chunk.GetNativeArray(ref Velocities);
				for (int i = 0; i < chunk.Count; i++)
				{
					MegaPartOffset offset = offsets[i];
					LocalTransform transform = transforms[i];
					transform.Position = BodyPosition + math.rotate(BodyRotation, offset.Position);
					transform.Rotation = math.mul(BodyRotation, offset.Rotation);
					transforms[i] = transform;
					velocities[i] = new PhysicsVelocity
					{
						Linear = BodyLinear + math.cross(AngularWorld, transform.Position - CenterOfMass),
						Angular = math.rotate(InverseRotation, BodyAngularLocal)
					};
				}
			}
		}
	}

	/// <summary>
	/// 巨构模式下检测整车碰到的东西：接触点的接近速度超过阈值就把那个部件从复合体里摘出来。
	///
	/// <para>
	/// 做法与 Unity.Physics 自带的 <c>DisplayCollisionEventsSystem</c> 相同 —— 在
	/// <see cref="AfterPhysicsSystemGroup"/> 里用 <see cref="ICollisionEventsJob"/> 消费本步
	/// 产生的碰撞事件（物理步已经算好事件数组，这里只是读）。碰撞事件里的 <c>ColliderKey</c>
	/// 指向复合体子件，高 <see cref="MegaStructure.ChildKeyBits"/> 位就是子件下标，
	/// 借此还原出「是哪个部件被撞了」。
	/// </para>
	/// <para>
	/// 阈值语义是「法向接近速度」（m/s）：车身自己高速落地时整个底面都会超标，
	/// 所以默认值给得比较大（<see cref="SimulationSettings.MegaStructureBreakSpeed"/>），
	/// 并且每步最多摘 <see cref="MaxDetachPerStep"/> 个，避免一次撞击把车拆光。
	/// </para>
	/// </summary>
	[UpdateInGroup(typeof(AfterPhysicsSystemGroup))]
	[UpdateBefore(typeof(MegaStructureMirrorSystem))]
	public partial class MegaStructureDamageSystem : SystemBase
	{
		public const int MaxDetachPerStep = 24;

		protected override void OnUpdate()
		{
			if (!MegaStructure.Active || MegaStructure.BreakSpeed <= 0f || MegaStructure.PartCount == 0)
			{
				return;
			}
			Entity body = MegaStructure.Body;
			EntityManager entityManager = base.EntityManager;
			if (!entityManager.Exists(body))
			{
				return;
			}

			// Anvil（box3d）后端：Unity.Physics / Havok 的事件通道对非 UnityPhysics 类型会静默跳过，
			// 改为直接消费 AnvilContactEventSystem 产出的接触表。
			if (Bpsim.Physics.Anvil.AnvilStepSystem.Active)
			{
				ConsumeAnvilContacts(body);
				return;
			}

			if (!SystemAPI.TryGetSingleton<SimulationSingleton>(out var simulation))
			{
				return;
			}
			NativeList<Entity> children = MegaStructure.Children;
			NativeList<MegaHit> hits = new NativeList<MegaHit>(16, Allocator.TempJob);
			NativeArray<int> eventCount = new NativeArray<int>(1, Allocator.TempJob);
			NativeArray<float> maxSpeed = new NativeArray<float>(1, Allocator.TempJob);
			ComponentLookup<PhysicsVelocity> velocities = base.GetComponentLookup<PhysicsVelocity>(true);
			ComponentLookup<MegaPartOffset> offsets = base.GetComponentLookup<MegaPartOffset>(true);
			MegaDamageJob job = new MegaDamageJob
			{
				Vehicle = body,
				VehicleLinear = MegaStructure.LastBodyLinear,
				VehicleAngularWorld = MegaStructure.LastBodyAngularWorld,
				VehicleRotation = MegaStructure.LastBodyRotation,
				Offsets = offsets,
				Children = children.AsArray(),
				KeyBits = MegaStructure.ChildKeyBits,
				Velocities = velocities,
				Threshold = MegaStructure.BreakSpeed,
				Hits = hits,
				EventCount = eventCount,
				MaxSpeed = maxSpeed
			};
			base.Dependency = job.Schedule(simulation, base.Dependency);
			base.Dependency.Complete();
			MegaStructure.LastEventCount = eventCount[0];
			MegaStructure.LastMaxSpeed = maxSpeed[0];
			eventCount.Dispose();
			maxSpeed.Dispose();
			if (hits.Length > 0)
			{
				// 同一部件可能有多条接触，去重后按速度从高到低摘，每步限量。
				int detached = 0;
				NativeList<MegaHit> sorted = hits;
				sorted.Sort(new MegaHitComparer());
				for (int i = 0; i < sorted.Length && detached < MaxDetachPerStep; i++)
				{
					MegaStructure.RequestDetach(sorted[i].Part);
					detached++;
				}
				MegaStructure.LastHitCount = detached;
				MegaStructure.DetachedTotal += detached;
			}
			else
			{
				MegaStructure.LastHitCount = 0;
			}
			MegaStructure.TotalEvents += MegaStructure.LastEventCount;
			MegaStructure.PeakImpactSpeed = math.max(MegaStructure.PeakImpactSpeed, MegaStructure.LastMaxSpeed);
			hits.Dispose();
		}

		/// <summary>
		/// Anvil 后端的接触消费：语义与 <see cref="MegaDamageJob"/> 一致 —— 只用「法向接近速度」
		/// 与阈值比较，部件还原靠 AnvilContactEvent 的 Entity（Anvil 没有 ColliderKey，
		/// 整车在 box3d 里就是单个 body，接触实体直接是整车实体，此时按最大接近速度摘件）。
		/// </summary>
		private void ConsumeAnvilContacts(Entity body)
		{
			if (!SystemAPI.TryGetSingleton(out Bpsim.Physics.Anvil.AnvilContactsSingleton contacts))
			{
				return;
			}

			ComponentLookup<PhysicsVelocity> velocities = GetComponentLookup<PhysicsVelocity>(true);
			ComponentLookup<MegaPartOffset> offsets = GetComponentLookup<MegaPartOffset>(true);
			NativeList<Entity> children = MegaStructure.Children;

			MegaStructure.LastEventCount = contacts.Count;
			MegaStructure.LastMaxSpeed = 0f;

			float3 vehicleVelocity = MegaStructure.LastBodyLinear;
			quaternion bodyRotation = MegaStructure.LastBodyRotation;
			float3 bodyPosition = EntityManager.GetComponentData<LocalTransform>(body).Position;
			int detached = 0;
			for (int i = 0; i < contacts.Count && detached < MaxDetachPerStep; i++)
			{
				Bpsim.Physics.Anvil.AnvilContactEvent contact = contacts.Events[i];
				bool aIsBody = contact.EntityA == body;
				bool bIsBody = contact.EntityB == body;
				if (!aIsBody && !bIsBody)
				{
					continue;
				}

				float3 normal = aIsBody ? contact.Normal : -contact.Normal;
				Entity other = aIsBody ? contact.EntityB : contact.EntityA;
				float3 otherVelocity = velocities.HasComponent(other) ? velocities[other].Linear : float3.zero;
				float speed = math.abs(math.dot(vehicleVelocity - otherVelocity, normal));
				MegaStructure.LastMaxSpeed = math.max(MegaStructure.LastMaxSpeed, speed);
				if (speed < MegaStructure.BreakSpeed)
				{
					continue;
				}

				// 找出离接触点最近的部件：box3d 里整车是单个 compound 拆成的多 shape 同体，
				// 接触表只给整车实体，因此按接触点位置就近归属。
				Entity closest = Entity.Null;
				float bestDistance = float.MaxValue;
				for (int c = 0; c < children.Length; c++)
				{
					Entity child = children[c];
					if (!offsets.HasComponent(child))
					{
						continue;
					}
					float3 partPosition = bodyPosition + math.rotate(bodyRotation, offsets[child].Position);
					float distance = math.distancesq(partPosition, contact.Position);
					if (distance < bestDistance)
					{
						bestDistance = distance;
						closest = child;
					}
				}
				if (closest == Entity.Null)
				{
					continue;
				}
				MegaStructure.RequestDetach(closest);
				detached++;
			}

			MegaStructure.LastHitCount = detached;
			MegaStructure.DetachedTotal += detached;
			MegaStructure.TotalEvents += MegaStructure.LastEventCount;
			MegaStructure.PeakImpactSpeed = math.max(MegaStructure.PeakImpactSpeed, MegaStructure.LastMaxSpeed);
		}

		private struct MegaHitComparer : IComparer<MegaHit>
		{
			public int Compare(MegaHit x, MegaHit y)
			{
				return y.Speed.CompareTo(x.Speed);
			}
		}

		private struct MegaDamageJob : ICollisionEventsJob
		{
			public Entity Vehicle;

			[ReadOnly]
			public NativeArray<Entity> Children;

			public int KeyBits;

			[ReadOnly]
			public ComponentLookup<PhysicsVelocity> Velocities;

			public float Threshold;

			public NativeList<MegaHit> Hits;

			/// <summary>本步收到的碰撞事件总数（含与整车无关的，用来判断「事件到底有没有产生」）。</summary>
			public NativeArray<int> EventCount;

			/// <summary>本步见到的最大法向接近速度。</summary>
			public NativeArray<float> MaxSpeed;

			/// <summary>步进前的整车线速度（质心速度，世界空间）。</summary>
			public float3 VehicleLinear;

			/// <summary>步进前的整车角速度（世界空间）。</summary>
			public float3 VehicleAngularWorld;

			/// <summary>步进前的整车朝向，用来把部件相对质心的偏移转成世界空间力臂。</summary>
			public quaternion VehicleRotation;

			/// <summary>部件相对整车质心的偏移（MegaPartOffset.Position 已经是相对质心的）。</summary>
			[ReadOnly]
			public ComponentLookup<MegaPartOffset> Offsets;

			public void Execute(CollisionEvent collisionEvent)
			{
				EventCount[0] = EventCount[0] + 1;
				Entity part;
				Entity other;
				float3 normal = collisionEvent.Normal;
				if (collisionEvent.EntityA == Vehicle)
				{
					part = Resolve(collisionEvent.ColliderKeyA);
					other = collisionEvent.EntityB;
				}
				else if (collisionEvent.EntityB == Vehicle)
				{
					part = Resolve(collisionEvent.ColliderKeyB);
					other = collisionEvent.EntityA;
					normal = -normal;
				}
				else
				{
					return;
				}
				if (part == Entity.Null)
				{
					return;
				}
				// 撞击强度用**步进前**的速度：本系统在 AfterPhysicsSystemGroup，
				// 此刻整车速度已经被求解器消掉（实测 20m/s 撞地后读到 0.04），拿它判定永远不会达标。
				// 另外部件在接触点的速度还要加上 ω × r（r = 部件相对质心的偏移），
				// 否则「原地转动时撞到东西」「撞击点在车身远端」都测不出强度。
				float3 arm = Offsets.HasComponent(part) ? math.rotate(VehicleRotation, Offsets[part].Position) : float3.zero;
				float3 vehicleVelocity = VehicleLinear + math.cross(VehicleAngularWorld, arm);
				float3 otherVelocity = Velocities.HasComponent(other) ? Velocities[other].Linear : float3.zero;
				float speed = math.abs(math.dot(vehicleVelocity - otherVelocity, normal));
				MaxSpeed[0] = math.max(MaxSpeed[0], speed);
				if (speed < Threshold)
				{
					return;
				}
				Hits.Add(new MegaHit
				{
					Part = part,
					Speed = speed
				});
			}

			private Entity Resolve(ColliderKey key)
			{
				if (KeyBits <= 0 || KeyBits >= 32 || Children.Length == 0)
				{
					return Entity.Null;
				}
				uint index = key.Value >> (32 - KeyBits);
				if (index >= (uint)Children.Length)
				{
					return Entity.Null;
				}
				return Children[(int)index];
			}
		}
	}
}
