using System;
using System.Collections.Generic;
using System.IO;
using Bpsim.Modding;
using Bpsim.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.Mods
{
	/// <summary>
	/// 游戏内 Mod 管理器界面（F10 或悬浮按钮打开）。两个页签：
	/// <c>已安装</c>（启用/禁用、打开面板、删除、导入 Mod）与 <c>创意工坊</c>（浏览索引并安装）。
	/// 使用 UserSettingsPanel 的原生面板、标题按钮组、设置行底板和滑动开关，
	/// Mod 元信息与操作按钮叠加在同一行中；
	/// 底部工具条把 zip / 文件夹导入合并成一个「导入 Mod」按钮（弹出小菜单）。
	/// </summary>
	public sealed class ModManagerUI : MonoBehaviour
	{
		private static ModManagerUI s_instance;

		// ── 面板配色：全部从全局界面配色（Bpsim.UI.UiTheme）推导，切换配色后重建即生效 ──
		private static Bpsim.UI.UiPalette Palette => Bpsim.UI.UiTheme.Current;
		private static Color FrameBg => new Color(Palette.Panel.r, Palette.Panel.g, Palette.Panel.b, 0.985f);
		private static Color HeaderBg => Palette.Header;
		private static Color Accent => Palette.Accent;
		private static Color AccentDim => new Color(Palette.Accent.r, Palette.Accent.g, Palette.Accent.b, 0.28f);
		private static Color CardBgAlt => Palette.Shade(1.45f);
		private static Color TabIdle => Palette.Shade(1.4f);
		private static Color BtnNeutral => Palette.BarButton;
		private static Color BtnGreen => Palette.Mix(Palette.Second, 0.5f);
		private static Color BtnRed => Palette.DangerStrong;
		private static Color TextMain => Palette.Text;
		private static Color TextDim => Palette.TextMuted;
		private static Color StateOk => Color.Lerp(Palette.Second, Color.white, 0.25f);
		private static Color StateBad => new Color(0.95f, 0.45f, 0.45f, 1f);

		private Canvas m_canvas;
		private RectTransform m_body;
		private Text m_status;
		private int m_tab; // 0=已安装 1=创意工坊

		private Button m_tabInstalled;
		private Button m_tabWorkshop;

		private Button m_installClipboard;
		private Button m_import;
		private RectTransform m_importMenu;
		private bool m_installing;

		public static bool IsOpen => s_instance != null && s_instance.m_canvas != null;

		public static void Open()
		{
			if (s_instance == null)
			{
				GameObject go = new GameObject("ModManagerUI");
				UnityEngine.Object.DontDestroyOnLoad(go);
				s_instance = go.AddComponent<ModManagerUI>();
			}
			s_instance.BuildIfNeeded();
			// A built-in catalog can be copied by the deployment step after the
			// manager has already started. Refresh the disk-backed catalog whenever
			// the list is opened so the entry is never hidden by a stale scan.
			ModManager.Ensure().Rescan();
			s_instance.m_canvas.gameObject.SetActive(true);
			s_instance.Rebuild();
		}

		public static void Toggle()
		{
			if (IsOpen && s_instance.m_canvas.gameObject.activeSelf)
			{
				s_instance.m_canvas.gameObject.SetActive(false);
			}
			else
			{
				Open();
			}
		}

		public static void Close()
		{
			if (s_instance != null && s_instance.m_canvas != null)
			{
				s_instance.m_canvas.gameObject.SetActive(false);
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
			EnsureFloatingButton();
			ModManager.Ensure();
			ModManager.Instance.Changed += OnModelChanged;
			Bpsim.UI.UiTheme.Changed += OnThemeChanged;
		}

		private void OnDestroy()
		{
			if (s_instance == this)
			{
				s_instance = null;
			}
			if (ModManager.Instance != null)
			{
				ModManager.Instance.Changed -= OnModelChanged;
			}
			Bpsim.UI.UiTheme.Changed -= OnThemeChanged;
		}

		private void OnThemeChanged(Bpsim.UI.UiPalette palette)
		{
			// 框架是一次性搭建的：配色切换时销毁重建，列表/悬浮按钮全部按新配色重刷。
			if (m_canvas != null)
			{
				bool wasVisible = m_canvas.gameObject.activeSelf;
				Destroy(m_canvas.gameObject);
				m_canvas = null;
				m_body = null;
				m_importMenu = null;
				if (wasVisible)
				{
					BuildIfNeeded();
					m_canvas.gameObject.SetActive(true);
					Rebuild();
				}
			}
			foreach (Transform child in transform)
			{
				if (child.name == "ModFloatingButton")
				{
					Destroy(child.gameObject);
				}
			}
			EnsureFloatingButton();
		}

		private void OnModelChanged()
		{
			if (IsOpen && m_canvas.gameObject.activeSelf)
			{
				Rebuild();
			}
		}

		private void EnsureFloatingButton()
		{
			foreach (Transform child in transform)
			{
				if (child.name == "ModFloatingButton")
				{
					return;
				}
			}
			Canvas canvas = ModUiKit.CreateCanvas("ModFloatingButton", 30500);
			canvas.transform.SetParent(transform, false);
			RectTransform rt = ModUiKit.Panel(canvas.transform, Accent, 22);
			rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
			rt.pivot = new Vector2(0f, 0f);
			rt.anchoredPosition = new Vector2(16f, 16f);
			rt.sizeDelta = new Vector2(110f, 44f);
			Button btn = rt.gameObject.AddComponent<Button>();
			btn.targetGraphic = rt.GetComponent<Image>();
			btn.onClick.AddListener(Open);
			Text label = ModUiKit.Label(rt, "MOD", 22, TextMain, TextAnchor.MiddleCenter);
			label.fontStyle = FontStyle.Bold;
			ModUiKit.SetRect(label.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
		}

		// ───────────────────────── 界面搭建 ─────────────────────────

		private void BuildIfNeeded()
		{
			if (m_canvas != null)
			{
				return;
			}
			m_canvas = ModUiKit.CreateCanvas("ModManagerCanvas", 31000);
			m_canvas.transform.SetParent(transform, false);

			// 背景遮罩：点击空白处关闭
			RectTransform backdrop = ModUiKit.Panel(m_canvas.transform, new Color(0.01f, 0.015f, 0.03f, 0.62f), 2);
			ModUiKit.SetRect(backdrop, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
			Button bdBtn = backdrop.gameObject.AddComponent<Button>();
			bdBtn.transition = Selectable.Transition.None;
			bdBtn.onClick.AddListener(Close);

			// 主框：复用设置面板的原生面板、标题栏和字体尺寸。
			RectTransform frame = ModUiKit.NativePanel(m_canvas.transform);
			if (frame == null)
			{
				frame = ModUiKit.Panel(m_canvas.transform, FrameBg, 16);
			}
			frame.name = "Frame";
			frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f);
			frame.pivot = new Vector2(0.5f, 0.5f);
			frame.sizeDelta = new Vector2(1600f, 900f);
			frame.anchoredPosition = Vector2.zero;
			// 面板自身挡住点击，避免穿透遮罩误关
			frame.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;

			Transform nativeHeader = frame.Find("Header");
			RectTransform header = nativeHeader as RectTransform;
			Text title = nativeHeader != null ? nativeHeader.Find("Text")?.GetComponent<Text>() : null;
			if (header == null)
			{
				header = ModUiKit.Panel(frame, HeaderBg, 16);
				ModUiKit.SetRect(header, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -60f), Vector2.zero);
			}
			if (title == null)
			{
				title = ModUiKit.Label(header, "MOD 管理器", 24, TextMain, TextAnchor.MiddleLeft);
				ModUiKit.SetRect(title.rectTransform, Vector2.zero, Vector2.one, new Vector2(30f, 0f), new Vector2(-90f, 0f));
			}
			title.text = "MOD 管理器";
			title.font = ModUiKit.NativeFont;
			title.fontSize = 24;
			// 复用设置面板标题区的原生按钮组：把「重置」位置用于刷新，
			// 「关闭」保留原标签与按钮外观。
			Transform nativeButtons = frame.Find("ButtonGroup");
			if (nativeButtons != null)
			{
				ConfigureHeaderButton(nativeButtons.Find("ResetButton")?.GetComponent<Button>(), "刷新", RefreshMods);
				ConfigureHeaderButton(nativeButtons.Find("CloseButton")?.GetComponent<Button>(), "关闭", Close);
			}
			else
			{
				Button headerClose = MakeButton(header, "关闭", BtnNeutral, Close, 20);
				RectTransform hcRt = headerClose.GetComponent<RectTransform>();
				hcRt.anchorMin = hcRt.anchorMax = new Vector2(1f, 0.5f);
				hcRt.pivot = new Vector2(1f, 0.5f);
				hcRt.sizeDelta = new Vector2(120f, 52f);
				hcRt.anchoredPosition = new Vector2(-20f, 0f);
			}

			// NativePanel 自带的滚动区会由 Mod 列表占用；保留标题区按钮组。
			Transform nativeScroll = frame.Find("Scroll View");
			if (nativeScroll != null)
			{
				nativeScroll.gameObject.SetActive(false);
			}

			// 页签（胶囊形）
			m_tabInstalled = MakeButton(frame, "已安装", Accent, () => SetTab(0), 22);
			Place(m_tabInstalled.GetComponent<RectTransform>(), new Vector2(30f, -78f), new Vector2(180f, 50f));
			m_tabWorkshop = MakeButton(frame, "创意工坊", TabIdle, () => SetTab(1), 22);
			Place(m_tabWorkshop.GetComponent<RectTransform>(), new Vector2(220f, -78f), new Vector2(180f, 50f));

			// 状态栏（右对齐，弱化）
			m_status = ModUiKit.Label(frame, "", 20, TextDim, TextAnchor.MiddleRight);
			ModUiKit.SetRect(m_status.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(430f, -128f), new Vector2(-30f, -78f));

			// 底部工具条
			RectTransform footerLine = ModUiKit.Panel(frame, new Color(1f, 1f, 1f, 0.06f), 2);
			ModUiKit.SetRect(footerLine, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 96f), new Vector2(0f, 97f));
			GameObject footerObject = new GameObject("Actions", typeof(RectTransform));
			RectTransform footer = footerObject.GetComponent<RectTransform>();
			footer.SetParent(frame, false);
			ModUiKit.SetRect(footer, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(30f, 22f), new Vector2(-30f, 78f));

			Button openFolder = MakeButton(footer, "打开 Mods 目录", BtnNeutral, OpenModsFolder, 20);
			Place(footer, openFolder, 0f, 190f);

			m_installClipboard = MakeButton(footer, "从剪贴板安装", BtnNeutral, InstallFromClipboard, 20);
			Place(footer, m_installClipboard, 204f, 190f);

			m_import = MakeButton(footer, "导入 Mod ▾", Accent, ToggleImportMenu, 20);
			Place(footer, m_import, 408f, 190f);

			// 列表容器
			RectTransform bodyHost = ModUiKit.Panel(frame, new Color(0f, 0f, 0f, 0.18f), 12);
			ModUiKit.SetRect(bodyHost, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(30f, 100f), new Vector2(-30f, -128f));
			bodyHost.gameObject.AddComponent<RectMask2D>();
			ScrollRect scroll = bodyHost.gameObject.AddComponent<ScrollRect>();
			GameObject contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
			m_body = contentGo.GetComponent<RectTransform>();
			m_body.SetParent(bodyHost, false);
			m_body.anchorMin = new Vector2(0f, 1f);
			m_body.anchorMax = new Vector2(1f, 1f);
			m_body.pivot = new Vector2(0.5f, 1f);
			m_body.anchoredPosition = Vector2.zero;
			m_body.sizeDelta = new Vector2(0f, 0f);
			VerticalLayoutGroup vlg = contentGo.GetComponent<VerticalLayoutGroup>();
			vlg.childForceExpandHeight = false;
			vlg.childForceExpandWidth = true;
			vlg.childControlHeight = true;
			vlg.childControlWidth = true;
			vlg.spacing = 12f;
			vlg.padding = new RectOffset(14, 14, 14, 14);
			contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			scroll.content = m_body;
			scroll.viewport = bodyHost;
			scroll.horizontal = false;
			scroll.vertical = true;
			scroll.movementType = ScrollRect.MovementType.Clamped;
			scroll.scrollSensitivity = 40f;

			// 导入菜单（最后创建保证盖在列表上方）
			BuildImportMenu(frame);
		}

		private static void ConfigureHeaderButton(Button button, string label, Action action)
		{
			if (button == null) return;
			button.onClick.RemoveAllListeners();
			if (action != null) button.onClick.AddListener(() => action());
			Image image = button.GetComponent<Image>();
			if (image != null) image.color = label == "刷新" ? Accent : Color.white;
			Text text = button.GetComponentInChildren<Text>(true);
			if (text != null) text.text = label;
		}

		private void RefreshMods()
		{
			ModManager.Instance.Rescan();
			ModManager.Instance.Initialize();
			Rebuild();
		}

		private void BuildImportMenu(RectTransform frame)
		{
			// 全屏点击拦截：点菜单外任意处收起
			RectTransform blocker = ModUiKit.Panel(frame, new Color(0f, 0f, 0f, 0.004f), 2);
			blocker.name = "ImportMenuBlocker";
			ModUiKit.SetRect(blocker, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
			Button blockerBtn = blocker.gameObject.AddComponent<Button>();
			blockerBtn.transition = Selectable.Transition.None;
			blockerBtn.onClick.AddListener(() => SetImportMenu(false));

			m_importMenu = ModUiKit.Panel(blocker, CardBgAlt, 12);
			m_importMenu.name = "ImportMenu";
			m_importMenu.anchorMin = m_importMenu.anchorMax = new Vector2(0f, 0f);
			m_importMenu.pivot = new Vector2(0f, 0f);
			m_importMenu.anchoredPosition = new Vector2(438f, 108f); // 对齐「导入 Mod」按钮上方
			m_importMenu.sizeDelta = new Vector2(280f, 128f);
			Button menuSelf = m_importMenu.gameObject.AddComponent<Button>();
			menuSelf.transition = Selectable.Transition.None; // 吃掉菜单空白处的点击

			Button zip = MakeButton(m_importMenu, "选择 zip 文件…", BtnNeutral, () =>
			{
				SetImportMenu(false);
				InstallFromFile();
			}, 20);
			ModUiKit.SetRect(zip.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(10f, -62f), new Vector2(-10f, -10f));
			Button folder = MakeButton(m_importMenu, "选择文件夹…", BtnNeutral, () =>
			{
				SetImportMenu(false);
				InstallFromFolder();
			}, 20);
			ModUiKit.SetRect(folder.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(10f, 10f), new Vector2(-10f, 62f));

			m_importMenu.gameObject.SetActive(false);
		}

		private void ToggleImportMenu()
		{
			SetImportMenu(m_importMenu != null && !m_importMenu.parent.gameObject.activeSelf);
		}

		private void SetImportMenu(bool open)
		{
			if (m_importMenu == null) return;
			Transform blocker = m_importMenu.parent;
			if (blocker != null) blocker.gameObject.SetActive(open);
		}

		private void SetTab(int tab)
		{
			m_tab = tab;
			SetImportMenu(false);
			Rebuild();
		}

		private void Rebuild()
		{
			if (m_body == null)
			{
				return;
			}
			SetImportMenu(false);
			// 高亮当前页签
			StyleTab(m_tabInstalled, m_tab == 0);
			StyleTab(m_tabWorkshop, m_tab == 1);

			foreach (Transform child in m_body)
			{
				Destroy(child.gameObject);
			}
			if (m_tab == 0)
			{
				BuildInstalledList();
			}
			else
			{
				BuildWorkshopList();
			}
		}

		private static void StyleTab(Button b, bool active)
		{
			if (b == null) return;
			Image img = b.GetComponent<Image>();
			if (img != null) img.color = active ? Accent : TabIdle;
			Text label = b.GetComponentInChildren<Text>(true);
			if (label != null)
			{
				label.color = active ? TextMain : TextDim;
				label.fontStyle = active ? FontStyle.Bold : FontStyle.Normal;
			}
		}

		// ───────────────────────── 已安装 ─────────────────────────

		private void BuildInstalledList()
		{
			ModManager mgr = ModManager.Instance;
			RectTransform content = m_body;

			if (mgr.Packages.Count == 0)
			{
				Text empty = ModUiKit.Label(content, "还没有安装任何 Mod。\n点击「导入 Mod」选择 zip 或文件夹，或把 Mod 文件夹（含 manifest.json）放到 Mods 目录后点「刷新」。\nMods 目录：" + ModPaths.ToDisplay(ModPaths.Root), 22, TextDim, TextAnchor.UpperLeft);
				empty.horizontalOverflow = HorizontalWrapMode.Wrap;
				ModUiKit.Size(empty.gameObject, 110f);
				SetStatus("发现 0 个 Mod");
				return;
			}

			ModSummaryComponent sum = ModEcsBridge.LatestSummary;
			SetStatus($"共 {mgr.Packages.Count} 个 · 已加载 {(sum.Loaded == 0 ? CountLoaded() : sum.Loaded)} · 启用 {CountEnabled()}" + (sum.Errors > 0 ? " · 错误 " + sum.Errors : ""));

			foreach (ModPackage pkg in mgr.Packages)
			{
				BuildModRow(content, pkg);
			}
		}

		private int CountLoaded()
		{
			int n = 0;
			foreach (ModPackage p in ModManager.Instance.Packages) if (p.IsLoaded) n++;
			return n;
		}

		private int CountEnabled()
		{
			int n = 0;
			foreach (ModPackage p in ModManager.Instance.Packages) if (p.Enabled) n++;
			return n;
		}

		private void BuildModRow(RectTransform content, ModPackage pkg)
		{
			// 行底与主标题直接复用设置面板的原生 ToggleTemplate。
			RectTransform card = ModUiKit.Row(content);
			card.name = "Row_" + pkg.Id;
			ModUiKit.Size(card.gameObject, 90f);

			Color stateColor = pkg.State == ModState.Loaded ? StateOk
				: pkg.State == ModState.Error || pkg.State == ModState.Incompatible ? StateBad : TextDim;

			Text name = card.Find("Name")?.GetComponent<Text>();
			if (name == null)
			{
				// 克隆出来的模板节点可能没有 Name/Text（预制体被改过），
				// 这种情况自己建一条标题，别让整行只剩副标题。
				name = ModUiKit.Label(card, pkg.DisplayName, 24, TextMain, TextAnchor.UpperLeft);
			}
			name.gameObject.SetActive(true);
			name.text = pkg.DisplayName;
			name.font = ModUiKit.NativeFont;
			name.fontSize = 24;
			name.color = pkg.Enabled ? TextMain : TextDim;
			name.fontStyle = FontStyle.Bold;
			// 模板的 Name 是 Truncate：给的高度只要比一行矮一点点，整行文本就会被裁成空。
			name.horizontalOverflow = HorizontalWrapMode.Wrap;
			name.verticalOverflow = VerticalWrapMode.Overflow;
			ModUiKit.SetRect(name.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
				new Vector2(15f, -40f), new Vector2(-360f, -4f));

			string sub = (string.IsNullOrEmpty(pkg.Manifest?.author) ? "" : "作者 " + pkg.Manifest.author + " · ")
				+ SourceText(pkg.Source)
				+ StateText(pkg.State);
			if (!string.IsNullOrEmpty(pkg.Error))
			{
				sub += " · " + pkg.Error;
			}
			sub = "v" + pkg.Version + " · " + sub;
			Text subText = ModUiKit.Label(card, sub, 18, stateColor, TextAnchor.UpperLeft);
			ModUiKit.SetRect(subText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
				new Vector2(15f, -78f), new Vector2(-360f, -42f));

			PlaceModActions(card, pkg);
		}

		/// <summary>
		/// 每行只放三个控件（从左到右）：面板 / 设置 / 开关。
		/// 删除与其它杂项都收进「设置」面板，行本身不再随面板数量变宽。
		/// </summary>
		private void PlaceModActions(RectTransform card, ModPackage pkg)
		{
			List<ModAction> actions = CollectModActions(pkg);
			float x = -16f;

			// 开关：与用户设置面板同一枚滑动开关。
			RectTransform toggleRect = CreateEnabledToggle(card, pkg, 110f, 44f);
			if (toggleRect != null)
			{
				PlaceRight(toggleRect, ref x, 110f);
			}

			// 设置按钮：复用建造界面的用户设置齿轮图标。
			Button settings = ModUiKit.IconButton(card, ModUiKit.IconSettings, BtnNeutral,
				() => ModSettingsPanel.Open(pkg), 44f, 26f);
			PlaceRight(settings.GetComponent<RectTransform>(), ref x, 44f);

			// 面板按钮：没有面板/快捷按钮时置灰，仍画出按钮保持三列对齐。
			bool hasPanel = actions.Count > 0;
			Button panelButton = MakeButton(card, "面板", hasPanel ? AccentDim : BtnNeutral, () =>
			{
				if (actions.Count == 1)
				{
					actions[0].Invoke();
				}
				else if (actions.Count > 1)
				{
					ShowActionMenu(card, actions);
				}
			}, 18);
			panelButton.interactable = hasPanel;
			PlaceRight(panelButton.GetComponent<RectTransform>(), ref x, 110f);
		}

		/// <summary>一行里可被「面板」按钮打开的动作：声明的网页面板 + 插件注册的快捷按钮。</summary>
		private static List<ModAction> CollectModActions(ModPackage pkg)
		{
			List<ModAction> actions = new List<ModAction>();
			if (pkg.Manifest != null && pkg.Manifest.HasPanels)
			{
				foreach (ModPanelSpec spec in pkg.Manifest.panels)
				{
					if (spec == null) continue;
					ModPanelSpec captured = spec;
					string title = string.IsNullOrEmpty(spec.title) ? spec.id : spec.title;
					actions.Add(new ModAction(title, () => ModPanelService.Open(pkg, captured)));
				}
			}
			List<ModManager.QuickButton> quick = ModManager.Instance != null ? ModManager.Instance.GetQuickButtons(pkg.Id) : null;
			if (quick != null)
			{
				foreach (ModManager.QuickButton qb in quick)
				{
					if (qb == null) continue;
					ModManager.QuickButton capturedQb = qb;
					actions.Add(new ModAction(capturedQb.Label, () => capturedQb.OnClick?.Invoke()));
				}
			}
			return actions;
		}

		/// <summary>一个 Mod 行上的可点击动作（用于「面板」按钮的多选菜单）。</summary>
		private sealed class ModAction
		{
			public readonly string Label;

			public readonly Action OnClick;

			public ModAction(string label, Action onClick)
			{
				Label = label;
				OnClick = onClick;
			}

			public void Invoke()
			{
				OnClick?.Invoke();
			}
		}

		/// <summary>「面板」按钮的下拉菜单：多面板/多快捷按钮时让玩家挑一个。</summary>
		private void ShowActionMenu(RectTransform anchor, List<ModAction> actions)
		{
			if (m_canvas == null) return;
			SetImportMenu(false);

			RectTransform blocker = ModUiKit.Panel(m_canvas.transform, new Color(0f, 0f, 0f, 0.004f), 2);
			blocker.name = "ActionMenuBlocker";
			ModUiKit.SetRect(blocker, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
			Button blockerButton = blocker.gameObject.AddComponent<Button>();
			blockerButton.transition = Selectable.Transition.None;
			blockerButton.onClick.AddListener(() => Destroy(blocker.gameObject));

			float height = actions.Count * 52f + 16f;
			RectTransform menu = ModUiKit.Panel(blocker, CardBgAlt, 12);
			menu.name = "ActionMenu";
			menu.anchorMin = menu.anchorMax = new Vector2(1f, 0.5f);
			menu.pivot = new Vector2(1f, 1f);
			menu.sizeDelta = new Vector2(240f, height);

			// 贴着按钮右上方展开：把按钮的世界坐标换算到遮罩层里。
			Vector3 world = anchor.TransformPoint(new Vector3(anchor.rect.width * 0.5f, anchor.rect.height * 0.5f, 0f));
			RectTransformUtility.ScreenPointToLocalPointInRectangle(
				blocker, RectTransformUtility.WorldToScreenPoint(null, world), null, out Vector2 local);
			menu.anchoredPosition = local;

			float y = -8f;
			foreach (ModAction action in actions)
			{
				ModAction captured = action;
				Button item = ModUiKit.Button(menu, captured.Label, BtnNeutral, () =>
				{
					Destroy(blocker.gameObject);
					captured.Invoke();
				}, 18);
				RectTransform rect = item.GetComponent<RectTransform>();
				rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
				rect.pivot = new Vector2(0.5f, 1f);
				rect.sizeDelta = new Vector2(224f, 44f);
				rect.anchoredPosition = new Vector2(0f, y);
				y -= 52f;
			}
		}

		/// <summary>
		/// 复用设置面板的 Value/ToggleSwitch 模板，保证 Mod 行使用同一套滑动开关视觉。
		/// 模板不可用时返回 null，让调用方保留其它操作按钮而不阻塞面板构建。
		/// </summary>
		private static RectTransform CreateEnabledToggle(RectTransform parent, ModPackage pkg,
			float fallbackWidth = 110f, float fallbackHeight = 44f)
		{
			UserInterface ui = UserInterface.Instance;
			Transform source = ui != null && ui.ActionBar != null
				? ModUiKit.Template("ToggleTemplate/Value")
				: null;
			if (source == null)
			{
				return FallbackToggle(parent, pkg, fallbackWidth, fallbackHeight);
			}

			// Keep the template's authored 0.45 scale and 200x100 coordinate space;
			// the holder is the action slot sized by PlaceRight.
			GameObject holder = new GameObject("EnabledToggle", typeof(RectTransform));
			holder.transform.SetParent(parent, false);
			RectTransform rect = holder.transform as RectTransform;
			GameObject clone = UnityEngine.Object.Instantiate(source.gameObject, holder.transform, false);
			clone.name = "ToggleSwitch";
			RectTransform toggleRect = clone.transform as RectTransform;
			ToggleSwitch toggle = clone.GetComponent<ToggleSwitch>();
			if (rect == null || toggleRect == null || toggle == null)
			{
				UnityEngine.Object.Destroy(holder);
				return FallbackToggle(parent, pkg, fallbackWidth, fallbackHeight);
			}
			toggleRect.anchorMin = toggleRect.anchorMax = new Vector2(0.5f, 0.5f);
			toggleRect.pivot = new Vector2(0.5f, 0.5f);
			toggleRect.anchoredPosition = Vector2.zero;

			toggle.SetIsOnWithoutNotify(pkg.Enabled);
			toggle.ApplyPalette(Bpsim.UI.UiTheme.Current);
			string id = pkg.Id;
			toggle.OnValueChanged.AddListener(value =>
			{
				ModPackage current = ModManager.Instance.Find(id);
				if (current != null && current.Enabled != value)
				{
					ModManager.Instance.SetEnabled(id, value);
				}
			});
			return rect;
		}

		/// <summary>模板缺失时的兜底开关：一枚普通的开/关按钮，至少不让功能消失。</summary>
		private static RectTransform FallbackToggle(RectTransform parent, ModPackage pkg, float width, float height)
		{
			GameObject holder = new GameObject("EnabledToggle", typeof(RectTransform));
			holder.transform.SetParent(parent, false);
			RectTransform rect = holder.transform as RectTransform;
			Button button = ModUiKit.Button(holder.transform, pkg.Enabled ? "开" : "关",
				pkg.Enabled ? BtnGreen : BtnNeutral, () =>
				{
					ModPackage current = ModManager.Instance.Find(pkg.Id);
					if (current != null) ModManager.Instance.SetEnabled(pkg.Id, !current.Enabled);
				}, 18);
			RectTransform buttonRect = button.GetComponent<RectTransform>();
			ModUiKit.SetRect(buttonRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
			if (rect != null)
			{
				rect.sizeDelta = new Vector2(width, Mathf.Max(height, 44f));
			}
			return rect;
		}

		private static void PlaceRight(RectTransform rt, ref float x, float width)
		{
			rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
			rt.pivot = new Vector2(1f, 0.5f);
			rt.sizeDelta = new Vector2(width, 44f);
			rt.anchoredPosition = new Vector2(x, 0f);
			x -= (width + 10f);
		}

		private static string StateText(ModState s)
		{
			switch (s)
			{
				case ModState.Loaded: return "已加载";
				case ModState.Error: return "加载失败";
				case ModState.Incompatible: return "版本不兼容";
				default: return "未加载";
			}
		}

		private static string SourceText(ModSource source)
		{
			switch (source)
			{
				case ModSource.BuiltIn: return "内置 · ";
				case ModSource.Workshop: return "工坊 · ";
				case ModSource.Additional: return "外部目录 · ";
				default: return "";
			}
		}

		// ───────────────────────── 创意工坊 ─────────────────────────

		private void BuildWorkshopList()
		{
			BuildWorkshopContent(m_body);
		}

		private void BuildWorkshopContent(RectTransform content)
		{
			if (WorkshopService.Loading)
			{
				Text t = ModUiKit.Label(content, "正在获取工坊索引…", 22, TextDim, TextAnchor.UpperLeft);
				ModUiKit.Size(t.gameObject, 40f);
				return;
			}
			if (WorkshopService.Index == null || WorkshopService.Index.packs == null || WorkshopService.Index.packs.Count == 0)
			{
				Text t = ModUiKit.Label(content, "工坊索引为空或未配置。\n"
					+ (string.IsNullOrEmpty(WorkshopService.LastError) ? "索引地址：" + WorkshopService.IndexUrl : "错误：" + WorkshopService.LastError),
					22, TextDim, TextAnchor.UpperLeft);
				t.horizontalOverflow = HorizontalWrapMode.Wrap;
				ModUiKit.Size(t.gameObject, 100f);
				Button fetch = MakeButton(content, "获取工坊索引", Accent, () =>
				{
					WorkshopService.LoadIndex(this, () =>
					{
						if (s_instance != null && s_instance.m_tab == 1) s_instance.Rebuild();
					});
					Rebuild();
				}, 18);
				ModUiKit.Size(fetch.gameObject, 46f);
				return;
			}

			SetStatus($"工坊共 {WorkshopService.Index.packs.Count} 个内容" + (WorkshopService.FromCache ? "（缓存）" : ""));
			foreach (WorkshopPack pack in WorkshopService.Index.packs)
			{
				BuildWorkshopRow(content, pack);
			}
		}

		private void BuildWorkshopRow(RectTransform content, WorkshopPack pack)
		{
			RectTransform card = ModUiKit.Row(content);
			ModUiKit.Size(card.gameObject, 90f);

			Text name = card.Find("Name")?.GetComponent<Text>();
			if (name == null) name = ModUiKit.Label(card, string.Empty, 24, TextMain, TextAnchor.UpperLeft);
			name.text = pack.name + "  v" + pack.version + "  <" + pack.contentType + ">";
			name.font = ModUiKit.NativeFont;
			name.fontSize = 24;
			name.color = TextMain;
			name.fontStyle = FontStyle.Bold;
			ModUiKit.SetRect(name.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(15f, -38f), new Vector2(-240f, -6f));

			string meta = (string.IsNullOrEmpty(pack.author) ? "" : "作者 " + pack.author + " · ")
				+ (pack.size > 0 ? FormatSize(pack.size) + " · " : "")
				+ (string.IsNullOrEmpty(pack.summary) ? "" : pack.summary);
			Text metaText = ModUiKit.Label(card, meta, 18, TextDim, TextAnchor.UpperLeft);
			ModUiKit.SetRect(metaText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(15f, -78f), new Vector2(-240f, -42f));

			WorkshopInstallState state = WorkshopService.GetState(pack);
			string btnText = state == WorkshopInstallState.Installed ? "已安装"
				: state == WorkshopInstallState.UpdateAvailable ? "更新" : "安装";
			Color btnColor = state == WorkshopInstallState.Installed ? BtnNeutral
				: state == WorkshopInstallState.UpdateAvailable ? Accent : BtnGreen;

			float x = -16f;
			Button install = MakeButton(card, btnText, btnColor, () =>
			{
				if (WorkshopService.GetState(pack) == WorkshopInstallState.Installed)
				{
					ModManager.Instance.Toast("已安装：" + pack.name);
					return;
				}
				WorkshopInstaller.Install(this, pack, (ok, msg) =>
				{
					ModManager.Instance.Toast(ok ? "安装成功：" + pack.name : "安装失败：" + msg);
					if (s_instance != null && s_instance.m_tab == 1) s_instance.Rebuild();
				});
				ModManager.Instance.Toast("开始安装：" + pack.name);
			}, 18);
			PlaceRight(install.GetComponent<RectTransform>(), ref x, 100f);
		}

		private static string FormatSize(long bytes)
		{
			if (bytes <= 0) return "?";
			if (bytes < 1024) return bytes + "B";
			if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0") + "KB";
			return (bytes / 1048576.0).ToString("0.0") + "MB";
		}

		// ───────────────────────── 控件工厂 ─────────────────────────

		/// <summary>面板统一按钮：复用设置面板的原生按钮模板。</summary>
		private static Button MakeButton(Transform parent, string text, Color bg, Action onClick, int fontSize)
		{
			return ModUiKit.Button(parent, text, bg, onClick, fontSize);
		}

		private static void Place(RectTransform rt, Vector2 topLeft, Vector2 size)
		{
			rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
			rt.pivot = new Vector2(0f, 1f);
			rt.anchoredPosition = topLeft;
			rt.sizeDelta = size;
		}

		private static void Place(Transform footer, Button button, float x, float width)
		{
			RectTransform rt = button.GetComponent<RectTransform>();
			rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
			rt.pivot = new Vector2(0f, 0.5f);
			rt.anchoredPosition = new Vector2(x, 0f);
			rt.sizeDelta = new Vector2(width, 56f);
		}

		private static void PlaceRight(Transform footer, Button button, float inset, float width)
		{
			RectTransform rt = button.GetComponent<RectTransform>();
			rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
			rt.pivot = new Vector2(1f, 0.5f);
			rt.anchoredPosition = new Vector2(-inset, 0f);
			rt.sizeDelta = new Vector2(width, 56f);
		}

		// ───────────────────────── 通用动作 ─────────────────────────

		private void SetStatus(string text)
		{
			if (m_status != null)
			{
				m_status.text = text;
				m_status.color = TextDim;
			}
		}

		private void OpenModsFolder()
		{
			try
			{
				ModPaths.EnsureRoot();
				Application.OpenURL("file://" + ModPaths.Root.Replace('\\', '/'));
			}
			catch (Exception ex)
			{
				ModManager.Instance.Toast("无法打开目录：" + ex.Message);
			}
		}

		private void InstallFromClipboard()
		{
			if (m_installing) return;
			if (!ModZipClipboard.TryRead(() => GUIUtility.systemCopyBuffer, out string path, out string error))
			{
				ShowInstallFeedback(error, true);
				return;
			}
			if (Directory.Exists(path))
			{
				InstallFolderAt(path);
				return;
			}
			InstallArchive(path);
		}

		private async void InstallFromFile()
		{
			if (m_installing) return;
			string selected = null;
			SetInstalling(true);
			try
			{
				selected = await FileAPI.OpenFile(ModPaths.Root);
			}
			catch (Exception ex) { ShowInstallFeedback("选择文件失败：" + ex.Message, true); }
			finally { SetInstalling(false); }
			if (string.IsNullOrEmpty(selected)) return;
			if (!ModZipClipboard.TryNormalizePath(selected, out string path, out string error))
			{
				ShowInstallFeedback(error, true);
				return;
			}
			if (Directory.Exists(path))
			{
				InstallFolderAt(path);
				return;
			}
			InstallArchive(path);
		}

		private async void InstallFromFolder()
		{
			if (m_installing) return;
			string selected = null;
			SetInstalling(true);
			try
			{
				selected = await FileAPI.OpenDirectory(ModPaths.Root);
			}
			catch (Exception ex) { ShowInstallFeedback("选择文件夹失败：" + ex.Message, true); }
			finally { SetInstalling(false); }
			if (string.IsNullOrEmpty(selected)) return;
			InstallFolderAt(selected);
		}

		private void InstallFolderAt(string folderPath)
		{
			if (m_installing) return;
			SetInstalling(true);
			ShowInstallFeedback("正在加载文件夹：" + Path.GetFileName(folderPath.TrimEnd('\\', '/')), false);
			try
			{
				List<ModPackage> loaded = ModManager.Instance.InstallFolder(folderPath);
				Rebuild();
				int ok = 0;
				string firstError = null;
				foreach (ModPackage p in loaded)
				{
					if (p.IsLoaded) ok++;
					else if (firstError == null) firstError = p.DisplayName + "：" + (p.Error ?? p.State.ToString());
				}
				if (loaded.Count == 1 && loaded[0].IsLoaded)
				{
					ShowInstallFeedback("已就地加载并启用：" + loaded[0].DisplayName + " · 目录：" + ModPaths.ToDisplay(loaded[0].Directory), false);
				}
				else if (firstError != null)
				{
					ShowInstallFeedback("发现 " + loaded.Count + " 个 Mod，成功 " + ok + " 个；首个失败：" + firstError, true);
				}
				else
				{
					ShowInstallFeedback("已就地加载并启用 " + ok + " 个 Mod · 目录：" +
						ModPaths.ToDisplay(Path.GetFullPath(folderPath.TrimEnd('\\', '/'))), false);
				}
			}
			catch (Exception ex)
			{
				ShowInstallFeedback("加载文件夹失败：" + ex.Message, true);
			}
			finally { SetInstalling(false); }
		}

		private void InstallArchive(string path)
		{
			if (m_installing) return;
			SetInstalling(true);
			ShowInstallFeedback("正在安装：" + Path.GetFileName(path), false);
			try
			{
				ModPackage pkg = ModManager.Instance.InstallZip(path);
				Rebuild();
				ShowInstallFeedback(pkg.IsLoaded ? "已安装并启用：" + pkg.DisplayName :
					"已安装，但未能加载：" + (pkg.Error ?? pkg.State.ToString()), !pkg.IsLoaded);
			}
			catch (Exception ex)
			{
				ShowInstallFeedback("安装失败：" + ex.Message, true);
			}
			finally { SetInstalling(false); }
		}

		private void SetInstalling(bool installing)
		{
			m_installing = installing;
			if (m_installClipboard != null) m_installClipboard.interactable = !installing;
			if (m_import != null) m_import.interactable = !installing;
		}

		private void ShowInstallFeedback(string message, bool error)
		{
			SetStatus(message);
			if (m_status != null)
			{
				m_status.supportRichText = false;
				m_status.color = error ? StateBad : TextMain;
			}
			ModManager.Ensure().Toast(message);
		}
	}
}
