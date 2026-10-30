using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Physics.GraphicsIntegration;
using Unity.Transforms;
using UnityEngine.Scripting;

namespace Bpsim.Parts.Simulation
{
	/// <summary>
	/// Keeps <see cref="WorldTransform"/> in sync with <see cref="LocalTransform"/> for parts whose
	/// <see cref="LocalToWorld"/> is driven by Unity.Physics' graphical smoothing.
	/// </summary>
	/// <remarks>
	/// <see cref="PhysicsGraphicalSmoothing"/> is marked <c>[WriteGroup(typeof(LocalToWorld))]</c>, so
	/// <see cref="LocalToWorldSystem"/>'s root query (which uses
	/// <c>EntityQueryOptions.FilterWriteGroup</c>) deliberately skips every entity that has it: the
	/// smoothing system owns the matrix, and the transform system must not overwrite it. The side
	/// effect is that those entities never get their <see cref="WorldTransform"/> refreshed either,
	/// because both are written by the same job.
	///
	/// This project still reads <see cref="WorldTransform"/> outside of rendering (joint frames in
	/// <c>FrameJointSystem</c>/<c>PartSimulator</c>, the part property panel angle readout), so it is
	/// kept up to date here. For a root entity (no <c>Parent</c>) the two components hold the same
	/// value, which is exactly what the skipped root job would have written.
	/// </remarks>
	[UpdateInGroup(typeof(TransformSystemGroup))]
	[UpdateAfter(typeof(SmoothRigidBodiesGraphicalMotion))]
	[UpdateBefore(typeof(LocalToWorldSystem))]
	internal partial class PartWorldTransformSyncSystem : SystemBase
	{
		[BurstCompile(FloatMode = FloatMode.Default, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = false)]
		private struct SyncWorldTransformJob : IJobChunk
		{
			[ReadOnly]
			public ComponentTypeHandle<LocalTransform> LocalTransformTypeHandle;

			public ComponentTypeHandle<WorldTransform> WorldTransformTypeHandle;

			public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				NativeArray<LocalTransform> localTransforms = chunk.GetNativeArray(ref LocalTransformTypeHandle);
				NativeArray<WorldTransform> worldTransforms = chunk.GetNativeArray(ref WorldTransformTypeHandle);
				for (int i = 0; i < chunk.Count; i++)
				{
					worldTransforms[i] = (WorldTransform)localTransforms[i];
				}
			}
		}

		private EntityQuery m_query;

		[Preserve]
		protected override void OnCreate()
		{
			m_query = GetEntityQuery(new EntityQueryDesc
			{
				All = new ComponentType[]
				{
					ComponentType.ReadOnly<LocalTransform>(),
					ComponentType.ReadWrite<WorldTransform>(),
					ComponentType.ReadOnly<PhysicsGraphicalSmoothing>()
				},
				None = new ComponentType[]
				{
					ComponentType.ReadOnly<Parent>()
				}
			});
			RequireForUpdate(m_query);
		}

		[Preserve]
		protected override void OnUpdate()
		{
			Dependency = new SyncWorldTransformJob
			{
				LocalTransformTypeHandle = GetComponentTypeHandle<LocalTransform>(isReadOnly: true),
				WorldTransformTypeHandle = GetComponentTypeHandle<WorldTransform>()
			}.ScheduleParallel(m_query, Dependency);
		}
	}
}
