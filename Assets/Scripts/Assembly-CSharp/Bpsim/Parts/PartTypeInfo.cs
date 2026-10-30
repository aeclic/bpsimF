using System;
using Unity.Mathematics;

namespace Bpsim.Parts
{
	public readonly struct PartTypeInfo : IEquatable<PartTypeInfo>
	{
		public struct Writable
		{
			public PartType PartType;

			public int PartIndex;

			public Writable(PartType partType, int partIndex)
			{
				PartType = partType;
				PartIndex = partIndex;
			}
		}

		public readonly PartType PartType;

		public readonly int PartIndex;

		public PartTypeInfo(in PartAspect part)
			: this(part.PartType, part.PartIndex)
		{
		}

		public PartTypeInfo(Writable writable)
			: this(writable.PartType, writable.PartIndex)
		{
		}

		public PartTypeInfo(PartType partType, int partIndex)
		{
			PartType = partType;
			PartIndex = partIndex;
		}

		public override bool Equals(object other)
		{
			if (other is PartTypeInfo other2)
			{
				return Equals(other2);
			}
			return false;
		}

		public bool Equals(PartTypeInfo other)
		{
			if (PartType == other.PartType)
			{
				return PartIndex == other.PartIndex;
			}
			return false;
		}

		public override int GetHashCode()
		{
			return (int)math.hash(new int2((int)PartType, PartIndex));
		}

		public void Deconstruct(out PartType partType, out int partIndex)
		{
			partType = PartType;
			partIndex = PartIndex;
		}

		public bool BelongsTo(PartTypeInfo info)
		{
			if (PartType == info.PartType)
			{
				return PartIndex == info.PartIndex;
			}
			return false;
		}

		public bool BelongsTo(PartTypeInfo info0, PartTypeInfo info1)
		{
			if (!BelongsTo(info0))
			{
				return BelongsTo(info1);
			}
			return true;
		}

		public bool BelongsTo(PartRangeInfo info)
		{
			if (PartType == info.PartType && info.PartStartIndex <= PartIndex)
			{
				return PartIndex <= info.PartEndIndex;
			}
			return false;
		}

		public bool BelongsTo(PartRangeInfo info0, PartRangeInfo info1)
		{
			if (!BelongsTo(info0))
			{
				return BelongsTo(info1);
			}
			return true;
		}

		public static bool operator ==(PartTypeInfo left, PartTypeInfo right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(PartTypeInfo left, PartTypeInfo right)
		{
			return !(left == right);
		}
	}
}
