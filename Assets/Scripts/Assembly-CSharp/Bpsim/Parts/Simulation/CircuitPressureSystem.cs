using Bpsim.Parts.Gameplay;
using Bpsim.Physics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;

namespace Bpsim.Parts.Simulation
{
    internal struct CircuitPressureTag : IComponentData { }

    internal static class CircuitPressureSetup
    {
        internal static unsafe void EnableEvents(Entity part)
        {
            EnableEvents(part,PartGameplayUtil.Em,PartGameplayUtil.Simulator);
        }
        internal static unsafe void EnableEvents(Entity part,EntityManager em,PartSimulatorUnmanaged simulator)
        {
            if(em.HasComponent<CircuitPressureTag>(part)||!em.HasComponent<PhysicsCollider>(part)) return;
            var original=em.GetComponentData<PhysicsCollider>(part);
            if(!original.Value.IsCreated) return;
            // Each sensor owns its response policy; never mutate a shared prefab collider.
            var clone=PartPhysics.CreateScaled(original.Value,new float2(1));
            if(!clone.IsCreated) return;
            clone.Value.SetCollisionFilter(original.Value.Value.GetCollisionFilter());
            ((Collider*)clone.GetUnsafePtr())->SetCollisionResponse(CollisionResponsePolicy.CollideRaiseCollisionEvents);
            em.SetComponentData(part,new PhysicsCollider { Value=clone });
            simulator.TrackScaledCollider(clone);
            if(!em.HasComponent<PartIsolatedCollider>(part)) em.AddComponent<PartIsolatedCollider>(part);
            em.AddComponent<CircuitPressureTag>(part);
        }
    }

    [UpdateInGroup(typeof(PartSimulationSystemGroup))]
    [UpdateBefore(typeof(CircuitSystem))]
    internal partial class CircuitPressureSystem : SystemBase
    {
        private struct Hit { public Entity Sensor; public float Pressure; }
        private struct Contacts : ICollisionEventsJob
        {
            [ReadOnly] public ComponentLookup<CircuitPressureTag> Sensors;
            [ReadOnly] public ComponentLookup<LocalToWorld> Transforms;
            [ReadOnly] public ComponentLookup<LocalTransform> PhysicsTransforms;
            [ReadOnly] public ComponentLookup<Parent> Parents;
            [ReadOnly] public NativeArray<Entity> MegaChildren;
            [ReadOnly] public PhysicsWorld World;
            public Entity MegaBody;
            public int MegaKeyBits;
            public float InverseDelta;
            public NativeList<Hit> Hits;
            public void Execute(CollisionEvent e)
            {
                Entity a=Resolve(e.EntityA,e.ColliderKeyA),b=Resolve(e.EntityB,e.ColliderKeyB);
                if(!Sensors.HasComponent(a)&&!Sensors.HasComponent(b)) return;
                var details=e.CalculateDetails(ref World);
                if(Sensors.HasComponent(a)) Add(a,e.Normal,details);
                if(Sensors.HasComponent(b)) Add(b,e.Normal,details);
            }
            private void Add(Entity entity,float3 normal,CollisionEvent.Details details)
            {
                if(!Transforms.HasComponent(entity)) return;
                var transform=Transforms[entity];float3 position=transform.Position;quaternion rotation=transform.Rotation;
                if(PhysicsTransforms.HasComponent(entity)&&!Parents.HasComponent(entity))
                {var physicsTransform=PhysicsTransforms[entity];position=physicsTransform.Position;rotation=physicsTransform.Rotation;}
                float3 up=math.mul(rotation,new float3(0,1,0));
                for(int i=0;i<details.EstimatedContactPointPositions.Length;i++)
                    if(math.dot(details.EstimatedContactPointPositions[i]-position,up)>.4f)
                    {
                        Hits.Add(new Hit { Sensor=entity,Pressure=math.abs(math.dot(normal,up))*details.EstimatedImpulse*InverseDelta });
                        break;
                    }
            }
            private Entity Resolve(Entity body,ColliderKey key)
            {
                if(body!=MegaBody||MegaKeyBits<=0||MegaKeyBits>=32) return body;
                uint i=key.Value>>(32-MegaKeyBits);return i<(uint)MegaChildren.Length?MegaChildren[(int)i]:body;
            }
        }
        private EntityQuery sensors;
        private NativeList<Hit> hits;
        private NativeArray<Entity> empty;
        protected override void OnCreate()
        {
            sensors=GetEntityQuery(ComponentType.ReadOnly<CircuitPressureTag>(),ComponentType.ReadWrite<CircuitPart>());
            hits=new NativeList<Hit>(32,Allocator.Persistent);empty=new NativeArray<Entity>(0,Allocator.Persistent);
        }
        protected override void OnDestroy() { hits.Dispose();empty.Dispose(); }
        protected override void OnUpdate()
        {
            var manager=PartManager.Instance;
            if(manager==null||!manager.IsSimulating||sensors.IsEmptyIgnoreFilter||World.Time.DeltaTime<=0) return;
            using(var entities=sensors.ToEntityArray(Allocator.Temp))
                for(int i=0;i<entities.Length;i++)
                {var data=EntityManager.GetComponentData<CircuitPart>(entities[i]);data.Pressure=0;EntityManager.SetComponentData(entities[i],data);}
            if(!SystemAPI.TryGetSingleton(out PhysicsWorldSingleton physics))return;
            hits.Clear();

            // Anvil（box3d）后端：Unity.Physics / Havok 的事件通道会静默跳过，
            // 直接读 AnvilContactEventSystem 产出的接触表。
            if(Bpsim.Physics.Anvil.AnvilStepSystem.Active)
            {
                if(!SystemAPI.TryGetSingleton(out Bpsim.Physics.Anvil.AnvilContactsSingleton anvilContacts)) return;
                var sensorLookup=GetComponentLookup<CircuitPressureTag>(true);
                var ltLookup=GetComponentLookup<LocalTransform>(true);
                var parentLookup=GetComponentLookup<Parent>(true);
                float inverseDelta=1/math.max(1e-5f,World.Time.DeltaTime);
                for(int i=0;i<anvilContacts.Count;i++)
                {
                    var e=anvilContacts.Events[i];
                    bool sa=sensorLookup.HasComponent(e.EntityA),sb=sensorLookup.HasComponent(e.EntityB);
                    if(!sa&&!sb) continue;
                    Entity sensor=sa?e.EntityA:e.EntityB;
                    if(!ltLookup.HasComponent(sensor)||parentLookup.HasComponent(sensor)) continue;
                    var t=ltLookup[sensor];
                    float3 up=math.mul(t.Rotation,new float3(0,1,0));
                    if(math.dot(e.Position-t.Position,up)>.4f)
                        hits.Add(new Hit{Sensor=sensor,Pressure=math.abs(math.dot(e.Normal,up))*e.Impulse*inverseDelta});
                }
                for(int i=0;i<hits.Length;i++) if(EntityManager.Exists(hits[i].Sensor))
                {
                    var data=EntityManager.GetComponentData<CircuitPart>(hits[i].Sensor);data.Pressure+=hits[i].Pressure;
                    EntityManager.SetComponentData(hits[i].Sensor,data);
                }
                return;
            }

            if(!SystemAPI.TryGetSingleton(out SimulationSingleton simulation))return;
            var job=new Contacts {
                Sensors=GetComponentLookup<CircuitPressureTag>(true),Transforms=GetComponentLookup<LocalToWorld>(true),World=physics.PhysicsWorld,
                PhysicsTransforms=GetComponentLookup<LocalTransform>(true),Parents=GetComponentLookup<Parent>(true),
                MegaBody=MegaStructure.Active?MegaStructure.Body:Entity.Null,
                MegaChildren=MegaStructure.Active?MegaStructure.Children.AsArray():empty,
                MegaKeyBits=MegaStructure.Active?MegaStructure.ChildKeyBits:0,
                InverseDelta=1/World.Time.DeltaTime,Hits=hits
            };
            Dependency=job.Schedule(simulation,Dependency);Dependency.Complete();
            for(int i=0;i<hits.Length;i++) if(EntityManager.Exists(hits[i].Sensor))
            {
                var data=EntityManager.GetComponentData<CircuitPart>(hits[i].Sensor);data.Pressure+=hits[i].Pressure;
                EntityManager.SetComponentData(hits[i].Sensor,data);
            }
        }
    }
}
