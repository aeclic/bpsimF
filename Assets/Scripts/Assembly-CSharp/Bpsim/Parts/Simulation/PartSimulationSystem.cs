using System;
using System.Runtime.CompilerServices;
using Bpsim.Collections;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using UnityEngine.Scripting;

namespace Bpsim.Parts.Simulation
{
	[UpdateInGroup(typeof(AfterPhysicsSystemGroup))]
	internal partial class PartSimulationSystem : SystemBase
	{
		[BurstCompile]
		private struct ImpulseEventJob : IImpulseEventsJob, IImpulseEventsJobBase
		{
			public NativeParallelHashSet<Entity> Joints;

			public void Execute(ImpulseEvent impulseEvent)
			{
				Joints.Add(impulseEvent.JointEntity);
			}
		}

		[BurstCompile]
		private struct RemoveJointsJob : IJob
		{
			public EntityCommandBuffer CommandBuffer;

			public ComponentLookup<PhysicsConstrainedBodyPair> ConstrainedBodyPairLookup;

			public NativeReference<bool> Dirty;

			[ReadOnly]
			public NativeParallelHashSet<Entity> DeletedJoints;

			public Reference<PartSimulatorUnmanaged> Simulator;

			public void Execute()
			{
				if (DeletedJoints.Count() == 0)
				{
					return;
				}
				Dirty.Value = true;
				NativeList<Entity> joints = Simulator.Value.Joints;
				int length = joints.Length;
				int i = 0;
				int length2 = 0;
				while (i < length)
				{
					for (; i < length && DeletedJoints.Contains(joints[i]); i++)
					{
					}
					if (i < length)
					{
						joints[length2++] = joints[i++];
					}
				}
				joints.Resize(length2, NativeArrayOptions.UninitializedMemory);
				foreach (Entity deletedJoint in DeletedJoints)
				{
					CommandBuffer.DestroyEntity(deletedJoint);
				}
			}
		}

		[BurstCompile]
		private struct UpdateComponentsJob : IJob
		{
			public NativeReference<bool> Dirty;

			public Reference<PartSimulatorUnmanaged> Simulator;

			[ReadOnly]
			public ComponentLookup<PhysicsConstrainedBodyPair> ConstrainedBodyPairLookup;

			public ComponentLookup<PartConnectedComponent> PartConnectedComponentLookup;

			[ReadOnly]
			public ComponentLookup<PartExtensionComponent> PartExtensionComponentLookup;

			public ComponentLookup<PhysicsCollider> PhysicsColliderLookup;

			[ReadOnly]
			public ComponentLookup<PartIsolatedCollider> IsolatedColliderLookup;

			public void Execute()
			{
				if (Dirty.Value)
				{
					NativeDisjointSet disjointSet = PartSimulation.FindConnectedComponents(Simulator, ConstrainedBodyPairLookup);
					int componentCount;
					NativeArray<int> componentIndexes = disjointSet.GetComponentIndexes(Allocator.Temp, out componentCount);
					PartSimulation.UpdateConnectedComponents(Simulator, PartConnectedComponentLookup, PartExtensionComponentLookup, componentIndexes, componentCount);
					// 关节被拉断后连通块会重新切分，碰撞过滤必须跟着更新：同一连通块内部
					// 不需要碰撞（见 PartCollisionFilter），断了以后碎片之间要重新互撞。
					PartCollisionFilter.ApplyInJob(Simulator.Value.Parts, componentIndexes, ref PhysicsColliderLookup, ref IsolatedColliderLookup);
					componentIndexes.Dispose();
					disjointSet.Dispose();
				}
			}
		}

		[NoAlias]
		[BurstCompile(FloatMode = FloatMode.Default, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = false)]
		private struct PartSimulationSystem_2DE0C1DC_LambdaJob_0_Job : IJobChunk
		{
			public NativeReference<bool> dirty;

			public Reference<PartSimulatorUnmanaged> simulator;

			[ReadOnly]
			public EntityTypeHandle __entityTypeHandle;

			public ComponentTypeHandle<PartJointInfo> __jointInfoTypeHandle;

			[ReadOnly]
			public ComponentTypeHandle<PhysicsConstrainedBodyPair> __constrainedBodyPairTypeHandle;

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			private void OriginalLambdaBody(Entity entity, [NoAlias] ref PartJointInfo jointInfo, [NoAlias] in PhysicsConstrainedBodyPair constrainedBodyPair)
			{
				if (jointInfo.State == 0)
				{
					dirty.Value = true;
					jointInfo.State = 1;
					simulator.Value.AddJoint(constrainedBodyPair.EntityA, constrainedBodyPair.EntityB, entity);
				}
			}

			[CompilerGenerated]
			public void Execute(in ArchetypeChunk chunk, int batchIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				IntPtr nativeArrayPtr = InternalCompilerInterface.UnsafeGetChunkEntityArrayIntPtr(chunk, __entityTypeHandle);
				IntPtr nativeArrayPtr2 = InternalCompilerInterface.UnsafeGetChunkNativeArrayIntPtr(chunk, ref __jointInfoTypeHandle);
				IntPtr nativeArrayPtr3 = InternalCompilerInterface.UnsafeGetChunkNativeArrayReadOnlyIntPtr(chunk, ref __constrainedBodyPairTypeHandle);
				int count = chunk.Count;
				if (!useEnabledMask)
				{
					for (int i = 0; i < count; i++)
					{
						OriginalLambdaBody(InternalCompilerInterface.UnsafeGetCopyOfNativeArrayPtrElement<Entity>(nativeArrayPtr, i), ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PartJointInfo>(nativeArrayPtr2, i), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PhysicsConstrainedBodyPair>(nativeArrayPtr3, i));
					}
					return;
				}
				if (math.countbits(chunkEnabledMask.ULong0 ^ (chunkEnabledMask.ULong0 << 1)) + math.countbits(chunkEnabledMask.ULong1 ^ (chunkEnabledMask.ULong1 << 1)) - 1 <= 4)
				{
					v128 mask = chunkEnabledMask;
					int j = 0;
					int endIndex = 0;
					while (EnabledBitUtility.GetNextRange(ref mask, ref j, ref endIndex))
					{
						for (; j < endIndex; j++)
						{
							OriginalLambdaBody(InternalCompilerInterface.UnsafeGetCopyOfNativeArrayPtrElement<Entity>(nativeArrayPtr, j), ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PartJointInfo>(nativeArrayPtr2, j), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PhysicsConstrainedBodyPair>(nativeArrayPtr3, j));
						}
					}
					return;
				}
				ulong num = chunkEnabledMask.ULong0;
				int num2 = math.min(64, count);
				for (int k = 0; k < num2; k++)
				{
					if ((num & 1) != 0L)
					{
						OriginalLambdaBody(InternalCompilerInterface.UnsafeGetCopyOfNativeArrayPtrElement<Entity>(nativeArrayPtr, k), ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PartJointInfo>(nativeArrayPtr2, k), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PhysicsConstrainedBodyPair>(nativeArrayPtr3, k));
					}
					num >>= 1;
				}
				num = chunkEnabledMask.ULong1;
				for (int l = 64; l < count; l++)
				{
					if ((num & 1) != 0L)
					{
						OriginalLambdaBody(InternalCompilerInterface.UnsafeGetCopyOfNativeArrayPtrElement<Entity>(nativeArrayPtr, l), ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PartJointInfo>(nativeArrayPtr2, l), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PhysicsConstrainedBodyPair>(nativeArrayPtr3, l));
					}
					num >>= 1;
				}
			}

			void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				Execute(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
			}
		}

		private EntityCommandBufferSystem m_commandBufferSystem;

		private EntityTypeHandle __Unity_Entities_Entity_TypeHandle;

		private ComponentTypeHandle<PartJointInfo> __Bpsim_Parts_Simulation_PartJointInfo_RW_ComponentTypeHandle;

		private ComponentTypeHandle<PhysicsConstrainedBodyPair> __Unity_Physics_PhysicsConstrainedBodyPair_RO_ComponentTypeHandle;

		private ComponentLookup<PhysicsConstrainedBodyPair> __Unity_Physics_PhysicsConstrainedBodyPair_RO_ComponentLookup;

		private ComponentLookup<PartConnectedComponent> __Bpsim_Parts_PartConnectedComponent_RW_ComponentLookup;

		private ComponentLookup<PartExtensionComponent> __Bpsim_Parts_PartExtensionComponent_RO_ComponentLookup;

		private ComponentLookup<PhysicsConstrainedBodyPair> __Unity_Physics_PhysicsConstrainedBodyPair_RW_ComponentLookup;

		private ComponentLookup<PhysicsCollider> __Unity_Physics_PhysicsCollider_RW_ComponentLookup;

		private ComponentLookup<PartIsolatedCollider> __Bpsim_Parts_Simulation_PartIsolatedCollider_RO_ComponentLookup;

		private EntityQuery __query_125383276_0;

		private EntityQuery __query_125383276_1;

		[Preserve]
		protected override void OnCreate()
		{
			m_commandBufferSystem = base.World.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
		}

		[Preserve]
		protected override void OnUpdate()
		{
			// The default world ticks these systems before PartManager.Awake (and for one frame
			// after it is destroyed on play-mode exit); Instance is null then, and touching it
			// threw NullReferenceException once per system per frame.
			if (PartManager.Instance == null)
			{
				return;
			}
			if (PartManager.Instance.IsSimulating)
			{
				JobHandle dependency = base.Dependency;
				NativeReference<bool> dirty = new NativeReference<bool>(Allocator.TempJob);
				Reference<PartSimulatorUnmanaged> unmanagedRef = PartManager.Instance.PartSimulator.UnmanagedRef;
				dependency = UpdateJoints(dependency, dirty);
				__Bpsim_Parts_PartExtensionComponent_RO_ComponentLookup.Update(ref base.CheckedStateRef);
				__Bpsim_Parts_PartConnectedComponent_RW_ComponentLookup.Update(ref base.CheckedStateRef);
				__Unity_Physics_PhysicsConstrainedBodyPair_RO_ComponentLookup.Update(ref base.CheckedStateRef);
				__Unity_Physics_PhysicsCollider_RW_ComponentLookup.Update(ref base.CheckedStateRef);
				__Bpsim_Parts_Simulation_PartIsolatedCollider_RO_ComponentLookup.Update(ref base.CheckedStateRef);
				dependency = IJobExtensions.Schedule(new UpdateComponentsJob
				{
					Dirty = dirty,
					Simulator = unmanagedRef,
					ConstrainedBodyPairLookup = __Unity_Physics_PhysicsConstrainedBodyPair_RO_ComponentLookup,
					PartConnectedComponentLookup = __Bpsim_Parts_PartConnectedComponent_RW_ComponentLookup,
					PartExtensionComponentLookup = __Bpsim_Parts_PartExtensionComponent_RO_ComponentLookup,
					PhysicsColliderLookup = __Unity_Physics_PhysicsCollider_RW_ComponentLookup,
					IsolatedColliderLookup = __Bpsim_Parts_Simulation_PartIsolatedCollider_RO_ComponentLookup
				}, dependency);
				// Editor port: `dirty` is a TempJob allocation and must be released or every frame
				// trips JobTempAlloc's 4-frame lifespan check.
				base.Dependency = dirty.Dispose(dependency);
			}
		}

		private JobHandle UpdateJoints(JobHandle dependency, NativeReference<bool> dirty)
		{
			Reference<PartSimulatorUnmanaged> unmanagedRef = PartManager.Instance.PartSimulator.UnmanagedRef;
			EntityCommandBuffer commandBuffer = m_commandBufferSystem.CreateCommandBuffer();
			dependency = PartSimulationSystem_2DE0C1DC_LambdaJob_0_Execute(dirty, unmanagedRef, dependency);
			NativeParallelHashSet<Entity> nativeParallelHashSet = new NativeParallelHashSet<Entity>(256, Allocator.TempJob);
			// Havok 移植：工程现在按发布版的方式编译 Unity.Physics（HAVOK_PHYSICS_EXISTS），
			// 因此冲量事件调度走 Havok.Physics 提供的 IHavokImpulseEventsJobExtensions.Schedule，
			// 与发布版一致（见 BPSIM_HAVOK_PORT.md）。
			dependency = IHavokImpulseEventsJobExtensions.Schedule(new ImpulseEventJob
			{
				Joints = nativeParallelHashSet
			}, __query_125383276_1.GetSingleton<SimulationSingleton>(), dependency);
			__Unity_Physics_PhysicsConstrainedBodyPair_RW_ComponentLookup.Update(ref base.CheckedStateRef);
			dependency = IJobExtensions.Schedule(new RemoveJointsJob
			{
				CommandBuffer = commandBuffer,
				ConstrainedBodyPairLookup = __Unity_Physics_PhysicsConstrainedBodyPair_RW_ComponentLookup,
				Dirty = dirty,
				DeletedJoints = nativeParallelHashSet,
				Simulator = unmanagedRef
			}, dependency);
			dependency = nativeParallelHashSet.Dispose(dependency);
			m_commandBufferSystem.AddJobHandleForProducer(dependency);
			return dependency;
		}

		private JobHandle PartSimulationSystem_2DE0C1DC_LambdaJob_0_Execute(NativeReference<bool> dirty, Reference<PartSimulatorUnmanaged> simulator, JobHandle __inputDependency)
		{
			__Unity_Entities_Entity_TypeHandle.Update(ref base.CheckedStateRef);
			__Bpsim_Parts_Simulation_PartJointInfo_RW_ComponentTypeHandle.Update(ref base.CheckedStateRef);
			__Unity_Physics_PhysicsConstrainedBodyPair_RO_ComponentTypeHandle.Update(ref base.CheckedStateRef);
			return InternalCompilerInterface.JobChunkInterface.Schedule(new PartSimulationSystem_2DE0C1DC_LambdaJob_0_Job
			{
				dirty = dirty,
				simulator = simulator,
				__entityTypeHandle = __Unity_Entities_Entity_TypeHandle,
				__jointInfoTypeHandle = __Bpsim_Parts_Simulation_PartJointInfo_RW_ComponentTypeHandle,
				__constrainedBodyPairTypeHandle = __Unity_Physics_PhysicsConstrainedBodyPair_RO_ComponentTypeHandle
			}, __query_125383276_0, __inputDependency);
		}

		private void __AssignHandlesImpl(ref SystemState state)
		{
			__query_125383276_0 = state.GetEntityQuery(new EntityQueryDesc
			{
				All = new ComponentType[2]
				{
					ComponentType.ReadOnly<PhysicsConstrainedBodyPair>(),
					ComponentType.ReadWrite<PartJointInfo>()
				},
				Any = new ComponentType[0],
				None = new ComponentType[0],
				Disabled = new ComponentType[0],
				Absent = new ComponentType[0],
				Options = EntityQueryOptions.Default
			});
			__query_125383276_1 = state.GetEntityQuery(new EntityQueryDesc
			{
				All = new ComponentType[1] { ComponentType.ReadOnly<SimulationSingleton>() },
				Any = new ComponentType[0],
				None = new ComponentType[0],
				Disabled = new ComponentType[0],
				Absent = new ComponentType[0],
				Options = EntityQueryOptions.IncludeSystems
			});
			__Unity_Entities_Entity_TypeHandle = state.GetEntityTypeHandle();
			__Bpsim_Parts_Simulation_PartJointInfo_RW_ComponentTypeHandle = state.GetComponentTypeHandle<PartJointInfo>();
			__Unity_Physics_PhysicsConstrainedBodyPair_RO_ComponentTypeHandle = state.GetComponentTypeHandle<PhysicsConstrainedBodyPair>(isReadOnly: true);
			__Unity_Physics_PhysicsConstrainedBodyPair_RO_ComponentLookup = state.GetComponentLookup<PhysicsConstrainedBodyPair>(isReadOnly: true);
			__Bpsim_Parts_PartConnectedComponent_RW_ComponentLookup = state.GetComponentLookup<PartConnectedComponent>();
			__Bpsim_Parts_PartExtensionComponent_RO_ComponentLookup = state.GetComponentLookup<PartExtensionComponent>(isReadOnly: true);
			__Unity_Physics_PhysicsConstrainedBodyPair_RW_ComponentLookup = state.GetComponentLookup<PhysicsConstrainedBodyPair>();
			// Editor port: the collision-filter category is refreshed with the connected components,
			// so this system needs a writable PhysicsCollider lookup of its own.  Declaring it here (the
			// way generated systems do) is what registers the component with the safety handles; a bare
			// field + Update() dereferences an unregistered type index and throws every frame.
			__Unity_Physics_PhysicsCollider_RW_ComponentLookup = state.GetComponentLookup<PhysicsCollider>();
			__Bpsim_Parts_Simulation_PartIsolatedCollider_RO_ComponentLookup = state.GetComponentLookup<PartIsolatedCollider>(isReadOnly: true);
		}

		protected override void OnCreateForCompiler()
		{
			base.OnCreateForCompiler();
			__AssignHandlesImpl(ref base.CheckedStateRef);
		}

		[Preserve]
		public PartSimulationSystem()
		{
		}
	}
}
