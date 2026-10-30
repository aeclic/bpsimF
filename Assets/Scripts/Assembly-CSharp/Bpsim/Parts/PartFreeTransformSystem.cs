using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Bpsim.Parts
{
	/// <summary>
	/// 在 <see cref="PartTransformSystem" /> 写好整数格位置/离散朝向之后，把
	/// <see cref="PartFreeTransform" /> 的自由偏移、连续角与缩放合成进 <see cref="LocalTransform" />。
	/// 缩放写到根实体的 <see cref="PostTransformScale" />：部件 prefab 根节点自身缩放都是 1，
	/// 所以覆盖它不会破坏美术层级（视觉缩放都在子物体上）。
	/// 模拟中（<c>IsSimulating</c>）不跑：物理系统接管 LocalTransform，自由变换只属于建造期。
	/// </summary>
	[UpdateAfter(typeof(PartTransformSystem))]
	[UpdateBefore(typeof(ParentSystem))]
	[UpdateInGroup(typeof(TransformSystemGroup))]
	internal partial class PartFreeTransformSystem : SystemBase
	{
		[BurstCompile]
		private struct ComposeJob : IJobChunk
		{
			public ComponentTypeHandle<LocalTransform> LocalTransformHandle;

			[ReadOnly]
			public ComponentTypeHandle<PartFreeTransform> FreeTransformHandle;

			[ReadOnly]
			public ComponentTypeHandle<PartBuildLayer> LayerHandle;

			public ComponentTypeHandle<PostTransformScale> PostTransformScaleHandle;

			public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				NativeArray<LocalTransform> localTransforms = chunk.GetNativeArray(ref LocalTransformHandle);
				NativeArray<PartFreeTransform> freeTransforms = chunk.GetNativeArray(ref FreeTransformHandle);
				NativeArray<PostTransformScale> postScales = chunk.GetNativeArray(ref PostTransformScaleHandle);
				bool hasLayer = chunk.Has(ref LayerHandle);
				NativeArray<PartBuildLayer> layers = hasLayer ? chunk.GetNativeArray(ref LayerHandle) : default(NativeArray<PartBuildLayer>);
				for (int i = 0; i < chunk.Count; i++)
				{
					PartFreeTransform free = PartFreeTransform.Sanitize(freeTransforms[i]);
					int layer = hasLayer ? PartBuildLayer.Clamp(layers[i].Value) : PartBuildLayer.MinLayer;
					LocalTransform local = localTransforms[i];
					local.Position += new float3(free.Offset.x, free.Offset.y, -((float)(layer - PartBuildLayer.MinLayer) * PartBuildLayer.DepthStep));
					if (free.RotationDegrees != 0f)
					{
						local.Rotation = math.mul(local.Rotation, quaternion.RotateZ(math.radians(free.RotationDegrees)));
					}
					localTransforms[i] = local;
					postScales[i] = new PostTransformScale
					{
						Value = float3x3.Scale(free.Scale.x, free.Scale.y, 1f)
					};
				}
			}
		}

		private EntityQuery m_query;

		private ComponentTypeHandle<LocalTransform> m_localTransformHandle;

		private ComponentTypeHandle<PartFreeTransform> m_freeTransformHandle;

		private ComponentTypeHandle<PartBuildLayer> m_layerHandle;

		private ComponentTypeHandle<PostTransformScale> m_postTransformScaleHandle;

		protected override void OnCreate()
		{
			EntityQueryBuilder builder = new EntityQueryBuilder(Allocator.Temp);
			builder = builder.WithAllRW<LocalTransform>();
			builder = builder.WithAll<PartTag>();
			builder = builder.WithAll<PartFreeTransform>();
			builder = builder.WithAllRW<PostTransformScale>();
			m_query = builder.Build(ref base.CheckedStateRef);
			m_localTransformHandle = base.CheckedStateRef.GetComponentTypeHandle<LocalTransform>(isReadOnly: false);
			m_freeTransformHandle = base.CheckedStateRef.GetComponentTypeHandle<PartFreeTransform>(isReadOnly: true);
			m_layerHandle = base.CheckedStateRef.GetComponentTypeHandle<PartBuildLayer>(isReadOnly: true);
			m_postTransformScaleHandle = base.CheckedStateRef.GetComponentTypeHandle<PostTransformScale>(isReadOnly: false);
		}

		protected override void OnUpdate()
		{
			if (PartManager.Instance == null || PartManager.Instance.IsSimulating)
			{
				return;
			}
			m_localTransformHandle.Update(ref base.CheckedStateRef);
			m_freeTransformHandle.Update(ref base.CheckedStateRef);
			m_layerHandle.Update(ref base.CheckedStateRef);
			m_postTransformScaleHandle.Update(ref base.CheckedStateRef);
			ComposeJob job = new ComposeJob
			{
				LocalTransformHandle = m_localTransformHandle,
				FreeTransformHandle = m_freeTransformHandle,
				LayerHandle = m_layerHandle,
				PostTransformScaleHandle = m_postTransformScaleHandle
			};
			base.Dependency = job.ScheduleParallel(m_query, base.Dependency);
		}
	}
}
