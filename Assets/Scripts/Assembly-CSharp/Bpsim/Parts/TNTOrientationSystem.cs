using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace Bpsim.Parts
{
	internal struct TNTVisualization : IComponentData
	{
		public Entity Root;
		public quaternion BaseRotation;
	}

	internal struct OrientationArtwork : IBufferElementData
	{
		public Entity Child;
		public int Mode;

		public static void Bake(EntityManager manager, Entity root, Entity child, PartTypeInfo type, string name)
		{
			int mode;
			if (type == BasePart.AutoControlLight)
			{
				if (name == "InactivePointLightSprite") mode = 0;
				else if (name == "InactiveFullPointLightSprite") mode = 4;
				else return;
			}
			else if (type.PartType == PartType.Kicker)
			{
				mode = name switch { "Deg0" => 0, "Deg90" => 1, "Deg180" => 2, "Deg270" => 3, _ => -1 };
				if (mode < 0) return;
			}
			else if (type.BelongsTo(BasePart.HingePlates) && (name == "Plate1" || name == "Plate2"))
			{
				mode = -1;
			}
			else return;
			DynamicBuffer<OrientationArtwork> buffer = manager.HasBuffer<OrientationArtwork>(root)
				? manager.GetBuffer<OrientationArtwork>(root) : manager.AddBuffer<OrientationArtwork>(root);
			buffer.Add(new OrientationArtwork { Child = child, Mode = mode });
		}
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
			var localTransforms = GetComponentLookup<LocalTransform>(true);
			var renderingDisabled = GetComponentLookup<DisableRendering>(true);
			var commands = new EntityCommandBuffer(Allocator.TempJob);
			try
			{
				Entities.WithChangeFilter<PartTransform>().WithReadOnly(localTransforms).WithReadOnly(renderingDisabled)
				.ForEach((in PartTransform transform, in PartTypeValue type, in DynamicBuffer<OrientationArtwork> artwork) =>
				{
					PartType family = type.BaseValue.PartType;
					int mode = family == PartType.PointLight ? (transform.Rotation % 5 + 5) % 5 : transform.Rotation & 7;
					if (family == PartType.PointLight && mode != 4) mode = 0;
					foreach (OrientationArtwork item in artwork)
					{
						if (item.Mode < 0)
						{
							LocalTransform child = localTransforms[item.Child];
							float height = HingePlate.Height(HingePlate.NormalizeLayout(transform.Rotation));
							if (child.Position.y != height)
							{
								child.Position.y = height;
								commands.SetComponent(item.Child, child);
							}
						}
						else
						{
							bool visible = item.Mode == mode;
							if (visible == renderingDisabled.HasComponent(item.Child))
							{
								if (visible) commands.RemoveComponent<DisableRendering>(item.Child);
								else commands.AddComponent<DisableRendering>(item.Child);
							}
						}
					}
				}).Run();
				if (!commands.IsEmpty) commands.Playback(EntityManager);
			}
			finally
			{
				commands.Dispose();
			}
		}
	}
}
