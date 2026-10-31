using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Bpsim.Modding;
using Newtonsoft.Json;

namespace Bpsim.Mods
{
	/// <summary>
	/// Mod 包的磁盘管理：扫描安装目录/内置目录、解析清单、把 zip 安装成目录、删除。不含「加载程序集」逻辑
	/// （那是 <see cref="ModManager"/> 的职责），只负责文件层面。
	/// </summary>
	public static class ModPackageStore
	{
		/// <summary>宽松反序列化：忽略清单里不认识的字段，容忍注释/尾逗号。</summary>
		public static readonly JsonSerializerSettings Lenient = new JsonSerializerSettings
		{
			MissingMemberHandling = MissingMemberHandling.Ignore,
			NullValueHandling = NullValueHandling.Ignore,
			DateParseHandling = DateParseHandling.None
		};

		/// <summary>扫描两个根目录，返回所有能被解析出清单的包（跳过以 '.' 开头的隐藏/临时目录）。</summary>
		public static List<ModPackage> Scan()
		{
			var result = new List<ModPackage>();
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			ScanRoot(ModPaths.Root, ModSource.Installed, result, seen);
			ScanRoot(ModPaths.BuiltInRoot, ModSource.BuiltIn, result, seen);
			return result;
		}

		private static void ScanRoot(string root, ModSource source, List<ModPackage> result, HashSet<string> seen)
		{
			if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
			{
				return;
			}
			foreach (string dir in Directory.GetDirectories(root))
			{
				string name = Path.GetFileName(dir);
				if (name.StartsWith("."))
				{
					continue;
				}
				ModPackage pkg = TryLoadPackage(dir, source);
				if (pkg == null)
				{
					continue;
				}
				// 同名 id 以先扫到的（Installed 优先）为准。
				if (!seen.Add(pkg.Id))
				{
					continue;
				}
				result.Add(pkg);
			}
		}

		/// <summary>
		/// 读取单个已解压目录里的清单：优先 <c>Mod.xml</c>，回退旧的 <c>manifest.json</c>。
		/// 失败返回 null。
		/// </summary>
		public static ModPackage TryLoadPackage(string dir, ModSource source)
		{
			ModManifest manifest = ModLoader.ReadManifest(dir, out string error);
			if (manifest == null)
			{
				return null;
			}
			return new ModPackage
			{
				Directory = dir,
				Manifest = manifest,
				Source = source
			};
		}

		/// <summary>
		/// 把 zip 安装到 <c>Mods/&lt;id&gt;</c>。zip 里 manifest.json 可以在根，也可以在一个顶层文件夹里。
		/// 返回安装好的包；失败抛 <see cref="InvalidDataException"/> 并清理临时目录。
		/// </summary>
		public static ModPackage InstallZip(string zipPath, bool overwrite = true)
		{
			ModPaths.EnsureRoot();
			string work = Path.Combine(ModPaths.TmpDir, "install-" + Guid.NewGuid().ToString("N"));
			ModPaths.EnsureDirectory(work);
			try
			{
				ZipFile.ExtractToDirectory(zipPath, work);

				string packageRoot = FindPackageRoot(work);
				if (packageRoot == null)
				{
					throw new InvalidDataException("压缩包里找不到 Mod.xml 或 manifest.json");
				}

				ModPackage probe = TryLoadPackage(packageRoot, ModSource.Installed);
				if (probe == null || probe.Manifest == null)
				{
					throw new InvalidDataException("清单解析失败（Mod.xml / manifest.json）");
				}

				string target = Path.Combine(ModPaths.Root, probe.Manifest.id);
				if (Directory.Exists(target))
				{
					if (!overwrite)
					{
						throw new InvalidDataException("目标已存在：" + probe.Manifest.id);
					}
					Directory.Delete(target, true);
				}
				CopyDirectory(packageRoot, target);

				ModPackage installed = TryLoadPackage(target, ModSource.Installed);
				if (installed == null)
				{
					throw new InvalidDataException("安装后重新解析失败");
				}
				return installed;
			}
			finally
			{
				try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch { }
			}
		}

		/// <summary>删除一个已安装的 Mod（内置源不可删）。</summary>
		public static bool Uninstall(ModPackage pkg)
		{
			if (pkg == null || pkg.Source != ModSource.Installed || !Directory.Exists(pkg.Directory))
			{
				return false;
			}
			try
			{
				Directory.Delete(pkg.Directory, true);
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}

		private static string FindPackageRoot(string extracted)
		{
			if (HasManifest(extracted))
			{
				return extracted;
			}
			foreach (string dir in Directory.GetDirectories(extracted))
			{
				if (HasManifest(dir))
				{
					return dir;
				}
				// 再深一层（有些打包工具会多套一层）
				foreach (string sub in Directory.GetDirectories(dir))
				{
					if (HasManifest(sub))
					{
						return sub;
					}
				}
			}
			return null;
		}

		/// <summary>目录里有没有可识别的清单（<c>Mod.xml</c> 或 <c>manifest.json</c>）。</summary>
		private static bool HasManifest(string dir)
		{
			return File.Exists(Path.Combine(dir, Bpsim.Modding.ModXml.FileName))
				|| File.Exists(Path.Combine(dir, BpsimModApi.ManifestFileName));
		}

		public static void CopyDirectory(string source, string target)
		{
			ModPaths.EnsureDirectory(target);
			foreach (string file in Directory.GetFiles(source))
			{
				File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
			}
			foreach (string dir in Directory.GetDirectories(source))
			{
				CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
			}
		}
	}
}
