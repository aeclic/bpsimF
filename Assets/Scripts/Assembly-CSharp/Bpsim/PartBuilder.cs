using System;
using System.Collections.Generic;
using System.Linq;
using Bpsim.Parts;
using Bpsim.Serialization;
using Bpsim.Templates;
using UnityEngine;

namespace Bpsim
{
	[Serializable]
	[CreateAssetMenu(fileName = "PartBuilder", menuName = "ScriptableObjects/PartBuilder", order = 1)]
	public class PartBuilder : ScriptableObject
	{
		[Serializable]
		public class PartRange
		{
			[SerializeField]
			private PartType m_partType;

			[SerializeField]
			private int m_partIndex;

			[SerializeField]
			private int m_startIndex;

			[SerializeField]
			private int m_endIndex;

			[SerializeField]
			private string m_nameFormat;

			public PartType PartType => m_partType;

			public int PartIndex => m_partIndex;

			public int StartIndex => m_startIndex;

			public int EndIndex => m_endIndex;

			public string NameFormat => m_nameFormat;
		}

		[SerializeField]
		private List<PartRange> m_ranges;

		[SerializeField]
		private List<TextAsset> m_templateGroups;

		private Dictionary<PartTypeInfo, PartRange> m_rangeMap;

		private List<List<GameObjectTemplate>> m_runtimeTemplates;

		public List<PartRange> Ranges => m_ranges;

		public List<List<GameObjectTemplate>> RuntimeTemplates => m_runtimeTemplates;

		private void OnEnable()
		{
			m_rangeMap = m_ranges.ToDictionary((PartRange range) => new PartTypeInfo(range.PartType, range.PartIndex));
		}

		public PartContainer Build(PartContainer container, Action<ManagedPart> callback)
		{
			LoadTemplates();
			PartContainer partContainer = PartContainer.Create();
			foreach (List<GameObjectTemplate> runtimeTemplate in m_runtimeTemplates)
			{
				foreach (ManagedPart item in CreatePartsFromTemplate(runtimeTemplate, callback))
				{
					if (item.PartIndex == 0)
					{
						partContainer.AddPart(item);
					}
					else
					{
						partContainer.Find(item.PartType).AddCustomPart(item);
					}
				}
			}
			return partContainer;
		}

		public IEnumerable<ManagedPart> CreateParts(ManagedPart part)
		{
			if (m_rangeMap.TryGetValue(part.TypeInfo, out var range))
			{
				for (int i = range.StartIndex; i <= range.EndIndex; i++)
				{
					ManagedPart managedPart = UnityEngine.Object.Instantiate(part);
					managedPart.PartIndex = i;
					managedPart.gameObject.name = string.Format(range.NameFormat, i);
					yield return managedPart;
				}
			}
		}

		private void LoadTemplates()
		{
			m_runtimeTemplates = new List<List<GameObjectTemplate>>(m_templateGroups.Count);
			foreach (TextAsset templateGroup in m_templateGroups)
			{
				List<GameObjectTemplate> item = Json.Deserialize<List<GameObjectTemplate>>(templateGroup.text);
				m_runtimeTemplates.Add(item);
			}
		}

		private ManagedPart CreatePartsFromTemplate(GameObjectTemplate template, Action<ManagedPart> callback)
		{
			ManagedPart component = template.Apply(ResourceResolver.Default).GetComponent<ManagedPart>();
			callback?.Invoke(component);
			return component;
		}

		private IEnumerable<ManagedPart> CreatePartsFromTemplate(List<GameObjectTemplate> templates, Action<ManagedPart> callback)
		{
			foreach (GameObjectTemplate template in templates)
			{
				yield return CreatePartsFromTemplate(template, callback);
			}
		}
	}
}
