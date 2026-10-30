using Unity.Entities;

namespace Bpsim.Parts
{
	public struct PartGridInfo
	{
		public Entity PartContainer;

		public Entity Part;

		public int Occupied;

		public bool IsEmpty
		{
			get
			{
				if (!HasPartContainer && !HasPart)
				{
					return Occupied == 0;
				}
				return false;
			}
		}

		public bool HasPartContainer => PartContainer != Entity.Null;

		public bool HasPart => Part != Entity.Null;

		public PartGridInfo(Entity partContainer, Entity part, int occupied)
		{
			PartContainer = partContainer;
			Part = part;
			Occupied = occupied;
		}
	}
}
