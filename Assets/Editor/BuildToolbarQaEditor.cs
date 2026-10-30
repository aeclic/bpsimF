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
	/// Batch-mode driver for <see cref="T:Bpsim.QA.BuildToolbarQA" />.
	///
	/// The toolbar is runtime uGUI that only exists after <c>UserInterface.Awake()</c>, so the
	/// checks can only run in play mode. This entry point does the whole dance headlessly:
	///
	///   Unity.exe -batchmode -projectPath bpsim-main \
	///             -executeMethod Bpsim.EditorTools.BuildToolbarQaEditor.RunBatch
	///
	/// (no <c>-quit</c>: the method calls <see cref="M:UnityEditor.EditorApplication.Exit(System.Int32)" /> itself).
	/// Exit code 0 = all checks passed, 1 = failures, 2 = timeout, 3 = play mode never started.
	/// The report is written to &lt;project&gt;/Logs/bpsim-toolbarqa.log.
	///
	/// WHY THE SESSION-STATE DANCE
	/// ---------------------------
	/// Entering play mode forces a domain reload, which clears every static field *and* every
	/// subscription made on <c>EditorApplication.update</c>. A driver that simply registers a
	/// callback before <c>EnterPlaymode()</c> therefore goes silent the moment play mode starts -
	/// which is exactly how the first attempt hung forever with no "play mode reached" line.
	/// So the run is driven by flags in <see cref="T:UnityEditor.SessionState" /> and re-armed from
	/// <c>[InitializeOnLoadMethod]</c> after every reload.
	/// </summary>
	public static class BuildToolbarQaEditor
	{
		private const string ScenePath = "Assets/Scenes/MainScene.unity";

		private const string RunningKey = "BuildToolbarQA.Running";

		private const string DoneKey = "BuildToolbarQA.Done";

		private const string StartKey = "BuildToolbarQA.StartTime";

		private const double PlayModeTimeout = 180.0;

		private const double TotalTimeout = 300.0;

		public static void RunBatch()
		{
			SessionState.SetBool(DoneKey, false);
			SessionState.SetBool("BuildToolbarQA.Started", false);
			SessionState.SetBool(RunningKey, true);
			SessionState.SetString(StartKey, EditorApplication.timeSinceStartup.ToString("R"));
			PlayerSettings.runInBackground = true;
			EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
			EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
			EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
			Debug.Log("[BuildToolbarQA] entering play mode from " + ScenePath);
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
			Debug.Log("[BuildToolbarQA] driver re-armed after domain reload (isPlaying=" + EditorApplication.isPlaying + ")");
		}

		private static void OnPlayModeStateChanged(PlayModeStateChange state)
		{
			Debug.Log("[BuildToolbarQA] play mode state: " + state);
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
			if (!SessionState.GetBool("BuildToolbarQA.Started", false))
			{
				SessionState.SetBool("BuildToolbarQA.Started", true);
				Debug.Log("[BuildToolbarQA] play mode reached, starting checks");
				BuildToolbarQA.Run();
				return;
			}
			string status = BuildToolbarQA.Status();
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
			string logPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "bpsim-toolbarqa.log"));
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(logPath));
				File.WriteAllText(logPath, status + "\n");
			}
			catch (Exception exception)
			{
				Debug.LogWarning("[BuildToolbarQA] 写报告失败: " + exception.Message);
			}
			Debug.Log("[BuildToolbarQA] === batch finished (" + exitCode + ") ===\n" + status);
			EditorApplication.Exit(exitCode);
		}
	}
}
#endif
