using System;

namespace Bpsim.Modding
{
	/// <summary>
	/// 托管 Mod 的入口接口。含 DLL 的 <c>plugin</c> / <c>mixed</c> 包必须提供实现此接口、
	/// 且带无参构造函数的类型，并把完整类型名写进 <c>manifest.json</c> 的 <c>entryType</c>。
	/// </summary>
	/// <remarks>
	/// 生命周期由 <c>Bpsim.Mods.ModManager</c> 驱动：加载程序集 → 反射实例化 → <see cref="OnLoad"/>；
	/// 禁用/卸载时调用 <see cref="OnUnload"/>。纯网页内容包（<c>panel</c>）不需要实现本接口。
	/// </remarks>
	public interface IBpsimMod
	{
		/// <summary>Mod 被加载（或从禁用改为启用）时调用一次。</summary>
		void OnLoad(IModContext context);

		/// <summary>Mod 被禁用/卸载/热重载前调用。应释放自己持有的资源。</summary>
		void OnUnload();
	}

	/// <summary>Mod 运行时上下文：Mod 通过它访问自己的目录、日志和宿主 UI。</summary>
	public interface IModContext
	{
		/// <summary>本 Mod 的安装目录（绝对路径）。</summary>
		string ModDirectory { get; }

		/// <summary>本 Mod 的清单。</summary>
		ModManifest Manifest { get; }

		/// <summary>日志。</summary>
		IModLogger Log { get; }

		/// <summary>宿主 UI 服务（加菜单按钮、开面板）。</summary>
		IModUI Ui { get; }
	}

	/// <summary>Mod 日志（带 Mod id 前缀，落到 Unity Console 与 mod.log）。</summary>
	public interface IModLogger
	{
		void Info(string message);

		void Warn(string message);

		void Error(string message);
	}

	/// <summary>宿主提供给 Mod 的 UI 能力。全部是惰性调用，宿主不存在时会安全忽略。</summary>
	public interface IModUI
	{
		/// <summary>在游戏内 Mod 管理器的「快捷入口」里加一个按钮。</summary>
		void AddQuickButton(string label, Action onClick);

		/// <summary>打开本 Mod 声明的某个网页面板（panelId = <see cref="ModPanelSpec.id"/>）。</summary>
		void OpenPanel(string panelId);

		/// <summary>关闭本 Mod 的某个网页面板。</summary>
		void ClosePanel(string panelId);

		/// <summary>往游戏内右下角弹一条气泡提示。</summary>
		void Toast(string message);
	}

	/// <summary>API 版本与全局常量。</summary>
	public static class BpsimModApi
	{
		/// <summary>当前 Mod API 主版本。Mod 可在 manifest 里声明 <c>minApi</c>，暂不强制。</summary>
		public const int Version = 1;

		/// <summary>Mods 安装根目录名（位于 persistentDataPath 下）。</summary>
		public const string ModsFolderName = "Mods";

		/// <summary>清单文件名。</summary>
		public const string ManifestFileName = "manifest.json";
	}
}
