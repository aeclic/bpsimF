using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Bpsim.Parts
{
	public static class PartSceneJobs
	{
		[BurstCompile]
		public struct SelectPartsJob : IJob
		{
			public Reference<PartSceneUnmanaged> PartScene;

			[ReadOnly]
			public int4 Selection;

			[ReadOnly]
			public ComponentLookup<PartTransform> PartTransformLookup;

			[WriteOnly]
			public NativeList<Entity> Result;

			public void Execute()
			{
				int2 @int = new int2(Selection.x, Selection.y);
				int2 int2 = new int2(Selection.z, Selection.w);
				if (int2.x * int2.y <= PartScene.Value.Parts.Length)
				{
					for (int i = @int.x; i < @int.x + int2.x; i++)
					{
						for (int j = @int.y; j < @int.y + int2.y; j++)
						{
							if (PartScene.Value.FindPartGridAt(i, j, out var grid))
							{
								if (grid.HasPartContainer)
								{
									Result.Add(in grid.PartContainer);
								}
								if (grid.HasPart)
								{
									Result.Add(in grid.Part);
								}
							}
						}
					}
					return;
				}
				foreach (Entity part in PartScene.Value.Parts)
				{
					Entity value = part;
					PartTransform partTransform = PartTransformLookup[value];
					int x = partTransform.X;
					int y = partTransform.Y;
					if (x >= @int.x && x < @int.x + int2.x && y >= @int.y && y < @int.y + int2.y)
					{
						Result.Add(in value);
					}
				}
			}
		}

		[BurstCompile]
		public struct PlacePartsJob : IJob
		{
			public Reference<PartSceneUnmanaged> PartScene;

			[ReadOnly]
			public bool Overlay;

			[ReadOnly]
			public NativeArray<Schematics.Unit> Schematics;

			[ReadOnly]
			public NativeParallelHashMap<PartTypeInfo, Entity> PrefabCollection;

			public void Execute()
			{
				int sceneID = PartScene.Value.SceneID;
				for (int i = 0; i < Schematics.Length; i++)
				{
					Schematics.Unit unit = Schematics[i];
					PartTypeInfo key = new PartTypeInfo((PartType)unit.Type, unit.Index);
					if (PrefabCollection.TryGetValue(key, out var item))
					{
						PartCreationRequest request = new PartCreationRequest(item, sceneID, unit);
						PartScene.Value.PlacePartAt(in request, Overlay);
					}
				}
				PartScene.Value.Update();
			}
		}

		[BurstCompile]
		public struct RemovePartsJob : IJob
		{
			public Reference<PartSceneUnmanaged> PartScene;

			[ReadOnly]
			public int4 Selection;

			public void Execute()
			{
				int2 @int = new int2(Selection.x, Selection.y);
				int2 int2 = new int2(Selection.z, Selection.w);
				if (int2.x * int2.y <= PartScene.Value.Parts.Length)
				{
					for (int i = @int.x; i < @int.x + int2.x; i++)
					{
						for (int j = @int.y; j < @int.y + int2.y; j++)
						{
							PartScene.Value.RemovePartAt(i, j);
						}
					}
					PartScene.Value.Update();
					return;
				}
				foreach (Entity part in PartScene.Value.Parts)
				{
					PartTransform componentData = PartScene.Value.EntityManager.GetComponentData<PartTransform>(part);
					int x = componentData.X;
					int y = componentData.Y;
					if (x >= @int.x && x < @int.x + int2.x && y >= @int.y && y < @int.y + int2.y)
					{
						PartScene.Value.RemovePartAt(x, y);
					}
				}
				PartScene.Value.Update();
			}
		}

		[BurstCompile]
		public struct SavePartsJob : IJob
		{
			[ReadOnly]
			public NativeList<Entity> Parts;

			[ReadOnly]
			public ComponentLookup<PartTypeValue> PartTypeValueLookup;

			[ReadOnly]
			public ComponentLookup<PartTransform> PartTransformLookup;

			[ReadOnly]
			public ComponentLookup<PartFreeTransform> FreeTransformLookup;

			[ReadOnly]
			public ComponentLookup<PartBuildLayer> LayerLookup;

			[ReadOnly]
			public ComponentLookup<FrameShape> ShapeLookup;

			[WriteOnly]
			public NativeArray<Schematics.Unit> Schematics;

			public void Execute()
			{
				for (int i = 0; i < Parts.Length; i++)
				{
					PartTypeInfo value = PartTypeValueLookup[Parts[i]].Value;
					PartTransform partTransform = PartTransformLookup[Parts[i]];
					PartFreeTransform freeTransform = FreeTransformLookup.TryGetComponent(Parts[i], out var free) ? PartFreeTransform.Sanitize(free) : PartFreeTransform.Identity;
					int layer = LayerLookup.TryGetComponent(Parts[i], out var buildLayer) ? PartBuildLayer.Clamp(buildLayer.Value) : PartBuildLayer.MinLayer;
					Schematics[i] = new Schematics.Unit(partTransform.X, partTransform.Y, (int)value.PartType, value.PartIndex, partTransform.Rotation, partTransform.Flipped, freeTransform.Offset.x, freeTransform.Offset.y, freeTransform.RotationDegrees, freeTransform.Scale.x, freeTransform.Scale.y, layer, ShapeLookup.TryGetComponent(Parts[i], out var shape) ? shape.Value : 0);
				}
			}
		}

		[BurstCompile]
		public struct MoveSchematicsJob : IJob
		{
			[ReadOnly]
			public int2 TargetPoint;

			[ReadOnly]
			public bool UseAbsoluteCoord;

			public NativeArray<Schematics.Unit> Schematics;

			public void Execute()
			{
				int2 targetPoint = TargetPoint;
				int length = Schematics.Length;
				if (UseAbsoluteCoord && length >= 1)
				{
					int2 @int = new int2(Schematics[0].X, Schematics[0].Y);
					for (int i = 0; i < length; i++)
					{
						Schematics.Unit unit = Schematics[i];
						@int = math.min(@int, new int2(unit.X, unit.Y));
					}
					targetPoint -= @int;
				}
				for (int j = 0; j < length; j++)
				{
					Schematics.Unit unit2 = Schematics[j];
					Schematics[j] = unit2.WithPosition(targetPoint.x + unit2.X, targetPoint.y + unit2.Y);
				}
			}
		}

		public static SelectPartsJob SelectParts(PartScene partScene, int4 selection, NativeList<Entity> result)
		{
			return new SelectPartsJob
			{
				PartScene = partScene.UnmanagedRef,
				Selection = selection,
				PartTransformLookup = PartManager.Instance.System.GetComponentLookup<PartTransform>(),
				Result = result
			};
		}

		[RequiresSyncPoint]
		public static PlacePartsJob PlaceParts(PartScene partScene, bool overlay, NativeArray<Schematics.Unit> schematics)
		{
			return new PlacePartsJob
			{
				PartScene = partScene.UnmanagedRef,
				Overlay = overlay,
				Schematics = schematics,
				PrefabCollection = PartManager.Instance.EntitySpawner.Collection
			};
		}

		[RequiresSyncPoint]
		public static RemovePartsJob RemoveParts(PartScene partScene, int4 selection)
		{
			return new RemovePartsJob
			{
				PartScene = partScene.UnmanagedRef,
				Selection = selection
			};
		}

		public static SavePartsJob SaveParts(NativeList<Entity> parts, NativeArray<Schematics.Unit> schematics)
		{
			return new SavePartsJob
			{
				Parts = parts,
				PartTypeValueLookup = PartManager.Instance.System.GetComponentLookup<PartTypeValue>(),
				PartTransformLookup = PartManager.Instance.System.GetComponentLookup<PartTransform>(),
				FreeTransformLookup = PartManager.Instance.System.GetComponentLookup<PartFreeTransform>(),
				LayerLookup = PartManager.Instance.System.GetComponentLookup<PartBuildLayer>(),
				ShapeLookup = PartManager.Instance.System.GetComponentLookup<FrameShape>(),
				Schematics = schematics
			};
		}

		public static MoveSchematicsJob MoveSchematics(int2 targetPoint, bool useAbsoluteCoord, NativeArray<Schematics.Unit> schematics)
		{
			return new MoveSchematicsJob
			{
				TargetPoint = targetPoint,
				UseAbsoluteCoord = useAbsoluteCoord,
				Schematics = schematics
			};
		}
	}
}
