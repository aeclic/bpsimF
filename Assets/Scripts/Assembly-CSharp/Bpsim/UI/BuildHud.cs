using System.Collections.Generic;
using Bpsim.Parts;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Bpsim.UI
{
	/// <summary>
	/// 建造界面的常驻抬头（HUD），布局参照 Besiege 的建造界面：
	/// 左上角一个大号播放键（模拟中原地变成红色停止键），它右边紧挨着一条横向滑条
	/// （建造时是镜头缩放、模拟时是时间倍速，见 <see cref="T:Bpsim.UI.HudSlider" />）。
	///
	/// 它自带一个 Canvas（<see cref="HudSortingOrder" /> = 5），所以
	/// <see cref="T:Bpsim.UI.UserInterface" /> 在模拟中关掉主画布之后这两个控件仍然在屏幕上 ——
	/// 这就是「运行时也能显示在左上角」的落点。画布顺序：
	/// 主画布 0 &lt; 模拟界面 2 &lt; HUD 5 &lt; SubCanvas 10（模态面板，必须压住 HUD）。
	///
	/// 播放键不是新建的：它就是把 ActionBar 上原来的「模拟」按钮搬到 HUD 画布上，
	/// 换成绿色底板 + 播放图标（模拟中换成红色底板 + 停止图标）。
	///
	/// 提示条也归 HUD 自己管（<see cref="UpdateHint" />）：工具栏的
	/// <see cref="T:Bpsim.UI.BuildToolbar" /> 只在 ActionBar 的子树里找悬停目标，
	/// 播放键搬走之后就落在它的视野之外了。两边的提示条样式一致，只是各管各的控件。
	/// </summary>
	internal class BuildHud : MonoBehaviour
	{
		/// <summary>HUD 画布的排序层。高于模拟界面（2）、低于 SubCanvas（10）。</summary>
		internal const int HudSortingOrder = 5;

		/// <summary>播放键的边长。Besiege 的播放键明显比工具栏按钮大。</summary>
		private const float PlaySize = 76f;

		/// <summary>
		/// 播放键中心相对屏幕左上角。建造与运行共用同一个位置 —— Besiege 的播放键
		/// 就在抬头左上角，模拟开始后原地变成红色的停止键，不会跳到别处。
		/// </summary>
		private const float PlayX = 56f;

		private const float PlayY = -45f;

		private const float PlayIconSize = 40f;

		/// <summary>
		/// 整条滑条的指针热区尺寸（细滑槽只有 6 高，热区得留够才拖得动）。
		/// 横排：[模式图标][百分比][滑槽][缩小图标]，见 <see cref="CreateSlider" />。
		/// </summary>
		private const float SliderWidth = 282f;

		private const float SliderHeight = 44f;

		/// <summary>
		/// 滑槽本体：Besiege 的时间滑条就是一条细横线（1080p 下约 5-6px 粗、166px 长），
		/// 没有填充也没有底板。
		/// </summary>
		private const float SliderTrackLength = 160f;

		private const float SliderTrackThickness = 6f;

		/// <summary>滑槽左端距滑条左边缘多远（让出左边的模式图标与百分比文字）。</summary>
		private const float SliderTrackOffsetX = 88f;

		/// <summary>旋钮直径（Besiege 的旋钮在 1080p 下大约 15px）。</summary>
		private const float SliderKnobSize = 16f;

		/// <summary>
		/// 滑条位置：播放键右侧紧邻，同一条水平中线上（播放键右缘 94 + 10 空隙）。
		/// 和 Besiege 一样 —— 它的 <c>TimeScale</c> 就挂在 <c>TopBar</c> 里播放键右边
		/// （`HUD &gt; TopBar &gt; Mover &gt; Align (Top Left) &gt; PlayButton` 的兄弟节点，
		/// localPos (1.987, -0.405) 对播放键的 (0.390, -0.405)，<b>y 完全相同</b>）。
		/// 建造与运行同一个位置：顶栏在模拟里只是换了套按钮，这条滑条自己不动。
		/// </summary>
		private const float SliderX = 104f;

		private const float SliderY = PlayY;

		/// <summary>提示条与控件下边缘之间的空隙。</summary>
		private const float HintGap = 6f;

		/// <summary>触屏长按显示提示的阈值，与 <see cref="T:Bpsim.UI.HoldableButton" /> 一致。</summary>
		private const float LongPressSeconds = 0.5f;

		private static readonly Color PlayColor = new Color(0.11f, 0.52f, 0.23f, 1f);

		private static readonly Color StopColor = new Color(0.62f, 0.17f, 0.16f, 1f);

		private static readonly Color PlayDisabledColor = new Color(0.18f, 0.18f, 0.18f, 1f);

		/// <summary>滑槽颜色：比背景亮的一条浅灰线（Besiege 实测 69,72,88 压在 12,14,30 上）。</summary>
		private static readonly Color TrackColor = new Color(0.27f, 0.28f, 0.35f, 0.95f);

		/// <summary>旋钮：纯白实心圆。</summary>
		private static readonly Color KnobColor = new Color(1f, 1f, 1f, 1f);

		/// <summary>倍速百分比那行字：Besiege 的青色 (19, 235, 192)。</summary>
		private static readonly Color AccentColor = new Color(0.075f, 0.922f, 0.753f, 1f);

		private static readonly Color CapColor = new Color(1f, 1f, 1f, 0.75f);

		private static readonly Color HintColor = new Color(0.05f, 0.05f, 0.05f, 0.9f);

		private UserInterface m_root;

		private Canvas m_canvas;

		private RectTransform m_layer;

		private RectTransform m_playRect;

		private Button m_playButton;

		private Image m_playPlate;

		private Image m_playIcon;

		private RectTransform m_sliderRect;

		private HudSlider m_slider;

		private Sprite m_plateSprite;

		private bool m_runtimeLayout;

		/// <summary>控件 → 悬停/长按提示文字。</summary>
		private readonly Dictionary<RectTransform, string> m_hints = new Dictionary<RectTransform, string>(4);

		private GameObject m_hintRoot;

		private RectTransform m_hintRect;

		private Text m_hintText;

		private RectTransform m_hovered;

		private bool m_touchTracking;

		private float m_touchStartTime;

		private RectTransform m_touchTarget;

		private bool m_touchLongPressed;

		private Button m_suppressedButton;

		private int m_restoreFrame = -1;

		internal Canvas Canvas => m_canvas;

		/// <summary>HUD 画布的根矩形。</summary>
		internal RectTransform Layer => m_layer;

		internal Button PlayButton => m_playButton;

		internal HudSlider Slider => m_slider;

		internal RectTransform SliderRect => m_sliderRect;

		internal RectTransform PlayRect => m_playRect;

		internal bool IsRuntimeLayout => m_runtimeLayout;

		internal void Initialize(UserInterface root)
		{
			m_root = root;
			CreateCanvas();
			m_plateSprite = FindPlateSprite();
			CreatePlayButton();
			CreateSlider();
			CreateHint();
			ApplyLayout(false);
		}

		private void Update()
		{
			if (m_canvas == null)
			{
				return;
			}
			PartManager manager = PartManager.Instance;
			bool simulating = manager != null && manager.IsSimulating;
			if (simulating != m_runtimeLayout)
			{
				ApplyLayout(simulating);
			}
			if (m_slider != null)
			{
				m_slider.SyncFromCamera();
			}
			UpdatePlayAvailability(manager, simulating);
			UpdateTouchLongPress();
			UpdateHint();
		}

		/// <summary>
		/// 建造 ⇄ 运行两套摆位。验收探针也直接调它。
		/// 位置是两套共用的（Besiege 的播放键和滑条在模拟里不会挪窝），
		/// 只有图标、配色、提示文字和<b>滑条用途</b>随状态切换：
		/// 建造时滑条缩放镜头，模拟时滑条调时间倍速（Besiege 的时间滑条也是模拟里才生效）。
		/// </summary>
		internal void ApplyLayout(bool simulating)
		{
			m_runtimeLayout = simulating;
			if (m_playRect != null)
			{
				SetAnchor(m_playRect, new Vector2(0f, 1f));
				m_playRect.sizeDelta = new Vector2(PlaySize, PlaySize);
				m_playRect.anchoredPosition = new Vector2(PlayX, PlayY);
			}
			if (m_playPlate != null && !simulating)
			{
				m_playPlate.color = PlayColor;
			}
			if (m_playPlate != null && simulating)
			{
				m_playPlate.color = StopColor;
			}
			if (m_playIcon != null)
			{
				m_playIcon.sprite = LoadIcon(simulating ? "Icon_Stop" : "Icon_Simulate");
				m_playIcon.enabled = (m_playIcon.sprite != null);
				m_playIcon.rectTransform.sizeDelta = new Vector2(PlayIconSize, PlayIconSize);
			}
			if (m_sliderRect != null)
			{
				// 滑条位置不随状态变：它就在播放键右边，和 Besiege 的 TopBar 一样。
				SetAnchor(m_sliderRect, new Vector2(0f, 1f));
				m_sliderRect.sizeDelta = new Vector2(SliderWidth, SliderHeight);
				m_sliderRect.anchoredPosition = new Vector2(SliderX, SliderY);
			}
			if (m_slider != null)
			{
				// 切用途：缩放 ⇄ 时间倍速。切回缩放时会把 Time.timeScale 恢复成 1。
				m_slider.SetMode(simulating ? HudSliderMode.TimeScale : HudSliderMode.Zoom);
			}
			if (m_playRect != null)
			{
				// 按钮没搬过来时（场景里缺模拟按钮）字典不能拿 null 当键。
				m_hints[m_playRect] = (simulating ? "停止模拟" : "开始模拟");
			}
			if (m_sliderRect != null)
			{
				m_hints[m_sliderRect] = (simulating ? "时间倍速" : "镜头缩放");
			}
			if (m_hovered != null)
			{
				// 提示文字可能刚被换掉（开始 ⇄ 停止），强制下一帧重刷。
				m_hovered = null;
			}
		}

		// ------------------------------------------------------------- 画布

		private void CreateCanvas()
		{
			GameObject gameObject = new GameObject("BuildHudCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
			gameObject.transform.SetParent(transform, false);
			m_canvas = gameObject.GetComponent<Canvas>();
			m_canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			m_canvas.sortingOrder = HudSortingOrder;
			CanvasScaler canvasScaler = gameObject.GetComponent<CanvasScaler>();
			canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			canvasScaler.referenceResolution = new Vector2(1920f, 1080f);
			canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
			canvasScaler.matchWidthOrHeight = 1f;
			m_layer = (RectTransform)gameObject.transform;
		}

		// ------------------------------------------------------------- 播放键

		private void CreatePlayButton()
		{
			if (m_root == null || m_root.ActionBar == null)
			{
				return;
			}
			m_playButton = m_root.ActionBar.SimulateButton;
			if (m_playButton == null)
			{
				Debug.LogWarning("[BuildHud] ActionBar 上没有模拟按钮，HUD 的播放键未生成。");
				return;
			}
			m_playRect = (RectTransform)m_playButton.transform;
			// 从 ActionBar 搬到 HUD 画布：ActionBar 会在模拟时被关掉，播放键必须留在屏幕上。
			m_playRect.SetParent(m_layer, false);
			m_playRect.pivot = new Vector2(0.5f, 0.5f);
			m_playButton.onClick.RemoveAllListeners();
			m_playButton.onClick.AddListener(OnPlayClicked);
			m_playPlate = m_playButton.GetComponent<Image>();
			if (m_playPlate != null)
			{
				m_playPlate.sprite = m_plateSprite;
				m_playPlate.type = ((m_plateSprite != null) ? Image.Type.Sliced : Image.Type.Simple);
			}
			Button button = m_playButton;
			ColorBlock colors = button.colors;
			colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 1f);
			button.colors = colors;
			Transform textTransform = m_playRect.Find("Text");
			if (textTransform != null)
			{
				textTransform.gameObject.SetActive(false);
			}
			RectTransform iconRect = CreateImage("Icon", m_playRect, null, Color.white, new Vector2(PlayIconSize, PlayIconSize), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero);
			Image icon = iconRect.GetComponent<Image>();
			icon.preserveAspect = true;
			m_playIcon = icon;
		}

		private void OnPlayClicked()
		{
			PartManager manager = PartManager.Instance;
			if (manager == null)
			{
				return;
			}
			if (manager.IsSimulating)
			{
				if (SimulationInterface.Instance != null)
				{
					SimulationInterface.Instance.EndSimulation();
				}
				else
				{
					manager.EndSimulation();
				}
				return;
			}
			if (manager.HasActiveScene())
			{
				manager.StartSimulation();
			}
		}

		private void UpdatePlayAvailability(PartManager manager, bool simulating)
		{
			if (m_playButton == null)
			{
				return;
			}
			bool available = manager != null && (simulating || manager.HasActiveScene());
			if (m_playButton.interactable != available)
			{
				m_playButton.interactable = available;
			}
			if (m_playPlate != null && !simulating)
			{
				Color color = (available ? PlayColor : PlayDisabledColor);
				if (m_playPlate.color != color)
				{
					m_playPlate.color = color;
				}
			}
		}

		// ------------------------------------------------------------- 抬头滑条

		/// <summary>
		/// 样式照 Besiege 的时间滑条：一条细灰横线、一颗白色圆旋钮、左边一个用途图标，
		/// 倍速模式下图标右边还有一行青色百分比。没有填充条、没有底板。
		///
		/// 排布从左到右：模式图标 22 → 百分比 56 → 滑槽 160 → 缩小图标 22，
		/// 整条贴在播放键右边、和播放键同一条水平线上。
		///
		/// 同一个控件在两个界面里干两件事（见 <see cref="T:Bpsim.UI.HudSlider" />）：
		/// 建造 = 镜头缩放，模拟 = 时间倍速。
		/// </summary>
		private void CreateSlider()
		{
			GameObject gameObject = new GameObject("HudSlider", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
			gameObject.transform.SetParent(m_layer, false);
			m_sliderRect = (RectTransform)gameObject.transform;
			SetAnchor(m_sliderRect, new Vector2(0f, 1f));
			m_sliderRect.pivot = new Vector2(0f, 0.5f);
			m_sliderRect.sizeDelta = new Vector2(SliderWidth, SliderHeight);
			// 透明底板只用来接指针：整条滑条（含左边的图标和文字）都能拖。
			Image hit = gameObject.GetComponent<Image>();
			hit.color = new Color(0f, 0f, 0f, 0f);
			RectTransform modeIconRect = CreateImage("ModeIcon", m_sliderRect, LoadIcon("Icon_ZoomIn"), CapColor, new Vector2(22f, 22f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(2f, 0f));
			Image modeIcon = modeIconRect.GetComponent<Image>();
			modeIcon.preserveAspect = true;
			Text label = CreateSliderLabel(m_sliderRect);
			RectTransform track = CreateImage("Track", m_sliderRect, null, TrackColor, new Vector2(SliderTrackLength, SliderTrackThickness), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(SliderTrackOffsetX, 0f));
			RectTransform knob = CreateImage("Knob", track, LoadIcon("Icon_Knob"), KnobColor, new Vector2(SliderKnobSize, SliderKnobSize), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero);
			RectTransform tail = CreateImage("ZoomOut", m_sliderRect, LoadIcon("Icon_ZoomOut"), CapColor, new Vector2(22f, 22f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-2f, 0f));
			Image tailIcon = tail.GetComponent<Image>();
			tailIcon.preserveAspect = true;
			m_slider = gameObject.AddComponent<HudSlider>();
			m_slider.Initialize(track, knob, label, modeIcon, tailIcon, LoadIcon("Icon_ZoomIn"), LoadIcon("Icon_ZoomOut"), LoadIcon("Icon_Clock"));
			m_hints[SliderKey] = "镜头缩放";
		}

		/// <summary>倍速百分比（Besiege 的「72%」）。填在模式图标与滑槽之间；缩放模式隐藏。</summary>
		private Text CreateSliderLabel(Transform parent)
		{
			GameObject textObject = new GameObject("Value", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
			textObject.transform.SetParent(parent, false);
			RectTransform rect = (RectTransform)textObject.transform;
			rect.anchorMin = new Vector2(0f, 0.5f);
			rect.anchorMax = new Vector2(0f, 0.5f);
			rect.pivot = new Vector2(0f, 0.5f);
			rect.sizeDelta = new Vector2(56f, 22f);
			rect.anchoredPosition = new Vector2(26f, 0f);
			Text text = textObject.GetComponent<Text>();
			text.text = "100%";
			text.color = AccentColor;
			text.fontSize = 16;
			text.alignment = TextAnchor.MiddleLeft;
			text.horizontalOverflow = HorizontalWrapMode.Overflow;
			text.raycastTarget = false;
			text.enabled = false;
			Font font = HudFont();
			if (font != null)
			{
				text.font = font;
			}
			return text;
		}

		// ------------------------------------------------------------- 提示

		/// <summary>
		/// 提示条的键。用控件本身当键会因为 <c>RectTransform</c> 的 <c>GetHashCode</c> 依赖
		/// 实例而需要持有引用；播放键和滑条都是我们自己建的，直接按字段取即可。
		/// </summary>
		private RectTransform PlayKey => m_playRect;

		private RectTransform SliderKey => m_sliderRect;

		private void CreateHint()
		{
			m_hintRoot = new GameObject("Hint", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
			m_hintRoot.transform.SetParent(m_layer, false);
			m_hintRoot.transform.SetAsLastSibling();
			m_hintRect = (RectTransform)m_hintRoot.transform;
			m_hintRect.anchorMin = new Vector2(0.5f, 0.5f);
			m_hintRect.anchorMax = new Vector2(0.5f, 0.5f);
			m_hintRect.pivot = new Vector2(0.5f, 1f);
			m_hintRect.sizeDelta = new Vector2(120f, 30f);
			Image image = m_hintRoot.GetComponent<Image>();
			image.sprite = m_plateSprite;
			image.type = ((m_plateSprite != null) ? Image.Type.Sliced : Image.Type.Simple);
			image.color = HintColor;
			image.raycastTarget = false;
			GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
			textObject.transform.SetParent(m_hintRoot.transform, false);
			RectTransform textRect = (RectTransform)textObject.transform;
			textRect.anchorMin = Vector2.zero;
			textRect.anchorMax = Vector2.one;
			textRect.offsetMin = new Vector2(12f, 0f);
			textRect.offsetMax = new Vector2(-12f, 0f);
			m_hintText = textObject.GetComponent<Text>();
			m_hintText.alignment = TextAnchor.MiddleLeft;
			m_hintText.fontSize = 18;
			m_hintText.color = Color.white;
			m_hintText.raycastTarget = false;
			Font font = HudFont();
			if (font != null)
			{
				m_hintText.font = font;
			}
			m_hintRoot.SetActive(false);
		}

		private static Font FindFont(Transform button)
		{
			Transform transform = button.Find("Text");
			Text text = (transform == null) ? null : transform.GetComponent<Text>();
			return (text == null) ? null : text.font;
		}

		/// <summary>抬头上的文字用工具栏按钮那套字体（工程里没有全局默认字体）。</summary>
		private Font HudFont()
		{
			if (m_root == null || m_root.ActionBar == null || m_root.ActionBar.TemplateButton == null)
			{
				return null;
			}
			return FindFont(m_root.ActionBar.TemplateButton.transform);
		}

		/// <summary>触屏长按 0.5 秒显示提示；越过阈值时把按钮临时置为不可交互，抬起不会误触发。</summary>
		private void UpdateTouchLongPress()
		{
			if (m_restoreFrame >= 0 && Time.frameCount >= m_restoreFrame)
			{
				m_restoreFrame = -1;
				if (m_suppressedButton != null)
				{
					m_suppressedButton.interactable = true;
					m_suppressedButton = null;
				}
			}
			Touchscreen touchscreen = Touchscreen.current;
			if (touchscreen == null)
			{
				return;
			}
			UnityEngine.InputSystem.Controls.TouchControl touch = touchscreen.primaryTouch;
			if (touch.press.isPressed)
			{
				if (!m_touchTracking)
				{
					m_touchTracking = true;
					m_touchStartTime = Time.unscaledTime;
					m_touchTarget = FindHintAt(touch.position.ReadValue());
				}
				if (!m_touchLongPressed && m_touchTarget != null && Time.unscaledTime - m_touchStartTime >= LongPressSeconds)
				{
					m_touchLongPressed = true;
					// 长按只用来「看」提示：临时禁用按钮，抬起时 uGUI 的 Button.OnPointerUp
					// 会因 IsActive() 为假而不触发 onClick（否则长按播放键会顺手开模拟）。
					// 滑条上没有 Button，天然不受影响。
					Button button = m_touchTarget.GetComponent<Button>();
					if (button != null && button.interactable)
					{
						button.interactable = false;
						m_suppressedButton = button;
					}
				}
			}
			else
			{
				if (m_touchLongPressed && m_restoreFrame < 0)
				{
					m_restoreFrame = Time.frameCount + 1;
				}
				m_touchTracking = false;
				m_touchLongPressed = false;
				m_touchTarget = null;
			}
		}

		private RectTransform FindHintAt(Vector2 point)
		{
			foreach (KeyValuePair<RectTransform, string> hint in m_hints)
			{
				if (hint.Key != null && hint.Key.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(hint.Key, point, null))
				{
					return hint.Key;
				}
			}
			return null;
		}

		private void UpdateHint()
		{
			if (m_hintRoot == null)
			{
				return;
			}
			RectTransform hovered;
			if (m_touchLongPressed)
			{
				hovered = m_touchTarget;
			}
			else
			{
				hovered = ((Mouse.current != null) ? FindHintAt(Mouse.current.position.ReadValue()) : null);
			}
			if (hovered == m_hovered)
			{
				return;
			}
			m_hovered = hovered;
			if (hovered == null)
			{
				m_hintRoot.SetActive(false);
				return;
			}
			string label;
			if (!m_hints.TryGetValue(hovered, out label))
			{
				m_hintRoot.SetActive(false);
				return;
			}
			m_hintText.text = label;
			m_hintText.horizontalOverflow = HorizontalWrapMode.Overflow;
			float width = Mathf.Max(96f, m_hintText.preferredWidth + 24f);
			m_hintRect.sizeDelta = new Vector2(width, 30f);
			Vector3[] corners = new Vector3[4];
			hovered.GetWorldCorners(corners);
			Vector2 bottomCenter = RectTransformUtility.WorldToScreenPoint(null, (corners[0] + corners[3]) * 0.5f);
			RectTransformUtility.ScreenPointToLocalPointInRectangle(m_layer, bottomCenter, null, out Vector2 local);
			float halfLayer = m_layer.rect.width * 0.5f;
			float halfWidth = width * 0.5f;
			float minX = -halfLayer + halfWidth + 8f;
			float maxX = Mathf.Max(minX, halfLayer - halfWidth - 8f);
			m_hintRect.anchoredPosition = new Vector2(Mathf.Clamp(local.x, minX, maxX), local.y - HintGap);
			m_hintRoot.SetActive(true);
		}

		// ------------------------------------------------------------- 探针接口

		/// <summary>供验收探针使用：某个控件上登记的提示文字。</summary>
		internal string HintOf(string control)
		{
			RectTransform key = null;
			if (control == "PlayButton")
			{
				key = m_playRect;
			}
			else if (control == "HudSlider")
			{
				key = m_sliderRect;
			}
			if (key == null)
			{
				return null;
			}
			string label;
			return m_hints.TryGetValue(key, out label) ? label : null;
		}

		/// <summary>供验收探针使用：屏幕坐标 → 该处控件的提示文字。</summary>
		internal string HintAtScreenPoint(Vector2 screenPoint)
		{
			RectTransform hovered = FindHintAt(screenPoint);
			string label;
			return (hovered != null && m_hints.TryGetValue(hovered, out label)) ? label : null;
		}

		// ------------------------------------------------------------- 工具

		private static void SetAnchor(RectTransform rect, Vector2 anchor)
		{
			rect.anchorMin = anchor;
			rect.anchorMax = anchor;
		}

		private static RectTransform CreateImage(string name, Transform parent, Sprite sprite, Color color, Vector2 size, Vector2 anchor, Vector2 pivot, Vector2 position, bool sliced = false)
		{
			GameObject gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
			gameObject.transform.SetParent(parent, false);
			RectTransform rectTransform = (RectTransform)gameObject.transform;
			SetAnchor(rectTransform, anchor);
			rectTransform.pivot = pivot;
			rectTransform.sizeDelta = size;
			rectTransform.anchoredPosition = position;
			Image image = gameObject.GetComponent<Image>();
			image.sprite = sprite;
			image.color = color;
			image.type = ((sliced && sprite != null) ? Image.Type.Sliced : Image.Type.Simple);
			image.raycastTarget = false;
			return rectTransform;
		}

		/// <summary>
		/// 借一张九宫格圆角 Sprite。和 <see cref="T:Bpsim.UI.BuildToolbar" /> 用的是同一张
		/// （左边栏按钮底板上那张内置九宫格）：<c>Resources.GetBuiltinResource</c> 在本工程会失败。
		/// </summary>
		private Sprite FindPlateSprite()
		{
			if (m_root == null || m_root.Canvas == null)
			{
				return null;
			}
			Image[] images = m_root.Canvas.GetComponentsInChildren<Image>(true);
			foreach (Image image in images)
			{
				if (image != null && image.sprite != null && image.type == Image.Type.Sliced)
				{
					return image.sprite;
				}
			}
			return null;
		}

		private static Sprite LoadIcon(string iconName)
		{
			if (string.IsNullOrEmpty(iconName))
			{
				return null;
			}
			Sprite sprite = Resources.Load<Sprite>("BuildToolbar/" + iconName);
			if (sprite == null)
			{
				Debug.LogWarning("[BuildHud] 缺少图标资源 Resources/BuildToolbar/" + iconName);
			}
			return sprite;
		}
	}
}
