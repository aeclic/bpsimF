using System;
using Bpsim.Modding;
using UnityEngine;

namespace Bpsim.Mods
{
	/// <summary><see cref="IModContext"/> 实现：每个已加载插件拿到一个，绑定到自己的包。</summary>
	internal sealed class ModContext : IModContext
	{
		private readonly ModPackage m_pkg;
		private readonly ModManager m_manager;

		private ModLogger m_log;

		private ModUiService m_ui;

		public ModContext(ModPackage pkg, ModManager manager)
		{
			m_pkg = pkg;
			m_manager = manager;
		}

		public string ModDirectory => m_pkg?.Directory;

		public ModManifest Manifest => m_pkg?.Manifest;

		public IModLogger Log => m_log ??= new ModLogger(m_pkg != null ? m_pkg.Id : "mod");

		public IModUI Ui => m_ui ??= new ModUiService(m_pkg, m_manager);
	}

	/// <summary>带 Mod id 前缀的日志。</summary>
	internal sealed class ModLogger : IModLogger
	{
		private readonly string m_prefix;

		public ModLogger(string id)
		{
			m_prefix = "[Mod:" + id + "] ";
		}

		public void Info(string message) => Debug.Log(m_prefix + message);

		public void Warn(string message) => Debug.LogWarning(m_prefix + message);

		public void Error(string message) => Debug.LogError(m_prefix + message);
	}

	/// <summary><see cref="IModUI"/> 实现：把 Mod 的 UI 请求转接到宿主服务。</summary>
	internal sealed class ModUiService : IModUI
	{
		private readonly ModPackage m_pkg;
		private readonly ModManager m_manager;

		public ModUiService(ModPackage pkg, ModManager manager)
		{
			m_pkg = pkg;
			m_manager = manager;
		}

		public void AddQuickButton(string label, Action onClick)
		{
			m_manager?.AddQuickButton(m_pkg?.Id, label, onClick);
		}

		public void OpenPanel(string panelId)
		{
			ModPanelSpec spec = FindSpec(panelId);
			if (spec == null)
			{
				Debug.LogWarning("[Mod:" + m_pkg?.Id + "] 未声明的面板：" + panelId);
				return;
			}
			ModPanelService.Open(m_pkg, spec);
		}

		public void ClosePanel(string panelId)
		{
			if (m_pkg != null)
			{
				ModPanelService.Close(m_pkg.Id + ":" + panelId);
			}
		}

		public void Toast(string message)
		{
			m_manager?.Toast(message);
		}

		private ModPanelSpec FindSpec(string panelId)
		{
			if (m_pkg?.Manifest?.panels == null)
			{
				return null;
			}
			foreach (ModPanelSpec s in m_pkg.Manifest.panels)
			{
				if (string.Equals(s.id, panelId, StringComparison.OrdinalIgnoreCase))
				{
					return s;
				}
			}
			return null;
		}
	}
}
