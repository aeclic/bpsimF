using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Bpsim.UI;
using IngameDebugConsole;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.QA
{
	/// <summary>
	/// Self-check for the ported IngameDebugConsole wiring. Run it from play mode:
	///   DshBridge: invoke_static {"type":"Bpsim.QA.DebugConsoleProbe","method":"Run"}
	/// The report is written to Logs/debugconsole-probe.txt so it can be read even when
	/// the editor window is not in front of us.
	/// </summary>
	public static class DebugConsoleProbe
	{
		private static readonly List<string> s_lines = new List<string>();
		private static bool s_running;
		private static int s_failures;

		public static void Run()
		{
			if (s_running)
			{
				Debug.LogWarning("[DCProbe] already running");
				return;
			}
			s_running = true;
			s_lines.Clear();
			s_failures = 0;

			GameObject host = new GameObject("[DCProbe]");
			UnityEngine.Object.DontDestroyOnLoad(host);
			host.AddComponent<Driver>();
		}

		public static string Report()
		{
			return string.Join("\n", s_lines.ToArray());
		}

		private static void Line(string text)
		{
			s_lines.Add(text);
			Debug.Log("[DCProbe] " + text);
		}

		private static void Check(string what, bool ok, string detail)
		{
			if (!ok)
			{
				s_failures++;
			}
			Line((ok ? "PASS " : "FAIL ") + what + (string.IsNullOrEmpty(detail) ? "" : " :: " + detail));
		}

		private static string Describe()
		{
			return DebugConsoleHost.Describe();
		}

		private static int CountManagers()
		{
			return UnityEngine.Object.FindObjectsByType<DebugLogManager>(FindObjectsSortMode.None).Length;
		}

		private sealed class Driver : MonoBehaviour
		{
			// 相位的异常必须就地捕获，同时又不能用 try/catch 包住 yield（CS1626），
			// 所以这里手工驱动子协程：try 只包 MoveNext，yield 留在 try 之外。
			private IEnumerator Start()
			{
				string path = Path.Combine(Application.dataPath, "../Logs/debugconsole-probe.txt");
				IEnumerator[] phases =
				{
					PhaseStartup(),
					PhaseEnable(),
					PhaseLogs(),
					PhaseDisable(),
					PhaseReenable(),
					PhaseVisual(),
				};
				for (int i = 0; i < phases.Length; i++)
				{
					bool completed = false;
					while (!completed)
					{
						object current = null;
						bool moved = false;
						try
						{
							moved = phases[i].MoveNext();
							if (moved)
							{
								current = phases[i].Current;
							}
						}
						catch (Exception exception)
						{
							s_failures++;
							Line("EXCEPTION in phase " + i + ": " + exception);
							moved = false;
						}
						if (!moved)
						{
							completed = true;
							continue;
						}
						yield return current;
					}
				}

				Line("FAILURES: " + s_failures);
				s_running = false;
				try
				{
					Directory.CreateDirectory(Path.GetDirectoryName(path));
					File.WriteAllText(path, string.Join("\n", s_lines.ToArray()) + "\n", new UTF8Encoding(false));
				}
				catch (Exception exception)
				{
					Debug.LogError("[DCProbe] write failed: " + exception.Message);
				}
				UnityEngine.Object.Destroy(gameObject);
			}

			private IEnumerator PhaseStartup()
			{
				yield return null;
				Line("=== startup ===");
				Line("describe " + Describe());
				bool loaded = UserSettings.Instance != null && UserSettings.Instance.DebugConsole != null;
				Check("UserSettings.Instance.DebugConsole exists", loaded, "");
				bool expected = loaded && UserSettings.Instance.DebugConsole.Enabled;
				Check("console presence matches setting on startup", DebugConsoleHost.Exists == expected, "exists=" + DebugConsoleHost.Exists + " setting=" + expected);
				Check("no stray DebugLogManager", CountManagers() == (expected ? 1 : 0), "count=" + CountManagers());
			}

			private IEnumerator PhaseEnable()
			{
				if (UserSettings.Instance == null || UserSettings.Instance.DebugConsole == null)
				{
					Line("=== enable skipped (no settings) ===");
					yield break;
				}
				Line("=== enable ===");
				UserSettings.Instance.DebugConsole.Enabled = true;
				yield return null;
				yield return null;
				Line("describe " + Describe());
				Check("manager created by the setting change", DebugConsoleHost.Exists, Describe());
				Check("exactly one DebugLogManager", CountManagers() == 1, "count=" + CountManagers());
				Check("log window starts closed", !DebugConsoleHost.IsOpen, "");
				Check("floating popup starts visible", DebugConsoleHost.PopupEnabled, "");
				GameObject root = GameObject.Find("INDebugConsole");
				Check("root object is named INDebugConsole", root != null, "");
				if (root != null)
				{
					Canvas canvas = root.GetComponent<Canvas>();
					Check("root has an overlay canvas", canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay, canvas == null ? "no canvas" : canvas.renderMode.ToString());
					int texts = root.GetComponentsInChildren<TextMeshProUGUI>(true).Length;
					int legacy = root.GetComponentsInChildren<Text>(true).Length;
					Check("TMP text components present", texts > 0, "tmp=" + texts + " legacy=" + legacy);
					DebugLogManager manager = root.GetComponent<DebugLogManager>();
					Check("DebugLogManager lives on the root", manager != null, "");
					TMP_FontAsset font = TMP_Settings.defaultFontAsset;
					Check("TMP default font asset resolves", font != null, font == null ? "null" : font.name);
					if (texts > 0)
					{
						TextMeshProUGUI sample = root.GetComponentsInChildren<TextMeshProUGUI>(true)[0];
						Check("first TMP text has a font", sample.font != null, sample.font == null ? "null" : sample.font.name);
						Check("first TMP text has a material", sample.fontSharedMaterial != null, sample.fontSharedMaterial == null ? "null" : sample.fontSharedMaterial.name);
						if (sample.fontSharedMaterial != null)
						{
							Shader shader = sample.fontSharedMaterial.shader;
							Check("font material shader is a TMP shader", shader != null && shader.name.Contains("TextMeshPro"), shader == null ? "null" : shader.name);
						}
					}
				}
			}

			private IEnumerator PhaseLogs()
			{
				Line("=== logs ===");
				if (!DebugConsoleHost.Exists)
				{
					Line("skipped (no manager)");
					yield break;
				}
				DebugConsoleHost.Show();
				yield return null;
				Check("Show() opens the log window", DebugConsoleHost.IsOpen, Describe());
				Debug.LogWarning("[DCProbe] sample warning 12345");
				Debug.LogError("[DCProbe] sample error 67890");
				for (int i = 0; i < 10; i++)
				{
					yield return null;
				}
				string logs = DebugLogManager.Instance.GetAllLogs();
				Check("warning captured", logs.Contains("sample warning 12345"), logs.Length.ToString() + " chars");
				Check("error captured", logs.Contains("sample error 67890"), "");
				DebugConsoleHost.Hide();
				yield return null;
				Check("Hide() closes the log window", !DebugConsoleHost.IsOpen, Describe());
			}

			private IEnumerator PhaseDisable()
			{
				if (UserSettings.Instance == null || UserSettings.Instance.DebugConsole == null)
				{
					yield break;
				}
				Line("=== disable ===");
				UserSettings.Instance.DebugConsole.Enabled = false;
				yield return null;
				yield return null;
				Line("describe " + Describe());
				Check("manager destroyed by the setting change", !DebugConsoleHost.Exists, Describe());
				Check("no DebugLogManager left behind", CountManagers() == 0, "count=" + CountManagers());
				Check("Static Instance cleared", DebugLogManager.Instance == null, "");
				Check("console root removed from the scene", GameObject.Find("INDebugConsole") == null, "");
			}

			private IEnumerator PhaseReenable()
			{
				if (UserSettings.Instance == null || UserSettings.Instance.DebugConsole == null)
				{
					yield break;
				}
				Line("=== re-enable ===");
				UserSettings.Instance.DebugConsole.Enabled = true;
				yield return null;
				yield return null;
				Check("manager recreated", DebugConsoleHost.Exists, Describe());
				Check("still exactly one manager", CountManagers() == 1, "count=" + CountManagers());
				UserSettings.Instance.DebugConsole.Enabled = false;
				yield return null;
				yield return null;
				Check("back to zero managers", CountManagers() == 0, "count=" + CountManagers());
			}

			/// <summary>
			/// 两个「只能看图」的验收点：
			/// ① 控制台窗口里的 TMP 文字在 URP 2D 下要真的画出来（不是洋红/不透明块）；
			/// ② 设置面板里要有我们加的那一行开关。
			/// </summary>
			private IEnumerator PhaseVisual()
			{
				Line("=== visual ===");
				if (UserSettings.Instance == null || UserSettings.Instance.DebugConsole == null)
				{
					Line("skipped (no settings)");
					yield break;
				}

				UserSettings.Instance.DebugConsole.Enabled = true;
				yield return null;
				yield return null;
				DebugConsoleHost.Show();
				Debug.LogWarning("[DCProbe] visual sample warning");
				Debug.LogError("[DCProbe] visual sample error");
				for (int i = 0; i < 20; i++)
				{
					yield return null;
				}

				yield return Capture("debugconsole-window.png", "console window");

				// 设置面板：UserInterface 是 internal，OpenSettings 是私有方法，只能反射进去。
				object ui = UserInterface.Instance;
				Check("UserInterface.Instance exists", ui != null, "");
				if (ui != null)
				{
					object actionBar = Field(ui, "m_actionBar");
					Check("action bar found", actionBar != null, "");
					if (actionBar != null)
					{
						System.Reflection.MethodInfo open = actionBar.GetType().GetMethod(
							"OpenSettings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
						Check("OpenSettings method found", open != null, "");
						if (open != null)
						{
							open.Invoke(actionBar, null);
						}
					}
				}
				for (int i = 0; i < 10; i++)
				{
					yield return null;
				}
				Bpsim.UI.UserSettingsPanel panel = UnityEngine.Object.FindObjectOfType<Bpsim.UI.UserSettingsPanel>();
				Check("settings panel opened", panel != null, "");
				if (panel != null)
				{
					Transform content = Field(panel, "m_content") as Transform;
					if (content == null && Field(panel, "m_content") is GameObject contentObject)
					{
						content = contentObject.transform;
					}
					Check("settings panel content found", content != null, "");
					if (content != null)
					{
						bool header = false;
						bool toggleRow = false;
						for (int i = 0; i < content.childCount; i++)
						{
							Transform child = content.GetChild(i);
							Text name = child.Find("Name") != null ? child.Find("Name").GetComponent<Text>() : null;
							if (name == null)
							{
								continue;
							}
							if (name.text == "调试")
							{
								header = true;
							}
							if (name.text.Contains("debug 控制台"))
							{
								Transform value = child.Find("Value");
								toggleRow = value != null && value.GetComponent<ToggleSwitch>() != null;
								Line("row text: " + name.text);
							}
						}
						Check("settings panel has the 调试 header", header, "rows=" + content.childCount);
						Check("settings panel has the debug console toggle", toggleRow, "");
						// 让截图能看见我们加的那一行：先收起控制台窗口，再把列表滚到底。
						DebugConsoleHost.Hide();
						ScrollRect scroll = panel.GetComponentInChildren<ScrollRect>(true);
						Check("settings panel has a ScrollRect", scroll != null, "");
						if (scroll != null)
						{
							scroll.verticalNormalizedPosition = 0f;
						}
						for (int i = 0; i < 10; i++)
						{
							yield return null;
						}
						if (scroll != null)
						{
							scroll.verticalNormalizedPosition = 0f;
						}
						yield return Capture("debugconsole-settings.png", "settings panel");
					}
				}

				DebugConsoleHost.Hide();
				UserSettings.Instance.DebugConsole.Enabled = false;
				yield return null;
			}

			private static object Field(object target, string name)
			{
				if (target == null)
				{
					return null;
				}
				System.Reflection.FieldInfo field = target.GetType().GetField(
					name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
				return field == null ? null : field.GetValue(target);
			}

			/// <summary>抓一帧并顺势做一次洋红检测——URP 下 shader 不兼容时整块会变成 (255,0,255)。</summary>
			private IEnumerator Capture(string fileName, string what)
			{
				yield return new WaitForEndOfFrame();
				Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
				try
				{
					string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", fileName));
					Directory.CreateDirectory(Path.GetDirectoryName(path));
					File.WriteAllBytes(path, shot.EncodeToPNG());
					Color32[] pixels = shot.GetPixels32();
					int magenta = 0;
					for (int i = 0; i < pixels.Length; i++)
					{
						Color32 pixel = pixels[i];
						if (pixel.r > 200 && pixel.b > 200 && pixel.g < 80)
						{
							magenta++;
						}
					}
					double ratio = pixels.Length == 0 ? 0.0 : (double)magenta / pixels.Length;
					Check(what + " has no magenta pixels", ratio < 0.001, string.Format("magenta={0}/{1} ({2:F4}) -> {3}", magenta, pixels.Length, ratio, path));
				}
				finally
				{
					UnityEngine.Object.Destroy(shot);
				}
			}
		}
	}
}
