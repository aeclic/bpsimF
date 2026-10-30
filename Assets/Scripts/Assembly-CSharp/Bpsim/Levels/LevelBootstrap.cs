using UnityEngine;

namespace Bpsim.Levels
{
	/// <summary>
	/// Brings the level-import feature up without touching <c>MainScene</c> or the
	/// hand-authored UI prefabs: it creates the <see cref="LevelManager"/> holder and the
	/// <see cref="LevelMenu"/> overlay once the first scene has finished loading.
	/// </summary>
	internal static class LevelBootstrap
	{
		public const string RootName = "BpsimLevels";

		private static bool s_booted;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Boot()
		{
			if (s_booted)
			{
				return;
			}
			s_booted = true;
			if (GameObject.Find(RootName) != null)
			{
				return;
			}
			if (!LevelCatalog.IsAvailable)
			{
				Debug.LogWarning("[BpsimLevels] StreamingAssets/BpsimLevels is missing; the level importer stays disabled. Run Tools/BPSim/export_levels.py --write.");
				return;
			}
			GameObject root = new GameObject(RootName);
			root.AddComponent<LevelManager>();
			LevelMenu.Create(root.transform);
			Debug.Log($"[BpsimLevels] ready: {LevelCatalog.Levels.Count} BPLE levels available");
		}
	}
}
