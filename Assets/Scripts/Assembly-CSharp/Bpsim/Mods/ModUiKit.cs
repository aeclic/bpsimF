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
		// 配色（对齐 BPSS 工坊的深色调）
		public static readonly Color PanelBg = new Color(0.086f, 0.098f, 0.125f, 0.985f);
		public static readonly Color CardBg = new Color(0.13f, 0.145f, 0.18f, 1f);
		public static readonly Color CardBgAlt = new Color(0.16f, 0.18f, 0.22f, 1f);
		public static readonly Color Primary = new Color(0.16f, 0.45f, 0.92f, 1f);
		public static readonly Color Success = new Color(0.20f, 0.62f, 0.36f, 1f);
		public static readonly Color Danger = new Color(0.82f, 0.27f, 0.28f, 1f);
		public static readonly Color Neutral = new Color(0.30f, 0.33f, 0.38f, 1f);
		public static readonly Color TextMain = new Color(0.93f, 0.94f, 0.96f, 1f);
		public static readonly Color TextDim = new Color(0.62f, 0.66f, 0.72f, 1f);

		private static Font s_font;
		private static readonly Dictionary<int, Sprite> s_roundCache = new Dictionary<int, Sprite>();

		private static GameObject NativeSettingsPanelTemplate
		{
			get
			{
				UserInterface ui = UserInterface.Instance;
				return ui != null && ui.ActionBar != null ? ui.ActionBar.SettingsPanelTemplate : null;
			}
		}

		private static Transform NativeTemplate(string path)
		{
			GameObject root = NativeSettingsPanelTemplate;
			return root == null ? null : root.transform.Find(path);
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

		/// <summary>用户设置列表行使用的无装饰容器，间距和高度由父级布局控制。</summary>
		public static RectTransform Row(Transform parent)
		{
			GameObject go = new GameObject("Row", typeof(RectTransform));
			RectTransform rt = go.GetComponent<RectTransform>();
			rt.SetParent(parent, false);
			return rt;
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
			if (label != null && fontSize > 0) label.fontSize = fontSize;
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
