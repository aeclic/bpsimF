using System;
using Bpsim.Modding;
using Bpsim.Mods;
using Bpsim.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.LevelEditor
{
	/// <summary>
	/// 关卡编辑器的覆盖窗口。网页一律走 <see cref="HtmlPanelView"/> 平台门面：
	/// 桌面/编辑器用 Vuplex，Android/iOS 真机用 UniWebView。
	/// 之前这里直接 <c>CanvasWebViewPrefab.Instantiate()</c>，工程没有 3D WebView for Android，
	/// 真机上会抛 WebViewUnavailableException 并留下一整块白色纹理（白板）。
	/// </summary>
	public sealed class EditorWindow : MonoBehaviour
	{
		private LevelEditorEntryPoint m_owner;
		private HtmlPanelView m_view;
		private IDisposable m_input;
		private RectTransform m_frame;
		private GameObject m_notice;
		private string m_url;
		private float m_hideAt;
		private Canvas m_canvas;
		private bool m_visible, m_initialized;
		// The whole overlay stays a little translucent so the game behind it stays readable.
		private const float OverlayOpacity = 0.84f;

		public static EditorWindow Create(LevelEditorEntryPoint owner, string url)
		{
			GameObject go = new GameObject("BpsimLevelEditorModWindow");
			UnityEngine.Object.DontDestroyOnLoad(go);
			EditorWindow window = go.AddComponent<EditorWindow>();
			window.Build(owner, url);
			window.Show();
			return window;
		}

		private void Build(LevelEditorEntryPoint owner, string url)
		{
			m_owner = owner;
			m_url = url;
			Canvas canvas = ModUiKit.CreateCanvas("LevelEditorModCanvas", 32500);
			m_canvas = canvas;
			canvas.transform.SetParent(transform, false);
			CanvasGroup group = canvas.gameObject.AddComponent<CanvasGroup>();
			group.alpha = OverlayOpacity;
			RectTransform backdrop = Rect(canvas.transform, "Backdrop", Vector2.zero, new Vector2(1920, 1080));
			backdrop.gameObject.AddComponent<Image>().color = new Color(0.03f, 0.04f, 0.06f, 0.45f);
			RectTransform frame = Rect(canvas.transform, "EditorFrame", Vector2.zero, new Vector2(1820, 1000));
			m_frame = frame;
			// No Mod chrome above the page: the web command bar is the only top row.
			RectTransform host = Rect(frame, "WebHost", Vector2.zero, new Vector2(1820, 1000));
			try
			{
				// 门面自己挑后端：Android/iOS Player 用 UniWebView，绝不创建 Vuplex.
				m_view = HtmlPanelView.Attach(gameObject, host, true,
					new Color(0.03f, 0.04f, 0.06f, 1f));
				if (m_view == null)
				{
					Notice("网页面板不可用：门面创建失败");
					return;
				}
				m_view.Ready += OnReady;
				m_view.MessageReceived += OnMessage;
				m_view.PageLoaded += OnPageLoaded;
				m_view.Failed += OnFailed;
				// 桌面端 Vuplex 的键盘桥才存在；移动端是原生 IME，门面里为 null。
				if (m_view.Keyboard != null) m_view.Keyboard.ForwardEditorShortcuts = true;
				// Attach 内部就可能同步 Fail（那时还没订阅），补一次显示。
				if (!string.IsNullOrEmpty(m_view.LastError)) OnFailed(m_view.LastError);
			}
			catch (Exception ex)
			{
				Notice("网页面板不可用：" + ex.Message);
			}
		}

		private void OnReady()
		{
			m_initialized = true;
			if (m_view == null) return;
			m_view.LoadUrl(m_url);
			SetVisible(m_visible);
		}

		private void OnPageLoaded(string url)
		{
			// 原生视图在布局稳定后可能还没对齐；再同步一次矩形。
			if (m_view != null) m_view.SyncFrame();
		}

		private void OnMessage(string text)
		{
			try
			{
				JObject message = JObject.Parse(text);
				if ((string)message["type"] != "bpsim.capabilities") return;
				Notice((bool?)message["webgpu"] == true ? null : "内置浏览器 WebGPU 不可用，3D 画布可能无法渲染。");
			}
			catch (Exception) { }
		}

		private void OnFailed(string reason)
		{
			Notice("网页面板不可用：" + reason);
		}

		private void Notice(string message)
		{
			if (string.IsNullOrEmpty(message))
			{
				if (m_notice != null) m_notice.SetActive(false);
				return;
			}
			if (m_notice == null)
			{
				RectTransform rect = Rect(m_frame, "WebViewNotice", new Vector2(0, 430), new Vector2(1180, 44));
				rect.gameObject.AddComponent<Image>().color = new Color(0.36f, 0.13f, 0.14f, 0.96f);
				Text text = ModUiKit.Label(rect, message, 18, ModUiKit.TextMain, TextAnchor.MiddleCenter);
				text.rectTransform.anchorMin = Vector2.zero;
				text.rectTransform.anchorMax = Vector2.one;
				text.rectTransform.offsetMin = new Vector2(12, 0);
				text.rectTransform.offsetMax = new Vector2(-12, 0);
				m_notice = rect.gameObject;
				return;
			}
			Text label = m_notice.GetComponentInChildren<Text>();
			if (label != null) label.text = message;
			m_notice.SetActive(true);
		}

		public void Show()
		{
			m_hideAt = 0;
			m_visible = true;
			gameObject.SetActive(true);
			SetVisible(true);
			if (m_input == null) m_input = ModInputCapture.Acquire(m_owner);
		}

		public void Hide()
		{
			m_hideAt = 0;
			m_visible = false;
			SetVisible(false);
			ReleaseInput();
		}

		public void Close()
		{
			Hide();
			Destroy(gameObject);
		}

		private void SetVisible(bool visible)
		{
			if (visible && !gameObject.activeSelf) gameObject.SetActive(true);
			if (m_view != null) m_view.SetVisible(visible);
			if (m_frame != null) m_frame.gameObject.SetActive(visible || !m_initialized);
			if (m_canvas != null)
			{
				m_canvas.enabled = visible;
				GraphicRaycaster raycaster = m_canvas.GetComponent<GraphicRaycaster>();
				if (raycaster != null) raycaster.enabled = visible;
			}
			if (!visible && m_initialized && gameObject.activeSelf) gameObject.SetActive(false);
		}

		public void RequestClose() { m_hideAt = Time.realtimeSinceStartup + 0.3f; }
		private void Update() { if (m_hideAt > 0 && Time.realtimeSinceStartup >= m_hideAt) Close(); }
		private void OnDisable() { ReleaseInput(); }
		private void ReleaseInput() { if (m_input != null) { m_input.Dispose(); m_input = null; } }

		private void OnDestroy()
		{
			ReleaseInput();
			if (m_view != null)
			{
				m_view.Ready -= OnReady;
				m_view.MessageReceived -= OnMessage;
				m_view.PageLoaded -= OnPageLoaded;
				m_view.Failed -= OnFailed;
			}
		}

		private static RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size)
		{
			GameObject go = new GameObject(name, typeof(RectTransform));
			RectTransform rect = (RectTransform)go.transform;
			rect.SetParent(parent, false);
			rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
			rect.anchoredPosition = position;
			rect.sizeDelta = size;
			return rect;
		}
	}
}
