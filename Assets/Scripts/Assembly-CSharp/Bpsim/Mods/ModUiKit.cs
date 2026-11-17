using System;
using System.Collections.Generic;
using Bpsim.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.Mods
{
	/// <summary>
	/// 程序化 uGUI 工具箱：Mod 管理器界面全部用代码搭（不依赖预制体），
	/// 与 <c>SkinPanelHost</c> 的做法一致。提供一个按圆角半径缓存的九宫格 Sprite。
	/// </summary>
	public static class ModUiKit
	{
		// 配色：转发全局界面配色（Bpsim.UI.UiTheme），设置里切换配色后新建的 Mod UI 自动跟随。
		public static Color PanelBg => WithAlpha(UiTheme.Current.Panel, 0.985f);
		public static Color CardBg => UiTheme.Current.Entry;
		public static Color CardBgAlt => UiTheme.Current.Shade(1.45f);
		public static Color Primary => UiTheme.Current.Accent;
		public static Color Success => UiTheme.Current.Mix(UiTheme.Current.Second, 0.55f);
		public static Color Danger => UiTheme.Current.DangerStrong;
		public static Color Neutral => UiTheme.Current.BarButton;
		public static Color TextMain => UiTheme.Current.Text;
		public static Color TextDim => UiTheme.Current.TextMuted;

		private static Color WithAlpha(Color color, float alpha)
		{
			color.a = alpha;
			return color;
		}

		private static Font s_font;
		private static Font s_cjkFont;
		private static readonly Dictionary<int, Sprite> s_roundCache = new Dictionary<int, Sprite>();

		/// <summary>中文标题字体：原生模板字体可能不含 CJK 字形，这里强制用系统中文字体。</summary>
		public static Font CjkFont
		{
			get
			{
				if (s_cjkFont == null)
				{
					try
					{
						s_cjkFont = Font.CreateDynamicFontFromOSFont(new[]
						{
							"Microsoft YaHei", "微软雅黑", "PingFang SC", "Noto Sans CJK SC",
							"Source Han Sans SC", "Arial Unicode MS", "Arial"
						}, 24);
					}
					catch (Exception)
					{
						s_cjkFont = null;
					}
					if (s_cjkFont == null) s_cjkFont = UiFont;
				}
				return s_cjkFont;
			}
		}

		private static GameObject NativeSettingsPanelTemplate
		{
			get
			{
				UserInterface ui = UserInterface.Instance;
				return ui != null && ui.ActionBar != null ? ui.ActionBar.SettingsPanelTemplate : null;
			}
		}

		private static Transform s_templateScanRoot;
		private static Dictionary<string, Transform> s_templateIndex;

		/// <summary>按路径或按节点名取设置面板预制体里的模板节点（供外部面板复用）。</summary>
		public static Transform Template(string path)
		{
			return NativeTemplate(path);
		}

		/// <summary>
		/// 在设置面板预制体里按路径或按节点名找模板。
		/// 预制体把 <c>ToggleTemplate</c> / <c>InputFieldTemplate</c> / <c>DropdownTemplate</c>
		/// 藏在 <c>Panel/Scroll View/Viewport/Content</c> 下（且默认 inactive），
		/// 所以路径查找失败时要退回整棵子树的按名索引，否则行会退化成零尺寸。
		/// </summary>
		private static Transform NativeTemplate(string path)
		{
			GameObject root = NativeSettingsPanelTemplate;
			if (root == null)
			{
				s_templateScanRoot = null;
				s_templateIndex = null;
				return null;
			}

			Transform direct = root.transform.Find(path);
			if (direct != null)
			{
				return direct;
			}

			if (s_templateIndex == null || s_templateScanRoot != root.transform)
			{
				s_templateScanRoot = root.transform;
				s_templateIndex = new Dictionary<string, Transform>(StringComparer.Ordinal);
				foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
				{
					if (child == null || child == root.transform)
					{
						continue;
					}
					if (!s_templateIndex.ContainsKey(child.name))
					{
						s_templateIndex[child.name] = child;
					}
				}
			}

			string key = path;
			int slash = key.LastIndexOf('/');
			if (slash >= 0 && slash + 1 < key.Length)
			{
				key = key.Substring(slash + 1);
			}
			return s_templateIndex.TryGetValue(key, out Transform found) ? found : null;
		}

		/// <summary>UI 字体：优先系统中文字体（保证中文名可显示），否则退回内置字体。</summary>
		public static Font UiFont
		{
			get
			{
				if (s_font == null)
				{
					try
					{
						UserInterface ui = UserInterface.Instance;
						Text native = ui != null && ui.ActionBar != null && ui.ActionBar.TemplateButton != null
							? ui.ActionBar.TemplateButton.GetComponentInChildren<Text>(true) : null;
						if (native != null && native.font != null)
						{
							s_font = native.font;
						}
					}
					catch (Exception)
					{
						// The manager can be opened before the main UI has finished binding.
					}
					if (s_font != null) return s_font;
					string[] names =
					{
						"Microsoft YaHei", "微软雅黑", "PingFang SC", "Noto Sans CJK SC",
						"Source Han Sans SC", "Arial Unicode MS", "Arial"
					};
					try
					{
						s_font = Font.CreateDynamicFontFromOSFont(names, 24);
					}
					catch (Exception)
					{
						s_font = null;
					}
					if (s_font == null)
					{
						s_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
					}
				}
				return s_font;
			}
		}

		/// <summary>白色圆角九宫格（用 Image.color 上色）。radius 像素。</summary>
		public static Sprite Rounded(int radius)
		{
			radius = Mathf.Clamp(radius, 2, 64);
			if (s_roundCache.TryGetValue(radius, out Sprite cached) && cached != null)
			{
				return cached;
			}
			int size = radius * 2 + 4;
			Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
			{
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp,
				name = "ModRound" + radius
			};
			Color32[] px = new Color32[size * size];
			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					float cx = 0f;
					float cy = 0f;
					if (x < radius) cx = radius - x - 0.5f;
					else if (x > size - radius) cx = x - (size - radius) + 0.5f;
					if (y < radius) cy = radius - y - 0.5f;
					else if (y > size - radius) cy = y - (size - radius) + 0.5f;
					float a = 1f;
					if (cx > 0f && cy > 0f)
					{
						float d = Mathf.Sqrt(cx * cx + cy * cy);
						a = Mathf.Clamp01(radius - d + 0.5f);
					}
					px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
				}
			}
			tex.SetPixels32(px);
			tex.Apply();
			Sprite sp = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
				SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
			sp.name = "ModRound" + radius;
			s_roundCache[radius] = sp;
			return sp;
		}

		public static Canvas CreateCanvas(string name, int order)
		{
			GameObject go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
			Canvas canvas = go.GetComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = order;
			CanvasScaler scaler = go.GetComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1920f, 1080f);
			// UserSettingsPanel is authored at 1600x900 and the game's SubCanvas
			// matches the height. Use the same scale so text has the same physical size.
			scaler.matchWidthOrHeight = 1f;
			return canvas;
		}

		/// <summary>克隆用户设置面板的主体，复用其背景、标题栏和滚动条资源。</summary>
		public static RectTransform NativePanel(Transform parent)
		{
			Transform source = NativeTemplate("Panel");
			if (source == null) return null;
			GameObject clone = UnityEngine.Object.Instantiate(source.gameObject, parent, false);
			clone.name = "Panel";
			UiThemeApplicator.ApplyTo(clone);
			return clone.GetComponent<RectTransform>();
		}

		/// <summary>克隆用户设置面板的原生按钮，保留其字体、过渡和颜色风格。</summary>
		public static Button NativeButton(Transform parent, string text, Action onClick)
		{
			Transform source = NativeTemplate("Panel/ButtonGroup/CloseButton");
			if (source == null)
			{
				return ProceduralButton(parent, text, Primary, onClick, 24);
			}
			Button button = UnityEngine.Object.Instantiate(source.gameObject, parent, false).GetComponent<Button>();
			button.name = "Button_" + text;
			button.onClick.RemoveAllListeners();
			if (onClick != null) button.onClick.AddListener(() => onClick());
			Text label = button.GetComponentInChildren<Text>(true);
			if (label != null)
			{
				label.text = text;
				label.font = UiFont;
				label.fontSize = 24;
				label.resizeTextForBestFit = true;
				label.resizeTextMinSize = 12;
				label.resizeTextMaxSize = 24;
				label.horizontalOverflow = HorizontalWrapMode.Wrap;
				label.verticalOverflow = VerticalWrapMode.Truncate;
			}
			return button;
		}

		public static Font NativeFont => UiFont;

		/// <summary>一个带圆角底色的容器（返回其 RectTransform，已按 stretch 填满父级；可再改）。</summary>
		public static RectTransform Panel(Transform parent, Color color, int radius)
		{
			GameObject go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
			RectTransform rt = go.GetComponent<RectTransform>();
			rt.SetParent(parent, false);
			Image img = go.GetComponent<Image>();
			img.sprite = Rounded(radius);
			img.type = Image.Type.Sliced;
			img.color = color;
			return rt;
		}

		/// <summary>克隆用户设置面板的 Toggle 行，复用灰色行底与原生文字样式。</summary>
		public static RectTransform Row(Transform parent)
		{
			return Row(parent, "ToggleTemplate", false, 90f);
		}

		/// <summary>
		/// 克隆设置面板里的某一行模板（<c>ToggleTemplate</c> / <c>InputFieldTemplate</c> /
		/// <c>DropdownTemplate</c>），返回已按列表布局摆好的行。
		/// </summary>
		/// <param name="templateName">面板 <c>Content</c> 下的模板节点名。</param>
		/// <param name="showValue">是否保留右侧的 <c>Value</c>（开关/输入框/下拉）。</param>
		/// <param name="height">行高；&lt;= 0 时保持模板原高。</param>
		public static RectTransform Row(Transform parent, string templateName, bool showValue, float height)
		{
			Transform source = NativeTemplate(templateName);
			if (source == null)
			{
				GameObject fallback = new GameObject("Row", typeof(RectTransform));
				fallback.transform.SetParent(parent, false);
				RectTransform fallbackRect = fallback.GetComponent<RectTransform>();
				fallbackRect.anchorMin = new Vector2(0f, 1f);
				fallbackRect.anchorMax = new Vector2(1f, 1f);
				fallbackRect.pivot = new Vector2(0.5f, 1f);
				fallbackRect.sizeDelta = new Vector2(0f, height > 0f ? height : 90f);
				return fallbackRect;
			}

			GameObject go = UnityEngine.Object.Instantiate(source.gameObject, parent, false);
			go.name = templateName.Replace("Template", string.Empty);
			Image rowImage = go.GetComponent<Image>();
			if (rowImage != null) rowImage.color = UiTheme.Current.Entry;
			RectTransform rt = go.GetComponent<RectTransform>();
			rt.anchorMin = new Vector2(0f, 1f);
			rt.anchorMax = new Vector2(1f, 1f);
			rt.pivot = new Vector2(0.5f, 1f);
			rt.anchoredPosition = Vector2.zero;
			rt.sizeDelta = new Vector2(0f, height > 0f ? height : 90f);
			rt.localScale = Vector3.one;
			// 模板根节点是 inactive 的，它的子节点也可能各自带着 inactive 标记
			// （Name 就是其一）。整棵子树先无条件激活，再按需藏掉 Value。
			foreach (Transform child in go.GetComponentsInChildren<Transform>(true))
			{
				if (child != null && !child.gameObject.activeSelf)
				{
					child.gameObject.SetActive(true);
				}
			}
			Transform value = go.transform.Find("Value");
			if (value != null && !showValue)
			{
				value.gameObject.SetActive(false);
			}
			go.SetActive(true);
			return rt;
		}

		/// <summary>行标题（模板自带的 <c>Name</c> 文本）。</summary>
		public static Text RowTitle(RectTransform row, string text = null)
		{
			Text label = row != null ? row.Find("Name")?.GetComponent<Text>() : null;
			if (label == null)
			{
				return null;
			}
			if (text != null)
			{
				label.text = text;
			}
			return label;
		}

		// ── 图标 ──────────────────────────────────────────────────────────
		// 建造工具栏的图标资源（Resources/BuildToolbar/Icon_*.png，都是白色剪影）。
		// 这里直接按名字取，缺失只警告一次，不让面板构建失败。

		private static readonly Dictionary<string, Sprite> s_iconCache = new Dictionary<string, Sprite>();
		private static readonly HashSet<string> s_iconWarned = new HashSet<string>();

		public const string IconSettings = "Icon_Settings";
		public const string IconAdjust = "Icon_Adjust";
		public const string IconScreenshot = "Icon_Screenshot";
		public const string IconLoad = "Icon_Load";
		public const string IconSave = "Icon_Save";
		public const string IconGrid = "Icon_Grid";

		/// <summary>取建造工具栏的图标 Sprite；没有该资源时返回 null。</summary>
		public static Sprite Icon(string iconName)
		{
			if (string.IsNullOrEmpty(iconName))
			{
				return null;
			}
			if (s_iconCache.TryGetValue(iconName, out Sprite cached))
			{
				return cached;
			}
			Sprite sprite = null;
			try
			{
				sprite = Resources.Load<Sprite>("BuildToolbar/" + iconName);
			}
			catch (Exception ex)
			{
				Debug.LogWarning("[ModUiKit] 加载图标 " + iconName + " 失败：" + ex.Message);
			}
			if (sprite == null && s_iconWarned.Add(iconName))
			{
				Debug.LogWarning("[ModUiKit] 缺少图标资源 Resources/BuildToolbar/" + iconName);
			}
			s_iconCache[iconName] = sprite;
			return sprite;
		}

		/// <summary>
		/// 方形图标按钮：与建造工具栏同款的圆角底板 + 居中图标（不带文字）。
		/// 底板颜色仍可被 <see cref="UiThemeApplicator"/> 按配色刷新。
		/// </summary>
		public static Button IconButton(Transform parent, string iconName, Color bg, Action onClick,
			float size = 44f, float iconSize = 26f)
		{
			GameObject go = new GameObject("Button_" + iconName, typeof(RectTransform), typeof(Image), typeof(Button));
			RectTransform rt = go.GetComponent<RectTransform>();
			rt.SetParent(parent, false);
			Image plate = go.GetComponent<Image>();
			plate.sprite = Rounded(8);
			plate.type = Image.Type.Sliced;
			plate.color = bg;

			Button button = go.GetComponent<Button>();
			button.targetGraphic = plate;
			ColorBlock colors = button.colors;
			colors.normalColor = Color.white;
			colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
			colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
			colors.selectedColor = Color.white;
			colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.6f);
			colors.fadeDuration = 0.08f;
			button.colors = colors;
			if (onClick != null)
			{
				button.onClick.AddListener(() => onClick());
			}

			GameObject iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
			RectTransform iconRect = iconGo.GetComponent<RectTransform>();
			iconRect.SetParent(rt, false);
			iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.5f);
			iconRect.pivot = new Vector2(0.5f, 0.5f);
			iconRect.anchoredPosition = Vector2.zero;
			iconRect.sizeDelta = new Vector2(iconSize, iconSize);
			Image icon = iconGo.GetComponent<Image>();
			icon.raycastTarget = false;
			icon.preserveAspect = true;
			icon.color = TextMain;
			Sprite sprite = Icon(iconName);
			icon.sprite = sprite;
			icon.enabled = sprite != null;
			if (sprite == null)
			{
				// 资源缺失时退化成文字标记，至少还认得出这是「设置」。
				Text fallback = Label(rt, "⚙", Mathf.RoundToInt(iconSize), TextMain, TextAnchor.MiddleCenter);
				SetRect(fallback.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
			}
			rt.sizeDelta = new Vector2(size, size);
			return button;
		}

		public static Text Label(Transform parent, string text, int size, Color color, TextAnchor anchor = TextAnchor.MiddleLeft)
		{
			GameObject go = new GameObject("Label", typeof(RectTransform), typeof(Text));
			RectTransform rt = go.GetComponent<RectTransform>();
			rt.SetParent(parent, false);
			Text t = go.GetComponent<Text>();
			t.font = UiFont;
			t.text = text;
			t.fontSize = size;
			t.color = color;
			t.alignment = anchor;
			t.horizontalOverflow = HorizontalWrapMode.Overflow;
			t.verticalOverflow = VerticalWrapMode.Overflow;
			t.raycastTarget = false;
			return t;
		}

		public static Button Button(Transform parent, string text, Color bg, Action onClick, int fontSize = 20)
		{
			Button button = NativeButton(parent, text, onClick);
			Image image = button.GetComponent<Image>();
			if (image != null) image.color = bg;
			Text label = button.GetComponentInChildren<Text>(true);
			if (label != null)
			{
				if (fontSize > 0) label.fontSize = fontSize;
				// 原生 CloseButton 的标题色是按浅色底板配的，套到深色底板上会变成黑字。
				label.color = TextMain;
			}
			return button;
		}

		private static Button ProceduralButton(Transform parent, string text, Color bg, Action onClick, int fontSize = 20)
		{
			GameObject go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
			RectTransform rt = go.GetComponent<RectTransform>();
			rt.SetParent(parent, false);
			Image img = go.GetComponent<Image>();
			img.sprite = Rounded(10);
			img.type = Image.Type.Sliced;
			img.color = bg;

			Button btn = go.GetComponent<Button>();
			ColorBlock cb = btn.colors;
			cb.normalColor = Color.white;
			cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
			cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
			cb.fadeDuration = 0.06f;
			btn.colors = cb;
			if (onClick != null)
			{
				btn.onClick.AddListener(() => onClick());
			}

			Text label = Label(rt, text, fontSize, TextMain, TextAnchor.MiddleCenter);
			RectTransform lrt = label.rectTransform;
			lrt.anchorMin = Vector2.zero;
			lrt.anchorMax = Vector2.one;
			lrt.offsetMin = new Vector2(8f, 2f);
			lrt.offsetMax = new Vector2(-8f, -2f);
			label.horizontalOverflow = HorizontalWrapMode.Wrap;
			label.resizeTextForBestFit = true;
			label.resizeTextMinSize = 10;
			return btn;
		}

		/// <summary>竖向滚动列表，返回 ScrollRect；content 是里面的内容容器（VerticalLayoutGroup）。</summary>
		public static ScrollRect ScrollList(Transform parent, out RectTransform content)
		{
			GameObject viewportGo = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
			RectTransform viewRt = viewportGo.GetComponent<RectTransform>();
			viewRt.SetParent(parent, false);

			Image bg = viewportGo.GetComponent<Image>();
			bg.color = new Color(0f, 0f, 0f, 0.12f);
			bg.raycastTarget = true;
			// ScrollRect does not clip its children by itself. Without a mask, a
			// long action row can paint over the frame on both sides of the list.
			viewportGo.AddComponent<RectMask2D>();

			GameObject contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup),
				typeof(ContentSizeFitter));
			content = contentGo.GetComponent<RectTransform>();
			content.SetParent(viewRt, false);
			content.anchorMin = new Vector2(0f, 1f);
			content.anchorMax = new Vector2(1f, 1f);
			content.pivot = new Vector2(0.5f, 1f);
			content.anchoredPosition = Vector2.zero;
			VerticalLayoutGroup vlg = contentGo.GetComponent<VerticalLayoutGroup>();
			vlg.childForceExpandHeight = false;
			vlg.childForceExpandWidth = true;
			vlg.childControlHeight = true;
			vlg.childControlWidth = true;
			vlg.spacing = 8f;
			vlg.padding = new RectOffset(8, 8, 8, 8);
			ContentSizeFitter fitter = contentGo.GetComponent<ContentSizeFitter>();
			fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

			ScrollRect sr = viewportGo.GetComponent<ScrollRect>();
			sr.content = content;
			sr.viewport = viewRt;
			sr.horizontal = false;
			sr.vertical = true;
			sr.movementType = ScrollRect.MovementType.Clamped;
			sr.scrollSensitivity = 30f;
			return sr;
		}

		public static void SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
		{
			rt.anchorMin = anchorMin;
			rt.anchorMax = anchorMax;
			rt.offsetMin = offsetMin;
			rt.offsetMax = offsetMax;
		}

		public static LayoutElement Size(GameObject go, float prefHeight, float prefWidth = -1f)
		{
			LayoutElement le = go.GetComponent<LayoutElement>();
			if (le == null)
			{
				le = go.AddComponent<LayoutElement>();
			}
			le.preferredHeight = prefHeight;
			le.minHeight = prefHeight;
			if (prefWidth > 0f)
			{
				le.preferredWidth = prefWidth;
			}
			return le;
		}
	}
}
