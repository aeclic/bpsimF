using System;
using System.Collections.Generic;

namespace Bpsim.Modding
{
	/// <summary>
	/// Mod 包清单（<c>manifest.json</c>）。安装到 <c>&lt;persistentDataPath&gt;/Mods/&lt;id&gt;/</c> 后，
	/// 由 <c>Bpsim.Mods.ModPackageStore</c> 反序列化。字段全部是纯数据，不依赖 UnityEngine，
	/// 这样外部 Mod 工程只引用 <c>Bpsim.Modding</c> 的类型即可声明包。
	/// </summary>
	[Serializable]
	public sealed class ModManifest
	{
		/// <summary>清单结构版本。用于将来迁移，读取时不做强制校验。</summary>
		public int schema = 1;

		/// <summary>全局唯一 id（小写、连字符）。同时是安装目录名。</summary>
		public string id;

		/// <summary>显示名（可中文）。</summary>
		public string name;

		/// <summary>语义化版本。</summary>
		public string version = "1.0.0";

		/// <summary>作者。</summary>
		public string author;

		/// <summary>一句话简介。</summary>
		public string description;

		/// <summary>要求的最低游戏版本（可选，形如 "2023.0.2"）。空表示不限制。</summary>
		public string minGameVersion;

		/// <summary>
		/// 包类型：<c>panel</c>（纯网页内容，无 DLL）、<c>plugin</c>（含托管程序集入口）、
		/// <c>mixed</c>（既有 DLL 又有网页面板）。默认 <c>panel</c>。
		/// </summary>
		public string type = ModPackageType.Panel;

		/// <summary>plugin/mixed 类型的入口程序集文件名（相对 Mod 目录，如 "MyMod.dll"）。</summary>
		public string entryAssembly;

		/// <summary>实现 <see cref="IBpsimMod"/> 的完整类型名（含命名空间）。</summary>
		public string entryType;

		/// <summary>是否影响模拟（服务器/工坊用来提示「联机不兼容」）。</summary>
		public bool affectsSimulation;

		/// <summary>是否联机安全。</summary>
		public bool multiplayerSafe;

		/// <summary>网页面板列表（panel/mixed 用）。</summary>
		public List<ModPanelSpec> panels = new List<ModPanelSpec>();

		/// <summary>标签（可选，工坊列表展示用）。</summary>
		public List<string> tags = new List<string>();

		public bool IsPlugin => string.Equals(type, ModPackageType.Plugin, StringComparison.OrdinalIgnoreCase)
			|| string.Equals(type, ModPackageType.Mixed, StringComparison.OrdinalIgnoreCase);

		public bool HasPanels => panels != null && panels.Count > 0;

		/// <summary>把缺失的关键字段补齐到可用状态（不抛异常，只做最小修正）。</summary>
		public void Normalize()
		{
			if (string.IsNullOrWhiteSpace(id))
			{
				id = "unnamed-mod";
			}
			id = id.Trim().ToLowerInvariant();
			if (string.IsNullOrWhiteSpace(name))
			{
				name = id;
			}
			if (string.IsNullOrWhiteSpace(version))
			{
				version = "1.0.0";
			}
			if (string.IsNullOrWhiteSpace(type))
			{
				type = ModPackageType.Panel;
			}
			if (panels == null)
			{
				panels = new List<ModPanelSpec>();
			}
			if (tags == null)
			{
				tags = new List<string>();
			}
			for (int i = 0; i < panels.Count; i++)
			{
				panels[i]?.Normalize(id + "-panel-" + i);
			}
		}
	}

	/// <summary>包类型常量（放在这里避免散落字符串）。</summary>
	public static class ModPackageType
	{
		public const string Panel = "panel";
		public const string Plugin = "plugin";
		public const string Mixed = "mixed";
	}

	/// <summary>一个网页面板的描述。宿主会用 Vuplex 3D WebView 把它渲染成一个可拖动的游戏内窗口。</summary>
	[Serializable]
	public sealed class ModPanelSpec
	{
		/// <summary>面板 id（在一个 mod 内唯一）。</summary>
		public string id;

		/// <summary>窗口/按钮显示标题。</summary>
		public string title;

		/// <summary>主页面文件名（相对 Mod 目录），如 "panel.html"。</summary>
		public string html = "panel.html";

		/// <summary>可选的外置样式（会被内联进 html 的 &lt;style&gt;）。</summary>
		public string css;

		/// <summary>可选的外置脚本（会被内联进 html 的 &lt;script&gt;）。</summary>
		public string js;

		/// <summary>窗口参考尺寸（1920x1080 画布下的像素）。</summary>
		public float width = 420f;

		public float height = 640f;

		/// <summary>锚点：bottom-right / bottom-left / top-right / top-left / center。</summary>
		public string anchor = "bottom-right";

		/// <summary>图标文件名（相对 Mod 目录，可选）。</summary>
		public string icon;

		/// <summary>是否默认自动打开。</summary>
		public bool autoOpen;

		public void Normalize(string fallbackId)
		{
			if (string.IsNullOrWhiteSpace(id))
			{
				id = fallbackId;
			}
			if (string.IsNullOrWhiteSpace(title))
			{
				title = id;
			}
			if (string.IsNullOrWhiteSpace(html))
			{
				html = "panel.html";
			}
			if (width < 80f)
			{
				width = 420f;
			}
			if (height < 80f)
			{
				height = 640f;
			}
			if (string.IsNullOrWhiteSpace(anchor))
			{
				anchor = "bottom-right";
			}
		}
	}
}
