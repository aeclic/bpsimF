using Unity.Entities;

namespace Bpsim.Parts
{
	public struct PartConnectedComponent : IComponentData, IQueryTypeParameter
	{
		public int Index;
	}
}
