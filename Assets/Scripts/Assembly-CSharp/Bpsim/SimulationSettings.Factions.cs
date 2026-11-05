namespace Bpsim
{
	public partial class SimulationSettings
	{
		private bool m_buildFactionOutlines = true;
		private bool m_runtimeFactionOutlines;
		private float m_factionOutlineWidth = 2.5f;
		public bool BuildFactionOutlines
		{
			get => m_buildFactionOutlines;
			set => SetProperty(ref m_buildFactionOutlines, value, nameof(BuildFactionOutlines));
		}
		public bool RuntimeFactionOutlines
		{
			get => m_runtimeFactionOutlines;
			set => SetProperty(ref m_runtimeFactionOutlines, value, nameof(RuntimeFactionOutlines));
		}
		public float FactionOutlineWidth
		{
			get => m_factionOutlineWidth;
			set { if (float.IsFinite(value) && value >= 1f && value <= 8f) SetProperty(ref m_factionOutlineWidth, value, nameof(FactionOutlineWidth)); }
		}
		private void UpdateFactions(SimulationSettings settings)
		{
			BuildFactionOutlines = settings.BuildFactionOutlines;
			RuntimeFactionOutlines = settings.RuntimeFactionOutlines;
			FactionOutlineWidth = settings.FactionOutlineWidth;
		}
	}
}
