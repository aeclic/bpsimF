using Unity.Entities;

namespace Bpsim.Parts.Simulation
{
	public struct PartJointInfo : IComponentData, IQueryTypeParameter
	{
		public PartJointType Type;

		public int State;
	}
}
