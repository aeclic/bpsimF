using System;
using System.Collections.Generic;
using Bpsim.Parts;
using Bpsim.Rendering;
using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.UI
{
	internal class CustomPartSelector : InterfaceBase
	{
		[SerializeField]
		private GameObject m_itemTemplate;

		[SerializeField]
		private Text m_partTypeText;

		[SerializeField]
		private Text m_partIndexText;

		[SerializeField]
		private InputField m_rotation;

		[SerializeField]
		private ToggleSwitch m_flipped;

		[SerializeField]
		private GameObject m_listContent;

		[SerializeField]
		private Button m_applyButton;

		[SerializeField]
		private Button m_cancelButton;

		private PartType m_partType;

		private int m_partIndex;

		private Dictionary<int, Toggle> m_toggleMap;

		public PartType PartType => m_partType;

		public event Action<PartType, int, int, bool> Completed;

		public void Initialize(PartType partType, int partIndex, int rotation, bool flipped)
		{
			m_partType = partType;
			m_partTypeText.text = $"{(int)partType}. {partType.GetAliasName()}";
			m_partIndex = -1;
			m_rotation.text = rotation.ToString();
			m_flipped.IsOn = flipped;
			m_toggleMap = new Dictionary<int, Toggle>();
			foreach (int key in PartManager.Instance.Factory.PartCollection.FindParts(partType).Data.Keys)
			{
				CreateItem(partType, key);
			}
			if (m_toggleMap.TryGetValue(partIndex, out var value))
			{
				value.isOn = true;
			}
			m_applyButton.onClick.AddListener(Apply);
			m_cancelButton.onClick.AddListener(Cancel);
		}

		private GameObject CreateItem(PartType partType, int partIndex)
		{
			GameObject obj = UnityEngine.Object.Instantiate(m_itemTemplate);
			obj.name = $"Item_{partType}_{partIndex}";
			obj.transform.SetParent(m_listContent.transform, worldPositionStays: false);
			obj.SetActive(value: true);
			obj.transform.Find("Name").GetComponent<Text>().text = partType.GetAliasName();
			Toggle component = obj.GetComponent<Toggle>();
			component.onValueChanged.AddListener(delegate(bool value)
			{
				if (value)
				{
					Select(partIndex);
				}
			});
			m_toggleMap.Add(partIndex, component);
			RectTransform rectTransform = (RectTransform)obj.transform.Find("PartIcon");
			PartSelector.SetRenderedIcon(rectTransform, new PartTypeInfo(partType, partIndex), partType == PartType.SkinPart);
			return obj;
		}

		private void Select(int partIndex)
		{
			if (m_partIndex != partIndex)
			{
				m_partIndex = partIndex;
				m_partIndexText.text = partIndex.ToString();
			}
		}

		private void Apply()
		{
			int.TryParse(m_rotation.text, out var result);
			this.Completed?.Invoke(m_partType, m_partIndex, result, m_flipped.IsOn);
			Close();
		}

		private void Cancel()
		{
			Close();
		}

		private new void Close()
		{
			UnityEngine.Object.Destroy(base.gameObject);
		}
	}
}
