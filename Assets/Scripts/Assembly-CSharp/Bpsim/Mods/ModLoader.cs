using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Bpsim.Modding;
using Newtonsoft.Json;
using UnityEngine;

namespace Bpsim.Mods
{
	/// <summary>
	/// Mod 加载管线（抄 Besiege 的 <c>ModLoader</c>）：发现 → 解析清单 → 静态扫描 → 加载程序集 → 实例化入口。
	/// </summary>
	/// <remarks>
	/// <para>和旧实现的区别：不再有「外部注册」通道（BepInEx 那套已经拆掉），
	/// 所有 Mod 都走同一条 <c>Discover → Load</c> 路径，来源只有「目录」。</para>
	/// <para>发现顺序决定优先级：Installed &gt; Workshop &gt; Additional &gt; BuiltIn。
	/// 同一个 GUID 只保留先扫到的那个，后扫到的会在日志里提示。</para>
	/// </remarks>
	public static class ModLoader
	{
		/// <summary>扫描所有来源，返回去重后的包列表（不加载程序集）。</summary>
		public static List<ModPackage> Discover(ModdingConfig config)
		{
			List<ModPackage> result = new List<ModPackage>();
			HashSet<Guid> seen = new HashSet<Guid>();

			ScanRoot(ModPaths.Root, ModSource.Installed, result, seen);
			ScanRoot(ModPaths.WorkshopInstalledRoot, ModSource.Workshop, result, seen);
			if (config != null)
			{
				foreach (string dir in config.AdditionalDirs)
				{
					ScanRoot(ResolveAdditional(dir), ModSource.Additional, result, seen);
				}
			}
			ScanRoot(ModPaths.BuiltInRoot, ModSource.BuiltIn, result, seen);

			result.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase));
			return result;
		}

		/// <summary>
		/// 加载一个包：扫描程序集、实例化入口、调用 <c>OnLoad</c>。
		/// 失败时 <paramref name="error"/> 给出原因，包状态置为 <see cref="ModState.Error"/>。
		/// </summary>
		public static bool Load(ModPackage pkg, ModdingConfig config, ModManager manager, out string error)
		{
			error = null;
			if (pkg == null || pkg.Manifest == null)
			{
				error = "包或清单为空";
				return false;
			}

			// 源码 Mod：没有声明程序集、但有 Code/*.cs 时，先编译成 dll 再当普通 Mod 处理。
			if (!pkg.HasCode && ModScriptCompiler.HasSources(pkg.Directory))
			{
				string compiled = ModScriptCompiler.Compile(pkg.Directory, pkg.Id, out string compileError);
				if (compiled == null)
				{
					error = compileError;
					Fail(pkg, error);
					return false;
				}
				pkg.Manifest.entryAssembly = compiled;
				pkg.Manifest.type = ModPackageType.Plugin;
			}

			// 纯内容包：没有代码，直接算加载成功（面板按需打开）。
			if (!pkg.HasCode)
			{
				pkg.State = ModState.Loaded;
				pkg.Error = null;
				return true;
			}

			string asmPath = ResolveAssemblyPath(pkg);
			if (asmPath == null)
			{
				error = "找不到入口程序集：" + (pkg.Manifest.entryAssembly ?? "(未声明)");
				Fail(pkg, error);
				return false;
			}

			Assembly assembly;
			try
			{
				assembly = Assembly.LoadFrom(asmPath);
			}
			catch (Exception ex)
			{
				error = "程序集加载失败：" + ex.Message;
				Fail(pkg, error);
				return false;
			}

			// 扫描必须在实例化之前 —— 不过的包不会注册任何东西。
			bool strict = config != null && config.StrictScan;
			ModScanResult scan = ModAssemblyScanner.Scan(assembly, strict);
			pkg.Scan = scan;
			if (!scan.Passed)
			{
				error = "静态扫描未通过：" + string.Join("；", scan.Violations.ToArray());
				Fail(pkg, error);
				return false;
			}
			if (scan.Warnings.Count > 0)
			{
				Debug.LogWarning("[Mods] " + pkg.Id + " 扫描告警：" + string.Join("；", scan.Warnings.ToArray()));
			}

			ModEntryPoint entry;
			try
			{
				entry = InstantiateEntryPoint(assembly, pkg, manager, out error);
			}
			catch (Exception ex)
			{
				error = "入口实例化失败：" + ex.Message;
				Fail(pkg, error);
				return false;
			}
			if (entry == null)
			{
				Fail(pkg, error ?? "程序集里没有 ModEntryPoint 子类，也没有 IBpsimMod 实现");
				return false;
			}

			pkg.Assembly = assembly;
			pkg.EntryPoint = entry;

			try
			{
				entry.OnLoad();
			}
			catch (Exception ex)
			{
				error = "OnLoad 抛出异常：" + ex.Message;
				Debug.LogException(ex);
				Fail(pkg, error);
				return false;
			}

			pkg.State = ModState.Loaded;
			pkg.Error = null;
			Debug.Log("[Mods] 已加载 " + pkg.Id + "（" + entry.GetType().FullName + "）");
			return true;
		}

		/// <summary>卸载一个包：调 <c>OnUnload</c> 并清空入口（程序集留在内存里，Mono 无法卸载）。</summary>
		public static void Unload(ModPackage pkg)
		{
			if (pkg == null)
			{
				return;
			}
			try
			{
				pkg.EntryPoint?.OnUnload();
			}
			catch (Exception ex)
			{
				Debug.LogWarning("[Mods] " + pkg.Id + " OnUnload 异常：" + ex.Message);
			}
			finally
			{
				pkg.EntryPoint = null;
				pkg.LegacyInstance = null;
				if (pkg.State == ModState.Loaded)
				{
					pkg.State = ModState.Discovered;
				}
			}
		}

		// ───────────────────────── 内部 ─────────────────────────

		private static void Fail(ModPackage pkg, string error)
		{
			pkg.State = ModState.Error;
			pkg.Error = error;
			Debug.LogError("[Mods] " + pkg.Id + " 加载失败：" + error);
		}

		private static string ResolveAssemblyPath(ModPackage pkg)
		{
			string relative = pkg.Manifest.entryAssembly;
			if (string.IsNullOrWhiteSpace(relative))
			{
				List<string> list = pkg.Manifest.assemblies;
				if (list != null && list.Count > 0)
				{
					relative = list[0];
				}
			}
			if (string.IsNullOrWhiteSpace(relative))
			{
				return null;
			}
			string full = Path.IsPathRooted(relative) ? relative : Path.Combine(pkg.Directory, relative);
			return File.Exists(full) ? Path.GetFullPath(full) : null;
		}

		private static ModEntryPoint InstantiateEntryPoint(Assembly assembly, ModPackage pkg, ModManager manager, out string error)
		{
			error = null;
			Type[] types;
			try
			{
				types = assembly.GetTypes();
			}
			catch (ReflectionTypeLoadException ex)
			{
				types = ex.Types ?? new Type[0];
			}

			List<Type> entryTypes = new List<Type>();
			List<Type> legacyTypes = new List<Type>();
			foreach (Type type in types)
			{
				if (type == null || type.IsAbstract || type.IsInterface)
				{
					continue;
				}
				if (type.IsSubclassOf(typeof(ModEntryPoint)))
				{
					entryTypes.Add(type);
				}
				else if (typeof(IBpsimMod).IsAssignableFrom(type))
				{
					legacyTypes.Add(type);
				}
			}

			if (entryTypes.Count > 1)
			{
				error = "程序集里有 " + entryTypes.Count + " 个 ModEntryPoint 子类（只能有一个）";
				return null;
			}

			ModEntryPoint entry = null;
			if (entryTypes.Count == 1)
			{
				entry = (ModEntryPoint)Activator.CreateInstance(entryTypes[0]);
			}
			else if (legacyTypes.Count > 0)
			{
				// 兼容层：老 Mod 实现 IBpsimMod，用适配器包一层，行为保持不变。
				Type legacyType = legacyTypes[0];
				IBpsimMod legacy = (IBpsimMod)Activator.CreateInstance(legacyType);
				pkg.LegacyInstance = legacy;
				entry = new LegacyModEntryPoint(legacy);
				Debug.Log("[Mods] " + pkg.Id + " 使用兼容入口（IBpsimMod）：" + legacyType.FullName);
			}

			if (entry == null)
			{
				error = "程序集里没有 ModEntryPoint 子类，也没有 IBpsimMod 实现";
				return null;
			}

			entry.Id = pkg.Guid;
			entry.ModDirectory = pkg.Directory;
			entry.Context = new ModContext(pkg, manager);
			return entry;
		}

		private static void ScanRoot(string root, ModSource source, List<ModPackage> result, HashSet<Guid> seen)
		{
			if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
			{
				return;
			}
			string[] dirs;
			try
			{
				dirs = Directory.GetDirectories(root);
			}
			catch (Exception ex)
			{
				Debug.LogWarning("[Mods] 扫描 " + root + " 失败：" + ex.Message);
				return;
			}
			foreach (string dir in dirs)
			{
				string name = Path.GetFileName(dir);
				if (name.StartsWith(".", StringComparison.Ordinal))
				{
					continue;
				}
				if (TryAddPackage(dir, source, result, seen))
				{
					continue;
				}
				// 再深一层：有些打包工具会多套一层目录
				string[] nested;
				try
				{
					nested = Directory.GetDirectories(dir);
				}
				catch (Exception)
				{
					continue;
				}
				foreach (string sub in nested)
				{
					TryAddPackage(sub, source, result, seen);
				}
			}
		}

		private static bool TryAddPackage(string dir, ModSource source, List<ModPackage> result, HashSet<Guid> seen)
		{
			ModManifest manifest = ReadManifest(dir, out string error);
			if (manifest == null)
			{
				return false;
			}
			if (manifest.guid == Guid.Empty)
			{
				manifest.guid = ModGuid.Derive(manifest.id);
			}
			if (!seen.Add(manifest.guid))
			{
				Debug.Log("[Mods] 跳过重复 Mod（GUID 已存在）：" + dir);
				return true;
			}
			ModPackage pkg = new ModPackage
			{
				Directory = dir,
				Manifest = manifest,
				Source = source,
				WorkshopId = source == ModSource.Workshop ? Path.GetFileName(dir) : null
			};
			result.Add(pkg);
			return true;
		}

		/// <summary>读清单：优先 <c>Mod.xml</c>，回退旧的 <c>manifest.json</c>。</summary>
		public static ModManifest ReadManifest(string dir, out string error)
		{
			error = null;
			string xmlPath = Path.Combine(dir, ModXml.FileName);
			if (File.Exists(xmlPath))
			{
				if (ModXml.TryParse(xmlPath, Path.GetFileName(dir), out ModManifest fromXml, out error))
				{
					return fromXml;
				}
				return null;
			}

			string jsonPath = Path.Combine(dir, BpsimModApi.ManifestFileName);
			if (!File.Exists(jsonPath))
			{
				return null;
			}
			try
			{
				ModManifest legacy = JsonConvert.DeserializeObject<ModManifest>(
					File.ReadAllText(jsonPath), ModPackageStore.Lenient);
				if (legacy == null)
				{
					error = "manifest.json 解析为空";
					return null;
				}
				legacy.Normalize();
				return legacy;
			}
			catch (Exception ex)
			{
				error = "manifest.json 解析失败：" + ex.Message;
				return null;
			}
		}

		private static string ResolveAdditional(string dir)
		{
			if (string.IsNullOrWhiteSpace(dir))
			{
				return null;
			}
			return Path.IsPathRooted(dir) ? dir : Path.Combine(Application.persistentDataPath, dir);
		}
	}

	/// <summary>
	/// 老式 <see cref="IBpsimMod"/> 的适配器：让旧 Mod 不用改一行代码就能跑在新管线上。
	/// </summary>
	internal sealed class LegacyModEntryPoint : ModEntryPoint
	{
		private readonly IBpsimMod m_mod;

		public LegacyModEntryPoint(IBpsimMod mod)
		{
			m_mod = mod;
		}

		public override void OnLoad()
		{
			m_mod.OnLoad(Context);
		}

		public override void OnUnload()
		{
			m_mod.OnUnload();
		}
	}
}
