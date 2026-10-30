using System;
using System.Collections.Generic;
using Unity.Assertions;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics.GraphicsIntegration;
using Unity.Transforms;
using UnityEngine;
using LegacyRigidBody = UnityEngine.Rigidbody;
using LegacyCollider = UnityEngine.Collider;
using LegacyBox = UnityEngine.BoxCollider;
using LegacyCapsule = UnityEngine.CapsuleCollider;
using LegacyMesh = UnityEngine.MeshCollider;
using LegacySphere = UnityEngine.SphereCollider;

namespace Unity.Physics.Authoring
{
    [RequireMatchingQueriesForUpdate]
    [UpdateAfter(typeof(PhysicsBodyBakingSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    public partial class PostProcessPhysicsTransformBakingSystem : SystemBase
    {
        internal void PostProcessTransformComponents(Entity entity, PhysicsPostProcessData physicsPostProcessData)
        {
            var rigidBodyTransform = Math.DecomposeRigidBodyTransform(physicsPostProcessData.LocalToWorldMatrix);

            var manager = EntityManager;

#if !ENABLE_TRANSFORM_V1
            if (math.lengthsq((float3)physicsPostProcessData.LossyScale - new float3(1f)) > 0f)
            {
                // Any non-identity scale at authoring time is baked into the physics collision shape/mass data.
                // In this case, the LocalTransform scale field should be set to 1.0 to avoid double-scaling
                // within the physics simulation. We bake the scale into the PostTransformScale to make sure the object
                // is rendered correctly.
                // TODO(DOTS-7098): should potentially add a tag component here to indicate that scale is baked in?
                var compositeScale = math.mul(
                    math.inverse(new float4x4(rigidBodyTransform)),
                    physicsPostProcessData.LocalToWorldMatrix
                );
                manager.SetComponentData(entity, new PostTransformScale { Value = new float3x3(compositeScale) });
            }
            var uniformScale = 1.0f;
            manager.SetComponentData(entity,
                LocalTransform.FromPositionRotationScale(rigidBodyTransform.pos, rigidBodyTransform.rot, uniformScale));
#else
            manager.SetComponentData(entity, new Translation { Value = rigidBodyTransform.pos });
            manager.SetComponentData(entity, new Rotation { Value = rigidBodyTransform.rot });

            if (math.lengthsq((float3)physicsPostProcessData.LossyScale - new float3(1f)) > 0f)
            {
                // bake in composite scale
                var compositeScale = math.mul(
                    math.inverse(new float4x4(rigidBodyTransform)),
                    physicsPostProcessData.LocalToWorldMatrix
                );
                manager.SetComponentData(entity, new CompositeScale { Value = compositeScale });
            }
#endif
        }

        protected override void OnUpdate()
        {
            foreach (var (postProcessData, entity) in SystemAPI.Query<RefRO<PhysicsPostProcessData>>()
                         .WithEntityAccess()
                         .WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities))
            {
                PostProcessTransformComponents(entity, postProcessData.ValueRO);
            }
        }
    }
}
