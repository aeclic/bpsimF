using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Bpsim.EditorTools
{
	/// <summary>
	/// 无人值守跑一遍 <see cref="Bpsim.QA.DebugConsoleProbe"/>。
	///
	/// 为什么需要它：这台机器上 DshBridge 经常不可用（主编辑器 + AssetImportWorker 抢同一个
	/// 8791 端口，请求随机落到没有 EditorApplication.update 循环的 worker 上就永远超时），
	/// 所以改由编辑器自己在启动时读一个标志文件，跑完把报告写到 Logs/debugconsole-autorun.txt 再退出。
	///
	/// 关键点：**进 Play Mode 会触发 domain reload，静态字段与 EditorApplication.update 订阅都会丢**。
	/// 所以进度存在 SessionState（它跨 domain reload 存活、编辑器退出即清空），
	/// [InitializeOnLoad] 每次 reload 后都按 SessionState 重新挂钩。
	/// </summary>
	[InitializeOnLoad]
	public static class DebugConsoleAutoRun
	{
		private const string ScenePath = "Assets/Scenes/MainScene.unity";
		private const string StepKey = "DCAutoRun.step";
		private const string LogKey = "DCAutoRun.log";
		private const double SettleSeconds = 20.0;
		private const double StartupTimeoutSeconds = 900.0;
		private const double PlayTimeoutSeconds = 180.0;
		private const double BootTimeoutSeconds = 180.0;
		private const double ReportTimeoutSeconds = 300.0;

		private static readonly string s_projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
		private static readonly string s_flagPath = Path.Combine(s_projectRoot, "Logs/_autorun.flag");
		private static readonly string s_reportPath = Path.Combine(s_projectRoot, "Logs/debugconsole-autorun.txt");
		private static readonly string s_probePath = Path.Combine(s_projectRoot, "Logs/debugconsole-probe.txt");

		private static double s_stepStartedAt;

		static DebugConsoleAutoRun()
		{
			if (File.Exists(s_flagPath))
			{
				File.Delete(s_flagPath);
				SessionState.SetInt(StepKey, 0);
				SessionState.SetString(LogKey, "");
				s_stepStartedAt = EditorApplication.timeSinceStartup;
				Log("autorun armed (editor " + Application.unityVersion + ")");
			}

			if (SessionState.GetInt(StepKey, -1) >= 0)
			{
				s_stepStartedAt = EditorApplication.timeSinceStartup;
				EditorApplication.update -= Tick;
				EditorApplication.update += Tick;
				Log("hook re-armed after domain reload at step " + SessionState.GetInt(StepKey, -1));
			}
		}

		private static void Log(string message)
		{
			string line = "[" + EditorApplication.timeSinceStartup.ToString("F1") + "] " + message;
			SessionState.SetString(LogKey, SessionState.GetString(LogKey, "") + line + "\n");
			Debug.Log("[DCAutoRun] " + message);
		}

		private static void Flush(int exitCode, string note)
		{
			EditorApplication.update -= Tick;
			Log("DONE " + note);
			string text = SessionState.GetString(LogKey, "");
			SessionState.SetInt(StepKey, -1);
			SessionState.SetString(LogKey, "");
			try
			{
				File.WriteAllText(s_reportPath, text, new UTF8Encoding(false));
			}
			catch (Exception exception)
			{
				Debug.LogError("[DCAutoRun] report write failed: " + exception.Message);
			}
			EditorApplication.Exit(exitCode);
		}

		private static bool EditorReady()
		{
			return !EditorApplication.isCompiling
				&& !EditorApplication.isUpdating
				&& !EditorApplication.isPlayingOrWillChangePlaymode;
		}

		private static void Advance(int step)
		{
			SessionState.SetInt(StepKey, step);
			s_stepStartedAt = EditorApplication.timeSinceStartup;
		}

		private static void Tick()
		{
			int step = SessionState.GetInt(StepKey, -1);
			if (step < 0)
			{
				EditorApplication.update -= Tick;
				return;
			}

			double elapsed = EditorApplication.timeSinceStartup - s_stepStartedAt;
			switch (step)
			{
				case 0:
					// 启动会重编译 + 导入资产，先让它安静下来，否则 EnterPlaymode 会被吞掉。
					if (!EditorReady() || elapsed < SettleSeconds)
					{
						if (elapsed > StartupTimeoutSeconds)
						{
							Flush(2, "timeout waiting for the editor to settle");
						}
						return;
					}
					Log("editor settled; opening " + ScenePath);
					EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
					Advance(1);
					return;

				case 1:
					if (!EditorReady() || elapsed < 2.0)
					{
						if (elapsed > StartupTimeoutSeconds)
						{
							Flush(2, "scene never became ready");
						}
						return;
					}
					Log("entering play mode");
					Advance(2);
					EditorApplication.EnterPlaymode();
					return;

				case 2:
					if (!EditorApplication.isPlaying)
					{
						if (elapsed > PlayTimeoutSeconds)
						{
							Flush(3, "play mode never started");
						}
						return;
					}
					Log("playing; waiting for the scene to boot");
					Advance(3);
					return;

				case 3:
					if (!SceneBooted())
					{
						if (elapsed > BootTimeoutSeconds)
						{
							Flush(4, "scene never booted");
						}
						return;
					}
					Log("scene booted; starting the probe");
					try
					{
						if (File.Exists(s_probePath))
						{
							File.Delete(s_probePath);
						}
					}
					catch (Exception exception)
					{
						Log("stale probe report not deleted: " + exception.Message);
					}
					Bpsim.QA.DebugConsoleProbe.Run();
					Advance(4);
					return;

				case 4:
					if (!File.Exists(s_probePath))
					{
						if (elapsed > ReportTimeoutSeconds)
						{
							Log("probe never wrote a report; leaving play mode anyway");
							Advance(5);
							EditorApplication.ExitPlaymode();
						}
						return;
					}
					Log("probe finished; leaving play mode");
					Advance(5);
					EditorApplication.ExitPlaymode();
					return;

				case 5:
					if (EditorApplication.isPlayingOrWillChangePlaymode && elapsed < 180.0)
					{
						return;
					}
					Flush(0, "probe report in " + s_probePath);
					return;
			}
		}

		private static bool SceneBooted()
		{
			if (GameObject.Find("CoreManager") == null)
			{
				return false;
			}
			Bpsim.UserSettings settings = Bpsim.UserSettings.Instance;
			return settings != null && settings.DebugConsole != null;
		}
	}
}
