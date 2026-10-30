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

		private void Start()
		{
			// Only the individual tabs need a background or intercept pointer input.
			Graphic plate = GetComponent<Graphic>();
			if (plate != null) plate.enabled = false;
			PartManager.Instance.SceneLoaded += OnSceneLoaded;
		}

		private void OnDestroy()
		{
			if (PartManager.Instance != null) PartManager.Instance.SceneLoaded -= OnSceneLoaded;
		}

		private void OnSceneLoaded(PartScene scene)
		{
			GameObject tabItem = Object.Instantiate(m_tabItemTemplate);
			tabItem.transform.SetParent(base.transform, worldPositionStays: false);
			tabItem.SetActive(value: true);
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
				Object.Destroy(tabItem);
			});
		}
	}
}
