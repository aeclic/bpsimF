using Unity.Entities;
using UnityEngine;

namespace Bpsim.Background
{
	/// <summary>
	/// 保证 <see cref="CustomBackgroundSystem"/> 存在于默认世界（与 Levels/LevelBootstrap 同样的思路：
	/// 不改 MainScene、不动手写 prefab、不往场景里塞 GameObject）。
	///
	/// 默认世界初始化本身会按反射创建所有 SystemBase，所以正常情况下这里只是取回同一个实例；
	/// 兜底的意义在于：若某个构建方式把系统自动创建挡掉，设置面板写的参数仍然有人消费。
	/// </summary>
	internal static class CustomBackgroundBootstrap
	{
		private static bool s_booted;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Boot()
		{
			if (s_booted)
			{
				return;
			}
			s_booted = true;
			Ensure();
		}

		/// <summary>把系统注册（或取回）到默认世界；世界还没建好时返回 null。</summary>
		public static CustomBackgroundSystem Ensure()
		{
			World world = World.DefaultGameObjectInjectionWorld;
			if (world == null || !world.IsCreated)
			{
				return null;
			}
			return world.GetOrCreateSystemManaged<CustomBackgroundSystem>();
		}
	}
}
