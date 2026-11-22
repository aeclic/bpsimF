namespace Bpsim
{
	public class InteractionSettings : SettingsBase
	{
		private bool m_quickBuild;

		private bool m_middleButtonPan;

		private float m_levelGridSize = 1f;

		public bool QuickBuild
		{
			get
			{
				return m_quickBuild;
			}
			set
			{
				SetProperty(ref m_quickBuild, value, "QuickBuild");
			}
		}

		public bool MiddleButtonPan
		{
			get
			{
				return m_middleButtonPan;
			}
			set
			{
				SetProperty(ref m_middleButtonPan, value, "MiddleButtonPan");
			}
		}

		public InteractionSettings()
		{
			QuickBuild = true;
			MiddleButtonPan = true;
		}

		public float LevelGridSize
		{
			get => m_levelGridSize;
			set => SetProperty(ref m_levelGridSize, float.IsNaN(value) || float.IsInfinity(value) || value <= 0f
				? 1f : UnityEngine.Mathf.Clamp(value, .25f, 4f), nameof(LevelGridSize));
		}

		public static float GetLevelMapScale() => 1f / (UserSettings.Instance?.InteractionSettings?.LevelGridSize ?? 1f);

		public void Update(InteractionSettings settings)
		{
			if (settings != null)
			{
				QuickBuild = settings.QuickBuild;
				MiddleButtonPan = settings.MiddleButtonPan;
				LevelGridSize = settings.LevelGridSize;
			}
		}
	}
}
