using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Bpsim.UI
{
	/// <summary>
	/// 顶部工具栏的视图端。它把 ActionBar 上的每个按钮都做成<b>图标按钮</b>：
	/// 克隆现有的 LoadButton 当骨架（保留 Image / Button / ResourceBinder 的既有装配），
	/// 把底板的 sprite 换成九宫格圆角、把文字层换成 36×36 的白色图标，
	/// 图标资源在 <c>Assets/Resources/BuildToolbar</c>（由 <c>_codex_work/topbar/make_icons.py</c> 生成）。
	/// 图标按钮没有文字，所以鼠标悬停时在工具栏下方弹一条提示文字。
	/// 逻辑全部在 <see cref="T:Bpsim.UI.BuildToolController" />，这里只负责按钮、图标与状态同步。
	/// </summary>
	internal class BuildToolbar : MonoBehaviour
	{
		/// <summary>图标按钮边长。左边栏的工具按钮是 60，这里略大一点以容纳 36px 图标。</summary>
		private const float ButtonSize = 64f;

		/// <summary>相邻按钮中心距（8px 间隙）。</summary>
		private const float ButtonStep = 72f;

		/// <summary>分组之间的额外间隔。</summary>
		private const float GroupGap = 24f;

		/// <summary>
		/// 第一个文件按钮的中心 x。要让出左上角 HUD 那一整块：
		/// 播放键中心 56、边长 76（占 18..94），右边紧跟一条横向滑条
		/// （x 104..386，见 <see cref="T:Bpsim.UI.BuildHud" />）。
		/// 所以文件按钮组从 428 起排（386 + 10 空隙 + 32 半宽）。
		/// </summary>
		private const float FileStartX = 428f;

		private const float IconSize = 36f;

		/// <summary>FPS 文本占用的右侧宽度（88 宽 + 16 余量）。工具栏不能压过去。</summary>
		private const float FpsZone = 104f;

		/// <summary>
		/// 最近一次排版的按钮中心距与边长。窄窗口下 <see cref="Reflow" /> 会把它们压小，
		/// 验收探针靠这两个值核对「文件按钮等距且不重叠」，而不是写死 72。
		/// </summary>
		internal static float LastStep { get; private set; } = ButtonStep;

		internal static float LastButtonSize { get; private set; } = ButtonSize;

		/// <summary>提示条相对 ActionBar 底边的距离。</summary>
		private const float HintOffsetY = 96f;

		/// <summary>
		/// 触屏上没有「悬停」，改用长按显示提示。阈值与 <see cref="T:Bpsim.UI.HoldableButton" />
		/// 的长按阈值（0.5s）一致，玩家不用记两套手感。
		/// </summary>
		private const float LongPressSeconds = 0.5f;

		private static readonly Color BaseColor = new Color(0f, 0.196875f, 0.4f, 1f);

		private static readonly Color ActiveColor = new Color(0f, 0.48235294f, 0.72156864f, 1f);

		private static readonly Color DisabledColor = new Color(0.18f, 0.18f, 0.18f, 1f);

		private static readonly Color HintColor = new Color(0.05f, 0.05f, 0.05f, 0.9f);

		/// <summary>九宫格圆角底板。从场景里已有按钮上借一张，见 <see cref="FindPlateSprite" />。</summary>
		private Sprite m_plateSprite;

		private readonly List<Button> m_toolButtons = new List<Button>(8);

		private readonly List<Image> m_toolImages = new List<Image>(8);

		private readonly List<Image> m_toolIcons = new List<Image>(8);

		/// <summary>与 m_toolButtons 一一对应的工具枚举；加按钮后不用再改 Refresh 里的硬编码下标。</summary>
		private readonly List<BuildTool> m_toolValues = new List<BuildTool>(8);

		/// <summary>按钮 → 悬停提示文字。</summary>
		private readonly Dictionary<RectTransform, string> m_hints = new Dictionary<RectTransform, string>(16);

		private UserInterface m_root;

		private BuildToolController m_controller;

		private RectTransform m_actionBar;

		private Button m_moveButton;

		private Button m_moveAllButton;

		private Button m_transformPartButton;

		private Button m_transformMachineButton;

		private Button m_gridButton;

		private Button m_undoButton;

		private Image m_gridImage;

		private Image m_gridIcon;

		/// <summary>三态网格按钮用的两张图标：吸附态是方格，自由放置态是无网格。</summary>
		private Sprite m_gridSprite;

		private Sprite m_gridlessSprite;

		private Image m_undoImage;

		private Image m_undoIcon;

		private GameObject m_hintRoot;

		private RectTransform m_hintRect;

		private Image m_hintImage;

		private Text m_hintText;

		private RectTransform m_hovered;

		/// <summary>当前这一轮触摸是否已经开始计时（手指按下到抬起之间为真）。</summary>
		private bool m_touchTracking;

		private float m_touchStartTime;

		/// <summary>手指按下时落在的按钮；长按期间提示条就挂在这个按钮上。</summary>
		private RectTransform m_touchButton;

		/// <summary>本次触摸已经越过长按阈值（提示条已显示，抬起时不应该再触发点击）。</summary>
		private bool m_touchLongPressed;

		/// <summary>被长按临时置为不可交互的按钮（用来吞掉抬起时的 onClick）。</summary>
		private Button m_suppressedButton;

		/// <summary>恢复 <see cref="m_suppressedButton" /> 的帧号，-1 表示没有待恢复的按钮。</summary>
		private int m_restoreFrame = -1;

		/// <summary>按从左到右的摆放顺序记下所有按钮，<see cref="Reflow" /> 靠它重新排。</summary>
		private readonly List<RectTransform> m_layout = new List<RectTransform>();

		/// <summary>分组的第一颗按钮在 <see cref="m_layout" /> 里的下标（这些位置前面多留一段空隙）。</summary>
		private readonly List<int> m_groupStarts = new List<int>();

		/// <summary>上一次排版依据的顶栏宽度（0 表示还没按真实宽度排过）。</summary>
		private float m_reflowWidth;

		public void Initialize(UserInterface root, BuildToolController controller)
		{
			m_root = root;
			m_controller = controller;
			GameObject gameObject = root.ActionBar.GameObject;
			m_actionBar = (RectTransform)gameObject.transform;
			Button template = root.ActionBar.TemplateButton;
			if (template == null)
			{
				Debug.LogWarning("[BuildToolbar] ActionBar 上没有可用的按钮模板，工具栏未生成。");
				return;
			}
			m_plateSprite = FindPlateSprite();
			SkinExistingButtons();
			float x = LayoutExistingButtons();
			BeginGroup();
			x = NextGroup(x);
			// 一组「按格吸附」：拖动平移，原地单击旋转。旋转以前是两枚独立按钮
			// （平移部件 / 旋转部件、整车平移 / 整车旋转），和快速建造里「短按旋转、长按拖动」
			// 是同一个手势的两种结局，所以并进同一枚按钮，靠「有没有拖动」区分。
			m_moveButton = CreateToolButton(template, "BuildMoveButton", "平移部件（拖动移动，单击旋转）", "Icon_Move", ref x, BuildTool.Move);
			m_moveAllButton = CreateToolButton(template, "BuildMoveAllButton", "整车平移（拖动移动，单击整车转 90°）", "Icon_MoveAll", ref x, BuildTool.MoveAll);
			BeginGroup();
			x = NextGroup(x);
			// 一组「自由变换 gizmo」：都只写 PartFreeTransform，不动网格占用表。
			// 「部件变换」与「自由变换」原本是两个按钮，但控制器里是同一套手势代码，
			// 唯一差别就是缩放手柄画不画，所以缩放手柄改成常显、按钮合成一枚。
			m_transformPartButton = CreateToolButton(template, "BuildTransformPartButton", "部件变换（拖箭头平移、圆环旋转、四角/四边拉伸）", "Icon_FreeTransformPart", ref x, BuildTool.TransformPart);
			m_transformMachineButton = CreateToolButton(template, "BuildTransformMachineButton", "载具变换（整车平移/旋转/缩放）", "Icon_TransformMachine", ref x, BuildTool.TransformMachine);
			BeginGroup();
			x = NextGroup(x);
			// 网格按钮是三态的（吸附+显示 / 吸附+隐藏 / 自由放置）：以前是「网格」「无网格」
			// 两枚按钮，但「无网格」本来就会把网格藏起来，两枚按钮能凑出
			// 「网格：开 + 无网格：开 + 屏幕上看不到网格」这种自相矛盾的状态。
			m_gridSprite = LoadIcon("Icon_Grid");
			m_gridlessSprite = LoadIcon("Icon_Gridless");
			m_gridButton = CreateToolButton(template, "BuildGridButton", "网格", "Icon_Grid", ref x, BuildTool.None);
			m_gridImage = m_gridButton.GetComponent<Image>();
			m_gridIcon = FindIcon(m_gridButton.transform);
			m_gridButton.onClick.AddListener(CycleGridMode);
			m_undoButton = CreateToolButton(template, "BuildUndoButton", "撤销", "Icon_Undo", ref x, BuildTool.None);
			m_undoImage = m_undoButton.GetComponent<Image>();
			m_undoIcon = FindIcon(m_undoButton.transform);
			m_undoButton.onClick.AddListener(m_controller.Undo);
			Reflow();
			CreateHint();
			m_controller.StateChanged += Refresh;
			Refresh();
		}

		private void OnDestroy()
		{
			if (m_controller != null)
			{
				m_controller.StateChanged -= Refresh;
			}
		}

		private void Update()
		{
			// 顶栏是拉伸锚定的，Initialize 时 rect 还没有真实宽度；等布局算出宽度再收一次间距。
			// 门槛 400：Canvas 重建过程中 rect 会短暂给出离谱的小值，那种时候不要动。
			if (m_actionBar != null)
			{
				float width = m_actionBar.rect.width;
				if (width >= 400f && Mathf.Abs(width - m_reflowWidth) > 1f)
				{
					Reflow();
				}
			}
			UpdateTouchLongPress();
			UpdateHint();
		}

		/// <summary>供验收探针使用：按钮上注册的提示文字（没有则返回 null）。</summary>
		internal string HintOf(string buttonName)
		{
			if (m_actionBar == null)
			{
				return null;
			}
			Transform transform = m_actionBar.Find(buttonName);
			if (transform == null)
			{
				return null;
			}
			string label;
			return m_hints.TryGetValue((RectTransform)transform, out label) ? label : null;
		}

		/// <summary>
		/// 供验收探针使用：给定屏幕坐标，返回该处按钮的提示文字（没有则返回 null）。
		/// 走的是和 <see cref="UpdateHint" /> 完全相同的命中逻辑，所以能验到
		/// 「屏幕坐标 → 按钮矩形」这一段（overlay canvas 下 <c>RectangleContainsScreenPoint</c> 的相机参数最容易搞错）。
		/// </summary>
		internal string HintAtScreenPoint(Vector2 screenPoint)
		{
			RectTransform hovered = FindHintAt(screenPoint);
			string label;
			return (hovered != null && m_hints.TryGetValue(hovered, out label)) ? label : null;
		}

		// ------------------------------------------------------------- 触屏长按

		/// <summary>
		/// 触屏（安卓）没有鼠标悬停，改成「按住 0.5 秒显示提示」。越过阈值时把按钮临时置为
		/// 不可交互：uGUI 的 <c>Button.OnPointerUp</c> 里 <c>IsActive()</c> 会检查
		/// <c>interactable</c>，因此抬起时不会触发 onClick —— 长按只用来「看」，不顺手把工具
		/// 打开或把一步撤销掉。恢复放到下一帧，避免和 EventSystem 处理 PointerUp 抢先后。
		/// </summary>
		private void UpdateTouchLongPress()
		{
			if (m_restoreFrame >= 0 && Time.frameCount >= m_restoreFrame)
			{
				RestoreSuppressedButton();
			}
			Touchscreen touchscreen = Touchscreen.current;
			if (touchscreen == null || m_actionBar == null)
			{
				return;
			}
			UnityEngine.InputSystem.Controls.TouchControl touch = touchscreen.primaryTouch;
			bool pressed = touch.press.isPressed;
			if (pressed)
			{
				if (!m_touchTracking)
				{
					m_touchTracking = true;
					m_touchStartTime = Time.unscaledTime;
					m_touchButton = FindHintAt(touch.position.ReadValue());
				}
				if (!m_touchLongPressed && m_touchButton != null && Time.unscaledTime - m_touchStartTime >= LongPressSeconds)
				{
					m_touchLongPressed = true;
					SuppressButton(m_touchButton);
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
				m_touchButton = null;
			}
		}

		private void SuppressButton(RectTransform target)
		{
			Button button = target.GetComponent<Button>();
			if (button == null || !button.interactable)
			{
				return;
			}
			m_suppressedButton = button;
			button.interactable = false;
		}

		private void RestoreSuppressedButton()
		{
			m_restoreFrame = -1;
			if (m_suppressedButton == null)
			{
				return;
			}
			m_suppressedButton.interactable = true;
			m_suppressedButton = null;
			Refresh();
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

		// ------------------------------------------------------------- 皮肤

		/// <summary>把已有按钮（加载/保存/截图/设置/模拟）从文字按钮改成图标按钮。</summary>
		private void SkinExistingButtons()
		{
			foreach (RectTransform item in m_actionBar)
			{
				if (item == null || item.name == "Text")
				{
					continue;
				}
				if (!(item.GetComponent<Button>() != null) || !item.gameObject.activeSelf)
				{
					continue;
				}
				string iconName = IconNameFor(item.name);
				string label = LabelFor(item.name);
				if (label != null)
				{
					m_hints[item] = label;
				}
				ApplyIconSkin(item.gameObject, iconName, item.name == "SimulationButton");
			}
		}

		private static string IconNameFor(string buttonName)
		{
			switch (buttonName)
			{
			case "LoadButton":
				return "Icon_Load";
			case "SaveButton":
				return "Icon_Save";
			case "ScreenshotButton":
				return "Icon_Screenshot";
			case "SettingsButton":
				return "Icon_Settings";
			case "SimulationButton":
				return "Icon_Simulate";
			default:
				return null;
			}
		}

		private static string LabelFor(string buttonName)
		{
			switch (buttonName)
			{
			case "LoadButton":
				return "加载场景";
			case "SaveButton":
				return "保存场景";
			case "ScreenshotButton":
				return "截图";
			case "SettingsButton":
				return "设置";
			case "SimulationButton":
				return "开始模拟";
			default:
				return null;
			}
		}

		/// <summary>
		/// 借一张九宫格圆角 Sprite 给工具栏按钮当底板。
		/// </summary>
		/// <remarks>
		/// 不能用 <c>Resources.GetBuiltinResource&lt;Sprite&gt;("UI/Skin/Background.psd")</c>：
		/// 这个工程里那句会稳定失败并往 Console 刷
		/// <c>The resource UI/Skin/Background.psd could not be loaded from the resource file!</c>，
		/// 按钮底板就成了没有 Sprite 的纯色方块（没有圆角）。而 prefab 里左边栏的
		/// <c>ViewButton/Background</c> 本来就用着同一张内置九宫格图，直接把它借过来即可，
		/// 既不用新美术资源，也和左边栏按钮完全同款。
		/// </remarks>
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
			Debug.LogWarning("[BuildToolbar] 没在界面上找到九宫格底板 Sprite，按钮将退化为纯色方块。");
			return null;
		}

		/// <summary>把现有的文件操作按钮重排成等距，返回下一个可用中心 x。</summary>
		private float LayoutExistingButtons()
		{
			List<RectTransform> list = new List<RectTransform>(8);
			foreach (RectTransform item in m_actionBar)
			{
				if (item == null || item.name == "Text")
				{
					continue;
				}
				if (item.GetComponent<Button>() != null && item.gameObject.activeSelf)
				{
					list.Add(item);
				}
			}
			list.Sort((a, b) => a.anchoredPosition.x.CompareTo(b.anchoredPosition.x));
			m_layout.Clear();
			m_layout.AddRange(list);
			for (int i = 0; i < list.Count; i++)
			{
				PlaceAt(list[i], FileStartX + ButtonStep * i);
			}
			RectTransform rectTransform = m_actionBar.Find("Text") as RectTransform;
			if (rectTransform != null)
			{
				// 文件按钮组右移一个槽位后，右侧 FPS 文本跟着收窄贴边，别被工具栏顶到。
				rectTransform.anchoredPosition = new Vector2(-48f, 0f);
				rectTransform.sizeDelta = new Vector2(88f, 60f);
			}
			return (list.Count > 0) ? (FileStartX + ButtonStep * (list.Count - 1)) : (FileStartX - ButtonStep);
		}

		/// <summary>标记下一颗按钮开启新的一组（组前多留一段空隙）。</summary>
		private void BeginGroup()
		{
			if (m_groupStarts.Count == 0 || m_groupStarts[m_groupStarts.Count - 1] != m_layout.Count)
			{
				m_groupStarts.Add(m_layout.Count);
			}
		}

		/// <summary>
		/// 按顶栏实际宽度收一遍间距。窄窗口（batchmode 的 1440）下 15 颗按钮按 72 的间距
		/// 会顶到右边 FPS 文本上，所以按比例压小边长与间距直到塞得下；宽窗口（1920/2098）
		/// 比例仍是 1，外观与设计值完全一致。
		/// </summary>
		private void Reflow()
		{
			if (m_layout.Count == 0)
			{
				return;
			}
			float width = m_actionBar.rect.width;
			if (width < 400f)
			{
				width = 1440f;
			}
			float limit = width - FpsZone;
			float size = ButtonSize;
			float step = ButtonStep;
			float gap = GroupGap;
			for (float k = 1f; k > 0.5f; k -= 0.02f)
			{
				size = ButtonSize * k;
				step = ButtonStep * k;
				gap = Mathf.Max(6f, GroupGap * k);
				float span = (m_layout.Count - 1) * step + m_groupStarts.Count * gap;
				if (FileStartX + span + size * 0.5f <= limit)
				{
					break;
				}
			}
			float x = FileStartX;
			for (int i = 0; i < m_layout.Count; i++)
			{
				if (m_groupStarts.Contains(i))
				{
					x += gap;
				}
				PlaceAt(m_layout[i], x, size);
				x += step;
			}
			float icon = Mathf.Min(IconSize, size * 0.5625f);
			foreach (RectTransform rect in m_layout)
			{
				Image image = FindIcon(rect);
				if (image != null)
				{
					image.rectTransform.sizeDelta = new Vector2(icon, icon);
				}
			}
			LastStep = step;
			LastButtonSize = size;
			m_reflowWidth = width;
		}

		private static float NextGroup(float lastCenter)
		{
			return lastCenter + ButtonStep * 0.5f + GroupGap + ButtonSize * 0.5f;
		}

		private static void PlaceAt(RectTransform rect, float x)
		{
			PlaceAt(rect, x, ButtonSize);
		}

		private static void PlaceAt(RectTransform rect, float x, float size)
		{
			rect.anchoredPosition = new Vector2(x, 0f);
			rect.sizeDelta = new Vector2(size, size);
		}

		/// <summary>底板换成九宫格圆角、文字层换成图标。图标注在 <paramref name="activeAsIcon" /> 为真时保持常白。</summary>
		private Image ApplyIconSkin(GameObject target, string iconName, bool accentPlate)
		{
			Image image = target.GetComponent<Image>();
			if (image != null)
			{
				image.sprite = m_plateSprite;
				image.type = Image.Type.Sliced;
				image.color = (accentPlate ? ActiveColor : BaseColor);
			}
			// 底板颜色由这里和 Refresh() 直接给，不需要 ColorTint 再叠一层。
			// 把 disabled 的着色改成纯白，长按临时禁用时按钮才不会突然变暗 ——
			// 「撤销」不可用时的灰色底板同样由 Refresh() 自己上色。
			Button button = target.GetComponent<Button>();
			if (button != null)
			{
				ColorBlock colors = button.colors;
				colors.disabledColor = Color.white;
				button.colors = colors;
			}
			Transform textTransform = target.transform.Find("Text");
			if (textTransform != null)
			{
				textTransform.gameObject.SetActive(false);
			}
			Image icon = FindIcon(target.transform);
			if (icon == null)
			{
				GameObject gameObject = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
				gameObject.transform.SetParent(target.transform, false);
				RectTransform rectTransform = (RectTransform)gameObject.transform;
				rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
				rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
				rectTransform.pivot = new Vector2(0.5f, 0.5f);
				rectTransform.anchoredPosition = Vector2.zero;
				rectTransform.sizeDelta = new Vector2(IconSize, IconSize);
				icon = gameObject.GetComponent<Image>();
				icon.raycastTarget = false;
			}
			Sprite sprite = LoadIcon(iconName);
			icon.sprite = sprite;
			icon.type = Image.Type.Simple;
			icon.preserveAspect = true;
			icon.color = Color.white;
			icon.enabled = (sprite != null);
			return icon;
		}

		private static Image FindIcon(Transform button)
		{
			Transform transform = button.Find("Icon");
			return (transform == null) ? null : transform.GetComponent<Image>();
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
				Debug.LogWarning("[BuildToolbar] 缺少图标资源 Resources/BuildToolbar/" + iconName);
			}
			return sprite;
		}

		private Button CreateToolButton(Button template, string name, string label, string iconName, ref float x, BuildTool tool)
		{
			Button button = Object.Instantiate(template, m_actionBar, false);
			button.name = name;
			button.onClick.RemoveAllListeners();
			PlaceAt((RectTransform)button.transform, x);
			m_layout.Add((RectTransform)button.transform);
			x += ButtonStep;
			Image plate = ApplyIconSkin(button.gameObject, iconName, false);
			Image icon = FindIcon(button.transform);
			m_hints[(RectTransform)button.transform] = label;
			if (tool != BuildTool.None)
			{
				BuildTool captured = tool;
				button.onClick.AddListener(delegate
				{
					m_controller.SetTool(captured);
				});
				m_toolButtons.Add(button);
				m_toolImages.Add(plate);
				m_toolIcons.Add(icon);
				m_toolValues.Add(tool);
			}
			return button;
		}

		// ------------------------------------------------------------- 状态

		private void Refresh()
		{
			if (m_controller == null)
			{
				return;
			}
			BuildTool currentTool = m_controller.CurrentTool;
			for (int i = 0; i < m_toolButtons.Count && i < m_toolValues.Count; i++)
			{
				SetToolVisual(m_toolButtons[i], i, m_toolValues[i], currentTool);
			}
			GridModeState gridMode = CurrentGridMode();
			if (m_gridImage != null)
			{
				m_gridImage.color = ((gridMode == GridModeState.Free) ? BaseColor : ActiveColor);
			}
			if (m_gridIcon != null)
			{
				Sprite sprite = ((gridMode == GridModeState.Free) ? m_gridlessSprite : m_gridSprite);
				if (sprite != null)
				{
					m_gridIcon.sprite = sprite;
				}
				m_gridIcon.color = ((gridMode == GridModeState.Free) ? new Color(1f, 1f, 1f, 0.85f) : Color.white);
			}
			m_hints[(RectTransform)m_gridButton.transform] = GridHint(gridMode);
			bool canUndo = m_controller.CanUndo;
			if (m_undoButton != null)
			{
				// 长按正在吞点击时不要把它改回可交互，否则抬起时又会被点掉。
				m_undoButton.interactable = canUndo && m_suppressedButton != m_undoButton;
			}
			if (m_undoImage != null)
			{
				m_undoImage.color = (canUndo ? BaseColor : DisabledColor);
			}
			if (m_undoIcon != null)
			{
				m_undoIcon.color = (canUndo ? Color.white : new Color(1f, 1f, 1f, 0.35f));
			}
		}

		private void SetToolVisual(Button button, int index, BuildTool tool, BuildTool currentTool)
		{
			if (button == null || index >= m_toolImages.Count || index >= m_toolIcons.Count)
			{
				return;
			}
			bool active = currentTool == tool;
			m_toolImages[index].color = (active ? ActiveColor : BaseColor);
			m_toolIcons[index].color = (active ? Color.white : new Color(1f, 1f, 1f, 0.85f));
		}

		// ------------------------------------------------------------- 网格三态

		/// <summary>
		/// 网格按钮的三个状态。以前是「网格」「无网格」两枚独立按钮，但
		/// <see cref="P:Bpsim.Parts.PartScene.GridEnabled" /> 与
		/// <see cref="P:Bpsim.Parts.PartScene.GridlessEnabled" /> 共同决定网格可见性
		/// （<c>gridVisible = gridEnabled &amp;&amp; !gridlessEnabled</c>），两枚按钮能凑出
		/// 「网格：开 + 无网格：开 + 屏幕上看不到网格」这种自相矛盾的显示，所以合并成一个循环。
		/// </summary>
		internal enum GridModeState
		{
			/// <summary>吸附格心 + 显示网格。</summary>
			Snap,

			/// <summary>仍然吸附格心，但把网格藏起来（截图、看结构时用）。</summary>
			Hidden,

			/// <summary>自由放置：单击放置按光标位置记偏移，网格随之隐藏。</summary>
			Free
		}

		internal GridModeState CurrentGridMode()
		{
			if (m_controller == null)
			{
				return GridModeState.Snap;
			}
			if (m_controller.GridlessEnabled)
			{
				return GridModeState.Free;
			}
			return m_controller.GridEnabled ? GridModeState.Snap : GridModeState.Hidden;
		}

		internal static string GridHint(GridModeState mode)
		{
			switch (mode)
			{
			case GridModeState.Hidden:
				return "网格：隐藏（仍吸附格心，单击切换为自由放置）";
			case GridModeState.Free:
				return "自由放置：开（单击放置不吸附格心，单击恢复网格）";
			default:
				return "网格：显示 + 吸附（单击隐藏，再单击自由放置）";
			}
		}

		/// <summary>显示 → 隐藏 → 自由放置 → 显示，单击循环一次。</summary>
		private void CycleGridMode()
		{
			CycleGridModeForTest();
			Refresh();
		}

		/// <summary>供验收探针调用：等价于点一下网格按钮，但不经过 uGUI 的事件系统。</summary>
		internal void CycleGridModeForTest()
		{
			if (m_controller == null)
			{
				return;
			}
			switch (CurrentGridMode())
			{
			case GridModeState.Snap:
				m_controller.GridEnabled = false;
				break;
			case GridModeState.Hidden:
				m_controller.GridlessEnabled = true;
				break;
			default:
				m_controller.GridlessEnabled = false;
				m_controller.GridEnabled = true;
				break;
			}
		}

		// ------------------------------------------------------------- 悬停提示

		private void CreateHint()
		{
			// 提示条挂在 ActionBar 的父物体（Panel）上并置为最后一个子物体，而不是挂在 ActionBar 里：
			// uGUI 按层级顺序绘制，ActionBar 的子树整棵都排在 Panel/SceneTabView 之前，提示条落在
			// 按钮下方 y=-96 处正好压在场景标签条（-90 ~ -150）上，挂在 ActionBar 里会被标签条盖住。
			// Panel 与 ActionBar 的左上角重合（ActionBar 顶到边、左到边），所以下面按 ActionBar
			// 坐标算的 anchoredPosition 在 Panel 坐标系里数值完全一致。
			Transform hintParent = ((m_actionBar.parent != null) ? m_actionBar.parent : m_actionBar);
			m_hintRoot = new GameObject("Hint", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
			m_hintRoot.transform.SetParent(hintParent, false);
			m_hintRoot.transform.SetAsLastSibling();
			m_hintRect = (RectTransform)m_hintRoot.transform;
			m_hintRect.anchorMin = new Vector2(0f, 1f);
			m_hintRect.anchorMax = new Vector2(0f, 1f);
			m_hintRect.pivot = new Vector2(0f, 1f);
			m_hintRect.sizeDelta = new Vector2(120f, 30f);
			m_hintImage = m_hintRoot.GetComponent<Image>();
			m_hintImage.sprite = m_plateSprite;
			m_hintImage.type = Image.Type.Sliced;
			m_hintImage.color = HintColor;
			m_hintImage.raycastTarget = false;
			GameObject gameObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
			gameObject.transform.SetParent(m_hintRoot.transform, false);
			RectTransform rectTransform = (RectTransform)gameObject.transform;
			rectTransform.anchorMin = Vector2.zero;
			rectTransform.anchorMax = Vector2.one;
			rectTransform.offsetMin = new Vector2(12f, 0f);
			rectTransform.offsetMax = new Vector2(-12f, 0f);
			m_hintText = gameObject.GetComponent<Text>();
			m_hintText.alignment = TextAnchor.MiddleLeft;
			m_hintText.fontSize = 18;
			m_hintText.color = Color.white;
			m_hintText.raycastTarget = false;
			Font font = ((m_root.ActionBar.TemplateButton != null) ? FindFont(m_root.ActionBar.TemplateButton.transform) : null);
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

		/// <summary>
		/// 每帧算一次指针落在哪个按钮上。这里用 <see cref="RectTransformUtility.RectangleContainsScreenPoint" />
		/// 而不是 EventSystem 射线，是因为 overlay canvas 下它足够快、也不依赖 UI 是否可交互。
		/// </summary>
		private void UpdateHint()
		{
			if (m_hintRoot == null)
			{
				return;
			}
			RectTransform hovered = null;
			if (m_touchLongPressed)
			{
				// 安卓长按：手指还按着，提示条就停在那个按钮下方。
				hovered = m_touchButton;
			}
			else if (Mouse.current != null && m_actionBar != null && m_actionBar.gameObject.activeInHierarchy)
			{
				hovered = FindHintAt(Mouse.current.position.ReadValue());
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
			m_hintText.text = m_hints[hovered];
			m_hintText.horizontalOverflow = HorizontalWrapMode.Overflow;
			float width = Mathf.Max(96f, m_hintText.preferredWidth + 24f);
			m_hintRect.sizeDelta = new Vector2(width, 30f);
			float centerX = hovered.anchoredPosition.x;
			float x = Mathf.Clamp(centerX - width * 0.5f, 8f, Mathf.Max(8f, m_actionBar.rect.width - width - 8f));
			m_hintRect.anchoredPosition = new Vector2(x, 0f - HintOffsetY);
			m_hintRoot.SetActive(true);
		}
	}
}
