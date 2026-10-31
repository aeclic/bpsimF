using System;
using System.Reflection;
using Bpsim.Modding;

namespace Bpsim.Mods
{
	/// <summary>Mod 的运行时状态机。</summary>
	public enum ModState
	{
		/// <summary>已发现但未加载（禁用或尚未装载）。</summary>
		Discovered,

		/// <summary>已成功加载（入口已 OnLoad / 面板已注册）。</summary>
		Loaded,

		/// <summary>加载/解析/扫描失败，Error 里是原因。</summary>
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

		/// <summary>工坊下载到 ModWorkshop/installed。</summary>
		Workshop,

		/// <summary>Modding.xml 里额外挂的目录。</summary>
		Additional
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

		/// <summary>工坊 id（仅 <see cref="ModSource.Workshop"/> 有）。</summary>
		public string WorkshopId;

		public ModState State = ModState.Discovered;

		public string Error;

		/// <summary>已加载的入口程序集（仅含代码的包）。</summary>
		public Assembly Assembly;

		/// <summary>
		/// 入口对象。<see cref="ModEntryPoint"/> 是首选；老 Mod 实现 <see cref="IBpsimMod"/> 时，
		/// 加载器会把它包进一个适配器，所以这里永远非空（加载成功的前提下）。
		/// </summary>
		public ModEntryPoint EntryPoint;

		/// <summary>老式 <see cref="IBpsimMod"/> 实例（兼容层用；新 Mod 应为 null）。</summary>
		public IBpsimMod LegacyInstance;

		/// <summary>程序集扫描结果（纯内容包为 null）。</summary>
		public ModScanResult Scan;

		/// <summary>玩家是否勾选启用（持久化在 Modding.xml 的 disabled-mods 里）。</summary>
		public bool Enabled;

		/// <summary>稳定 GUID。</summary>
		public Guid Guid => Manifest != null ? Manifest.guid : Guid.Empty;

		public string Id => Manifest != null ? Manifest.id : "unknown";

		public string DisplayName => Manifest != null ? Manifest.name : Id;

		public string Version => Manifest != null ? Manifest.version : "?";

		public bool IsLoaded => State == ModState.Loaded;

		public bool HasCode => Manifest != null && Manifest.IsPlugin;

		public override string ToString()
		{
			return Id + "@" + Version + " [" + State + "]";
		}
	}
}
