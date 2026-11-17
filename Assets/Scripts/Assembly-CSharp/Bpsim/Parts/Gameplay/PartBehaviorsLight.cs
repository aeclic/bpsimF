using Unity.Mathematics;

namespace Bpsim.Parts.Gameplay
{
	public sealed class PointLightBehavior : PartBehavior
	{
		public bool CanTrigger => PartVariants.Resolve(Info).PartIndex != 1 || BpleLightRuntime.LightSystem;
		public override void Init()
		{
			Enabled = false;
			BpleLightRuntime.Ensure().Register(Part, Info);
			PartGameplayUtil.SetPointLightVisual(Part, false, false);
		}
		public override void Trigger()
		{
			if (!CanTrigger) return;
			Enabled = !Enabled;
			PartGameplayUtil.SetPointLightVisual(Part, Enabled, false);
			BpleLightRuntime.Instance.SetActivation(Part, Enabled);
		}
		public override void Dispose() => BpleLightRuntime.Instance?.Unregister(Part);
		public override string Describe()
		{
			var source = BpleLightRuntime.Instance?.Find(Part);
			return "pointlight#" + Info.PartIndex + " enabled=" + Enabled + " entity=" + (source?.EntityEnabled ?? false)
				+ " type=" + (source?.Shape.Type ?? -1) + " length=" + (source?.Shape.Length ?? 0);
		}
	}

	public sealed class SpotLightBehavior : PartBehavior
	{
		private float3 m_current;
		public float3 ConeScale => m_current;
		public override void Init()
		{
			Enabled = false; m_current = float3.zero;
			BpleLightRuntime.Ensure().Register(Part, Info);
			PartGameplayUtil.SetSpotLightCone(Part, m_current);
		}
		public override void Trigger()
		{
			Enabled = !Enabled;
			BpleLightRuntime.Instance.SetActivation(Part, Enabled);
		}
		public override void Tick(float dt)
		{
			// With BPLE's light system, an assembled lamp emits an entity light, not the legacy cone sprite.
			float3 target = BpleLightRuntime.LightSystem && PartVariantPhysics.IsAssembly(Part)
				? float3.zero : new float3(Enabled ? 3f : .001f, 1, 1);
			float3 next = math.all(target == float3.zero) ? float3.zero : math.lerp(m_current, target, math.saturate(dt * 30));
			if (math.all(m_current == next)) return;
			m_current = next;
			PartGameplayUtil.SetSpotLightCone(Part, m_current);
		}
		public override void Dispose() => BpleLightRuntime.Instance?.Unregister(Part);
		public override string Describe() => "spotlight#" + Info.PartIndex + " enabled=" + Enabled + " cone=" + m_current;
	}
}
