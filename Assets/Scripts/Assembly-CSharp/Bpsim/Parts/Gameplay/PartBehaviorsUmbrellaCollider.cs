using Bpsim.Parts.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace Bpsim.Parts.Gameplay
{
	public sealed partial class UmbrellaBehavior
	{
		private float m_colliderBlend = -1;

		private void UpdateCanopyCollider(float progress)
		{
			if (!Em.HasComponent<PhysicsCollider>(Part) || Em.HasComponent<MegaPartOffset>(Part)) return;
			float blend = m_open ? 1 - math.pow(progress, 3) : math.pow(progress, 6);
			if (math.abs(blend - m_colliderBlend) < .00001f) return;
			m_colliderBlend = blend;
			float width = math.lerp(.4f, 1, blend), height = math.lerp(2, 1, blend);
			float y = math.lerp(-.05f, 0, m_open ? 1 - progress : progress) - .45f * (height - 1);
			float2 scale = Em.HasComponent<PartFreeTransform>(Part) ? PartFreeTransform.Sanitize(Em.GetComponentData<PartFreeTransform>(Part)).Scale : new float2(1);
			var filter = Em.GetComponentData<PhysicsCollider>(Part).Value.Value.GetCollisionFilter();
			var material = Unity.Physics.Material.Default;
			material.Friction = .6f;
			var shaft = Unity.Physics.BoxCollider.Create(new BoxGeometry {
				Center = new float3(0, -.15f * scale.y, 0), Size = new float3(.2f * scale.x, .7f * scale.y, 1), Orientation = quaternion.identity, BevelRadius = 0 }, filter, material);
			var canopy = Unity.Physics.BoxCollider.Create(new BoxGeometry {
				Center = new float3(0, (y + .25f * height) * scale.y, 0), Size = new float3(width * scale.x, .4f * height * scale.y, 1), Orientation = quaternion.identity, BevelRadius = 0 }, filter, material);
			var children = new NativeArray<CompoundCollider.ColliderBlobInstance>(2, Allocator.Temp);
			BlobAssetReference<Unity.Physics.Collider> compound;
			try
			{
				children[0] = new CompoundCollider.ColliderBlobInstance { Collider = shaft, CompoundFromChild = RigidTransform.identity };
				children[1] = new CompoundCollider.ColliderBlobInstance { Collider = canopy, CompoundFromChild = RigidTransform.identity };
				compound = CompoundCollider.Create(children);
			}
			finally { children.Dispose(); shaft.Dispose(); canopy.Dispose(); }
			Em.SetComponentData(Part, new PhysicsCollider { Value = compound });
			PartGameplayUtil.Simulator.TrackScaledCollider(compound);
			if (Em.HasComponent<PhysicsMass>(Part))
			{
				var old = Em.GetComponentData<PhysicsMass>(Part);
				if (old.InverseMass > 0) Em.SetComponentData(Part, PhysicsMass.CreateDynamic(compound.Value.MassProperties, 1 / old.InverseMass));
			}
			// BPLE caches these at initialization, after the special canopy's local position is reset.
			if (Em.HasComponent<BpleLightBodyBounds>(Part) && progress == 0 && m_open)
			{
				var bounds = Em.GetComponentData<BpleLightBodyBounds>(Part);
				bounds.Bounds = new BpleLightBounds { X = 0, Y = -.025f, A = .5f, B = .475f, R = math.sqrt(.25f + .475f * .475f) };
				Em.SetComponentData(Part, bounds);
			}
		}
	}
}
