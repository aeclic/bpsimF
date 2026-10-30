using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace Bpsim.Parts
{
	internal partial class ActiveSceneSystem : SystemBase
	{
		[NoAlias]
		[BurstCompile(FloatMode = FloatMode.Default, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = false)]
		private struct ActiveSceneSystem_3F833320_LambdaJob_0_Job : IJobChunk
		{
			public int id;

			[ReadOnly]
			public EntityTypeHandle __entityTypeHandle;

			public EntityCommandBuffer __entityCommandBuffer;

			[ReadOnly]
			public ComponentTypeHandle<PartSceneID> __sceneIDTypeHandle;

			public BufferTypeHandle<LinkedEntityGroup> __linkedEntityGroupTypeHandle;

			[ReadOnly]
			public ComponentLookup<Disabled> __Unity_Entities_Disabled_ComponentLookup;

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			private void OriginalLambdaBody(Entity entity, [NoAlias] in PartSceneID sceneID, DynamicBuffer<LinkedEntityGroup> linkedEntityGroup)
			{
				bool flag = sceneID.Value == id;
				if (flag != __Unity_Entities_Disabled_ComponentLookup.HasComponent(entity))
				{
					return;
				}
				foreach (Entity item in linkedEntityGroup.Reinterpret<Entity>())
				{
					if (flag)
					{
						__entityCommandBuffer.RemoveComponent<Disabled>(item);
					}
					else
					{
						__entityCommandBuffer.AddComponent<Disabled>(item);
					}
				}
			}

			[CompilerGenerated]
			public void Execute(in ArchetypeChunk chunk, int batchIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				IntPtr nativeArrayPtr = InternalCompilerInterface.UnsafeGetChunkEntityArrayIntPtr(chunk, __entityTypeHandle);
				IntPtr nativeArrayPtr2 = InternalCompilerInterface.UnsafeGetChunkNativeArrayReadOnlyIntPtr(chunk, ref __sceneIDTypeHandle);
				BufferAccessor<LinkedEntityGroup> bufferAccessor = chunk.GetBufferAccessor(ref __linkedEntityGroupTypeHandle);
				int count = chunk.Count;
				if (!useEnabledMask)
				{
					for (int i = 0; i < count; i++)
					{
						OriginalLambdaBody(InternalCompilerInterface.UnsafeGetCopyOfNativeArrayPtrElement<Entity>(nativeArrayPtr, i), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PartSceneID>(nativeArrayPtr2, i), bufferAccessor[i]);
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
							OriginalLambdaBody(InternalCompilerInterface.UnsafeGetCopyOfNativeArrayPtrElement<Entity>(nativeArrayPtr, j), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PartSceneID>(nativeArrayPtr2, j), bufferAccessor[j]);
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
						OriginalLambdaBody(InternalCompilerInterface.UnsafeGetCopyOfNativeArrayPtrElement<Entity>(nativeArrayPtr, k), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PartSceneID>(nativeArrayPtr2, k), bufferAccessor[k]);
					}
					num >>= 1;
				}
				num = chunkEnabledMask.ULong1;
				for (int l = 64; l < count; l++)
				{
					if ((num & 1) != 0L)
					{
						OriginalLambdaBody(InternalCompilerInterface.UnsafeGetCopyOfNativeArrayPtrElement<Entity>(nativeArrayPtr, l), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PartSceneID>(nativeArrayPtr2, l), bufferAccessor[l]);
					}
					num >>= 1;
				}
			}

			void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				Execute(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
			}
		}

		private int m_lastID;

		private EndSimulationEntityCommandBufferSystem __Unity_Entities_EndSimulationEntityCommandBufferSystem;

		private EntityTypeHandle __Unity_Entities_Entity_TypeHandle;

		private ComponentTypeHandle<PartSceneID> __Bpsim_Parts_PartSceneID_RO_ComponentTypeHandle;

		private BufferTypeHandle<LinkedEntityGroup> __Unity_Entities_LinkedEntityGroup_RO_BufferTypeHandle;

		private ComponentLookup<Disabled> __Unity_Entities_Disabled_RO_ComponentLookup;

		private EntityQuery __query_1877468374_0;

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
			int num = 0;
			if (PartManager.Instance.IsSimulating)
			{
				num = -1;
			}
			else if (PartManager.Instance.HasActiveScene())
			{
				num = PartManager.Instance.ActiveScene.SceneID;
			}
			if (m_lastID != num)
			{
				m_lastID = num;
				ActiveSceneSystem_3F833320_LambdaJob_0_Execute(num);
			}
		}

		private void ActiveSceneSystem_3F833320_LambdaJob_0_Execute(int id)
		{
			__Unity_Entities_Entity_TypeHandle.Update(ref base.CheckedStateRef);
			__Bpsim_Parts_PartSceneID_RO_ComponentTypeHandle.Update(ref base.CheckedStateRef);
			__Unity_Entities_LinkedEntityGroup_RO_BufferTypeHandle.Update(ref base.CheckedStateRef);
			__Unity_Entities_Disabled_RO_ComponentLookup.Update(ref base.CheckedStateRef);
			ActiveSceneSystem_3F833320_LambdaJob_0_Job jobData = new ActiveSceneSystem_3F833320_LambdaJob_0_Job
			{
				id = id,
				__entityTypeHandle = __Unity_Entities_Entity_TypeHandle,
				__entityCommandBuffer = __Unity_Entities_EndSimulationEntityCommandBufferSystem.CreateCommandBuffer(),
				__sceneIDTypeHandle = __Bpsim_Parts_PartSceneID_RO_ComponentTypeHandle,
				__linkedEntityGroupTypeHandle = __Unity_Entities_LinkedEntityGroup_RO_BufferTypeHandle,
				__Unity_Entities_Disabled_ComponentLookup = __Unity_Entities_Disabled_RO_ComponentLookup
			};
			base.Dependency = InternalCompilerInterface.JobChunkInterface.Schedule(jobData, __query_1877468374_0, base.Dependency);
			__Unity_Entities_EndSimulationEntityCommandBufferSystem.AddJobHandleForProducer(base.Dependency);
		}

		private void __AssignHandlesImpl(ref SystemState state)
		{
			__query_1877468374_0 = state.GetEntityQuery(new EntityQueryDesc
			{
				All = new ComponentType[3]
				{
					ComponentType.ReadOnly<PartSceneID>(),
					ComponentType.ReadOnly<LinkedEntityGroup>(),
					ComponentType.ReadOnly<PartTag>()
				},
				Any = new ComponentType[0],
				None = new ComponentType[0],
				Disabled = new ComponentType[0],
				Absent = new ComponentType[0],
				Options = EntityQueryOptions.IncludeDisabledEntities
			});
			__Unity_Entities_Entity_TypeHandle = state.GetEntityTypeHandle();
			__Bpsim_Parts_PartSceneID_RO_ComponentTypeHandle = state.GetComponentTypeHandle<PartSceneID>(isReadOnly: true);
			__Unity_Entities_LinkedEntityGroup_RO_BufferTypeHandle = state.GetBufferTypeHandle<LinkedEntityGroup>(isReadOnly: true);
			__Unity_Entities_Disabled_RO_ComponentLookup = state.GetComponentLookup<Disabled>(isReadOnly: true);
		}

		protected override void OnCreateForCompiler()
		{
			base.OnCreateForCompiler();
			__AssignHandlesImpl(ref base.CheckedStateRef);
			__Unity_Entities_EndSimulationEntityCommandBufferSystem = base.World.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
		}

		[Preserve]
		public ActiveSceneSystem()
		{
		}
	}
}
