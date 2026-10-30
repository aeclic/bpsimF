using System;
using System.Collections.Generic;
using System.Reflection;
using Bpsim.Modding;

namespace Bpsim.Mods
{
	/// <summary>Mod 的运行时状态机。</summary>
	public enum ModState
	{
		/// <summary>已发现但未加载（禁用或尚未装载）。</summary>
		Discovered,

		/// <summary>已成功加载（插件已 OnLoad / 面板已注册）。</summary>
		Loaded,

		/// <summary>加载/解析失败，Error 里是原因。</summary>
		Error,

		/// <summary>被版本/兼容性检查拒绝。</summary>
		Incompatible
	}

	/// <summary>Mod 的来源。决定它能否被删除/覆盖。</summary>
	public enum ModSource
	{
		/// <summary>玩家安装到 persistentDataPath/Mods。</summary>
		Installed,

		/// <summary>随游戏分发的 streamingAssets/Mods。</summary>
		BuiltIn,
		BepInEx
	}

	/// <summary>
	/// 一个已发现的 Mod 包。这是运行时对象（非 ECS），持有清单、目录、已加载的程序集与入口实例。
	/// 与 ECS 侧的 <see cref="ModEntityComponent"/> 通过 id 一一对应。
	/// </summary>
	public sealed class ModPackage
	{
		public string Directory;

		public ModManifest Manifest;

		public ModSource Source;

		public ModState State = ModState.Discovered;

		public string Error;

		/// <summary>已加载的入口程序集（仅 plugin/mixed）。</summary>
		public Assembly Assembly;

		/// <summary>已实例化的入口对象（仅 plugin/mixed）。</summary>
		public IBpsimMod Instance;

		// Registered by the BepInEx chainloader. The manager owns OnLoad/OnUnload, not the DLL.
		public IBpsimMod ExternalInstance;
		public bool ExternalDefaultEnabled;

		/// <summary>玩家是否勾选启用（持久化在 mods.settings.json）。</summary>
		public bool Enabled;

		public string Id => Manifest != null ? Manifest.id : "unknown";

		public string DisplayName => Manifest != null ? Manifest.name : Id;

		public string Version => Manifest != null ? Manifest.version : "?";

		public bool IsLoaded => State == ModState.Loaded;

		public override string ToString()
		{
			return Id + "@" + Version + " [" + State + "]";
		}
	}

	/// <summary>持久化到 <c>mods.settings.json</c> 的玩家偏好：每个 mod 的启用开关。</summary>
	[Serializable]
	public sealed class ModSettings
	{
		public int schema = 1;

		/// <summary>被明确启用的 mod id 列表。未列出的按 manifest 默认（panel 默认开，plugin 默认关）。</summary>
		public List<string> enabled = new List<string>();

		/// <summary>被明确禁用的 mod id 列表（优先级高于 enabled）。</summary>
		public List<string> disabled = new List<string>();

		public bool IsEnabled(string id)
		{
			if (disabled != null && disabled.Contains(id))
			{
				return false;
			}
			return enabled != null && enabled.Contains(id);
		}

		public void Set(string id, bool on)
		{
			enabled ??= new List<string>();
			disabled ??= new List<string>();
			enabled.Remove(id);
			disabled.Remove(id);
			(on ? enabled : disabled).Add(id);
		}
	}
}
