using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using Bpsim.Modding;
using UnityEngine;

namespace Bpsim.Mods
{
	/// <summary>
	/// 每个 Mod 的可调设置值（<c>Mods/ModSettings.xml</c>）。
	/// </summary>
	/// <remarks>
	/// <para>键是 Mod 的稳定 GUID（大写、无连字符，和 <c>ModData</c> 目录同一套），
	/// 所以玩家改文件夹名或重装 Mod 都不会丢设置。</para>
	/// <para>值一律按文本存：清单里声明的类型（<see cref="ModSettingSpec"/>）负责解释，
	/// 面板写入时已经过 <see cref="ModSettingSpec.Sanitize"/> 归一化。</para>
	/// <para>格式：</para>
	/// <code>
	/// &lt;ModSettings version="1"&gt;
	///   &lt;Mod guid="E47B018A61A64E79A789C5F627A00AA4"&gt;
	///     &lt;Value key="autoSave"&gt;true&lt;/Value&gt;
	///   &lt;/Mod&gt;
	/// &lt;/ModSettings&gt;
	/// </code>
	/// </remarks>
	public static class ModSettingsStore
	{
		public const string FileName = "ModSettings.xml";

		private static readonly Dictionary<string, Dictionary<string, string>> s_values =
			new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

		private static bool s_loaded;
		private static bool s_dirty;

		/// <summary>任意一项值改变时触发（参数：Mod 存储键、设置键）。</summary>
		public static event Action<string, string> Changed;

		/// <summary>第一次访问时从磁盘读入（失败只记日志，不抛异常）。</summary>
		public static void EnsureLoaded()
		{
			if (s_loaded)
			{
				return;
			}
			s_loaded = true;
			string path = ModPaths.ModSettingsPath;
			try
			{
				if (!File.Exists(path))
				{
					return;
				}
				XDocument doc = XDocument.Load(path);
				XElement root = doc.Root;
				if (root == null)
				{
					return;
				}
				foreach (XElement mod in root.Elements("Mod"))
				{
					string key = (string)mod.Attribute("guid");
					if (string.IsNullOrWhiteSpace(key))
					{
						key = (string)mod.Attribute("id");
					}
					if (string.IsNullOrWhiteSpace(key))
					{
						continue;
					}
					Dictionary<string, string> map = ValuesOf(key, create: true);
					foreach (XElement value in mod.Elements("Value"))
					{
						string name = (string)value.Attribute("key");
						if (string.IsNullOrWhiteSpace(name))
						{
							continue;
						}
						map[name] = value.Value ?? string.Empty;
					}
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning("[Mods] 读取 " + FileName + " 失败：" + ex.Message);
			}
		}

		/// <summary>该 Mod 是否已经存过任何设置。</summary>
		public static bool HasValues(string modKey)
		{
			EnsureLoaded();
			return !string.IsNullOrEmpty(modKey) && s_values.TryGetValue(modKey, out Dictionary<string, string> map)
				&& map.Count > 0;
		}

		/// <summary>读一项原始文本；没有时返回 <paramref name="fallback"/>。</summary>
		public static string GetText(string modKey, string settingKey, string fallback)
		{
			EnsureLoaded();
			if (string.IsNullOrEmpty(modKey) || string.IsNullOrEmpty(settingKey))
			{
				return fallback;
			}
			if (s_values.TryGetValue(modKey, out Dictionary<string, string> map)
				&& map.TryGetValue(settingKey, out string value))
			{
				return value;
			}
			return fallback;
		}

		/// <summary>写一项原始文本（不落盘，<see cref="Save"/> 才写）。</summary>
		public static void SetText(string modKey, string settingKey, string value)
		{
			EnsureLoaded();
			if (string.IsNullOrEmpty(modKey) || string.IsNullOrEmpty(settingKey))
			{
				return;
			}
			Dictionary<string, string> map = ValuesOf(modKey, create: true);
			string previous = map.TryGetValue(settingKey, out string old) ? old : null;
			if (string.Equals(previous, value, StringComparison.Ordinal))
			{
				return;
			}
			map[settingKey] = value ?? string.Empty;
			s_dirty = true;
			Changed?.Invoke(modKey, settingKey);
		}

		/// <summary>删掉一个 Mod 的全部设置（卸载 Mod 时调用）。</summary>
		public static void Clear(string modKey)
		{
			EnsureLoaded();
			if (string.IsNullOrEmpty(modKey) || !s_values.Remove(modKey))
			{
				return;
			}
			s_dirty = true;
			Save();
		}

		/// <summary>把当前值写回磁盘（无改动时是空操作）。</summary>
		public static void Save()
		{
			if (!s_dirty)
			{
				return;
			}
			s_dirty = false;
			try
			{
				string path = ModPaths.ModSettingsPath;
				ModPaths.EnsureDirectory(Path.GetDirectoryName(path));
				XElement root = new XElement("ModSettings", new XAttribute("version", 1));
				foreach (KeyValuePair<string, Dictionary<string, string>> mod in s_values)
				{
					if (mod.Value == null || mod.Value.Count == 0)
					{
						continue;
					}
					XElement node = new XElement("Mod", new XAttribute("guid", mod.Key));
					foreach (KeyValuePair<string, string> value in mod.Value)
					{
						node.Add(new XElement("Value", new XAttribute("key", value.Key), value.Value ?? string.Empty));
					}
					root.Add(node);
				}
				new XDocument(root).Save(path);
			}
			catch (Exception ex)
			{
				Debug.LogWarning("[Mods] 写入 " + FileName + " 失败：" + ex.Message);
			}
		}

		/// <summary>设置存储键：优先 GUID，没有则退回 Mod id。</summary>
		public static string KeyFor(ModPackage pkg)
		{
			if (pkg == null)
			{
				return null;
			}
			if (pkg.Guid != Guid.Empty && pkg.Manifest != null && pkg.Manifest.guid != Guid.Empty)
			{
				return pkg.Guid.ToString("N").ToUpperInvariant();
			}
			return pkg.Id;
		}

		/// <summary>按清单声明的类型读出该设置（未存过则用清单默认值）。</summary>
		public static string GetText(ModPackage pkg, ModSettingSpec spec)
		{
			if (spec == null)
			{
				return null;
			}
			string key = KeyFor(pkg);
			string fallback = spec.Sanitize(spec.value);
			string raw = GetText(key, spec.key, null);
			return raw == null ? fallback : spec.Sanitize(raw);
		}

		public static bool GetBool(ModPackage pkg, string settingKey, bool fallback)
		{
			string raw = GetText(KeyFor(pkg), settingKey, null);
			if (string.IsNullOrWhiteSpace(raw))
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

		public static int GetInt(ModPackage pkg, string settingKey, int fallback)
		{
			string raw = GetText(KeyFor(pkg), settingKey, null);
			return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;
		}

		public static float GetFloat(ModPackage pkg, string settingKey, float fallback)
		{
			string raw = GetText(KeyFor(pkg), settingKey, null);
			return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
				&& !float.IsNaN(value) && !float.IsInfinity(value) ? value : fallback;
		}

		public static string GetString(ModPackage pkg, string settingKey, string fallback)
		{
			return GetText(KeyFor(pkg), settingKey, fallback);
		}

		private static Dictionary<string, string> ValuesOf(string modKey, bool create)
		{
			if (s_values.TryGetValue(modKey, out Dictionary<string, string> map))
			{
				return map;
			}
			if (!create)
			{
				return null;
			}
			map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			s_values[modKey] = map;
			return map;
		}
	}
}
