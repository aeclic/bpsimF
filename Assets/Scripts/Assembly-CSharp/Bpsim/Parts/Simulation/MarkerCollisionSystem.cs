using Bpsim.Parts.Gameplay;
using Havok.Physics;
using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using Unity.Physics.Systems;

namespace Bpsim.Parts.Simulation
{
	[UpdateInGroup(typeof(PhysicsSimulationGroup))]
	[UpdateAfter(typeof(PhysicsCreateBodyPairsGroup))]
	[UpdateBefore(typeof(PhysicsCreateContactsGroup))]
	internal partial class MarkerCollisionSystem : SystemBase
	{
		private struct FilterPairs : IBodyPairsJob
		{
			[ReadOnly] public ComponentLookup<PartMarkerTeam> Teams;
			public void Execute(ref ModifiableBodyPair pair)
			{
				if (!Teams.HasComponent(pair.EntityA) || !Teams.HasComponent(pair.EntityB)) return;
				var a = Teams[pair.EntityA]; var b = Teams[pair.EntityB];
				if (a.Component != b.Component && (a.Teams & b.Teams) != 0 && (a.Diagonal || b.Diagonal)) pair.Disable();
			}
		}
		protected override void OnUpdate()
		{
			if (PartManager.Instance == null || !PartManager.Instance.IsSimulating) return;
			if (GetEntityQuery(ComponentType.ReadOnly<PartMarkerTeam>()).IsEmptyIgnoreFilter) return;
			var physics = SystemAPI.GetSingletonRW<PhysicsWorldSingleton>();
			Dependency = new FilterPairs { Teams = GetComponentLookup<PartMarkerTeam>(true) }.Schedule(
				SystemAPI.GetSingleton<SimulationSingleton>(), ref physics.ValueRW.PhysicsWorld, Dependency);
		}
	}
}
