using System.Collections.Generic;
using Bpsim.Parts;
using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.UI
{
	internal class SceneTabView : MonoBehaviour
	{
		[SerializeField]
		private GameObject m_tabItemTemplate;

		[SerializeField]
		private ToggleGroup m_toggleGroup;

		private readonly Dictionary<int, GameObject> m_tabs = new Dictionary<int, GameObject>();

		private void Start()
		{
			// Only the individual tabs need a background or intercept pointer input.
			Graphic plate = GetComponent<Graphic>();
			if (plate != null) plate.enabled = false;
			PartManager.Instance.SceneLoaded += OnSceneLoaded;
			PartManager.Instance.SceneUnloaded += OnSceneUnloaded;
			PartManager.Instance.SceneSelected += OnSceneSelected;
		}

		private void OnDestroy()
		{
			if (PartManager.Instance != null)
			{
				PartManager.Instance.SceneLoaded -= OnSceneLoaded;
				PartManager.Instance.SceneUnloaded -= OnSceneUnloaded;
				PartManager.Instance.SceneSelected -= OnSceneSelected;
			}
		}

		private void OnSceneLoaded(PartScene scene)
		{
			GameObject tabItem = Object.Instantiate(m_tabItemTemplate);
			tabItem.name = "SceneTab_" + scene.SceneID;
			tabItem.transform.SetParent(base.transform, worldPositionStays: false);
			tabItem.SetActive(value: true);
			m_tabs[scene.SceneID] = tabItem;
			Toggle component = tabItem.GetComponent<Toggle>();
			component.onValueChanged.AddListener(delegate(bool value)
			{
				if (value)
				{
					PartManager.Instance.SelectScene(scene.SceneID);
				}
			});
			component.group = m_toggleGroup;
			component.isOn = true;
			tabItem.transform.Find("Name").GetComponent<Text>().text = scene.SceneName;
			tabItem.transform.Find("CloseButton").GetComponent<Button>().onClick.AddListener(delegate
			{
				PartManager.Instance.UnloadScene(scene.SceneID);
			});
			UiThemeToggleCard.Attach(component);
			UiThemeApplicator.ApplyTo(tabItem);
		}

		private void OnSceneUnloaded(PartScene scene)
		{
			if (m_tabs.TryGetValue(scene.SceneID, out GameObject item))
			{
				m_tabs.Remove(scene.SceneID);
				Object.Destroy(item);
			}
		}

		private void OnSceneSelected(PartScene scene)
		{
			if (m_tabs.TryGetValue(scene.SceneID, out GameObject item) && item != null)
				item.GetComponent<Toggle>().SetIsOnWithoutNotify(true);
			foreach (GameObject tab in m_tabs.Values)
				if (tab != null) tab.GetComponent<UiThemeToggleCard>()?.RefreshPalette();
		}
	}
}
