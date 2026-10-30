using System;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Physics.Authoring;
using Unity.Transforms;
using UnityEngine;
#if LEGACY_PHYSICS
using LegacyBox = UnityEngine.BoxCollider;
using LegacyRigidBody = UnityEngine.Rigidbody;
#endif

namespace Unity.Physics.Tests.Authoring
{
    class BodyAndShapeConversionSystems_TransformSystems_IntegrationTests : BaseHierarchyConversionTest
    {
        static void ConvertHierarchyAndUpdateTransformSystemsVerifyEntityExists(GameObject gameObjectHierarchyRoot, EntityQueryDesc query)
        {
            ConvertHierarchyAndUpdateTransformSystems<LocalToWorld>(gameObjectHierarchyRoot, query, false);
        }

        static T ConvertHierarchyAndUpdateTransformSystems<T>(GameObject gameObjectHierarchyRoot)
            where T : unmanaged, IComponentData
        {
            // query with read/write to trigger update of transform system
            var query = new EntityQueryDesc { All = new[] { typeof(PhysicsCollider), ComponentType.ReadWrite<T>() } };
            return ConvertHierarchyAndUpdateTransformSystems<T>(gameObjectHierarchyRoot, query, true);
        }

        static unsafe T ConvertHierarchyAndUpdateTransformSystems<T>(
            GameObject gameObjectHierarchyRoot, EntityQueryDesc query, bool returnData
        )
            where T : unmanaged, IComponentData
        {
            if (
                returnData // i.e. value of post-conversion data will be asserted
                && !query.All.Contains(ComponentType.ReadWrite<T>())
                && !query.Any.Contains(ComponentType.ReadWrite<T>())
            )
                Assert.Fail($"{nameof(query)} must contain {ComponentType.ReadWrite<T>()} in order to trigger update of transform system");

            var queryStr = query.ToReadableString();

            using (var world = new World("Test world"))
            using (var blobAssetStore = new BlobAssetStore(128))
            {
                // convert GameObject hierarchy
                ConvertBakeGameObject(gameObjectHierarchyRoot, world, blobAssetStore);

                // trigger update of transform systems
                using (var group = world.EntityManager.CreateEntityQuery(query))
                {
                    using (var entities = group.ToEntityArray(Allocator.TempJob))
                    {
                        Assert.That(
                            entities.Length, Is.EqualTo(1),
                            $"Conversion systems produced unexpected number of physics entities {queryStr}"
                        );
                    }

#if !ENABLE_TRANSFORM_V1
                    var localToWorldSystem = world.GetOrCreateSystem<LocalToWorldSystem>();
                    ref var localToWorldSystemState = ref world.Unmanaged.ResolveSystemStateRef(localToWorldSystem);
                    var lastVersion = localToWorldSystemState.GlobalSystemVersion;

                    world.GetOrCreateSystem<ParentSystem>().Update(world.Unmanaged);
                    localToWorldSystem.Update(world.Unmanaged);
#else
                    var localToWorldSystem = world.GetOrCreateSystem<TRSToLocalToWorldSystem>();
                    ref var localToWorldSystemState = ref world.Unmanaged.ResolveSystemStateRef(localToWorldSystem);
                    var lastVersion = localToWorldSystemState.GlobalSystemVersion;

                    world.GetOrCreateSystem<ParentSystem>().Update(world.Unmanaged);
                    world.GetOrCreateSystem<CompositeRotationSystem>().Update(world.Unmanaged);
                    world.GetOrCreateSystem<CompositeScaleSystem>().Update(world.Unmanaged);
                    world.GetOrCreateSystem<ParentScaleInverseSystem>().Update(world.Unmanaged);
                    world.GetOrCreateSystem<TRSToLocalToWorldSystem>().Update(world.Unmanaged);
                    world.GetOrCreateSystem<TRSToLocalToParentSystem>().Update(world.Unmanaged);
                    world.GetOrCreateSystem<LocalToParentSystem>().Update(world.Unmanaged);
                    world.GetOrCreateSystem<WorldToLocalSystem>().Update(world.Unmanaged);
#endif
                    world.EntityManager.CompleteAllTrackedJobs();

                    using (var entities = group.ToEntityArray(Allocator.TempJob))
                    {
                        Assume.That(
                            entities.Length, Is.EqualTo(1),
                            $"Updating transform systems resulted in unexpected number of physics entities {queryStr}"
                        );
                    }

                    if (!returnData)
                        return default;

                    using (var chunks = group.ToArchetypeChunkArray(Allocator.TempJob))
                    {
                        var localToWorldTypeHandle = localToWorldSystemState.GetComponentTypeHandle<LocalToWorld>();
                        // assume transform systems ran if LocalToWorld chunk version has increased
                        var changed = chunks[0].DidChange(
                            ref localToWorldTypeHandle, lastVersion
                        );
                        Assume.That(
                            changed, Is.True,
                            $"Transform systems did not run. Is {typeof(T)} an input for any transform system?"
                        );
                    }

                    using (var data = group.ToComponentDataArray<T>(Allocator.TempJob))
                        return data[0];
                }
            }
        }

        static readonly TestCaseData[] k_ExplicitPhysicsBodyHierarchyTestCases =
        {
            new TestCaseData(
                BodyMotionType.Static,
                new EntityQueryDesc { All = new ComponentType[] { typeof(PhysicsCollider), typeof(Parent) } }
            ).SetName("Static has parent"),
            new TestCaseData(
                BodyMotionType.Dynamic,
#if !ENABLE_TRANSFORM_V1
                new EntityQueryDesc { All = new ComponentType[] { typeof(PhysicsCollider) }, None = new ComponentType[] { typeof(Parent), typeof(PreviousParent) } }
#else
                new EntityQueryDesc { All = new ComponentType[] { typeof(PhysicsCollider) }, None = new ComponentType[] { typeof(Parent), typeof(PreviousParent), typeof(LocalToParent) } }
#endif
            ).SetName("Dynamic is unparented"),
            new TestCaseData(
                BodyMotionType.Kinematic,
#if !ENABLE_TRANSFORM_V1
                new EntityQueryDesc { All = new ComponentType[] { typeof(PhysicsCollider) }, None = new ComponentType[] { typeof(Parent), typeof(PreviousParent) } }
#else
                new EntityQueryDesc { All = new ComponentType[] { typeof(PhysicsCollider) }, None = new ComponentType[] { typeof(Parent), typeof(PreviousParent), typeof(LocalToParent) } }
#endif
            ).SetName("Kinematic is unparented"),
        };

        [TestCaseSource(nameof(k_ExplicitPhysicsBodyHierarchyTestCases))]
        public void ConversionSystems_WhenChildGOHasExplicitPhysicsBody_EntityIsInExpectedHierarchyLocation(
            BodyMotionType motionType, EntityQueryDesc expectedQuery
        )
        {
            CreateHierarchy(
                Array.Empty<Type>(),
                Array.Empty<Type>(),
                new[] { typeof(PhysicsBodyAuthoring), typeof(PhysicsShapeAuthoring) }
            );
            Child.GetComponent<PhysicsBodyAuthoring>().MotionType = motionType;

            ConvertHierarchyAndUpdateTransformSystemsVerifyEntityExists(Root, expectedQuery);
        }

#if LEGACY_PHYSICS
        static readonly TestCaseData[] k_ExplicitLegacyRigidBodyHierarchyTestCases =
        {
            // no means to produce hierarchy of explicit static bodies with legacy
            k_ExplicitPhysicsBodyHierarchyTestCases[1],
            k_ExplicitPhysicsBodyHierarchyTestCases[2]
        };

        [TestCaseSource(nameof(k_ExplicitLegacyRigidBodyHierarchyTestCases))]
        public void ConversionSystems_WhenChildGOHasExplicitLegacyRigidBody_EntityIsInExpectedHierarchyLocation(
            BodyMotionType motionType, EntityQueryDesc expectedQuery
        )
        {
            CreateHierarchy(
                Array.Empty<Type>(),
                Array.Empty<Type>(),
                new[] { typeof(LegacyRigidBody), typeof(LegacyBox) }
            );
            Child.GetComponent<LegacyRigidBody>().isKinematic = motionType != BodyMotionType.Dynamic;
            Child.gameObject.isStatic = motionType == BodyMotionType.Static;

            ConvertHierarchyAndUpdateTransformSystemsVerifyEntityExists(Root, expectedQuery);
        }

#endif

        [Test]
        public void ConversionSystems_WhenChildGOHasImplicitStaticBody_EntityIsInHierarchy(
            [Values(
#if LEGACY_PHYSICS
                typeof(LegacyBox),
#endif
                typeof(PhysicsShapeAuthoring)
             )]
            Type colliderType
        )
        {
            CreateHierarchy(
                Array.Empty<Type>(),
                Array.Empty<Type>(),
                new[] { colliderType }
            );

            var query = new EntityQueryDesc
            {
                All = new ComponentType[] { typeof(PhysicsCollider), typeof(Parent) }
            };
            ConvertHierarchyAndUpdateTransformSystemsVerifyEntityExists(Root, query);
        }

        void TransformHierarchyNodes()
        {
            Root.transform.localPosition = new Vector3(1f, 2f, 3f);
            Root.transform.localRotation = Quaternion.Euler(30f, 60f, 90f);
            Root.transform.localScale = new Vector3(3f, 5f, 7f);
            Parent.transform.localPosition = new Vector3(2f, 4f, 8f);
            Parent.transform.localRotation = Quaternion.Euler(10f, 20f, 30f);
            Parent.transform.localScale = new Vector3(2f, 4f, 8f);
            Child.transform.localPosition = new Vector3(3f, 6f, 9f);
            Child.transform.localRotation = Quaternion.Euler(15f, 30f, 45f);
            Child.transform.localScale = new Vector3(-1f, 2f, -4f);
        }

        [Test]
        public void ConversionSystems_WhenGOHasPhysicsComponents_EntityHasSameLocalToWorldAsGO(
            [Values(
#if LEGACY_PHYSICS
                typeof(LegacyRigidBody),
#endif
                typeof(PhysicsBodyAuthoring), null
             )]
            Type bodyType,
            [Values(
#if LEGACY_PHYSICS
                typeof(LegacyBox),
#endif
                typeof(PhysicsShapeAuthoring)
             )]
            Type colliderType,
            [Values(typeof(StaticOptimizeEntity), null)] Type otherType
        )
        {
            CreateHierarchy(
                Array.Empty<Type>(),
                Array.Empty<Type>(),
                new[] { bodyType, colliderType, otherType }.Where(t => t != null).ToArray()
            );
            TransformHierarchyNodes();

            var localToWorld = ConvertHierarchyAndUpdateTransformSystems<LocalToWorld>(Root);

            Assert.That(localToWorld.Value, Is.PrettyCloseTo(Child.transform.localToWorldMatrix));
        }

        [Test]
        public void ConversionSystems_WhenGOIsImplicitStaticBody_EntityHasSameLocalToWorldAsGO(
            [Values(
#if LEGACY_PHYSICS
                typeof(LegacyBox),
#endif
                typeof(PhysicsShapeAuthoring)
             )]
            Type colliderType
        )
        {
            CreateHierarchy(
                Array.Empty<Type>(),
                Array.Empty<Type>(),
                new[] { colliderType }
            );
            TransformHierarchyNodes();

            var localToWorld = ConvertHierarchyAndUpdateTransformSystems<LocalToWorld>(Root);

            Assert.That(localToWorld.Value, Is.PrettyCloseTo(Child.transform.localToWorldMatrix));
        }

        [Test]
        public void ConversionSystems_WhenGOHasNonStaticBody_EntityHasRotationInWorldSpace(
            [Values(
#if LEGACY_PHYSICS
                typeof(LegacyRigidBody),
#endif
                typeof(PhysicsBodyAuthoring)
             )]
            Type bodyType,
            [Values(BodyMotionType.Dynamic, BodyMotionType.Kinematic)]
            BodyMotionType motionType,
            [Values(
#if LEGACY_PHYSICS
                typeof(LegacyBox),
#endif
                typeof(PhysicsShapeAuthoring)
             )]
            Type colliderType
        )
        {
            CreateHierarchy(
                Array.Empty<Type>(),
                Array.Empty<Type>(),
                new[] { bodyType, colliderType }.Where(t => t != null).ToArray()
            );
            TransformHierarchyNodes();
            SetBodyMotionType(Child.GetComponent(bodyType), motionType);

#if !ENABLE_TRANSFORM_V1
            var rotation = ConvertHierarchyAndUpdateTransformSystems<LocalTransform>(Root).Rotation;

            var expectedRotation = Math.DecomposeRigidBodyOrientation(Child.transform.localToWorldMatrix);
            Assert.That(rotation, Is.EqualTo(expectedRotation));
#else
            var rotation = ConvertHierarchyAndUpdateTransformSystems<Rotation>(Root);

            var expectedRotation = Math.DecomposeRigidBodyOrientation(Child.transform.localToWorldMatrix);
            Assert.That(rotation.Value, Is.EqualTo(expectedRotation));
#endif
        }

        static void SetBodyMotionType(Component component, BodyMotionType motionType)
        {
#if LEGACY_PHYSICS
            var rigidBody = component as LegacyRigidBody;
            if (rigidBody != null)
            {
                rigidBody.isKinematic = motionType == BodyMotionType.Kinematic;
                return;
            }
#endif
            var physicsBody = component as PhysicsBodyAuthoring;
            physicsBody.MotionType = motionType;
        }

        [Test]
        public void ConversionSystems_WhenGOHasPhysicsComponents_EntityHasTranslationInWorldSpace(
            [Values(
#if LEGACY_PHYSICS
                typeof(LegacyRigidBody),
#endif
                typeof(PhysicsBodyAuthoring)
             )]
            Type bodyType,
            [Values(
#if LEGACY_PHYSICS
                typeof(LegacyBox),
#endif
                typeof(PhysicsShapeAuthoring)
             )]
            Type colliderType,
            [Values(typeof(StaticOptimizeEntity), null)]
            Type otherType
        )
        {
            CreateHierarchy(
                Array.Empty<Type>(),
                Array.Empty<Type>(),
                new[] { bodyType, colliderType, otherType }.Where(t => t != null).ToArray()
            );
            TransformHierarchyNodes();

#if !ENABLE_TRANSFORM_V1
            var translation = ConvertHierarchyAndUpdateTransformSystems<LocalTransform>(Root);
            Assert.That(translation.Position, Is.PrettyCloseTo(Child.transform.position));
#else
            var translation = ConvertHierarchyAndUpdateTransformSystems<Translation>(Root);
            Assert.That(translation.Value, Is.PrettyCloseTo(Child.transform.position));
#endif
        }

        [Test, Description("Validate that GameObject.isStatic and StaticOptimizeEntity are baked to equivalent results.")]
        public void ConversionSystems_StaticOptimizeRoot_IsEquivalent_StaticRoot(
            [Values(
#if LEGACY_PHYSICS
                typeof(LegacyBox),
#endif
                typeof(PhysicsShapeAuthoring)
             )]
            Type colliderType
        )
        {
            void TransformHierarchyNodes(GameObject root, GameObject parent, GameObject child)
            {
                root.transform.localPosition = new Vector3(1f, 2f, 3f);
                root.transform.localRotation = Quaternion.Euler(30f, 60f, 90f);
                root.transform.localScale = new Vector3(3f, 5f, 7f);
                parent.transform.localPosition = new Vector3(2f, 4f, 8f);
                parent.transform.localRotation = Quaternion.Euler(10f, 20f, 30f);
                parent.transform.localScale = new Vector3(2f, 4f, 8f);
                child.transform.localPosition = new Vector3(3f, 6f, 9f);
                child.transform.localRotation = Quaternion.Euler(15f, 30f, 45f);
                child.transform.localScale = new Vector3(-1f, 2f, -4f);
            }

            var staticRoot = new GameObject("Root");
            var staticParent = new GameObject("Parent");
            var staticChild = new GameObject("Child", colliderType);
            staticChild.transform.parent = staticParent.transform;
            staticParent.transform.parent = staticRoot.transform;
            staticRoot.isStatic = true;

            var staticOptRoot = new GameObject("Root", typeof(StaticOptimizeEntity));
            var staticOptParent = new GameObject("Parent");
            var staticOptChild = new GameObject("Child", colliderType);
            staticOptChild.transform.parent = staticOptParent.transform;
            staticOptParent.transform.parent = staticOptRoot.transform;

            try
            {
                TransformHierarchyNodes(staticRoot, staticParent, staticChild);
                TransformHierarchyNodes(staticOptRoot, staticOptParent, staticOptChild);

                using (var world = new World("Test world"))
                using (var blobAssetStore = new BlobAssetStore(128))
                {
                    var staticRootEntity = ConvertBakeGameObject(staticRoot, world, blobAssetStore);
                    var staticOptRootEntity = ConvertBakeGameObject(staticRoot, world, blobAssetStore);

                    var query = new EntityQueryBuilder(Allocator.Temp).WithAll<PhysicsCollider>().Build(world.EntityManager);
                    Assert.AreEqual(2, query.CalculateEntityCount(), "Unexpected number of static roots");
                    var roots = query.ToEntityArray(Allocator.Temp).ToArray();
                    Assert.Contains(staticRootEntity, roots, "Root with static flag was not baked");
                    Assert.Contains(staticOptRootEntity, roots, "Root with StaticOptimizeEntity was not baked");
                    Assert.AreEqual(world.EntityManager.GetComponentCount(staticOptRootEntity), world.EntityManager.GetComponentCount(staticRootEntity), "The static roots have different numbers of components");
                }
            }
            finally
            {
                GameObject.DestroyImmediate(staticRoot);
                GameObject.DestroyImmediate(staticParent);
                GameObject.DestroyImmediate(staticChild);
                GameObject.DestroyImmediate(staticOptRoot);
                GameObject.DestroyImmediate(staticOptParent);
                GameObject.DestroyImmediate(staticOptChild);
            }
        }
    }
}
