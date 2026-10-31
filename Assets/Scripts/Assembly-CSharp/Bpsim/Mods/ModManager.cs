using System;
using System.Collections.Generic;
using System.IO;
using Bpsim.Modding;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.Mods
{
	/// <summary>
	/// Mod 框架的托管中枢（单例）。职责：
	/// 文件层（扫描/安装/卸载）→ 装配层（<see cref="ModLoader"/>：扫描 + Assembly.LoadFrom + 入口实例化）
	/// → 同步到 ECS 注册表（<see cref="ModEcsBridge"/>）→ 驱动 UI（<see cref="ModManagerUI"/>、<see cref="ModPanelService"/>）。
	/// </summary>
	/// <remarks>
	/// <para>这一版把 BepInEx 通道整个拆了：没有 <c>RegisterExternal</c>，没有「等插件来注册」的状态，
	/// 所有 Mod 都从目录里发现、走同一条加载管线。设计对齐 Besiege 的官方加载器。</para>
	/// <para>ECS 思想不变：这里只处理「无法放进 ECS 的东西」（文件 IO、托管程序集加载）；
	/// 每个 Mod 的状态镜像成 <see cref="ModEntityComponent"/> 实体，供其它系统用 EntityQuery 查询。</para>
	/// </remarks>
	[DefaultExecutionOrder(-500)]
	public sealed class ModManager : MonoBehaviour
	{
		public static ModManager Instance { get; private set; }

		/// <summary>全部已发现的 Mod 包（Installed 优先于 Workshop / Additional / BuiltIn）。</summary>
		public readonly List<ModPackage> Packages = new List<ModPackage>();

		/// <summary>任何状态改变（加载/禁用/安装/删除）后触发，UI 订阅重绘。</summary>
		public event Action Changed;

		private ModdingConfig m_config;
		private readonly Dictionary<string, List<QuickButton>> m_quickButtons =
			new Dictionary<string, List<QuickButton>>(StringComparer.OrdinalIgnoreCase);

		private GameObject m_toastRoot;
		private RectTransform m_toastRect;
		private CanvasGroup m_toastGroup;
		private float m_toastUntil;
		private bool m_initialized;

		public bool Initialized => m_initialized;

		/// <summary>全局配置（禁用列表、维护版本、严格扫描）。</summary>
		public ModdingConfig Config => m_config;

		public static ModManager Ensure()
		{
			if (Instance != null)
			{
				return Instance;
			}
			GameObject go = new GameObject("ModManager");
			UnityEngine.Object.DontDestroyOnLoad(go);
			Instance = go.AddComponent<ModManager>();
			return Instance;
		}

		public sealed class QuickButton
		{
			public string ModId;
			public string Label;
			public Action OnClick;
		}

		private void Awake()
		{
			if (Instance != null && Instance != this)
			{
				Destroy(gameObject);
				return;
			}
			Instance = this;
			UnityEngine.Object.DontDestroyOnLoad(gameObject);
			if (!m_initialized)
			{
				Initialize();
			}
		}

		/// <summary>初始化（幂等）。</summary>
		public void Initialize()
		{
			if (m_initialized)
			{
				return;
			}
			m_initialized = true;
			ModPaths.EnsureRoot();
			m_config = ModdingConfig.Load(ModPaths.ConfigPath);
			Rescan();
			// 迁移期状态必须先固化，否则玩家手动启用的 Mod 会被迁移规则重新判成禁用
			m_config.MaterializeLegacy(Packages);
			ApplyEnabled();

			// 游戏版本变了 → 全禁用（Besiege 同款保守策略）
			string previousVersion = m_config.LastGameVersion;
			bool versionChanged = m_config.Maintenance(Application.version, Packages);
			if (versionChanged)
			{
				ApplyEnabled();
				Debug.LogWarning("[Mods] 检测到游戏版本变化（" + previousVersion + " → " + Application.version + "），已禁用全部 Mod。");
			}
			m_config.Save(ModPaths.ConfigPath);

			AttachEcs();

			foreach (ModPackage pkg in Packages)
			{
				if (pkg.Enabled)
				{
					LoadPackage(pkg);
				}
			}
			ModEcsBridge.SyncAll(Packages);

			if (versionChanged)
			{
				Toast("游戏已更新，Mod 已全部禁用，请逐个确认后再启用");
			}
			Debug.Log("[Mods] 初始化完成：发现 " + Packages.Count + " 个 Mod。root=" + ModPaths.ToDisplay(ModPaths.Root));
		}

		// ───────────────────────── 发现 & 启用状态 ─────────────────────────

		/// <summary>重新扫描磁盘，保留已加载对象（程序集与入口实例）。</summary>
		public void Rescan()
		{
			List<ModPackage> fresh = ModLoader.Discover(m_config);

			// 复用旧对象，避免把已加载的入口丢掉
			Dictionary<Guid, ModPackage> old = new Dictionary<Guid, ModPackage>();
			foreach (ModPackage p in Packages)
			{
				if (p != null && p.Manifest != null && p.Guid != Guid.Empty)
				{
					old[p.Guid] = p;
				}
			}

			Packages.Clear();
			foreach (ModPackage np in fresh)
			{
				if (old.TryGetValue(np.Guid, out ModPackage op))
				{
					op.Directory = np.Directory;
					op.Manifest = np.Manifest;
					op.Source = np.Source;
					op.WorkshopId = np.WorkshopId;
					Packages.Add(op);
				}
				else
				{
					Packages.Add(np);
				}
			}

			ModRegistry.Rebuild(Packages);
			ApplyEnabled();
		}

		private void ApplyEnabled()
		{
			foreach (ModPackage p in Packages)
			{
				if (p.Manifest == null)
				{
					continue;
				}
				p.Enabled = !m_config.IsDisabled(p.Manifest.guid, p.Id);
			}
		}

		// ───────────────────────── 加载 / 卸载 ─────────────────────────

		public ModPackage Find(string id)
		{
			ModPackage byRegistry = ModRegistry.GetModById(id);
			if (byRegistry != null)
			{
				return byRegistry;
			}
			foreach (ModPackage p in Packages)
			{
				if (string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
				{
					return p;
				}
			}
			return null;
		}

		/// <summary>启用/禁用并持久化到 Modding.xml。</summary>
		public void SetEnabled(string id, bool enabled)
		{
			ModPackage pkg = Find(id);
			if (pkg == null)
			{
				return;
			}
			if (enabled == pkg.Enabled && (enabled ? pkg.IsLoaded : true))
			{
				return;
			}
			m_config.SetEnabled(pkg.Guid, enabled);
			m_config.Save(ModPaths.ConfigPath);
			if (enabled)
			{
				pkg.Enabled = true;
				LoadPackage(pkg);
			}
			else
			{
				UnloadPackage(pkg);
				pkg.Enabled = false;
			}
			ModEcsBridge.UpdateOne(pkg);
			Changed?.Invoke();
		}

		/// <summary>加载单个包（不做持久化）。</summary>
		public void LoadPackage(ModPackage pkg)
		{
			if (pkg == null || pkg.Manifest == null || pkg.State == ModState.Loaded)
			{
				return;
			}
			if (!CheckVersion(pkg))
			{
				pkg.State = ModState.Incompatible;
				pkg.Error = "需要游戏版本 ≥ " + pkg.Manifest.minGameVersion;
				ModEcsBridge.UpdateOne(pkg);
				return;
			}

			if (ModLoader.Load(pkg, m_config, this, out string error))
			{
				ModRegistry.InvokeLoaded(pkg.Guid);
				NotifyWorld(pkg);
				// Mod 通常晚于部件工厂初始化，这里补发一次「部件已就绪」。
				ModHooks.NotifyPartsReady();
			}
			else if (!string.IsNullOrEmpty(error))
			{
				pkg.Error = error;
			}
			ModEcsBridge.UpdateOne(pkg);
		}

		/// <summary>世界已经存在时，补一次 <c>OnWorldCreated</c>（Mod 是在世界之后才加载的）。</summary>
		private static void NotifyWorld(ModPackage pkg)
		{
			if (pkg == null || pkg.EntryPoint == null)
			{
				return;
			}
			World world = World.DefaultGameObjectInjectionWorld;
			if (world == null || !world.IsCreated)
			{
				return;
			}
			try
			{
				pkg.EntryPoint.OnWorldCreated(world);
			}
			catch (Exception ex)
			{
				Debug.LogWarning("[Mods] " + pkg.Id + " 的 OnWorldCreated 抛异常：" + ex.Message);
			}
		}

		/// <summary>卸载/禁用单个包（不做持久化）。</summary>
		public void UnloadPackage(ModPackage pkg)
		{
			if (pkg == null)
			{
				return;
			}
			ModLoader.Unload(pkg);
			ModPanelService.ClosePackage(pkg);
			m_quickButtons.Remove(pkg.Id);
			ModEcsBridge.UpdateOne(pkg);
		}

		private static bool CheckVersion(ModPackage pkg)
		{
			string min = pkg.Manifest.minGameVersion;
			if (string.IsNullOrWhiteSpace(min))
			{
				return true;
			}
			// 只在明确更旧时拒绝；解析不出来就放行
			string game = Application.version;
			if (string.IsNullOrEmpty(game))
			{
				return true;
			}
			return CompareVersion(game, min) >= 0;
		}

		/// <summary>比较 a/b 版本号，返回 -1/0/1。非数字段忽略。</summary>
		public static int CompareVersion(string a, string b)
		{
			int[] pa = ParseVersion(a);
			int[] pb = ParseVersion(b);
			int n = Math.Max(pa.Length, pb.Length);
			for (int i = 0; i < n; i++)
			{
				int x = i < pa.Length ? pa[i] : 0;
				int y = i < pb.Length ? pb[i] : 0;
				if (x != y)
				{
					return x < y ? -1 : 1;
				}
			}
			return 0;
		}

		private static int[] ParseVersion(string v)
		{
			if (string.IsNullOrEmpty(v))
			{
				return new int[0];
			}
			List<int> parts = new List<int>();
			foreach (string seg in v.Split('.', '-', '+', ' '))
			{
				if (int.TryParse(seg, out int n))
				{
					parts.Add(n);
				}
			}
			return parts.ToArray();
		}

		// ───────────────────────── 安装 / 卸载 ─────────────────────────

		/// <summary>从 zip 安装（返回安装好的包；失败抛异常）。</summary>
		public ModPackage InstallZip(string zipPath)
		{
			ModPackage pkg = ModPackageStore.InstallZip(zipPath);
			Rescan();
			ModEcsBridge.SyncAll(Packages);
			ModPackage found = Find(pkg.Id);
			if (found != null)
			{
				m_config.SetEnabled(found.Guid, true);
				m_config.Save(ModPaths.ConfigPath);
				found.Enabled = true;
				LoadPackage(found);
				ModEcsBridge.UpdateOne(found);
			}
			Changed?.Invoke();
			return found ?? pkg;
		}

		public bool Uninstall(string id)
		{
			ModPackage pkg = Find(id);
			if (pkg == null)
			{
				return false;
			}
			UnloadPackage(pkg);
			bool ok = ModPackageStore.Uninstall(pkg);
			Rescan();
			ModEcsBridge.SyncAll(Packages);
			Changed?.Invoke();
			return ok;
		}

		// ───────────────────────── ECS 桥 & 命令回读 ─────────────────────────

		private void AttachEcs()
		{
			World world = World.DefaultGameObjectInjectionWorld;
			if (world != null && world.IsCreated)
			{
				ModEcsBridge.Attach(world);
			}
			else
			{
				Debug.LogWarning("[Mods] 未能获取 ECS World，ECS 注册表将为空。");
			}
		}

		private void Update()
		{
			// 回读 ECS 侧命令（系统请求启停）
			if (ModEcsBridge.Attached && ModEcsBridge.PendingCommands.Count > 0)
			{
				foreach (ModCommandElement cmd in ModEcsBridge.PendingCommands)
				{
					if (cmd.Command == ModCommandElement.Kind.SetEnabled)
					{
						string id = cmd.Id.ToString();
						ModPackage pkg = Find(id);
						if (pkg != null && (cmd.Value != 0) != pkg.Enabled)
						{
							SetEnabled(id, cmd.Value != 0);
						}
					}
				}
				ModEcsBridge.ClearPending();
			}

			// 面板快捷键（新输入系统启用时 GetKeyDown 会抛异常，忽略即可）
			try
			{
				if (UnityEngine.Input.GetKeyDown(KeyCode.F10))
				{
					ModManagerUI.Toggle();
				}
			}
			catch (InvalidOperationException) { }

			// Toast 淡出
			if (m_toastGroup != null && Time.unscaledTime > m_toastUntil + 0.6f)
			{
				if (m_toastRoot != null)
				{
					Destroy(m_toastRoot);
					m_toastRoot = null;
					m_toastGroup = null;
					m_toastRect = null;
				}
			}
			else if (m_toastGroup != null)
			{
				float remain = m_toastUntil - Time.unscaledTime;
				m_toastGroup.alpha = Mathf.Clamp01(remain / 0.6f);
			}
		}

		// ───────────────────────── 快速入口 ─────────────────────────

		public void AddQuickButton(string modId, string label, Action onClick)
		{
			if (!m_quickButtons.TryGetValue(modId, out List<QuickButton> list))
			{
				list = new List<QuickButton>();
				m_quickButtons[modId] = list;
			}
			list.Add(new QuickButton { ModId = modId, Label = label, OnClick = onClick });
			Changed?.Invoke();
		}

		public List<QuickButton> GetQuickButtons(string modId)
		{
			return m_quickButtons.TryGetValue(modId, out List<QuickButton> list) ? list : null;
		}

		public IEnumerable<QuickButton> AllQuickButtons()
		{
			foreach (List<QuickButton> list in m_quickButtons.Values)
			{
				foreach (QuickButton b in list)
				{
					yield return b;
				}
			}
		}

		// ───────────────────────── Toast ─────────────────────────

		public void Toast(string message)
		{
			if (string.IsNullOrEmpty(message))
			{
				return;
			}
			if (m_toastRoot == null)
			{
				Canvas canvas = ModUiKit.CreateCanvas("ModToast", 35000);
				canvas.transform.SetParent(transform, false);
				m_toastRoot = canvas.gameObject;
				m_toastGroup = m_toastRoot.AddComponent<CanvasGroup>();
				m_toastGroup.blocksRaycasts = false;
				m_toastGroup.interactable = false;
				m_toastRect = ModUiKit.Panel(canvas.transform, new Color(0.07f, 0.08f, 0.1f, 0.95f), 12);
				m_toastRect.anchorMin = m_toastRect.anchorMax = new Vector2(0.5f, 0f);
				m_toastRect.pivot = new Vector2(0.5f, 0f);
				m_toastRect.anchoredPosition = new Vector2(0f, 120f);
				m_toastRect.sizeDelta = new Vector2(560f, 56f);
				Text t = ModUiKit.Label(m_toastRect, "", 22, ModUiKit.TextMain, TextAnchor.MiddleCenter);
				ModUiKit.SetRect(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(12f, 4f), new Vector2(-12f, -4f));
				t.name = "ToastText";
			}
			Transform txt = m_toastRoot.transform.Find("Panel/Label");
			if (txt != null)
			{
				txt.GetComponent<Text>().text = message;
			}
			m_toastGroup.alpha = 1f;
			m_toastUntil = Time.unscaledTime + 2.4f;
			m_toastRoot.SetActive(true);
		}

		// ───────────────────────── 设置持久化 ─────────────────────────

		/// <summary>把当前状态写回 <c>Modding.xml</c>（旧名保留，避免外部调用断掉）。</summary>
		public void SaveSettings()
		{
			m_config?.Save(ModPaths.ConfigPath);
		}

		/// <summary>打开 Mod 管理器界面。</summary>
		public void OpenManagerUi()
		{
			ModManagerUI.Open();
		}
	}
}
