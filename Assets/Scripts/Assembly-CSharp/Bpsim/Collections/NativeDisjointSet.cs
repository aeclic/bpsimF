using System;
using Unity.Collections;

namespace Bpsim.Collections
{
	public struct NativeDisjointSet : IDisposable
	{
		private int m_count;

		private NativeArray<int> m_parent;

		private NativeArray<int> m_size;

		public bool IsCreated
		{
			get
			{
				if (m_parent.IsCreated)
				{
					return m_size.IsCreated;
				}
				return false;
			}
		}

		public int Count => m_count;

		public NativeDisjointSet(int count, Allocator allocator)
		{
			m_count = count;
			m_parent = new NativeArray<int>(count, allocator);
			m_size = new NativeArray<int>(count, allocator);
			for (int i = 0; i < count; i++)
			{
				MakeSet(i);
			}
		}

		public void MakeSet(int x)
		{
			m_parent[x] = x;
			m_size[x] = 1;
		}

		public int FindSet(int x)
		{
			int num = m_parent[x];
			if (x != num)
			{
				return m_parent[x] = FindSet(num);
			}
			return num;
		}

		public int FindSet(int x, out int size)
		{
			int num = FindSet(x);
			size = m_size[num];
			return num;
		}

		public void Union(int x, int y)
		{
			int num = FindSet(x);
			int num2 = FindSet(y);
			if (num != num2)
			{
				if (m_size[num] > m_size[num2])
				{
					m_size[num] += m_size[num2];
					m_parent[num2] = num;
				}
				else
				{
					m_size[num2] += m_size[num];
					m_parent[num] = num2;
				}
			}
		}

		public void Clear()
		{
			for (int i = 0; i < m_count; i++)
			{
				MakeSet(i);
			}
		}

		public void Dispose()
		{
			m_parent.Dispose();
			m_size.Dispose();
		}
	}
}
