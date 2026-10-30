using Unity.Entities;

namespace Bpsim.Rendering
{
	public struct SpriteID : IComponentData, IQueryTypeParameter
	{
		public int Value;
	}
}
