using System;
using Unity.Mathematics;

namespace Bpsim.Collections
{
	public struct ComparableTuple<T1, T2> : IEquatable<ComparableTuple<T1, T2>>, IComparable<ComparableTuple<T1, T2>> where T1 : IEquatable<T1>, IComparable<T1> where T2 : IEquatable<T2>, IComparable<T2>
	{
		public T1 Item1;

		public T2 Item2;

		public ComparableTuple(T1 item1, T2 item2)
		{
			Item1 = item1;
			Item2 = item2;
		}

		public override bool Equals(object obj)
		{
			if (obj is ComparableTuple<T1, T2> other)
			{
				return Equals(other);
			}
			return false;
		}

		public bool Equals(ComparableTuple<T1, T2> other)
		{
			ref T1 item = ref Item1;
			T1 item2 = other.Item1;
			if (item.Equals(item2))
			{
				ref T2 item3 = ref Item2;
				T2 item4 = other.Item2;
				return item3.Equals(item4);
			}
			return false;
		}

		public int CompareTo(ComparableTuple<T1, T2> other)
		{
			ref T1 item = ref Item1;
			T1 item2 = other.Item1;
			int num = item.CompareTo(item2);
			if (num == 0)
			{
				ref T2 item3 = ref Item2;
				T2 item4 = other.Item2;
				return item3.CompareTo(item4);
			}
			return num;
		}

		public override int GetHashCode()
		{
			return (int)math.hash(new int2(Item1.GetHashCode(), Item2.GetHashCode()));
		}
	}
}
