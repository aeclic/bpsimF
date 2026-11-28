using System.Collections.Generic;
using UnityEngine;

namespace Bpsim
{
	public class CoreManager : UnitySingleton<CoreManager>
	{
		[SerializeField]
		private List<GameObject> m_prefabList;

		[SerializeField]
		private ResourceData m_resources;

		private ResourceManager m_runtimeResources;

		public ResourceManager Resources => m_runtimeResources;

		public new static CoreManager Instance => UnitySingleton<CoreManager>.Instance;

		protected override void Awake()
		{
			base.Awake();
			BuildResources();
			UserSettings.Load();
			foreach (GameObject prefab in m_prefabList)
			{
				Object.Instantiate(prefab).transform.SetParent(base.transform, worldPositionStays: false);
			}
			// Unity's mobile default for an unlimited target is 30 FPS, while the
			// standalone default follows the display refresh rate.  The quality
			// presets also enable v-sync, which takes precedence over targetFrameRate
			// on standalone and can make high-refresh displays exceed the intended
			// cap.  Disable v-sync and use one explicit cap for every player target.
			QualitySettings.vSyncCount = 0;
			Application.targetFrameRate = 60;
		}

		private void BuildResources()
		{
			ResourceManager.Builder builder = new ResourceManager.Builder();
			builder.AddRange(m_resources.Prefabs, (GameObject obj) => obj.name);
			builder.AddRange(m_resources.Fonts, (Font obj) => obj.name);
			builder.AddRange(m_resources.Textures, (Texture2D obj) => obj.name);
			builder.AddRange(m_resources.Shaders, (Shader obj) => obj.name);
			builder.AddRange(m_resources.Materials, (Material obj) => obj.name);
			builder.AddRange(m_resources.TextAssets, (TextAsset obj) => obj.name);
			builder.AddRange(m_resources.ScriptableObjects, (ScriptableObject obj) => obj.name);
			builder.Add("DefaultFont-Regular", m_resources.Fonts.Find((Font obj) => obj.name == "FrutigerNeueLTW1G-Regular"));
			builder.Add("DefaultFont-Bold", m_resources.Fonts.Find((Font obj) => obj.name == "FrutigerNeueLTW1G-Bold"));
			m_runtimeResources = builder.Build();
		}

		private void Start()
		{
		}
	}
}
