using Unity.Entities;

namespace Bpsim.Parts
{
	public struct PartExtensionComponent : IComponentData, IQueryTypeParameter
	{
		public PartExtensionData Value;
	}
}
