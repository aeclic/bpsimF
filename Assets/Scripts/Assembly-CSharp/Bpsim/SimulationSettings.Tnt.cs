namespace Bpsim
{
	public partial class SimulationSettings
	{
		// BPSS 901-17 INSettingsBExp defaults. Field initializers also cover old settings files.
		private float m_tntForce = 2f, m_tntRadius = 1f;
		private float m_alienTntForce = 2f, m_alienTntRadius = 1f, m_alienTntCooling = 0.1f;
		private bool m_alienTntGun = true, m_blasterTntDestroy;

		public float TNTExplosionForce
		{
			get => m_tntForce;
			set { if (float.IsFinite(value)) SetProperty(ref m_tntForce, value, nameof(TNTExplosionForce)); }
		}
		public float TNTExplosionRadius
		{
			get => m_tntRadius;
			set { if (float.IsFinite(value) && value >= 0f) SetProperty(ref m_tntRadius, value, nameof(TNTExplosionRadius)); }
		}
		public float AlienTNTExplosionForce
		{
			get => m_alienTntForce;
			set { if (float.IsFinite(value)) SetProperty(ref m_alienTntForce, value, nameof(AlienTNTExplosionForce)); }
		}
		public float AlienTNTExplosionRadius
		{
			get => m_alienTntRadius;
			set { if (float.IsFinite(value) && value >= 0f) SetProperty(ref m_alienTntRadius, value, nameof(AlienTNTExplosionRadius)); }
		}
		public float AlienTNTExplosionCoolingTime
		{
			get => m_alienTntCooling;
			set { if (float.IsFinite(value) && value >= 0f) SetProperty(ref m_alienTntCooling, value, nameof(AlienTNTExplosionCoolingTime)); }
		}
		public bool AlienTNTTriggerGun
		{
			get => m_alienTntGun;
			set => SetProperty(ref m_alienTntGun, value, nameof(AlienTNTTriggerGun));
		}
		public bool BlasterTNTDestroy
		{
			get => m_blasterTntDestroy;
			set => SetProperty(ref m_blasterTntDestroy, value, nameof(BlasterTNTDestroy));
		}
		private void UpdateTnt(SimulationSettings other)
		{
			TNTExplosionForce = other.TNTExplosionForce;
			TNTExplosionRadius = other.TNTExplosionRadius;
			AlienTNTExplosionForce = other.AlienTNTExplosionForce;
			AlienTNTExplosionRadius = other.AlienTNTExplosionRadius;
			AlienTNTExplosionCoolingTime = other.AlienTNTExplosionCoolingTime;
			AlienTNTTriggerGun = other.AlienTNTTriggerGun;
			BlasterTNTDestroy = other.BlasterTNTDestroy;
		}
	}
}
