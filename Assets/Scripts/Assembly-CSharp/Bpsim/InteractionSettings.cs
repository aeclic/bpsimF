namespace Bpsim
{
	public class InteractionSettings : SettingsBase
	{
		private bool m_quickBuild;

		private bool m_middleButtonPan;

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

		public void Update(InteractionSettings settings)
		{
			if (settings != null)
			{
				QuickBuild = settings.QuickBuild;
				MiddleButtonPan = settings.MiddleButtonPan;
			}
		}
	}
}
