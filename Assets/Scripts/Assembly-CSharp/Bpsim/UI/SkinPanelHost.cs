using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Bpsim.Mods;
using Bpsim.Parts.Gameplay;
using Bpsim.Parts.Skin;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.UI
{
	/// <summary>
	/// 原生 UGUI 蒙皮编辑器。内容布局参考 BPRC 的蒙皮部件，标题栏、底色、字体与按钮
	/// 复用用户设置面板的原生模板，保证与游戏内其它界面统一。
	/// </summary>
	public sealed class SkinPanelHost : MonoBehaviour
	{
		private const float PanelWidth = 760f;
		private const float PanelHeight = 960f;
		private const float PreviewTop = 48f;
		private const float PreviewWidth = 224f;
		private const float PreviewHeight = 128f;
		private const float PreviewRightX = 252f;
		private const float TextureRowHeight = 56f;
		private const float TextureRowStep = 62f;
		private const float MaxTextureRows = 40f;

		private static readonly Color PanelColor = new Color(0.15f, 0.15f, 0.15f, 0.99f);
		private static readonly Color HeaderColor = new Color(0f, 0.2f, 0.4f, 1f);
		private static readonly Color FieldColor = new Color(0.075f, 0.085f, 0.11f, 1f);
		private static readonly Color SwitchOff = new Color(0.17f, 0.19f, 0.23f, 1f);
		private static readonly Color SwitchOn = new Color(0.16f, 0.45f, 0.92f, 1f);
		private static readonly Color PreviewBack = new Color(0.05f, 0.06f, 0.08f, 1f);
		private static readonly Color SelectedRow = new Color(0.16f, 0.32f, 0.58f, 0.92f);
		private static readonly Color NormalRow = new Color(0.17f, 0.19f, 0.23f, 1f);

		private static SkinPanelHost s_instance;

		private readonly Dictionary<string, InputField> m_fields = new Dictionary<string, InputField>(StringComparer.Ordinal);
		private Entity m_part = Entity.Null;
		private GameObject m_root;
		private ScrollRect m_scroll;
		private RectTransform m_content;
		private Toggle m_tiling;
		private Image m_switchTrack;
		private RectTransform m_switchKnob;
		private Image m_swatch;
		private Text m_status;
		private Text m_textureName;
		private Text m_textureMeta;
		private RawImage m_texturePreview;
		private RectTransform m_previewHost;
		private Button m_cropButton;
		private bool m_built;
		private bool m_refreshing;
		private string m_lastMessage = "none";
		private string m_lastError = string.Empty;

		public static SkinPanelHost Instance
		{
			get
			{
				if (s_instance == null)
				{
					GameObject host = new GameObject("SkinPanelHost");
					DontDestroyOnLoad(host);
					s_instance = host.AddComponent<SkinPanelHost>();
				}
				return s_instance;
			}
		}

		public bool IsOpen => m_root != null && m_root.activeSelf;

		public string Describe()
		{
			string part = m_part == Entity.Null ? "none" : m_part.Index.ToString(CultureInfo.InvariantCulture);
			return "skinpanel open=" + IsOpen + " ugui=" + (m_root == null ? "null" : "ready") + " part=" + part + " textures=" + EnumerateTextureIds().Count + " last=" + m_lastMessage + (m_lastError.Length == 0 ? string.Empty : " err=" + m_lastError);
		}

		public void Open(Entity part)
		{
			if (part == Entity.Null) return;
			m_part = part;
			if (m_root == null) BuildUi();
			m_root.SetActive(true);
			RefreshUi();
		}

		public void Close()
		{
			m_part = Entity.Null;
			if (m_root != null) m_root.SetActive(false);
			if (SkinAtlasCropDialog.Instance.IsOpen) SkinAtlasCropDialog.Instance.Close();
		}

		/// <summary>QA 直接修改曲率时使用，与用户输入框走同一写回路径。</summary>
		public string ApplyCurveForQa(float curve)
		{
			if (!TryGetSheet(out EntityManager em, out SkinSheet sheet)) return "no part";
			sheet.Curve = new float4(curve, curve, curve, curve);
			sheet.Version++;
			em.SetComponentData(m_part, sheet);
			RefreshUi();
			m_lastMessage = "apply";
			return "curve=" + curve.ToString("0.###", CultureInfo.InvariantCulture) + " version=" + sheet.Version;
		}

		private void Awake()
		{
			if (s_instance != null && s_instance != this) { Destroy(gameObject); return; }
			s_instance = this;
		}

		private void Update()
		{
			if (!IsOpen || !UiKeys.EscapePressed) return;
			if (SkinAtlasCropDialog.Instance.IsOpen) return;
			Close();
		}

		private void OnDestroy()
		{
			if (s_instance == this) s_instance = null;
			if (m_root != null)
			{
				if (Application.isPlaying) Destroy(m_root);
				else DestroyImmediate(m_root);
			}
		}

		private void BuildUi()
		{
			Canvas canvas = ModUiKit.CreateCanvas("SkinPanelCanvas", 32000);
			m_root = canvas.gameObject;
			m_root.SetActive(false);

			RectTransform panel = ModUiKit.Panel(canvas.transform, PanelColor, 10);
			panel.anchorMin = panel.anchorMax = new Vector2(1f, 0f);
			panel.pivot = new Vector2(1f, 0f);
			panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);
			panel.anchoredPosition = new Vector2(-18f, 18f);

			// 标题栏：与用户设置面板同色（0, 0.2, 0.4）。
			RectTransform header = ModUiKit.Panel(panel, HeaderColor, 10);
			SetRect(header, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -60f), Vector2.zero);
			Text title = ModUiKit.Label(header, "蒙皮参数", 22, Color.white);
			title.fontStyle = FontStyle.Bold;
			SetRect(title.rectTransform, Vector2.zero, Vector2.one, new Vector2(22f, 0f), new Vector2(-72f, 0f));
			Button close = ModUiKit.Button(header, "×", new Color(0.22f, 0.25f, 0.30f, 1f), Close, 22);
			SetRight(close.GetComponent<RectTransform>(), 10f, 42f, 40f);

			m_scroll = ModUiKit.ScrollList(panel, out m_content);
			SetRect(m_scroll.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, new Vector2(14f, 68f), new Vector2(-26f, -72f));

			// ModUiKit.ScrollList 的 Content 留了 100px 的 sizeDelta.x，会把卡片撑出视口，
			// 这里改为横向跟随视口宽度（高度由 ContentSizeFitter 决定）。
			m_content.anchorMin = new Vector2(0f, 1f);
			m_content.anchorMax = new Vector2(1f, 1f);
			m_content.pivot = new Vector2(0.5f, 1f);
			m_content.sizeDelta = new Vector2(0f, m_content.sizeDelta.y);

			// 细滚动条：给可滚动内容一个可见的提示，用户设置面板同样有滚动条。
			RectTransform bar = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar)).GetComponent<RectTransform>();
			bar.SetParent(panel, false);
			SetRect(bar, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-13f, 68f), new Vector2(-7f, -72f));
			Image barImage = bar.GetComponent<Image>();
			barImage.sprite = ModUiKit.Rounded(4);
			barImage.type = Image.Type.Sliced;
			barImage.color = new Color(1f, 1f, 1f, 0.05f);
			RectTransform handle = new GameObject("Handle", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
			handle.SetParent(bar, false);
			// Handle 的位置完全由 Scrollbar 改写 anchors 得出，这里必须把新建
			// RectTransform 的默认 sizeDelta(100) 清掉，否则滑块会比轨道大两个量级。
			handle.anchorMin = Vector2.zero;
			handle.anchorMax = Vector2.one;
			handle.offsetMin = Vector2.zero;
			handle.offsetMax = Vector2.zero;
			Image handleImage = handle.GetComponent<Image>();
			handleImage.sprite = ModUiKit.Rounded(4);
			handleImage.type = Image.Type.Sliced;
			handleImage.color = new Color(1f, 1f, 1f, 0.22f);
			Scrollbar scrollbar = bar.GetComponent<Scrollbar>();
			scrollbar.handleRect = handle;
			scrollbar.targetGraphic = handleImage;
			scrollbar.direction = Scrollbar.Direction.BottomToTop;
			m_scroll.verticalScrollbar = scrollbar;
			m_scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

			Image footerLine = new GameObject("FooterLine", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
			footerLine.transform.SetParent(panel, false);
			footerLine.color = new Color(1f, 1f, 1f, 0.06f);
			footerLine.raycastTarget = false;
			SetRect(footerLine.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(14f, 60f), new Vector2(-14f, 61f));

			RectTransform footer = new GameObject("Footer", typeof(RectTransform)).GetComponent<RectTransform>();
			footer.SetParent(panel, false);
			SetRect(footer, Vector2.zero, new Vector2(1f, 0f), new Vector2(16f, 12f), new Vector2(-16f, 56f));
			m_status = ModUiKit.Label(footer, "就绪", 14, ModUiKit.TextDim);
			SetRect(m_status.rectTransform, Vector2.zero, Vector2.one, new Vector2(4f, 0f), new Vector2(-290f, 0f));
			Button cancel = ModUiKit.Button(footer, "关闭", new Color(0.28f, 0.31f, 0.36f, 1f), Close, 18);
			SetRight(cancel.GetComponent<RectTransform>(), 0f, 92f, 36f);
			Button apply = ModUiKit.Button(footer, "应用更改", ModUiKit.Primary, ApplyFromUi, 18);
			SetRight(apply.GetComponent<RectTransform>(), 102f, 130f, 36f);

			m_built = true;
		}

		/// <summary>按当前组件重建整张面板，滚动位置保持不变。</summary>
		private void RefreshUi()
		{
			if (!m_built || !TryGetSheet(out _, out SkinSheet sheet))
			{
				SetStatus("没有可编辑的蒙皮");
				return;
			}
			float keep = m_scroll != null ? m_scroll.verticalNormalizedPosition : 1f;
			bool hadContent = m_content != null && m_content.childCount > 0;
			m_refreshing = true;
			RebuildContent(sheet);
			WriteFields(sheet);
			if (m_tiling != null) { m_tiling.SetIsOnWithoutNotify(sheet.TextureMode == 1); StyleSwitch(); }
			RefreshPreview(sheet);
			m_refreshing = false;
			if (hadContent)
			{
				Canvas.ForceUpdateCanvases();
				if (m_scroll != null) m_scroll.verticalNormalizedPosition = keep;
			}
		}

		private void RebuildContent(SkinSheet sheet)
		{
			for (int i = m_content.childCount - 1; i >= 0; i--)
			{
				GameObject child = m_content.GetChild(i).gameObject;
				child.SetActive(false);
				Destroy(child);
			}
			m_fields.Clear();

			List<string> ids = EnumerateTextureIds();
			BuildTextureCard(sheet, ids);
			BuildCornerCard();
			BuildCurveCard();
			BuildModeCard(sheet);
			BuildUvCard();
		}

		// ── 卡片 ────────────────────────────────────────────────────────────

		private RectTransform Card(string title, float height)
		{
			RectTransform card = ModUiKit.Panel(m_content, ModUiKit.CardBg, 8);
			ModUiKit.Size(card.gameObject, height);
			Text label = ModUiKit.Label(card, title, 16, ModUiKit.TextMain);
			label.fontStyle = FontStyle.Bold;
			Row(label.rectTransform, 16f, 10f, 16f, 26f);
			return card;
		}

		private void BuildTextureCard(SkinSheet sheet, List<string> ids)
		{
			int rows = Mathf.Min(ids.Count, (int)MaxTextureRows);
			float listTop = PreviewTop + PreviewHeight + 16f;
			float rowsTop = listTop + 30f;
			float height = rowsTop + (rows == 0 ? 46f : rows * TextureRowStep) + 10f;
			RectTransform card = Card("贴图", height);

			RectTransform preview = ModUiKit.Panel(card, PreviewBack, 8);
			Fixed(preview, 16f, PreviewTop, PreviewWidth, PreviewHeight);
			m_previewHost = preview;
			m_texturePreview = new GameObject("Preview", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
			m_texturePreview.transform.SetParent(preview, false);
			m_texturePreview.raycastTarget = false;

			m_textureName = ModUiKit.Label(card, "无贴图（默认布面）", 17, ModUiKit.TextMain);
			m_textureName.fontStyle = FontStyle.Bold;
			Row(m_textureName.rectTransform, PreviewRightX, PreviewTop + 2f, 16f, 26f);
			m_textureMeta = ModUiKit.Label(card, "使用部件默认材质", 13, ModUiKit.TextDim);
			Row(m_textureMeta.rectTransform, PreviewRightX, PreviewTop + 30f, 16f, 22f);

			m_cropButton = ModUiKit.Button(card, "图集裁剪", new Color(0.12f, 0.52f, 0.74f, 1f), OpenCrop, 18);
			Fixed(m_cropButton.GetComponent<RectTransform>(), PreviewRightX, PreviewTop + 58f, 118f, 36f);
			Button clear = ModUiKit.Button(card, "清除贴图", new Color(0.24f, 0.27f, 0.31f, 1f), () => ApplyTextureId(string.Empty), 18);
			Fixed(clear.GetComponent<RectTransform>(), PreviewRightX + 126f, PreviewTop + 58f, 112f, 36f);

			Text listTitle = ModUiKit.Label(card, "可用贴图", 14, ModUiKit.TextDim);
			Row(listTitle.rectTransform, 16f, listTop, 16f, 26f);

			if (rows == 0)
			{
				Text empty = ModUiKit.Label(card, "还没有可用贴图。先在属性面板导入贴图，或用「图集裁剪」从现有图集里切一块。", 13, ModUiKit.TextDim);
				empty.horizontalOverflow = HorizontalWrapMode.Wrap;
				Row(empty.rectTransform, 16f, rowsTop, 16f, 40f);
				return;
			}

			string current = sheet.TextureHandle == SkinSheetRegistry.NoTexture ? string.Empty : SkinSheetRegistry.Instance.GetTextureId(sheet.TextureHandle);
			float y = rowsTop;
			for (int i = 0; i < rows; i++)
			{
				BuildTextureRow(card, y, ids[i], string.Equals(ids[i], current, StringComparison.Ordinal));
				y += TextureRowStep;
			}
		}

		private void BuildTextureRow(RectTransform card, float top, string id, bool selected)
		{
			GameObject go = new GameObject("Texture_" + id, typeof(RectTransform), typeof(Image), typeof(Button));
			go.transform.SetParent(card, false);
			RectTransform rect = go.GetComponent<RectTransform>();
			Row(rect, 16f, top, 16f, TextureRowHeight);

			Image bg = go.GetComponent<Image>();
			bg.sprite = ModUiKit.Rounded(6);
			bg.type = Image.Type.Sliced;
			bg.color = selected ? SelectedRow : NormalRow;
			Button button = go.GetComponent<Button>();
			button.targetGraphic = bg;
			ColorBlock colors = button.colors;
			colors.normalColor = Color.white;
			colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
			colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
			colors.fadeDuration = 0.06f;
			button.colors = colors;
			string captured = id;
			button.onClick.AddListener(() => ApplyTextureId(captured));

			RectTransform thumb = ModUiKit.Panel(go.transform, new Color(0.03f, 0.04f, 0.05f, 1f), 5);
			Fixed(thumb, 6f, 6f, 44f, 44f);
			RawImage raw = new GameObject("Thumb", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
			raw.transform.SetParent(thumb, false);
			raw.raycastTarget = false;
			Texture2D texture = LoadTexture(id);
			raw.texture = texture;
			FitInside(thumb, raw.rectTransform, texture, 2f);

			Text name = ModUiKit.Label(go.transform, "贴图 " + Shorten(id, 10), 15, ModUiKit.TextMain);
			Row(name.rectTransform, 60f, 6f, 40f, 22f);
			Text meta = ModUiKit.Label(go.transform, texture == null ? "无法加载" : texture.width + " × " + texture.height, 12, ModUiKit.TextDim);
			Row(meta.rectTransform, 60f, 30f, 40f, 18f);

			if (selected)
			{
				Text mark = ModUiKit.Label(go.transform, "✓", 20, ModUiKit.Primary, TextAnchor.MiddleCenter);
				SetRight(mark.rectTransform, 10f, 30f, 26f);
			}
		}

		private void BuildCornerCard()
		{
			RectTransform card = Card("四角坐标（部件本地）", 48f + 4f * 46f + 10f);
			string[] keys = { "bl", "br", "tr", "tl" };
			string[] names = { "左下", "右下", "右上", "左上" };
			for (int i = 0; i < 4; i++)
			{
				float top = 48f + i * 46f;
				Caption(card, names[i], 16f, top, 88f, 34f);
				Field(card, keys[i] + "x", 110f, top, 126f, "X");
				Field(card, keys[i] + "y", 252f, top, 126f, "Y");
			}
		}

		private void BuildCurveCard()
		{
			RectTransform card = Card("边缘弯曲与沿边", 48f + 2f * 46f + 10f);
			FourRow(card, 48f, "弯曲", "curve");
			FourRow(card, 94f, "沿边", "along");
		}

		private void FourRow(RectTransform card, float top, string caption, string key)
		{
			Caption(card, caption, 16f, top, 88f, 34f);
			string[] hints = { "下", "上", "右", "左" };
			for (int i = 0; i < 4; i++)
			{
				Field(card, key + i, 110f + i * 142f, top, 126f, hints[i]);
			}
		}

		private void BuildModeCard(SkinSheet sheet)
		{
			RectTransform card = Card("贴图模式与颜色", 96f + 34f + 10f);
			Caption(card, "平铺贴图", 16f, 48f, 88f, 34f);
			m_tiling = Switch(card, 106f, 53f);
			StyleSwitch();
			Caption(card, "X 平铺", 174f, 48f, 62f, 34f);
			Field(card, "tileX", 238f, 48f, 90f, "1");
			Caption(card, "Y 平铺", 338f, 48f, 62f, 34f);
			Field(card, "tileY", 402f, 48f, 90f, "1");

			Caption(card, "RGBA 颜色", 16f, 96f, 88f, 34f);
			string[] hints = { "R", "G", "B", "A" };
			for (int i = 0; i < 4; i++)
			{
				Field(card, "color" + i, 110f + i * 128f, 96f, 118f, hints[i]);
			}

			RectTransform swatch = ModUiKit.Panel(card, Color.white, 6);
			Fixed(swatch, 626f, 101f, 36f, 24f);
			m_swatch = swatch.GetComponent<Image>();
			m_swatch.color = new Color(sheet.Color.x, sheet.Color.y, sheet.Color.z, sheet.Color.w);
		}

		private void BuildUvCard()
		{
			RectTransform card = Card("图集 UV（左下原点）", 48f + 34f + 10f);
			string[] hints = { "X", "Y", "宽", "高" };
			for (int i = 0; i < 4; i++)
			{
				Field(card, "uv" + i, 16f + i * 142f, 48f, 126f, hints[i]);
			}
			Button reset = ModUiKit.Button(card, "重置", new Color(0.24f, 0.27f, 0.31f, 1f), ResetUv, 16);
			Fixed(reset.GetComponent<RectTransform>(), 580f, 48f, 86f, 34f);
		}

		// ── 控件 ────────────────────────────────────────────────────────────

		private void Caption(Transform parent, string text, float x, float top, float width, float height)
		{
			Text label = ModUiKit.Label(parent, text, 14, ModUiKit.TextDim);
			Fixed(label.rectTransform, x, top, width, height);
		}

		private InputField Field(Transform parent, string key, float x, float top, float width, string hint)
		{
			GameObject go = new GameObject("Field_" + key, typeof(RectTransform), typeof(Image), typeof(InputField));
			go.transform.SetParent(parent, false);
			RectTransform rect = go.GetComponent<RectTransform>();
			Fixed(rect, x, top, width, 34f);

			Image bg = go.GetComponent<Image>();
			bg.sprite = ModUiKit.Rounded(6);
			bg.type = Image.Type.Sliced;
			bg.color = FieldColor;

			InputField field = go.GetComponent<InputField>();
			field.targetGraphic = bg;
			field.contentType = InputField.ContentType.Standard;
			field.lineType = InputField.LineType.SingleLine;
			field.customCaretColor = false;

			Text text = ModUiKit.Label(go.transform, string.Empty, 16, ModUiKit.TextMain);
			Stretch(text.rectTransform, 9f, 2f, 9f, 2f);
			field.textComponent = text;

			Text placeholder = ModUiKit.Label(go.transform, hint, 14, new Color(0.45f, 0.50f, 0.58f, 1f));
			Stretch(placeholder.rectTransform, 9f, 2f, 9f, 2f);
			field.placeholder = placeholder;

			m_fields[key] = field;
			field.onEndEdit.AddListener(_ => ApplyFromUi());
			return field;
		}

		private Toggle Switch(RectTransform parent, float x, float top)
		{
			GameObject go = new GameObject("Switch", typeof(RectTransform), typeof(Image), typeof(Toggle));
			go.transform.SetParent(parent, false);
			Fixed(go.GetComponent<RectTransform>(), x, top, 52f, 26f);

			Image track = go.GetComponent<Image>();
			track.sprite = ModUiKit.Rounded(8);
			track.type = Image.Type.Sliced;
			track.color = SwitchOff;

			GameObject knobGo = new GameObject("Knob", typeof(RectTransform), typeof(Image));
			knobGo.transform.SetParent(go.transform, false);
			Image knobImage = knobGo.GetComponent<Image>();
			knobImage.sprite = ModUiKit.Rounded(11);
			knobImage.type = Image.Type.Simple;
			knobImage.color = new Color(0.92f, 0.93f, 0.95f, 1f);
			RectTransform knob = knobGo.GetComponent<RectTransform>();
			knob.anchorMin = knob.anchorMax = new Vector2(0f, 0.5f);
			knob.pivot = new Vector2(0f, 0.5f);
			knob.sizeDelta = new Vector2(20f, 20f);
			knob.anchoredPosition = new Vector2(3f, 0f);

			m_switchTrack = track;
			m_switchKnob = knob;

			Toggle toggle = go.GetComponent<Toggle>();
			toggle.targetGraphic = track;
			toggle.onValueChanged.AddListener(_ =>
			{
				StyleSwitch();
				if (!m_refreshing) ApplyFromUi();
			});
			return toggle;
		}

		private void StyleSwitch()
		{
			if (m_tiling == null || m_switchTrack == null || m_switchKnob == null) return;
			bool on = m_tiling.isOn;
			m_switchTrack.color = on ? SwitchOn : SwitchOff;
			m_switchKnob.anchoredPosition = new Vector2(on ? 29f : 3f, 0f);

			// 平铺倍率只在平铺模式下有意义。
			if (m_fields.TryGetValue("tileX", out InputField tileX)) tileX.interactable = on;
			if (m_fields.TryGetValue("tileY", out InputField tileY)) tileY.interactable = on;
		}

		private void ResetUv()
		{
			if (m_fields.TryGetValue("uv0", out InputField x)) x.SetTextWithoutNotify("0");
			if (m_fields.TryGetValue("uv1", out InputField y)) y.SetTextWithoutNotify("0");
			if (m_fields.TryGetValue("uv2", out InputField w)) w.SetTextWithoutNotify("1");
			if (m_fields.TryGetValue("uv3", out InputField h)) h.SetTextWithoutNotify("1");
			ApplyFromUi();
		}

		// ── 数据 ────────────────────────────────────────────────────────────

		private void OpenCrop()
		{
			if (!TryGetSheet(out _, out SkinSheet sheet)) return;
			Texture2D texture = SkinSheetRegistry.Instance.GetTexture(sheet.TextureHandle);
			if (texture == null) { SetStatus("当前没有可裁剪的贴图"); return; }
			SkinAtlasCropDialog.Instance.Open(texture, ApplyTextureId);
		}

		private void ApplyTextureId(string id)
		{
			if (!TryGetSheet(out EntityManager em, out SkinSheet sheet)) return;
			sheet.TextureHandle = string.IsNullOrEmpty(id) ? SkinSheetRegistry.NoTexture : SkinSheetRegistry.Instance.GetOrLoadTexture(id);
			sheet.UVRect = new float4(0f, 0f, 1f, 1f);
			sheet.Version++;
			em.SetComponentData(m_part, sheet);
			m_lastMessage = "texture";
			RefreshUi();
			SetStatus(string.IsNullOrEmpty(id) ? "已清除贴图" : "已切换贴图 " + Shorten(id, 12));
		}

		private void ApplyFromUi()
		{
			if (m_refreshing || !TryGetSheet(out EntityManager em, out SkinSheet sheet)) return;
			sheet.BottomLeft = ReadVector("bl", sheet.BottomLeft);
			sheet.BottomRight = ReadVector("br", sheet.BottomRight);
			sheet.TopRight = ReadVector("tr", sheet.TopRight);
			sheet.TopLeft = ReadVector("tl", sheet.TopLeft);
			sheet.Curve = ReadFour("curve", sheet.Curve);
			sheet.Along = ReadFour("along", sheet.Along);
			sheet.TileX = Mathf.Max(.01f, Read("tileX", sheet.TileX));
			sheet.TileY = Mathf.Max(.01f, Read("tileY", sheet.TileY));
			sheet.Color = ReadFour("color", sheet.Color);
			sheet.UVRect = ClampUv(ReadFour("uv", sheet.UVRect));
			sheet.TextureMode = m_tiling != null && m_tiling.isOn ? 1 : 0;
			sheet.Version++;
			em.SetComponentData(m_part, sheet);
			m_lastMessage = "apply";
			if (m_swatch != null) m_swatch.color = new Color(sheet.Color.x, sheet.Color.y, sheet.Color.z, sheet.Color.w);
			SetStatus("已应用，版本 " + sheet.Version);
		}

		private void WriteFields(SkinSheet sheet)
		{
			WriteVector("bl", sheet.BottomLeft); WriteVector("br", sheet.BottomRight); WriteVector("tr", sheet.TopRight); WriteVector("tl", sheet.TopLeft);
			WriteFour("curve", sheet.Curve); WriteFour("along", sheet.Along);
			Write("tileX", sheet.TileX); Write("tileY", sheet.TileY);
			WriteFour("color", sheet.Color); WriteFour("uv", sheet.UVRect);
		}

		private void RefreshPreview(SkinSheet sheet)
		{
			Texture2D texture = SkinSheetRegistry.Instance.GetTexture(sheet.TextureHandle);
			if (m_texturePreview != null)
			{
				m_texturePreview.texture = texture;
				FitInside(m_previewHost, m_texturePreview.rectTransform, texture, 8f);
			}
			if (m_textureName != null)
			{
				string id = texture == null ? null : SkinSheetRegistry.Instance.GetTextureId(sheet.TextureHandle);
				m_textureName.text = texture == null ? "无贴图（默认布面）" : (id != null ? "贴图 " + Shorten(id, 12) : texture.name);
			}
			if (m_textureMeta != null)
			{
				m_textureMeta.text = texture == null
					? "使用部件默认材质"
					: texture.width + " × " + texture.height + "  ·  UV " + sheet.UVRect.x.ToString("0.##", CultureInfo.InvariantCulture) + ", " + sheet.UVRect.y.ToString("0.##", CultureInfo.InvariantCulture) + ", " + sheet.UVRect.z.ToString("0.##", CultureInfo.InvariantCulture) + ", " + sheet.UVRect.w.ToString("0.##", CultureInfo.InvariantCulture);
			}
			if (m_cropButton != null) m_cropButton.interactable = texture != null;
		}

		private bool TryGetSheet(out EntityManager em, out SkinSheet sheet)
		{
			em = PartGameplayUtil.Em; sheet = default;
			if (m_part == Entity.Null || em.World == null || !em.World.IsCreated || !em.Exists(m_part) || !em.HasComponent<SkinSheet>(m_part)) return false;
			sheet = em.GetComponentData<SkinSheet>(m_part);
			return true;
		}

		private float Read(string key, float fallback) { return m_fields.TryGetValue(key, out InputField field) && float.TryParse(field.text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback; }
		private float2 ReadVector(string key, float2 fallback) { return new float2(Read(key + "x", fallback.x), Read(key + "y", fallback.y)); }
		private float4 ReadFour(string key, float4 fallback) { return new float4(Read(key + "0", fallback.x), Read(key + "1", fallback.y), Read(key + "2", fallback.z), Read(key + "3", fallback.w)); }
		private void Write(string key, float value) { if (m_fields.TryGetValue(key, out InputField field)) field.SetTextWithoutNotify(value.ToString("0.###", CultureInfo.InvariantCulture)); }
		private void WriteVector(string key, float2 value) { Write(key + "x", value.x); Write(key + "y", value.y); }
		private void WriteFour(string key, float4 value) { Write(key + "0", value.x); Write(key + "1", value.y); Write(key + "2", value.z); Write(key + "3", value.w); }
		private static float4 ClampUv(float4 value) { float x = Mathf.Clamp01(value.x); float y = Mathf.Clamp01(value.y); return new float4(x, y, Mathf.Clamp(value.z, .001f, 1f - x), Mathf.Clamp(value.w, .001f, 1f - y)); }
		private static string Shorten(string text, int length) { if (string.IsNullOrEmpty(text) || text.Length <= length) return text; return text.Substring(0, length) + "…"; }

		private static Texture2D LoadTexture(string id)
		{
			try
			{
				int handle = SkinSheetRegistry.Instance.GetOrLoadTexture(id);
				return SkinSheetRegistry.Instance.GetTexture(handle);
			}
			catch (Exception)
			{
				return null;
			}
		}

		/// <summary>状态栏提示（裁剪窗口等外部流程也会调用）。</summary>
		public void SetStatus(string text) { if (m_status != null) m_status.text = text; }

		private static List<string> EnumerateTextureIds()
		{
			List<string> result = new List<string>();
			try
			{
				if (!Directory.Exists(SkinSheetRegistry.TextureDirectory)) return result;
				foreach (string path in Directory.GetFiles(SkinSheetRegistry.TextureDirectory, "*.png"))
				{
					string id = Path.GetFileNameWithoutExtension(path);
					if (SkinSheetRegistry.IsValidTextureId(id)) result.Add(id);
				}
				result.Sort(StringComparer.Ordinal);
			}
			catch (Exception)
			{
				// A locked directory must not break the panel.
			}
			return result;
		}

		// ── 布局工具（以父级左上角为原点、y 向下为正） ──────────────────────

		private static void Row(RectTransform rect, float left, float top, float right, float height)
		{
			rect.anchorMin = new Vector2(0f, 1f);
			rect.anchorMax = new Vector2(1f, 1f);
			rect.pivot = new Vector2(0f, 1f);
			rect.offsetMin = new Vector2(left, -top - height);
			rect.offsetMax = new Vector2(-right, -top);
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

		/// <summary>把目标 Rect 按贴图宽高比缩放后居中放进 host（letterbox）。</summary>
		private static void FitInside(RectTransform host, RectTransform target, Texture2D texture, float padding)
		{
			if (host == null || target == null) return;
			if (texture == null)
			{
				target.gameObject.SetActive(false);
				return;
			}
			target.gameObject.SetActive(true);
			float availableWidth = Mathf.Max(1f, host.rect.width - padding * 2f);
			float availableHeight = Mathf.Max(1f, host.rect.height - padding * 2f);
			float scale = Mathf.Min(availableWidth / texture.width, availableHeight / texture.height);
			target.anchorMin = target.anchorMax = new Vector2(0.5f, 0.5f);
			target.pivot = new Vector2(0.5f, 0.5f);
			target.anchoredPosition = Vector2.zero;
			target.sizeDelta = new Vector2(Mathf.Max(1f, texture.width * scale), Mathf.Max(1f, texture.height * scale));
		}
	}

	/// <summary>
	/// 键盘查询。工程只启用新 Input System（ENABLE_INPUT_SYSTEM），直接读
	/// UnityEngine.Input 每次都会抛 InvalidOperationException，因此这里按宏分流。
	/// </summary>
	internal static class UiKeys
	{
		public static bool EscapePressed
		{
			get
			{
#if ENABLE_INPUT_SYSTEM
				return UnityEngine.InputSystem.Keyboard.current != null
					&& UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
				return Input.GetKeyDown(KeyCode.Escape);
#else
				return false;
#endif
			}
		}

		public static bool EnterPressed
		{
			get
			{
#if ENABLE_INPUT_SYSTEM
				UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
				return keyboard != null
					&& (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame);
#elif ENABLE_LEGACY_INPUT_MANAGER
				return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
#else
				return false;
#endif
			}
		}
	}
}
