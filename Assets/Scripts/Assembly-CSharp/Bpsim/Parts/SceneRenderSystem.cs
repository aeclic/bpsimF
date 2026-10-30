using System;
using Bpsim.Rendering;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Scripting;

namespace Bpsim.Parts
{
	[UpdateAfter(typeof(UpdatePresentationSystemGroup))]
	[UpdateInGroup(typeof(PresentationSystemGroup))]
	internal partial class SceneRenderSystem : SystemBase
	{
		private readonly struct SortPriority : IComparable<SortPriority>
		{
			public readonly int Index;

			public readonly int Priority;

			// Constant-buffer platforms (GLES/WebGL) draw one batch per window of instance data, so
			// the instances have to be grouped page-first.  The single-batch path always uses 0.
			public readonly int Group;

			public SortPriority(int index, int priority)
				: this(index, priority, 0)
			{
			}

			public SortPriority(int index, int priority, int group)
			{
				Index = index;
				Priority = priority;
				Group = group;
			}

			int IComparable<SortPriority>.CompareTo(SortPriority other)
			{
				int num = Group.CompareTo(other.Group);
				if (num != 0)
				{
					return num;
				}
				num = Priority.CompareTo(other.Priority);
				if (num == 0)
				{
					return Index.CompareTo(other.Index);
				}
				return num;
			}
		}

		private readonly struct PropertyInfo
		{
			public readonly int ID;

			public readonly int Size;

			public PropertyInfo(int id, int size)
			{
				ID = id;
				Size = size;
			}
		}

		private readonly struct GpuBufferInfo
		{
			public readonly NativeArray<byte> Source;

			public readonly int Offset;

			public readonly int Count;

			public readonly int Stride;

			public GpuBufferInfo(NativeArray<byte> source, int offset, int count, int stride)
			{
				Source = source;
				Offset = offset;
				Count = count;
				Stride = stride;
			}
		}

		[BurstCompile]
		private struct CopyPropertiesJob : IJobChunk
		{
			[ReadOnly]
			public NativeArray<int> EntityIndices;

			public ComponentTypeHandle<PartRenderInfo> PartRenderInfoHandle;

			[ReadOnly]
			public ComponentTypeHandle<LocalToWorld> LocalToWorldHandle;

			[ReadOnly]
			public ComponentTypeHandle<MaterialColor> MaterialColorHandle;

			[ReadOnly]
			public ComponentTypeHandle<PartLayerTint> LayerTintHandle;

			[ReadOnly]
			public ComponentTypeHandle<MeshRect> MeshRectHandle;

			[ReadOnly]
			public ComponentTypeHandle<MeshUVRect> MeshUVRectHandle;

			[ReadOnly]
			public ComponentTypeHandle<BlendFactor> BlendFactorHandle;

			[NativeDisableContainerSafetyRestriction]
			[NativeDisableParallelForRestriction]
			public NativeArray<GpuBufferInfo> BufferInfo;

			public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				int count = chunk.Count;
				int num = EntityIndices[unfilteredChunkIndex];
				NativeArray<PartRenderInfo> nativeArray = chunk.GetNativeArray(ref PartRenderInfoHandle);
				NativeArray<LocalToWorld> nativeArray2 = chunk.GetNativeArray(ref LocalToWorldHandle);
				NativeArray<MaterialColor> nativeArray3 = chunk.GetNativeArray(ref MaterialColorHandle);
				bool hasLayerTint = chunk.Has(ref LayerTintHandle);
				NativeArray<PartLayerTint> nativeArrayTint = (hasLayerTint ? chunk.GetNativeArray(ref LayerTintHandle) : default(NativeArray<PartLayerTint>));
				NativeArray<MeshRect> nativeArray4 = chunk.GetNativeArray(ref MeshRectHandle);
				NativeArray<MeshUVRect> nativeArray5 = chunk.GetNativeArray(ref MeshUVRectHandle);
				NativeArray<BlendFactor> nativeArray6 = chunk.GetNativeArray(ref BlendFactorHandle);
				for (int i = 0; i < count; i++)
				{
					nativeArray[i] = nativeArray[i].WithIndex(num + i);
					GpuBufferInfo bufferInfo = BufferInfo[0];
					int offset = num + i;
					LocalToWorld localToWorld = nativeArray2[i];
					CopyToBuffer(in bufferInfo, offset, PackMatrix(in localToWorld.Value));
					CopyToBuffer(BufferInfo[1], num + i, PackMatrix(math.inverse(nativeArray2[i].Value)));
				}
				CopyToBuffer(BufferInfo[2], num, nativeArray3, count);
				CopyToBuffer(BufferInfo[3], num, nativeArray4, count);
				CopyToBuffer(BufferInfo[4], num, nativeArray5, count);
				CopyToBuffer(BufferInfo[5], num, nativeArray6, count);
				for (int k = 0; k < count; k++)
				{
					float4 tintValue = (hasLayerTint ? nativeArrayTint[k].Value : new float4(1f, 1f, 1f, 1f));
					CopyToBuffer(BufferInfo[6], num + k, tintValue);
				}
			}

			private unsafe void CopyToBuffer<T>(in GpuBufferInfo bufferInfo, int offset, T value) where T : struct
			{
				UnsafeUtility.WriteArrayElement(bufferInfo.Source.GetUnsafePtr(), offset, value);
			}

			private unsafe void CopyToBuffer<T>(in GpuBufferInfo bufferInfo, int offset, NativeArray<T> source, int count) where T : struct
			{
				void* unsafeReadOnlyPtr = source.GetUnsafeReadOnlyPtr();
				void* destination = (byte*)bufferInfo.Source.GetUnsafePtr() + offset * bufferInfo.Stride;
				UnsafeUtility.MemCpy(destination, unsafeReadOnlyPtr, count * bufferInfo.Stride);
			}

			void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				Execute(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
			}
		}

		[BurstCompile]
		private struct CullingJob : IJobChunk
		{
			[ReadOnly]
			public UnityEngine.FrustumPlanes Planes;

			[ReadOnly]
			public ComponentTypeHandle<PartRenderInfo> PartRenderInfoHandle;

			[ReadOnly]
			public ComponentTypeHandle<WorldTransform> WorldTransformHandle;

			[WriteOnly]
			public NativeList<SortPriority>.ParallelWriter SortedIndices;

			public int InstancesPerPage;

			public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				int count = chunk.Count;
				NativeArray<PartRenderInfo> nativeArray = chunk.GetNativeArray(ref PartRenderInfoHandle);
				NativeList<SortPriority> list = new NativeList<SortPriority>(count, Allocator.Temp);
				for (int i = 0; i < count; i++)
				{
					PartRenderInfo partRenderInfo = nativeArray[i];
					float3 min = partRenderInfo.Bounds.Min;
					float3 max = partRenderInfo.Bounds.Max;
					if (max.x >= Planes.left && min.x <= Planes.right && max.y >= Planes.bottom && min.y <= Planes.top)
					{
						list.AddNoResize(new SortPriority(partRenderInfo.Index, partRenderInfo.Priority, (InstancesPerPage > 0) ? (partRenderInfo.Index / InstancesPerPage) : 0));
					}
				}
				SortedIndices.AddRangeNoResize(list);
				list.Dispose();
			}

			void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				Execute(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
			}
		}

		[BurstCompile]
		private struct SortIndicesJob : IJob
		{
			public NativeList<SortPriority> SortedIndices;

			public void Execute()
			{
				SortedIndices.Sort();
			}
		}

		// One draw command per batch.  On constant-buffer platforms every batch can only address the
		// window of instance data it was registered with, so the visible instances are grouped by
		// page (SortPriority.Group) and each page becomes its own command whose instance indices are
		// page-local.  The raw (structured buffer) path has a single page and therefore emits exactly
		// one command, like it always did.
		[BurstCompile]
		private struct EmitDrawCommandsJob : IJob
		{
			[ReadOnly]
			public NativeArray<BatchID> BatchIDs;

			public int InstancesPerPage;

			[ReadOnly]
			public BatchMeshID MeshID;

			[ReadOnly]
			public BatchMaterialID MaterialID;

			[ReadOnly]
			public NativeList<SortPriority> SortedIndices;

			[WriteOnly]
			public BatchCullingOutput CullingOutput;

			public unsafe void Execute()
			{
				int length = SortedIndices.Length;
				int num = 0;
				// Count the distinct page groups.  The first entry must NOT seed the "previous group"
				// value: that made the first group look unchanged, so a single-page scene emitted zero
				// draw commands (nothing rendered) and a multi-page scene emitted one command more than
				// it had allocated, which overflowed the TempJob allocation and hung the player.
				int num2 = -1;
				for (int i = 0; i < length; i++)
				{
					if (SortedIndices[i].Group != num2)
					{
						num++;
						num2 = SortedIndices[i].Group;
					}
				}
				BatchCullingOutputDrawCommands* unsafePtr = (BatchCullingOutputDrawCommands*)CullingOutput.drawCommands.GetUnsafePtr();
				int num3 = ((num > 0) ? num : 1);
				unsafePtr->drawCommands = (BatchDrawCommand*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<BatchDrawCommand>() * num3, UnsafeUtility.AlignOf<BatchDrawCommand>(), Allocator.TempJob);
				unsafePtr->drawRanges = (BatchDrawRange*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<BatchDrawRange>(), UnsafeUtility.AlignOf<BatchDrawRange>(), Allocator.TempJob);
				unsafePtr->visibleInstances = (int*)UnsafeUtility.Malloc(length * 4, UnsafeUtility.AlignOf<int>(), Allocator.TempJob);
				unsafePtr->drawCommandPickingInstanceIDs = null;
				unsafePtr->drawCommandCount = num;
				unsafePtr->drawRangeCount = ((num > 0) ? 1 : 0);
				unsafePtr->visibleInstanceCount = length;
				unsafePtr->instanceSortingPositions = null;
				unsafePtr->instanceSortingPositionFloatCount = 0;
				int num4 = 0;
				int num5 = ((length > 0) ? SortedIndices[0].Group : (-1));
				int num6 = 0;
				for (int j = 0; j < length; j++)
				{
					SortPriority sortPriority = SortedIndices[j];
					if (sortPriority.Group != num5)
					{
						if (num5 >= 0)
						{
							EmitDrawCommand(unsafePtr, num4++, num6, j - num6, num5);
						}
						num5 = sortPriority.Group;
						num6 = j;
					}
					unsafePtr->visibleInstances[j] = ((InstancesPerPage > 0) ? (sortPriority.Index - num5 * InstancesPerPage) : sortPriority.Index);
				}
				if (num5 >= 0)
				{
					EmitDrawCommand(unsafePtr, num4, num6, length - num6, num5);
				}
				if (num > 0)
				{
					*unsafePtr->drawRanges = new BatchDrawRange
					{
						drawCommandsBegin = 0u,
						drawCommandsCount = (uint)num,
						filterSettings = new BatchFilterSettings
						{
							renderingLayerMask = uint.MaxValue
						}
					};
				}
			}

			private unsafe void EmitDrawCommand(BatchCullingOutputDrawCommands* commands, int index, int visibleOffset, int visibleCount, int group)
			{
				// Nothing to draw into when no batch is registered; indexing BatchIDs with the
				// clamped page index would be -1 here, which reads outside the array.
				if (BatchIDs.Length == 0)
				{
					return;
				}
				int num = ((group >= 0 && group < BatchIDs.Length) ? group : (BatchIDs.Length - 1));
				commands->drawCommands[index] = new BatchDrawCommand
				{
					visibleOffset = (uint)visibleOffset,
					visibleCount = (uint)visibleCount,
					batchID = BatchIDs[num],
					materialID = MaterialID,
					meshID = MeshID,
					submeshIndex = 0,
					splitVisibilityMask = 255,
					flags = BatchDrawCommandFlags.None,
					sortingPosition = group
				};
			}
		}

		private bool m_enableDepthSorting;

		private int m_bufferSize;

		private int m_sizeOfInstance;

		private int m_instanceCount;

		private int m_instanceCapacity;

		private NativeArray<BatchID> m_pageBatches;

		private int m_pageCount;

		private bool m_batchesReady;

		// GLES and WebGL only accept constant buffers for BatchRendererGroup instance data, and the
		// shader-side window is small (UnityDOTSInstancing.hlsl declares
		// "float4 unity_DOTSInstanceDataRaw[1024]" = 16 KB in that mode).  A part needs
		// m_sizeOfInstance bytes, so a scene that does not fit in one window is split into pages and
		// every page becomes its own batch with its own bufferOffset/windowSize.
		private bool m_constantBuffer;

		private int m_instancesPerPage;

		private int m_pageBytes;

		private int m_pageStride;

		private BatchMeshID m_meshID;

		private BatchMaterialID m_materialID;

		private EntityQuery m_updateEntityQuery;

		private EntityQuery m_cullingEntityQuery;

		private JobHandle m_performCullingDependency;

		private NativeArray<PropertyInfo> m_properties;

		private BatchRendererGroup m_batchRendererGroup;

		private GraphicsBuffer m_buffer;

		private const int BufferBegin = 48;

		// The buffer used to start at 1 MB, so a scene with a handful of parts
		// re-uploaded a full megabyte of (mostly unused) instance data every frame.
		// Start small and let the doubling in AllocateBuffer grow it: a part needs
		// m_sizeOfInstance bytes (160 for the current six properties).
		private const int InitialBufferSize = 65536;

		private ComponentTypeHandle<PartRenderInfo> __Bpsim_Parts_PartRenderInfo_RW_ComponentTypeHandle;

		private ComponentTypeHandle<LocalToWorld> __Unity_Transforms_LocalToWorld_RO_ComponentTypeHandle;

		private ComponentTypeHandle<MaterialColor> __Unity_Rendering_MaterialColor_RO_ComponentTypeHandle;

		private ComponentTypeHandle<PartLayerTint> __Bpsim_Rendering_PartLayerTint_RO_ComponentTypeHandle;

		private ComponentTypeHandle<MeshRect> __Bpsim_Rendering_MeshRect_RO_ComponentTypeHandle;

		private ComponentTypeHandle<MeshUVRect> __Bpsim_Rendering_MeshUVRect_RO_ComponentTypeHandle;

		private ComponentTypeHandle<BlendFactor> __Bpsim_Rendering_BlendFactor_RO_ComponentTypeHandle;

		private EntityQuery __query_595537251_0;

		private EntityQuery __query_595537251_1;

		[Preserve]
		protected override void OnCreate()
		{
			m_enableDepthSorting = true;
			m_batchRendererGroup = new BatchRendererGroup(OnPerformCulling, IntPtr.Zero);
			m_updateEntityQuery = __query_595537251_0;
			m_cullingEntityQuery = __query_595537251_1;
			InitializeProperties();
			ConfigureConstantBuffer();
		}

		// The ECS world reaches its first update before CoreManager's Awake has built the
		// runtime resources (this is reliable in a player build, and it happens in the
		// editor too), so the quad mesh / part material are registered lazily from
		// OnUpdate instead of OnStartRunning.  Registering eagerly there threw
		// "NullReferenceException at SceneRenderSystem.OnStartRunning" on every boot, and
		// because that aborted the registration the batch never got a material - the parts
		// then drew with an invalid material id.
		private bool TryRegisterResources()
		{
			if (m_materialID != default(BatchMaterialID))
			{
				return true;
			}
			CoreManager coreManager = CoreManager.Instance;
			if (coreManager == null || coreManager.Resources == null)
			{
				return false;
			}
			Material material;
			if (!coreManager.Resources.TryLoadAsset<Material>("Part_Texture_0", out material))
			{
				return false;
			}
			Mesh builtinResource = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
			if (builtinResource == null)
			{
				return false;
			}
			m_meshID = m_batchRendererGroup.RegisterMesh(builtinResource);
			m_materialID = m_batchRendererGroup.RegisterMaterial(material);
			return true;
		}

		[Preserve]
		protected override void OnUpdate()
		{
			if (!TryRegisterResources())
			{
				return;
			}
			int num = m_updateEntityQuery.CalculateEntityCount();
			if (num == 0)
			{
				m_instanceCount = 0;
				return;
			}
			m_instanceCount = num;
			if (num > m_instanceCapacity)
			{
				AllocateBuffer(48 + m_sizeOfInstance * num);
				UpdateBatch();
			}
			PopulateBuffer();
		}

		private void InitializeProperties()
		{
			int num = UnsafeUtility.SizeOf<float3x4>();
			int num2 = UnsafeUtility.SizeOf<float4>();
			NativeArray<PropertyInfo> properties = new NativeArray<PropertyInfo>(7, Allocator.Persistent);
			properties[0] = new PropertyInfo(Shader.PropertyToID("unity_ObjectToWorld"), num);
			properties[1] = new PropertyInfo(Shader.PropertyToID("unity_WorldToObject"), num);
			properties[2] = new PropertyInfo(Shader.PropertyToID("_Color"), num2);
			properties[3] = new PropertyInfo(Shader.PropertyToID("_Rect"), num2);
			properties[4] = new PropertyInfo(Shader.PropertyToID("_UVRect"), num2);
			properties[5] = new PropertyInfo(Shader.PropertyToID("_BlendFactor"), num2);
			properties[6] = new PropertyInfo(Shader.PropertyToID("_LayerTint"), num2);
			m_sizeOfInstance = 2 * num + 5 * num2;
			m_properties = properties;
		}

		// Decides whether the instance data has to live in a constant buffer.  On GLES/WebGL the
		// BatchRendererGroup refuses a raw buffer altogether ("AddBatch requires a non-zero windowSize
		// parameter when BufferTarget is ConstantBuffer"), and a constant buffer window is tiny
		// (UnityDOTSInstancing.hlsl declares float4 unity_DOTSInstanceDataRaw[1024] = 16 KB), so the
		// scene is split into pages of m_instancesPerPage parts, one batch per page.
		private void ConfigureConstantBuffer()
		{
			BatchBufferTarget bufferTarget = BatchRendererGroup.BufferTarget;
			m_constantBuffer = bufferTarget == BatchBufferTarget.ConstantBuffer;
			m_instancesPerPage = 0;
			m_pageBytes = 0;
			m_pageStride = 0;
			if (m_constantBuffer)
			{
				int num = (int)BatchRendererGroup.GetConstantBufferMaxWindowSize();
				int num2 = math.min(num, 16384);
				if (SystemInfo.maxConstantBufferSize >= m_sizeOfInstance)
				{
					num2 = math.min(num2, SystemInfo.maxConstantBufferSize);
				}
				int num3 = math.max(16, (int)BatchRendererGroup.GetConstantBufferOffsetAlignment());
				m_instancesPerPage = math.max(1, num2 / m_sizeOfInstance);
				m_pageBytes = num2;
				m_pageStride = (m_pageBytes + num3 - 1) / num3 * num3;
				while (m_pageStride % 16 != 0)
				{
					m_pageStride += num3;
				}
				Debug.Log(string.Format("[SceneRenderSystem] constant buffer instancing: target={0} window={1} alignment={2} partsPerPage={3} pageStride={4} maxWindow={5}", bufferTarget, m_pageBytes, num3, m_instancesPerPage, m_pageStride, num));
			}
		}

		private void AllocateBuffer(int size)
		{
			if (m_bufferSize >= size)
			{
				return;
			}
			int num;
			for (num = ((m_bufferSize == 0) ? InitialBufferSize : m_bufferSize); num < size; num *= 2)
			{
			}
			if (m_buffer != null || m_pageBatches.IsCreated)
			{
				// The old batches still point at this buffer, so they have to be unregistered
				// *before* the buffer goes away; otherwise the renderer keeps a dangling handle to a
				// disposed GraphicsBuffer.  The culling job of the previous frame reads the batch
				// array, so it has to finish first.
				m_performCullingDependency.Complete();
				m_performCullingDependency = default(JobHandle);
				RemoveBatches();
				if (m_buffer != null)
				{
					m_buffer.Dispose();
				}
				if (m_pageBatches.IsCreated)
				{
					m_pageBatches.Dispose();
				}
			}
			m_bufferSize = num;
			int num2 = math.max(1, (num - 48) / m_sizeOfInstance);
			if (m_constantBuffer)
			{
				int num3 = (num2 + m_instancesPerPage - 1) / m_instancesPerPage;
				m_instanceCapacity = num3 * m_instancesPerPage;
				m_pageCount = num3;
				m_buffer = new GraphicsBuffer(GraphicsBuffer.Target.Constant, num3 * m_pageStride / 16, 16);
			}
			else
			{
				m_instanceCapacity = num2;
				m_pageCount = 1;
				m_buffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, GraphicsBuffer.UsageFlags.LockBufferForWrite, num / 4, 4);
			}
			m_pageBatches = new NativeArray<BatchID>(m_pageCount, Allocator.Persistent, NativeArrayOptions.ClearMemory);
		}

		private void RemoveBatches()
		{
			m_batchesReady = false;
			if (!m_pageBatches.IsCreated)
			{
				return;
			}
			for (int i = 0; i < m_pageBatches.Length; i++)
			{
				if (m_pageBatches[i] != BatchID.Null)
				{
					m_batchRendererGroup.RemoveBatch(m_pageBatches[i]);
					m_pageBatches[i] = BatchID.Null;
				}
			}
		}

		// Constant buffer upload: every page is bound on its own, and the shader addresses instance
		// data relative to the window it is bound with, so the per-property blocks have to be laid
		// out page by page instead of once for the whole scene.
		private unsafe void UploadPages(NativeArray<GpuBufferInfo> bufferInfo)
		{
			int num = (m_instanceCount + m_instancesPerPage - 1) / m_instancesPerPage;
			int num2 = num * m_pageStride;
			if (num2 > m_buffer.count * m_buffer.stride)
			{
				Debug.LogError(string.Format("[SceneRenderSystem] constant buffer too small: {0} < {1}", m_buffer.count * m_buffer.stride, num2));
				return;
			}
			NativeArray<float4> nativeArray = new NativeArray<float4>(num2 / 16, Allocator.Temp, NativeArrayOptions.ClearMemory);
			byte* ptr = (byte*)nativeArray.GetUnsafePtr();
			for (int i = 0; i < num; i++)
			{
				int num3 = i * m_instancesPerPage;
				byte* ptr2 = ptr + i * m_pageStride;
				int num4 = 0;
				for (int j = 0; j < bufferInfo.Length; j++)
				{
					GpuBufferInfo gpuBufferInfo = bufferInfo[j];
					int stride = gpuBufferInfo.Stride;
					int num5 = math.min(m_instancesPerPage, gpuBufferInfo.Count - num3);
					if (num5 > 0)
					{
						UnsafeUtility.MemCpy(ptr2 + num4, (byte*)gpuBufferInfo.Source.GetUnsafePtr() + (long)num3 * stride, (long)num5 * stride);
					}
					num4 += m_instancesPerPage * stride;
				}
			}
			m_buffer.SetData(nativeArray, 0, 0, nativeArray.Length);
			nativeArray.Dispose();
		}

		private void PopulateBuffer()
		{
			m_performCullingDependency.Complete();
			m_performCullingDependency = default(JobHandle);
			int instanceCount = m_instanceCount;
			int instanceCapacity = m_instanceCapacity;
			int num = 48;
			NativeArray<GpuBufferInfo> bufferInfo = new NativeArray<GpuBufferInfo>(m_properties.Length, Allocator.TempJob);
			for (int i = 0; i < m_properties.Length; i++)
			{
				PropertyInfo propertyInfo = m_properties[i];
				NativeArray<byte> source = new NativeArray<byte>(instanceCount * propertyInfo.Size, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
				bufferInfo[i] = new GpuBufferInfo(source, num, instanceCount, propertyInfo.Size);
				num += instanceCapacity * propertyInfo.Size;
			}
			NativeArray<int> entityIndices = m_updateEntityQuery.CalculateBaseEntityIndexArray(Allocator.TempJob);
			__Bpsim_Rendering_BlendFactor_RO_ComponentTypeHandle.Update(ref base.CheckedStateRef);
			__Bpsim_Rendering_MeshUVRect_RO_ComponentTypeHandle.Update(ref base.CheckedStateRef);
			__Bpsim_Rendering_MeshRect_RO_ComponentTypeHandle.Update(ref base.CheckedStateRef);
			__Unity_Rendering_MaterialColor_RO_ComponentTypeHandle.Update(ref base.CheckedStateRef);
			__Bpsim_Rendering_PartLayerTint_RO_ComponentTypeHandle.Update(ref base.CheckedStateRef);
			__Unity_Transforms_LocalToWorld_RO_ComponentTypeHandle.Update(ref base.CheckedStateRef);
			__Bpsim_Parts_PartRenderInfo_RW_ComponentTypeHandle.Update(ref base.CheckedStateRef);
			JobChunkExtensions.ScheduleParallel(new CopyPropertiesJob
			{
				EntityIndices = entityIndices,
				PartRenderInfoHandle = __Bpsim_Parts_PartRenderInfo_RW_ComponentTypeHandle,
				LocalToWorldHandle = __Unity_Transforms_LocalToWorld_RO_ComponentTypeHandle,
				MaterialColorHandle = __Unity_Rendering_MaterialColor_RO_ComponentTypeHandle,
				LayerTintHandle = __Bpsim_Rendering_PartLayerTint_RO_ComponentTypeHandle,
				MeshRectHandle = __Bpsim_Rendering_MeshRect_RO_ComponentTypeHandle,
				MeshUVRectHandle = __Bpsim_Rendering_MeshUVRect_RO_ComponentTypeHandle,
				BlendFactorHandle = __Bpsim_Rendering_BlendFactor_RO_ComponentTypeHandle,
				BufferInfo = bufferInfo
			}, m_updateEntityQuery, base.Dependency).Complete();
			entityIndices.Dispose();
			if (m_constantBuffer)
			{
				// Constant buffers are uploaded page by page; see UploadPages.
				UploadPages(bufferInfo);
			}
			else
			{
				// One single lock/unlock pair for the whole instance block.  Locking the
				// same GraphicsBuffer once per property (6 times a frame) makes the D3D11
				// backend report "multiple uploads in flight ... Falling back to slow
				// path", keeps a staging resource per pending upload and eventually kills
				// the device (device reset -> editor shutdown after a few minutes of Play).
				// Every property region is contiguous in the buffer, so they can be
				// filled from a single locked range.
				int uploadBytes = m_sizeOfInstance * instanceCapacity;
				NativeArray<byte> dst2 = m_buffer.LockBufferForWrite<byte>(48, uploadBytes);
				for (int j = 0; j < bufferInfo.Length; j++)
				{
					GpuBufferInfo gpuBufferInfo = bufferInfo[j];
					int num2 = gpuBufferInfo.Count * gpuBufferInfo.Stride;
					if (num2 <= 0)
					{
						continue;
					}
					NativeArray<byte> dst3 = dst2.GetSubArray(gpuBufferInfo.Offset - 48, num2);
					NativeArray<byte>.Copy(gpuBufferInfo.Source, dst3);
				}
				m_buffer.UnlockBufferAfterWrite<byte>(uploadBytes);
			}
			for (int k = 0; k < bufferInfo.Length; k++)
			{
				bufferInfo[k].Source.Dispose();
			}
			bufferInfo.Dispose();
		}

		// How the batch metadata reaches the shader (worked out the hard way, see
		// BPSIM_EDITOR_MIGRATION.md):
		//   * the shader's DOTS instancing block MUST be named "MaterialPropertyMetadata" - Unity
		//     only fills a metadata cbuffer with that name, any other name silently reads zeros, so
		//     every property falls back to its UnityPerMaterial default;
		//   * the array must be sorted by NameID ascending (documented requirement of
		//     BatchRendererGroup.AddBatch); the buffer layout stays in m_properties order.
		private void UpdateBatch()
		{
			int num = (m_constantBuffer ? 0 : 48);
			int num2 = (m_constantBuffer ? m_instancesPerPage : m_instanceCapacity);
			// BatchRendererGroup requires the metadata array to be sorted by NameID ascending; the
			// buffer layout (PopulateBuffer) stays in m_properties order, only this array is sorted.
			int count = m_properties.Length;
			int[] ids = new int[count];
			uint[] values = new uint[count];
			for (int i = 0; i < count; i++)
			{
				PropertyInfo propertyInfo = m_properties[i];
				ids[i] = propertyInfo.ID;
				values[i] = (uint)(int.MinValue | num);
				num += num2 * propertyInfo.Size;
			}
			for (int j = 1; j < count; j++)
			{
				int keyId = ids[j];
				uint keyValue = values[j];
				int k = j - 1;
				while (k >= 0 && ids[k] > keyId)
				{
					ids[k + 1] = ids[k];
					values[k + 1] = values[k];
					k--;
				}
				ids[k + 1] = keyId;
				values[k + 1] = keyValue;
			}
			NativeArray<MetadataValue> batchMetadata = new NativeArray<MetadataValue>(count, Allocator.Temp);
			for (int l = 0; l < count; l++)
			{
				batchMetadata[l] = new MetadataValue
				{
					NameID = ids[l],
					Value = values[l]
				};
			}
			m_batchesReady = true;
			if (m_constantBuffer)
			{
				// One batch per page.  Every batch binds its own window of the buffer, so the shader
				// addresses instance data relative to zero again.
				for (int m = 0; m < m_pageCount; m++)
				{
					m_pageBatches[m] = m_batchRendererGroup.AddBatch(batchMetadata, m_buffer.bufferHandle, (uint)(m * m_pageStride), (uint)m_pageBytes);
					if (m_pageBatches[m] == BatchID.Null)
					{
						m_batchesReady = false;
						Debug.LogError(string.Format("[SceneRenderSystem] AddBatch failed for page {0}/{1} (offset={2}, window={3})", m, m_pageCount, m * m_pageStride, m_pageBytes));
					}
				}
			}
			else
			{
				m_pageBatches[0] = m_batchRendererGroup.AddBatch(batchMetadata, m_buffer.bufferHandle);
				if (m_pageBatches[0] == BatchID.Null)
				{
					m_batchesReady = false;
					Debug.LogError("[SceneRenderSystem] AddBatch failed");
				}
			}
		}

		[Preserve]
		protected override void OnDestroy()
		{
			m_performCullingDependency.Complete();
			m_performCullingDependency = default(JobHandle);
			RemoveBatches();
			if (m_pageBatches.IsCreated)
			{
				m_pageBatches.Dispose();
			}
			m_batchRendererGroup.Dispose();
			m_buffer?.Dispose();
			m_buffer = null;
			if (m_properties.IsCreated)
			{
				m_properties.Dispose();
			}
		}

		public JobHandle OnPerformCulling(BatchRendererGroup rendererGroup, BatchCullingContext cullingContext, BatchCullingOutput cullingOutput, IntPtr userContext)
		{
			if (!m_batchesReady || m_instanceCount == 0)
			{
				return m_performCullingDependency;
			}
			NativeList<SortPriority> sortedIndices = new NativeList<SortPriority>(m_instanceCount, Allocator.TempJob);
			JobHandle jobHandle = JobChunkExtensions.ScheduleParallel(new CullingJob
			{
				Planes = cullingContext.cullingSplits[0].cullingMatrix.decomposeProjection,
				PartRenderInfoHandle = GetComponentTypeHandle<PartRenderInfo>(isReadOnly: true),
				WorldTransformHandle = GetComponentTypeHandle<WorldTransform>(isReadOnly: true),
				InstancesPerPage = m_instancesPerPage,
				SortedIndices = sortedIndices.AsParallelWriter()
			}, m_cullingEntityQuery, m_performCullingDependency);
			JobHandle dependsOn = jobHandle;
			if (m_enableDepthSorting)
			{
				dependsOn = IJobExtensions.Schedule(new SortIndicesJob
				{
					SortedIndices = sortedIndices
				}, jobHandle);
			}
			JobHandle inputDeps = IJobExtensions.Schedule(new EmitDrawCommandsJob
			{
				BatchIDs = m_pageBatches,
				InstancesPerPage = m_instancesPerPage,
				MeshID = m_meshID,
				MaterialID = m_materialID,
				SortedIndices = sortedIndices,
				CullingOutput = cullingOutput
			}, dependsOn);
			JobHandle performCullingDependency = sortedIndices.Dispose(inputDeps);
			m_performCullingDependency = performCullingDependency;
			return m_performCullingDependency;
		}

		private static float3x4 PackMatrix(in float4x4 matrix)
		{
			return new float3x4(matrix.c0.xyz, matrix.c1.xyz, matrix.c2.xyz, matrix.c3.xyz);
		}

		private void __AssignHandlesImpl(ref SystemState state)
		{
			EntityQueryBuilder entityQueryBuilder = new EntityQueryBuilder(Allocator.Temp);
			EntityQueryBuilder entityQueryBuilder2 = entityQueryBuilder.WithAll<PartRenderInfo, LocalToWorld, MaterialColor, MeshRect, MeshUVRect, BlendFactor>();
			entityQueryBuilder2 = entityQueryBuilder2.WithNone<DisableRendering>();
			__query_595537251_0 = entityQueryBuilder2.Build(ref state);
			entityQueryBuilder.Reset();
			entityQueryBuilder2 = entityQueryBuilder.WithAll<PartRenderInfo>();
			entityQueryBuilder2 = entityQueryBuilder2.WithNone<DisableRendering>();
			__query_595537251_1 = entityQueryBuilder2.Build(ref state);
			entityQueryBuilder.Reset();
			entityQueryBuilder.Dispose();
			__Bpsim_Parts_PartRenderInfo_RW_ComponentTypeHandle = state.GetComponentTypeHandle<PartRenderInfo>();
			__Unity_Transforms_LocalToWorld_RO_ComponentTypeHandle = state.GetComponentTypeHandle<LocalToWorld>(isReadOnly: true);
			__Unity_Rendering_MaterialColor_RO_ComponentTypeHandle = state.GetComponentTypeHandle<MaterialColor>(isReadOnly: true);
			__Bpsim_Rendering_PartLayerTint_RO_ComponentTypeHandle = state.GetComponentTypeHandle<PartLayerTint>(isReadOnly: true);
			__Bpsim_Rendering_MeshRect_RO_ComponentTypeHandle = state.GetComponentTypeHandle<MeshRect>(isReadOnly: true);
			__Bpsim_Rendering_MeshUVRect_RO_ComponentTypeHandle = state.GetComponentTypeHandle<MeshUVRect>(isReadOnly: true);
			__Bpsim_Rendering_BlendFactor_RO_ComponentTypeHandle = state.GetComponentTypeHandle<BlendFactor>(isReadOnly: true);
		}

		protected override void OnCreateForCompiler()
		{
			base.OnCreateForCompiler();
			__AssignHandlesImpl(ref base.CheckedStateRef);
		}

		[Preserve]
		public SceneRenderSystem()
		{
		}
	}
}
