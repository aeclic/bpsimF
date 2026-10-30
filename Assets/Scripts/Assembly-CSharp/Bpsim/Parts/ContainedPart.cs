using Unity.Entities;

namespace Bpsim.Parts
{
	public struct ContainedPart : IComponentData, IQueryTypeParameter
	{
		public Entity Value;
	}
}
