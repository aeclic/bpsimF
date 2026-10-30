using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Bpsim.Modding;
using Newtonsoft.Json;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.Mods
{
	/// <summary>
	/// Mod 框架的托管中枢（单例）。职责：
	/// 文件层（扫描/安装/卸载）→ 装配层（Assembly.LoadFrom + 实例化 <see cref="IBpsimMod"/>）→
	/// 同步到 ECS 注册表（<see cref="ModEcsBridge"/>）→ 驱动 UI（<see cref="ModManagerUI"/>、<see cref="ModPanelService"/>）。
	/// </summary>
	/// <remarks>
	/// ECS 思想：这里只处理「无法放进 ECS 的东西」（文件 IO、托管程序集加载）；
	/// 每个 Mod 的状态都会镜像成 <see cref="ModEntityComponent"/> 实体，供其它系统用 EntityQuery 查询。
	/// 需要从 ECS 侧改变启停时，系统往命令缓冲写命令，本类每帧回读后执行实际的加载/卸载。
	/// </remarks>
	[DefaultExecutionOrder(-500)]
	public sealed class ModManager : MonoBehaviour
	{
		public static ModManager Instance { get; private set; }

		/// <summary>全部已发现的 Mod 包（Installed 优先于 BuiltIn）。</summary>
		public readonly List<ModPackage> Packages = new List<ModPackage>();

		/// <summary>任何状态改变（加载/禁用/安装/删除）后触发，UI 订阅重绘。</summary>
		public event Action Changed;

		private ModSettings m_settings;
		private readonly Dictionary<string, ModPackage> m_external = new Dictionary<string, ModPackage>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, List<QuickButton>> m_quickButtons =
			new Dictionary<string, List<QuickButton>>(StringComparer.OrdinalIgnoreCase);

		private GameObject m_toastRoot;
		private RectTransform m_toastRect;
		private CanvasGroup m_toastGroup;
		private float m_toastUntil;
		private bool m_initialized;

		public bool Initialized => m_initialized;

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
			m_settings = LoadSettings();
			Rescan();
			ApplyDefaultEnabled();
			AttachEcs();
			// 加载玩家启用的 Mod
			foreach (ModPackage pkg in Packages)
			{
				if (pkg.Enabled)
				{
					LoadPackage(pkg);
				}
			}
			ModEcsBridge.SyncAll(Packages);
			Debug.Log("[Mods] 初始化完成：发现 " + Packages.Count + " 个 Mod。root=" + ModPaths.ToDisplay(ModPaths.Root));
		}

		// ───────────────────────── 发现 & 默认启用 ─────────────────────────

		/// <summary>重新扫描磁盘，保留已加载对象的状态。</summary>
		public void Rescan()
		{
			List<ModPackage> fresh = ModPackageStore.Scan();
			foreach (ModPackage external in m_external.Values)
			{
				fresh.RemoveAll(p => string.Equals(p.Id, external.Id, StringComparison.OrdinalIgnoreCase));
				fresh.Add(external);
			}
			// 尽量复用旧对象（保留已加载的程序集/实例）
			Dictionary<string, ModPackage> old = new Dictionary<string, ModPackage>(StringComparer.OrdinalIgnoreCase);
			foreach (ModPackage p in Packages)
			{
				old[p.Id] = p;
			}
			Packages.Clear();
			foreach (ModPackage np in fresh)
			{
				if (old.TryGetValue(np.Id, out ModPackage op))
				{
					op.Directory = np.Directory;
					op.Manifest = np.Manifest;
					op.Source = np.Source;
					Packages.Add(op);
				}
				else
				{
					Packages.Add(np);
				}
			}
			foreach (ModPackage p in Packages)
			{
				p.Enabled = m_settings.IsEnabled(p.Id) || (!m_settings.disabled.Contains(p.Id) && DefaultEnabled(p));
			}
		}

		private void ApplyDefaultEnabled()
		{
			foreach (ModPackage p in Packages)
			{
				p.Enabled = m_settings.IsEnabled(p.Id) || (!m_settings.disabled.Contains(p.Id) && DefaultEnabled(p));
			}
		}

		/// <summary>默认启用策略：纯内容包(panel)默认开；含代码(plugin/mixed)默认关（需玩家显式授权）。</summary>
		private static bool DefaultEnabled(ModPackage p)
		{
			return p.Source == ModSource.BepInEx ? p.ExternalDefaultEnabled : p.Manifest != null && !p.Manifest.IsPlugin;
		}

		/// <summary>Register an already loaded BepInEx plugin. Never load its DLL a second time.</summary>
		public void RegisterExternal(ModManifest manifest, IBpsimMod instance, string directory, bool defaultEnabled)
		{
			if (manifest == null || instance == null) throw new ArgumentNullException();
			manifest.Normalize();
			if (m_external.TryGetValue(manifest.id, out var previous))
			{
				if (ReferenceEquals(previous.ExternalInstance, instance)) return;
				throw new InvalidOperationException("BepInEx Mod 重复注册：" + manifest.id);
			}
			var existing = Find(manifest.id);
			if (existing != null) throw new InvalidOperationException("Mod id 已存在：" + manifest.id);
			var pkg = new ModPackage { Manifest = manifest, Directory = directory, Source = ModSource.BepInEx,
				ExternalInstance = instance, ExternalDefaultEnabled = defaultEnabled };
			m_external.Add(pkg.Id, pkg);
			Packages.Add(pkg);
			pkg.Enabled = m_settings.IsEnabled(pkg.Id) || (!m_settings.disabled.Contains(pkg.Id) && defaultEnabled);
			if (pkg.Enabled) LoadPackage(pkg);
			ModEcsBridge.UpdateOne(pkg);
			Changed?.Invoke();
		}

		public void UnregisterExternal(string id, IBpsimMod instance)
		{
			if (!m_external.TryGetValue(id, out var pkg) || !ReferenceEquals(pkg.ExternalInstance, instance)) return;
			UnloadPackage(pkg);
			m_external.Remove(id);
			Packages.Remove(pkg);
			ModEcsBridge.SyncAll(Packages);
			Changed?.Invoke();
		}

		// ───────────────────────── 加载 / 卸载 ─────────────────────────

		public ModPackage Find(string id)
		{
			foreach (ModPackage p in Packages)
			{
				if (string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
				{
					return p;
				}
			}
			return null;
		}

		/// <summary>启用/禁用并持久化。</summary>
		public void SetEnabled(string id, bool enabled)
		{
			ModPackage pkg = Find(id);
			if (pkg == null)
			{
				return;
			}
			if (enabled == pkg.Enabled && (enabled ? pkg.IsLoaded : true))
			{
				// 已经处于目标状态
			}
			m_settings.Set(id, enabled);
			SaveSettings();
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
				return;
			}
			pkg.Error = null;
			try
			{
				if (pkg.Manifest.IsPlugin)
				{
					LoadPlugin(pkg);
				}
				else
				{
					pkg.State = ModState.Loaded; // 纯内容包：面板按需打开
				}
			}
			catch (Exception ex)
			{
				pkg.State = ModState.Error;
				pkg.Error = ex.Message;
				Debug.LogError("[Mods] 加载失败 " + pkg.Id + ": " + ex);
			}
			ModEcsBridge.UpdateOne(pkg);
		}

		private void LoadPlugin(ModPackage pkg)
		{
			if (pkg.ExternalInstance != null)
			{
				pkg.Instance = pkg.ExternalInstance;
				pkg.Instance.OnLoad(new ModContext(pkg, this));
				pkg.State = ModState.Loaded;
				return;
			}
			if (string.IsNullOrWhiteSpace(pkg.Manifest.entryAssembly))
			{
				throw new InvalidOperationException("manifest 缺少 entryAssembly");
			}
			string asmPath = Path.Combine(pkg.Directory, pkg.Manifest.entryAssembly);
			if (!File.Exists(asmPath))
			{
				throw new FileNotFoundException("找不到入口程序集：" + pkg.Manifest.entryAssembly);
			}
			pkg.Assembly = Assembly.LoadFrom(asmPath);
			Type entryType = null;
			if (!string.IsNullOrWhiteSpace(pkg.Manifest.entryType))
			{
				entryType = pkg.Assembly.GetType(pkg.Manifest.entryType);
			}
			if (entryType == null)
			{
				// 兜底：找程序集里第一个 IBpsimMod 实现
				foreach (Type t in pkg.Assembly.GetTypes())
				{
					if (typeof(IBpsimMod).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
					{
						entryType = t;
						break;
					}
				}
			}
			if (entryType == null)
			{
				throw new InvalidOperationException("程序集里找不到 IBpsimMod 入口");
			}
			IBpsimMod instance = (IBpsimMod)Activator.CreateInstance(entryType);
			pkg.Instance = instance;
			instance.OnLoad(new ModContext(pkg, this));
			pkg.State = ModState.Loaded;
			Debug.Log("[Mods] 已加载插件 " + pkg.Id + " (" + entryType.FullName + ")");
		}

		/// <summary>卸载/禁用单个包（不做持久化）。</summary>
		public void UnloadPackage(ModPackage pkg)
		{
			if (pkg == null)
			{
				return;
			}
			try
			{
				if (pkg.Instance != null)
				{
					pkg.Instance.OnUnload();
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning("[Mods] OnUnload 异常 " + pkg.Id + ": " + ex.Message);
			}
			finally
			{
				pkg.Instance = null;
			}
			ModPanelService.ClosePackage(pkg);
			m_quickButtons.Remove(pkg.Id);
			if (pkg.State == ModState.Loaded)
			{
				pkg.State = ModState.Discovered;
			}
		}

		private static bool CheckVersion(ModPackage pkg)
		{
			string min = pkg.Manifest.minGameVersion;
			if (string.IsNullOrWhiteSpace(min))
			{
				return true;
			}
			string game = Application.version;
			if (CompareVersion(game, min) < 0)
			{
				// 无法比较时放行（只在明确更旧时拒绝）
				return true;
			}
			return true;
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
			ApplyDefaultEnabled();
			ModEcsBridge.SyncAll(Packages);
			// 新装的内容包默认直接加载
			ModPackage found = Find(pkg.Id);
			if (found != null && found.Enabled)
			{
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
			ApplyDefaultEnabled();
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

		private static ModSettings LoadSettings()
		{
			try
			{
				if (File.Exists(ModPaths.SettingsPath))
				{
					ModSettings s = JsonConvert.DeserializeObject<ModSettings>(
						File.ReadAllText(ModPaths.SettingsPath), ModPackageStore.Lenient);
					if (s != null)
					{
						s.enabled ??= new List<string>();
						s.disabled ??= new List<string>();
						return s;
					}
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning("[Mods] 读取设置失败：" + ex.Message);
			}
			return new ModSettings();
		}

		public void SaveSettings()
		{
			try
			{
				ModPaths.EnsureRoot();
				File.WriteAllText(ModPaths.SettingsPath, JsonConvert.SerializeObject(m_settings, Formatting.Indented));
			}
			catch (Exception ex)
			{
				Debug.LogWarning("[Mods] 写入设置失败：" + ex.Message);
			}
		}

		/// <summary>打开 Mod 管理器界面。</summary>
		public void OpenManagerUi()
		{
			ModManagerUI.Open();
		}
	}
}
