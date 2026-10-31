using System;
using System.IO;
using Unity.Entities;
using UnityEngine;

namespace Bpsim.Modding
{
	/// <summary>
	/// Mod 入口基类（抄 Besiege 的 <c>ModEntryPoint</c>）。
	/// 一个 Mod 程序集里**必须恰好有一个**非抽象子类，加载器反射找到它并实例化。
	/// </summary>
	/// <remarks>
	/// <para>和旧 <see cref="IBpsimMod"/> 的区别：入口不是「被宿主调 OnLoad 的插件」，
	/// 而是一个**带钩子的宿主对象** —— 宿主在部件注册、世界创建、载具载入这些时机回调它。
	/// 这是 Besiege 那套能「加方块」而 BepInEx 只能「改方法」的根本原因。</para>
	/// <para>所有钩子都有空实现，Mod 只覆写自己关心的。钩子内抛异常会被宿主吞掉并记日志，
	/// 不会把游戏打挂。</para>
	/// </remarks>
	public abstract class ModEntryPoint
	{
		/// <summary>本 Mod 的稳定 GUID（来自 <c>Mod.xml</c> 的 <c>&lt;ID&gt;</c>）。</summary>
		public Guid Id { get; internal set; }

		/// <summary>Mod 目录的绝对路径。</summary>
		public string ModDirectory { get; internal set; }

		/// <summary>宿主上下文（日志、UI、面板）。</summary>
		public IModContext Context { get; internal set; }

		/// <summary>便捷日志入口；<see cref="Context"/> 为空时返回 null。</summary>
		public IModLogger Log => Context != null ? Context.Log : null;

		/// <summary>把相对路径解析成 Mod 目录下的绝对路径。</summary>
		public string Path(string relativePath, bool cleanPath = false)
		{
			if (string.IsNullOrEmpty(relativePath))
			{
				return ModDirectory;
			}
			string combined = System.IO.Path.IsPathRooted(relativePath)
				? relativePath
				: System.IO.Path.Combine(ModDirectory ?? string.Empty, relativePath);
			return cleanPath ? System.IO.Path.GetFullPath(combined) : combined;
		}

		/// <summary>Mod 被启用时调用一次（在程序集扫描通过、入口实例化之后）。</summary>
		public virtual void OnLoad()
		{
		}

		/// <summary>Mod 被禁用/卸载前调用。注意：Mono 无法卸载程序集，这里只做清理，不释放代码。</summary>
		public virtual void OnUnload()
		{
		}

		/// <summary>ECS 世界创建后调用。注册系统、查询组件都在这里做。</summary>
		public virtual void OnWorldCreated(World world)
		{
		}

		/// <summary>ECS 世界销毁前调用。</summary>
		public virtual void OnWorldDestroyed(World world)
		{
		}

		/// <summary>
		/// 一个部件类型注册完成时调用。Mod 可以在这里替换图标、改参数、挂自定义行为。
		/// </summary>
		/// <param name="partTypeId">部件类型数值 id。</param>
		/// <param name="partName">部件名（用于日志与匹配）。</param>
		public virtual void OnPartRegistered(int partTypeId, string partName)
		{
		}

		/// <summary>载具/关卡载入完成时调用（<paramref name="machinePath"/> 是存档路径，可能为空）。</summary>
		public virtual void OnMachineLoaded(string machinePath)
		{
		}

		/// <summary>
		/// 存档是否依赖本 Mod。载入载具时宿主会问每个已加载 Mod；
		/// 返回 true 的 Mod 会被记进「本存档需要这些 GUID」的列表（抄 Besiege 的 <c>IsRequiredForMachine</c>）。
		/// </summary>
		public virtual bool IsRequiredForMachine(string machineJson)
		{
			return false;
		}

		/// <summary>关卡是否依赖本 Mod。</summary>
		public virtual bool IsRequiredForLevel(string levelId)
		{
			return false;
		}
	}

	/// <summary>
	/// Mod 自带的资源读取。全部走 Mod 目录，路径相对于 Mod 根，避免 Mod 之间互相覆盖。
	/// </summary>
	public static class ModAssets
	{
		public static string ReadText(string modDirectory, string relativePath)
		{
			string full = Resolve(modDirectory, relativePath);
			return full != null && File.Exists(full) ? File.ReadAllText(full) : null;
		}

		public static byte[] ReadBytes(string modDirectory, string relativePath)
		{
			string full = Resolve(modDirectory, relativePath);
			return full != null && File.Exists(full) ? File.ReadAllBytes(full) : null;
		}

		/// <summary>读一张 PNG/JPG 成 <see cref="Texture2D"/>；失败返回 null。</summary>
		public static Texture2D LoadTexture(string modDirectory, string relativePath)
		{
			byte[] data = ReadBytes(modDirectory, relativePath);
			if (data == null || data.Length == 0)
			{
				return null;
			}
			Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
			if (!texture.LoadImage(data))
			{
				UnityEngine.Object.Destroy(texture);
				return null;
			}
			texture.name = System.IO.Path.GetFileNameWithoutExtension(relativePath);
			return texture;
		}

		private static string Resolve(string modDirectory, string relativePath)
		{
			if (string.IsNullOrEmpty(modDirectory) || string.IsNullOrEmpty(relativePath))
			{
				return null;
			}
			return System.IO.Path.IsPathRooted(relativePath)
				? relativePath
				: System.IO.Path.Combine(modDirectory, relativePath);
		}
	}
}
