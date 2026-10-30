using System;
using Bpsim.Parts;

namespace Bpsim.UI
{
	public struct PartButtonInfo : IEquatable<PartButtonInfo>
	{
		public PartButtonType ButtonType;

		public int ButtonIndex;

		public PartType PartType;

		public int PartIndex;

		public int ComponentIndex;

		public int ComponentRank;

		public PartButtonInfo(PartButtonType buttonType, int buttonIndex, PartType partType, int partIndex, int componentIndex)
			: this(buttonType, buttonIndex, partType, partIndex, componentIndex, -1)
		{
		}

		public PartButtonInfo(PartButtonType buttonType, int buttonIndex, PartType partType, int partIndex, int componentIndex, int componentRank)
		{
			ButtonType = buttonType;
			ButtonIndex = buttonIndex;
			PartType = partType;
			PartIndex = partIndex;
			ComponentIndex = componentIndex;
			ComponentRank = componentRank;
		}

		public override bool Equals(object other)
		{
			if (other is PartButtonInfo other2)
			{
				return Equals(other2);
			}
			return false;
		}

		public bool Equals(PartButtonInfo other)
		{
			if (ButtonType == other.ButtonType && ButtonIndex == other.ButtonIndex && PartType == other.PartType && PartIndex == other.PartIndex)
			{
				return ComponentRank == other.ComponentRank;
			}
			return false;
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(ButtonType, ButtonIndex, PartType, PartIndex, ComponentRank);
		}

		public static bool operator ==(PartButtonInfo left, PartButtonInfo right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(PartButtonInfo left, PartButtonInfo right)
		{
			return !(left == right);
		}
	}
}
