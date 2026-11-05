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
	/// Batch driver for <see cref="T:Bpsim.QA.BpirImportQA" />.
	///
	///   Unity.exe -batchmode -projectPath bpsim-main \
	///             -executeMethod Bpsim.EditorTools.BpirImportQaEditor.RunBatch
	///
	/// (no <c>-quit</c>: this method calls <see cref="M:UnityEditor.EditorApplication.Exit(System.Int32)" /> itself).
	/// Exit code 0 = all checks passed, 1 = failures, 2 = timeout, 3 = play mode never started.
	/// The report is written to &lt;project&gt;/Logs/bpsim-bpirimportqa.log (by the QA itself) and
	/// &lt;project&gt;/Logs/bpsim-bpirimportqa-driver.log (by this driver).
	/// </summary>
	public static class BpirImportQaEditor
	{
		private const string ScenePath = "Assets/Scenes/MainScene.unity";

		private const string RunningKey = "BpirImportQA.Running";

		private const string DoneKey = "BpirImportQA.Done";

		private const string StartKey = "BpirImportQA.StartTime";

		private const string StartedKey = "BpirImportQA.Started";

		// 解析 286 件 + 五个受控场景，每个场景都要真的进一次模拟再等 1 秒，比
		// LayerConnectionQA 的四个场景略长，超时给宽一点。
		private const double PlayModeTimeout = 180.0;

		private const double TotalTimeout = 420.0;

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
			Debug.Log("[BpirImportQA] entering play mode from " + ScenePath);
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
			Debug.Log("[BpirImportQA] driver re-armed after domain reload (isPlaying=" + EditorApplication.isPlaying + ")");
		}

		private static void OnPlayModeStateChanged(PlayModeStateChange state)
		{
			Debug.Log("[BpirImportQA] play mode state: " + state);
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
				Debug.Log("[BpirImportQA] play mode reached, starting checks");
				BpirImportQA.Run();
				return;
			}
			string status = BpirImportQA.Status();
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
			// QA 自己会把完整报告写到 bpsim-bpirimportqa.log；这里再落一份驱动摘要，
			// 免得「QA 没跑到 Finish」时没有任何产物可看。
			string logPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "bpsim-bpirimportqa-driver.log"));
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(logPath));
				File.WriteAllText(logPath, status + "\n");
			}
			catch (Exception exception)
			{
				Debug.LogWarning("[BpirImportQA] 写驱动摘要失败: " + exception.Message);
			}
			Debug.Log("[BpirImportQA] === batch finished (" + exitCode + ") ===\n" + status);
			EditorApplication.Exit(exitCode);
		}
	}
}
#endif
