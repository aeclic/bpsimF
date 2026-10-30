using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Bpsim.Parts
{
	internal struct TNTVisualization : IComponentData
	{
		public Entity Root;
		public quaternion BaseRotation;
	}

	/// <summary>TNT keeps its collider orthogonal and tilts its artwork, as in BPSS ApplyOrientation.</summary>
	[UpdateInGroup(typeof(TransformSystemGroup))]
	[UpdateAfter(typeof(PartTransformSystem))]
	[UpdateBefore(typeof(ParentSystem))]
	internal partial class TNTOrientationSystem : SystemBase
	{
		protected override void OnUpdate()
		{
			if (PartManager.Instance == null || PartManager.Instance.IsSimulating) return;
			var transforms = GetComponentLookup<PartTransform>(true);
			Entities.WithReadOnly(transforms).ForEach((ref LocalTransform transform, in TNTVisualization visualization) => {
				if (transforms.HasComponent(visualization.Root))
					transform.Rotation = math.mul(visualization.BaseRotation,
						quaternion.RotateZ(transforms[visualization.Root].Rotation >= 4 ? math.PI / 4f : 0f));
			}).ScheduleParallel();
		}
	}
}
