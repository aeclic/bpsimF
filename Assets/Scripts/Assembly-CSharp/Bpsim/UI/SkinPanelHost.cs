using System;
using System.Collections.Generic;
using System.IO;
using Bpsim.Parts;
using Bpsim.Parts.Gameplay;
using Bpsim.Parts.Skin;
using Newtonsoft.Json.Linq;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using Vuplex.WebView;

namespace Bpsim.UI
{
	/// <summary>
	/// 蒙皮参数面板：用 Vuplex 的 3D WebView 承载 <c>StreamingAssets/WebPanels/skin.html</c>。
	/// </summary>
	/// <remarks>
	/// 这里只做「网页 ↔ 组件数据」的桥：
	/// 网页发来的数值写进 <see cref="SkinSheet"/> 并把 <c>Version</c> 加一，
	/// 网格与凸包仍由 <c>SkinSheetSystem</c> 按 Version 重建，
	/// 本类不持有几何数据、也不缓存 Mesh。
	/// </remarks>
	public sealed class SkinPanelHost : MonoBehaviour
	{
		private const int MaxTextureThumbnails = 24;

		private const int MaxThumbnailBytes = 262144;

		private static SkinPanelHost s_instance;

		private Entity m_part = Entity.Null;

		private GameObject m_root;

		private CanvasWebViewPrefab m_webView;
		private WebViewKeyboardBridge m_keyboardBridge;

		private bool m_ready;

		private string m_lastMessage = "none";

		private string m_lastError = string.Empty;

		public static SkinPanelHost Instance
		{
			get
			{
				if (s_instance == null)
				{
					GameObject host = new GameObject("SkinPanelHost");
					s_instance = host.AddComponent<SkinPanelHost>();
				}
				return s_instance;
			}
		}

		public bool IsOpen => m_root != null && m_root.activeSelf;

		/// <summary>底层 webview（QA 里用 ExecuteJavaScript 驱动页面时用）。</summary>
		public IWebView WebView => m_webView == null ? null : m_webView.WebView;

		/// <summary>QA/日志用的一行状态。</summary>
		public string Describe()
		{
			int textures = EnumerateTextureIds().Count;
			string part = m_part == Entity.Null ? "none" : m_part.Index.ToString();
			string webView = m_webView == null ? "null" : (m_webView.WebView == null ? "creating" : "ready");
			return "skinpanel open=" + IsOpen + " pageReady=" + m_ready + " webview=" + webView + " part=" + part + " textures=" + textures + " last=" + m_lastMessage + (m_lastError.Length == 0 ? string.Empty : " err=" + m_lastError);
		}

		public void Open(Entity part)
		{
			if (part == Entity.Null)
			{
				return;
			}
			m_part = part;
			if (m_root == null)
			{
				BuildUi();
			}
			m_root.SetActive(true);
			if (m_ready)
			{
				PostState();
			}
			// 页面还没发 "ready" 时不用记标记：ready 分支自己会 PostState()。
		}

		public void Close()
		{
			m_part = Entity.Null;
			if (m_root != null)
			{
				m_root.SetActive(false);
			}
		}

		private void Awake()
		{
			if (s_instance != null && s_instance != this)
			{
				Destroy(gameObject);
				return;
			}
			s_instance = this;
		}

		private void OnDestroy()
		{
			if (s_instance == this)
			{
				s_instance = null;
			}
			if (m_root != null)
			{
				Destroy(m_root);
			}
		}

		private void BuildUi()
		{
			m_root = new GameObject("SkinPanelCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
			Canvas canvas = m_root.GetComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 32000;
			CanvasScaler scaler = m_root.GetComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1920f, 1080f);
			scaler.matchWidthOrHeight = 0.5f;

			GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
			RectTransform rect = panel.GetComponent<RectTransform>();
			rect.SetParent(m_root.transform, false);
			rect.anchorMin = new Vector2(1f, 0f);
			rect.anchorMax = new Vector2(1f, 0f);
			rect.pivot = new Vector2(1f, 0f);
			rect.sizeDelta = new Vector2(430f, 640f);
			rect.anchoredPosition = new Vector2(-16f, 16f);
			Image background = panel.GetComponent<Image>();
			background.color = new Color(0.07f, 0.078f, 0.1f, 0.98f);

			m_webView = CanvasWebViewPrefab.Instantiate();
			// 桌面端用真实键盘，不需要 3D WebView 的软键盘：关掉它既省掉 CanvasKeyboard 预制体，
			// 也避开 KeyboardManager 尚未初始化时的空引用（BaseWebViewPrefab.Update 每帧都会走那条路）。
			m_webView.KeyboardEnabled = false;
			m_webView.LogConsoleMessages = false;
			RectTransform viewRect = (RectTransform)m_webView.transform;
			viewRect.SetParent(rect, false);
			viewRect.anchorMin = Vector2.zero;
			viewRect.anchorMax = Vector2.one;
			viewRect.offsetMin = new Vector2(3f, 3f);
			viewRect.offsetMax = new Vector2(-3f, -3f);
			viewRect.localScale = Vector3.one;
			m_webView.Initialized += OnWebViewInitialized;
			// 3D WebView 自带的键盘转发只认旧 Input，本工程只有新输入系统（它会在
			// NativeKeyboardListener.Awake 里直接放弃），所以键帽全由这个桥从 InputSystem 转过去。
			m_keyboardBridge = WebViewKeyboardBridge.Attach(m_webView);
		}

		private void OnWebViewInitialized(object sender, EventArgs args)
		{
			IWebView webView = m_webView.WebView;
			webView.MessageEmitted += OnMessageEmitted;
			webView.LoadHtml(BuildPage());
		}

		/// <summary>html/css/js 拼成一份自包含页面，走 <c>LoadHtml</c> 打开。</summary>
		private static string BuildPage()
		{
			string directory = Path.Combine(Application.streamingAssetsPath, "WebPanels");
			string html = ReadText(Path.Combine(directory, "skin.html"), "<html><body>skin.html missing</body></html>");
			string css = ReadText(Path.Combine(directory, "skin.css"), string.Empty);
			string js = ReadText(Path.Combine(directory, "skin.js"), string.Empty);
			html = html.Replace("<!--SKIN_CSS-->", "<style>" + css + "</style>");
			html = html.Replace("<!--SKIN_JS-->", "<script>" + js + "</script>");
			return html;
		}

		private static string ReadText(string path, string fallback)
		{
			try
			{
				return File.Exists(path) ? File.ReadAllText(path) : fallback;
			}
			catch (Exception exception)
			{
				Debug.LogWarning("[SkinPanel] read failed " + path + ": " + exception.Message);
				return fallback;
			}
		}

		private void OnMessageEmitted(object sender, EventArgs<string> args)
		{
			JObject message;
			try
			{
				message = JObject.Parse(args.Value);
			}
			catch (Exception exception)
			{
				Debug.LogWarning("[SkinPanel] bad message: " + exception.Message + " :: " + args.Value);
				return;
			}
			string type = (string)message["t"];
			m_lastMessage = type ?? "(null)";
			Debug.Log("[SkinPanel] recv " + m_lastMessage);
			if (type == "error")
			{
				m_lastError = (string)message["message"] ?? string.Empty;
			}
			if (type == "ready")
			{
				m_ready = true;
				if (m_part != Entity.Null)
				{
					PostState();
				}
				return;
			}
			if (type == "close")
			{
				Close();
				return;
			}
			if (type == "texture")
			{
				ApplyTexture((string)message["id"]);
				return;
			}
			if (type == "apply")
			{
				Apply(message["sheet"] as JObject);
			}
		}

		private bool TryGetSheet(out EntityManager em, out SkinSheet sheet)
		{
			em = PartGameplayUtil.Em;
			sheet = default;
			if (m_part == Entity.Null || em.World == null || !em.World.IsCreated || !em.Exists(m_part))
			{
				return false;
			}
			if (!em.HasComponent<SkinSheet>(m_part))
			{
				return false;
			}
			sheet = em.GetComponentData<SkinSheet>(m_part);
			return true;
		}

		private void Apply(JObject data)
		{
			if (data == null || !TryGetSheet(out EntityManager em, out SkinSheet sheet))
			{
				Debug.LogWarning("[SkinPanel] no skin part to edit");
				return;
			}
			sheet.BottomLeft = ReadVector(data["bl"], sheet.BottomLeft);
			sheet.BottomRight = ReadVector(data["br"], sheet.BottomRight);
			sheet.TopRight = ReadVector(data["tr"], sheet.TopRight);
			sheet.TopLeft = ReadVector(data["tl"], sheet.TopLeft);
			sheet.Curve = ReadFloat4(data["curve"], sheet.Curve);
			sheet.Along = ReadFloat4(data["along"], sheet.Along);
			sheet.TextureMode = Mathf.Clamp(data["mode"] == null ? sheet.TextureMode : (int)data["mode"], 0, 1);
			sheet.TileX = Mathf.Max(0.01f, data["tileX"] == null ? sheet.TileX : (float)data["tileX"]);
			sheet.TileY = Mathf.Max(0.01f, data["tileY"] == null ? sheet.TileY : (float)data["tileY"]);
			sheet.Color = ReadFloat4(data["color"], sheet.Color);
			sheet.UVRect = ReadUVRect(data["uvRect"], sheet.UVRect);
			sheet.Version++;
			em.SetComponentData(m_part, sheet);
			PostApplied(sheet.Version);
			Debug.Log("[SkinPanel] skin updated version=" + sheet.Version);
		}

		private void ApplyTexture(string id)
		{
			if (!TryGetSheet(out EntityManager em, out SkinSheet sheet))
			{
				return;
			}
			sheet.TextureHandle = string.IsNullOrEmpty(id) ? SkinSheetRegistry.NoTexture : SkinSheetRegistry.Instance.GetOrLoadTexture(id);
			sheet.Version++;
			em.SetComponentData(m_part, sheet);
			PostApplied(sheet.Version);
		}

		private void PostApplied(int version)
		{
			Post(new JObject
			{
				["t"] = "applied",
				["version"] = version
			});
			PostState();
		}

		private void PostState()
		{
			if (!TryGetSheet(out _, out SkinSheet sheet))
			{
				return;
			}
			string current = sheet.TextureHandle > 0 ? SkinSheetRegistry.Instance.GetTextureId(sheet.TextureHandle) : null;
			JArray textureArray = new JArray();
			JArray images = new JArray();
			int thumbnails = 0;
			foreach (string id in EnumerateTextureIds())
			{
				textureArray.Add(new JObject { ["id"] = id });
				if (thumbnails >= MaxTextureThumbnails)
				{
					continue;
				}
				string dataUri = ReadThumbnail(id);
				if (dataUri != null)
				{
					images.Add(new JArray(id, dataUri));
					thumbnails++;
				}
			}

			Post(new JObject
			{
				["t"] = "state",
				["version"] = sheet.Version,
				["texture"] = current ?? string.Empty,
				["sheet"] = new JObject
				{
					["bl"] = Write(sheet.BottomLeft),
					["br"] = Write(sheet.BottomRight),
					["tr"] = Write(sheet.TopRight),
					["tl"] = Write(sheet.TopLeft),
					["curve"] = Write(sheet.Curve),
					["along"] = Write(sheet.Along),
					["mode"] = sheet.TextureMode,
					["tileX"] = (double)sheet.TileX,
					["tileY"] = (double)sheet.TileY,
					["uvRect"] = Write(sheet.UVRect),
					["color"] = Write(sheet.Color)
				},
				["textures"] = textureArray,
				["images"] = images
			});
		}

		private static List<string> EnumerateTextureIds()
		{
			List<string> ids = new List<string>();
			try
			{
				string directory = SkinSheetRegistry.TextureDirectory;
				if (!Directory.Exists(directory))
				{
					return ids;
				}
				foreach (string path in Directory.GetFiles(directory, "*.png"))
				{
					string id = Path.GetFileNameWithoutExtension(path);
					if (SkinSheetRegistry.IsValidTextureId(id))
					{
						ids.Add(id);
					}
				}
				ids.Sort(StringComparer.Ordinal);
			}
			catch (Exception exception)
			{
				Debug.LogWarning("[SkinPanel] list textures failed: " + exception.Message);
			}
			return ids;
		}

		private static string ReadThumbnail(string id)
		{
			try
			{
				string path = Path.Combine(SkinSheetRegistry.TextureDirectory, id + ".png");
				if (!File.Exists(path) || new FileInfo(path).Length > MaxThumbnailBytes)
				{
					return null;
				}
				return "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(path));
			}
			catch (Exception exception)
			{
				Debug.LogWarning("[SkinPanel] thumbnail failed: " + exception.Message);
				return null;
			}
		}

		private void Post(JObject payload)
		{
			if (m_webView == null || m_webView.WebView == null)
			{
				return;
			}
			m_webView.WebView.PostMessage(payload.ToString(Newtonsoft.Json.Formatting.None));
		}

		private static float2 ReadVector(JToken token, float2 fallback)
		{
			if (token is JArray array && array.Count >= 2)
			{
				return new float2((float)array[0], (float)array[1]);
			}
			return fallback;
		}

		private static float4 ReadFloat4(JToken token, float4 fallback)
		{
			if (token is JArray array && array.Count >= 4)
			{
				return new float4((float)array[0], (float)array[1], (float)array[2], (float)array[3]);
			}
			return fallback;
		}

		/// <summary>读取图集子矩形（<c>x, y, width, height</c>），并钳到 0..1。</summary>
		private static float4 ReadUVRect(JToken token, float4 fallback)
		{
			float4 rect = ReadFloat4(token, fallback);
			float x = math.clamp(rect.x, 0f, 1f);
			float y = math.clamp(rect.y, 0f, 1f);
			float w = math.clamp(rect.z, 0.001f, 1f - x);
			float h = math.clamp(rect.w, 0.001f, 1f - y);
			return new float4(x, y, w, h);
		}

		private static JArray Write(float2 value)
		{
			return new JArray((double)value.x, (double)value.y);
		}

		private static JArray Write(float4 value)
		{
			return new JArray((double)value.x, (double)value.y, (double)value.z, (double)value.w);
		}
	}
}
