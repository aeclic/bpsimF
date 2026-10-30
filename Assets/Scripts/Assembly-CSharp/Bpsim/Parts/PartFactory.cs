using System;
using System.Collections.Generic;
using Bpsim.Serialization;
using Bpsim.Templates;
using Unity.Rendering.Authoring;
using Unity.VisualScripting;
using UnityEngine;

namespace Bpsim.Parts
{
	public class PartFactory : MonoBehaviour
	{
		[SerializeField]
		private PartResources m_resources;

		[SerializeField]
		private GameObject m_runtimePartList;

		private PartCollection<GameObjectTemplate> m_templateCollection;

		private PartCollection<ManagedPart> m_partCollection;

		private Dictionary<PartTypeInfo, PartExtensionData> m_partExtensionMap;

		public PartCollection<GameObjectTemplate> TemplateCollection => m_templateCollection;

		public PartCollection<ManagedPart> PartCollection => m_partCollection;

		public Dictionary<PartTypeInfo, PartExtensionData> PartExtensionMap => m_partExtensionMap;

		private void Awake()
		{
			BuildTemplates();
			BuildParts(OnPartCreated);
			BuildExtensionMap();
		}

		private void BuildTemplates()
		{
			PartCollection<GameObjectTemplate> partCollection = new PartCollection<GameObjectTemplate>();
			foreach (TextAsset templateGroup in m_resources.TemplateGroups)
			{
				foreach (GameObjectTemplate item in Json.Deserialize<List<GameObjectTemplate>>(templateGroup.text))
				{
					PartTemplate component = item.GetComponent<PartTemplate>();
					partCollection.AddPart(new PartTypeInfo(component.PartType, component.PartIndex), item);
				}
			}
			m_templateCollection = partCollection;
		}

		private void BuildParts(Action<ManagedPart> callback)
		{
			PartCollection<ManagedPart> partCollection = new PartCollection<ManagedPart>();
			for (int i = 0; i < m_resources.PartGroups.Count; i++)
			{
				GameObject gameObject = UnityEngine.Object.Instantiate(m_resources.PartGroups[i]);
				gameObject.transform.parent = m_runtimePartList.transform;
				for (int j = 0; j < gameObject.transform.childCount; j++)
				{
					ManagedPart component = gameObject.transform.GetChild(j).GetComponent<ManagedPart>();
					callback?.Invoke(component);
					partCollection.AddPart(component.TypeInfo, component);
				}
			}
			ModifyParts(partCollection);
			m_partCollection = partCollection;
		}

		private void ModifyParts(PartCollection<ManagedPart> collection)
		{
			foreach (ManagedPart value in collection.FindParts(PartType.MetalFrame).Data.Values)
			{
				if (value.TypeInfo.BelongsTo(BasePart.ColoredFrames, BasePart.TransparentFrames))
				{
					value.AddComponent<MaterialColor>().color = new Color(1f, 1f, 1f, 1f);
					value.transform.Find("Background").AddComponent<MaterialColor>().color = new Color(1f, 1f, 1f, 0.6f);
				}
			}
		}

		private void BuildExtensionMap()
		{
			List<PartExtensionPair> list = Json.Deserialize<List<PartExtensionPair>>(m_resources.ExtensionMap.text);
			Dictionary<PartTypeInfo, PartExtensionData> dictionary = new Dictionary<PartTypeInfo, PartExtensionData>();
			foreach (PartExtensionPair item in list)
			{
				for (int i = item.Key.PartStartIndex; i <= item.Key.PartEndIndex; i++)
				{
					dictionary.Add(new PartTypeInfo(item.Key.PartType, i), item.Value);
				}
			}
			m_partExtensionMap = dictionary;
		}

		private void OnPartCreated(ManagedPart part)
		{
			part.transform.position = new Vector3(2 * (int)part.PartType, -2 * part.PartIndex, 0f);
		}
	}
}
