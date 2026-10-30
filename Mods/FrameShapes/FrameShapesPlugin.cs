using System.IO;
using BepInEx;
using Bpsim.Modding;
using Bpsim.Mods.BepInExHost;
using Bpsim.Parts;
using UnityEngine.SceneManagement;

namespace Bpsim.FrameShapesMod
{
	[BepInPlugin(Id, "BPIR 变形框", "1.0.0")]
	public sealed class FrameShapesPlugin : BaseUnityPlugin, IBpsimMod
	{
		public const string Id = "bput.bpsim.frame-shapes";
		private bool m_registered;
		private void Awake()
		{
			SceneManager.sceneLoaded += OnSceneLoaded;
			if (BpsimModHost.Ready) Register();
		}
		private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Register();
		private void Register()
		{
			if (m_registered) return;
			BpsimModHost.Register(new ModManifest
			{
				id = Id, name = "BPIR 变形框", version = "1.0.0", author = "BPIR / bpsim port",
				description = "方框、直角三角框、半圆框、圆框；选择框后在部件属性中切换形状。",
				type = ModPackageType.Plugin, affectsSimulation = true, multiplayerSafe = false
			}, this, Path.GetDirectoryName(Info.Location), defaultEnabled: true);
			m_registered = true;
			SceneManager.sceneLoaded -= OnSceneLoaded;
			Logger.LogInfo("已注册到 Mod 列表（默认开启），ECS 变形框就绪。");
		}
		public void OnLoad(IModContext context) => FrameShapeSystem.SetEnabled(true);
		public void OnUnload() => FrameShapeSystem.SetEnabled(false);
		private void OnDestroy()
		{
			SceneManager.sceneLoaded -= OnSceneLoaded;
			if (m_registered) BpsimModHost.Unregister(Id, this);
		}
	}
}
