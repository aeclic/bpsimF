namespace Bpsim
{
	public class ButtonSettings : SettingsBase
	{
		private float m_buttonScale;

		private float m_scrollViewHeightScale;

		private int m_layoutMode;

		private bool m_highPartTypePriority;

		private int m_maxSeparationCount;

		private bool m_displayButtonIndex;

		public float ButtonScale
		{
			get
			{
				return m_buttonScale;
			}
			set
			{
				if (float.IsFinite(value))
				{
					SetProperty(ref m_buttonScale, value, "ButtonScale");
				}
			}
		}

		public float ScrollViewHeightScale
		{
			get
			{
				return m_scrollViewHeightScale;
			}
			set
			{
				if (float.IsFinite(value))
				{
					SetProperty(ref m_scrollViewHeightScale, value, "ScrollViewHeightScale");
				}
			}
		}

		public int LayoutMode
		{
			get
			{
				return m_layoutMode;
			}
			set
			{
				if (value == 0 || value == 1)
				{
					SetProperty(ref m_layoutMode, value, "LayoutMode");
				}
			}
		}

		public bool HighPartTypePriority
		{
			get
			{
				return m_highPartTypePriority;
			}
			set
			{
				SetProperty(ref m_highPartTypePriority, value, "HighPartTypePriority");
			}
		}

		public int MaxSeparationCount
		{
			get
			{
				return m_maxSeparationCount;
			}
			set
			{
				if (value >= 0)
				{
					SetProperty(ref m_maxSeparationCount, value, "MaxSeparationCount");
				}
			}
		}

		public bool DisplayButtonIndex
		{
			get
			{
				return m_displayButtonIndex;
			}
			set
			{
				SetProperty(ref m_displayButtonIndex, value, "DisplayButtonIndex");
			}
		}

		public ButtonSettings()
		{
			ButtonScale = 1f;
			ScrollViewHeightScale = 1f;
			LayoutMode = 0;
			HighPartTypePriority = false;
			MaxSeparationCount = 3;
			DisplayButtonIndex = false;
		}

		public void Update(ButtonSettings settings)
		{
			if (settings != null)
			{
				ButtonScale = settings.ButtonScale;
				ScrollViewHeightScale = settings.ScrollViewHeightScale;
				LayoutMode = settings.LayoutMode;
				HighPartTypePriority = settings.HighPartTypePriority;
				MaxSeparationCount = settings.MaxSeparationCount;
				DisplayButtonIndex = settings.DisplayButtonIndex;
			}
		}
	}
}
