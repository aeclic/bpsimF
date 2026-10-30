using Unity.Entities;
using Unity.Mathematics;

namespace Bpsim.Parts
{
	public struct PartRenderInfo : IComponentData, IQueryTypeParameter
	{
		public int Index;

		public int Priority;

		public AABB Bounds;

		public PartRenderInfo WithIndex(int index)
		{
			return new PartRenderInfo
			{
				Index = index,
				Priority = Priority,
				Bounds = Bounds
			};
		}
	}
}
