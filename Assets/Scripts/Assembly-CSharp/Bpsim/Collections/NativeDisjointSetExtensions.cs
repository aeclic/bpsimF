using Unity.Collections;

namespace Bpsim.Collections
{
	public static class NativeDisjointSetExtensions
	{
		public static NativeArray<int> ToArray(this NativeDisjointSet disjointSet, Allocator allocator)
		{
			int count = disjointSet.Count;
			NativeArray<int> result = new NativeArray<int>(count, allocator);
			for (int i = 0; i < count; i++)
			{
				result[i] = disjointSet.FindSet(i);
			}
			return result;
		}

		public static NativeArray<int> GetComponentIndexes(this NativeDisjointSet disjointSet, Allocator allocator, out int componentCount)
		{
			NativeArray<int> nativeArray = new NativeArray<int>(disjointSet.Count, allocator);
			disjointSet.GetComponentIndexes(nativeArray, out componentCount);
			return nativeArray;
		}

		public static void GetComponentIndexes(this NativeDisjointSet disjointSet, NativeArray<int> componentIndexes, out int componentCount)
		{
			int count = disjointSet.Count;
			componentCount = 0;
			for (int i = 0; i < count; i++)
			{
				componentIndexes[i] = -1;
			}
			for (int j = 0; j < count; j++)
			{
				int index = disjointSet.FindSet(j);
				int num = componentIndexes[index];
				if (num == -1)
				{
					num = (componentIndexes[index] = componentCount++);
				}
				componentIndexes[j] = num;
			}
		}
	}
}
