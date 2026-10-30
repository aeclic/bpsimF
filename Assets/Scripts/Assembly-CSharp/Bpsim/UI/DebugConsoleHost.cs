using System.ComponentModel;
using IngameDebugConsole;
using UnityEngine;

namespace Bpsim.UI
{
	/// <summary>
	/// 按 <see cref="Bpsim.DebugConsoleSettings.Enabled"/> 决定「实例化 / 销毁」整个 debug 控制台。
	/// </summary>
	/// <remarks>
	/// <para>
	/// 为什么不是「隐藏窗口」：<c>DebugLogManager</c> 只要存在，就在 <c>OnEnable</c> 里订阅
	/// <c>Application.logMessageReceivedThreaded</c>，并在每帧 <c>LateUpdate</c> 里出队日志、按
	/// <c>maxLogCount</c> 裁剪、必要时刷新列表——即使窗口和悬浮球都关着也一样（源码里那条
	/// <c>if( !isLogWindowVisible && !PopupEnabled ) return;</c> 只是跳过刷新，不是跳过捕获）。
	/// 对模拟器来说那笔固定开销不值得常驻，所以开关的语义是「组件在不在」，关掉就是真的没有对象。
	/// </para>
	/// <para>
	/// 开启时用 prefab 上已调好的序列化值把影响压到最小：只收 Warning / Error / Exception
	/// （<c>receiveInfoLogs: 0</c>）、窗口默认收起（<c>startMinimized: 1</c>）、隐藏期间日志进队列而不处理
	/// （<c>queuedLogLimit: 256</c>）、<c>maxLogCount: 2048</c> 封顶内存。控制台还带一个只在收到日志时
	/// 才出现的悬浮球，点它才展开窗口。
	/// </para>
	/// <para>
	/// 接线方式是事件驱动：只有 <c>UserSettings</c> 还没加载好时才每帧等一会儿
	/// （<c>UserSettings.Load()</c> 在 <c>CoreManager.Awake</c> 里，比这里的 AfterSceneLoad 可能更晚），
	/// 拿到 <see cref="Bpsim.DebugConsoleSettings"/> 后立刻退订 Update，之后开关变化走
	/// <c>PropertyChanged</c>，不产生每帧调用。
	/// </para>
	/// </remarks>
	public static class DebugConsoleHost
	{
		/// <summary>控制台 prefab 在 Resources 下的路径（移植时由 Tools/stage_debugconsole_prefab.py 生成）。</summary>
		private const string ResourcePath = "INDebugConsole";

		private static GameObject s_root;

		private static bool s_missingPrefabLogged;

		/// <summary>控制台是否已经存在（存在即意味着它在捕获日志）。</summary>
		public static bool Exists
		{
			get { return DebugLogManager.Instance != null; }
		}

		/// <summary>日志窗口是否展开。</summary>
		public static bool IsOpen
		{
			get
			{
				DebugLogManager manager = DebugLogManager.Instance;
				return manager != null && manager.IsLogWindowVisible;
			}
		}

		/// <summary>
		/// 悬浮球是否显示。
		/// </summary>
		/// <remarks>
		/// <c>DebugLogManager.Start()</c> 会无条件按 prefab 里的 <c>popupVisibility</c> 覆盖一次，所以这个值
		/// 只能「读回来」再同步，不能当作持久状态。prefab 里设的是 <c>Always</c>：用 <c>Never</c> 的话，
		/// 玩家点一次窗口右上角的 X 之后悬浮球就再也不出现了。
		/// </remarks>
		public static bool PopupEnabled
		{
			get
			{
				DebugLogManager manager = DebugLogManager.Instance;
				return manager != null && manager.PopupEnabled;
			}
			set
			{
				DebugLogManager manager = DebugLogManager.Instance;
				if (manager != null)
				{
					manager.PopupEnabled = value;
				}
			}
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Boot()
		{
			GameObject host = new GameObject("[Bpsim] DebugConsoleHost");
			Object.DontDestroyOnLoad(host);
			host.AddComponent<Watchdog>();
		}

		/// <summary>按开关把控制台建起来或拆掉（幂等；重复调用同一状态不做任何事）。</summary>
		public static void Apply(bool enabled)
		{
			if (enabled)
			{
				EnsureCreated();
			}
			else
			{
				DestroyConsole();
			}
		}

		/// <summary>诊断串：一次性报告「设置是否就绪 / 控制台是否已创建 / 窗口与悬浮球状态」。</summary>
		public static string Describe()
		{
			UserSettings userSettings = UserSettings.Instance;
			DebugConsoleSettings debugSettings = userSettings == null ? null : userSettings.DebugConsole;
			DebugLogManager manager = DebugLogManager.Instance;
			System.Text.StringBuilder builder = new System.Text.StringBuilder();
			builder.Append("settings=").Append(userSettings == null ? "null" : "ok");
			builder.Append(" debugSettings=").Append(debugSettings == null ? "null" : (debugSettings.Enabled ? "on" : "off"));
			builder.Append(" manager=").Append(manager == null ? "null" : "ok");
			builder.Append(" root=").Append(s_root == null ? "null" : s_root.name);
			if (manager != null)
			{
				builder.Append(" windowVisible=").Append(manager.IsLogWindowVisible);
				builder.Append(" popup=").Append(manager.PopupEnabled);
			}
			return builder.ToString();
		}

		/// <summary>手动打开日志窗口（窗口不存在时先尝试创建）。</summary>
		public static void Show()
		{
			EnsureCreated();
			DebugLogManager manager = DebugLogManager.Instance;
			if (manager != null)
			{
				manager.ShowLogWindow();
			}
		}

		/// <summary>收起日志窗口（保留悬浮球与日志捕获）。</summary>
		public static void Hide()
		{
			DebugLogManager manager = DebugLogManager.Instance;
			if (manager != null)
			{
				manager.HideLogWindow();
			}
		}

		private static void EnsureCreated()
		{
			if (DebugLogManager.Instance != null)
			{
				return;
			}
			// 编辑器里热重载脚本会把 Instance 清空，但对象还留在场景/DDOL 里。先清掉这些残留，
			// 否则新实例会在 Awake 里因为「已有实例」而 Destroy(gameObject) 自己。
			DebugLogManager[] stale = Object.FindObjectsByType<DebugLogManager>(FindObjectsSortMode.None);
			if (stale.Length > 0)
			{
				for (int i = 0; i < stale.Length; i++)
				{
					if (stale[i] != null)
					{
						Object.Destroy(stale[i].gameObject);
					}
				}
			}
			GameObject prefab = Resources.Load<GameObject>(ResourcePath);
			if (prefab == null)
			{
				if (!s_missingPrefabLogged)
				{
					s_missingPrefabLogged = true;
					Debug.LogWarning("[DebugConsoleHost] Resources/" + ResourcePath + " is missing; the debug console cannot start.");
				}
				return;
			}
			s_root = Object.Instantiate(prefab);
			s_root.name = "INDebugConsole";
			// prefab 里 startMinimized: 1 已把窗口收起；这里再显式收一次，避免这一帧闪一下。
			DebugLogManager manager = DebugLogManager.Instance;
			if (manager != null)
			{
				try
				{
					manager.HideLogWindow();
				}
				catch (System.Exception exception)
				{
					Debug.LogWarning("[DebugConsoleHost] HideLogWindow failed: " + exception.Message);
				}
			}
		}

		private static void DestroyConsole()
		{
			if (s_root != null)
			{
				Object.Destroy(s_root);
				s_root = null;
				return;
			}
			// s_root 丢了（例如被别的代码销毁过）：按类型兜底清一遍。
			DebugLogManager[] stale = Object.FindObjectsByType<DebugLogManager>(FindObjectsSortMode.None);
			for (int i = 0; i < stale.Length; i++)
			{
				if (stale[i] != null)
				{
					Object.Destroy(stale[i].gameObject);
				}
			}
		}

		/// <summary>
		/// 等 <c>UserSettings</c> 就绪后接到开关上；接好即停掉自身的每帧回调。
		/// </summary>
		private sealed class Watchdog : MonoBehaviour
		{
			private DebugConsoleSettings m_settings;

			private void Update()
			{
				// UserSettings.Load() 在 CoreManager.Awake 里，可能晚于 AfterSceneLoad。
				UserSettings userSettings = UserSettings.Instance;
				if (userSettings == null || userSettings.DebugConsole == null)
				{
					return;
				}
				if (m_settings != null)
				{
					// 已经接上了（正常情况下上一帧就 return 了）。
					return;
				}
				m_settings = userSettings.DebugConsole;
				m_settings.PropertyChanged += OnSettingChanged;
				// 之后完全由 PropertyChanged 驱动，这个 Update 不再需要跑。
				enabled = false;
				DebugConsoleHost.Apply(m_settings.Enabled);
			}

			private void OnSettingChanged(object sender, PropertyChangedEventArgs args)
			{
				if (args.PropertyName == "Enabled" && m_settings != null)
				{
					DebugConsoleHost.Apply(m_settings.Enabled);
				}
			}

			private void OnDestroy()
			{
				if (m_settings != null)
				{
					m_settings.PropertyChanged -= OnSettingChanged;
					m_settings = null;
				}
			}
		}
	}
}
