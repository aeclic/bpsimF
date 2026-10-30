using Bpsim.Collections;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Physics;

namespace Bpsim.Parts.Simulation
{
	public static class PartSimulationJobs
	{
		[BurstCompile]
		public struct UpdateComponentsJob : IJob
		{
			public Reference<PartSimulatorUnmanaged> Simulator;

			[ReadOnly]
			public ComponentLookup<PhysicsConstrainedBodyPair> ConstrainedBodyPairLookup;

			public ComponentLookup<PartConnectedComponent> PartConnectedComponentLookup;

			[ReadOnly]
			public ComponentLookup<PartExtensionComponent> PartExtensionComponentLookup;

			public void Execute()
			{
				using NativeDisjointSet disjointSet = PartSimulation.FindConnectedComponents(Simulator, ConstrainedBodyPairLookup);
				PartSimulation.UpdateConnectedComponents(Simulator, PartConnectedComponentLookup, PartExtensionComponentLookup, disjointSet);
			}
		}

		public static UpdateComponentsJob UpdateComponents(Reference<PartSimulatorUnmanaged> simulator)
		{
			return new UpdateComponentsJob
			{
				Simulator = simulator,
				ConstrainedBodyPairLookup = PartManager.Instance.System.GetComponentLookup<PhysicsConstrainedBodyPair>(isReadOnly: true),
				PartConnectedComponentLookup = PartManager.Instance.System.GetComponentLookup<PartConnectedComponent>(),
				PartExtensionComponentLookup = PartManager.Instance.System.GetComponentLookup<PartExtensionComponent>(isReadOnly: true)
			};
		}
	}
}
