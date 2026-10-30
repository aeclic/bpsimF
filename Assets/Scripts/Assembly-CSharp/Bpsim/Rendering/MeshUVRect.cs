using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

namespace Bpsim.Rendering
{
	[MaterialProperty("_UVRect", -1)]
	public struct MeshUVRect : IComponentData, IQueryTypeParameter
	{
		public float4 Value;
	}
}
