using System;
using Bpsim.Mods;
using Bpsim.Parts.Skin;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Bpsim.UI
{
	/// <summary>
	/// 图集局部裁剪窗口。交互与 BPRC 的 skin-crop 一致：像素坐标从左上角起、
	/// 拖动框选，Enter 使用裁剪；外观沿用用户设置面板的标题栏、圆角与按钮。
	/// </summary>
	public sealed class SkinAtlasCropDialog : MonoBehaviour
	{
		private enum DragMode
		{
			None,
			New,
			Move
		}

		private static readonly Color PanelColor = new Color(0.15f, 0.15f, 0.15f, 0.99f);
		private static readonly Color HeaderColor = new Color(0f, 0.2f, 0.4f, 1f);
		private static readonly Color FieldColor = new Color(0.075f, 0.085f, 0.11f, 1f);
		private static readonly Color PreviewBack = new Color(0.04f, 0.05f, 0.07f, 1f);
		private static readonly Color Accent = new Color(0.12f, 0.52f, 0.74f, 1f);

		private const float SidebarWidth = 288f;
		private const float SidebarGap = 12f;

		private static SkinAtlasCropDialog s_instance;

		private GameObject m_root;
		private RectTransform m_panel;
		private RawImage m_image;
		private RectTransform m_imageView;
		private RectTransform m_view;
		private RectTransform m_selection;
		private InputField m_x;
		private InputField m_y;
		private InputField m_width;
		private InputField m_height;
		private Text m_name;
		private Text m_dimensions;
		private Text m_status;
		private Texture2D m_source;
		private Action<string> m_apply;
		private bool m_built;

		private int m_cropX;
		private int m_cropY;
		private int m_cropWidth;
		private int m_cropHeight;
		private int m_previousX;
		private int m_previousY;
		private int m_previousWidth;
		private int m_previousHeight;
		private DragMode m_dragMode;
		private Vector2 m_dragStartPixel;
		private Vector4 m_dragStartRect;
		private Vector2 m_lastViewSize;

		public static SkinAtlasCropDialog Instance
		{
			get
			{
				if (s_instance == null)
				{
					GameObject host = new GameObject("SkinAtlasCropWindow");
					DontDestroyOnLoad(host);
					s_instance = host.AddComponent<SkinAtlasCropDialog>();
				}
				return s_instance;
			}
		}

		public bool IsOpen => m_root != null && m_root.activeSelf;

		public void Open(Texture2D source, Action<string> onApply)
		{
			if (source == null) return;
			if (!m_built) BuildUi();
			m_source = source;
			m_apply = onApply;
			m_image.texture = source;
			m_image.enabled = true;
			m_name.text = source.name;
			m_dimensions.text = source.width + " × " + source.height + " px";
			m_status.text = "拖动框选裁剪区域；拖动框内可移动；Enter 使用裁剪，Esc 取消";
			m_root.SetActive(true);
			Canvas.ForceUpdateCanvases();
			LayoutImage();
			SetRect(0, 0, source.width, source.height);
			m_lastViewSize = m_view.rect.size;
		}

		public void Close()
		{
			m_dragMode = DragMode.None;
			m_apply = null;
			m_source = null;
			if (m_root != null) m_root.SetActive(false);
		}

		private void Update()
		{
			if (!IsOpen) return;
			if (UiKeys.EscapePressed)
			{
				Close();
				return;
			}
			if (UiKeys.EnterPressed)
			{
				ApplyCrop();
				return;
			}
			if (m_view != null && m_source != null && m_view.rect.size != m_lastViewSize)
			{
				m_lastViewSize = m_view.rect.size;
				LayoutImage();
				SetRect(m_cropX, m_cropY, m_cropWidth, m_cropHeight);
			}
		}

		private void BuildUi()
		{
			Canvas canvas = ModUiKit.CreateCanvas("SkinAtlasCropCanvas", 32020);
			m_root = canvas.gameObject;
			m_root.SetActive(false);

			// 半透明遮罩：点空白处关闭。
			RectTransform backdrop = ModUiKit.Panel(canvas.transform, new Color(0f, 0f, 0f, 0.55f), 2);
			SetRect(backdrop, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
			Button backdropButton = backdrop.gameObject.AddComponent<Button>();
			backdropButton.transition = Selectable.Transition.None;
			backdropButton.targetGraphic = backdrop.GetComponent<Image>();
			backdropButton.onClick.AddListener(Close);

			m_panel = ModUiKit.Panel(canvas.transform, PanelColor, 10);
			m_panel.anchorMin = m_panel.anchorMax = new Vector2(0.5f, 0.5f);
			m_panel.pivot = new Vector2(0.5f, 0.5f);
			m_panel.sizeDelta = new Vector2(1180f, 760f);
			m_panel.anchoredPosition = Vector2.zero;

			RectTransform header = ModUiKit.Panel(m_panel, HeaderColor, 10);
			SetRect(header, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -60f), Vector2.zero);
			Text title = ModUiKit.Label(header, "图片切片选择器", 22, Color.white);
			title.fontStyle = FontStyle.Bold;
			SetRect(title.rectTransform, Vector2.zero, Vector2.one, new Vector2(22f, 0f), new Vector2(-72f, 0f));
			Button close = ModUiKit.Button(header, "×", new Color(0.22f, 0.25f, 0.30f, 1f), Close, 22);
			SetRight(close.GetComponent<RectTransform>(), 10f, 42f, 40f);

			RectTransform body = new GameObject("Body", typeof(RectTransform)).GetComponent<RectTransform>();
			body.SetParent(m_panel, false);
			SetRect(body, Vector2.zero, Vector2.one, new Vector2(16f, 68f), new Vector2(-16f, -72f));

			BuildSidebar(body);
			BuildPreview(body);
			BuildFooter();
			m_built = true;
		}

		private void BuildSidebar(RectTransform body)
		{
			RectTransform sidebar = ModUiKit.Panel(body, ModUiKit.CardBg, 8);
			SetRect(sidebar, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(SidebarWidth, 0f));

			Caption(sidebar, "当前贴图", 16f, 12f, 254f, 20f);
			m_name = ModUiKit.Label(sidebar, "—", 15, ModUiKit.TextMain);
			Fixed(m_name.rectTransform, 16f, 34f, 254f, 22f);

			Caption(sidebar, "原图尺寸", 16f, 68f, 254f, 20f);
			m_dimensions = ModUiKit.Label(sidebar, "—", 15, ModUiKit.TextMain);
			Fixed(m_dimensions.rectTransform, 16f, 90f, 254f, 22f);

			Caption(sidebar, "裁剪区域（像素，左上原点）", 16f, 132f, 254f, 20f);
			Caption(sidebar, "X", 16f, 150f, 118f, 16f);
			Caption(sidebar, "Y", 148f, 150f, 118f, 16f);
			m_x = Field(sidebar, 16f, 168f, 118f, "X");
			m_y = Field(sidebar, 148f, 168f, 118f, "Y");
			Caption(sidebar, "宽", 16f, 208f, 118f, 16f);
			Caption(sidebar, "高", 148f, 208f, 118f, 16f);
			m_width = Field(sidebar, 16f, 226f, 118f, "宽");
			m_height = Field(sidebar, 148f, 226f, 118f, "高");

			Text hint = ModUiKit.Label(sidebar, "在右侧图上拖拽框选区域，拖动框内可平移选区。", 12, ModUiKit.TextDim);
			hint.horizontalOverflow = HorizontalWrapMode.Wrap;
			hint.verticalOverflow = VerticalWrapMode.Overflow;
			Fixed(hint.rectTransform, 16f, 276f, 254f, 60f);
		}

		private void BuildPreview(RectTransform body)
		{
			RectTransform preview = ModUiKit.Panel(body, PreviewBack, 8);
			SetRect(preview, Vector2.zero, Vector2.one, new Vector2(SidebarWidth + SidebarGap, 0f), Vector2.zero);
			preview.gameObject.AddComponent<RectMask2D>();

			RectTransform view = new GameObject("View", typeof(RectTransform)).GetComponent<RectTransform>();
			view.SetParent(preview, false);
			SetRect(view, Vector2.zero, Vector2.one, new Vector2(16f, 16f), new Vector2(-16f, -16f));
			m_view = view;

			GameObject imageGo = new GameObject("ImageView", typeof(RectTransform), typeof(RawImage), typeof(CropSurface));
			m_imageView = imageGo.GetComponent<RectTransform>();
			m_imageView.SetParent(view, false);
			m_imageView.anchorMin = m_imageView.anchorMax = new Vector2(0.5f, 0.5f);
			m_imageView.pivot = new Vector2(0.5f, 0.5f);
			m_imageView.anchoredPosition = Vector2.zero;
			m_image = imageGo.GetComponent<RawImage>();
			m_image.color = Color.white;
			m_image.raycastTarget = true;
			imageGo.GetComponent<CropSurface>().Owner = this;

			GameObject selectionGo = new GameObject("Selection", typeof(RectTransform), typeof(Image));
			m_selection = selectionGo.GetComponent<RectTransform>();
			m_selection.SetParent(m_imageView, false);
			Image fill = selectionGo.GetComponent<Image>();
			fill.color = new Color(0.20f, 0.75f, 0.95f, 0.16f);
			fill.raycastTarget = false;

			// Outline 的四份偏移拷贝不受 Image alpha 影响，会把选框填成实色，
			// 这里改用四条 2px 边线画选框。
			Color border = new Color(0.35f, 0.85f, 1f, 1f);
			Edge(m_selection, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -2f), Vector2.zero, border);
			Edge(m_selection, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 2f), border);
			Edge(m_selection, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(2f, 0f), border);
			Edge(m_selection, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-2f, 0f), Vector2.zero, border);
		}

		private static void Edge(RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color)
		{
			Image image = new GameObject("Edge", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
			image.transform.SetParent(parent, false);
			image.color = color;
			image.raycastTarget = false;
			SetRect(image.rectTransform, anchorMin, anchorMax, offsetMin, offsetMax);
		}

		private void BuildFooter()
		{
			RectTransform footer = new GameObject("Footer", typeof(RectTransform)).GetComponent<RectTransform>();
			footer.SetParent(m_panel, false);
			SetRect(footer, Vector2.zero, new Vector2(1f, 0f), new Vector2(16f, 12f), new Vector2(-16f, 56f));
			m_status = ModUiKit.Label(footer, "就绪", 14, ModUiKit.TextDim);
			SetRect(m_status.rectTransform, Vector2.zero, Vector2.one, new Vector2(4f, 0f), new Vector2(-360f, 0f));

			Button use = ModUiKit.Button(footer, "使用裁剪", Accent, ApplyCrop, 18);
			SetRight(use.GetComponent<RectTransform>(), 0f, 132f, 36f);
			Button cancel = ModUiKit.Button(footer, "取消", new Color(0.28f, 0.31f, 0.36f, 1f), Close, 18);
			SetRight(cancel.GetComponent<RectTransform>(), 140f, 96f, 36f);
			Button all = ModUiKit.Button(footer, "全选", new Color(0.24f, 0.27f, 0.31f, 1f), SelectAll, 18);
			SetRight(all.GetComponent<RectTransform>(), 244f, 96f, 36f);
		}

		private void SelectAll()
		{
			if (m_source == null) return;
			SetRect(0, 0, m_source.width, m_source.height);
		}

		// ── 缩放与坐标 ──────────────────────────────────────────────────────

		/// <summary>把图片按宽高比放进预览区域（letterbox），所有坐标都相对这块图。</summary>
		private void LayoutImage()
		{
			if (m_imageView == null || m_view == null || m_source == null) return;
			Vector2 area = m_view.rect.size;
			if (area.x <= 1f || area.y <= 1f) return;
			float scale = Mathf.Min(area.x / m_source.width, area.y / m_source.height);
			if (scale <= 0f || float.IsNaN(scale) || float.IsInfinity(scale)) scale = 1f;
			m_imageView.anchorMin = m_imageView.anchorMax = new Vector2(0.5f, 0.5f);
			m_imageView.pivot = new Vector2(0.5f, 0.5f);
			m_imageView.anchoredPosition = Vector2.zero;
			m_imageView.sizeDelta = new Vector2(m_source.width * scale, m_source.height * scale);
		}

		private void SetRect(int x, int y, int width, int height)
		{
			if (m_source == null || m_selection == null) return;
			int textureWidth = Mathf.Max(1, m_source.width);
			int textureHeight = Mathf.Max(1, m_source.height);
			x = Mathf.Clamp(x, 0, textureWidth - 1);
			y = Mathf.Clamp(y, 0, textureHeight - 1);
			width = Mathf.Clamp(width, 1, textureWidth - x);
			height = Mathf.Clamp(height, 1, textureHeight - y);

			m_cropX = x;
			m_cropY = y;
			m_cropWidth = width;
			m_cropHeight = height;

			m_selection.anchorMin = new Vector2((float)x / textureWidth, 1f - (float)(y + height) / textureHeight);
			m_selection.anchorMax = new Vector2((float)(x + width) / textureWidth, 1f - (float)y / textureHeight);
			m_selection.offsetMin = Vector2.zero;
			m_selection.offsetMax = Vector2.zero;

			if (m_x != null) m_x.SetTextWithoutNotify(x.ToString());
			if (m_y != null) m_y.SetTextWithoutNotify(y.ToString());
			if (m_width != null) m_width.SetTextWithoutNotify(width.ToString());
			if (m_height != null) m_height.SetTextWithoutNotify(height.ToString());
		}

		private void ApplyFields()
		{
			if (m_source == null) return;
			SetRect(Parse(m_x, 0), Parse(m_y, 0), Parse(m_width, m_source.width), Parse(m_height, m_source.height));
		}

		private static int Parse(InputField field, int fallback)
		{
			return field != null && int.TryParse(field.text, out int value) ? value : fallback;
		}

		private Vector2 ToPixel(Vector2 screenPosition)
		{
			if (m_imageView == null || m_source == null) return Vector2.zero;
			if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(m_imageView, screenPosition, null, out Vector2 local))
			{
				return Vector2.zero;
			}
			Rect rect = m_imageView.rect;
			float normalizedX = (local.x - rect.xMin) / Mathf.Max(1f, rect.width);
			float normalizedY = (rect.yMax - local.y) / Mathf.Max(1f, rect.height);
			return new Vector2(Mathf.Clamp(normalizedX * m_source.width, 0f, m_source.width), Mathf.Clamp(normalizedY * m_source.height, 0f, m_source.height));
		}

		// ── 拖选 ────────────────────────────────────────────────────────────

		private void OnSurfaceDown(PointerEventData eventData)
		{
			if (m_source == null) return;
			if (!RectTransformUtility.RectangleContainsScreenPoint(m_imageView, eventData.position, null)) return;
			Vector2 pixel = ToPixel(eventData.position);
			m_dragStartPixel = pixel;
			m_previousX = m_cropX;
			m_previousY = m_cropY;
			m_previousWidth = m_cropWidth;
			m_previousHeight = m_cropHeight;
			m_dragStartRect = new Vector4(m_cropX, m_cropY, m_cropWidth, m_cropHeight);
			bool inside = pixel.x >= m_cropX && pixel.x <= m_cropX + m_cropWidth && pixel.y >= m_cropY && pixel.y <= m_cropY + m_cropHeight;
			m_dragMode = inside ? DragMode.Move : DragMode.New;
			if (m_dragMode == DragMode.New)
			{
				SetRect(Mathf.RoundToInt(pixel.x), Mathf.RoundToInt(pixel.y), 1, 1);
			}
		}

		private void OnSurfaceDrag(PointerEventData eventData)
		{
			if (m_dragMode == DragMode.None || m_source == null) return;
			Vector2 pixel = ToPixel(eventData.position);
			if (m_dragMode == DragMode.Move)
			{
				int dx = Mathf.RoundToInt(pixel.x - m_dragStartPixel.x);
				int dy = Mathf.RoundToInt(pixel.y - m_dragStartPixel.y);
				SetRect(Mathf.RoundToInt(m_dragStartRect.x) + dx, Mathf.RoundToInt(m_dragStartRect.y) + dy, m_previousWidth, m_previousHeight);
			}
			else
			{
				int x0 = Mathf.RoundToInt(Mathf.Min(m_dragStartPixel.x, pixel.x));
				int y0 = Mathf.RoundToInt(Mathf.Min(m_dragStartPixel.y, pixel.y));
				int x1 = Mathf.RoundToInt(Mathf.Max(m_dragStartPixel.x, pixel.x));
				int y1 = Mathf.RoundToInt(Mathf.Max(m_dragStartPixel.y, pixel.y));
				SetRect(x0, y0, Mathf.Max(1, x1 - x0), Mathf.Max(1, y1 - y0));
			}
		}

		private void OnSurfaceUp(PointerEventData eventData)
		{
			if (m_dragMode == DragMode.New && (m_cropWidth < 4 || m_cropHeight < 4))
			{
				SetRect(m_previousX, m_previousY, m_previousWidth, m_previousHeight);
			}
			m_dragMode = DragMode.None;
		}

		// ── 裁剪 ────────────────────────────────────────────────────────────

		private void ApplyCrop()
		{
			if (m_source == null) return;
			ApplyFields();
			if (!EnsureReadable(m_source))
			{
				if (m_status != null) m_status.text = "贴图不可读，无法裁剪";
				return;
			}
			try
			{
				int textureHeight = m_source.height;
				Color[] pixels = m_source.GetPixels(m_cropX, textureHeight - m_cropY - m_cropHeight, m_cropWidth, m_cropHeight);
				Texture2D cropped = new Texture2D(m_cropWidth, m_cropHeight, TextureFormat.RGBA32, false)
				{
					name = m_source.name + "_crop"
				};
				cropped.SetPixels(pixels);
				cropped.Apply();
				string id = SkinSheetRegistry.StoreTexture(cropped);
				string message = "已裁剪 " + m_cropWidth + " × " + m_cropHeight + "，贴图 " + id.Substring(0, Mathf.Min(8, id.Length));
				Action<string> apply = m_apply;
				Close();
				if (apply != null) apply(id);
				if (SkinPanelHost.Instance.IsOpen) SkinPanelHost.Instance.SetStatus(message);
			}
			catch (Exception exception)
			{
				if (m_status != null) m_status.text = "裁剪失败：" + exception.Message;
			}
		}

		private static bool EnsureReadable(Texture2D texture)
		{
			try
			{
				texture.GetPixel(0, 0);
				return true;
			}
			catch (UnityException)
			{
				// Not readable: blit through a RenderTexture.
			}
			try
			{
				RenderTexture temporary = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
				Graphics.Blit(texture, temporary);
				RenderTexture previous = RenderTexture.active;
				RenderTexture.active = temporary;
				texture.ReadPixels(new Rect(0f, 0f, texture.width, texture.height), 0, 0);
				texture.Apply();
				RenderTexture.active = previous;
				RenderTexture.ReleaseTemporary(temporary);
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}

		// ── 控件与布局 ──────────────────────────────────────────────────────

		private InputField Field(Transform parent, float x, float top, float width, string hint)
		{
			GameObject go = new GameObject("Field_" + hint, typeof(RectTransform), typeof(Image), typeof(InputField));
			go.transform.SetParent(parent, false);
			RectTransform rect = go.GetComponent<RectTransform>();
			Fixed(rect, x, top, width, 34f);

			Image bg = go.GetComponent<Image>();
			bg.sprite = ModUiKit.Rounded(6);
			bg.type = Image.Type.Sliced;
			bg.color = FieldColor;

			InputField field = go.GetComponent<InputField>();
			field.targetGraphic = bg;
			field.contentType = InputField.ContentType.IntegerNumber;
			field.lineType = InputField.LineType.SingleLine;

			Text text = ModUiKit.Label(go.transform, string.Empty, 15, ModUiKit.TextMain);
			Stretch(text.rectTransform, 9f, 2f, 9f, 2f);
			field.textComponent = text;

			Text placeholder = ModUiKit.Label(go.transform, hint, 13, new Color(0.45f, 0.50f, 0.58f, 1f));
			Stretch(placeholder.rectTransform, 9f, 2f, 9f, 2f);
			field.placeholder = placeholder;

			field.onEndEdit.AddListener(_ => ApplyFields());
			return field;
		}

		private void Caption(Transform parent, string text, float x, float top, float width, float height)
		{
			Text label = ModUiKit.Label(parent, text, 12, ModUiKit.TextDim);
			Fixed(label.rectTransform, x, top, width, height);
		}

		private static void Fixed(RectTransform rect, float x, float top, float width, float height)
		{
			rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
			rect.pivot = new Vector2(0f, 1f);
			rect.sizeDelta = new Vector2(width, height);
			rect.anchoredPosition = new Vector2(x, -top);
		}

		private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
		{
			rect.anchorMin = anchorMin;
			rect.anchorMax = anchorMax;
			rect.offsetMin = offsetMin;
			rect.offsetMax = offsetMax;
		}

		private static void SetRight(RectTransform rect, float right, float width, float height)
		{
			rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
			rect.pivot = new Vector2(1f, 0.5f);
			rect.sizeDelta = new Vector2(width, height);
			rect.anchoredPosition = new Vector2(-right, 0f);
		}

		private static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
		{
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = new Vector2(left, bottom);
			rect.offsetMax = new Vector2(-right, -top);
		}

		private sealed class CropSurface : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
		{
			public SkinAtlasCropDialog Owner;

			public void OnPointerDown(PointerEventData eventData)
			{
				if (Owner != null) Owner.OnSurfaceDown(eventData);
			}

			public void OnDrag(PointerEventData eventData)
			{
				if (Owner != null) Owner.OnSurfaceDrag(eventData);
			}

			public void OnPointerUp(PointerEventData eventData)
			{
				if (Owner != null) Owner.OnSurfaceUp(eventData);
			}
		}
	}
}
