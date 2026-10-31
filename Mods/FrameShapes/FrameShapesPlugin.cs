using System.IO;
using BepInEx;
using Bpsim.Modding;
using Bpsim.Mods.BepInExHost;
using Bpsim.Parts;
using UnityEngine.SceneManagement;

namespace Bpsim.FrameShapesMod
{
	[BepInPlugin(Id, "bp-rebooted ECS 框形", "1.0.0")]
	public sealed class FrameShapesPlugin : BaseUnityPlugin, IBpsimMod
	{
		public const string Id = "bput.bpsim.bpreboot-frame-shapes";
		private bool m_registered;
		private void Awake()
		{
			SceneManager.sceneLoaded += OnSceneLoaded;
			TryRegister();
		}
		private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TryRegister();
		private void Update() => TryRegister();
		private void TryRegister()
		{
			if (!m_registered && BpsimModHost.Ready) Register();
		}
		private void Register()
		{
			if (m_registered) return;
			BpsimModHost.Register(new ModManifest
			{
				id = Id, name = "bp-rebooted ECS 框形", version = "1.0.0", author = "Goggs77 / bpsim ECS port",
				description = "bp-rebooted / BPIR 变形框的 ECS 实现，支持长方形框、直角三角框、半圆框和圆框，选择框后可切换形状。",
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
