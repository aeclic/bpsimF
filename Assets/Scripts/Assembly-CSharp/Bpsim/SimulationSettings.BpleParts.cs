namespace Bpsim
{
	public partial class SimulationSettings
	{
		private bool m_bpleLightSystem = true;
		private bool m_bpleSpecialUmbrellas = true;
		private bool m_bpleSeparatedLightButtons = true;
		private bool m_bpleDarkness;
		private bool m_bpleLightReaction;
		private bool m_bpleLightTriggerExplosions;
		public bool BpleLightSystem { get => m_bpleLightSystem; set => SetProperty(ref m_bpleLightSystem, value, nameof(BpleLightSystem)); }
		public bool BpleSpecialUmbrellas { get => m_bpleSpecialUmbrellas; set => SetProperty(ref m_bpleSpecialUmbrellas, value, nameof(BpleSpecialUmbrellas)); }
		public bool BpleSeparatedLightButtons { get => m_bpleSeparatedLightButtons; set => SetProperty(ref m_bpleSeparatedLightButtons, value, nameof(BpleSeparatedLightButtons)); }
		public bool BpleDarkness { get => m_bpleDarkness; set => SetProperty(ref m_bpleDarkness, value, nameof(BpleDarkness)); }
		public bool BpleLightReaction { get => m_bpleLightReaction; set => SetProperty(ref m_bpleLightReaction, value, nameof(BpleLightReaction)); }
		public bool BpleLightTriggerExplosions { get => m_bpleLightTriggerExplosions; set => SetProperty(ref m_bpleLightTriggerExplosions, value, nameof(BpleLightTriggerExplosions)); }
		private void UpdateBpleParts(SimulationSettings settings)
		{
			BpleLightSystem = settings.BpleLightSystem;
			BpleSpecialUmbrellas = settings.BpleSpecialUmbrellas;
			BpleSeparatedLightButtons = settings.BpleSeparatedLightButtons;
			BpleDarkness = settings.BpleDarkness;
			BpleLightReaction = settings.BpleLightReaction;
			BpleLightTriggerExplosions = settings.BpleLightTriggerExplosions;
		}
	}
}
