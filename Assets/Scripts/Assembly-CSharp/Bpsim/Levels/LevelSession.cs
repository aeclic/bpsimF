using System;
using System.Collections;
using System.IO;
using Bpsim.Parts;
using UnityEngine;

namespace Bpsim.Levels
{
	/// <summary>
	/// Remembers what the player had open when the application was closed and puts it back
	/// on the next launch: the BPLE level imported through the 关卡 picker and the
	/// schematics file loaded through the 加载场景 dialog.
	/// </summary>
	/// <remarks>
	/// The two are tracked separately because they are independent features: 关卡 imports a
	/// whole BPLE level (terrain + the level's own starter contraption) while 加载场景 loads
	/// an arbitrary .txt/.json vehicle blueprint. A player who does both ends up with the
	/// level's contraption scene plus their own loaded scene, so the restore has to replay
	/// both steps in the same order to reproduce the same scene list.
	/// </remarks>
	internal static class LevelSession
	{
		private const string LevelKey = "Bpsim.Session.Level";

		private const string ScenePathKey = "Bpsim.Session.ScenePath";

		private const string SceneFormatKey = "Bpsim.Session.SceneFormat";

		/// <summary>The scene name of the BPLE level that was imported last, or empty.</summary>
		public static string LevelScene => PlayerPrefs.GetString(LevelKey, string.Empty);

		/// <summary>
		/// Set to false by automated QA runs (which build their own scenes) so the recorded
		/// session is not replayed on top of them.
		/// </summary>
		public static bool AutoRestore { get; set; } = true;

		/// <summary>Path of the schematics file loaded last through 加载场景, or empty.</summary>
		public static string ScenePath => PlayerPrefs.GetString(ScenePathKey, string.Empty);

		public static SchematicsFormat SceneFormat =>
			(SchematicsFormat)PlayerPrefs.GetInt(SceneFormatKey, (int)SchematicsFormat.Xml);

		public static bool HasLevel => !string.IsNullOrEmpty(LevelScene);

		public static bool HasScene => !string.IsNullOrEmpty(ScenePath);

		public static void RecordLevel(string scene)
		{
			if (string.IsNullOrEmpty(scene))
			{
				PlayerPrefs.DeleteKey(LevelKey);
			}
			else
			{
				PlayerPrefs.SetString(LevelKey, scene);
			}
			PlayerPrefs.Save();
		}

		public static void ClearLevel()
		{
			PlayerPrefs.DeleteKey(LevelKey);
			PlayerPrefs.Save();
		}

		public static void RecordScene(string path, SchematicsFormat format)
		{
			if (string.IsNullOrEmpty(path))
			{
				ClearScene();
				return;
			}
			PlayerPrefs.SetString(ScenePathKey, path);
			PlayerPrefs.SetInt(SceneFormatKey, (int)format);
			PlayerPrefs.Save();
		}

		public static void ClearScene()
		{
			PlayerPrefs.DeleteKey(ScenePathKey);
			PlayerPrefs.DeleteKey(SceneFormatKey);
			PlayerPrefs.Save();
		}

		public static void Forget()
		{
			PlayerPrefs.DeleteKey(LevelKey);
			PlayerPrefs.DeleteKey(ScenePathKey);
			PlayerPrefs.DeleteKey(SceneFormatKey);
			PlayerPrefs.Save();
		}

		/// <summary>
		/// Replays the recorded session. Safe to call when nothing was recorded; the caller
		/// is expected to have waited for <see cref="PartManager"/> to finish its Start().
		/// </summary>
		/// <returns>A one line report, also written to the console.</returns>
		public static string Restore()
		{
			string levelScene = LevelScene;
			string scenePath = ScenePath;
			SchematicsFormat format = SceneFormat;
			if (string.IsNullOrEmpty(levelScene) && string.IsNullOrEmpty(scenePath))
			{
				return "nothing to restore";
			}

			bool hasScene = !string.IsNullOrEmpty(scenePath) && File.Exists(scenePath);
			if (!hasScene && !string.IsNullOrEmpty(scenePath))
			{
				Debug.LogWarning($"[BpsimLevels] 上次加载的场景文件已不存在：{scenePath}");
				ClearScene();
			}

			string report = "restore:";
			if (!string.IsNullOrEmpty(levelScene))
			{
				LevelManager manager = LevelManager.Instance;
				LevelEntryJson entry = LevelCatalog.FindLevel(levelScene);
				if (manager == null)
				{
					report += " no LevelManager";
				}
				else if (entry == null)
				{
					report += $" level '{levelScene}' not found";
					ClearLevel();
				}
				else if (manager.Load(entry, out string error))
				{
					report += $" level '{entry.scene}'";
				}
				else
				{
					report += $" level '{levelScene}' failed: {error}";
				}
			}

			if (hasScene)
			{
				if (LoadSceneFile(scenePath, format, out string error))
				{
					report += $" scene '{Path.GetFileNameWithoutExtension(scenePath)}'";
				}
				else
				{
					report += $" scene failed: {error}";
					ClearScene();
					// A level without a contraption leaves no active scene behind, and the
					// builder needs one; ask for an empty scene the same way the dialog's
					// blank path does.
					if (!string.IsNullOrEmpty(levelScene) && PartManager.Instance != null && !PartManager.Instance.HasActiveScene())
					{
						PartManager.Instance.LoadEmptyScene("Empty");
					}
				}
			}

			Debug.Log("[BpsimLevels] " + report);
			return report;
		}

		/// <summary>
		/// Loads a schematics file into a fresh <see cref="PartScene"/> and selects it, the
		/// way the scene tab view does after the dialog has loaded one.
		/// </summary>
		public static bool LoadSceneFile(string path, SchematicsFormat format, out string error)
		{
			error = null;
			PartManager partManager = PartManager.Instance;
			if (partManager == null)
			{
				error = "PartManager 尚未就绪";
				return false;
			}

			PartScene created = null;
			Action<PartScene> handler = delegate(PartScene scene)
			{
				created = scene;
			};
			partManager.SceneLoaded += handler;
			try
			{
				using Stream stream = FileAPI.ReadFileAsStream(path);
				partManager.LoadScene(stream, Path.GetFileNameWithoutExtension(path), format);
			}
			catch (Exception exception)
			{
				error = exception.Message;
				return false;
			}
			finally
			{
				partManager.SceneLoaded -= handler;
			}

			if (created == null)
			{
				error = "场景没有创建";
				return false;
			}

			partManager.SelectScene(created.SceneID);
			return true;
		}

		/// <summary>
		/// One frame delay wrapper so the bootstrap can hand control back to Unity before
		/// touching the ECS world.
		/// </summary>
		public static IEnumerator RestoreNextFrame()
		{
			yield return null;
			Restore();
		}
	}
}
