using UnityEngine;

namespace Bpsim.Parts.Gameplay
{
	internal static class BplePartAudio
	{
		private static AudioSource s_source;
		public static void Play(string name)
		{
			AudioClip clip = Resources.Load<AudioClip>("Bpsim/" + name);
			if (clip == null || PartGameplayManager.Instance == null) return;
			if (s_source == null)
			{
				s_source = PartGameplayManager.Instance.gameObject.AddComponent<AudioSource>();
				s_source.playOnAwake = false;
				s_source.spatialBlend = 0;
			}
			s_source.PlayOneShot(clip);
		}
	}
}
