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
	/// Batch-mode driver for <see cref="T:Bpsim.QA.FreeTransformQA" />.
	///
	///   Unity.exe -batchmode -projectPath bpsim-main \
	///             -executeMethod Bpsim.EditorTools.FreeTransformQaEditor.RunBatch
	///
	/// (no <c>-quit</c>: this method calls <see cref="M:UnityEditor.EditorApplication.Exit(System.Int32)" /> itself).
	/// Exit code 0 = all checks passed, 1 = failures, 2 = timeout, 3 = play mode never started.
	/// The report is written to &lt;project&gt;/Logs/bpsim-freetransformqa.log.
	///
	/// The play-mode jump forces a domain reload which clears statics, so the driver state
	/// lives in <see cref="T:UnityEditor.SessionState" /> and is re-armed from
	/// <c>[InitializeOnLoadMethod]</c> — same approach as <see cref="T:Bpsim.EditorTools.BuildToolbarQaEditor" />.
	/// </summary>
	public static class FreeTransformQaEditor
	{
		private const string ScenePath = "Assets/Scenes/MainScene.unity";

		private const string RunningKey = "FreeTransformQA.Running";

		private const string DoneKey = "FreeTransformQA.Done";

		private const string StartKey = "FreeTransformQA.StartTime";

		private const string StartedKey = "FreeTransformQA.Started";

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
			Debug.Log("[FreeTransformQA] entering play mode from " + ScenePath);
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
			Debug.Log("[FreeTransformQA] driver re-armed after domain reload (isPlaying=" + EditorApplication.isPlaying + ")");
		}

		private static void OnPlayModeStateChanged(PlayModeStateChange state)
		{
			Debug.Log("[FreeTransformQA] play mode state: " + state);
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
				Debug.Log("[FreeTransformQA] play mode reached, starting checks");
				FreeTransformQA.Run();
				return;
			}
			string status = FreeTransformQA.Status();
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
			string logPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "bpsim-freetransformqa.log"));
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(logPath));
				File.WriteAllText(logPath, status + "\n");
			}
			catch (Exception exception)
			{
				Debug.LogWarning("[FreeTransformQA] 写报告失败: " + exception.Message);
			}
			Debug.Log("[FreeTransformQA] === batch finished (" + exitCode + ") ===\n" + status);
			EditorApplication.Exit(exitCode);
		}
	}
}
#endif
