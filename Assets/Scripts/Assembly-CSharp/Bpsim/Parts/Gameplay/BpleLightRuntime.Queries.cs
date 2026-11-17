using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace Bpsim.Parts.Gameplay
{
	/// <summary>Ray-query representation of BPLE's trigger light pillars. CCD supplies the physical response.</summary>
	internal struct BpleLightQuery : IComponentData { public Entity Part; }

	internal sealed partial class BpleLightRuntime
	{
		private void UpdateQueryPillars()
		{
			foreach (Source source in m_sources.Values)
			{
				bool enabled = source.EntityEnabled && source.Shape.Type <= 1;
				if (!enabled)
				{
					if (source.Query != Entity.Null && Em.Exists(source.Query)) Em.SetEnabled(source.Query, false);
					continue;
				}
				if (source.Query == Entity.Null || !Em.Exists(source.Query))
				{
					source.Query = Em.CreateEntity(typeof(LocalTransform), typeof(LocalToWorld), typeof(PhysicsCollider), typeof(PhysicsWorldIndex), typeof(BpleLightQuery));
					Em.SetSharedComponent(source.Query, new PhysicsWorldIndex { Value = 0 });
					Em.SetComponentData(source.Query, new BpleLightQuery { Part = source.Part });
				}
				if (source.QueryLength != source.Shape.Length || source.QueryWidth != source.Shape.HalfWidth)
				{
					var material = Unity.Physics.Material.Default;
					material.CollisionResponse = CollisionResponsePolicy.RaiseTriggerEvents;
					var collider = Unity.Physics.BoxCollider.Create(new BoxGeometry {
						Center = new float3(source.Shape.Length * .5f, 0, 0), Size = new float3(source.Shape.Length, source.Shape.HalfWidth * 2, 1),
						Orientation = quaternion.identity, BevelRadius = 0 }, CollisionFilter.Default, material);
					Em.SetComponentData(source.Query, new PhysicsCollider { Value = collider });
					PartGameplayUtil.Simulator.TrackScaledCollider(collider);
					source.QueryLength = source.Shape.Length; source.QueryWidth = source.Shape.HalfWidth;
				}
				var pose = LocalTransform.FromPositionRotation(new float3(source.Position.x, source.Position.y, source.RootPosition.z),
					quaternion.RotateZ(math.atan2(source.Direction.y, source.Direction.x)));
				Em.SetComponentData(source.Query, pose);
				Em.SetComponentData(source.Query, new LocalToWorld { Value = pose.ToMatrix() });
				Em.SetEnabled(source.Query, true);
			}
		}
	}
}
