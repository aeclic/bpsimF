using Bpsim.Parts.Gameplay;
using Bpsim.Physics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;

namespace Bpsim.Parts.Simulation
{
	internal struct TNTCollisionTag : IComponentData { }
	internal struct TNTNoCollisionTag : IComponentData { }

	internal static class TNTCollisionSetup
	{
		internal static int ActiveCount;
		internal static void StopEvents(Entity part)
		{
			var em = PartGameplayUtil.Em;
			if (!em.HasComponent<TNTCollisionTag>(part)) return;
			em.RemoveComponent<TNTCollisionTag>(part);
			ActiveCount = math.max(0, ActiveCount - 1);
		}
		internal static unsafe void EnableEvents(Entity part)
		{
			EntityManager em = PartGameplayUtil.Em;
			if (!em.HasComponent<PhysicsCollider>(part)) return;
			var original = em.GetComponentData<PhysicsCollider>(part);
			if (!original.Value.IsCreated) return;
			// Never edit a prefab/shared collider: response policy is part of the blob.
			var clone = PartPhysics.CreateScaled(original.Value, new float2(1f));
			if (!clone.IsCreated) return;
			clone.Value.SetCollisionFilter(original.Value.Value.GetCollisionFilter());
			((Collider*)clone.GetUnsafePtr())->SetCollisionResponse(CollisionResponsePolicy.CollideRaiseCollisionEvents);
			em.SetComponentData(part, new PhysicsCollider { Value = clone });
			PartGameplayUtil.Simulator.TrackScaledCollider(clone);
			if (!em.HasComponent<PartIsolatedCollider>(part)) em.AddComponent<PartIsolatedCollider>(part);
			em.AddComponent<TNTCollisionTag>(part);
			ActiveCount++;
		}
		internal static void DisableCollision(Entity part)
		{
			var em = PartGameplayUtil.Em;
			if (!em.HasComponent<PhysicsCollider>(part)) return;
			if (!em.HasComponent<TNTNoCollisionTag>(part)) em.AddComponent<TNTNoCollisionTag>(part);
			// Removing PhysicsWorldIndex also survives later connected-component filter refreshes.
			if (em.HasComponent<PhysicsWorldIndex>(part)) em.RemoveComponent<PhysicsWorldIndex>(part);
		}
	}

	/// <summary>One velocity snapshot per physics step, shared by every TNT contact; solver output is too late.</summary>
	[UpdateInGroup(typeof(BeforePhysicsSystemGroup))]
	internal partial class TNTVelocitySystem : SystemBase
	{
		internal NativeParallelHashMap<Entity, float3> Velocities;
		internal NativeParallelHashSet<Entity> Charges;
		internal NativeList<Entity> MegaChildren;
		internal Entity MegaBody;
		internal int MegaKeyBits;
		protected override void OnCreate()
		{
			Velocities = new NativeParallelHashMap<Entity, float3>(256, Allocator.Persistent);
			Charges = new NativeParallelHashSet<Entity>(32, Allocator.Persistent);
			MegaChildren = new NativeList<Entity>(Allocator.Persistent);
		}
		protected override void OnUpdate()
		{
			TNTImpulse.Flush();
			Velocities.Clear(); Charges.Clear();
			MegaChildren.Clear(); MegaBody = Entity.Null; MegaKeyBits = 0;
			if (PartManager.Instance == null || !PartManager.Instance.IsSimulating || TNTCollisionSetup.ActiveCount == 0) return;
			EntityManager.CompleteAllTrackedJobs();
			using (var query = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TNTCollisionTag>()))
			using (var entities = query.ToEntityArray(Allocator.Temp))
			{
				if (Charges.Capacity < entities.Length) Charges.Capacity = math.max(32, entities.Length);
				for (int i = 0; i < entities.Length; i++) Charges.Add(entities[i]);
			}
			if (Charges.Count() == 0) return;
			// Damage may detach a collider before our event consumer runs. Preserve the event's original keys.
			if (MegaStructure.Active)
			{
				MegaBody = MegaStructure.Body;
				MegaKeyBits = MegaStructure.ChildKeyBits;
				MegaChildren.AddRange(MegaStructure.Children.AsArray());
			}
			using (var query = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PhysicsVelocity>()))
			using (var entities = query.ToEntityArray(Allocator.Temp))
			using (var velocities = query.ToComponentDataArray<PhysicsVelocity>(Allocator.Temp))
			{
				if (Velocities.Capacity < entities.Length) Velocities.Capacity = math.max(256, entities.Length);
				for (int i = 0; i < entities.Length; i++) Velocities.TryAdd(entities[i], velocities[i].Linear);
			}
		}
		protected override void OnDestroy() { Velocities.Dispose(); Charges.Dispose(); MegaChildren.Dispose(); }
	}

	[UpdateInGroup(typeof(PartSimulationSystemGroup))]
	internal partial class TNTCollisionSystem : SystemBase
	{
		private struct Hit { public Entity Part; public float Speed; }
		private struct Contacts : ICollisionEventsJob
		{
			[ReadOnly] public NativeParallelHashMap<Entity, float3> Velocities;
			[ReadOnly] public NativeParallelHashSet<Entity> Charges;
			[ReadOnly] public NativeArray<Entity> MegaChildren;
			public Entity MegaBody;
			public int MegaKeyBits;
			public NativeList<Hit> Hits;
			public void Execute(CollisionEvent e)
			{
				Entity a = Resolve(e.EntityA, e.ColliderKeyA), b = Resolve(e.EntityB, e.ColliderKeyB);
				// Mega child velocities are mirrors; collision speed belongs to the actual physics bodies.
				Velocities.TryGetValue(e.EntityA, out float3 va); Velocities.TryGetValue(e.EntityB, out float3 vb);
				float speed = math.length(va - vb);
				if (Charges.Contains(a)) Hits.Add(new Hit { Part = a, Speed = speed });
				if (Charges.Contains(b)) Hits.Add(new Hit { Part = b, Speed = speed });
			}
			private Entity Resolve(Entity body, ColliderKey key)
			{
				if (body != MegaBody || MegaKeyBits <= 0 || MegaKeyBits >= 32) return body;
				uint index = key.Value >> (32 - MegaKeyBits);
				return index < (uint)MegaChildren.Length ? MegaChildren[(int)index] : body;
			}
		}
		protected override void OnUpdate()
		{
			var manager = PartGameplayManager.Instance;
			if (manager == null || !manager.IsRunning || TNTCollisionSetup.ActiveCount == 0) return;
			var snapshots = World.GetExistingSystemManaged<TNTVelocitySystem>();
			if (snapshots == null || snapshots.Charges.Count() == 0 || !SystemAPI.TryGetSingleton(out SimulationSingleton simulation)) return;
			using var hits = new NativeList<Hit>(Allocator.TempJob);
			var job = new Contacts {
				Velocities = snapshots.Velocities, Charges = snapshots.Charges, Hits = hits,
				MegaBody = snapshots.MegaBody,
				MegaChildren = snapshots.MegaChildren.AsArray(),
				MegaKeyBits = snapshots.MegaKeyBits
			};
			Dependency = job.Schedule(simulation, Dependency);
			Dependency.Complete();
			for (int i = 0; i < hits.Length; i++) if (manager.Find(hits[i].Part) is TNTBehavior tnt) tnt.OnImpact(hits[i].Speed);
		}
	}
}
