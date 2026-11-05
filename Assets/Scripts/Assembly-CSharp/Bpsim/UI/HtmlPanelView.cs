using System;
using System.Collections;
using UnityEngine;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX
using Vuplex.WebView;
#endif

namespace Bpsim.UI
{
	/// <summary>网页面板当前实际使用的后端。</summary>
	public enum HtmlPanelBackend
	{
		None = 0,
		UniWebView = 1,
		Vuplex = 2
	}

	/// <summary>
	/// 网页面板的平台门面：桌面/编辑器用 Vuplex（3D WebView），Android/iOS Player 用 UniWebView。
	/// </summary>
	/// <remarks>
	/// 为什么要有这一层：
	/// Vuplex 的 Android 后端是**单独售卖的包**，本工程只带了 Core + Standalone（Windows/Mac）。
	/// 在 Android Player 上直接 <c>CanvasWebViewPrefab.Instantiate()</c> 会在
	/// <c>WebPluginFactory.GetDefaultPlugin()</c> 里抛 <c>WebViewUnavailableException</c>，
	/// 整个 Mod 面板因此打不开。前置项目 bprc 用 UniWebView（原生 WebView）解决了同一问题，
	/// 这里照搬同一套思路，只是把「哪个后端」封装成一个门面，业务侧不再出现平台条件编译。
	///
	/// 两边的消息协议保持一致：
	/// 页面 → 宿主：<c>window.vuplex.postMessage(text)</c>；
	/// Android 上由注入的 shim 把它翻译成 <c>location.href = 'uniwebview://message?data=...'</c>，
	/// 宿主在 <see cref="MessageReceived"/> 里拿到原始 text，所以既有 Mod 页面（netease-music 等）不用改一行。
	/// 宿主 → 页面：<see cref="ExecuteJavaScript"/>，页面侧入口仍是 <c>window.__bpsimHost</c>。
	///
	/// 键盘：Vuplex 走 <see cref="WebViewKeyboardBridge"/>（本工程只有新输入系统）；
	/// Android 是原生 WebView，输入框自己拉系统输入法，不需要也不应该再挂桥。
	/// </remarks>
	[DisallowMultipleComponent]
	public sealed class HtmlPanelView : MonoBehaviour
	{
		/// <summary>移动端 JS→原生消息的自定义 scheme（与 bprc 的 bridge.js 保持一致）。</summary>
		public const string MobileScheme = "uniwebview";

		/// <summary>移动端消息的 path 段：<c>uniwebview://message?data=...</c>。</summary>
		public const string MobileMessagePath = "message";

		/// <summary>移动端消息里承载正文的参数名。</summary>
		public const string MobileDataArgument = "data";

		private HtmlPanelBackend m_backend = HtmlPanelBackend.None;
		private RectTransform m_rect;
		private Color m_background = new Color(0.09f, 0.10f, 0.13f, 1f);
		private bool m_keyboardEnabled = true;
		private bool m_backendInitialized;
		private bool m_readyFired;
		private bool m_pageLoaded;
		private bool m_visible = true;
		private bool m_disposed;
		private string m_lastError = string.Empty;
		private int m_messagesIn;
		private int m_messagesOut;

		/// <summary>页面 → 宿主的原始消息文本（与 Vuplex 的 <c>MessageEmitted</c> 同语义）。</summary>
		public event Action<string> MessageReceived;

		/// <summary>后端可用、可以开始 <see cref="LoadHtml"/>（在第一帧触发，保证调用方已拿到返回值）。</summary>
		public event Action Ready;

		/// <summary>页面加载完成。</summary>
		public event Action<string> PageLoaded;

		/// <summary>后端创建失败或页面加载失败；参数是给玩家看的一句话。</summary>
		public event Action<string> Failed;

		public HtmlPanelBackend Backend => m_backend;

		public bool IsBackendReady => m_readyFired;

		public bool IsPageLoaded => m_pageLoaded;

		public string LastError => m_lastError;

		public int MessagesIn => m_messagesIn;

		public int MessagesOut => m_messagesOut;

		/// <summary>Vuplex 后端才有的键盘桥；移动端为 null。</summary>
		public WebViewKeyboardBridge Keyboard { get; private set; }

		public string BackendName
		{
			get
			{
				switch (m_backend)
				{
					case HtmlPanelBackend.UniWebView: return "uniwebview";
					case HtmlPanelBackend.Vuplex: return "vuplex";
					default: return "none";
				}
			}
		}

		public string Status
		{
			get
			{
				string error = m_lastError.Length == 0 ? string.Empty : "(err:" + m_lastError + ")";
				if (m_backend == HtmlPanelBackend.None)
				{
					return "none" + error;
				}
				return BackendName
					+ (m_readyFired ? "+init" : "")
					+ (m_pageLoaded ? "+page" : "")
					+ error;
			}
		}

		/// <summary>
		/// 给一个面板挂上网页面板：<paramref name="rect"/> 是页面要占据的 UI 区域（WebHost）。
		/// 返回的组件已经选好后端；监听 <see cref="Ready"/> 后再 <see cref="LoadHtml"/>。
		/// </summary>
		public static HtmlPanelView Attach(GameObject owner, RectTransform rect, bool keyboardEnabled = true,
			Color? background = null)
		{
			if (owner == null || rect == null)
			{
				return null;
			}
			HtmlPanelView view = owner.AddComponent<HtmlPanelView>();
			view.m_rect = rect;
			view.m_keyboardEnabled = keyboardEnabled;
			if (background.HasValue)
			{
				view.m_background = background.Value;
			}
			view.CreateBackend();
			return view;
		}

		private void CreateBackend()
		{
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
			CreateMobileBackend();
#elif UNITY_EDITOR || UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX
			CreateDesktopBackend();
#else
			m_backend = HtmlPanelBackend.None;
			Fail("当前平台没有可用的网页后端（仅支持 Windows/编辑器 + Android/iOS 真机）");
#endif
		}

		private void Start()
		{
			FireReady();
		}

		private void FireReady()
		{
			if (m_readyFired || m_disposed || !m_backendInitialized)
			{
				return;
			}
			m_readyFired = true;
			try
			{
				Ready?.Invoke();
			}
			catch (Exception ex)
			{
				Debug.LogError("[HtmlPanel] Ready 回调异常: " + ex);
			}
		}

		private void Fail(string message)
		{
			m_lastError = string.IsNullOrEmpty(message) ? "unknown" : message;
			Debug.LogWarning("[HtmlPanel] " + m_lastError + " (backend=" + BackendName + ", obj=" + name + ")");
			try
			{
				Failed?.Invoke(m_lastError);
			}
			catch (Exception ex)
			{
				Debug.LogError("[HtmlPanel] Failed 回调异常: " + ex);
			}
		}

		/// <summary>把一段自包含 HTML 塞进后端。<paramref name="baseUrl"/> 用于解析页面里的相对资源。</summary>
		public void LoadHtml(string html, string baseUrl)
		{
			if (m_disposed)
			{
				return;
			}
			if (string.IsNullOrEmpty(html))
			{
				Fail("页面内容为空");
				return;
			}
			if (!m_readyFired)
			{
				Fail("网页后端尚未初始化完成");
				return;
			}
			try
			{
				switch (m_backend)
				{
					case HtmlPanelBackend.UniWebView:
						LoadMobile(InjectMobileShim(html), baseUrl);
						break;
					case HtmlPanelBackend.Vuplex:
						LoadDesktop(html);
						break;
					default:
						Fail("没有可用的网页后端");
						break;
				}
			}
			catch (Exception ex)
			{
				Fail("加载页面失败: " + ex.Message);
			}
		}

		/// <summary>
		/// 让后端直接加载一个 URL（关卡编辑器这类只有本地 HTTP 页面地址的 Mod 用）。
		/// 移动端没法把 shim 拼进 HTML，改由 UniWebView 在页面开始加载时注入。
		/// </summary>
		public void LoadUrl(string url)
		{
			if (m_disposed)
			{
				return;
			}
			if (string.IsNullOrEmpty(url))
			{
				Fail("页面地址为空");
				return;
			}
			if (!m_readyFired)
			{
				Fail("网页后端尚未初始化完成");
				return;
			}
			try
			{
				switch (m_backend)
				{
					case HtmlPanelBackend.UniWebView:
						LoadMobileUrl(url);
						break;
					case HtmlPanelBackend.Vuplex:
						LoadDesktopUrl(url);
						break;
					default:
						Fail("没有可用的网页后端");
						break;
				}
			}
			catch (Exception ex)
			{
				Fail("加载页面失败: " + ex.Message);
			}
		}

		/// <summary>在页面里执行一段 JS（宿主 → 页面）。</summary>
		public void ExecuteJavaScript(string js)
		{
			if (m_disposed || string.IsNullOrEmpty(js))
			{
				return;
			}
			m_messagesOut++;
			try
			{
				switch (m_backend)
				{
					case HtmlPanelBackend.UniWebView:
						RunMobileJs(js);
						break;
					case HtmlPanelBackend.Vuplex:
						RunDesktopJs(js);
						break;
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning("[HtmlPanel] ExecuteJavaScript 失败: " + ex.Message);
			}
		}

		/// <summary>显示/隐藏页面本体（移动端是原生视图，必须显式 Show/Hide）。</summary>
		public void SetVisible(bool visible)
		{
			m_visible = visible;
			if (m_disposed)
			{
				return;
			}
			try
			{
				switch (m_backend)
				{
					case HtmlPanelBackend.UniWebView:
						SetMobileVisible(visible);
						break;
					case HtmlPanelBackend.Vuplex:
						SetDesktopVisible(visible);
						break;
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning("[HtmlPanel] 切换可见性失败: " + ex.Message);
			}
		}

		/// <summary>
		/// 让后端把原生视图的屏幕矩形重新对齐到 RectTransform。
		/// 移动端（UniWebView）只在方向/分辨率变化时自动重算，拖动窗口后必须手动调用；桌面端不需要。
		/// </summary>
		public void SyncFrame()
		{
			if (m_disposed)
			{
				return;
			}
			try
			{
				SyncBackendFrame();
			}
			catch (Exception ex)
			{
				Debug.LogWarning("[HtmlPanel] 同步页面矩形失败: " + ex.Message);
			}
		}

		private void SyncBackendFrame()
		{
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
			if (m_mobile != null)
			{
				m_mobile.UpdateFrame();
			}
#endif
		}

		private void OnDestroy()
		{
			m_disposed = true;
			MessageReceived = null;
			Ready = null;
			PageLoaded = null;
			Failed = null;
		}

		private void RaiseMessage(string text)
		{
			if (m_disposed || string.IsNullOrEmpty(text))
			{
				return;
			}
			m_messagesIn++;
			try
			{
				MessageReceived?.Invoke(text);
			}
			catch (Exception ex)
			{
				Debug.LogError("[HtmlPanel] MessageReceived 回调异常: " + ex);
			}
		}

		private void RaisePageLoaded(string url)
		{
			m_pageLoaded = true;
			try
			{
				PageLoaded?.Invoke(url ?? string.Empty);
			}
			catch (Exception ex)
			{
				Debug.LogError("[HtmlPanel] PageLoaded 回调异常: " + ex);
			}
		}

		// ─────────────────────────── 移动端（UniWebView） ───────────────────────────

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
		private UniWebView m_mobile;
		private bool m_mobileUrlMode;

		private void CreateMobileBackend()
		{
			try
			{
				m_mobile = gameObject.AddComponent<UniWebView>();
				m_mobile.ReferenceRectTransform = m_rect;
				m_mobile.SetBackButtonEnabled(false);
				m_mobile.BackgroundColor = m_background;
				try
				{
					m_mobile.SetShowSpinnerWhileLoading(false);
				}
				catch (Exception)
				{
					// 老版本没有这个 API，忽略。
				}
				try
				{
					// 页面来自持久化目录（file://...），Android 11+ 默认屏蔽 file 访问，这里显式打开。
					m_mobile.SetAllowFileAccess(true);
					m_mobile.SetAllowFileAccessFromFileURLs(true);
				}
				catch (Exception ex)
				{
					Debug.LogWarning("[HtmlPanel] 打开本地文件访问失败: " + ex.Message);
				}
				m_mobile.OnMessageReceived += OnMobileMessage;
				m_mobile.OnPageFinished += OnMobilePageFinished;
				m_mobile.OnLoadingErrorReceived += OnMobileLoadError;
				m_mobile.OnShouldClose += _ => false;
				m_backend = HtmlPanelBackend.UniWebView;
				m_backendInitialized = true;
			}
			catch (Exception ex)
			{
				m_backend = HtmlPanelBackend.None;
				Fail("UniWebView 初始化失败: " + ex.Message);
			}
		}

		private void LoadMobile(string html, string baseUrl)
		{
			if (m_mobile == null)
			{
				Fail("UniWebView 组件缺失");
				return;
			}
			string url = string.IsNullOrEmpty(baseUrl) ? "file:///" : baseUrl;
			// 面板是运行时用代码搭出来的，Attach 那一刻 Canvas 还没完成布局，
			// ReferenceRectTransform 算出来的 frame 可能是 0x0（原生视图因此不可见）。
			// 等布局稳定后再同步一次，页面加载完再同步一次。
			StartCoroutine(DeferredFrameSync());
			m_mobile.LoadHTMLString(html, url, false);
		}

		private void LoadMobileUrl(string url)
		{
			if (m_mobile == null)
			{
				Fail("UniWebView 组件缺失");
				return;
			}
			m_mobileUrlMode = true;
			// HTTP 页面不能像 LoadHtml 那样把 shim 拼进标记，改成让 UniWebView
			// 在页面开始加载时注入初始化脚本；页面加载完成后还会幂等补一次。
			try
			{
				m_mobile.AddJavaScript(MobileShimJs);
			}
			catch (Exception ex)
			{
				Debug.LogWarning("[HtmlPanel] 注入移动端消息桥失败: " + ex.Message);
			}
			StartCoroutine(DeferredFrameSync());
			// skipEncoding=true：保留 URL 里的 #bpsim-token=... fragment。
			m_mobile.Load(url, true);
		}

		private IEnumerator DeferredFrameSync()
		{
			yield return null;
			yield return null;
			yield return null;
			if (m_mobile != null)
			{
				m_mobile.UpdateFrame();
				Debug.Log("[HtmlPanel] 布局后同步 frame=" + m_mobile.Frame);
			}
		}

		private void RunMobileJs(string js)
		{
			if (m_mobile != null)
			{
				m_mobile.EvaluateJavaScript(js);
			}
		}

		private void SetMobileVisible(bool visible)
		{
			if (m_mobile == null)
			{
				return;
			}
			if (visible && m_pageLoaded)
			{
				m_mobile.Show();
			}
			else
			{
				m_mobile.Hide();
			}
		}

		private void OnMobileMessage(UniWebView view, UniWebViewMessage message)
		{
			// UniWebViewMessage 是 struct，没有 null 可判；Args 在解析失败时才可能为 null。
			string text = null;
			if (message.Args != null && message.Path == MobileMessagePath)
			{
				message.Args.TryGetValue(MobileDataArgument, out text);
			}
			if (string.IsNullOrEmpty(text))
			{
				text = message.RawMessage;
			}
			RaiseMessage(text);
		}

		private void OnMobilePageFinished(UniWebView view, int statusCode, string url)
		{
			if (m_mobile == null)
			{
				return;
			}
			m_mobile.UpdateFrame();
			if (m_mobileUrlMode)
			{
				// 确保 window.vuplex 一定存在（MobileShimJs 自身幂等，重复执行无副作用）。
				try
				{
					m_mobile.EvaluateJavaScript(MobileShimJs);
				}
				catch (Exception ex)
				{
					Debug.LogWarning("[HtmlPanel] 补注入移动端消息桥失败: " + ex.Message);
				}
			}
			// 顺序要紧：RaisePageLoaded 才会把 m_pageLoaded 置 true，
			// 而 SetMobileVisible 用 m_pageLoaded 判断能否 Show。反了就会在加载完成时反过来 Hide。
			RaisePageLoaded(url);
			SetMobileVisible(m_visible);
			Debug.Log("[HtmlPanel] 移动端 frame=" + m_mobile.Frame + " visible=" + m_visible
				+ " rect=" + m_rect.rect + " screen=" + Screen.width + "x" + Screen.height);
		}

		private void OnMobileLoadError(UniWebView view, int code, string errorMessage,
			UniWebViewNativeResultPayload payload)
		{
			Fail("页面加载失败(" + code + "): " + errorMessage);
		}
#else
		private void CreateMobileBackend()
		{
			m_backend = HtmlPanelBackend.None;
			Fail("当前构建目标没有 UniWebView 后端");
		}

		private void LoadMobile(string html, string baseUrl)
		{
			Fail("当前构建目标没有 UniWebView 后端");
		}

		private void LoadMobileUrl(string url)
		{
			Fail("当前构建目标没有 UniWebView 后端");
		}

		private void RunMobileJs(string js)
		{
		}

		private void SetMobileVisible(bool visible)
		{
		}
#endif

		// ─────────────────────────── 桌面端（Vuplex） ───────────────────────────

#if UNITY_EDITOR || UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX
		private CanvasWebViewPrefab m_desktopPrefab;
		private IWebView m_desktopView;

		private void CreateDesktopBackend()
		{
			try
			{
#if UNITY_EDITOR
				// 编辑器里只装了 Standalone 插件；没有 Android/iOS 后端时别让 Vuplex 抛异常，退回 Mock。
				Vuplex.WebView.Internal.WebPluginFactory.IgnoreMissingPluginInEditor = true;
#endif
				m_desktopPrefab = CanvasWebViewPrefab.Instantiate();
				m_desktopPrefab.KeyboardEnabled = m_keyboardEnabled;
				m_desktopPrefab.LogConsoleMessages = false;
				RectTransform vr = (RectTransform)m_desktopPrefab.transform;
				vr.SetParent(m_rect, false);
				vr.anchorMin = Vector2.zero;
				vr.anchorMax = Vector2.one;
				vr.offsetMin = Vector2.zero;
				vr.offsetMax = Vector2.zero;
				vr.localScale = Vector3.one;
				m_desktopPrefab.Initialized += OnDesktopInitialized;
				if (m_keyboardEnabled)
				{
					// Vuplex 内置键盘链路只认旧 Input，本工程只有新输入系统，靠这个桥（见类注释）。
					Keyboard = WebViewKeyboardBridge.Attach(m_desktopPrefab);
				}
				m_backend = HtmlPanelBackend.Vuplex;
			}
			catch (Exception ex)
			{
				m_backend = HtmlPanelBackend.None;
				Fail("Vuplex 初始化失败: " + ex.Message);
			}
		}

		private void LoadDesktop(string html)
		{
			if (m_desktopView == null)
			{
				Fail("Vuplex WebView 为空");
				return;
			}
			m_desktopView.LoadHtml(html);
		}

		private void LoadDesktopUrl(string url)
		{
			if (m_desktopView == null)
			{
				Fail("Vuplex WebView 为空");
				return;
			}
			m_desktopView.LoadUrl(url);
		}

		private void RunDesktopJs(string js)
		{
			if (m_desktopView != null)
			{
				m_desktopView.ExecuteJavaScript(js, null);
			}
		}

		private void SetDesktopVisible(bool visible)
		{
			if (m_desktopPrefab != null)
			{
				m_desktopPrefab.gameObject.SetActive(visible);
			}
		}

		private void OnDesktopInitialized(object sender, EventArgs e)
		{
			if (m_disposed)
			{
				return;
			}
			m_desktopView = m_desktopPrefab != null ? m_desktopPrefab.WebView : null;
			if (m_desktopView == null)
			{
				Fail("Vuplex 初始化后 WebView 为空");
				return;
			}
			m_desktopView.MessageEmitted += OnDesktopMessage;
			m_desktopView.LoadFailed += OnDesktopLoadFailed;
			m_desktopView.LoadProgressChanged += OnDesktopProgress;
			m_backendInitialized = true;
			FireReady();
		}

		private void OnDesktopMessage(object sender, EventArgs<string> args)
		{
			RaiseMessage(args?.Value);
		}

		private void OnDesktopLoadFailed(object sender, LoadFailedEventArgs args)
		{
			Fail("页面加载失败: " + (args != null ? args.NativeErrorCode : "unknown"));
		}

		private void OnDesktopProgress(object sender, ProgressChangedEventArgs args)
		{
			if (args != null && args.Type == ProgressChangeType.Finished)
			{
				RaisePageLoaded(string.Empty);
			}
		}
#else
		private void CreateDesktopBackend()
		{
			m_backend = HtmlPanelBackend.None;
			Fail("当前构建目标没有 Vuplex 后端");
		}

		private void LoadDesktop(string html)
		{
			Fail("当前构建目标没有 Vuplex 后端");
		}

		private void LoadDesktopUrl(string url)
		{
			Fail("当前构建目标没有 Vuplex 后端");
		}

		private void RunDesktopJs(string js)
		{
		}

		private void SetDesktopVisible(bool visible)
		{
		}
#endif

		// ─────────────────────────── 公共：移动端 shim ───────────────────────────

		/// <summary>
		/// 移动端把 <c>window.vuplex.postMessage</c> 翻译成 URL scheme 导航。
		/// 位置放在 &lt;head&gt; 之后而不是文档最前面：避免把 &lt;!DOCTYPE&gt; 顶下去导致怪异模式。
		/// </summary>
		private static string InjectMobileShim(string html)
		{
			if (html.IndexOf("__bpsimMobileShim", StringComparison.Ordinal) >= 0)
			{
				return html;
			}
			string block = "<script>" + MobileShimJs + "</script>";
			int head = html.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
			if (head >= 0)
			{
				int close = html.IndexOf('>', head);
				if (close > 0)
				{
					return html.Insert(close + 1, block);
				}
			}
			int htmlTag = html.IndexOf("<html", StringComparison.OrdinalIgnoreCase);
			if (htmlTag >= 0)
			{
				int close = html.IndexOf('>', htmlTag);
				if (close > 0)
				{
					return html.Insert(close + 1, block);
				}
			}
			return block + html;
		}

		/// <summary>
		/// 移动端的 <c>window.vuplex</c> 垫片：把 postMessage 转成自定义 scheme，
		/// 顺带补一个最小 addEventListener，让只认 Vuplex 事件风格的老页面不会直接报错。
		/// </summary>
		private const string MobileShimJs =
			"(function(){/*__bpsimMobileShim*/\n" +
			"if(window.vuplex&&window.vuplex.postMessage){return;}\n" +
			"var seq=0,listeners=[],queue=[],busy=false;\n" +
			"function flush(){if(busy||!queue.length){return;}busy=true;var text=queue.shift();\n" +
			"  try{location.href='uniwebview://message?data='+encodeURIComponent(text)+'&seq='+(++seq);}catch(e){}\n" +
			"  setTimeout(function(){busy=false;flush();},16);}\n" +
			"function post(text){try{\n" +
			"  if(text===null||text===undefined){return;}\n" +
			"  if(typeof text!=='string'){text=JSON.stringify(text);}\n" +
			"  if(text===null||text===undefined){return;}\n" +
			"  queue.push(String(text));flush();\n" +
			"}catch(e){}}\n" +
			"window.vuplex={\n" +
			"  postMessage:post,\n" +
			"  addEventListener:function(ch,fn){if(ch==='message'&&typeof fn==='function'){listeners.push(fn);}},\n" +
			"  removeEventListener:function(ch,fn){for(var i=listeners.length-1;i>=0;i--){if(listeners[i]===fn){listeners.splice(i,1);}}},\n" +
			"  __bpsimDispatch:function(text){for(var i=0;i<listeners.length;i++){try{listeners[i]({data:text});}catch(e){}}}\n" +
			"};\n" +
			"})();";
	}
}
