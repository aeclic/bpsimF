using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

namespace Bpsim.Rendering
{
	[MaterialProperty("_BlendFactor", -1)]
	public struct BlendFactor : IComponentData, IQueryTypeParameter
	{
		public float4 Value;
	}
}
