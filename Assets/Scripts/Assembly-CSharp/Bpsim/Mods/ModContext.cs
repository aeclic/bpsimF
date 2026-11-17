using System;
using System.Collections.Generic;
using System.Globalization;
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

		private ModSettingsService m_settings;

		public ModContext(ModPackage pkg, ModManager manager)
		{
			m_pkg = pkg;
			m_manager = manager;
		}

		public string ModDirectory => m_pkg?.Directory;

		public ModManifest Manifest => m_pkg?.Manifest;

		public IModLogger Log => m_log ??= new ModLogger(m_pkg != null ? m_pkg.Id : "mod");

		public IModUI Ui => m_ui ??= new ModUiService(m_pkg, m_manager);

		public IModSettings Settings => m_settings ??= new ModSettingsService(m_pkg);
	}

	/// <summary>
	/// <see cref="IModSettings"/> 实现：把读写接到 <see cref="ModSettingsStore"/>，
	/// 写值时按清单声明的类型归一化并立即落盘。
	/// </summary>
	/// <remarks>
	/// 只有外部真的订阅了 <see cref="Changed"/> 才挂到存储的全局事件上，
	/// 否则每次重载 Mod 都会在静态事件里留一份订阅（ModContext 每次加载都新建）。
	/// </remarks>
	internal sealed class ModSettingsService : IModSettings
	{
		private readonly ModPackage m_pkg;
		private readonly string m_key;
		private Action<string> m_changed;
		private bool m_subscribed;

		public ModSettingsService(ModPackage pkg)
		{
			m_pkg = pkg;
			m_key = ModSettingsStore.KeyFor(pkg);
		}

		public IReadOnlyList<ModSettingSpec> Specs
		{
			get
			{
				ModManifest manifest = m_pkg?.Manifest;
				return manifest?.settings ?? (IReadOnlyList<ModSettingSpec>)Array.Empty<ModSettingSpec>();
			}
		}

		public event Action<string> Changed
		{
			add
			{
				m_changed += value;
				if (!m_subscribed)
				{
					m_subscribed = true;
					ModSettingsStore.Changed += OnStoreChanged;
				}
			}
			remove
			{
				m_changed -= value;
				if (m_subscribed && m_changed == null)
				{
					m_subscribed = false;
					ModSettingsStore.Changed -= OnStoreChanged;
				}
			}
		}

		public bool GetBool(string key, bool fallback = false)
		{
			string raw = Read(key);
			if (raw == null)
			{
				return fallback;
			}
			switch (raw.Trim().ToLowerInvariant())
			{
			case "true":
			case "1":
			case "yes":
			case "on":
				return true;
			case "false":
			case "0":
			case "no":
			case "off":
				return false;
			default:
				return fallback;
			}
		}

		public int GetInt(string key, int fallback = 0)
		{
			string raw = Read(key);
			return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;
		}

		public float GetFloat(string key, float fallback = 0f)
		{
			string raw = Read(key);
			return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
				&& !float.IsNaN(value) && !float.IsInfinity(value) ? value : fallback;
		}

		public string GetString(string key, string fallback = null)
		{
			return Read(key) ?? fallback;
		}

		public void Set(string key, string value)
		{
			ModSettingSpec spec = FindSpec(key);
			ModSettingsStore.SetText(m_key, spec?.key ?? key, spec != null ? spec.Sanitize(value) : value);
			ModSettingsStore.Save();
		}

		/// <summary>读原始值；已声明的项额外套一层类型归一化（手改坏了也能回默认）。</summary>
		private string Read(string key)
		{
			ModSettingSpec spec = FindSpec(key);
			if (spec == null)
			{
				return ModSettingsStore.GetText(m_key, key, null);
			}
			return ModSettingsStore.GetText(m_pkg, spec);
		}

		private ModSettingSpec FindSpec(string key)
		{
			List<ModSettingSpec> specs = m_pkg?.Manifest?.settings;
			if (specs == null || string.IsNullOrEmpty(key))
			{
				return null;
			}
			foreach (ModSettingSpec spec in specs)
			{
				if (spec != null && string.Equals(spec.key, key, StringComparison.OrdinalIgnoreCase))
				{
					return spec;
				}
			}
			return null;
		}

		private void OnStoreChanged(string modKey, string settingKey)
		{
			if (string.Equals(modKey, m_key, StringComparison.OrdinalIgnoreCase))
			{
				m_changed?.Invoke(settingKey);
			}
		}
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
