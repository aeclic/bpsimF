using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Scripting;

namespace Bpsim.Parts
{
	[UpdateInGroup(typeof(UpdatePresentationSystemGroup))]
	internal partial class PartRenderSystem : SystemBase
	{
		[BurstCompile(FloatMode = FloatMode.Default, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = false)]
		[NoAlias]
		private struct PartRenderSystem_8E72A11_LambdaJob_0_Job : IJobChunk
		{
			public AABB quadBounds;

			[NativeDisableParallelForRestriction]
			public ComponentLookup<PartRenderInfo> renderInfoLookup;

			[ReadOnly]
			public ComponentTypeHandle<PartTransform> __partTransformTypeHandle;

			public BufferTypeHandle<LinkedEntityGroup> __linkedEntityGroupTypeHandle;

			[ReadOnly]
			public ComponentLookup<LocalToWorld> __Unity_Transforms_LocalToWorld_ComponentLookup;

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			private void OriginalLambdaBody([NoAlias] in PartTransform partTransform, DynamicBuffer<LinkedEntityGroup> linkedEntityGroup)
			{
				foreach (Entity item in linkedEntityGroup.Reinterpret<Entity>())
				{
					if (renderInfoLookup.HasComponent(item))
					{
						LocalToWorld localToWorld = __Unity_Transforms_LocalToWorld_ComponentLookup[item];
						int renderPriority = BasePart.GetRenderPriority(in partTransform, in localToWorld);
						AABB bounds = AABB.Transform(localToWorld.Value, quadBounds);
						renderInfoLookup[item] = new PartRenderInfo
						{
							Priority = renderPriority,
							Bounds = bounds
						};
					}
				}
			}

			[CompilerGenerated]
			public void Execute(in ArchetypeChunk chunk, int batchIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				IntPtr nativeArrayPtr = InternalCompilerInterface.UnsafeGetChunkNativeArrayReadOnlyIntPtr(chunk, ref __partTransformTypeHandle);
				BufferAccessor<LinkedEntityGroup> bufferAccessor = chunk.GetBufferAccessor(ref __linkedEntityGroupTypeHandle);
				int count = chunk.Count;
				if (!useEnabledMask)
				{
					for (int i = 0; i < count; i++)
					{
						OriginalLambdaBody(in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PartTransform>(nativeArrayPtr, i), bufferAccessor[i]);
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
							OriginalLambdaBody(in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PartTransform>(nativeArrayPtr, j), bufferAccessor[j]);
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
						OriginalLambdaBody(in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PartTransform>(nativeArrayPtr, k), bufferAccessor[k]);
					}
					num >>= 1;
				}
				num = chunkEnabledMask.ULong1;
				for (int l = 64; l < count; l++)
				{
					if ((num & 1) != 0L)
					{
						OriginalLambdaBody(in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PartTransform>(nativeArrayPtr, l), bufferAccessor[l]);
					}
					num >>= 1;
				}
			}

			void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				Execute(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
			}
		}

		private AABB m_quadBounds;

		private ComponentTypeHandle<PartTransform> __Bpsim_Parts_PartTransform_RO_ComponentTypeHandle;

		private BufferTypeHandle<LinkedEntityGroup> __Unity_Entities_LinkedEntityGroup_RO_BufferTypeHandle;

		private ComponentLookup<LocalToWorld> __Unity_Transforms_LocalToWorld_RO_ComponentLookup;

		private ComponentLookup<PartRenderInfo> __Bpsim_Parts_PartRenderInfo_RW_ComponentLookup;

		private EntityQuery __query_1260451566_0;

		[Preserve]
		protected override void OnStartRunning()
		{
			Mesh builtinResource = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
			m_quadBounds = builtinResource.bounds.ToAABB();
		}

		[Preserve]
		protected override void OnUpdate()
		{
			AABB quadBounds = m_quadBounds;
			__Bpsim_Parts_PartRenderInfo_RW_ComponentLookup.Update(ref base.CheckedStateRef);
			ComponentLookup<PartRenderInfo> _Bpsim_Parts_PartRenderInfo_RW_ComponentLookup = __Bpsim_Parts_PartRenderInfo_RW_ComponentLookup;
			PartRenderSystem_8E72A11_LambdaJob_0_Execute(quadBounds, _Bpsim_Parts_PartRenderInfo_RW_ComponentLookup);
		}

		private void PartRenderSystem_8E72A11_LambdaJob_0_Execute(AABB quadBounds, ComponentLookup<PartRenderInfo> renderInfoLookup)
		{
			__Bpsim_Parts_PartTransform_RO_ComponentTypeHandle.Update(ref base.CheckedStateRef);
			__Unity_Entities_LinkedEntityGroup_RO_BufferTypeHandle.Update(ref base.CheckedStateRef);
			__Unity_Transforms_LocalToWorld_RO_ComponentLookup.Update(ref base.CheckedStateRef);
			PartRenderSystem_8E72A11_LambdaJob_0_Job jobData = new PartRenderSystem_8E72A11_LambdaJob_0_Job
			{
				quadBounds = quadBounds,
				renderInfoLookup = renderInfoLookup,
				__partTransformTypeHandle = __Bpsim_Parts_PartTransform_RO_ComponentTypeHandle,
				__linkedEntityGroupTypeHandle = __Unity_Entities_LinkedEntityGroup_RO_BufferTypeHandle,
				__Unity_Transforms_LocalToWorld_ComponentLookup = __Unity_Transforms_LocalToWorld_RO_ComponentLookup
			};
			base.Dependency = InternalCompilerInterface.JobChunkInterface.ScheduleParallel(jobData, __query_1260451566_0, base.Dependency);
		}

		private void __AssignHandlesImpl(ref SystemState state)
		{
			__query_1260451566_0 = state.GetEntityQuery(new EntityQueryDesc
			{
				All = new ComponentType[4]
				{
					ComponentType.ReadOnly<PartTransform>(),
					ComponentType.ReadOnly<LinkedEntityGroup>(),
					ComponentType.ReadOnly<PartTag>(),
					ComponentType.ReadOnly<LocalToWorld>()
				},
				Any = new ComponentType[0],
				None = new ComponentType[0],
				Disabled = new ComponentType[0],
				Absent = new ComponentType[0],
				Options = EntityQueryOptions.Default
			});
			__query_1260451566_0.SetChangedVersionFilter(new ComponentType[2]
			{
				ComponentType.ReadOnly<PartTransform>(),
				ComponentType.ReadOnly<LocalToWorld>()
			});
			__Bpsim_Parts_PartTransform_RO_ComponentTypeHandle = state.GetComponentTypeHandle<PartTransform>(isReadOnly: true);
			__Unity_Entities_LinkedEntityGroup_RO_BufferTypeHandle = state.GetBufferTypeHandle<LinkedEntityGroup>(isReadOnly: true);
			__Unity_Transforms_LocalToWorld_RO_ComponentLookup = state.GetComponentLookup<LocalToWorld>(isReadOnly: true);
			__Bpsim_Parts_PartRenderInfo_RW_ComponentLookup = state.GetComponentLookup<PartRenderInfo>();
		}

		protected override void OnCreateForCompiler()
		{
			base.OnCreateForCompiler();
			__AssignHandlesImpl(ref base.CheckedStateRef);
		}

		[Preserve]
		public PartRenderSystem()
		{
		}
	}
}
