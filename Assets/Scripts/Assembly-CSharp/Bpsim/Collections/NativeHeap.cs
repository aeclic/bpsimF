using System;
using Unity.Collections;

namespace Bpsim.Collections
{
	public struct NativeHeap<T> : IDisposable where T : unmanaged, IComparable<T>
	{
		private NativeList<T> m_nodes;

		public bool IsCreated => m_nodes.IsCreated;

		public bool IsEmpty => m_nodes.Length == 0;

		public int Count => m_nodes.Length;

		public NativeArray<T>.ReadOnly UnorderedItems => m_nodes.AsReadOnly();

		public NativeHeap(int capacity, Allocator allocator)
		{
			m_nodes = new NativeList<T>(capacity, allocator);
		}

		public NativeHeap(NativeArray<T> nodes, Allocator allocator)
		{
			m_nodes = new NativeList<T>(nodes.Length, allocator);
			foreach (T item in nodes)
			{
				T value = item;
				m_nodes.Add(in value);
			}
			Heapify();
		}

		private void Heapify()
		{
			int num = m_nodes.Length - 1;
			if (num > 0)
			{
				for (int num2 = num - 1 >> 1; num2 >= 0; num2--)
				{
					MoveDown(num2);
				}
			}
		}

		public void Push(T node)
		{
			m_nodes.Add(in node);
			MoveUp(m_nodes.Length - 1);
		}

		public void PushRange(NativeArray<T> nodes)
		{
			if (m_nodes.Length == 0)
			{
				m_nodes.AddRange(nodes);
				Heapify();
				return;
			}
			foreach (T item in nodes)
			{
				Push(item);
			}
		}

		public T Peek()
		{
			if (m_nodes.Length == 0)
			{
				throw new InvalidOperationException();
			}
			return m_nodes[0];
		}

		public T Pop()
		{
			if (m_nodes.Length == 0)
			{
				throw new InvalidOperationException();
			}
			T result = m_nodes[0];
			int num = m_nodes.Length - 1;
			m_nodes[0] = m_nodes[num];
			m_nodes.RemoveAt(num);
			if (num > 0)
			{
				MoveDown(0);
			}
			return result;
		}

		public T PopAndPush(T node)
		{
			if (m_nodes.Length == 0)
			{
				throw new InvalidOperationException();
			}
			T result = m_nodes[0];
			m_nodes[0] = node;
			MoveDown(0);
			return result;
		}

		public void Clear()
		{
			m_nodes.Clear();
		}

		public void Dispose()
		{
			m_nodes.Dispose();
		}

		private void MoveUp(int index)
		{
			T value = m_nodes[index];
			while (index > 0)
			{
				int num = index - 1 >> 1;
				T val = m_nodes[num];
				if (value.CompareTo(val) < 0)
				{
					m_nodes[num] = value;
					m_nodes[index] = val;
					index = num;
					continue;
				}
				break;
			}
		}

		private void MoveDown(int index)
		{
			T value = m_nodes[index];
			int length = m_nodes.Length;
			while ((index << 1) + 1 < length)
			{
				int num = (index << 1) + 1;
				if (num + 1 < length && m_nodes[num + 1].CompareTo(m_nodes[num]) < 0)
				{
					num++;
				}
				T val = m_nodes[num];
				if (value.CompareTo(val) > 0)
				{
					m_nodes[num] = value;
					m_nodes[index] = val;
					index = num;
					continue;
				}
				break;
			}
		}
	}
}
