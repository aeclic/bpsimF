using System;

namespace Bpsim.Parts
{
	public readonly struct PartRangeInfo : IEquatable<PartRangeInfo>
	{
		public struct Writable
		{
			public PartType PartType;

			public int PartStartIndex;

			public int PartEndIndex;

			public Writable(PartType partType, int partStartIndex, int partEndIndex)
			{
				PartType = partType;
				PartStartIndex = partStartIndex;
				PartEndIndex = partEndIndex;
			}
		}

		public readonly PartType PartType;

		public readonly int PartStartIndex;

		public readonly int PartEndIndex;

		public PartRangeInfo(in PartAspect part)
			: this(part.PartType, part.PartIndex)
		{
		}

		public PartRangeInfo(PartType partType, int partIndex)
			: this(partType, partIndex, partIndex)
		{
		}

		public PartRangeInfo(Writable writable)
			: this(writable.PartType, writable.PartStartIndex, writable.PartEndIndex)
		{
		}

		public PartRangeInfo(PartType partType, int partStartIndex, int partEndIndex)
		{
			PartType = partType;
			PartStartIndex = partStartIndex;
			PartEndIndex = partEndIndex;
		}

		public override bool Equals(object other)
		{
			if (other is PartRangeInfo other2)
			{
				return Equals(other2);
			}
			return false;
		}

		public bool Equals(PartRangeInfo other)
		{
			if (PartType == other.PartType && PartStartIndex == other.PartStartIndex)
			{
				return PartEndIndex == other.PartEndIndex;
			}
			return false;
		}

		public override int GetHashCode()
		{
			return HashCode.Combine((int)PartType, PartStartIndex, PartEndIndex);
		}

		public void Deconstruct(out PartType partType, out int partIndex)
		{
			partType = PartType;
			partIndex = PartStartIndex;
		}

		public void Deconstruct(out PartType partType, out int partStartIndex, out int partEndIndex)
		{
			partType = PartType;
			partStartIndex = PartStartIndex;
			partEndIndex = PartEndIndex;
		}

		public bool Contains(PartTypeInfo info)
		{
			if (PartType == info.PartType && PartStartIndex <= info.PartIndex)
			{
				return info.PartIndex <= PartEndIndex;
			}
			return false;
		}

		public bool Contains(PartRangeInfo info)
		{
			if (PartType == info.PartType && PartStartIndex <= info.PartStartIndex)
			{
				return info.PartEndIndex <= PartEndIndex;
			}
			return false;
		}

		public static bool operator ==(PartRangeInfo left, PartRangeInfo right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(PartRangeInfo left, PartRangeInfo right)
		{
			return !(left == right);
		}

		public static explicit operator PartRangeInfo(PartTypeInfo info)
		{
			return new PartRangeInfo(info.PartType, info.PartIndex);
		}
	}
}
