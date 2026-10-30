using Unity.Entities;

namespace Bpsim.Parts
{
	public struct PartTypeValue : IComponentData, IQueryTypeParameter
	{
		public PartType Type;

		public int Index;

		public PartTypeInfo Value => new PartTypeInfo(Type, Index);
	}
}
