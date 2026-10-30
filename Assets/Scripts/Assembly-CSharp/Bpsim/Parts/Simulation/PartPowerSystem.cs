using Unity.Entities;
using UnityEngine.Scripting;

namespace Bpsim.Parts.Simulation
{
	[UpdateInGroup(typeof(PartSimulationSystemGroup))]
	internal partial class PartPowerSystem : SystemBase
	{
		[Preserve]
		protected override void OnUpdate()
		{
		}

		[Preserve]
		public PartPowerSystem()
		{
		}
	}
}
