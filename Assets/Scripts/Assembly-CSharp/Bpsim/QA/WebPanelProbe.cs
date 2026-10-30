using System;
using System.Collections;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Vuplex.WebView;
using Bpsim.Mods;
using Bpsim.UI;

namespace Bpsim.QA
{
	/// <summary>
	/// 网页面板“没声音 / 不能打字”两个问题的自动化回归探针。
	/// </summary>
	/// <remarks>
	/// 通过 DshBridge 的 <c>invoke_static</c> 触发（需要在 Play Mode 下）：
	/// <code>
	/// invoke_static("Bpsim.QA.WebPanelProbe", "ProbeLegacyInput")
	/// invoke_static("Bpsim.QA.WebPanelProbe", "RunAudio")
	/// invoke_static("Bpsim.QA.WebPanelProbe", "RunKeyboard")
	/// invoke_static("Bpsim.QA.WebPanelProbe", "RunUgui")
	/// invoke_static("Bpsim.QA.WebPanelProbe", "Report")
	/// </code>
	/// 结果写在静态字段里，同时 Debug.Log 一份。
	/// </remarks>
	public static class WebPanelProbe
	{
		public static string LegacyInputResult = "(未运行)";
		public static string AudioResult = "(未运行)";
		public static string KeyboardResult = "(未运行)";
		public static string UguiResult = "(未运行)";

		public static string Report()
		{
			return "\n  legacy=" + LegacyInputResult
				+ "\n  audio=" + AudioResult
				+ "\n  keyboard=" + KeyboardResult
				+ "\n  ugui=" + UguiResult;
		}

		/// <summary>legacy Input 类在当前 Player Settings 下还能不能用（uGUI 文本输入依赖它）。</summary>
		public static string ProbeLegacyInput()
		{
			string text;
			try
			{
				Vector3 mouse = UnityEngine.Input.mousePosition;
				string typed = UnityEngine.Input.inputString;
				text = "legacy-ok mouse=" + mouse + " inputStringLen=" + typed.Length;
			}
			catch (Exception ex)
			{
				text = "legacy-DEAD " + ex.GetType().Name + ": " + ex.Message;
			}
			LegacyInputResult = text;
			Debug.Log("[Probe] legacy: " + text);
			return text;
		}

		/// <summary>生成 1 秒 440Hz 单声道 WAV，写成文件，交给 ModPanelAudio 从磁盘加载并播放。</summary>
		public static string RunAudio()
		{
			AudioResult = "(运行中)";
			GameObject go = new GameObject("WebPanelProbe_Audio");
			go.AddComponent<AudioProbeRunner>();
			return "started";
		}

		/// <summary>建一个最小 webview + 输入框，用 InputSystem 排队文本/按键事件，看页面是否收到。</summary>
		public static string RunKeyboard()
		{
			KeyboardResult = "(运行中)";
			GameObject go = new GameObject("WebPanelProbe_Keyboard");
			go.AddComponent<KeyboardProbeRunner>();
			return "started";
		}

		/// <summary>建一个 uGUI InputField，用 InputSystem 排队文本事件，看它能不能收到字符。</summary>
		public static string RunUgui()
		{
			UguiResult = "(运行中)";
			GameObject go = new GameObject("WebPanelProbe_Ugui");
			go.AddComponent<UguiProbeRunner>();
			return "started";
		}

		internal static void LogUgui(string message)
		{
			UguiResult = message;
			Debug.Log("[UguiProbe] " + message);
		}

		internal static void LogAudio(string message)
		{
			AudioResult = message;
			Debug.Log("[AudioProbe] " + message);
		}

		internal static void LogKeyboard(string message)
		{
			KeyboardResult = message;
			Debug.Log("[KeyProbe] " + message);
		}

		internal static byte[] BuildToneWav(float seconds, float frequency, int sampleRate)
		{
			int sampleCount = Mathf.Max(1, Mathf.RoundToInt(seconds * sampleRate));
			int byteCount = sampleCount * 2; // 16-bit mono
			byte[] bytes = new byte[44 + byteCount];
			using (MemoryStream stream = new MemoryStream(bytes))
			using (BinaryWriter writer = new BinaryWriter(stream))
			{
				writer.Write(new[] { 'R', 'I', 'F', 'F' });
				writer.Write(36 + byteCount);
				writer.Write(new[] { 'W', 'A', 'V', 'E' });
				writer.Write(new[] { 'f', 'm', 't', ' ' });
				writer.Write(16);
				writer.Write((short)1);        // PCM
				writer.Write((short)1);        // mono
				writer.Write(sampleRate);
				writer.Write(sampleRate * 2);  // byte rate
				writer.Write((short)2);        // block align
				writer.Write((short)16);       // bits
				writer.Write(new[] { 'd', 'a', 't', 'a' });
				writer.Write(byteCount);
				for (int i = 0; i < sampleCount; i++)
				{
					float t = (float)i / sampleRate;
					// 淡入淡出，避免首尾爆音
					float env = Mathf.Min(1f, Mathf.Min(t * 20f, (seconds - t) * 20f));
					short sample = (short)(Mathf.Sin(2f * Mathf.PI * frequency * t) * env * 0.35f * short.MaxValue);
					writer.Write(sample);
				}
			}
			return bytes;
		}
	}

	/// <summary>音频链路：本地文件 → UnityWebRequestMultimedia → AudioSource 出声。</summary>
	public sealed class AudioProbeRunner : MonoBehaviour
	{
		private IEnumerator Start()
		{
			string path = Path.Combine(Application.temporaryCachePath, "bpsim-probe-tone.wav");
			try
			{
				File.WriteAllBytes(path, WebPanelProbe.BuildToneWav(1f, 440f, 44100));
				WebPanelProbe.LogAudio("wav=" + path + " bytes=" + new FileInfo(path).Length);
			}
			catch (Exception ex)
			{
				WebPanelProbe.LogAudio("FAIL 写 wav: " + ex.Message);
				yield break;
			}

			AudioSource source = gameObject.AddComponent<AudioSource>();
			source.playOnAwake = false;
			ModPanelAudio audio = gameObject.AddComponent<ModPanelAudio>();
			string lastState = "(none)";
			int reports = 0;
			audio.Post = json =>
			{
				reports++;
				try
				{
					JObject obj = JObject.Parse(json);
					lastState = json;
				}
				catch (Exception)
				{
					lastState = json;
				}
			};
			audio.Bind("probe", Application.temporaryCachePath);

			audio.Handle(new JObject { ["op"] = "playLocal", ["path"] = path, ["title"] = "probe tone" });

			float deadline = Time.realtimeSinceStartup + 6f;
			float soundedFor = 0f;
			while (Time.realtimeSinceStartup < deadline)
			{
				yield return null;
				if (source.isPlaying)
				{
					soundedFor += Time.unscaledDeltaTime;
				}
			}

			bool ok = soundedFor > 0.3f && source.clip != null;
			WebPanelProbe.LogAudio((ok ? "PASS" : "FAIL")
				+ " 播放时长=" + soundedFor.ToString("0.00") + "s"
				+ " clipSamples=" + (source.clip != null ? source.clip.samples : 0)
				+ " 状态上报=" + reports + " 次"
				+ " last=" + lastState);
			Destroy(gameObject);
		}
	}

	/// <summary>键盘链路：InputSystem 文本/按键事件 → WebViewKeyboardBridge → CEF → 页面输入框。</summary>
	public sealed class KeyboardProbeRunner : MonoBehaviour
	{
		private const string PageHtml =
			"<!DOCTYPE html><html><head><meta charset='utf-8'><style>body{background:#111;color:#eee;font:16px sans-serif}"
			+ "input{width:90%;font-size:20px;padding:8px}</style></head><body>"
			+ "<input id='t' type='text' autofocus placeholder='type here'>"
			+ "<script>var t=document.getElementById('t'),keys=[];"
			+ "function send(){try{if(window.vuplex&&window.vuplex.postMessage)"
			+ "window.vuplex.postMessage(JSON.stringify({type:'probe',v:t.value,keys:keys.join(',')}));}catch(e){}}"
			+ "document.addEventListener('keydown',function(e){keys.push(e.key);send();});"
			+ "t.addEventListener('input',send);t.focus();"
			+ "window.__ppsBridge=function(m){try{window.vuplex.postMessage(JSON.stringify({type:'probe',v:t.value,keys:keys.join(','),note:m}));}catch(e){}};"
			+ "</script></body></html>";

		private CanvasWebViewPrefab m_prefab;
		private WebViewKeyboardBridge m_bridge;
		private string m_value = "(无消息)";
		private int m_messages;

		private IEnumerator Start()
		{
			Canvas canvas = new GameObject("ProbeCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster))
				.GetComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 32000;

			m_prefab = CanvasWebViewPrefab.Instantiate();
			m_prefab.KeyboardEnabled = false;
			m_prefab.LogConsoleMessages = true;
			RectTransform rect = (RectTransform)m_prefab.transform;
			rect.SetParent(canvas.transform, false);
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = new Vector2(40f, 40f);
			rect.offsetMax = new Vector2(-40f, -40f);
			rect.localScale = Vector3.one;
			m_bridge = WebViewKeyboardBridge.Attach(m_prefab);

			bool ready = false;
			m_prefab.Initialized += (s, e) => ready = true;
			float waitEnd = Time.realtimeSinceStartup + 20f;
			while (!ready && Time.realtimeSinceStartup < waitEnd)
			{
				yield return null;
			}
			if (!ready || m_prefab.WebView == null)
			{
				WebPanelProbe.LogKeyboard("FAIL webview 未初始化");
				yield break;
			}

			IWebView view = m_prefab.WebView;
			view.MessageEmitted += (s, e) =>
			{
				m_messages++;
				m_value = e.Value;
			};
			view.LoadHtml(PageHtml);
			yield return new WaitForSeconds(2.5f);

			UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
			if (keyboard == null)
			{
				WebPanelProbe.LogKeyboard("FAIL 没有 Keyboard.current（新输入系统未启用？）");
				yield break;
			}
			m_bridge.SetFocused(true);
			yield return new WaitForSeconds(0.3f);

			// 1) 普通字符
			foreach (char c in "abc")
			{
				InputSystem.QueueTextEvent(keyboard, c);
				yield return null;
			}
			yield return new WaitForSeconds(1f);
			string afterText = m_value;

			// 2) 退格
			InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Backspace));
			yield return null;
			yield return null;
			InputSystem.QueueStateEvent(keyboard, new KeyboardState());
			yield return new WaitForSeconds(0.8f);
			string afterBackspace = m_value;

			// 3) 回车
			InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter));
			yield return null;
			yield return null;
			InputSystem.QueueStateEvent(keyboard, new KeyboardState());
			yield return new WaitForSeconds(0.8f);
			string afterEnter = m_value;

			view.ExecuteJavaScript("(window.__ppsBridge||function(){})('js-ok');");
			yield return new WaitForSeconds(0.8f);

			bool textOk = afterText != null && afterText.Contains("\"abc\"");
			bool backOk = afterBackspace != null && afterBackspace.Contains("\"ab\"");
			bool enterOk = afterEnter != null && afterEnter.Contains("Backspace") && afterEnter.Contains("Enter");
			WebPanelProbe.LogKeyboard((textOk && backOk && enterOk ? "PASS" : "FAIL")
				+ " msgs=" + m_messages + " focused=" + m_bridge.IsFocused
				+ " | 文本=" + afterText
				+ " | 退格后=" + afterBackspace
				+ " | 回车后=" + afterEnter);
			yield return new WaitForSeconds(0.5f);
			Destroy(gameObject);
		}
	}

	/// <summary>uGUI 链路：InputField 的文本输入是否还活着（取决于 legacy Input 是否可用）。</summary>
	public sealed class UguiProbeRunner : MonoBehaviour
	{
		private IEnumerator Start()
		{
			if (EventSystem.current == null)
			{
				GameObject system = new GameObject("ProbeEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
				system.transform.SetParent(transform, false);
			}

			Canvas canvas = new GameObject("ProbeUiCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster))
				.GetComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 32000;
			canvas.transform.SetParent(transform, false);

			GameObject fieldGo = new GameObject("ProbeField", typeof(RectTransform), typeof(Image), typeof(InputField));
			RectTransform rt = (RectTransform)fieldGo.transform;
			rt.SetParent(canvas.transform, false);
			rt.anchorMin = new Vector2(0.5f, 0.5f);
			rt.anchorMax = new Vector2(0.5f, 0.5f);
			rt.sizeDelta = new Vector2(420f, 64f);
			rt.anchoredPosition = Vector2.zero;

			GameObject textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
			RectTransform textRt = (RectTransform)textGo.transform;
			textRt.SetParent(rt, false);
			textRt.anchorMin = Vector2.zero;
			textRt.anchorMax = Vector2.one;
			textRt.offsetMin = new Vector2(8f, 4f);
			textRt.offsetMax = new Vector2(-8f, -4f);
			Text text = textGo.GetComponent<Text>();
			text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
			text.fontSize = 28;
			text.color = Color.black;
			text.supportRichText = false;

			InputField field = fieldGo.GetComponent<InputField>();
			field.textComponent = text;
			field.targetGraphic = fieldGo.GetComponent<Image>();
			field.lineType = InputField.LineType.SingleLine;
			field.text = string.Empty;

			yield return null;
			EventSystem.current.SetSelectedGameObject(fieldGo);
			field.ActivateInputField();
			yield return new WaitForSeconds(0.3f);

			UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
			if (keyboard == null)
			{
				WebPanelProbe.LogUgui("FAIL 没有 Keyboard.current");
				yield break;
			}
			foreach (char c in "12")
			{
				InputSystem.QueueTextEvent(keyboard, c);
				yield return null;
			}
			yield return new WaitForSeconds(1f);

			// 也试一次真实按键事件（部分输入模块只认按键、不认文本事件）
			InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Digit3));
			yield return null;
			yield return null;
			InputSystem.QueueStateEvent(keyboard, new KeyboardState());
			yield return new WaitForSeconds(1f);

			bool ok = field.text != null && field.text.Contains("12");
			WebPanelProbe.LogUgui((ok ? "PASS" : "FAIL")
				+ " text='" + field.text + "' selected=" + (EventSystem.current.currentSelectedGameObject == fieldGo)
				+ " module=" + (EventSystem.current.currentInputModule != null
					? EventSystem.current.currentInputModule.GetType().Name : "null"));
			yield return new WaitForSeconds(0.5f);
			Destroy(gameObject);
		}
	}
}
