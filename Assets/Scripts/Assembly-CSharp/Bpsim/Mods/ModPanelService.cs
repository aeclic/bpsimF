using System;
using System.Collections.Generic;
using System.IO;
using Bpsim.Modding;
using Bpsim.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.Mods
{
	/// <summary>
	/// 把 Mod 声明的网页面板（<see cref="ModPanelSpec"/>）渲染成游戏内可拖动窗口。
	/// 具体用哪个网页后端由 <see cref="HtmlPanelView"/> 按平台挑：Windows/编辑器走 Vuplex，
	/// Android/iOS 真机走 UniWebView（本工程没有 Vuplex 的 Android 后端）。
	/// </summary>
	/// <remarks>
	/// 平台差异全部封在 <see cref="HtmlPanelView"/> 里，本文件不再出现条件编译。
	/// 纯数据/清单/安装/ECS 部分在所有平台完全可用，不受影响。
	/// </remarks>
	public static class ModPanelService
	{
		private static readonly Dictionary<string, ModPanelWindow> s_open =
			new Dictionary<string, ModPanelWindow>(StringComparer.OrdinalIgnoreCase);

		/// <summary>每个已加载 Mod 的按钮回调里用：包 + spec。</summary>
		public struct PanelRef
		{
			public ModPackage Package;
			public ModPanelSpec Spec;
			public string Key => (Package != null ? Package.Id : "?") + ":" + (Spec != null ? Spec.id : "?");
		}

		public static bool IsOpen(string key) => s_open.ContainsKey(key) && s_open[key] != null;

		public static int OpenCount
		{
			get
			{
				int n = 0;
				foreach (ModPanelWindow w in s_open.Values)
				{
					if (w != null) n++;
				}
				return n;
			}
		}

		public static void Open(ModPackage pkg, ModPanelSpec spec)
		{
			if (pkg == null || spec == null)
			{
				return;
			}
			string key = pkg.Id + ":" + spec.id;
			if (s_open.TryGetValue(key, out ModPanelWindow existing) && existing != null)
			{
				existing.Show();
				return;
			}
			ModPanelWindow window = ModPanelWindow.Create(pkg, spec);
			s_open[key] = window;
		}

		public static void Close(string key)
		{
			if (s_open.TryGetValue(key, out ModPanelWindow w))
			{
				if (w != null)
				{
					UnityEngine.Object.Destroy(w.gameObject);
				}
				s_open.Remove(key);
			}
		}

		public static void CloseAll()
		{
			foreach (ModPanelWindow w in new List<ModPanelWindow>(s_open.Values))
			{
				if (w != null)
				{
					UnityEngine.Object.Destroy(w.gameObject);
				}
			}
			s_open.Clear();
		}

		internal static void NotifyClosed(string key)
		{
			s_open.Remove(key);
		}

		/// <summary>一个 Mod 的全部面板 key（用于关闭整个 Mod 的面板）。</summary>
		public static void ClosePackage(ModPackage pkg)
		{
			if (pkg?.Manifest?.panels == null)
			{
				CloseByPrefix(pkg?.Id + ":");
				return;
			}
			foreach (ModPanelSpec spec in pkg.Manifest.panels)
			{
				Close(pkg.Id + ":" + spec.id);
			}
			CloseByPrefix(pkg.Id + ":");
		}

		private static void CloseByPrefix(string prefix)
		{
			foreach (string k in new List<string>(s_open.Keys))
			{
				if (k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
				{
					Close(k);
				}
			}
		}

		/// <summary>QA/日志：所有打开窗口的状态。</summary>
		public static string Describe()
		{
			List<string> items = new List<string>();
			foreach (KeyValuePair<string, ModPanelWindow> kv in s_open)
			{
				items.Add(kv.Key + "=" + (kv.Value == null ? "null" : kv.Value.Status));
			}
			return "panels[" + items.Count + "] " + string.Join("; ", items);
		}
	}

	/// <summary>单个网页面板窗口：Canvas + 标题栏 + 关闭按钮 + 网页后端（见 <see cref="HtmlPanelView"/>）。</summary>
	public sealed class ModPanelWindow : MonoBehaviour
	{
		private ModPackage m_pkg;
		private ModPanelSpec m_spec;
		private HtmlPanelView m_html;
		private ModPanelAudio m_audio;
		private RectTransform m_host;
		private Text m_errorLabel;

		public string Key => (m_pkg != null ? m_pkg.Id : "?") + ":" + (m_spec != null ? m_spec.id : "?");

		public string Status => m_html == null ? "null" : m_html.Status;

		/// <summary>当前窗口实际使用的网页后端（QA/日志用）。</summary>
		public HtmlPanelBackend Backend => m_html == null ? HtmlPanelBackend.None : m_html.Backend;

		public static ModPanelWindow Create(ModPackage pkg, ModPanelSpec spec)
		{
			GameObject go = new GameObject("ModPanel_" + pkg.Id + "_" + spec.id);
			UnityEngine.Object.DontDestroyOnLoad(go);
			ModPanelWindow win = go.AddComponent<ModPanelWindow>();
			win.Build(pkg, spec);
			return win;
		}

		private void Build(ModPackage pkg, ModPanelSpec spec)
		{
			m_pkg = pkg;
			m_spec = spec;

			Canvas canvas = ModUiKit.CreateCanvas("Canvas", 30000 + UnityEngine.Random.Range(0, 500));
			canvas.transform.SetParent(transform, false);

			RectTransform frame = ModUiKit.Panel(canvas.transform, ModUiKit.PanelBg, 14);
			frame.name = "Frame";
			// 定位
			Vector2 size = new Vector2(spec.width, spec.height);
			frame.sizeDelta = size;
			float margin = 24f;
			switch ((spec.anchor ?? "bottom-right").ToLowerInvariant())
			{
				case "bottom-left":
					frame.anchorMin = frame.anchorMax = new Vector2(0f, 0f);
					frame.pivot = new Vector2(0f, 0f);
					frame.anchoredPosition = new Vector2(margin, margin);
					break;
				case "top-left":
					frame.anchorMin = frame.anchorMax = new Vector2(0f, 1f);
					frame.pivot = new Vector2(0f, 1f);
					frame.anchoredPosition = new Vector2(margin, -margin);
					break;
				case "top-right":
					frame.anchorMin = frame.anchorMax = new Vector2(1f, 1f);
					frame.pivot = new Vector2(1f, 1f);
					frame.anchoredPosition = new Vector2(-margin, -margin);
					break;
				case "center":
					frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f);
					frame.pivot = new Vector2(0.5f, 0.5f);
					frame.anchoredPosition = Vector2.zero;
					break;
				default: // bottom-right
					frame.anchorMin = frame.anchorMax = new Vector2(1f, 0f);
					frame.pivot = new Vector2(1f, 0f);
					frame.anchoredPosition = new Vector2(-margin, margin);
					break;
			}

			// 标题栏
			RectTransform bar = ModUiKit.Panel(frame, new Color(0.12f, 0.14f, 0.18f, 1f), 14);
			bar.name = "TitleBar";
			bar.anchorMin = new Vector2(0f, 1f);
			bar.anchorMax = new Vector2(1f, 1f);
			bar.pivot = new Vector2(0.5f, 1f);
			bar.sizeDelta = new Vector2(0f, 40f);
			bar.anchoredPosition = Vector2.zero;
			// 让标题栏本身可拖动窗口；移动端页面是原生 View，拖动时要同步它的屏幕矩形。
			WindowDrag drag = bar.gameObject.AddComponent<WindowDrag>();
			drag.Target = frame;
			drag.Moved = SyncHtmlFrame;

			Text title = ModUiKit.Label(bar, string.IsNullOrEmpty(spec.title) ? m_pkg.DisplayName : spec.title,
				20, ModUiKit.TextMain, TextAnchor.MiddleLeft);
			ModUiKit.SetRect(title.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(14f, 0f), new Vector2(-48f, 0f));

			GameObject closeGo = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button));
			RectTransform closeRt = closeGo.GetComponent<RectTransform>();
			closeRt.SetParent(bar, false);
			closeRt.anchorMin = closeRt.anchorMax = new Vector2(1f, 0.5f);
			closeRt.pivot = new Vector2(1f, 0.5f);
			closeRt.sizeDelta = new Vector2(32f, 32f);
			closeRt.anchoredPosition = new Vector2(-6f, 0f);
			Image closeImg = closeGo.GetComponent<Image>();
			closeImg.sprite = ModUiKit.Rounded(8);
			closeImg.type = Image.Type.Sliced;
			closeImg.color = ModUiKit.Danger;
			closeGo.GetComponent<Button>().onClick.AddListener(CloseSelf);
			Text x = ModUiKit.Label(closeRt, "×", 24, ModUiKit.TextMain, TextAnchor.MiddleCenter);
			ModUiKit.SetRect(x.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

			// WebView 区
			RectTransform host = ModUiKit.Panel(frame, new Color(0f, 0f, 0f, 0f), 8);
			host.name = "WebHost";
			ModUiKit.SetRect(host, Vector2.zero, Vector2.one, new Vector2(6f, 6f), new Vector2(-6f, -46f));
			m_host = host;

			// 平台后端在门面里选：桌面/编辑器 = Vuplex，Android/iOS 真机 = UniWebView。
			m_html = HtmlPanelView.Attach(gameObject, host, true, new Color(0.09f, 0.10f, 0.13f, 1f));
			if (m_html == null)
			{
				ShowError("网页组件创建失败");
				return;
			}
			m_html.MessageReceived += OnMessage;
			m_html.Ready += OnWebReady;
			m_html.PageLoaded += OnPageLoaded;
			m_html.Failed += OnWebFailed;
		}

		/// <summary>后端就绪：接上音频后端并塞入页面。</summary>
		private void OnWebReady()
		{
			if (m_html == null)
			{
				return;
			}
			// 音频后端：网页发指令，Unity 真正出声（页面本身拿不到本地文件，联网也受跨域限制）。
			if (m_audio == null)
			{
				m_audio = gameObject.AddComponent<ModPanelAudio>();
				m_audio.Post = PostToPage;
				m_audio.Bind(m_pkg != null ? m_pkg.Id : string.Empty, m_pkg != null ? m_pkg.Directory : string.Empty);
			}
			Debug.Log("[ModPanel] " + Key + " 后端 " + m_html.BackendName + " 就绪，开始加载页面 " + (m_spec != null ? m_spec.html : "?"));
			m_html.LoadHtml(BuildPage(), BuildBaseUrl());
		}

		private void OnPageLoaded(string url)
		{
			Debug.Log("[ModPanel] " + Key + " 页面已加载 " + (string.IsNullOrEmpty(url) ? string.Empty : url));
		}

		/// <summary>窗口位置变了就通知后端同步原生视图矩形（桌面后端是空操作）。</summary>
		private void SyncHtmlFrame()
		{
			if (m_html != null)
			{
				m_html.SyncFrame();
			}
		}

		private void OnWebFailed(string reason)
		{
			ShowError(reason);
		}

		/// <summary>后端不可用时在窗口里直接说明原因，而不是弹异常把整个面板干掉。</summary>
		private void ShowError(string reason)
		{
			if (m_errorLabel != null || m_host == null)
			{
				Debug.LogWarning("[ModPanel] " + Key + " 网页不可用: " + reason);
				return;
			}
			string text = "网页面板不可用\n" + reason;
			m_errorLabel = ModUiKit.Label(m_host, text, 18, ModUiKit.Danger, TextAnchor.MiddleCenter);
			ModUiKit.SetRect(m_errorLabel.rectTransform, Vector2.zero, Vector2.one, new Vector2(16f, 16f), new Vector2(-16f, -16f));
			Debug.LogWarning("[ModPanel] " + Key + " 网页不可用: " + reason);
		}

		private void OnMessage(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return;
			}
			// 面板协议：{"type":"audio",...} / {"type":"close"}，其余按普通消息记日志。
			if (text[0] == '{')
			{
				try
				{
					JObject message = JObject.Parse(text);
					string type = (string)message["type"] ?? string.Empty;
					if (string.Equals(type, "audio", StringComparison.OrdinalIgnoreCase))
					{
						if (m_audio != null)
						{
							m_audio.Handle(message);
						}
						return;
					}
					if (string.Equals(type, "close", StringComparison.OrdinalIgnoreCase))
					{
						CloseSelf();
						return;
					}
					if (string.Equals(type, "log", StringComparison.OrdinalIgnoreCase))
					{
						Debug.Log("[ModPanel] " + Key + " 页面: " + (string)message["text"]);
						return;
					}
				}
				catch (Exception)
				{
					// 不是我们的协议，落到下面的普通日志。
				}
			}
			Debug.Log("[ModPanel] " + Key + " msg: " + (text.Length > 200 ? text.Substring(0, 200) + "…" : text));
		}

		/// <summary>把一条 JSON 消息推给页面（页面侧入口 <c>window.__bpsimHost</c>）。</summary>
		private void PostToPage(string json)
		{
			if (m_html == null || string.IsNullOrEmpty(json))
			{
				return;
			}
			// JSON 是合法 JS 字面量；只要把 JS 的行分隔符转义掉，塞进表达式就安全。
			string safe = json.Replace("\u2028", "\\u2028").Replace("\u2029", "\\u2029");
			m_html.ExecuteJavaScript("(window.__bpsimHost||function(){})(" + safe + ");");
		}

		/// <summary>页面文件相对于 Mod 目录，拼接成自包含 html（css/js 外置时内联）。</summary>
		private string BuildPage()
		{
			string dir = m_pkg != null ? m_pkg.Directory : string.Empty;
			string html = ReadText(Path.Combine(dir, m_spec.html), null);
			if (html == null)
			{
				return "<html><body style='color:#eee;background:#15181f;font-family:sans-serif;padding:24px'>"
					+ "找不到页面：" + m_spec.html + "</body></html>";
			}
			string css = string.IsNullOrEmpty(m_spec.css) ? null : ReadText(Path.Combine(dir, m_spec.css), null);
			string js = string.IsNullOrEmpty(m_spec.js) ? null : ReadText(Path.Combine(dir, m_spec.js), null);
			if (css != null)
			{
				html = InjectIntoHtml(html, "<style>" + css + "</style>", "<!--MOD_CSS-->", "</head>");
			}
			if (js != null)
			{
				html = InjectIntoHtml(html, "<script>" + js + "</script>", "<!--MOD_JS-->", "</body>");
			}
			return html;
		}

		private static string InjectIntoHtml(string html, string block, string placeholder, string beforeTag)
		{
			if (html.Contains(placeholder))
			{
				return html.Replace(placeholder, block);
			}
			int idx = html.LastIndexOf(beforeTag, StringComparison.OrdinalIgnoreCase);
			if (idx >= 0)
			{
				return html.Substring(0, idx) + block + html.Substring(idx);
			}
			return html + block;
		}

		private string BuildBaseUrl()
		{
			// 让相对资源（图标等）能定位到 Mod 目录
			try
			{
				string dir = m_pkg?.Directory;
				if (!string.IsNullOrEmpty(dir))
				{
					return new Uri(dir.Replace('\\', '/') + "/").AbsoluteUri;
				}
			}
			catch (Exception) { }
			return null;
		}

		private static string ReadText(string path, string fallback)
		{
			try
			{
				return File.Exists(path) ? File.ReadAllText(path) : fallback;
			}
			catch (Exception ex)
			{
				Debug.LogWarning("[ModPanel] read failed " + path + ": " + ex.Message);
				return fallback;
			}
		}

		public void Show()
		{
			gameObject.SetActive(true);
			if (m_html != null)
			{
				m_html.SetVisible(true);
			}
		}

		public void CloseSelf()
		{
			if (m_html != null)
			{
				m_html.SetVisible(false);
			}
			ModPanelService.NotifyClosed(Key);
			Destroy(gameObject);
		}

		private void OnDestroy()
		{
			ModPanelService.NotifyClosed(Key);
		}
	}

	/// <summary>简单窗口拖动：拖标题栏移动 Frame 的 anchoredPosition。</summary>
	public sealed class WindowDrag : MonoBehaviour, UnityEngine.EventSystems.IBeginDragHandler,
		UnityEngine.EventSystems.IDragHandler
	{
		public RectTransform Target;

		/// <summary>位置改变后的回调；移动端用它同步原生网页视图的屏幕矩形。</summary>
		public Action Moved;

		private Vector2 m_start;

		public void OnBeginDrag(UnityEngine.EventSystems.PointerEventData eventData)
		{
			if (Target != null)
			{
				m_start = Target.anchoredPosition;
			}
		}

		public void OnDrag(UnityEngine.EventSystems.PointerEventData eventData)
		{
			if (Target != null)
			{
				Canvas canvas = Target.GetComponentInParent<Canvas>();
				float scale = canvas != null ? canvas.scaleFactor : 1f;
				if (scale <= 0f) scale = 1f;
				Target.anchoredPosition = m_start + eventData.delta / scale;
				if (Moved != null)
				{
					Moved();
				}
			}
		}
	}
}
