using Bpsim.Collections;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

namespace Bpsim.Parts.Simulation
{
	public class PartSimulator
	{
		[BurstCompile]
		private struct InstantiatePartsJob : IJob
		{
			public EntityManager EntityManager;

			public Reference<PartSceneUnmanaged> Source;

			public Reference<PartSimulatorUnmanaged> Destination;

			public void Execute()
			{
				foreach (Entity part in Source.Value.Parts)
				{
					Entity value = EntityManager.Instantiate(part);
					Destination.Value.Parts.Add(in value);
				}
			}
		}

		/// <summary>
		/// 给这一帧刚实例化出来的模拟部件写 <see cref="LocalTransform" /> 并补 <see cref="WorldTransform" />。
		/// </summary>
		/// <remarks>
		/// <para>
		/// <see cref="WorldTransform" /> 在本工程里没有任何烘焙器负责，只由 Unity.Transforms 的
		/// <c>LocalToWorldSystem</c> 在 <see cref="TransformSystemGroup" /> 里给「有 LocalTransform、
		/// 没有 WorldTransform」的实体补上；而 <see cref="Run" /> 里的
		/// <c>InstantiatePartsJob</c> → <c>BuildRigidbodiesJob</c> → <c>BuildJointsAndComponentsJob</c>
		/// 全是同一帧同步 <c>IJobExtensions.Run</c>，跑在 TransformSystemGroup 之前。
		/// 于是建关节时 <c>worldTransformLookup[entity]</c> 读的是不存在的组件，
		/// <see cref="PartSimulatorUnmanaged.AddFixedJoint" /> 抛
		/// <c>ArgumentException: A component with type:Unity.Transforms.WorldTransform has not been added to the entity</c>，
		/// 整份 job 当场中断：一个关节都建不出来，连通块也停留在「每件一块」。
		/// </para>
		/// <para>
		/// 位姿同样不能直接抄 Instantiate 复制过来的 LocalTransform：部件刚放置完就在这里开跑时，
		/// 逐帧写 LocalTransform 的 <c>PartTransformSystem</c>（它在 TransformSystemGroup 里，
		/// 且 <c>IsSimulating</c> 为真后会整个跳过）还没轮到过，复制到的是预制体模板的占位坐标。
		/// 所以这里按 <c>PartTransformSystem</c> 一模一样的公式
		/// <c>BasePart.MoveTo/RotateTo</c> 从 <see cref="PartTransform" /> 重算一遍，
		/// 同时写回 LocalTransform —— 紧随其后的 <c>BuildRigidbodiesJob</c> 用
		/// <c>Constraint2D.CreateFromTransform(LocalTransform)</c> 建约束、Unity.Physics 也按
		/// LocalTransform 生成刚体，两边必须是同一份落位后的位姿。
		/// </para>
		/// <para>
		/// 部件根实体没有 <see cref="Unity.Transforms.Parent" />，世界变换就等于自身
		/// LocalTransform，所以 WorldTransform 取 <c>(WorldTransform)localTransform</c>，
		/// 与 <c>LocalToWorldSystem.ComputeRootLocalToWorldJob</c> 一致。子实体
		/// （<c>Visualization</c> / <c>WheelSprite</c> 之类）不参与焊接，仍交给
		/// LocalToWorldSystem 的子物体路径。
		/// </para>
		/// <para>不能加 [BurstCompile]：要调用 EntityManager 做结构变更。</para>
		/// </remarks>
		private struct SeedWorldTransformJob : IJob
		{
			public EntityManager EntityManager;

			public PartAspect.Lookup PartAspectLookup;

			public Reference<PartSimulatorUnmanaged> Simulator;

			public void Execute()
			{
				int count = Simulator.Value.Parts.Length;
				// 分两趟：第一趟只读（PartAspect.Lookup 里缓存着 chunk 指针，边读边做结构变更
				// 会让它失效），第二趟再统一写。
				// 不能用 using 声明：using 变量是只读的，NativeArray 的索引器是属性，
				// 对它赋值会报 CS1654，所以这里 try/finally 手动 Dispose。
				NativeArray<LocalTransform> localTransforms = new NativeArray<LocalTransform>(count, Allocator.Temp);
				NativeArray<bool> hasLocalTransform = new NativeArray<bool>(count, Allocator.Temp);
				try
				{
					for (int i = 0; i < count; i++)
					{
						Entity part = Simulator.Value.Parts[i];
						if (!EntityManager.HasComponent<LocalTransform>(part))
						{
							continue;
						}
						PartAspect aspect = PartAspectLookup[part];
						LocalTransform localTransform = EntityManager.GetComponentData<LocalTransform>(part);
						localTransform.Position = BasePart.MoveTo(in aspect);
						localTransform.Rotation = BasePart.RotateTo(ref aspect);
						localTransforms[i] = localTransform;
						hasLocalTransform[i] = true;
					}
					for (int i = 0; i < count; i++)
					{
						if (!hasLocalTransform[i])
						{
							continue;
						}
						Entity part = Simulator.Value.Parts[i];
						LocalTransform localTransform = localTransforms[i];
						EntityManager.SetComponentData(part, localTransform);
						WorldTransform worldTransform = (WorldTransform)localTransform;
						if (EntityManager.HasComponent<WorldTransform>(part))
						{
							EntityManager.SetComponentData(part, worldTransform);
						}
						else
						{
							EntityManager.AddComponentData(part, worldTransform);
						}
					}
				}
				finally
				{
					localTransforms.Dispose();
					hasLocalTransform.Dispose();
				}
			}
		}

		[BurstCompile]
		private struct BuildPartsJob : IJob
		{
			public ComponentLookup<PartSceneID> PartSceneIDLookup;

			public PartAspect.Lookup PartAspectLookup;

			[ReadOnly]
			public ComponentLookup<PartBuildLayer> LayerLookup;

			public Reference<PartSceneUnmanaged> Source;

			public Reference<PartSimulatorUnmanaged> Destination;

			public void Execute()
			{
				int length = Source.Value.Parts.Length;
				using NativeParallelHashMap<Entity, Entity> nativeParallelHashMap = new NativeParallelHashMap<Entity, Entity>(length, Allocator.Temp);
				for (int i = 0; i < length; i++)
				{
					Entity entity = Source.Value.Parts[i];
					PartAspect partAspect = PartAspectLookup[entity];
					int coordX = partAspect.CoordX;
					int coordY = partAspect.CoordY;
					int level = LayerLookup.TryGetComponent(entity, out var buildLayer) ? PartBuildLayer.Clamp(buildLayer.Value) : PartBuildLayer.MinLayer;
					Entity entity2 = Destination.Value.Parts[i];
					PartSceneIDLookup[entity2] = new PartSceneID
					{
						Value = -1
					};
					nativeParallelHashMap.Add(entity, entity2);
					Destination.Value.PartMap.TryGet(coordX, coordY, level, out var part);
					if (BasePart.IsContainerResolved(partAspect.BaseTypeInfo))
					{
						part.PartContainer = entity2;
						Destination.Value.PartMap.Set(coordX, coordY, level, part);
					}
					else
					{
						part.Part = entity2;
						Destination.Value.PartMap.Set(coordX, coordY, level, part);
					}
				}
				for (int j = 0; j < length; j++)
				{
					PartAspect partAspect2 = PartAspectLookup[Source.Value.Parts[j]];
					PartAspect partAspect3 = PartAspectLookup[Destination.Value.Parts[j]];
					if (partAspect2.ContainedPart != Entity.Null)
					{
						partAspect3.ContainedPart = nativeParallelHashMap[partAspect2.ContainedPart];
					}
					if (partAspect2.PartContainer != Entity.Null)
					{
						partAspect3.PartContainer = nativeParallelHashMap[partAspect2.PartContainer];
					}
				}
			}
		}

		/// <summary>
		/// 把建造期的自由缩放（<see cref="PartFreeTransform.Scale"/> × <see cref="LocalTransform"/>.Scale）
		/// 烘进模拟实体的碰撞体几何。
		/// <para>
		/// Unity.Physics 只在实体没有 <see cref="PostTransformScale"/> 时才会把 LocalTransform.Scale 交给
		/// RigidBody（PhysicsWorldBuilder.CreateRigidBodies），而本工程每个部件都带 PostTransformScale
		/// （自由缩放走渲染矩阵），于是模拟期碰撞体永远保持 prefab 原尺寸：缩小后相邻部件的间距变小、
		/// 碰撞体不变，互相穿插被求解器弹开，固定关节冲量超阈就断 —— 这正是"缩放后连接坏掉"。
		/// 这里在创建刚体之前按缩放重建几何（<see cref="PartPhysics.CreateScaled"/>），尺寸与质量分布
		/// 都跟着走，视觉和物理一致（就是"图片缩放"）。
		/// </para>
		/// <para>不能加 [BurstCompile]：要 new/Dispose blob 并调用 EntityManager。</para>
		/// </summary>
		private struct ApplyFreeScaleJob : IJob
		{
			public EntityManager EntityManager;

			[ReadOnly]
			public ComponentLookup<PartFreeTransform> FreeTransformLookup;

			[ReadOnly]
			public ComponentLookup<LocalTransform> LocalTransformLookup;

			[ReadOnly]
			public ComponentLookup<PhysicsCollider> PhysicsColliderLookup;

			public Reference<PartSimulatorUnmanaged> Simulator;

			public void Execute()
			{
				for (int i = 0; i < Simulator.Value.Parts.Length; i++)
				{
					Entity part = Simulator.Value.Parts[i];
					if (!PhysicsColliderLookup.HasComponent(part))
					{
						continue;
					}
					float2 scale = FreeTransformLookup.TryGetComponent(part, out var free) ? free.Scale : new float2(1f, 1f);
					float localScale = LocalTransformLookup.TryGetComponent(part, out var local) ? local.Scale : 1f;
					scale *= localScale;
					if (math.abs(scale.x - 1f) < 1e-4f && math.abs(scale.y - 1f) < 1e-4f)
					{
						continue;
					}
					BlobAssetReference<Unity.Physics.Collider> scaledCollider;
					if (EntityManager.HasComponent<FrameShape>(part) && FrameShapeSystem.FeatureEnabled && EntityManager.GetComponentData<FrameShape>(part).Value != 0)
						scaledCollider = FrameShapeSystem.BuildHull(EntityManager.GetComponentData<FrameShape>(part).Value, EntityManager.GetComponentData<FrameShapeOriginalCollider>(part).Value, scale);
					else
						scaledCollider = PartPhysics.CreateScaled(PhysicsColliderLookup[part].Value, scale);
					if (!scaledCollider.IsCreated)
					{
						// 形状暂不支持缩放（例如将来出现 MeshCollider），保留原碰撞体总好过没有。
						continue;
					}
					EntityManager.SetComponentData(part, new PhysicsCollider
					{
						Value = scaledCollider
					});
					Simulator.Value.TrackScaledCollider(scaledCollider);
				}
			}
		}

		[BurstCompile]
		private struct BuildRigidbodiesJob : IJob
		{
			public EntityCommandBuffer CommandBuffer;

			[ReadOnly]
			public ComponentLookup<LocalTransform> LocalTransformLookup;

			[ReadOnly]
			public ComponentLookup<PhysicsCollider> PhysicsColliderLookup;

			[ReadOnly]
			public ComponentLookup<PartTypeValue> PartTypeLookup;

			public Reference<PartSimulatorUnmanaged> Simulator;

			[ReadOnly]
			public NativeParallelHashMap<PartTypeInfo, PartExtensionData> ExtensionMap;

			public void Execute()
			{
				foreach (Entity part in Simulator.Value.Parts)
				{
					if (PhysicsColliderLookup.HasComponent(part))
					{
						PartTypeInfo value = PartTypeLookup[part].Value;
						bool kinematic = IsKinematic(value);
						float mass = value.PartType == PartType.Umbrella ? .5f : ExtensionMap[value].Mass;
						PartSimulatorUnmanaged.AddPhysicsComponents(CommandBuffer, LocalTransformLookup, PhysicsColliderLookup, part, kinematic, mass, 0.2f, 0f);
					}
				}
			}
		}

		private struct BuildJointsAndComponentsJob : IJob
		{
			public EntityCommandBuffer CommandBuffer;

			[ReadOnly]
			public ComponentLookup<WorldTransform> WorldTransformLookup;

			public PartAspect.Lookup PartAspectLookup;

			[ReadOnly]
			public ComponentLookup<PartBuildLayer> LayerLookup;

			public Reference<PartSimulatorUnmanaged> Simulator;

			public float ConnectionStrengthFactor;

			/// <summary>
			/// 是否真的建固定关节。巨构模式（<see cref="SimulationSettings.MegaStructure"/>）下
			/// 部件之间不焊关节（整车只有一具刚体，关节纯属浪费），但仍然要算连通块 ——
			/// 玩法/动力缓存/过滤统计都按连通块索引。
			/// </summary>
			public bool CreateJoints;

			public void Execute()
			{
				NativeList<Entity> parts = Simulator.Value.Parts;
				using NativeParallelHashMap<Entity, int> nativeParallelHashMap = new NativeParallelHashMap<Entity, int>(parts.Length, Allocator.Temp);
				for (int i = 0; i < parts.Length; i++)
				{
					nativeParallelHashMap.Add(parts[i], i);
				}
				using NativeDisjointSet disjointSet = new NativeDisjointSet(parts.Length, Allocator.Temp);
				for (int pi = 0; pi < parts.Length; pi++)
				{
					Entity part2 = parts[pi];
					PartAspect sourceAspect = PartAspectLookup[part2];
					int coordX = sourceAspect.CoordX;
					int coordY = sourceAspect.CoordY;
					int layer = LayerLookup.TryGetComponent(part2, out var buildLayer) ? PartBuildLayer.Clamp(buildLayer.Value) : PartBuildLayer.MinLayer;
					if (BasePart.IsContainerResolved(sourceAspect.BaseTypeInfo) && sourceAspect.ContainedPart != Entity.Null)
					{
						PartAspect partAspect = PartAspectLookup[sourceAspect.ContainedPart];
						float maxImpulse = ConnectionStrengthFactor * (sourceAspect.ExtensionData.ConnectionStrength + partAspect.ExtensionData.ConnectionStrength);
						if (CreateJoints)
						{
							PartSimulatorUnmanaged.AddFixedJoint(CommandBuffer, WorldTransformLookup, part2, sourceAspect.ContainedPart, maxImpulse, enableCollision: false);
						}
						disjointSet.Union(nativeParallelHashMap[part2], nativeParallelHashMap[sourceAspect.ContainedPart]);
					}
					for (int j = 0; j < 4; j++)
					{
						BitDirection direction = BitDirectionExtensions.FromIndex(j);
						var (num, num2) = direction.ToVector();
						if (!Simulator.Value.PartMap.TryGet(coordX + num, coordY + num2, layer, out var part))
						{
							continue;
						}
						for (int k = 0; k < 2; k++)
						{
							Entity entity = ((k == 0) ? part.PartContainer : part.Part);
							if (!(entity == Entity.Null))
							{
								PartAspect targetAspect = PartAspectLookup[entity];
								// ElectricalPart.CreateCustomJoints in BPLE welds facing electrical ports.
								// These cannot use the mechanical extension table: every electrical row is None.
								if (sourceAspect.PartType == PartType.ElectricalPart && targetAspect.PartType == PartType.ElectricalPart && part2.Index < entity.Index &&
									(CircuitPart.GetTerminals(sourceAspect.PartIndex, sourceAspect.Rotation, sourceAspect.Flipped) & direction) != 0 &&
									(CircuitPart.GetTerminals(targetAspect.PartIndex, targetAspect.Rotation, targetAspect.Flipped) & direction.Reverse()) != 0)
								{
									float electricalImpulse = ConnectionStrengthFactor * (sourceAspect.ExtensionData.ConnectionStrength + targetAspect.ExtensionData.ConnectionStrength);
									if (CreateJoints) PartSimulatorUnmanaged.AddFixedJoint(CommandBuffer, WorldTransformLookup, part2, entity, electricalImpulse, enableCollision: false);
									disjointSet.Union(nativeParallelHashMap[part2], nativeParallelHashMap[entity]);
									continue;
								}
								if (CanConnectTo(part2, in sourceAspect, entity, in targetAspect, direction))
								{
									float maxImpulse2 = ConnectionStrengthFactor * (sourceAspect.ExtensionData.ConnectionStrength + targetAspect.ExtensionData.ConnectionStrength);
									if (CreateJoints)
									{
										PartSimulatorUnmanaged.AddFixedJoint(CommandBuffer, WorldTransformLookup, part2, entity, maxImpulse2, enableCollision: false);
									}
									disjointSet.Union(nativeParallelHashMap[part2], nativeParallelHashMap[entity]);
								}
							}
						}
					}
					// 跨层连接（照 BPIR Contraption.ConnectPart + TryConnectSameCell 的原始规则）：
					// * 层 1 件没有跨层连接；
					// * 层 2..N 件先看「正下方」同格有没有层 1 件：
					//   - 有，就只连那一个（四个方向逐个试方向判定，命中即止）；
					//   - 没有，才回退去连层 1 的四邻域；
					// * 任何跨层连接都必须先过 CanConnectAcrossLayers 的方向判定。
					// 旧实现是「同格 + 四邻域全部无条件焊接」，会把方向不对的零件（只有上下端子的
					// 弹簧、油管之类）也焊进同一个刚体，这正是导入 BPIR 存档后「不该连的连上了」的来源。
					if (layer <= PartBuildLayer.MinLayer)
					{
						continue;
					}
					bool hasBelow = Simulator.Value.PartMap.TryGet(coordX, coordY, PartBuildLayer.MinLayer, out var belowCell) && (!(belowCell.PartContainer == Entity.Null) || !(belowCell.Part == Entity.Null));
					if (hasBelow)
					{
						for (int k2 = 0; k2 < 2; k2++)
						{
							Entity entity2 = ((k2 == 0) ? belowCell.PartContainer : belowCell.Part);
							if (entity2 == Entity.Null || entity2 == part2)
							{
								continue;
							}
							PartAspect targetAspect2 = PartAspectLookup[entity2];
							for (int j2 = 0; j2 < 4; j2++)
							{
								BitDirection direction2 = BitDirectionExtensions.FromIndex(j2);
								if (!CanConnectAcrossLayers(in sourceAspect, in targetAspect2, direction2))
								{
									continue;
								}
								float crossImpulse = ConnectionStrengthFactor * (sourceAspect.ExtensionData.ConnectionStrength + targetAspect2.ExtensionData.ConnectionStrength);
								if (CreateJoints)
								{
									PartSimulatorUnmanaged.AddFixedJoint(CommandBuffer, WorldTransformLookup, part2, entity2, crossImpulse, enableCollision: false);
								}
								disjointSet.Union(nativeParallelHashMap[part2], nativeParallelHashMap[entity2]);
								break;
							}
						}
						continue;
					}
					for (int j2 = 0; j2 < 4; j2++)
					{
						BitDirection direction2 = BitDirectionExtensions.FromIndex(j2);
						var (num3, num4) = direction2.ToVector();
						if (!Simulator.Value.PartMap.TryGet(coordX + num3, coordY + num4, PartBuildLayer.MinLayer, out var belowNeighbour))
						{
							continue;
						}
						for (int k2 = 0; k2 < 2; k2++)
						{
							Entity entity2 = ((k2 == 0) ? belowNeighbour.PartContainer : belowNeighbour.Part);
							if (entity2 == Entity.Null || entity2 == part2)
							{
								continue;
							}
							PartAspect targetAspect2 = PartAspectLookup[entity2];
							if (!CanConnectAcrossLayers(in sourceAspect, in targetAspect2, direction2))
							{
								continue;
							}
							float crossImpulse = ConnectionStrengthFactor * (sourceAspect.ExtensionData.ConnectionStrength + targetAspect2.ExtensionData.ConnectionStrength);
							if (CreateJoints)
							{
								PartSimulatorUnmanaged.AddFixedJoint(CommandBuffer, WorldTransformLookup, part2, entity2, crossImpulse, enableCollision: false);
							}
							disjointSet.Union(nativeParallelHashMap[part2], nativeParallelHashMap[entity2]);
						}
					}
				}
				PartSimulation.UpdateConnectedComponents(Simulator, PartAspectLookup, disjointSet);
			}
		}

		private Entity m_physicsStep;

		private EntityManager m_entityManager;

		private Reference<PartSimulatorUnmanaged> m_unmanaged;

		public ref PartSimulatorUnmanaged Unmanaged => ref m_unmanaged.Value;

		public Reference<PartSimulatorUnmanaged> UnmanagedRef => m_unmanaged;

		public void Run(Reference<PartSceneUnmanaged> partScene)
		{
			m_unmanaged = Reference<PartSimulatorUnmanaged>.Allocate(Allocator.Persistent);
			m_unmanaged.Value.Initialize(partScene);
			SimulationSettings simulationSettings = UserSettings.Instance.SimulationSettings;
			PartSceneSystem system = PartManager.Instance.System;
			EntityManager entityManager = (m_entityManager = partScene.Value.EntityManager);
			CreatePhysicsStep(entityManager);
			// 用 PartFactory.ToNativeExtensionMap：它会把 Mod 变体按基部件参数补进表里，
			// 否则变体在 ApplyPartsJob 里 ExtensionMap[value] 会抛 KeyNotFoundException。
			using NativeParallelHashMap<PartTypeInfo, PartExtensionData> extensionMap = PartManager.Instance.Factory.ToNativeExtensionMap(Allocator.TempJob);
			entityManager.CompleteAllTrackedJobs();
			IJobExtensions.Run(new InstantiatePartsJob
			{
				EntityManager = entityManager,
				Source = partScene,
				Destination = m_unmanaged
			});
			// 实例化出来的部件此刻还没有 WorldTransform（只有 TransformSystemGroup 之后的
			// LocalToWorldSystem 会补），而下面的 BuildJointsAndComponentsJob 是同一帧同步
			// Run 的，读 WorldTransform 会抛异常并中断整份 job。必须先补上；
			// 顺带把 LocalTransform 校正成落位后的位姿（PartTransformSystem 本帧还没跑）。
			IJobExtensions.Run(new SeedWorldTransformJob
			{
				EntityManager = entityManager,
				PartAspectLookup = new PartAspect.Lookup(ref system.CheckedStateRef, isReadOnly: false),
				Simulator = m_unmanaged
			});
			EntityCommandBuffer commandBuffer = new EntityCommandBuffer(Allocator.TempJob);
			IJobExtensions.Run(new ApplyFreeScaleJob
			{
				EntityManager = entityManager,
				FreeTransformLookup = system.GetComponentLookup<PartFreeTransform>(isReadOnly: true),
				LocalTransformLookup = system.GetComponentLookup<LocalTransform>(isReadOnly: true),
				PhysicsColliderLookup = system.GetComponentLookup<PhysicsCollider>(isReadOnly: true),
				Simulator = m_unmanaged
			});
			IJobExtensions.Run(new BuildPartsJob
			{
				PartSceneIDLookup = system.GetComponentLookup<PartSceneID>(),
				PartAspectLookup = new PartAspect.Lookup(ref system.CheckedStateRef, isReadOnly: false),
				LayerLookup = system.GetComponentLookup<PartBuildLayer>(isReadOnly: true),
				Source = partScene,
				Destination = m_unmanaged
			});
			bool mega = simulationSettings.MegaStructure;
			if (mega)
			{
				// Separators need independently movable connected components. A single
				// compound body cannot represent release, elastic links or team collisions.
				foreach (Entity part in m_unmanaged.Value.Parts)
				{
					if (entityManager.GetComponentData<PartTypeValue>(part).Value.PartType != PartType.Kicker) continue;
					mega = false;
					UnityEngine.Debug.Log("[PartSimulation] 分离器/队伍标记需要独立刚体，本次模拟停用巨构合并（不修改设置）。");
					break;
				}
			}
			if (!mega)
			{
				// 巨构模式下不给部件建刚体：一旦加了 PhysicsWorldIndex，20k 个部件就会成为
				// 2 万个 body + 18 万根关节的巨型求解岛，正是要省掉的那部分开销。
				// 部件需要的质量/速度组件由 MegaStructure.Build 直接补上（不含物理世界成员资格）。
				IJobExtensions.Run(new BuildRigidbodiesJob
				{
					CommandBuffer = commandBuffer,
					LocalTransformLookup = system.GetComponentLookup<LocalTransform>(isReadOnly: true),
					PhysicsColliderLookup = system.GetComponentLookup<PhysicsCollider>(isReadOnly: true),
					PartTypeLookup = system.GetComponentLookup<PartTypeValue>(isReadOnly: true),
					Simulator = m_unmanaged,
					ExtensionMap = extensionMap
				});
			}
			if (simulationSettings.CreateJoints || mega)
			{
				float num = (simulationSettings.InfiniteConnectionStrength ? float.PositiveInfinity : simulationSettings.ConnectionStrengthFactor);
				IJobExtensions.Run(new BuildJointsAndComponentsJob
				{
					CommandBuffer = commandBuffer,
					WorldTransformLookup = system.GetComponentLookup<WorldTransform>(isReadOnly: true),
					PartAspectLookup = new PartAspect.Lookup(ref system.CheckedStateRef, isReadOnly: false),
					LayerLookup = system.GetComponentLookup<PartBuildLayer>(isReadOnly: true),
					Simulator = m_unmanaged,
					ConnectionStrengthFactor = num * Time.fixedDeltaTime,
					CreateJoints = !mega
				});
				if (!mega)
				{
					system.World.GetOrCreateSystemManaged<FrameJointSystem>().SetDirty();
				}
			}
			commandBuffer.Playback(entityManager);
			commandBuffer.Dispose();
			// Enable pressure contacts before the first physics step and before mega
			// copies child colliders into its compound collider.
			foreach (Entity circuitPart in m_unmanaged.Value.Parts)
				if (entityManager.HasComponent<CircuitPart>(circuitPart) && entityManager.GetComponentData<CircuitPart>(circuitPart).Kind == CircuitKind.PressureSensor)
					CircuitPressureSetup.EnableEvents(circuitPart, entityManager, m_unmanaged.Value);
			if (mega)
			{
				// 巨构优化：把全部部件烘成一个复合碰撞体 + 一具刚体。
				// 部件实体不再拿到 PhysicsWorldIndex，因此不产生 body、不参与求解，
				// 也不会有关节/接触/连通块过滤这些开销 —— 这正是省下启动卡顿与运行时的部分。
				entityManager.CompleteAllTrackedJobs();
				MegaStructure.Build(entityManager, m_unmanaged, extensionMap, simulationSettings);
				return;
			}
			// 焊接完成的连通块内部不再需要碰撞检测：Unity.Physics 会按速度膨胀 AABB，
			// 大载具高速旋转时 1600+ 个部件的膨胀体会互相吞掉对方，宽相位与推测接触数量爆炸。
			// 但过滤是存在碰撞体 blob 里的，Instantiate 出来的部件共用 prefab 那份 blob
			// （1648 个部件只有 25 份），所以必须先给每个部件换一份独占 blob，否则改一个等于改一片。
			// 想恢复「连通块内部也互撞」的原生行为，把 SimulationSettings.PartSelfCollision 设成 true。
			if (!simulationSettings.PartSelfCollision)
			{
				PartCollisionFilter.IsolateColliders(entityManager, m_unmanaged.Value);
				PartCollisionFilter.Apply(entityManager, m_unmanaged.Value);
			}
		}

		private void CreatePhysicsStep(EntityManager entityManager)
		{
			SimulationSettings simulationSettings = UserSettings.Instance.SimulationSettings;
			Entity entity = entityManager.CreateEntity();
			PhysicsStep componentData = PhysicsStep.Default;
			componentData.SimulationType = simulationSettings.SimulationType;
			componentData.Gravity = new float3(simulationSettings.GravityX, simulationSettings.GravityY, 0f);
			componentData.SolverIterationCount = simulationSettings.SolverIterationCount;
			entityManager.AddComponentData(entity, componentData);
			m_physicsStep = entity;
		}

		public void Dispose()
		{
			MegaStructure.Dispose(m_entityManager);
			m_entityManager.DestroyEntity(m_physicsStep);
			m_unmanaged.Value.Dispose(m_entityManager);
			Reference<PartSimulatorUnmanaged>.Free(m_unmanaged, Allocator.Persistent);
		}

		public static bool IsKinematic(PartTypeInfo info)
		{
			// 绳索 0-3：BPSS Rope.Awake 里 rigidbody.isKinematic = true / mass = 0，
			// 绳子本身钉在建造位置上，只有内部节点链在动（bpsim 侧由 RopeSystem 的
			// Verlet 求解负责），所以这里必须冻结，否则它会带着 1kg 质量往下掉。
			// 4-7 是铰接板（HingePlate），原版是普通动态刚体，保持不动。
			// 木框（WoodenFrame）在原版同样是**动态**刚体：BPSS BasePart.EnsureRigidbody
			// 给所有部件加动态 Rigidbody（useGravity=true、有 mass、没有 isKinematic），
			// 只有 Rope.cs 在 Start 里把自己设成 kinematic + mass 0。以前这里把木框
			// 一起冻住，含木框的存档（如「不败荣光」的 6 个 type=5）整个连通刚体会被
			// 钉死在原地；「bpsim1号机」没有木框，所以看不出问题。
			return Rope.IsRope(info);
		}

		public static bool CanConnectTo(Entity source, in PartAspect sourceAspect, Entity target, in PartAspect targetAspect, BitDirection direction)
		{
			if (!sourceAspect.ExtensionData.IsConnectionSource)
			{
				return false;
			}
			if ((sourceAspect.ExtensionData.ConnectionDirection.Rotate(sourceAspect.Rotation) & direction) == 0)
			{
				return false;
			}
			// Ordinary electronics accept chassis joints on any side; wires and point
			// charges only have the explicit electrical/container joints built above.
			BitDirection targetDirection = targetAspect.PartType == PartType.ElectricalPart
				&& targetAspect.PartIndex >= 4 && targetAspect.PartIndex != 42
				? BitDirection.Any : targetAspect.ExtensionData.ConnectionDirection.Rotate(targetAspect.Rotation);
			if ((targetDirection & direction.Reverse()) == 0)
			{
				return false;
			}
			if (!targetAspect.ExtensionData.IsConnectionSource)
			{
				return true;
			}
			return source.Index < target.Index;
		}

		/// <summary>
		/// 跨层连接用的方向判定。与 <see cref="CanConnectTo" /> 唯一的区别是**不做
		/// <c>source.Index &lt; target.Index</c> 去重**：那条去重是为同层四邻域准备的
		/// （一对零件会被双方各访问一次），而跨层连接只有高层那一侧会发起扫描
		/// （BPIR <c>Contraption.ConnectPart</c> 同样只在 <c>TryConnectSameCell</c> /
		/// 四邻域回退里单向发起），套上索引去重会让「两块机架叠在不同层」永远连不上。
		/// 方向语义与 BPIR <c>Contraption.CanConnectTo(part1, part2, direction)</c> 一致：
		/// 发起方要含 <paramref name="direction" />，对方要含反方向。
		/// </summary>
		public static bool CanConnectAcrossLayers(in PartAspect sourceAspect, in PartAspect targetAspect, BitDirection direction)
		{
			if (!sourceAspect.ExtensionData.IsConnectionSource)
			{
				return false;
			}
			if ((sourceAspect.ExtensionData.ConnectionDirection.Rotate(sourceAspect.Rotation) & direction) == 0)
			{
				return false;
			}
			BitDirection targetDirection = targetAspect.PartType == PartType.ElectricalPart
				&& targetAspect.PartIndex >= 4 && targetAspect.PartIndex != 42
				? BitDirection.Any : targetAspect.ExtensionData.ConnectionDirection.Rotate(targetAspect.Rotation);
			return (targetDirection & direction.Reverse()) != 0;
		}
	}
}
