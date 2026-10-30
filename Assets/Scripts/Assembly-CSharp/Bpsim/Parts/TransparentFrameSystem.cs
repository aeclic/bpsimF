using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace Bpsim.Parts
{
	[UpdateBefore(typeof(ColoredFrameSystem))]
	internal partial class TransparentFrameSystem : SystemBase
	{
		[BurstCompile(FloatMode = FloatMode.Default, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = false)]
		[NoAlias]
		private struct TransparentFrameSystem_3A68A05F_LambdaJob_0_Job : IJobChunk
		{
			public Reference<PartSceneUnmanaged> partScene;

			public ComponentTypeHandle<TransparentFrame> __transparentFrameTypeHandle;

			[ReadOnly]
			public ComponentTypeHandle<PartTransform> __partTransformTypeHandle;

			[ReadOnly]
			public ComponentLookup<ColoredFrame> __Bpsim_Parts_ColoredFrame_ComponentLookup;

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			private void OriginalLambdaBody([NoAlias] ref TransparentFrame transparentFrame, [NoAlias] in PartTransform partTransform)
			{
				transparentFrame.Neighbours.Clear();
				for (int i = 0; i < 4; i++)
				{
					var (num, num2) = BitDirectionExtensions.FromIndex(i).ToVector();
					if (partScene.Value.FindPartGridAt(partTransform.X + num, partTransform.Y + num2, out var grid) && grid.HasPartContainer && __Bpsim_Parts_ColoredFrame_ComponentLookup.HasComponent(grid.PartContainer))
					{
						transparentFrame.Neighbours.Add(in grid.PartContainer);
					}
				}
			}

			[CompilerGenerated]
			public void Execute(in ArchetypeChunk chunk, int batchIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				IntPtr nativeArrayPtr = InternalCompilerInterface.UnsafeGetChunkNativeArrayIntPtr(chunk, ref __transparentFrameTypeHandle);
				IntPtr nativeArrayPtr2 = InternalCompilerInterface.UnsafeGetChunkNativeArrayReadOnlyIntPtr(chunk, ref __partTransformTypeHandle);
				int count = chunk.Count;
				if (!useEnabledMask)
				{
					for (int i = 0; i < count; i++)
					{
						OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<TransparentFrame>(nativeArrayPtr, i), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PartTransform>(nativeArrayPtr2, i));
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
							OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<TransparentFrame>(nativeArrayPtr, j), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PartTransform>(nativeArrayPtr2, j));
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
						OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<TransparentFrame>(nativeArrayPtr, k), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PartTransform>(nativeArrayPtr2, k));
					}
					num >>= 1;
				}
				num = chunkEnabledMask.ULong1;
				for (int l = 64; l < count; l++)
				{
					if ((num & 1) != 0L)
					{
						OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<TransparentFrame>(nativeArrayPtr, l), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PartTransform>(nativeArrayPtr2, l));
					}
					num >>= 1;
				}
			}

			void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				Execute(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
			}
		}

		[NoAlias]
		[BurstCompile(FloatMode = FloatMode.Default, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = false)]
		private struct TransparentFrameSystem_3A68A05F_LambdaJob_1_Job : IJobChunk
		{
			public float4 defaultColor;

			public ComponentTypeHandle<TransparentFrame> __transparentFrameTypeHandle;

			[ReadOnly]
			public ComponentTypeHandle<ColoredFrame> __coloredFrameTypeHandle;

			[ReadOnly]
			public ComponentLookup<ColoredFrame> __Bpsim_Parts_ColoredFrame_ComponentLookup;

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			private void OriginalLambdaBody([NoAlias] ref TransparentFrame transparentFrame, [NoAlias] in ColoredFrame coloredFrame)
			{
				float4 color = coloredFrame.Color;
				float4 transparentColor = color;
				foreach (Entity neighbour in transparentFrame.Neighbours)
				{
					float4 color2 = __Bpsim_Parts_ColoredFrame_ComponentLookup[neighbour].Color;
					transparentColor += 0.2f * (color2 - color);
				}
				if (transparentFrame.Neighbours.Length <= 2)
				{
					transparentColor += 0.001f * (defaultColor - color);
				}
				transparentFrame.TransparentColor = transparentColor;
			}

			[CompilerGenerated]
			public void Execute(in ArchetypeChunk chunk, int batchIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				IntPtr nativeArrayPtr = InternalCompilerInterface.UnsafeGetChunkNativeArrayIntPtr(chunk, ref __transparentFrameTypeHandle);
				IntPtr nativeArrayPtr2 = InternalCompilerInterface.UnsafeGetChunkNativeArrayReadOnlyIntPtr(chunk, ref __coloredFrameTypeHandle);
				int count = chunk.Count;
				if (!useEnabledMask)
				{
					for (int i = 0; i < count; i++)
					{
						OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<TransparentFrame>(nativeArrayPtr, i), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<ColoredFrame>(nativeArrayPtr2, i));
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
							OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<TransparentFrame>(nativeArrayPtr, j), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<ColoredFrame>(nativeArrayPtr2, j));
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
						OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<TransparentFrame>(nativeArrayPtr, k), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<ColoredFrame>(nativeArrayPtr2, k));
					}
					num >>= 1;
				}
				num = chunkEnabledMask.ULong1;
				for (int l = 64; l < count; l++)
				{
					if ((num & 1) != 0L)
					{
						OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<TransparentFrame>(nativeArrayPtr, l), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<ColoredFrame>(nativeArrayPtr2, l));
					}
					num >>= 1;
				}
			}

			void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				Execute(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
			}
		}

		[NoAlias]
		[BurstCompile(FloatMode = FloatMode.Default, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = false)]
		private struct TransparentFrameSystem_3A68A05F_LambdaJob_2_Job : IJobChunk
		{
			public ComponentTypeHandle<ColoredFrame> __coloredFrameTypeHandle;

			[ReadOnly]
			public ComponentTypeHandle<TransparentFrame> __transparentFrameTypeHandle;

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			private void OriginalLambdaBody([NoAlias] ref ColoredFrame coloredFrame, [NoAlias] in TransparentFrame transparentFrame)
			{
				coloredFrame.HasChanged = true;
				coloredFrame.Color = transparentFrame.TransparentColor;
			}

			[CompilerGenerated]
			public void Execute(in ArchetypeChunk chunk, int batchIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				IntPtr nativeArrayPtr = InternalCompilerInterface.UnsafeGetChunkNativeArrayIntPtr(chunk, ref __coloredFrameTypeHandle);
				IntPtr nativeArrayPtr2 = InternalCompilerInterface.UnsafeGetChunkNativeArrayReadOnlyIntPtr(chunk, ref __transparentFrameTypeHandle);
				int count = chunk.Count;
				if (!useEnabledMask)
				{
					for (int i = 0; i < count; i++)
					{
						OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<ColoredFrame>(nativeArrayPtr, i), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<TransparentFrame>(nativeArrayPtr2, i));
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
							OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<ColoredFrame>(nativeArrayPtr, j), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<TransparentFrame>(nativeArrayPtr2, j));
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
						OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<ColoredFrame>(nativeArrayPtr, k), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<TransparentFrame>(nativeArrayPtr2, k));
					}
					num >>= 1;
				}
				num = chunkEnabledMask.ULong1;
				for (int l = 64; l < count; l++)
				{
					if ((num & 1) != 0L)
					{
						OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<ColoredFrame>(nativeArrayPtr, l), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<TransparentFrame>(nativeArrayPtr2, l));
					}
					num >>= 1;
				}
			}

			void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				Execute(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
			}
		}

		private ComponentTypeHandle<TransparentFrame> __Bpsim_Parts_TransparentFrame_RW_ComponentTypeHandle;

		private ComponentTypeHandle<PartTransform> __Bpsim_Parts_PartTransform_RO_ComponentTypeHandle;

		private ComponentLookup<ColoredFrame> __Bpsim_Parts_ColoredFrame_RO_ComponentLookup;

		private ComponentTypeHandle<ColoredFrame> __Bpsim_Parts_ColoredFrame_RO_ComponentTypeHandle;

		private ComponentTypeHandle<ColoredFrame> __Bpsim_Parts_ColoredFrame_RW_ComponentTypeHandle;

		private ComponentTypeHandle<TransparentFrame> __Bpsim_Parts_TransparentFrame_RO_ComponentTypeHandle;

		private EntityQuery __query_1874290430_0;

		private EntityQuery __query_1874290430_1;

		private EntityQuery __query_1874290430_2;

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
				float4 defaultColor = new float4(1f, 1f, 1f, 0.2f);
				Reference<PartSceneUnmanaged> unmanagedRef = PartManager.Instance.ActiveScene.UnmanagedRef;
				JobHandle dependency = base.Dependency;
				dependency = TransparentFrameSystem_3A68A05F_LambdaJob_0_Execute(unmanagedRef, dependency);
				for (int i = 0; i < 16; i++)
				{
					dependency = TransparentFrameSystem_3A68A05F_LambdaJob_1_Execute(defaultColor, dependency);
					dependency = TransparentFrameSystem_3A68A05F_LambdaJob_2_Execute(dependency);
				}
				base.Dependency = dependency;
			}
		}

		private JobHandle TransparentFrameSystem_3A68A05F_LambdaJob_0_Execute(Reference<PartSceneUnmanaged> partScene, JobHandle __inputDependency)
		{
			__Bpsim_Parts_TransparentFrame_RW_ComponentTypeHandle.Update(ref base.CheckedStateRef);
			__Bpsim_Parts_PartTransform_RO_ComponentTypeHandle.Update(ref base.CheckedStateRef);
			__Bpsim_Parts_ColoredFrame_RO_ComponentLookup.Update(ref base.CheckedStateRef);
			return InternalCompilerInterface.JobChunkInterface.ScheduleParallel(new TransparentFrameSystem_3A68A05F_LambdaJob_0_Job
			{
				partScene = partScene,
				__transparentFrameTypeHandle = __Bpsim_Parts_TransparentFrame_RW_ComponentTypeHandle,
				__partTransformTypeHandle = __Bpsim_Parts_PartTransform_RO_ComponentTypeHandle,
				__Bpsim_Parts_ColoredFrame_ComponentLookup = __Bpsim_Parts_ColoredFrame_RO_ComponentLookup
			}, __query_1874290430_0, __inputDependency);
		}

		private JobHandle TransparentFrameSystem_3A68A05F_LambdaJob_1_Execute(float4 defaultColor, JobHandle __inputDependency)
		{
			__Bpsim_Parts_TransparentFrame_RW_ComponentTypeHandle.Update(ref base.CheckedStateRef);
			__Bpsim_Parts_ColoredFrame_RO_ComponentTypeHandle.Update(ref base.CheckedStateRef);
			__Bpsim_Parts_ColoredFrame_RO_ComponentLookup.Update(ref base.CheckedStateRef);
			return InternalCompilerInterface.JobChunkInterface.ScheduleParallel(new TransparentFrameSystem_3A68A05F_LambdaJob_1_Job
			{
				defaultColor = defaultColor,
				__transparentFrameTypeHandle = __Bpsim_Parts_TransparentFrame_RW_ComponentTypeHandle,
				__coloredFrameTypeHandle = __Bpsim_Parts_ColoredFrame_RO_ComponentTypeHandle,
				__Bpsim_Parts_ColoredFrame_ComponentLookup = __Bpsim_Parts_ColoredFrame_RO_ComponentLookup
			}, __query_1874290430_1, __inputDependency);
		}

		private JobHandle TransparentFrameSystem_3A68A05F_LambdaJob_2_Execute(JobHandle __inputDependency)
		{
			__Bpsim_Parts_ColoredFrame_RW_ComponentTypeHandle.Update(ref base.CheckedStateRef);
			__Bpsim_Parts_TransparentFrame_RO_ComponentTypeHandle.Update(ref base.CheckedStateRef);
			return InternalCompilerInterface.JobChunkInterface.ScheduleParallel(new TransparentFrameSystem_3A68A05F_LambdaJob_2_Job
			{
				__coloredFrameTypeHandle = __Bpsim_Parts_ColoredFrame_RW_ComponentTypeHandle,
				__transparentFrameTypeHandle = __Bpsim_Parts_TransparentFrame_RO_ComponentTypeHandle
			}, __query_1874290430_2, __inputDependency);
		}

		private void __AssignHandlesImpl(ref SystemState state)
		{
			__query_1874290430_0 = state.GetEntityQuery(new EntityQueryDesc
			{
				All = new ComponentType[3]
				{
					ComponentType.ReadOnly<PartTransform>(),
					ComponentType.ReadOnly<PartTag>(),
					ComponentType.ReadWrite<TransparentFrame>()
				},
				Any = new ComponentType[0],
				None = new ComponentType[0],
				Disabled = new ComponentType[0],
				Absent = new ComponentType[0],
				Options = EntityQueryOptions.Default
			});
			__query_1874290430_1 = state.GetEntityQuery(new EntityQueryDesc
			{
				All = new ComponentType[3]
				{
					ComponentType.ReadOnly<ColoredFrame>(),
					ComponentType.ReadOnly<PartTag>(),
					ComponentType.ReadWrite<TransparentFrame>()
				},
				Any = new ComponentType[0],
				None = new ComponentType[0],
				Disabled = new ComponentType[0],
				Absent = new ComponentType[0],
				Options = EntityQueryOptions.Default
			});
			__query_1874290430_2 = state.GetEntityQuery(new EntityQueryDesc
			{
				All = new ComponentType[3]
				{
					ComponentType.ReadOnly<TransparentFrame>(),
					ComponentType.ReadOnly<PartTag>(),
					ComponentType.ReadWrite<ColoredFrame>()
				},
				Any = new ComponentType[0],
				None = new ComponentType[0],
				Disabled = new ComponentType[0],
				Absent = new ComponentType[0],
				Options = EntityQueryOptions.Default
			});
			__Bpsim_Parts_TransparentFrame_RW_ComponentTypeHandle = state.GetComponentTypeHandle<TransparentFrame>();
			__Bpsim_Parts_PartTransform_RO_ComponentTypeHandle = state.GetComponentTypeHandle<PartTransform>(isReadOnly: true);
			__Bpsim_Parts_ColoredFrame_RO_ComponentLookup = state.GetComponentLookup<ColoredFrame>(isReadOnly: true);
			__Bpsim_Parts_ColoredFrame_RO_ComponentTypeHandle = state.GetComponentTypeHandle<ColoredFrame>(isReadOnly: true);
			__Bpsim_Parts_ColoredFrame_RW_ComponentTypeHandle = state.GetComponentTypeHandle<ColoredFrame>();
			__Bpsim_Parts_TransparentFrame_RO_ComponentTypeHandle = state.GetComponentTypeHandle<TransparentFrame>(isReadOnly: true);
		}

		protected override void OnCreateForCompiler()
		{
			base.OnCreateForCompiler();
			__AssignHandlesImpl(ref base.CheckedStateRef);
		}

		[Preserve]
		public TransparentFrameSystem()
		{
		}
	}
}
