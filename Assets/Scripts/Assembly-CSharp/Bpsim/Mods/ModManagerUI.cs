using System;
using System.Collections.Generic;
using System.IO;
using Bpsim.Modding;
using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.Mods
{
	/// <summary>
	/// 游戏内 Mod 管理器界面（F10 或悬浮按钮打开）。两个页签：
	/// <c>已安装</c>（启用/禁用、打开面板、删除、装 zip）与 <c>创意工坊</c>（浏览索引并安装）。
	/// 全部用 uGUI 代码搭建，与 BPSS 工坊的工坊面板布局同源（顶部标题 + 分页 + 卡片列表）。
	/// </summary>
	public sealed class ModManagerUI : MonoBehaviour
	{
		private static ModManagerUI s_instance;

		private Canvas m_canvas;
		private RectTransform m_body;
		private Text m_status;
		private int m_tab; // 0=已安装 1=创意工坊

		private Button m_tabInstalled;
		private Button m_tabWorkshop;

		private GameObject m_floatingButton;

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
			Canvas canvas = ModUiKit.CreateCanvas("ModFloatingButton", 30500);
			canvas.transform.SetParent(transform, false);
			RectTransform rt = ModUiKit.Panel(canvas.transform, ModUiKit.Primary, 10);
			rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
			rt.pivot = new Vector2(0f, 0f);
			rt.anchoredPosition = new Vector2(16f, 16f);
			rt.sizeDelta = new Vector2(120f, 44f);
			Button btn = rt.gameObject.AddComponent<Button>();
			btn.targetGraphic = rt.GetComponent<Image>();
			btn.onClick.AddListener(Open);
			Text label = ModUiKit.Label(rt, "MOD", 22, ModUiKit.TextMain, TextAnchor.MiddleCenter);
			ModUiKit.SetRect(label.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
		}

		private void BuildIfNeeded()
		{
			if (m_canvas != null)
			{
				return;
			}
			m_canvas = ModUiKit.CreateCanvas("ModManagerCanvas", 31000);
			m_canvas.transform.SetParent(transform, false);

			// 背景遮罩
			RectTransform backdrop = ModUiKit.Panel(m_canvas.transform, new Color(0f, 0f, 0f, 0.55f), 2);
			ModUiKit.SetRect(backdrop, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
			Button bdBtn = backdrop.gameObject.AddComponent<Button>();
			bdBtn.transition = Selectable.Transition.None;
			bdBtn.onClick.AddListener(Close);

			// 主框：直接复用用户设置面板的背景、标题栏、字体和滚动视图。
			RectTransform frame = ModUiKit.NativePanel(m_canvas.transform);
			if (frame == null)
			{
				frame = ModUiKit.Panel(m_canvas.transform, ModUiKit.PanelBg, 16);
			}
			frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f);
			frame.pivot = new Vector2(0.5f, 0.5f);
			frame.sizeDelta = new Vector2(1600f, 900f);
			frame.anchoredPosition = Vector2.zero;

			Transform nativeHeader = frame.Find("Header");
			Text title;
			if (nativeHeader != null)
			{
				title = nativeHeader.Find("Text") != null ? nativeHeader.Find("Text").GetComponent<Text>() : null;
			}
			else
			{
				title = null;
			}
			if (title == null)
			{
				title = ModUiKit.Label(frame, "MOD 管理器", 24, ModUiKit.TextMain, TextAnchor.MiddleLeft);
				ModUiKit.SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(30f, -60f), Vector2.zero);
			}
			title.text = "MOD 管理器";
			title.font = ModUiKit.NativeFont;
			title.fontSize = 24;

			// 页签
			m_tabInstalled = ModUiKit.Button(frame, "已安装", ModUiKit.Primary, () => SetTab(0), 24);
			ModUiKit.SetRect(m_tabInstalled.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -118f), new Vector2(190f, -74f));
			m_tabWorkshop = ModUiKit.Button(frame, "创意工坊", ModUiKit.Neutral, () => SetTab(1), 24);
			ModUiKit.SetRect(m_tabWorkshop.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(200f, -118f), new Vector2(390f, -74f));

			// 状态栏
			m_status = ModUiKit.Label(frame, "", 24, ModUiKit.TextDim, TextAnchor.MiddleLeft);
			ModUiKit.SetRect(m_status.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(410f, -118f), new Vector2(-30f, -74f));

			// 底部工具条
			Transform oldButtonGroup = frame.Find("ButtonGroup");
			if (oldButtonGroup != null) Destroy(oldButtonGroup.gameObject);
			GameObject footerObject = new GameObject("Actions", typeof(RectTransform));
			RectTransform footer = footerObject.GetComponent<RectTransform>();
			footer.SetParent(frame, false);
			ModUiKit.SetRect(footer, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(30f, 20f), new Vector2(-30f, 80f));

			Button openFolder = ModUiKit.Button(footer, "打开 Mods 文件夹", ModUiKit.Neutral, OpenModsFolder, 24);
			ModUiKit.SetRect(openFolder.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(250f, 0f));

			Button installClip = ModUiKit.Button(footer, "安装剪贴板里的 zip", ModUiKit.Success, InstallFromClipboard, 24);
			ModUiKit.SetRect(installClip.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(270f, 0f), new Vector2(590f, 0f));

			Button refresh = ModUiKit.Button(footer, "刷新", ModUiKit.Primary, () =>
			{
				ModManager.Instance.Rescan();
				ModManager.Instance.Initialize();
				Rebuild();
			}, 18);
			ModUiKit.SetRect(refresh.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-132f, 0f), Vector2.zero);

			Button close = ModUiKit.Button(footer, "关闭", ModUiKit.Neutral, Close, 24);
			ModUiKit.SetRect(close.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-290f, 0f), new Vector2(-150f, 0f));

			// 列表容器：沿用用户设置预制体的 ScrollRect、Viewport、遮罩和滚动条。
			Transform nativeScroll = frame.Find("Scroll View");
			if (nativeScroll != null)
			{
				RectTransform scrollRect = nativeScroll.GetComponent<RectTransform>();
				ModUiKit.SetRect(scrollRect, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(30f, 100f), new Vector2(-30f, -150f));
				Transform content = nativeScroll.Find("Viewport/Content");
				m_body = content != null ? content.GetComponent<RectTransform>() : scrollRect;
				VerticalLayoutGroup layout = m_body.GetComponent<VerticalLayoutGroup>();
				if (layout != null)
				{
					layout.padding = new RectOffset(0, 0, 20, 20);
					layout.spacing = 20f;
					layout.childControlWidth = true;
					layout.childForceExpandWidth = true;
				}
			}
			else
			{
				RectTransform bodyHost = ModUiKit.Panel(frame, new Color(0f, 0f, 0f, 0f), 2);
				ModUiKit.SetRect(bodyHost, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(30f, 100f), new Vector2(-30f, -150f));
				m_body = bodyHost;
			}
		}

		private void SetTab(int tab)
		{
			m_tab = tab;
			Rebuild();
		}

		private void Rebuild()
		{
			if (m_body == null)
			{
				return;
			}
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
			if (img != null) img.color = active ? ModUiKit.Primary : ModUiKit.Neutral;
		}

		// ───────────────────────── 已安装 ─────────────────────────

		private void BuildInstalledList()
		{
			ModManager mgr = ModManager.Instance;
			RectTransform content = m_body;

			if (mgr.Packages.Count == 0)
			{
				Text empty = ModUiKit.Label(content, "还没有安装任何 Mod。把 Mod 文件夹（含 manifest.json）放到 Mods 目录，点「刷新」。\nMods 目录：" + ModPaths.ToDisplay(ModPaths.Root), 24, ModUiKit.TextDim, TextAnchor.UpperLeft);
				ModUiKit.Size(empty.gameObject, 90f);
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
			RectTransform card = ModUiKit.Row(content);
			card.name = "Row_" + pkg.Id;
			ModUiKit.Size(card.gameObject, 90f);

			Color stateColor = pkg.State == ModState.Loaded ? ModUiKit.Success
				: pkg.State == ModState.Error || pkg.State == ModState.Incompatible ? ModUiKit.Danger : ModUiKit.TextDim;

			Text name = ModUiKit.Label(card, pkg.DisplayName + "  v" + pkg.Version, 24, ModUiKit.TextMain, TextAnchor.UpperLeft);
			ModUiKit.SetRect(name.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(15f, -38f), new Vector2(-420f, -6f));

			string sub = (string.IsNullOrEmpty(pkg.Manifest?.author) ? "" : "作者 " + pkg.Manifest.author + " · ")
				+ SourceText(pkg.Source)
				+ StateText(pkg.State);
			if (!string.IsNullOrEmpty(pkg.Error))
			{
				sub += " · " + pkg.Error;
			}
			Text subText = ModUiKit.Label(card, sub, 20, stateColor, TextAnchor.UpperLeft);
			ModUiKit.SetRect(subText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(15f, -78f), new Vector2(-420f, -42f));

			// 右侧按钮区
			var actions = new List<RectTransform>();
			float x = -18f;
			Button toggle = ModUiKit.Button(card, pkg.Enabled ? "已启用" : "已禁用",
				pkg.Enabled ? ModUiKit.Success : ModUiKit.Neutral,
				() => ModManager.Instance.SetEnabled(pkg.Id, !pkg.Enabled), 17);
			PlaceRight(toggle.GetComponent<RectTransform>(), ref x, 120f);
			actions.Add(toggle.GetComponent<RectTransform>());

			if (pkg.Source == ModSource.Installed)
			{
				Button del = ModUiKit.Button(card, "删除", ModUiKit.Danger,
					() => ModManager.Instance.Uninstall(pkg.Id), 17);
				PlaceRight(del.GetComponent<RectTransform>(), ref x, 80f);
				actions.Add(del.GetComponent<RectTransform>());
			}

			// 面板打开按钮
			if (pkg.Manifest != null && pkg.Manifest.HasPanels)
			{
				foreach (ModPanelSpec spec in pkg.Manifest.panels)
				{
					ModPanelSpec captured = spec;
					Button open = ModUiKit.Button(card, "打开:" + (string.IsNullOrEmpty(spec.title) ? spec.id : spec.title),
						ModUiKit.Primary, () => ModPanelService.Open(pkg, captured), 16);
					PlaceRight(open.GetComponent<RectTransform>(), ref x, 160f);
					actions.Add(open.GetComponent<RectTransform>());
				}
			}

			// 插件注册的快捷按钮
			List<ModManager.QuickButton> quick = ModManager.Instance.GetQuickButtons(pkg.Id);
			if (quick != null)
			{
				foreach (ModManager.QuickButton qb in quick)
				{
					ModManager.QuickButton capturedQb = qb;
					Button qbtn = ModUiKit.Button(card, capturedQb.Label, ModUiKit.CardBgAlt, () => capturedQb.OnClick?.Invoke(), 16);
					PlaceRight(qbtn.GetComponent<RectTransform>(), ref x, 140f);
					actions.Add(qbtn.GetComponent<RectTransform>());
				}
			}
			FitRightActions(actions);
		}

		private static void PlaceRight(RectTransform rt, ref float x, float width)
		{
			rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
			rt.pivot = new Vector2(1f, 0.5f);
			rt.sizeDelta = new Vector2(width, 44f);
			rt.anchoredPosition = new Vector2(x, 0f);
			x -= (width + 8f);
		}

		private static void FitRightActions(List<RectTransform> actions)
		{
			if (actions == null || actions.Count < 2) return;
			const float rightInset = 18f;
			const float leftInset = 420f;
			const float gap = 8f;
			float total = 0f;
			foreach (RectTransform action in actions) total += action.sizeDelta.x;
			float available = leftInset - rightInset - gap * (actions.Count - 1);
			if (total <= available) return;

			// Keep every action inside the card's reserved right column. Text is
			// clamped by the button label, so narrow rows remain clickable instead
			// of pushing the first control past the list viewport.
			// The lower bound keeps the calculation finite even if a third-party
			// plugin registers many quick actions; the row still never crosses its
			// reserved column.
			float width = Mathf.Max(1f, available / actions.Count);
			float x = -rightInset;
			foreach (RectTransform action in actions)
			{
				action.sizeDelta = new Vector2(width, action.sizeDelta.y);
				action.anchoredPosition = new Vector2(x, action.anchoredPosition.y);
				x -= width + gap;
			}
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
				Text t = ModUiKit.Label(content, "正在获取工坊索引…", 24, ModUiKit.TextDim, TextAnchor.UpperLeft);
				ModUiKit.Size(t.gameObject, 40f);
				return;
			}
			if (WorkshopService.Index == null || WorkshopService.Index.packs == null || WorkshopService.Index.packs.Count == 0)
			{
				Text t = ModUiKit.Label(content, "工坊索引为空或未配置。\n"
					+ (string.IsNullOrEmpty(WorkshopService.LastError) ? "索引地址：" + WorkshopService.IndexUrl : "错误：" + WorkshopService.LastError),
					24, ModUiKit.TextDim, TextAnchor.UpperLeft);
				ModUiKit.Size(t.gameObject, 100f);
				Button fetch = ModUiKit.Button(content, "获取工坊索引", ModUiKit.Primary, () =>
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

			Text name = ModUiKit.Label(card, pack.name + "  v" + pack.version + "  <" + pack.contentType + ">", 24, ModUiKit.TextMain, TextAnchor.UpperLeft);
			ModUiKit.SetRect(name.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(15f, -38f), new Vector2(-240f, -6f));

			string meta = (string.IsNullOrEmpty(pack.author) ? "" : "作者 " + pack.author + " · ")
				+ (pack.size > 0 ? FormatSize(pack.size) + " · " : "")
				+ (string.IsNullOrEmpty(pack.summary) ? "" : pack.summary);
			Text metaText = ModUiKit.Label(card, meta, 20, ModUiKit.TextDim, TextAnchor.UpperLeft);
			ModUiKit.SetRect(metaText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(15f, -78f), new Vector2(-240f, -42f));

			WorkshopInstallState state = WorkshopService.GetState(pack);
			string btnText = state == WorkshopInstallState.Installed ? "已安装"
				: state == WorkshopInstallState.UpdateAvailable ? "更新" : "安装";
			Color btnColor = state == WorkshopInstallState.Installed ? ModUiKit.Neutral
				: state == WorkshopInstallState.UpdateAvailable ? ModUiKit.Primary : ModUiKit.Success;

			float x = -18f;
			Button install = ModUiKit.Button(card, btnText, btnColor, () =>
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

		// ───────────────────────── 通用动作 ─────────────────────────

		private void SetStatus(string text)
		{
			if (m_status != null)
			{
				m_status.text = text;
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
			string path = string.Empty;
			try { path = GUIUtility.systemCopyBuffer; } catch { }
			if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
			{
				ModManager.Instance.Toast("剪贴板里没有 .zip 路径。请复制 zip 的完整路径后重试。");
				return;
			}
			try
			{
				ModPackage pkg = ModManager.Instance.InstallZip(path);
				ModManager.Instance.Toast("已安装：" + pkg.DisplayName);
				Rebuild();
			}
			catch (Exception ex)
			{
				ModManager.Instance.Toast("安装失败：" + ex.Message);
			}
		}
	}
}
