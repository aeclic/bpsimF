using System;
using System.Collections.Generic;
using Bpsim.Modding;
using Bpsim.UI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Bpsim.Mods
{
	/// <summary>
	/// 单个 Mod 的设置面板：DSH 插件设置页那套布局 —— 左侧一列 Mod 列表，
	/// 右侧是这个 Mod 的设置行，底部是「删除 / 关闭」。
	/// </summary>
	/// <remarks>
	/// <para>视觉全部复用 <see cref="UserSettingsPanel"/> 的原生预制体（<c>Panel</c> /
	/// <c>ToggleTemplate</c> / <c>InputFieldTemplate</c> / <c>DropdownTemplate</c>），
	/// 所以字体、行高、开关动画和配色切换与用户设置面板完全一致。</para>
	/// <para>每行的可调项来自 <c>Mod.xml</c> 的 &lt;Settings&gt;，值存进
	/// <see cref="ModSettingsStore"/>；没有声明任何设置的 Mod 也会有「启用 / 目录」
	/// 这类通用项，以及底部的删除按钮。</para>
	/// </remarks>
	public sealed class ModSettingsPanel : MonoBehaviour
	{
		private const float RowHeight = 90f;

		/// <summary>行标题右侧给控件留的宽度（开关 / 下拉 / 输入框都在这一侧）。</summary>
		private const float LabelRightInset = 300f;

		private static ModSettingsPanel s_instance;

		private Canvas m_canvas;

		private RectTransform m_frame;

		private RectTransform m_body;

		private Text m_title;

		private Text m_subtitle;

		private Button m_deleteButton;

		private Button m_resetButton;

		private ModPackage m_package;

		private readonly List<ModPackage> m_packages = new List<ModPackage>();

		private readonly List<RectTransform> m_rows = new List<RectTransform>();

		private float m_lastClickTime;

		private string m_lastClickId;

		/// <summary>面板是否可见。</summary>
		public static bool IsOpen => s_instance != null && s_instance.m_canvas != null
			&& s_instance.m_canvas.gameObject.activeSelf;

		/// <summary>当前正在编辑的 Mod（可能为 null）。</summary>
		public static ModPackage Current => s_instance != null ? s_instance.m_package : null;

		/// <summary>打开某个 Mod 的设置面板。</summary>
		public static void Open(ModPackage pkg)
		{
			if (pkg == null)
			{
				return;
			}
			if (s_instance == null)
			{
				GameObject host = new GameObject("ModSettingsPanel");
				Object.DontDestroyOnLoad(host);
				s_instance = host.AddComponent<ModSettingsPanel>();
			}
			s_instance.Show(pkg);
		}

		/// <summary>按 Mod id 打开（找不到就静默返回）。</summary>
		public static void Open(string modId)
		{
			ModManager manager = ModManager.Ensure();
			ModPackage pkg = manager != null ? manager.Find(modId) : null;
			if (pkg != null)
			{
				Open(pkg);
			}
		}

		/// <summary>关闭面板。</summary>
		public static void Close()
		{
			if (s_instance == null || s_instance.m_canvas == null)
			{
				return;
			}
			s_instance.m_canvas.gameObject.SetActive(false);
		}

		private void Awake()
		{
			UiTheme.Changed += OnThemeChanged;
		}

		private void OnDestroy()
		{
			UiTheme.Changed -= OnThemeChanged;
		}

		private void OnThemeChanged(UiPalette palette)
		{
			// 配色切换后整块重建：模板行是克隆出来的，重新走一遍 ApplyTo 最省事也最一致。
			if (m_canvas == null)
			{
				return;
			}
			ModPackage keep = m_package;
			DestroyCanvas();
			BuildIfNeeded();
			if (keep != null)
			{
				Fill(keep);
			}
			m_canvas.gameObject.SetActive(true);
		}

		private void Show(ModPackage pkg)
		{
			BuildIfNeeded();
			Fill(pkg);
			m_canvas.gameObject.SetActive(true);
			m_canvas.transform.SetAsLastSibling();
		}

		private void DestroyCanvas()
		{
			if (m_canvas != null)
			{
				Object.Destroy(m_canvas.gameObject);
			}
			m_canvas = null;
			m_frame = null;
			m_body = null;
			m_title = null;
			m_subtitle = null;
			m_deleteButton = null;
			m_resetButton = null;
			m_rows.Clear();
		}

		// ───────────────────────── 构建 ─────────────────────────

		private void BuildIfNeeded()
		{
			if (m_canvas != null)
			{
				return;
			}
			m_canvas = ModUiKit.CreateCanvas("ModSettingsCanvas", 31200);
			m_canvas.transform.SetParent(transform, false);

			RectTransform backdrop = ModUiKit.Panel(m_canvas.transform, new Color(0.01f, 0.015f, 0.03f, 0.62f), 2);
			ModUiKit.SetRect(backdrop, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
			Button backdropButton = backdrop.gameObject.AddComponent<Button>();
			backdropButton.transition = Selectable.Transition.None;
			backdropButton.onClick.AddListener(Close);

			m_frame = ModUiKit.NativePanel(m_canvas.transform);
			if (m_frame == null)
			{
				m_frame = ModUiKit.Panel(m_canvas.transform, ModUiKit.PanelBg, 16);
			}
			m_frame.name = "Frame";
			m_frame.anchorMin = m_frame.anchorMax = new Vector2(0.5f, 0.5f);
			m_frame.pivot = new Vector2(0.5f, 0.5f);
			m_frame.sizeDelta = new Vector2(1200f, 820f);
			m_frame.anchoredPosition = Vector2.zero;
			// 挡住背景点击，避免误关。
			m_frame.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;

			// 标题区：复用原生 Header 的背景，但换掉标题文字与右侧按钮。
			Transform nativeHeader = m_frame.Find("Header");
			RectTransform header = nativeHeader as RectTransform;
			if (header == null)
			{
				header = ModUiKit.Panel(m_frame, UiTheme.Current.Header, 16);
				ModUiKit.SetRect(header, new Vector2(0f, 1f), new Vector2(1f, 1f),
					new Vector2(0f, -60f), Vector2.zero);
			}
			m_title = header.Find("Text")?.GetComponent<Text>();
			if (m_title == null)
			{
				m_title = ModUiKit.Label(header, "Mod 设置", 24, ModUiKit.TextMain, TextAnchor.MiddleLeft);
			}
			m_title.text = "Mod 设置";
			m_title.font = ModUiKit.NativeFont;
			m_title.fontSize = 24;
			m_title.fontStyle = FontStyle.Bold;
			// 标题占左侧 470，副标题占右侧剩下的空间，两者不再叠在一起。
			ModUiKit.SetRect(m_title.rectTransform, Vector2.zero, Vector2.one,
				new Vector2(30f, 0f), new Vector2(-700f, 0f));

			m_subtitle = ModUiKit.Label(header, string.Empty, 17, ModUiKit.TextDim, TextAnchor.MiddleRight);
			ModUiKit.SetRect(m_subtitle.rectTransform, Vector2.zero, Vector2.one,
				new Vector2(500f, 0f), new Vector2(-300f, 0f));

			Transform nativeButtons = m_frame.Find("ButtonGroup");
			if (nativeButtons != null)
			{
				// 原生「重置」位置放「恢复默认」，原生「关闭」保留为关闭。
				m_resetButton = nativeButtons.Find("ResetButton")?.GetComponent<Button>();
				Button close = nativeButtons.Find("CloseButton")?.GetComponent<Button>();
				ConfigureHeaderButton(m_resetButton, "恢复默认", ResetSettings);
				ConfigureHeaderButton(close, "关闭", Close);
			}
			else
			{
				m_resetButton = ModUiKit.NativeButton(header, "恢复默认", ResetSettings);
				RectTransform resetRect = m_resetButton.GetComponent<RectTransform>();
				resetRect.anchorMin = resetRect.anchorMax = new Vector2(1f, 0.5f);
				resetRect.pivot = new Vector2(1f, 0.5f);
				resetRect.sizeDelta = new Vector2(150f, 44f);
				resetRect.anchoredPosition = new Vector2(-190f, 0f);

				Button closeButton = ModUiKit.NativeButton(header, "关闭", Close);
				RectTransform closeRect = closeButton.GetComponent<RectTransform>();
				closeRect.anchorMin = closeRect.anchorMax = new Vector2(1f, 0.5f);
				closeRect.pivot = new Vector2(1f, 0.5f);
				closeRect.sizeDelta = new Vector2(120f, 44f);
				closeRect.anchoredPosition = new Vector2(-20f, 0f);
			}

			// 原生滚动区不用，列表由我们自己排。
			Transform nativeScroll = m_frame.Find("Scroll View");
			if (nativeScroll != null)
			{
				nativeScroll.gameObject.SetActive(false);
			}

			// 左侧 Mod 列表。
			RectTransform sidebar = ModUiKit.Panel(m_frame, new Color(0f, 0f, 0f, 0.16f), 12);
			sidebar.name = "Sidebar";
			ModUiKit.SetRect(sidebar, new Vector2(0f, 0f), new Vector2(0f, 1f),
				new Vector2(24f, 92f), new Vector2(324f, -80f));
			sidebar.gameObject.AddComponent<RectMask2D>();
			ScrollRect sideScroll = sidebar.gameObject.AddComponent<ScrollRect>();
			GameObject sideContent = new GameObject("Content", typeof(RectTransform),
				typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
			RectTransform sideContentRect = sideContent.GetComponent<RectTransform>();
			sideContentRect.SetParent(sidebar, false);
			sideContentRect.anchorMin = new Vector2(0f, 1f);
			sideContentRect.anchorMax = new Vector2(1f, 1f);
			sideContentRect.pivot = new Vector2(0.5f, 1f);
			sideContentRect.anchoredPosition = Vector2.zero;
			// 不归零的话 Content 会保持默认 100x100，比 Sidebar 宽 100，被遮罩左右各切 50。
			sideContentRect.sizeDelta = new Vector2(0f, 0f);
			VerticalLayoutGroup sideLayout = sideContent.GetComponent<VerticalLayoutGroup>();
			sideLayout.childForceExpandHeight = false;
			sideLayout.childForceExpandWidth = true;
			sideLayout.childControlHeight = true;
			sideLayout.childControlWidth = true;
			sideLayout.spacing = 6f;
			sideLayout.padding = new RectOffset(8, 8, 8, 8);
			sideContent.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			sideScroll.content = sideContentRect;
			sideScroll.viewport = sidebar;
			sideScroll.horizontal = false;
			sideScroll.vertical = true;
			sideScroll.movementType = ScrollRect.MovementType.Clamped;
			sideScroll.scrollSensitivity = 30f;

			// 右侧设置区。
			RectTransform bodyHost = ModUiKit.Panel(m_frame, new Color(0f, 0f, 0f, 0.10f), 12);
			bodyHost.name = "Body";
			ModUiKit.SetRect(bodyHost, new Vector2(0f, 0f), new Vector2(1f, 1f),
				new Vector2(340f, 92f), new Vector2(-24f, -80f));
			bodyHost.gameObject.AddComponent<RectMask2D>();
			ScrollRect scroll = bodyHost.gameObject.AddComponent<ScrollRect>();
			GameObject content = new GameObject("Content", typeof(RectTransform),
				typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
			m_body = content.GetComponent<RectTransform>();
			m_body.SetParent(bodyHost, false);
			m_body.anchorMin = new Vector2(0f, 1f);
			m_body.anchorMax = new Vector2(1f, 1f);
			m_body.pivot = new Vector2(0.5f, 1f);
			m_body.anchoredPosition = Vector2.zero;
			// 同上：Content 的 sizeDelta 必须是 0，否则整体比 Body 宽 100 被裁掉两边。
			m_body.sizeDelta = new Vector2(0f, 0f);
			VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
			layout.childForceExpandHeight = false;
			layout.childForceExpandWidth = true;
			layout.childControlHeight = true;
			layout.childControlWidth = true;
			layout.spacing = 12f;
			layout.padding = new RectOffset(16, 16, 16, 16);
			content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			scroll.content = m_body;
			scroll.viewport = bodyHost;
			scroll.horizontal = false;
			scroll.vertical = true;
			scroll.movementType = ScrollRect.MovementType.Clamped;
			scroll.scrollSensitivity = 30f;

			// 底部：删除 Mod（危险操作，和设置行分开）。
			RectTransform footerLine = ModUiKit.Panel(m_frame, new Color(1f, 1f, 1f, 0.06f), 2);
			ModUiKit.SetRect(footerLine, new Vector2(0f, 0f), new Vector2(1f, 0f),
				new Vector2(0f, 80f), new Vector2(0f, 81f));

			RectTransform footer = ModUiKit.Panel(m_frame, new Color(0f, 0f, 0f, 0.16f), 12);
			footer.name = "Footer";
			ModUiKit.SetRect(footer, new Vector2(0f, 0f), new Vector2(1f, 0f),
				new Vector2(24f, 20f), new Vector2(-24f, 70f));

			m_deleteButton = ModUiKit.Button(footer, "删除这个 Mod", ModUiKit.Danger, DeleteCurrent, 20);
			RectTransform deleteRect = m_deleteButton.GetComponent<RectTransform>();
			deleteRect.anchorMin = deleteRect.anchorMax = new Vector2(0f, 0.5f);
			deleteRect.pivot = new Vector2(0f, 0.5f);
			deleteRect.sizeDelta = new Vector2(200f, 40f);
			deleteRect.anchoredPosition = new Vector2(12f, 0f);

			Text hint = ModUiKit.Label(footer, "删除不可撤销；内置 Mod 只能在这里禁用。",
				17, ModUiKit.TextDim, TextAnchor.MiddleLeft);
			ModUiKit.SetRect(hint.rectTransform, Vector2.zero, Vector2.one,
				new Vector2(228f, 0f), new Vector2(-12f, 0f));

			m_canvas.gameObject.SetActive(false);
		}

		private static void ConfigureHeaderButton(Button button, string label, Action action)
		{
			if (button == null)
			{
				return;
			}
			button.onClick.RemoveAllListeners();
			if (action != null)
			{
				button.onClick.AddListener(() => action());
			}
			Text text = button.GetComponentInChildren<Text>(true);
			if (text != null)
			{
				text.text = label;
			}
		}

		// ───────────────────────── 填充 ─────────────────────────

		private void Fill(ModPackage pkg)
		{
			m_package = pkg;
			ClearRows();

			m_title.text = "Mod 设置 · " + pkg.DisplayName;
			// 副标题只放短信息：描述通常是一大段，塞进标题栏会把两边都挤爆。
			string subtitle = "v" + pkg.Version;
			if (pkg.Manifest != null && !string.IsNullOrEmpty(pkg.Manifest.author))
			{
				subtitle = "作者 " + pkg.Manifest.author + " · " + subtitle;
			}
			m_subtitle.text = subtitle;

			BuildSidebar(pkg);
			BuildSettingsRows(pkg);
			RefreshFooter(pkg);

			UiThemeApplicator.ApplyTo(m_frame.gameObject);
		}

		private void ClearRows()
		{
			for (int i = 0; i < m_rows.Count; i++)
			{
				if (m_rows[i] != null)
				{
					Object.Destroy(m_rows[i].gameObject);
				}
			}
			m_rows.Clear();
			Transform sidebar = m_frame != null ? m_frame.Find("Sidebar/Content") : null;
			if (sidebar != null)
			{
				for (int i = sidebar.childCount - 1; i >= 0; i--)
				{
					Object.Destroy(sidebar.GetChild(i).gameObject);
				}
			}
		}

		private void BuildSidebar(ModPackage selected)
		{
			Transform content = m_frame != null ? m_frame.Find("Sidebar/Content") : null;
			if (content == null)
			{
				return;
			}
			m_packages.Clear();
			ModManager manager = ModManager.Instance;
			if (manager == null)
			{
				return;
			}
			m_packages.AddRange(manager.Packages);
			foreach (ModPackage pkg in m_packages)
			{
				ModPackage captured = pkg;
				bool active = pkg == selected;
				Button item = MakeSidebarItem(content, pkg.DisplayName, active, () =>
				{
					if (m_package != captured)
					{
						Fill(captured);
					}
				});
				ModUiKit.Size(item.gameObject, 52f);
			}
		}

		private Button MakeSidebarItem(Transform parent, string label, bool active, Action onClick)
		{
			Color background = active ? ModUiKit.Primary : ModUiKit.Neutral;
			Button button = ModUiKit.Button(parent, label, background, onClick, 19);
			foreach (Text text in button.GetComponentsInChildren<Text>(true))
			{
				text.alignment = TextAnchor.MiddleLeft;
				text.color = active ? ModUiKit.TextMain : ModUiKit.TextDim;
				text.fontStyle = active ? FontStyle.Bold : FontStyle.Normal;
				RectTransform rect = text.rectTransform;
				rect.anchorMin = Vector2.zero;
				rect.anchorMax = Vector2.one;
				rect.offsetMin = new Vector2(14f, 0f);
				rect.offsetMax = new Vector2(-10f, 0f);
			}
			return button;
		}

		private void BuildSettingsRows(ModPackage pkg)
		{
			if (m_body == null)
			{
				return;
			}

			// 1) 通用项：启用开关。
			AddToggleRow("启用这个 Mod", pkg.Enabled, value =>
			{
				ModManager manager = ModManager.Instance;
				if (manager != null)
				{
					manager.SetEnabled(pkg.Id, value);
				}
			});

			// 2) Mod 自己声明的设置。
			List<ModSettingSpec> specs = pkg.Manifest != null ? pkg.Manifest.settings : null;
			int declared = 0;
			if (specs != null)
			{
				foreach (ModSettingSpec spec in specs)
				{
					if (spec == null || string.IsNullOrWhiteSpace(spec.key))
					{
						continue;
					}
					AddSettingRow(pkg, spec);
					declared++;
				}
			}

			// 3) 没声明设置的给出说明。
			if (declared == 0)
			{
				AddHintRow("这个 Mod 没有声明可调设置（Mod.xml 的 <Settings>）。", ModUiKit.TextDim);
			}
			else
			{
				AddHintRow("以上设置保存在 Mods/ModSettings.xml，Mod 通过 IModContext.Settings 读取。",
					ModUiKit.TextDim);
			}

			// 4) 只读信息。
			AddInfoRow("来源", SourceText(pkg.Source));
			AddInfoRow("目录", ModPaths.ToDisplay(pkg.Directory));
			if (pkg.Guid != Guid.Empty)
			{
				AddInfoRow("GUID", pkg.Guid.ToString());
			}
			if (!string.IsNullOrEmpty(pkg.Error))
			{
				AddInfoRow("错误", pkg.Error);
			}
		}

		private void AddSettingRow(ModPackage pkg, ModSettingSpec spec)
		{
			if (spec.IsBool)
			{
				bool value = string.Equals(ModSettingsStore.GetText(pkg, spec), "true",
					StringComparison.OrdinalIgnoreCase);
				string modKey = ModSettingsStore.KeyFor(pkg);
				AddToggleRow(spec.DisplayTitle, value, changed =>
				{
					ModSettingsStore.SetText(modKey, spec.key, spec.Sanitize(changed ? "true" : "false"));
					ModSettingsStore.Save();
				}, spec.description);
				return;
			}
			if (spec.IsEnum)
			{
				AddEnumRow(pkg, spec);
				return;
			}
			if (spec.IsInt || spec.IsFloat)
			{
				AddNumberRow(pkg, spec);
				return;
			}
			AddTextRow(pkg, spec);
		}

		/// <summary>开关行：克隆原生 ToggleTemplate，右侧就是设置面板那枚滑动开关。</summary>
		private ToggleSwitch AddToggleRow(string label, bool value, Action<bool> onChanged,
			string description = null)
		{
			RectTransform row = ModUiKit.Row(m_body, "ToggleTemplate", true, RowHeight);
			row.name = "Toggle_" + label;
			ModUiKit.Size(row.gameObject, RowHeight);
			StyleRowLabel(row, label, description);
			m_rows.Add(row);

			RectTransform valueTransform = row.Find("Value") as RectTransform;
			ToggleSwitch toggle = valueTransform != null ? valueTransform.GetComponent<ToggleSwitch>() : null;
			if (toggle == null)
			{
				// 模板不可用时的退化：至少让用户能点。
				Button fallback = ModUiKit.Button(row, value ? "开" : "关", ModUiKit.Neutral,
					() => onChanged(!value), 18);
				RectTransform rect = fallback.GetComponent<RectTransform>();
				rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
				rect.pivot = new Vector2(1f, 0.5f);
				rect.sizeDelta = new Vector2(96f, 44f);
				rect.anchoredPosition = new Vector2(-20f, 0f);
				return null;
			}
			valueTransform.anchoredPosition = new Vector2(-40f, 0f);
			toggle.SetIsOnWithoutNotify(value);
			toggle.ApplyPalette(UiTheme.Current);
			toggle.OnValueChanged.AddListener(changed => onChanged(changed));
			return toggle;
		}

		/// <summary>下拉行。</summary>
		private void AddEnumRow(ModPackage pkg, ModSettingSpec spec)
		{
			RectTransform row = ModUiKit.Row(m_body, "DropdownTemplate", true, RowHeight);
			row.name = "Enum_" + spec.key;
			ModUiKit.Size(row.gameObject, RowHeight);
			StyleRowLabel(row, spec.DisplayTitle, spec.description);
			m_rows.Add(row);

			RectTransform valueTransform = row.Find("Value") as RectTransform;
			Dropdown dropdown = valueTransform != null ? valueTransform.GetComponent<Dropdown>() : null;
			if (dropdown == null)
			{
				return;
			}
			valueTransform.anchoredPosition = new Vector2(-60f, 0f);
			string current = ModSettingsStore.GetText(pkg, spec);
			List<Dropdown.OptionData> options = new List<Dropdown.OptionData>(spec.options.Count);
			int index = 0;
			for (int i = 0; i < spec.options.Count; i++)
			{
				ModSettingOption option = spec.options[i];
				options.Add(new Dropdown.OptionData(option.DisplayLabel));
				if (string.Equals(option.value, current, StringComparison.OrdinalIgnoreCase))
				{
					index = i;
				}
			}
			dropdown.ClearOptions();
			dropdown.AddOptions(options);
			dropdown.value = index;
			dropdown.RefreshShownValue();
			string modKey = ModSettingsStore.KeyFor(pkg);
			dropdown.onValueChanged.AddListener(selected =>
			{
				if (selected >= 0 && selected < spec.options.Count)
				{
					ModSettingsStore.SetText(modKey, spec.key, spec.Sanitize(spec.options[selected].value));
					ModSettingsStore.Save();
				}
			});
		}

		/// <summary>数值行：整数用整数内容，浮点保留小数。</summary>
		private void AddNumberRow(ModPackage pkg, ModSettingSpec spec)
		{
			RectTransform row = ModUiKit.Row(m_body, "InputFieldTemplate", true, RowHeight);
			row.name = "Number_" + spec.key;
			ModUiKit.Size(row.gameObject, RowHeight);
			StyleRowLabel(row, spec.DisplayTitle, spec.description);
			m_rows.Add(row);

			RectTransform valueTransform = row.Find("Value") as RectTransform;
			InputField input = valueTransform != null ? valueTransform.GetComponent<InputField>() : null;
			if (input == null)
			{
				return;
			}
			valueTransform.anchoredPosition = new Vector2(-60f, 0f);
			input.contentType = spec.IsInt ? InputField.ContentType.IntegerNumber
				: InputField.ContentType.DecimalNumber;
			input.text = ModSettingsStore.GetText(pkg, spec);
			string modKey = ModSettingsStore.KeyFor(pkg);
			input.onEndEdit.AddListener(raw =>
			{
				string clean = spec.Sanitize(raw);
				input.SetTextWithoutNotify(clean);
				ModSettingsStore.SetText(modKey, spec.key, clean);
				ModSettingsStore.Save();
			});
		}

		/// <summary>文本行。</summary>
		private void AddTextRow(ModPackage pkg, ModSettingSpec spec)
		{
			RectTransform row = ModUiKit.Row(m_body, "InputFieldTemplate", true, RowHeight);
			row.name = "Text_" + spec.key;
			ModUiKit.Size(row.gameObject, RowHeight);
			StyleRowLabel(row, spec.DisplayTitle, spec.description);
			m_rows.Add(row);

			RectTransform valueTransform = row.Find("Value") as RectTransform;
			InputField input = valueTransform != null ? valueTransform.GetComponent<InputField>() : null;
			if (input == null)
			{
				return;
			}
			valueTransform.anchoredPosition = new Vector2(-60f, 0f);
			input.contentType = InputField.ContentType.Standard;
			input.text = ModSettingsStore.GetText(pkg, spec);
			string modKey = ModSettingsStore.KeyFor(pkg);
			input.onEndEdit.AddListener(raw =>
			{
				string clean = spec.Sanitize(raw);
				input.SetTextWithoutNotify(clean);
				ModSettingsStore.SetText(modKey, spec.key, clean);
				ModSettingsStore.Save();
			});
		}

		/// <summary>说明行（不可点）。</summary>
		private void AddHintRow(string text, Color color)
		{
			RectTransform row = ModUiKit.Row(m_body, "InputFieldTemplate", false, 64f);
			row.name = "Hint";
			ModUiKit.Size(row.gameObject, 64f);
			Text label = row.Find("Name")?.GetComponent<Text>();
			if (label == null)
			{
				label = ModUiKit.Label(row, text, 18, color, TextAnchor.MiddleLeft);
			}
			label.text = text;
			label.font = ModUiKit.NativeFont;
			label.fontSize = 18;
			label.color = color;
			label.fontStyle = FontStyle.Normal;
			ModUiKit.SetRect(label.rectTransform, Vector2.zero, Vector2.one,
				new Vector2(16f, 0f), new Vector2(-16f, 0f));
			m_rows.Add(row);
		}

		/// <summary>只读信息行：右侧是灰字，不可编辑。</summary>
		private void AddInfoRow(string label, string value)
		{
			RectTransform row = ModUiKit.Row(m_body, "InputFieldTemplate", true, 64f);
			row.name = "Info_" + label;
			ModUiKit.Size(row.gameObject, 64f);
			Text name = row.Find("Name")?.GetComponent<Text>();
			if (name != null)
			{
				name.text = label;
				name.font = ModUiKit.NativeFont;
				name.fontSize = 18;
				name.color = ModUiKit.TextDim;
				ModUiKit.SetRect(name.rectTransform, Vector2.zero, Vector2.one,
					new Vector2(16f, 0f), new Vector2(-520f, 0f));
			}
			Text detail = ModUiKit.Label(row, value, 17, ModUiKit.TextDim, TextAnchor.MiddleRight);
			ModUiKit.SetRect(detail.rectTransform, Vector2.zero, Vector2.one,
				new Vector2(300f, 0f), new Vector2(-16f, 0f));
			// 路径一长就会从右对齐的位置往左溢出，盖住左边那两个字。
			detail.horizontalOverflow = HorizontalWrapMode.Wrap;
			detail.verticalOverflow = VerticalWrapMode.Truncate;
			RectTransform valueTransform = row.Find("Value") as RectTransform;
			if (valueTransform != null)
			{
				valueTransform.gameObject.SetActive(false);
			}
			m_rows.Add(row);
		}

		private void StyleRowLabel(RectTransform row, string label, string description)
		{
			Text name = row.Find("Name")?.GetComponent<Text>();
			if (name == null)
			{
				return;
			}
			name.text = label;
			name.font = ModUiKit.NativeFont;
			name.fontSize = 22;
			name.color = ModUiKit.TextMain;
			name.fontStyle = FontStyle.Normal;
			// 模板的 Name 是 Truncate，行高稍紧就会把整行文字裁空。
			name.horizontalOverflow = HorizontalWrapMode.Wrap;
			name.verticalOverflow = VerticalWrapMode.Overflow;
			ModUiKit.SetRect(name.rectTransform, Vector2.zero, Vector2.one,
				new Vector2(24f, 0f), new Vector2(-LabelRightInset, 0f));
			if (string.IsNullOrEmpty(description))
			{
				return;
			}
			Text detail = ModUiKit.Label(row, description, 17, ModUiKit.TextDim, TextAnchor.MiddleLeft);
			ModUiKit.SetRect(detail.rectTransform, Vector2.zero, Vector2.one,
				new Vector2(24f, 6f), new Vector2(-LabelRightInset, -48f));
		}

		private void RefreshFooter(ModPackage pkg)
		{
			if (m_deleteButton == null)
			{
				return;
			}
			bool deletable = pkg.Source == ModSource.Installed || pkg.Source == ModSource.Additional;
			m_deleteButton.gameObject.SetActive(deletable || pkg.Source == ModSource.Workshop);
			Text label = m_deleteButton.GetComponentInChildren<Text>(true);
			if (label != null)
			{
				label.text = pkg.Source == ModSource.Additional ? "从列表移除" : "删除这个 Mod";
			}
			Image image = m_deleteButton.GetComponent<Image>();
			if (image != null)
			{
				image.color = deletable ? ModUiKit.Danger : ModUiKit.Neutral;
			}
		}

		// ───────────────────────── 动作 ─────────────────────────

		private void ResetSettings()
		{
			if (m_package == null)
			{
				return;
			}
			List<ModSettingSpec> specs = m_package.Manifest != null ? m_package.Manifest.settings : null;
			if (specs != null)
			{
				string key = ModSettingsStore.KeyFor(m_package);
				foreach (ModSettingSpec spec in specs)
				{
					if (spec != null && !string.IsNullOrWhiteSpace(spec.key))
					{
						ModSettingsStore.SetText(key, spec.key, spec.Sanitize(spec.value));
					}
				}
				ModSettingsStore.Save();
			}
			ModManager manager = ModManager.Instance;
			if (manager != null)
			{
				manager.Toast("已恢复「" + m_package.DisplayName + "」的默认设置");
			}
			Fill(m_package);
		}

		private void DeleteCurrent()
		{
			if (m_package == null)
			{
				return;
			}
			ModPackage pkg = m_package;
			// 第一次点只是变成红字提示，第二次才真的删 —— 删除不可撤销。
			if (!string.Equals(m_lastClickId, pkg.Id, StringComparison.OrdinalIgnoreCase)
				|| Time.unscaledTime - m_lastClickTime > 4f)
			{
				m_lastClickId = pkg.Id;
				m_lastClickTime = Time.unscaledTime;
				Text label = m_deleteButton.GetComponentInChildren<Text>(true);
				if (label != null)
				{
					label.text = pkg.Source == ModSource.Additional ? "确认移除？" : "确认删除？";
				}
				return;
			}
			m_lastClickId = null;
			ModManager manager = ModManager.Instance;
			if (manager == null)
			{
				return;
			}
			bool ok = manager.Uninstall(pkg.Id);
			manager.Toast(ok
				? "已删除「" + pkg.DisplayName + "」"
				: "删除失败（内置或工坊 Mod 不能直接删）");
			ModSettingsStore.Clear(ModSettingsStore.KeyFor(pkg));
			ModPackage next = manager.Packages.Count > 0 ? manager.Packages[0] : null;
			if (next != null && next != pkg)
			{
				Fill(next);
			}
			else
			{
				Close();
			}
		}

		private string SourceText(ModSource source)
		{
			switch (source)
			{
			case ModSource.BuiltIn:
				return "内置（随游戏分发）";
			case ModSource.Workshop:
				return "创意工坊";
			case ModSource.Additional:
				return "外部目录";
			default:
				return "已安装";
			}
		}
	}
}
