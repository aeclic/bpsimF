#if UNITY_EDITOR
using System;
using System.IO;
using Bpsim.QA;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Bpsim.EditorTools
{
	/// <summary>
	/// Batch driver for <see cref="T:Bpsim.QA.LayerConnectionQA" />.
	///
	///   Unity.exe -batchmode -projectPath bpsim-main \
	///             -executeMethod Bpsim.EditorTools.LayerConnectionQaEditor.RunBatch
	///
	/// (no <c>-quit</c>: this method calls <see cref="M:UnityEditor.EditorApplication.Exit(System.Int32)" /> itself).
	/// Exit code 0 = all checks passed, 1 = failures, 2 = timeout, 3 = play mode never started.
	/// The report is written to &lt;project&gt;/Logs/bpsim-layerconnectionqa.log.
	/// </summary>
	public static class LayerConnectionQaEditor
	{
		private const string ScenePath = "Assets/Scenes/MainScene.unity";

		private const string RunningKey = "LayerConnectionQA.Running";

		private const string DoneKey = "LayerConnectionQA.Done";

		private const string StartKey = "LayerConnectionQA.StartTime";

		private const string StartedKey = "LayerConnectionQA.Started";

		private const double PlayModeTimeout = 180.0;

		private const double TotalTimeout = 300.0;

		public static void RunBatch()
		{
			SessionState.SetBool(DoneKey, false);
			SessionState.SetBool(StartedKey, false);
			SessionState.SetBool(RunningKey, true);
			SessionState.SetString(StartKey, EditorApplication.timeSinceStartup.ToString("R"));
			PlayerSettings.runInBackground = true;
			EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
			EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
			EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
			Debug.Log("[LayerConnectionQA] entering play mode from " + ScenePath);
			EditorApplication.EnterPlaymode();
		}

		[InitializeOnLoadMethod]
		private static void Rearm()
		{
			if (!Application.isBatchMode || !SessionState.GetBool(RunningKey, false) || SessionState.GetBool(DoneKey, false))
			{
				return;
			}
			EditorApplication.update -= Tick;
			EditorApplication.update += Tick;
			EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
			EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
			Debug.Log("[LayerConnectionQA] driver re-armed after domain reload (isPlaying=" + EditorApplication.isPlaying + ")");
		}

		private static void OnPlayModeStateChanged(PlayModeStateChange state)
		{
			Debug.Log("[LayerConnectionQA] play mode state: " + state);
		}

		private static void Tick()
		{
			if (SessionState.GetBool(DoneKey, false))
			{
				EditorApplication.update -= Tick;
				return;
			}
			double start = double.TryParse(SessionState.GetString(StartKey, "0"), out double parsed) ? parsed : EditorApplication.timeSinceStartup;
			double elapsed = EditorApplication.timeSinceStartup - start;
			if (elapsed > TotalTimeout)
			{
				Finish("timeout after " + (int)elapsed + "s", 2);
				return;
			}
			if (!EditorApplication.isPlaying)
			{
				if (elapsed > PlayModeTimeout)
				{
					Finish("play mode never started", 3);
				}
				return;
			}
			if (!SessionState.GetBool(StartedKey, false))
			{
				SessionState.SetBool(StartedKey, true);
				Debug.Log("[LayerConnectionQA] play mode reached, starting checks");
				LayerConnectionQA.Run();
				return;
			}
			string status = LayerConnectionQA.Status();
			if (status.StartsWith("done:PASS"))
			{
				Finish(status, 0);
			}
			else if (status.StartsWith("done:FAIL"))
			{
				Finish(status, 1);
			}
		}

		private static void Finish(string status, int exitCode)
		{
			if (SessionState.GetBool(DoneKey, false))
			{
				return;
			}
			SessionState.SetBool(DoneKey, true);
			SessionState.SetBool(RunningKey, false);
			EditorApplication.update -= Tick;
			EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
			string logPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "bpsim-layerconnectionqa.log"));
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(logPath));
				File.WriteAllText(logPath, status + "\n");
			}
			catch (Exception exception)
			{
				Debug.LogWarning("[LayerConnectionQA] 写报告失败: " + exception.Message);
			}
			Debug.Log("[LayerConnectionQA] === batch finished (" + exitCode + ") ===\n" + status);
			EditorApplication.Exit(exitCode);
		}
	}
}
#endif
