using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using Unity.Physics.Systems;

namespace Bpsim.Parts.Simulation
{
	[UpdateInGroup(typeof(PartSimulationSystemGroup))]
	[UpdateBefore(typeof(BalloonSystem))]
	internal partial class BalloonCollisionSystem : SystemBase
	{
		private struct Contacts : ICollisionEventsJob
		{
			[ReadOnly] public ComponentLookup<BalloonBody> Balloons;
			[ReadOnly] public ComponentLookup<BalloonPopSurface> Surfaces;
			[ReadOnly] public ComponentLookup<PartTag> Parts;
			[ReadOnly] public ComponentLookup<PhysicsMass> Masses;
			public NativeList<Entity> Pops;
			public void Execute(CollisionEvent collision)
			{
				Consider(collision.EntityA, collision.EntityB); Consider(collision.EntityB, collision.EntityA);
			}
			private void Consider(Entity balloon, Entity other)
			{
				if (!Balloons.HasComponent(balloon) || Balloons[balloon].CollisionUnpoppable) return;
				if (Surfaces.HasComponent(other) || (!Parts.HasComponent(other) &&
					(!Masses.HasComponent(other) || Masses[other].InverseMass == 0f))) Pops.Add(balloon);
			}
		}
		protected override void OnUpdate()
		{
			if (PartManager.Instance == null || !PartManager.Instance.IsSimulating) return;
			if (Bpsim.Physics.Anvil.AnvilStepSystem.Active)
			{
				if (!SystemAPI.TryGetSingleton(out Bpsim.Physics.Anvil.AnvilContactsSingleton contacts)) return;
				for (int i = 0; i < contacts.Count; i++)
				{
					var e = contacts.Events[i]; ConsiderAnvil(e.EntityA, e.EntityB); ConsiderAnvil(e.EntityB, e.EntityA);
				}
				return;
			}
			if (!SystemAPI.TryGetSingleton(out SimulationSingleton simulation)) return;
			using var pops = new NativeList<Entity>(Allocator.TempJob);
			var job = new Contacts { Pops = pops, Balloons = GetComponentLookup<BalloonBody>(true),
				Surfaces = GetComponentLookup<BalloonPopSurface>(true), Parts = GetComponentLookup<PartTag>(true),
				Masses = GetComponentLookup<PhysicsMass>(true) };
			Dependency = job.Schedule(simulation, Dependency); Dependency.Complete();
			foreach (Entity part in pops) Request(part);
		}
		private void ConsiderAnvil(Entity balloon, Entity other)
		{
			if (!EntityManager.HasComponent<BalloonBody>(balloon) || EntityManager.GetComponentData<BalloonBody>(balloon).CollisionUnpoppable) return;
			if (EntityManager.HasComponent<BalloonPopSurface>(other) || (!EntityManager.HasComponent<PartTag>(other) &&
				(!EntityManager.HasComponent<PhysicsMass>(other) || EntityManager.GetComponentData<PhysicsMass>(other).InverseMass == 0f))) Request(balloon);
		}
		private void Request(Entity part)
		{
			if (!EntityManager.Exists(part) || !EntityManager.HasComponent<BalloonBody>(part)) return;
			var body = EntityManager.GetComponentData<BalloonBody>(part); body.PopRequest = true;
			EntityManager.SetComponentData(part, body);
		}
	}
}
