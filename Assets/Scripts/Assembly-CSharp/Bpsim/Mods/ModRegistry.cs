using System;
using System.Collections.Generic;
using Bpsim.Modding;
using Bpsim.Parts;
using Unity.Entities;
using UnityEngine;

namespace Bpsim.Mods
{
	/// <summary>
	/// GUID → Mod 的注册表（抄 Besiege 的 <c>ModIds</c>）。存档只记 GUID，靠它反查是哪个 Mod。
	/// </summary>
	public static class ModRegistry
	{
		private static readonly Dictionary<Guid, ModPackage> s_byGuid = new Dictionary<Guid, ModPackage>();
		private static readonly Dictionary<string, ModPackage> s_byId = new Dictionary<string, ModPackage>(StringComparer.OrdinalIgnoreCase);
		private static readonly List<Action<Guid>> s_loadedHandlers = new List<Action<Guid>>();

		/// <summary>某个 Mod 加载完成（每次启用都会触发一次）。</summary>
		public static event Action<Guid> OnModLoaded
		{
			add
			{
				if (value == null)
				{
					return;
				}
				s_loadedHandlers.Add(value);
				foreach (ModPackage pkg in s_byGuid.Values)
				{
					if (pkg != null && pkg.IsLoaded)
					{
						SafeInvoke(value, pkg.Guid);
					}
				}
			}
			remove
			{
				s_loadedHandlers.Remove(value);
			}
		}

		/// <summary>用当前包列表重建索引（每次 Rescan 后调用）。</summary>
		public static void Rebuild(IEnumerable<ModPackage> packages)
		{
			s_byGuid.Clear();
			s_byId.Clear();
			if (packages == null)
			{
				return;
			}
			foreach (ModPackage pkg in packages)
			{
				if (pkg == null || pkg.Manifest == null)
				{
					continue;
				}
				if (pkg.Guid != Guid.Empty)
				{
					s_byGuid[pkg.Guid] = pkg;
				}
				s_byId[pkg.Id] = pkg;
			}
		}

		public static ModPackage GetModById(Guid id)
		{
			return s_byGuid.TryGetValue(id, out ModPackage pkg) ? pkg : null;
		}

		public static ModPackage GetModById(string id)
		{
			if (string.IsNullOrEmpty(id))
			{
				return null;
			}
			if (s_byId.TryGetValue(id, out ModPackage pkg))
			{
				return pkg;
			}
			return Guid.TryParse(id, out Guid guid) ? GetModById(guid) : null;
		}

		public static bool IsModLoaded(Guid id)
		{
			ModPackage pkg = GetModById(id);
			return pkg != null && pkg.IsLoaded;
		}

		/// <summary>取 Mod 声明的版本；解析失败返回 null。</summary>
		public static Version GetVersion(Guid id)
		{
			ModPackage pkg = GetModById(id);
			if (pkg == null || pkg.Manifest == null)
			{
				return null;
			}
			return ParseVersion(pkg.Manifest.version);
		}

		/// <summary>广播「这个 Mod 加载好了」。</summary>
		public static void InvokeLoaded(Guid id)
		{
			for (int i = 0; i < s_loadedHandlers.Count; i++)
			{
				SafeInvoke(s_loadedHandlers[i], id);
			}
		}

		private static void SafeInvoke(Action<Guid> handler, Guid id)
		{
			try
			{
				handler(id);
			}
			catch (Exception ex)
			{
				Debug.LogWarning("[Mods] OnModLoaded 回调异常：" + ex.Message);
			}
		}

		private static Version ParseVersion(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				return null;
			}
			if (Version.TryParse(text, out Version parsed))
			{
				return parsed;
			}
			string[] parts = text.Split('.', '-', '+');
			List<int> numbers = new List<int>();
			foreach (string part in parts)
			{
				if (int.TryParse(part, out int n))
				{
					numbers.Add(n);
				}
				else
				{
					break;
				}
			}
			if (numbers.Count == 0)
			{
				return null;
			}
			while (numbers.Count < 2)
			{
				numbers.Add(0);
			}
			return new Version(numbers[0], numbers[1], numbers.Count > 2 ? numbers[2] : 0);
		}
	}

	/// <summary>
	/// 游戏侧往 Mod 侧发事件的入口。宿主代码在这些时机调一下，
	/// 已加载的 Mod 就会收到对应钩子（每个 Mod 单独 try/catch，互不影响）。
	/// </summary>
	public static class ModHooks
	{
		/// <summary>ECS 世界创建后调用。</summary>
		public static void NotifyWorldCreated(World world)
		{
			foreach (ModPackage pkg in Loaded())
			{
				try
				{
					pkg.EntryPoint?.OnWorldCreated(world);
				}
				catch (Exception ex)
				{
					LogError(pkg, "OnWorldCreated", ex);
				}
			}
		}

		/// <summary>ECS 世界销毁前调用。</summary>
		public static void NotifyWorldDestroyed(World world)
		{
			foreach (ModPackage pkg in Loaded())
			{
				try
				{
					pkg.EntryPoint?.OnWorldDestroyed(world);
				}
				catch (Exception ex)
				{
					LogError(pkg, "OnWorldDestroyed", ex);
				}
			}
		}

		/// <summary>一个部件类型注册完成时调用。</summary>
		public static void NotifyPartRegistered(int partTypeId, string partName)
		{
			foreach (ModPackage pkg in Loaded())
			{
				try
				{
					pkg.EntryPoint?.OnPartRegistered(partTypeId, partName);
				}
				catch (Exception ex)
				{
					LogError(pkg, "OnPartRegistered", ex);
				}
			}
		}

		/// <summary>
		/// 把「当前已有的全部部件」补发一遍 <see cref="ModEntryPoint.OnPartRegistered"/>。
		/// 两个调用时机：部件工厂刚建完，以及有 Mod 刚加载完（Mod 通常晚于工厂初始化）。
		/// </summary>
		public static void NotifyPartsReady()
		{
			List<ModPackage> loaded = Loaded();
			if (loaded.Count == 0)
			{
				return;
			}
			PartManager partManager = PartManager.Instance;
			PartCollection<ManagedPart> collection = partManager != null && partManager.Factory != null
				? partManager.Factory.PartCollection
				: null;
			if (collection == null)
			{
				return;
			}
			for (int i = 0; i < collection.Length; i++)
			{
				PartType type = (PartType)i;
				if (!collection.TryFindParts(type, out PartCollection<ManagedPart>.Entry entry) || entry == null)
				{
					continue;
				}
				foreach (int index in entry.Data.Keys)
				{
					for (int m = 0; m < loaded.Count; m++)
					{
						ModPackage pkg = loaded[m];
						try
						{
							pkg.EntryPoint.OnPartRegistered((int)type, type + "#" + index);
						}
						catch (Exception ex)
						{
							LogError(pkg, "OnPartRegistered", ex);
						}
					}
				}
			}
		}

		/// <summary>载具/关卡载入完成时调用。</summary>
		public static void NotifyMachineLoaded(string machinePath)
		{
			foreach (ModPackage pkg in Loaded())
			{
				try
				{
					pkg.EntryPoint?.OnMachineLoaded(machinePath);
				}
				catch (Exception ex)
				{
					LogError(pkg, "OnMachineLoaded", ex);
				}
			}
		}

		/// <summary>
		/// 问每个已加载 Mod「这个存档是不是依赖你」。返回被依赖的 GUID 列表，
		/// 存档写入时把它们记下来，载入时就能提示「缺少某某 Mod」。
		/// </summary>
		public static List<Guid> CollectRequiredForMachine(string machineJson)
		{
			List<Guid> required = new List<Guid>();
			foreach (ModPackage pkg in Loaded())
			{
				try
				{
					if (pkg.EntryPoint != null && pkg.EntryPoint.IsRequiredForMachine(machineJson))
					{
						required.Add(pkg.Guid);
					}
				}
				catch (Exception ex)
				{
					LogError(pkg, "IsRequiredForMachine", ex);
				}
			}
			return required;
		}

		/// <summary>存档里的 GUID 有没有缺。</summary>
		public static bool TryResolveMissing(IEnumerable<Guid> required, List<Guid> missing)
		{
			bool allPresent = true;
			if (required == null)
			{
				return true;
			}
			foreach (Guid id in required)
			{
				if (!ModRegistry.IsModLoaded(id))
				{
					allPresent = false;
					missing?.Add(id);
				}
			}
			return allPresent;
		}

		private static List<ModPackage> Loaded()
		{
			List<ModPackage> list = new List<ModPackage>();
			ModManager manager = ModManager.Instance;
			if (manager == null)
			{
				return list;
			}
			foreach (ModPackage pkg in manager.Packages)
			{
				if (pkg != null && pkg.IsLoaded && pkg.EntryPoint != null)
				{
					list.Add(pkg);
				}
			}
			return list;
		}

		private static void LogError(ModPackage pkg, string hook, Exception ex)
		{
			Debug.LogWarning("[Mods] " + pkg.Id + " 的 " + hook + " 抛异常：" + ex.Message);
		}
	}
}
