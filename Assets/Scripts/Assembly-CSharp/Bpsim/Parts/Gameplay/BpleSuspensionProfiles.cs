using System;
using UnityEngine;

namespace Bpsim.Parts.Gameplay
{
	[Serializable]
	internal sealed class SuspensionProfile
	{
		public string family;
		public int index, count;
		public float mass, force, maxSpeed;
		public bool ghost;
	}
	[Serializable]
	internal sealed class SuspensionSettings
	{
		public float balloonForce;
		public int balloonConnectionDistance, sandbagConnectionDistance;
		public bool rotatableBalloon, unpoppableBalloon, rotatableSandbag;
	}
	[Serializable]
	internal sealed class BpleSuspensionProfiles
	{
		public SuspensionProfile[] balloons, sandbags;
		public SuspensionSettings settings;
		private static BpleSuspensionProfiles s_data;
		private static BpleSuspensionProfiles Data
		{
			get
			{
				if (s_data != null) return s_data;
				var asset = Resources.Load<TextAsset>("Bpsim/BpleSuspensionProfiles");
				if (asset == null) throw new InvalidOperationException("Missing BPLE suspension profiles");
				return s_data = JsonUtility.FromJson<BpleSuspensionProfiles>(asset.text);
			}
		}
		internal static SuspensionSettings Settings => Data.settings;
		internal static SuspensionProfile Get(PartTypeInfo info)
		{
			info = PartVariants.Resolve(info);
			string family = info.PartType.ToString();
			var list = Simulation.SuspensionRuntime.IsBalloon(info.PartType) ? Data.balloons : Data.sandbags;
			foreach (var profile in list)
				if (profile.family == family && profile.index == info.PartIndex) return profile;
			throw new ArgumentOutOfRangeException(nameof(info), "No BPLE suspension profile for " + info);
		}
	}
}
