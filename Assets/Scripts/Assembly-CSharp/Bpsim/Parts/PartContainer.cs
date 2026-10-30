using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bpsim.Parts
{
	[Serializable]
	[CreateAssetMenu(fileName = "PartContainer", menuName = "ScriptableObjects/PartContainer", order = 1)]
	public class PartContainer : ScriptableObject
	{
		[Serializable]
		public class Entry
		{
			[SerializeField]
			private PartType m_partType;

			[SerializeField]
			private ManagedPart m_regularPart;

			[SerializeField]
			private List<ManagedPart> m_customParts;

			public PartType PartType => m_partType;

			public ManagedPart RegularPart => m_regularPart;

			public List<ManagedPart> CustomParts => m_customParts;

			public Entry(PartType partType)
			{
				m_partType = partType;
				m_regularPart = null;
				m_customParts = new List<ManagedPart>();
			}

			public ManagedPart FindPart(int partIndex)
			{
				if (partIndex == 0)
				{
					return m_regularPart;
				}
				return m_customParts.Find((ManagedPart part) => part.PartIndex == partIndex);
			}

			public int FindPartIndex(int partIndex)
			{
				return m_customParts.FindIndex((ManagedPart part) => part.PartIndex == partIndex);
			}

			public void SetPart(ManagedPart part)
			{
				m_regularPart = part;
			}

			public void AddCustomPart(ManagedPart part)
			{
				if (!SetCustomPart(part))
				{
					m_customParts.Add(part);
				}
			}

			public bool SetCustomPart(ManagedPart part)
			{
				int num = FindPartIndex(part.PartIndex);
				if (num == -1)
				{
					return false;
				}
				CustomParts[num] = part;
				return true;
			}

			public void AddCustomParts(IEnumerable<ManagedPart> part)
			{
				m_customParts.AddRange(part);
			}
		}

		[SerializeField]
		private List<Entry> m_data;

		public List<Entry> Data => m_data;

		public static PartContainer Create()
		{
			PartContainer partContainer = ScriptableObject.CreateInstance<PartContainer>();
			partContainer.m_data = new List<Entry>();
			return partContainer;
		}

		public Entry Find(PartType partType)
		{
			return m_data.Find((Entry entry) => entry.PartType == partType);
		}

		public ManagedPart FindPart(PartType partType)
		{
			return Find(partType)?.RegularPart;
		}

		public ManagedPart FindCustomPart(PartType partType, int partIndex)
		{
			return Find(partType)?.FindPart(partIndex);
		}

		public ManagedPart CreatePart(PartType partType)
		{
			ManagedPart managedPart = FindPart(partType);
			if (!(managedPart != null))
			{
				return null;
			}
			return UnityEngine.Object.Instantiate(managedPart);
		}

		public ManagedPart CreateCustomPart(PartType partType, int partIndex)
		{
			ManagedPart managedPart = FindCustomPart(partType, partIndex);
			if (!(managedPart != null))
			{
				return null;
			}
			return UnityEngine.Object.Instantiate(managedPart);
		}

		public void Add(Entry entry)
		{
			m_data.Add(entry);
		}

		public void AddPart(ManagedPart part)
		{
			if (!SetPart(part))
			{
				Entry entry = new Entry(part.PartType);
				entry.SetPart(part);
				m_data.Add(entry);
			}
		}

		public bool SetPart(ManagedPart part)
		{
			Entry entry = Find(part.PartType);
			if (entry == null)
			{
				return false;
			}
			entry.SetPart(part);
			return true;
		}

		public void AddCustomPart(ManagedPart part)
		{
			Entry entry = Find(part.PartType);
			if (entry == null)
			{
				entry = new Entry(part.PartType);
				m_data.Add(entry);
			}
			entry.AddCustomPart(part);
		}

		public bool SetCustomPart(ManagedPart part)
		{
			return Find(part.PartType)?.SetCustomPart(part) ?? false;
		}

		public void AddCustomParts(PartType partType, IEnumerable<ManagedPart> part)
		{
			Find(partType)?.AddCustomParts(part);
		}
	}
}
