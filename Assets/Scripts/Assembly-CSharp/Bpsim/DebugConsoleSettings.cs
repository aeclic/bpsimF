namespace Bpsim
{
	/// <summary>
	/// Debug 控制台（设置面板 → 调试）。
	/// </summary>
	/// <remarks>
	/// <para>
	/// 开关决定「控制台组件是否存在」，而不只是「窗口是否可见」：<see cref="Bpsim.UI.DebugConsoleHost"/>
	/// 在关闭时把整个控制台对象销毁，开启时才实例化。这不是洁癖——<c>DebugLogManager</c> 一旦存在就会订阅
	/// <c>Application.logMessageReceivedThreaded</c> 并每帧跑 <c>LateUpdate</c>（哪怕窗口与悬浮球都关着，
	/// 仍要出队日志、按 <c>maxLogCount</c> 裁剪），那笔开销对模拟器来说不便宜，所以默认关闭就必须是真的没有。
	/// </para>
	/// <para>
	/// 开启后的额外开销由 prefab 上的序列化值控制：只接收 Warning / Error / Exception
	/// （<c>receiveInfoLogs: 0</c>），隐藏期间日志进队列但不处理（<c>queuedLogLimit: 256</c>）。
	/// </para>
	/// </remarks>
	public class DebugConsoleSettings : SettingsBase
	{
		private bool m_enabled;

		/// <summary>是否启用 debug 控制台（实例化 <c>Resources/INDebugConsole</c>）。</summary>
		public bool Enabled
		{
			get
			{
				return m_enabled;
			}
			set
			{
				SetProperty(ref m_enabled, value, "Enabled");
			}
		}

		public DebugConsoleSettings()
		{
			Enabled = false;
		}

		public void Update(DebugConsoleSettings settings)
		{
			if (settings != null)
			{
				Enabled = settings.Enabled;
			}
		}
	}
}
