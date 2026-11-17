using Unity.Entities;
using Unity.Mathematics;

namespace Bpsim.Parts
{
	/// <summary>Simulation state only. Input requests a cycle; BellowsSystem owns time and effects.</summary>
	public struct Bellows : IComponentData
	{
		public bool Initialized;
		public bool TriggerRequest;
		public bool Enabled;
		public bool Alien;
		public float Elapsed;
		public float Force;
		public float Compression;
		public float DeliveredImpulse;
		public int Cycles;
		public float3x3 OriginalVisualScale;

		public static Bellows Create(PartTypeInfo info) => new Bellows
		{
			Force = info.PartIndex == 6 ? 120f : 30f, Compression = 1f, Alien = info.PartIndex == 6
		};
	}

	/// <summary>APK BellowsForce/CompressionScale contract, independent of Unity frame timing.</summary>
	public static class BellowsMath
	{
		public const float BoostDuration = .5f;
		public const float WaitDuration = .3f;
		public static float InflateDuration(bool alien) => alien ? .15f : .3f;
		public static float Cooldown(bool alien) => BoostDuration + WaitDuration + InflateDuration(alien);
		public static float Force(float time, float force)
		{
			if (time < 0f || time >= BoostDuration) return 0f;
			float remainder = 1f - 2f * time;
			return force * (1f - remainder * remainder);
		}
		public static float Compression(float time, bool alien)
		{
			float amount = time < BoostDuration ? time / BoostDuration
				: time < BoostDuration + WaitDuration ? 1f
				: 1f - (time - BoostDuration - WaitDuration) / InflateDuration(alien);
			return math.lerp(1f, .3f, math.saturate(amount));
		}
		// Integrating the verified force curve clips the final boost step and avoids
		// frame-rate-dependent total impulse when the physics timestep is changed.
		public static float Impulse(float start, float end, float force)
		{
			start = math.clamp(start, 0f, BoostDuration);
			end = math.clamp(end, start, BoostDuration);
			return force * (Primitive(end) - Primitive(start));
		}
		private static float Primitive(float t) => 2f * t * t - (4f / 3f) * t * t * t;
	}
}
