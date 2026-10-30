using System.IO;
using UnityEngine;

namespace Bpsim.Mods
{
	/// <summary>
	/// Mod 相关的路径解析。安装的 Mod 放在 <c>persistentDataPath/Mods</c>（可写）；
	/// 随包分发的内置 Mod 放在 <c>streamingAssetsPath/Mods</c>（只读，随构建发布）。
	/// </summary>
	public static class ModPaths
	{
		public static string Root => Path.Combine(Application.persistentDataPath, Bpsim.Modding.BpsimModApi.ModsFolderName);

		public static string BuiltInRoot => Path.Combine(Application.streamingAssetsPath, Bpsim.Modding.BpsimModApi.ModsFolderName);

		public static string SettingsPath => Path.Combine(Root, "mods.settings.json");

		public static string TmpDir => Path.Combine(Root, ".tmp");

		/// <summary>工坊下载与缓存目录（与 Mods 并列，便于整体清理）。</summary>
		public static string WorkshopRoot => Path.Combine(Application.persistentDataPath, "ModWorkshop");

		public static string WorkshopCacheDir => Path.Combine(WorkshopRoot, "cache");

		public static string WorkshopDownloadsDir => Path.Combine(WorkshopRoot, "downloads");

		public static string WorkshopIndexCachePath => Path.Combine(WorkshopCacheDir, "index.json");

		public static string EnsureRoot()
		{
			EnsureDirectory(Root);
			EnsureDirectory(TmpDir);
			return Root;
		}

		public static void EnsureDirectory(string dir)
		{
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}
		}

		/// <summary>把绝对路径规整成以 '/' 分隔的相对形式，便于日志与比较。</summary>
		public static string ToDisplay(string path)
		{
			return string.IsNullOrEmpty(path) ? path : path.Replace('\\', '/');
		}
	}
}
