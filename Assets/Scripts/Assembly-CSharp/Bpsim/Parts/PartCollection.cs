using System;
using System.Collections.Generic;

namespace Bpsim.Parts
{
	public class PartCollection<T>
	{
		public class Entry
		{
			private SortedList<int, T> m_data;

			public IReadOnlyDictionary<int, T> Data => m_data;

			public Entry()
			{
				m_data = new SortedList<int, T>();
			}

			public void Add(int index, T value)
			{
				m_data.Add(index, value);
			}

			public void Remove(int index)
			{
				m_data.Remove(index);
			}

			public T Get(int index)
			{
				return m_data[index];
			}

			public bool TryGet(int index, out T result)
			{
				return m_data.TryGetValue(index, out result);
			}
		}

		private Entry[] m_data;

		public int Length => (int)PartType.Max;

		public PartCollection()
		{
			// 必须按 PartType.Max 分配：旧的硬编码 49 在加入 FuelBox/FuelTube
			// （Max 改成 51）之后会让索引 49/50 在边界检查通过的情况下仍然越界，
			// 表现为进 Play 就 IndexOutOfRangeException、整个模拟起不来。
			m_data = new Entry[(int)PartType.Max];
		}

		public void AddPart(PartTypeInfo typeInfo, T value)
		{
			if (typeInfo.PartType < PartType.Unknown || typeInfo.PartType >= PartType.Max)
			{
				throw new ArgumentOutOfRangeException("PartType");
			}
			int partType = (int)typeInfo.PartType;
			if (m_data[partType] == null)
			{
				m_data[partType] = new Entry();
			}
			m_data[partType].Add(typeInfo.PartIndex, value);
		}

		public void RemovePart(PartTypeInfo typeInfo)
		{
			if (typeInfo.PartType < PartType.Unknown || typeInfo.PartType >= PartType.Max)
			{
				throw new ArgumentOutOfRangeException("PartType");
			}
			int partType = (int)typeInfo.PartType;
			m_data[partType].Remove(typeInfo.PartIndex);
		}

		public Entry FindParts(PartType partType)
		{
			if (partType < PartType.Unknown || partType >= PartType.Max)
			{
				throw new ArgumentOutOfRangeException("partType");
			}
			return m_data[(int)partType];
		}

		public bool TryFindParts(PartType partType, out Entry entry)
		{
			if (partType < PartType.Unknown || partType >= PartType.Max)
			{
				entry = null;
				return false;
			}
			if (m_data[(int)partType] == null)
			{
				entry = null;
				return false;
			}
			entry = m_data[(int)partType];
			return true;
		}

		public T FindPart(PartTypeInfo typeInfo)
		{
			return FindParts(typeInfo.PartType).Get(typeInfo.PartIndex);
		}

		public bool TryFindPart(PartTypeInfo typeInfo, out T result)
		{
			if (!TryFindParts(typeInfo.PartType, out var entry))
			{
				result = default(T);
				return false;
			}
			return entry.TryGet(typeInfo.PartIndex, out result);
		}
	}
}
