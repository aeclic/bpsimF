using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine.Scripting;

namespace Bpsim.Parts
{
	[UpdateBefore(typeof(ParentSystem))]
	[UpdateInGroup(typeof(TransformSystemGroup))]
	internal partial class PartTransformSystem : SystemBase
	{
		[NoAlias]
		[BurstCompile(FloatMode = FloatMode.Default, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = false)]
		private struct PartTransformSystem_1F2E3313_LambdaJob_0_Job : IJobChunk
		{
			public ComponentTypeHandle<LocalTransform> __transformTypeHandle;

			public PartAspect.TypeHandle __partTypeHandle;

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			private void OriginalLambdaBody([NoAlias] ref LocalTransform transform, PartAspect part)
			{
				transform.Position = BasePart.MoveTo(in part);
				transform.Rotation = BasePart.RotateTo(ref part);
			}

			[CompilerGenerated]
			public void Execute(in ArchetypeChunk chunk, int batchIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				IntPtr nativeArrayPtr = InternalCompilerInterface.UnsafeGetChunkNativeArrayIntPtr(chunk, ref __transformTypeHandle);
				PartAspect.ResolvedChunk resolvedChunk = __partTypeHandle.Resolve(chunk);
				int count = chunk.Count;
				if (!useEnabledMask)
				{
					for (int i = 0; i < count; i++)
					{
						OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<LocalTransform>(nativeArrayPtr, i), resolvedChunk[i]);
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
							OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<LocalTransform>(nativeArrayPtr, j), resolvedChunk[j]);
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
						OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<LocalTransform>(nativeArrayPtr, k), resolvedChunk[k]);
					}
					num >>= 1;
				}
				num = chunkEnabledMask.ULong1;
				for (int l = 64; l < count; l++)
				{
					if ((num & 1) != 0L)
					{
						OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<LocalTransform>(nativeArrayPtr, l), resolvedChunk[l]);
					}
					num >>= 1;
				}
			}

			void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				Execute(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
			}
		}

		private ComponentTypeHandle<LocalTransform> __Unity_Transforms_LocalTransform_RW_ComponentTypeHandle;

		private PartAspect.TypeHandle __Bpsim_Parts_PartAspect_RW_AspectTypeHandle;

		private EntityQuery __query_117030725_0;

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
			if (!PartManager.Instance.IsSimulating)
			{
				PartTransformSystem_1F2E3313_LambdaJob_0_Execute();
			}
		}

		private void PartTransformSystem_1F2E3313_LambdaJob_0_Execute()
		{
			__Unity_Transforms_LocalTransform_RW_ComponentTypeHandle.Update(ref base.CheckedStateRef);
			__Bpsim_Parts_PartAspect_RW_AspectTypeHandle.Update(ref base.CheckedStateRef);
			PartTransformSystem_1F2E3313_LambdaJob_0_Job jobData = new PartTransformSystem_1F2E3313_LambdaJob_0_Job
			{
				__transformTypeHandle = __Unity_Transforms_LocalTransform_RW_ComponentTypeHandle,
				__partTypeHandle = __Bpsim_Parts_PartAspect_RW_AspectTypeHandle
			};
			base.Dependency = InternalCompilerInterface.JobChunkInterface.ScheduleParallel(jobData, __query_117030725_0, base.Dependency);
		}

		private void __AssignHandlesImpl(ref SystemState state)
		{
			EntityQueryBuilder entityQueryBuilder = new EntityQueryBuilder(Allocator.Temp);
			entityQueryBuilder = entityQueryBuilder.WithAllRW<LocalTransform>();
			entityQueryBuilder = entityQueryBuilder.WithAll<PartTag>();
			entityQueryBuilder = entityQueryBuilder.WithAll<PartTransform>();
			entityQueryBuilder = entityQueryBuilder.WithAspect<PartAspect>();
			__query_117030725_0 = entityQueryBuilder.Build(ref state);
			__Unity_Transforms_LocalTransform_RW_ComponentTypeHandle = state.GetComponentTypeHandle<LocalTransform>();
			__Bpsim_Parts_PartAspect_RW_AspectTypeHandle = new PartAspect.TypeHandle(ref state, isReadOnly: false);
		}

		protected override void OnCreateForCompiler()
		{
			base.OnCreateForCompiler();
			__AssignHandlesImpl(ref base.CheckedStateRef);
		}

		[Preserve]
		public PartTransformSystem()
		{
		}
	}
}
