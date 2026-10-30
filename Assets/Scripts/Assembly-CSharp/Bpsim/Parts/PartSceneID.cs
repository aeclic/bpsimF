using Unity.Entities;

namespace Bpsim.Parts
{
	public struct PartSceneID : IComponentData, IQueryTypeParameter
	{
		public int Value;
	}
}
