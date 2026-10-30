using System;
using System.Runtime.CompilerServices;
using Bpsim.Rendering;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace Bpsim.Parts
{
	internal partial class SeparatorSystem : SystemBase
	{
		[NoAlias]
		[BurstCompile(FloatMode = FloatMode.Default, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = false)]
		private struct SeparatorSystem_680A320D_LambdaJob_0_Job : IJobChunk
		{
			public ComponentLookup<SpriteID> spriteIDLookup;

			[ReadOnly]
			public NativeParallelHashMap<int2, int> spriteMap;

			public ComponentTypeHandle<Separator> __separatorTypeHandle;

			[ReadOnly]
			public ComponentTypeHandle<PartTypeValue> __partTypeTypeHandle;

			[ReadOnly]
			public ComponentTypeHandle<PartTransform> __partTransformTypeHandle;

			public BufferTypeHandle<LinkedEntityGroup> __linkedEntityGroupTypeHandle;

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			private void OriginalLambdaBody([NoAlias] ref Separator separator, [NoAlias] in PartTypeValue partType, [NoAlias] in PartTransform partTransform, DynamicBuffer<LinkedEntityGroup> linkedEntityGroup)
			{
				int value = spriteMap[new int2(partType.Index, partTransform.Rotation)];
				spriteIDLookup[linkedEntityGroup[1].Value] = new SpriteID
				{
					Value = value
				};
			}

			[CompilerGenerated]
			public void Execute(in ArchetypeChunk chunk, int batchIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				IntPtr nativeArrayPtr = InternalCompilerInterface.UnsafeGetChunkNativeArrayIntPtr(chunk, ref __separatorTypeHandle);
				IntPtr nativeArrayPtr2 = InternalCompilerInterface.UnsafeGetChunkNativeArrayReadOnlyIntPtr(chunk, ref __partTypeTypeHandle);
				IntPtr nativeArrayPtr3 = InternalCompilerInterface.UnsafeGetChunkNativeArrayReadOnlyIntPtr(chunk, ref __partTransformTypeHandle);
				BufferAccessor<LinkedEntityGroup> bufferAccessor = chunk.GetBufferAccessor(ref __linkedEntityGroupTypeHandle);
				int count = chunk.Count;
				if (!useEnabledMask)
				{
					for (int i = 0; i < count; i++)
					{
						OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<Separator>(nativeArrayPtr, i), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PartTypeValue>(nativeArrayPtr2, i), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PartTransform>(nativeArrayPtr3, i), bufferAccessor[i]);
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
							OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<Separator>(nativeArrayPtr, j), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PartTypeValue>(nativeArrayPtr2, j), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PartTransform>(nativeArrayPtr3, j), bufferAccessor[j]);
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
						OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<Separator>(nativeArrayPtr, k), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PartTypeValue>(nativeArrayPtr2, k), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PartTransform>(nativeArrayPtr3, k), bufferAccessor[k]);
					}
					num >>= 1;
				}
				num = chunkEnabledMask.ULong1;
				for (int l = 64; l < count; l++)
				{
					if ((num & 1) != 0L)
					{
						OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<Separator>(nativeArrayPtr, l), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PartTypeValue>(nativeArrayPtr2, l), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PartTransform>(nativeArrayPtr3, l), bufferAccessor[l]);
					}
					num >>= 1;
				}
			}

			void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				Execute(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
			}
		}

		private NativeParallelHashMap<int2, int> m_spriteMap;

		private ComponentTypeHandle<Separator> __Bpsim_Parts_Separator_RW_ComponentTypeHandle;

		private ComponentTypeHandle<PartTypeValue> __Bpsim_Parts_PartTypeValue_RO_ComponentTypeHandle;

		private ComponentTypeHandle<PartTransform> __Bpsim_Parts_PartTransform_RO_ComponentTypeHandle;

		private BufferTypeHandle<LinkedEntityGroup> __Unity_Entities_LinkedEntityGroup_RO_BufferTypeHandle;

		private ComponentLookup<SpriteID> __Bpsim_Rendering_SpriteID_RW_ComponentLookup;

		private EntityQuery __query_697681675_0;

		[Preserve]
		protected override void OnStartRunning()
		{
			m_spriteMap = new NativeParallelHashMap<int2, int>(64, Allocator.Persistent);
			for (int i = 0; i < 5; i++)
			{
				for (int j = 0; j < 4; j++)
				{
					try
					{
						m_spriteMap[new int2(i, j)] = SpriteManager.Instance.GetID($"Part_Kicker_{i}_{j + 1}");
					}
					catch
					{
					}
				}
			}
		}

		[Preserve]
		protected override void OnUpdate()
		{
			__Bpsim_Rendering_SpriteID_RW_ComponentLookup.Update(ref base.CheckedStateRef);
			ComponentLookup<SpriteID> _Bpsim_Rendering_SpriteID_RW_ComponentLookup = __Bpsim_Rendering_SpriteID_RW_ComponentLookup;
			NativeParallelHashMap<int2, int> spriteMap = m_spriteMap;
			SeparatorSystem_680A320D_LambdaJob_0_Execute(_Bpsim_Rendering_SpriteID_RW_ComponentLookup, spriteMap);
		}

		[Preserve]
		protected override void OnDestroy()
		{
			m_spriteMap.Dispose();
		}

		private void SeparatorSystem_680A320D_LambdaJob_0_Execute(ComponentLookup<SpriteID> spriteIDLookup, NativeParallelHashMap<int2, int> spriteMap)
		{
			__Bpsim_Parts_Separator_RW_ComponentTypeHandle.Update(ref base.CheckedStateRef);
			__Bpsim_Parts_PartTypeValue_RO_ComponentTypeHandle.Update(ref base.CheckedStateRef);
			__Bpsim_Parts_PartTransform_RO_ComponentTypeHandle.Update(ref base.CheckedStateRef);
			__Unity_Entities_LinkedEntityGroup_RO_BufferTypeHandle.Update(ref base.CheckedStateRef);
			SeparatorSystem_680A320D_LambdaJob_0_Job jobData = new SeparatorSystem_680A320D_LambdaJob_0_Job
			{
				spriteIDLookup = spriteIDLookup,
				spriteMap = spriteMap,
				__separatorTypeHandle = __Bpsim_Parts_Separator_RW_ComponentTypeHandle,
				__partTypeTypeHandle = __Bpsim_Parts_PartTypeValue_RO_ComponentTypeHandle,
				__partTransformTypeHandle = __Bpsim_Parts_PartTransform_RO_ComponentTypeHandle,
				__linkedEntityGroupTypeHandle = __Unity_Entities_LinkedEntityGroup_RO_BufferTypeHandle
			};
			base.Dependency = InternalCompilerInterface.JobChunkInterface.Schedule(jobData, __query_697681675_0, base.Dependency);
		}

		private void __AssignHandlesImpl(ref SystemState state)
		{
			__query_697681675_0 = state.GetEntityQuery(new EntityQueryDesc
			{
				All = new ComponentType[5]
				{
					ComponentType.ReadOnly<PartTypeValue>(),
					ComponentType.ReadOnly<PartTransform>(),
					ComponentType.ReadOnly<LinkedEntityGroup>(),
					ComponentType.ReadOnly<PartTag>(),
					ComponentType.ReadWrite<Separator>()
				},
				Any = new ComponentType[0],
				None = new ComponentType[0],
				Disabled = new ComponentType[0],
				Absent = new ComponentType[0],
				Options = EntityQueryOptions.Default
			});
			__Bpsim_Parts_Separator_RW_ComponentTypeHandle = state.GetComponentTypeHandle<Separator>();
			__Bpsim_Parts_PartTypeValue_RO_ComponentTypeHandle = state.GetComponentTypeHandle<PartTypeValue>(isReadOnly: true);
			__Bpsim_Parts_PartTransform_RO_ComponentTypeHandle = state.GetComponentTypeHandle<PartTransform>(isReadOnly: true);
			__Unity_Entities_LinkedEntityGroup_RO_BufferTypeHandle = state.GetBufferTypeHandle<LinkedEntityGroup>(isReadOnly: true);
			__Bpsim_Rendering_SpriteID_RW_ComponentLookup = state.GetComponentLookup<SpriteID>();
		}

		protected override void OnCreateForCompiler()
		{
			base.OnCreateForCompiler();
			__AssignHandlesImpl(ref base.CheckedStateRef);
		}

		[Preserve]
		public SeparatorSystem()
		{
		}
	}
}
