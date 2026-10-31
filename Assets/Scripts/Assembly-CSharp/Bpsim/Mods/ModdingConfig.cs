using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Bpsim.Modding;
using Newtonsoft.Json;
using UnityEngine;

namespace Bpsim.Mods
{
	/// <summary>
	/// 全局 Mod 配置 <c>Mods/Modding.xml</c>（格式抄 Besiege 的 <c>Modding.xml</c>）。
	/// 只存三类东西：哪些 Mod 被禁用、上次运行的游戏版本、以及扫描器严格模式开关。
	/// </summary>
	/// <remarks>
	/// <para>旧版用的是 <c>mods.settings.json</c>。这里会自动迁移一次：老配置里「没被显式启用」的
	/// Mod 一律按禁用处理，避免升级后代码 Mod 被静默放行。</para>
	/// <para>游戏版本变化时（<see cref="Maintenance"/>）会像 Besiege 一样**全部禁用**，
	/// 防止更新后旧 Mod 把游戏打崩 —— 玩家确认没问题再逐个打开。</para>
	/// </remarks>
	public sealed class ModdingConfig
	{
		public const string FileName = "Modding.xml";

		private const string KeyLastGameVersion = "maintenance-lastGameVersion";
		private const string KeyLastMods = "maintenance-lastMods";
		private const string KeyDisabled = "disabled-mods";
		private const string KeyStrict = "strict-scan";
		private const string KeyAdditionalDirs = "additional-dirs";

		/// <summary>上次运行的游戏版本。</summary>
		public string LastGameVersion = string.Empty;

		/// <summary>上次运行时装了哪些 Mod（<c>GUID~来源~工坊id~版本~名字</c>），只用于展示与排查。</summary>
		public readonly List<string> LastMods = new List<string>();

		/// <summary>被玩家禁用的 Mod GUID（大写带连字符）。</summary>
		public readonly HashSet<string> Disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>扫描器严格模式：开启后 IL 启发式命中也会拒绝加载。默认关闭。</summary>
		public bool StrictScan;

		/// <summary>额外扫描目录（绝对路径或相对 persistentDataPath）。</summary>
		public readonly List<string> AdditionalDirs = new List<string>();

		/// <summary>是否刚从 <c>mods.settings.json</c> 迁移过来。</summary>
		public bool MigratedFromLegacy;

		/// <summary>迁移时老配置里被显式启用的 id。</summary>
		public readonly HashSet<string> LegacyEnabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>读取配置；文件不存在或损坏时返回默认值（不抛异常）。</summary>
		public static ModdingConfig Load(string path)
		{
			ModdingConfig cfg = new ModdingConfig();
			try
			{
				if (File.Exists(path))
				{
					XDocument doc = XDocument.Load(path);
					XElement root = doc.Root;
					if (root != null)
					{
						cfg.LastGameVersion = ReadString(root, KeyLastGameVersion) ?? string.Empty;
						ReadArray(root, KeyLastMods, cfg.LastMods);
						foreach (string guid in ReadArray(root, KeyDisabled, null))
						{
							cfg.Disabled.Add(guid);
						}
						cfg.StrictScan = ReadBool(root, KeyStrict, false);
						ReadArray(root, KeyAdditionalDirs, cfg.AdditionalDirs);
					}
					return cfg;
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning("[Mods] 读取 " + FileName + " 失败：" + ex.Message);
			}
			cfg.MigrateLegacy();
			return cfg;
		}

		/// <summary>写入配置；失败只记日志。</summary>
		public void Save(string path)
		{
			try
			{
				ModPaths.EnsureDirectory(Path.GetDirectoryName(path));
				XElement root = new XElement("Configuration", new XAttribute("version", 2));
				root.Add(new XElement("String", new XAttribute("key", KeyLastGameVersion), LastGameVersion ?? string.Empty));
				root.Add(BuildArray(KeyLastMods, LastMods));
				root.Add(BuildArray(KeyDisabled, Disabled));
				root.Add(new XElement("Bool", new XAttribute("key", KeyStrict), StrictScan ? "true" : "false"));
				root.Add(BuildArray(KeyAdditionalDirs, AdditionalDirs));
				new XDocument(root).Save(path);
			}
			catch (Exception ex)
			{
				Debug.LogWarning("[Mods] 写入 " + FileName + " 失败：" + ex.Message);
			}
		}

		/// <summary>这个 Mod 现在是否被禁用。</summary>
		public bool IsDisabled(Guid guid, string id)
		{
			if (guid != Guid.Empty && Disabled.Contains(ModGuid.ToStorage(guid)))
			{
				return true;
			}
			if (MigratedFromLegacy)
			{
				// 迁移期：老配置里没显式启用过的，一律当禁用（保守）。
				return string.IsNullOrEmpty(id) || !LegacyEnabled.Contains(id);
			}
			return false;
		}

		/// <summary>设置启用状态并同步到禁用集合。</summary>
		public void SetEnabled(Guid guid, bool enabled)
		{
			string key = ModGuid.ToStorage(guid);
			if (string.IsNullOrEmpty(key))
			{
				return;
			}
			if (enabled)
			{
				Disabled.Remove(key);
			}
			else
			{
				Disabled.Add(key);
			}
		}

		/// <summary>
		/// 把「迁移期」的隐式状态固化成显式的禁用集合。必须在第一次 Rescan 之后、玩家操作之前调用，
		/// 否则玩家手动启用的 Mod 会被迁移规则重新判成禁用。
		/// </summary>
		public void MaterializeLegacy(IEnumerable<ModPackage> packages)
		{
			if (!MigratedFromLegacy)
			{
				return;
			}
			if (packages != null)
			{
				foreach (ModPackage pkg in packages)
				{
					if (pkg == null || pkg.Manifest == null)
					{
						continue;
					}
					if (!LegacyEnabled.Contains(pkg.Id))
					{
						Disabled.Add(ModGuid.ToStorage(pkg.Manifest.guid));
					}
				}
			}
			MigratedFromLegacy = false;
			LegacyEnabled.Clear();
		}

		/// <summary>
		/// 维护检查：游戏版本变了就把所有 Mod 标记为禁用（Besiege 同款策略）。
		/// 返回 true 表示发生了「版本变化 → 全禁用」，调用方应提示玩家。
		/// </summary>
		public bool Maintenance(string gameVersion, IEnumerable<ModPackage> packages)
		{
			bool versionChanged = !string.IsNullOrEmpty(LastGameVersion)
				&& !string.Equals(LastGameVersion, gameVersion, StringComparison.OrdinalIgnoreCase);

			if (versionChanged)
			{
				foreach (ModPackage pkg in packages)
				{
					if (pkg != null && pkg.Manifest != null)
					{
						SetEnabled(pkg.Manifest.guid, false);
					}
				}
			}

			LastGameVersion = gameVersion ?? string.Empty;
			LastMods.Clear();
			foreach (ModPackage pkg in packages)
			{
				if (pkg != null && pkg.Manifest != null)
				{
					LastMods.Add(ModGuid.ToStorage(pkg.Manifest.guid)
						+ "~" + SourceTag(pkg.Source)
						+ "~" + (pkg.WorkshopId ?? string.Empty)
						+ "~" + pkg.Version
						+ "~" + pkg.DisplayName);
				}
			}
			return versionChanged;
		}

		private static string SourceTag(ModSource source)
		{
			switch (source)
			{
			case ModSource.BuiltIn:
				return "B";
			case ModSource.Workshop:
				return "W";
			case ModSource.Additional:
				return "A";
			default:
				return "L";
			}
		}

		/// <summary>把旧的 <c>mods.settings.json</c> 读进来，只读一次。</summary>
		private void MigrateLegacy()
		{
			string legacyPath = Path.Combine(ModPaths.Root, "mods.settings.json");
			if (!File.Exists(legacyPath))
			{
				return;
			}
			try
			{
				LegacyModSettings legacy = JsonConvert.DeserializeObject<LegacyModSettings>(File.ReadAllText(legacyPath));
				if (legacy == null)
				{
					return;
				}
				MigratedFromLegacy = true;
				if (legacy.enabled != null)
				{
					foreach (string id in legacy.enabled)
					{
						LegacyEnabled.Add(id);
					}
				}
				if (legacy.disabled != null)
				{
					foreach (string id in legacy.disabled)
					{
						LegacyEnabled.Remove(id);
					}
				}
				Debug.Log("[Mods] 已从 mods.settings.json 迁移：" + LegacyEnabled.Count + " 个 Mod 保持启用。");
			}
			catch (Exception ex)
			{
				Debug.LogWarning("[Mods] 迁移旧设置失败：" + ex.Message);
			}
		}

		private static XElement BuildArray(string key, IEnumerable<string> values)
		{
			XElement node = new XElement("StringArray", new XAttribute("key", key));
			if (values != null)
			{
				foreach (string v in values)
				{
					node.Add(new XElement("String", v));
				}
			}
			return node;
		}

		private static string ReadString(XElement root, string key)
		{
			foreach (XElement e in root.Elements("String"))
			{
				if (string.Equals((string)e.Attribute("key"), key, StringComparison.OrdinalIgnoreCase))
				{
					return e.Value;
				}
			}
			return null;
		}

		private static bool ReadBool(XElement root, string key, bool fallback)
		{
			foreach (XElement e in root.Elements("Bool"))
			{
				if (string.Equals((string)e.Attribute("key"), key, StringComparison.OrdinalIgnoreCase))
				{
					return string.Equals(e.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase);
				}
			}
			string raw = ReadString(root, key);
			if (string.IsNullOrWhiteSpace(raw))
			{
				return fallback;
			}
			return string.Equals(raw.Trim(), "true", StringComparison.OrdinalIgnoreCase);
		}

		private static IEnumerable<string> ReadArray(XElement root, string key, List<string> into)
		{
			List<string> result = into ?? new List<string>();
			foreach (XElement e in root.Elements("StringArray"))
			{
				if (!string.Equals((string)e.Attribute("key"), key, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				foreach (XElement item in e.Elements("String"))
				{
					if (!string.IsNullOrWhiteSpace(item.Value))
					{
						result.Add(item.Value.Trim());
					}
				}
			}
			return result;
		}

		/// <summary>旧 <c>mods.settings.json</c> 的形状，只用于迁移。</summary>
		[Serializable]
		private sealed class LegacyModSettings
		{
			public List<string> enabled = new List<string>();

			public List<string> disabled = new List<string>();
		}
	}
}
