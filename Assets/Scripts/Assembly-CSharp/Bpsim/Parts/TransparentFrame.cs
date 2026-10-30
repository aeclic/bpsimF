using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Bpsim.Parts
{
	public struct TransparentFrame : IComponentData, IQueryTypeParameter
	{
		public float4 TransparentColor;

		public FixedList64Bytes<Entity> Neighbours;
	}
}
