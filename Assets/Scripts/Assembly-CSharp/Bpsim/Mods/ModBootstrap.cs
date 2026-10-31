using UnityEngine;

namespace Bpsim.Mods
{
	/// <summary>
	/// 让 Mod 框架在「游戏运行时」自动启动，不依赖任何场景配置。
	/// 与 <c>Bpsim.Background.CustomBackgroundBootstrap</c> 同样的做法。
	/// </summary>
	/// <remarks>
	/// 旧版还有一条「BepInEx 插件里调 <c>ModManager.Ensure</c>」的并行入口，已随 BepInEx 通道一起移除：
	/// 现在只有这一个启动点，行为可预测。
	/// </remarks>
	public static class ModBootstrap
	{
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void AutoStart()
		{
			ModManager.Ensure();
		}
	}
}
