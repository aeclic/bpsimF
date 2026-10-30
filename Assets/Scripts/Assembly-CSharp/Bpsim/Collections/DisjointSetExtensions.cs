namespace Bpsim.Collections
{
	public static class DisjointSetExtensions
	{
		public static int[] ToArray(this DisjointSet disjointSet)
		{
			int count = disjointSet.Count;
			int[] array = new int[count];
			for (int i = 0; i < count; i++)
			{
				array[i] = disjointSet.FindSet(i);
			}
			return array;
		}

		public static int[] GetComponentIndexes(this DisjointSet disjointSet, out int componentCount)
		{
			int[] array = new int[disjointSet.Count];
			disjointSet.GetComponentIndexes(array, out componentCount);
			return array;
		}

		public static void GetComponentIndexes(this DisjointSet disjointSet, int[] componentIndexes, out int componentCount)
		{
			int count = disjointSet.Count;
			componentCount = 0;
			for (int i = 0; i < count; i++)
			{
				componentIndexes[i] = -1;
			}
			for (int j = 0; j < count; j++)
			{
				int num = disjointSet.FindSet(j);
				int num2 = componentIndexes[num];
				if (num2 == -1)
				{
					num2 = (componentIndexes[num] = componentCount++);
				}
				componentIndexes[j] = num2;
			}
		}
	}
}
