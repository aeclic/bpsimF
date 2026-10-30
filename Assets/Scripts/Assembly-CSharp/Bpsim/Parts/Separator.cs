using Unity.Collections;
using Unity.Entities;

namespace Bpsim.Parts
{
	public struct Separator : IComponentData, IQueryTypeParameter
	{
		public FixedList64Bytes<Entity> Children;
	}
}
