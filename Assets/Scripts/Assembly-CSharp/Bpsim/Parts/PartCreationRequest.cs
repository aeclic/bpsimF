using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Bpsim.Parts
{
	public readonly struct PartCreationRequest
	{
		public readonly Entity Entity;

		public readonly int SceneID;

		public readonly PartTypeInfo TypeInfo;

		public readonly int CoordX;

		public readonly int CoordY;

		public readonly int Rotation;

		public readonly bool Flipped;

		/// <summary>无网格自由平移（世界单位），随部件一起存档。</summary>
		public readonly float OffsetX;

		public readonly float OffsetY;

		/// <summary>叠加在离散朝向之上的连续旋转角（度）。</summary>
		public readonly float RotationDegrees;

		public readonly float ScaleX;

		public readonly float ScaleY;

		/// <summary>建造图层（1..16）；不同层使用独立占位表。</summary>
		public readonly int Layer;

		public readonly int FrameShape;

		/// <summary>
		/// 落位后是否按邻居自动对齐朝向（BPSS <c>Contraption.AutoAlign</c>）。
		/// </summary>
		/// <remarks>
		/// 只有玩家手动放置的路径会置位；读档/蓝图（<see cref="Schematics.Unit" /> 构造）保持 false，
		/// 否则重新载入的载具会被悄悄改掉朝向。
		/// </remarks>
		public readonly bool AutoAlign;

		public PartCreationRequest(Entity entity, int sceneID, PartTypeInfo typeInfo, int coordX, int coordY, int rotation, bool flipped)
			: this(entity, sceneID, typeInfo, coordX, coordY, rotation, flipped, 0f, 0f, 0f, 1f, 1f, PartBuildLayer.MinLayer, false)
		{
		}

		public PartCreationRequest(Entity entity, int sceneID, PartTypeInfo typeInfo, int coordX, int coordY, int rotation, bool flipped, float offsetX, float offsetY, float rotationDegrees, float scaleX, float scaleY)
			: this(entity, sceneID, typeInfo, coordX, coordY, rotation, flipped, offsetX, offsetY, rotationDegrees, scaleX, scaleY, PartBuildLayer.MinLayer, false)
		{
		}

		public PartCreationRequest(Entity entity, int sceneID, PartTypeInfo typeInfo, int coordX, int coordY, int rotation, bool flipped, float offsetX, float offsetY, float rotationDegrees, float scaleX, float scaleY, int layer)
			: this(entity, sceneID, typeInfo, coordX, coordY, rotation, flipped, offsetX, offsetY, rotationDegrees, scaleX, scaleY, layer, false)
		{
		}

		public PartCreationRequest(Entity entity, int sceneID, PartTypeInfo typeInfo, int coordX, int coordY, int rotation, bool flipped, float offsetX, float offsetY, float rotationDegrees, float scaleX, float scaleY, int layer, bool autoAlign, int frameShape = 0)
		{
			Entity = entity;
			SceneID = sceneID;
			TypeInfo = typeInfo;
			CoordX = coordX;
			CoordY = coordY;
			Rotation = rotation;
			Flipped = flipped;
			OffsetX = offsetX;
			OffsetY = offsetY;
			RotationDegrees = rotationDegrees;
			ScaleX = (scaleX > 0f) ? scaleX : 1f;
			ScaleY = (scaleY > 0f) ? scaleY : 1f;
			Layer = PartBuildLayer.Clamp(layer);
			AutoAlign = autoAlign;
			FrameShape = global::Bpsim.Parts.FrameShape.Clamp(frameShape);
		}

		public PartCreationRequest(Entity entity, int sceneID, Schematics.Unit unit)
			: this(entity, sceneID, new PartTypeInfo((PartType)unit.Type, unit.Index), unit.X, unit.Y, unit.Rotation, unit.Flipped, unit.OffsetX, unit.OffsetY, unit.RotationDegrees, unit.ScaleX, unit.ScaleY, unit.Layer, false, unit.FrameShape)
		{
		}

		public PartCreationRequest WithPosition(int x, int y)
		{
			return new PartCreationRequest(Entity, SceneID, TypeInfo, x, y, Rotation, Flipped, OffsetX, OffsetY, RotationDegrees, ScaleX, ScaleY, Layer, AutoAlign, FrameShape);
		}

		public PartCreationRequest WithRotation(int rotation, bool flipped)
		{
			return new PartCreationRequest(Entity, SceneID, TypeInfo, CoordX, CoordY, rotation, flipped, OffsetX, OffsetY, RotationDegrees, ScaleX, ScaleY, Layer, AutoAlign, FrameShape);
		}

		public PartCreationRequest WithFreeTransform(in PartFreeTransform freeTransform)
		{
			PartFreeTransform value = PartFreeTransform.Sanitize(freeTransform);
			return new PartCreationRequest(Entity, SceneID, TypeInfo, CoordX, CoordY, Rotation, Flipped, value.Offset.x, value.Offset.y, value.RotationDegrees, value.Scale.x, value.Scale.y, Layer, AutoAlign, FrameShape);
		}

		public PartCreationRequest WithLayer(int layer)
		{
			return new PartCreationRequest(Entity, SceneID, TypeInfo, CoordX, CoordY, Rotation, Flipped, OffsetX, OffsetY, RotationDegrees, ScaleX, ScaleY, layer, AutoAlign, FrameShape);
		}

		/// <summary>请求落位后自动对齐（仅玩家手动放置路径使用）。</summary>
		public PartCreationRequest WithAutoAlign(bool autoAlign)
		{
			return new PartCreationRequest(Entity, SceneID, TypeInfo, CoordX, CoordY, Rotation, Flipped, OffsetX, OffsetY, RotationDegrees, ScaleX, ScaleY, Layer, autoAlign, FrameShape);
		}

		public Entity Submit(in PartSceneUnmanaged partScene)
		{
			EntityManager entityManager = partScene.EntityManager;
			Entity entity = entityManager.Instantiate(Entity);
			entityManager.AddComponent(entity, ComponentType.ReadOnly<PartSceneID>());
			entityManager.SetComponentData(entity, new PartTransform
			{
				X = CoordX,
				Y = CoordY,
				Rotation = Rotation,
				Flipped = Flipped
			});
			entityManager.SetComponentData(entity, new PartSceneID
			{
				Value = SceneID
			});
			PartBuildLayerUtility.SetLayer(entityManager, entity, Layer);
			PartFreeTransform freeTransform = PartFreeTransform.Sanitize(new PartFreeTransform
			{
				Offset = new float2(OffsetX, OffsetY),
				RotationDegrees = RotationDegrees,
				Scale = new float2(ScaleX, ScaleY)
			});
			if (!entityManager.HasComponent<PartFreeTransform>(entity))
			{
				entityManager.AddComponent<PartFreeTransform>(entity);
			}
			entityManager.SetComponentData(entity, freeTransform);
			if (entityManager.HasComponent<FrameShape>(entity))
			{
				entityManager.SetComponentData(entity, new FrameShape { Value = FrameShape });
				entityManager.SetComponentEnabled<FrameShapeDirty>(entity, true);
			}
			if (!entityManager.HasComponent<PostTransformScale>(entity))
			{
				entityManager.AddComponent<PostTransformScale>(entity);
			}
			entityManager.SetComponentData(entity, new PostTransformScale
			{
				Value = float3x3.Scale(freeTransform.Scale.x, freeTransform.Scale.y, 1f)
			});
			// 子渲染实体要继承根的非等比缩放，必须让根走 LocalToWorld 矩阵路径。
			// 模板实体已经在 PartEntityFactory.Create 里带上这个标签，这里只是兜底
			// （例如从别处直接 Instantiate 出来的 prefab）。
			if (!entityManager.HasComponent<PropagateLocalToWorld>(entity))
			{
				entityManager.AddComponent<PropagateLocalToWorld>(entity);
			}
			return entity;
		}
	}
}
