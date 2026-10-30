using System;
using System.Runtime.CompilerServices;
using Bpsim.Rendering;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.Scripting;

namespace Bpsim.Parts
{
	internal partial class ColoredFrameSystem : SystemBase
	{
		[BurstCompile(FloatMode = FloatMode.Default, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = false)]
		[NoAlias]
		private struct ColoredFrameSystem_646686E9_LambdaJob_0_Job : IJobChunk
		{
			[NativeDisableParallelForRestriction]
			public ComponentLookup<MaterialColor> materialColorLookup;

			public ComponentTypeHandle<ColoredFrame> __coloredFrameTypeHandle;

			public BufferTypeHandle<LinkedEntityGroup> __linkedEntityGroupTypeHandle;

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			private void OriginalLambdaBody([NoAlias] ref ColoredFrame coloredFrame, DynamicBuffer<LinkedEntityGroup> linkedEntityGroup)
			{
				if (!coloredFrame.HasChanged)
				{
					return;
				}
				coloredFrame.HasChanged = false;
				float4 color = coloredFrame.Color;
				foreach (Entity item in linkedEntityGroup.Reinterpret<Entity>())
				{
					ref MaterialColor valueRW = ref materialColorLookup.GetRefRW(item, isReadOnly: false).ValueRW;
					float w = (coloredFrame.IsTransparent ? color.w : valueRW.Value.w);
					valueRW.Value = new float4(color.xyz, w);
				}
			}

			[CompilerGenerated]
			public void Execute(in ArchetypeChunk chunk, int batchIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				IntPtr nativeArrayPtr = InternalCompilerInterface.UnsafeGetChunkNativeArrayIntPtr(chunk, ref __coloredFrameTypeHandle);
				BufferAccessor<LinkedEntityGroup> bufferAccessor = chunk.GetBufferAccessor(ref __linkedEntityGroupTypeHandle);
				int count = chunk.Count;
				if (!useEnabledMask)
				{
					for (int i = 0; i < count; i++)
					{
						OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<ColoredFrame>(nativeArrayPtr, i), bufferAccessor[i]);
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
							OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<ColoredFrame>(nativeArrayPtr, j), bufferAccessor[j]);
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
						OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<ColoredFrame>(nativeArrayPtr, k), bufferAccessor[k]);
					}
					num >>= 1;
				}
				num = chunkEnabledMask.ULong1;
				for (int l = 64; l < count; l++)
				{
					if ((num & 1) != 0L)
					{
						OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<ColoredFrame>(nativeArrayPtr, l), bufferAccessor[l]);
					}
					num >>= 1;
				}
			}

			void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				Execute(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
			}
		}

		[BurstCompile(FloatMode = FloatMode.Default, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = false)]
		[NoAlias]
		private struct ColoredFrameSystem_646686E9_LambdaJob_1_Job : IJobChunk
		{
			[NativeDisableParallelForRestriction]
			public ComponentLookup<MaterialColor> materialColorLookup;

			[NativeDisableParallelForRestriction]
			public ComponentLookup<BlendFactor> blendFactorLookup;

			[ReadOnly]
			public PartAspect.TypeHandle __partTypeHandle;

			public BufferTypeHandle<LinkedEntityGroup> __linkedEntityGroupTypeHandle;

			[ReadOnly]
			public ComponentLookup<ColoredFrame> __Bpsim_Parts_ColoredFrame_ComponentLookup;

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			private void OriginalLambdaBody(PartAspect part, DynamicBuffer<LinkedEntityGroup> linkedEntityGroup)
			{
				Entity partContainer = part.PartContainer;
				float4 value = new float4(1f, 1f, 1f, 1f);
				float4 value2 = new float4(1f, 0f, 0f, 0f);
				if (partContainer != Entity.Null && __Bpsim_Parts_ColoredFrame_ComponentLookup.HasComponent(partContainer))
				{
					value = (Vector4)__Bpsim_Parts_ColoredFrame_ComponentLookup[partContainer].Color;
					value2 = new float4(0f, 0.75f, 0f, 0f);
				}
				foreach (Entity item in linkedEntityGroup.Reinterpret<Entity>())
				{
					if (materialColorLookup.HasComponent(item))
					{
						materialColorLookup[item] = new MaterialColor
						{
							Value = value
						};
					}
					if (blendFactorLookup.HasComponent(item))
					{
						blendFactorLookup[item] = new BlendFactor
						{
							Value = new float4(value2.xy, blendFactorLookup[item].Value.zw)
						};
					}
				}
			}

			[CompilerGenerated]
			public void Execute(in ArchetypeChunk chunk, int batchIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				PartAspect.ResolvedChunk resolvedChunk = __partTypeHandle.Resolve(chunk);
				BufferAccessor<LinkedEntityGroup> bufferAccessor = chunk.GetBufferAccessor(ref __linkedEntityGroupTypeHandle);
				int count = chunk.Count;
				if (!useEnabledMask)
				{
					for (int i = 0; i < count; i++)
					{
						OriginalLambdaBody(resolvedChunk[i], bufferAccessor[i]);
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
							OriginalLambdaBody(resolvedChunk[j], bufferAccessor[j]);
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
						OriginalLambdaBody(resolvedChunk[k], bufferAccessor[k]);
					}
					num >>= 1;
				}
				num = chunkEnabledMask.ULong1;
				for (int l = 64; l < count; l++)
				{
					if ((num & 1) != 0L)
					{
						OriginalLambdaBody(resolvedChunk[l], bufferAccessor[l]);
					}
					num >>= 1;
				}
			}

			void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				Execute(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
			}
		}

		private ComponentTypeHandle<ColoredFrame> __Bpsim_Parts_ColoredFrame_RW_ComponentTypeHandle;

		private BufferTypeHandle<LinkedEntityGroup> __Unity_Entities_LinkedEntityGroup_RO_BufferTypeHandle;

		private PartAspect.TypeHandle __Bpsim_Parts_PartAspect_RO_AspectTypeHandle;

		private ComponentLookup<ColoredFrame> __Bpsim_Parts_ColoredFrame_RO_ComponentLookup;

		private ComponentLookup<MaterialColor> __Unity_Rendering_MaterialColor_RW_ComponentLookup;

		private ComponentLookup<BlendFactor> __Bpsim_Rendering_BlendFactor_RW_ComponentLookup;

		private EntityQuery __query_1982409865_0;

		private EntityQuery __query_1982409865_1;

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
			if (PartManager.Instance.HasActiveScene())
			{
				__Unity_Rendering_MaterialColor_RW_ComponentLookup.Update(ref base.CheckedStateRef);
				ComponentLookup<MaterialColor> _Unity_Rendering_MaterialColor_RW_ComponentLookup = __Unity_Rendering_MaterialColor_RW_ComponentLookup;
				__Bpsim_Rendering_BlendFactor_RW_ComponentLookup.Update(ref base.CheckedStateRef);
				ComponentLookup<BlendFactor> _Bpsim_Rendering_BlendFactor_RW_ComponentLookup = __Bpsim_Rendering_BlendFactor_RW_ComponentLookup;
				JobHandle dependency = base.Dependency;
				dependency = ColoredFrameSystem_646686E9_LambdaJob_0_Execute(_Unity_Rendering_MaterialColor_RW_ComponentLookup, dependency);
				dependency = ColoredFrameSystem_646686E9_LambdaJob_1_Execute(_Unity_Rendering_MaterialColor_RW_ComponentLookup, _Bpsim_Rendering_BlendFactor_RW_ComponentLookup, dependency);
				base.Dependency = dependency;
			}
		}

		private JobHandle ColoredFrameSystem_646686E9_LambdaJob_0_Execute(ComponentLookup<MaterialColor> materialColorLookup, JobHandle __inputDependency)
		{
			__Bpsim_Parts_ColoredFrame_RW_ComponentTypeHandle.Update(ref base.CheckedStateRef);
			__Unity_Entities_LinkedEntityGroup_RO_BufferTypeHandle.Update(ref base.CheckedStateRef);
			return InternalCompilerInterface.JobChunkInterface.ScheduleParallel(new ColoredFrameSystem_646686E9_LambdaJob_0_Job
			{
				materialColorLookup = materialColorLookup,
				__coloredFrameTypeHandle = __Bpsim_Parts_ColoredFrame_RW_ComponentTypeHandle,
				__linkedEntityGroupTypeHandle = __Unity_Entities_LinkedEntityGroup_RO_BufferTypeHandle
			}, __query_1982409865_0, __inputDependency);
		}

		private JobHandle ColoredFrameSystem_646686E9_LambdaJob_1_Execute(ComponentLookup<MaterialColor> materialColorLookup, ComponentLookup<BlendFactor> blendFactorLookup, JobHandle __inputDependency)
		{
			__Bpsim_Parts_PartAspect_RO_AspectTypeHandle.Update(ref base.CheckedStateRef);
			__Unity_Entities_LinkedEntityGroup_RO_BufferTypeHandle.Update(ref base.CheckedStateRef);
			__Bpsim_Parts_ColoredFrame_RO_ComponentLookup.Update(ref base.CheckedStateRef);
			return InternalCompilerInterface.JobChunkInterface.ScheduleParallel(new ColoredFrameSystem_646686E9_LambdaJob_1_Job
			{
				materialColorLookup = materialColorLookup,
				blendFactorLookup = blendFactorLookup,
				__partTypeHandle = __Bpsim_Parts_PartAspect_RO_AspectTypeHandle,
				__linkedEntityGroupTypeHandle = __Unity_Entities_LinkedEntityGroup_RO_BufferTypeHandle,
				__Bpsim_Parts_ColoredFrame_ComponentLookup = __Bpsim_Parts_ColoredFrame_RO_ComponentLookup
			}, __query_1982409865_1, __inputDependency);
		}

		private void __AssignHandlesImpl(ref SystemState state)
		{
			__query_1982409865_0 = state.GetEntityQuery(new EntityQueryDesc
			{
				All = new ComponentType[3]
				{
					ComponentType.ReadOnly<LinkedEntityGroup>(),
					ComponentType.ReadOnly<PartTag>(),
					ComponentType.ReadWrite<ColoredFrame>()
				},
				Any = new ComponentType[0],
				None = new ComponentType[0],
				Disabled = new ComponentType[0],
				Absent = new ComponentType[0],
				Options = EntityQueryOptions.Default
			});
			EntityQueryBuilder entityQueryBuilder = new EntityQueryBuilder(Allocator.Temp);
			entityQueryBuilder = entityQueryBuilder.WithAll<LinkedEntityGroup>();
			entityQueryBuilder = entityQueryBuilder.WithAll<PartTag>();
			entityQueryBuilder = entityQueryBuilder.WithAll<PartContainerValue>();
			entityQueryBuilder = entityQueryBuilder.WithNone<ColoredFrame>();
			entityQueryBuilder = entityQueryBuilder.WithAspect<PartAspect>();
			__query_1982409865_1 = entityQueryBuilder.Build(ref state);
			__Bpsim_Parts_ColoredFrame_RW_ComponentTypeHandle = state.GetComponentTypeHandle<ColoredFrame>();
			__Unity_Entities_LinkedEntityGroup_RO_BufferTypeHandle = state.GetBufferTypeHandle<LinkedEntityGroup>(isReadOnly: true);
			__Bpsim_Parts_PartAspect_RO_AspectTypeHandle = new PartAspect.TypeHandle(ref state, isReadOnly: true);
			__Bpsim_Parts_ColoredFrame_RO_ComponentLookup = state.GetComponentLookup<ColoredFrame>(isReadOnly: true);
			__Unity_Rendering_MaterialColor_RW_ComponentLookup = state.GetComponentLookup<MaterialColor>();
			__Bpsim_Rendering_BlendFactor_RW_ComponentLookup = state.GetComponentLookup<BlendFactor>();
		}

		protected override void OnCreateForCompiler()
		{
			base.OnCreateForCompiler();
			__AssignHandlesImpl(ref base.CheckedStateRef);
		}

		[Preserve]
		public ColoredFrameSystem()
		{
		}
	}
}
