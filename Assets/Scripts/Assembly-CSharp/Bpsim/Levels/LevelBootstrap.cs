using UnityEngine;

namespace Bpsim.Levels
{
	/// <summary>
	/// Brings the level-import engine up without touching <c>MainScene</c> or the
	/// hand-authored UI prefabs: it creates only the <see cref="LevelManager"/> holder
	/// once the first scene has finished loading.
	/// </summary>
	/// <remarks>
	/// 关卡数据已经剥离成可选 Mod：宿主这里不再挂右上角常驻「关卡」按钮、也不再创建
	/// <see cref="LevelMenu"/> 面板。没有关卡 Mod 时 <c>LevelCatalog</c> 指向空的
	/// StreamingAssets 目录（里面没有关卡数据），引擎常驻但没有任何关卡可打开。
	/// 加载关卡 Mod 后由 <c>ModLevels.SetBuiltInRoot</c> 注入数据，并由 Mod 通过
	/// <c>ModLevels.OpenLevelPicker()</c> 按需打开面板。
	/// </remarks>
	internal static class LevelBootstrap
	{
		public const string RootName = "BpsimLevels";

		private static bool s_booted;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Boot()
		{
			if (s_booted)
			{
				return;
			}
			s_booted = true;
			if (GameObject.Find(RootName) != null)
			{
				return;
			}
			GameObject root = new GameObject(RootName);
			root.AddComponent<LevelManager>();
			Debug.Log("[BpsimLevels] level engine ready (level data is provided by the optional BPLE Levels mod)");
		}
	}
}
