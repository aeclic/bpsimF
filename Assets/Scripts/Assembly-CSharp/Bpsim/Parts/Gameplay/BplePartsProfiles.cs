using System;
using UnityEngine;

namespace Bpsim.Parts.Gameplay
{
	[Serializable]
	internal sealed class BpleCurveKey
	{
		public float time, value, inSlope, outSlope, inWeight, outWeight;
		public int weightedMode;
	}

	[Serializable]
	internal sealed class BpleLightProfile
	{
		public string kind;
		public int index, tier, entityType, sourceType;
		public float mass, sourceSize, beamAngle, beamCut, borderWidth, flicker, mainSinSpeed, subSinSpeed;
		public float length, halfWidth, angle;
		public BpleCurveKey[] turnOnCurve, turnOffCurve;
		private AnimationCurve m_onCurve, m_offCurve;

		public float Transition(bool enabled, float time)
		{
			if (m_onCurve == null)
			{
				m_onCurve = Curve(turnOnCurve);
				m_offCurve = Curve(turnOffCurve);
			}
			return (enabled ? m_onCurve : m_offCurve).Evaluate(time);
		}

		private static AnimationCurve Curve(BpleCurveKey[] values)
		{
			var keys = new Keyframe[values.Length];
			for (int i = 0; i < values.Length; i++)
			{
				var value = values[i];
				keys[i] = new Keyframe(value.time, value.value, value.inSlope, value.outSlope, value.inWeight, value.outWeight)
					{ weightedMode = (WeightedMode)value.weightedMode };
			}
			return new AnimationCurve(keys);
		}
	}

	[Serializable]
	internal sealed class BpleUmbrellaProfile
	{
		public int index, tier;
		public float mass, force;
	}

	[Serializable]
	internal sealed class BplePartsProfiles
	{
		public BpleLightProfile[] lights;
		public BpleUmbrellaProfile[] umbrellas;
		private static BplePartsProfiles s_profiles;
		private static BplePartsProfiles Data
		{
			get
			{
				if (s_profiles != null) return s_profiles;
				var asset = Resources.Load<TextAsset>("Bpsim/BplePartsProfiles");
				if (asset == null) throw new InvalidOperationException("Missing BPLE part profiles resource");
				return s_profiles = JsonUtility.FromJson<BplePartsProfiles>(asset.text);
			}
		}

		public static BpleLightProfile Light(PartTypeInfo info)
		{
			info = PartVariants.Resolve(info);
			string kind = info.PartType == PartType.SpotLight ? "SpotLight" : "PointLight";
			foreach (var profile in Data.lights)
				if (profile.kind == kind && profile.index == info.PartIndex) return profile;
			throw new ArgumentOutOfRangeException(nameof(info), "No BPLE light profile for " + info);
		}

		public static BpleUmbrellaProfile Umbrella(int index)
		{
			foreach (var profile in Data.umbrellas) if (profile.index == index) return profile;
			return Data.umbrellas[0];
		}
	}
}
