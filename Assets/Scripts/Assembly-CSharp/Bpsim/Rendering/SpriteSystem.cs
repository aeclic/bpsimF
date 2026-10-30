using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace Bpsim.Rendering
{
	public partial class SpriteSystem : SystemBase
	{
		[BurstCompile(FloatMode = FloatMode.Default, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = false)]
		[NoAlias]
		private struct SpriteSystem_6DD6E173_LambdaJob_0_Job : IJobChunk
		{
			[ReadOnly]
			public NativeArray<SpriteRect> array;

			public ComponentTypeHandle<MeshRect> __meshRectTypeHandle;

			public ComponentTypeHandle<MeshUVRect> __meshUVRectTypeHandle;

			[ReadOnly]
			public ComponentTypeHandle<SpriteID> __idTypeHandle;

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			private void OriginalLambdaBody([NoAlias] ref MeshRect meshRect, [NoAlias] ref MeshUVRect meshUVRect, [NoAlias] in SpriteID id)
			{
				if (id.Value >= 0 && id.Value < array.Length)
				{
					SpriteRect spriteRect = array[id.Value];
					meshRect.Value = new float4(spriteRect.VertexX, spriteRect.VertexY, spriteRect.VertexW, spriteRect.VertexH);
					meshUVRect.Value = new float4(spriteRect.U, spriteRect.V, spriteRect.W, spriteRect.H);
				}
			}

			[CompilerGenerated]
			public void Execute(in ArchetypeChunk chunk, int batchIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				IntPtr nativeArrayPtr = InternalCompilerInterface.UnsafeGetChunkNativeArrayIntPtr(chunk, ref __meshRectTypeHandle);
				IntPtr nativeArrayPtr2 = InternalCompilerInterface.UnsafeGetChunkNativeArrayIntPtr(chunk, ref __meshUVRectTypeHandle);
				IntPtr nativeArrayPtr3 = InternalCompilerInterface.UnsafeGetChunkNativeArrayReadOnlyIntPtr(chunk, ref __idTypeHandle);
				int count = chunk.Count;
				if (!useEnabledMask)
				{
					for (int i = 0; i < count; i++)
					{
						OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<MeshRect>(nativeArrayPtr, i), ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<MeshUVRect>(nativeArrayPtr2, i), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<SpriteID>(nativeArrayPtr3, i));
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
							OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<MeshRect>(nativeArrayPtr, j), ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<MeshUVRect>(nativeArrayPtr2, j), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<SpriteID>(nativeArrayPtr3, j));
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
						OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<MeshRect>(nativeArrayPtr, k), ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<MeshUVRect>(nativeArrayPtr2, k), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<SpriteID>(nativeArrayPtr3, k));
					}
					num >>= 1;
				}
				num = chunkEnabledMask.ULong1;
				for (int l = 64; l < count; l++)
				{
					if ((num & 1) != 0L)
					{
						OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<MeshRect>(nativeArrayPtr, l), ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<MeshUVRect>(nativeArrayPtr2, l), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<SpriteID>(nativeArrayPtr3, l));
					}
					num >>= 1;
				}
			}

			void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				Execute(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
			}
		}

		private NativeArray<SpriteRect> m_data;

		private ComponentTypeHandle<MeshRect> __Bpsim_Rendering_MeshRect_RW_ComponentTypeHandle;

		private ComponentTypeHandle<MeshUVRect> __Bpsim_Rendering_MeshUVRect_RW_ComponentTypeHandle;

		private ComponentTypeHandle<SpriteID> __Bpsim_Rendering_SpriteID_RO_ComponentTypeHandle;

		private EntityQuery __query_1970876144_0;

		// The ECS world can reach its first update before SpriteManager's Awake has run
		// (and in a player build it reliably does), so the sprite table is fetched lazily
		// from OnUpdate instead of OnStartRunning.  Doing it eagerly there threw
		// "NullReferenceException at SpriteSystem.OnStartRunning" on every boot.
		private bool TryInitializeData()
		{
			if (m_data.IsCreated)
			{
				return true;
			}
			SpriteManager manager = SpriteManager.Instance;
			if (manager == null)
			{
				return false;
			}
			SpriteRect[] sprites = manager.SpriteData;
			if (sprites == null)
			{
				return false;
			}
			m_data = new NativeArray<SpriteRect>(sprites, Allocator.Persistent);
			return true;
		}

		[Preserve]
		protected override void OnUpdate()
		{
			if (!TryInitializeData())
			{
				return;
			}
			NativeArray<SpriteRect> data = m_data;
			SpriteSystem_6DD6E173_LambdaJob_0_Execute(data);
		}

		[Preserve]
		protected override void OnStopRunning()
		{
			if (m_data.IsCreated)
			{
				m_data.Dispose();
			}
		}

		private void SpriteSystem_6DD6E173_LambdaJob_0_Execute(NativeArray<SpriteRect> array)
		{
			__Bpsim_Rendering_MeshRect_RW_ComponentTypeHandle.Update(ref base.CheckedStateRef);
			__Bpsim_Rendering_MeshUVRect_RW_ComponentTypeHandle.Update(ref base.CheckedStateRef);
			__Bpsim_Rendering_SpriteID_RO_ComponentTypeHandle.Update(ref base.CheckedStateRef);
			SpriteSystem_6DD6E173_LambdaJob_0_Job jobData = new SpriteSystem_6DD6E173_LambdaJob_0_Job
			{
				array = array,
				__meshRectTypeHandle = __Bpsim_Rendering_MeshRect_RW_ComponentTypeHandle,
				__meshUVRectTypeHandle = __Bpsim_Rendering_MeshUVRect_RW_ComponentTypeHandle,
				__idTypeHandle = __Bpsim_Rendering_SpriteID_RO_ComponentTypeHandle
			};
			base.Dependency = InternalCompilerInterface.JobChunkInterface.ScheduleParallel(jobData, __query_1970876144_0, base.Dependency);
		}

		private void __AssignHandlesImpl(ref SystemState state)
		{
			__query_1970876144_0 = state.GetEntityQuery(new EntityQueryDesc
			{
				All = new ComponentType[3]
				{
					ComponentType.ReadOnly<SpriteID>(),
					ComponentType.ReadWrite<MeshRect>(),
					ComponentType.ReadWrite<MeshUVRect>()
				},
				Any = new ComponentType[0],
				None = new ComponentType[0],
				Disabled = new ComponentType[0],
				Absent = new ComponentType[0],
				Options = EntityQueryOptions.Default
			});
			__query_1970876144_0.SetChangedVersionFilter(new ComponentType[1] { ComponentType.ReadOnly<SpriteID>() });
			__Bpsim_Rendering_MeshRect_RW_ComponentTypeHandle = state.GetComponentTypeHandle<MeshRect>();
			__Bpsim_Rendering_MeshUVRect_RW_ComponentTypeHandle = state.GetComponentTypeHandle<MeshUVRect>();
			__Bpsim_Rendering_SpriteID_RO_ComponentTypeHandle = state.GetComponentTypeHandle<SpriteID>(isReadOnly: true);
		}

		protected override void OnCreateForCompiler()
		{
			base.OnCreateForCompiler();
			__AssignHandlesImpl(ref base.CheckedStateRef);
		}

		[Preserve]
		public SpriteSystem()
		{
		}
	}
}
