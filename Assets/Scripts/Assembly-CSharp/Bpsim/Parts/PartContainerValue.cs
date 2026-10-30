using Unity.Entities;

namespace Bpsim.Parts
{
	public struct PartContainerValue : IComponentData, IQueryTypeParameter
	{
		public Entity Value;
	}
}
