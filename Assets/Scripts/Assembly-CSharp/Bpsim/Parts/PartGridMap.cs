using System;
using Unity.Collections;
using Unity.Mathematics;

namespace Bpsim.Parts
{
	public struct PartGridMap<T> : IDisposable where T : unmanaged
	{
		private NativeParallelHashMap<int3, T> m_data;

		public int Count => m_data.Count();

		public int Capacity
		{
			get
			{
				return m_data.Capacity;
			}
			set
			{
				m_data.Capacity = value;
			}
		}

		public PartGridMap(int capacity, Allocator allocator)
		{
			m_data = new NativeParallelHashMap<int3, T>(capacity, allocator);
		}

		public bool Contains(int x, int y, int level)
		{
			return m_data.ContainsKey(new int3(x, y, level));
		}

		public void Add(int x, int y, int level, T part)
		{
			m_data.Add(new int3(x, y, level), part);
		}

		public T Get(int x, int y, int level)
		{
			return m_data[new int3(x, y, level)];
		}

		public void Set(int x, int y, int level, T part)
		{
			m_data[new int3(x, y, level)] = part;
		}

		public bool TryGet(int x, int y, int level, out T part)
		{
			return m_data.TryGetValue(new int3(x, y, level), out part);
		}

		public void Remove(int x, int y, int level)
		{
			m_data.Remove(new int3(x, y, level));
		}

		public void Clear()
		{
			m_data.Clear();
		}

		public void Dispose()
		{
			m_data.Dispose();
		}
	}
}
