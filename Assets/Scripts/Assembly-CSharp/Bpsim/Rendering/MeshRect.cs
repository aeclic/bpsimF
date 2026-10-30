using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

namespace Bpsim.Rendering
{
	[MaterialProperty("_Rect", -1)]
	public struct MeshRect : IComponentData, IQueryTypeParameter
	{
		public float4 Value;
	}
}
