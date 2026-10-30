using UnityEngine;

namespace Bpsim.Mods
{
	/// <summary>
	/// 让 Mod 框架在「游戏运行时」自动启动，不依赖任何场景配置。
	/// 与 <c>Bpsim.Background.CustomBackgroundBootstrap</c> 同样的做法。
	/// 装了 BepInEx 时，插件里也会调 <see cref="ModManager.Ensure"/>，两边幂等。
	/// </summary>
	public static class ModBootstrap
	{
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void AutoStart()
		{
			ModManager.Ensure();
		}
	}
}
