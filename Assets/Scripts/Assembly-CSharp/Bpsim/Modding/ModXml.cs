using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace Bpsim.Modding
{
	/// <summary>
	/// <c>Mod.xml</c> 清单的解析器（格式抄 Besiege）。一个包只要有 <c>Mod.xml</c> 就能被识别，
	/// 不需要 <c>manifest.json</c>。
	/// </summary>
	/// <remarks>
	/// <para>XML 结构（除 <c>Name</c> 外全部可选）：</para>
	/// <code>
	/// &lt;Mod&gt;
	///   &lt;Name&gt;EasyScale&lt;/Name&gt;
	///   &lt;Author&gt;someone&lt;/Author&gt;
	///   &lt;Version&gt;1.0.0&lt;/Version&gt;
	///   &lt;Description&gt;一句话简介&lt;/Description&gt;
	///   &lt;Icon name="icon" path="icon.png" /&gt;
	///   &lt;MultiplayerCompatible&gt;true&lt;/MultiplayerCompatible&gt;
	///   &lt;MinGameVersion&gt;0.0.12&lt;/MinGameVersion&gt;
	///   &lt;ID&gt;bb7ba333-e72b-49a8-8c0d-011a3fcadaf3&lt;/ID&gt;
	///   &lt;EntryPoint type="MyMod.MyEntryPoint" /&gt;
	///   &lt;Assemblies&gt;
	///     &lt;Assembly path="EasyScale.dll" /&gt;
	///   &lt;/Assemblies&gt;
	///   &lt;Resources&gt;
	///     &lt;Texture name="icon" path="icon.png" /&gt;
	///     &lt;Panel id="player" title="播放器" html="panel.html" width="460" height="680" /&gt;
	///   &lt;/Resources&gt;
	///   &lt;Settings&gt;
	///     &lt;Setting key="autoSave" title="自动保存" type="bool" default="true" /&gt;
	///     &lt;Setting key="gridSize" title="网格边长" type="int" default="4" min="1" max="32" /&gt;
	///     &lt;Setting key="mode" title="模式" type="enum" default="fast"&gt;
	///       &lt;Option value="fast" label="快速" /&gt;
	///     &lt;/Setting&gt;
	///   &lt;/Settings&gt;
	/// &lt;/Mod&gt;
	/// </code>
	/// <para>解析是宽松的：认不得的元素忽略，缺字段用目录名兜底，绝不因为一个拼错的标签整包失败。</para>
	/// </remarks>
	public static class ModXml
	{
		/// <summary>清单文件名。</summary>
		public const string FileName = "Mod.xml";

		/// <summary>解析一个 <c>Mod.xml</c>。失败时 <paramref name="error"/> 给出可读原因。</summary>
		public static bool TryParse(string xmlPath, string directoryName, out ModManifest manifest, out string error)
		{
			manifest = null;
			error = null;
			try
			{
				if (!File.Exists(xmlPath))
				{
					error = "找不到 " + FileName;
					return false;
				}
				XDocument doc = XDocument.Load(xmlPath);
				XElement root = doc.Root;
				if (root == null)
				{
					error = "Mod.xml 是空的";
					return false;
				}
				manifest = FromElement(root, directoryName);
				return true;
			}
			catch (Exception ex)
			{
				error = "Mod.xml 解析失败：" + ex.Message;
				return false;
			}
		}

		/// <summary>把 <c>&lt;Mod&gt;</c> 元素转成内存清单。</summary>
		public static ModManifest FromElement(XElement root, string directoryName)
		{
			ModManifest m = new ModManifest();
			m.name = Value(root, "Name");
			m.author = Value(root, "Author");
			m.version = Value(root, "Version");
			m.description = Value(root, "Description");
			m.minGameVersion = Value(root, "MinGameVersion");

			string guidText = Value(root, "ID");
			string fallbackId = !string.IsNullOrWhiteSpace(directoryName) ? directoryName : m.name;
			m.id = SanitizeId(fallbackId);
			m.guid = ModGuid.Parse(guidText, m.id);

			string multiplayer = Value(root, "MultiplayerCompatible");
			m.multiplayerSafe = ParseBool(multiplayer, true);

			XElement icon = Child(root, "Icon");
			if (icon != null)
			{
				m.icon = Attribute(icon, "path");
			}

			// 程序集：<Assemblies><Assembly path="x.dll"/></Assemblies>，也接受直接写 <Assembly path=".."/>
			List<string> assemblies = new List<string>();
			XElement assembliesNode = Child(root, "Assemblies");
			CollectAssemblies(assembliesNode, assemblies);
			CollectAssemblies(root, assemblies);

			// 入口类型：<EntryPoint type="Ns.Type"/> 或 <EntryPoint>Ns.Type</EntryPoint>
			XElement entry = Child(root, "EntryPoint");
			if (entry != null)
			{
				m.entryType = Attribute(entry, "type");
				if (string.IsNullOrWhiteSpace(m.entryType))
				{
					m.entryType = entry.Value != null ? entry.Value.Trim() : null;
				}
			}

			// 面板：<Resources><Panel .../></Resources> 或 <Panels><Panel .../></Panels>
			List<ModPanelSpec> panels = new List<ModPanelSpec>();
			CollectPanels(Child(root, "Resources"), panels);
			CollectPanels(Child(root, "Panels"), panels);

			// 可调设置：<Settings><Setting .../></Settings>（也接受 <Resources>/<Config> 下直接写）
			List<ModSettingSpec> settings = new List<ModSettingSpec>();
			CollectSettings(Child(root, "Settings"), settings);
			CollectSettings(Child(root, "Resources"), settings);
			CollectSettings(Child(root, "Config"), settings);

			// 标签
			XElement tagsNode = Child(root, "Tags");
			if (tagsNode != null)
			{
				foreach (XElement tag in tagsNode.Elements("Tag"))
				{
					if (!string.IsNullOrWhiteSpace(tag.Value))
					{
						m.tags.Add(tag.Value.Trim());
					}
				}
			}

			m.panels = panels;
			m.settings = settings;
			if (assemblies.Count > 0)
			{
				m.entryAssembly = assemblies[0];
				m.assemblies = assemblies;
				m.type = panels.Count > 0 ? ModPackageType.Mixed : ModPackageType.Plugin;
			}
			else
			{
				m.type = ModPackageType.Panel;
			}
			m.schema = 2;
			m.Normalize();
			return m;
		}

		/// <summary>把任意字符串规整成可用作目录名/存档 key 的 id。</summary>
		public static string SanitizeId(string raw)
		{
			if (string.IsNullOrWhiteSpace(raw))
			{
				return "unnamed-mod";
			}
			char[] buffer = new char[raw.Length];
			int n = 0;
			bool lastDash = false;
			foreach (char c in raw.Trim().ToLowerInvariant())
			{
				bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-';
				char outChar = ok ? c : '-';
				if (outChar == '-' && lastDash)
				{
					continue;
				}
				lastDash = outChar == '-';
				buffer[n++] = outChar;
			}
			string result = new string(buffer, 0, n).Trim('-', '.');
			return string.IsNullOrEmpty(result) ? "unnamed-mod" : result;
		}

		private static void CollectAssemblies(XElement node, List<string> into)
		{
			if (node == null)
			{
				return;
			}
			foreach (XElement asm in node.Elements("Assembly"))
			{
				string path = Attribute(asm, "path");
				if (string.IsNullOrWhiteSpace(path))
				{
					path = asm.Value != null ? asm.Value.Trim() : null;
				}
				if (!string.IsNullOrWhiteSpace(path) && !into.Contains(path))
				{
					into.Add(path);
				}
			}
		}

		private static void CollectPanels(XElement node, List<ModPanelSpec> into)
		{
			if (node == null)
			{
				return;
			}
			foreach (XElement panel in node.Elements("Panel"))
			{
				ModPanelSpec spec = new ModPanelSpec();
				spec.id = Attribute(panel, "id");
				spec.title = Attribute(panel, "title");
				spec.html = Attribute(panel, "html");
				spec.css = Attribute(panel, "css");
				spec.js = Attribute(panel, "js");
				spec.icon = Attribute(panel, "icon");
				spec.anchor = Attribute(panel, "anchor");
				spec.width = ParseFloat(Attribute(panel, "width"), 420f);
				spec.height = ParseFloat(Attribute(panel, "height"), 640f);
				spec.autoOpen = ParseBool(Attribute(panel, "autoOpen"), false);
				into.Add(spec);
			}
		}

		/// <summary>
		/// 收集 <c>&lt;Setting&gt;</c> 声明。接受两种形状：
		/// <c>type</c> 属性（bool/int/float/string/enum）或 <c>kind</c> 别名；
		/// enum 的候选写成子元素 <c>&lt;Option value=".." label=".."/&gt;</c>，
		/// 也接受 <c>options="a,b,c"</c> 这种逗号串。认不出的键忽略，不让整包失败。
		/// </summary>
		private static void CollectSettings(XElement node, List<ModSettingSpec> into)
		{
			if (node == null)
			{
				return;
			}
			// 也接受包了一层：<Resources><Settings><Setting .../></Settings></Resources>
			XElement nested = node.Element("Settings");
			if (nested != null)
			{
				CollectSettings(nested, into);
			}
			foreach (XElement element in node.Elements("Setting"))
			{
				ModSettingSpec spec = new ModSettingSpec();
				spec.key = Attribute(element, "key");
				if (string.IsNullOrWhiteSpace(spec.key))
				{
					spec.key = Attribute(element, "name");
				}
				spec.title = Attribute(element, "title");
				if (string.IsNullOrWhiteSpace(spec.title))
				{
					spec.title = Value(element, "Title");
				}
				spec.description = Attribute(element, "description");
				if (string.IsNullOrWhiteSpace(spec.description))
				{
					spec.description = Value(element, "Description");
				}
				string type = Attribute(element, "type");
				if (string.IsNullOrWhiteSpace(type))
				{
					type = Attribute(element, "kind");
				}
				if (!string.IsNullOrWhiteSpace(type))
				{
					spec.type = type.Trim().ToLowerInvariant();
				}
				spec.value = Attribute(element, "default");
				if (spec.value == null)
				{
					spec.value = Attribute(element, "value");
				}
				spec.min = ParseNumber(Attribute(element, "min"), 0f);
				spec.max = ParseNumber(Attribute(element, "max"), 0f);

				foreach (XElement option in element.Elements("Option"))
				{
					ModSettingOption candidate = new ModSettingOption();
					candidate.value = Attribute(option, "value");
					if (string.IsNullOrWhiteSpace(candidate.value))
					{
						candidate.value = option.Value != null ? option.Value.Trim() : null;
					}
					candidate.label = Attribute(option, "label");
					spec.options.Add(candidate);
				}
				string inlineOptions = Attribute(element, "options");
				if (!string.IsNullOrWhiteSpace(inlineOptions))
				{
					foreach (string part in inlineOptions.Split(','))
					{
						if (string.IsNullOrWhiteSpace(part))
						{
							continue;
						}
						ModSettingOption candidate = new ModSettingOption();
						// 支持 "fast=快速" 形式。
						int split = part.IndexOf('=');
						if (split > 0)
						{
							candidate.value = part.Substring(0, split).Trim();
							candidate.label = part.Substring(split + 1).Trim();
						}
						else
						{
							candidate.value = part.Trim();
						}
						spec.options.Add(candidate);
					}
				}
				into.Add(spec);
			}
		}

		private static XElement Child(XElement parent, string name)
		{
			return parent != null ? parent.Element(name) : null;
		}

		private static string Value(XElement parent, string name)
		{
			XElement e = Child(parent, name);
			return e != null && e.Value != null ? e.Value.Trim() : null;
		}

		private static string Attribute(XElement e, string name)
		{
			XAttribute a = e != null ? e.Attribute(name) : null;
			return a != null ? a.Value : null;
		}

		private static bool ParseBool(string text, bool fallback)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				return fallback;
			}
			switch (text.Trim().ToLowerInvariant())
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

		private static float ParseFloat(string text, float fallback)
		{
			if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && v > 0f)
			{
				return v;
			}
			return fallback;
		}

		/// <summary>带区间约束的数值属性：允许 0 和负数（不同于只收正数的 <see cref="ParseFloat"/>）。</summary>
		private static float ParseNumber(string text, float fallback)
		{
			if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)
				&& !float.IsNaN(v) && !float.IsInfinity(v))
			{
				return v;
			}
			return fallback;
		}
	}
}
