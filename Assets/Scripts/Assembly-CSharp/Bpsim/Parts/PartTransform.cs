using Unity.Entities;

namespace Bpsim.Parts
{
	public struct PartTransform : IComponentData, IQueryTypeParameter
	{
		public int X;

		public int Y;

		public int Rotation;

		public bool Flipped;
	}
}
