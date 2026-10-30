using System;
using System.IO;
using Bpsim.Background;
using Bpsim.Serialization;
using UnityEngine;

namespace Bpsim
{
	[Serializable]
	public class UserSettings
	{
		public Version Version { get; set; }

		public SimulationSettings SimulationSettings { get; set; }

		public ButtonSettings ButtonSettings { get; set; }

		public InteractionSettings InteractionSettings { get; set; }

		/// <summary>自定义背景（设置面板 → 渲染系统 → 一张 quad）。</summary>
		public CustomBackgroundSettings CustomBackground { get; set; }

		/// <summary>Debug 控制台（设置面板 → 调试）。默认关闭 = 不实例化控制台组件。</summary>
		public DebugConsoleSettings DebugConsole { get; set; }

		public static UserSettings Default { get; private set; }

		public static UserSettings Instance { get; private set; }

		public UserSettings(Version version)
		{
			Version = version;
			SimulationSettings = new SimulationSettings();
			ButtonSettings = new ButtonSettings();
			InteractionSettings = new InteractionSettings();
			CustomBackground = new CustomBackgroundSettings();
			DebugConsole = new DebugConsoleSettings();
		}

		public void Update(UserSettings settings)
		{
			SimulationSettings.Update(settings.SimulationSettings);
			ButtonSettings.Update(settings.ButtonSettings);
			InteractionSettings.Update(settings.InteractionSettings);
			if (settings.CustomBackground != null)
			{
				if (CustomBackground == null)
				{
					CustomBackground = new CustomBackgroundSettings();
				}
				CustomBackground.Update(settings.CustomBackground);
			}
			// 旧存档没有这一节，settings.DebugConsole 为 null 时保持默认（关闭）。
			if (settings.DebugConsole != null)
			{
				if (DebugConsole == null)
				{
					DebugConsole = new DebugConsoleSettings();
				}
				DebugConsole.Update(settings.DebugConsole);
			}
		}

		public void Reset()
		{
			Update(Default);
		}

		public static void Load()
		{
			Version version = Version.Parse(Application.version);
			Default = new UserSettings(version);
			Instance = new UserSettings(version);
			string path = Application.persistentDataPath + "/UserSettings.json";
			try
			{
				if (!File.Exists(path))
				{
					return;
				}
				using FileStream stream = File.OpenRead(path);
				UserSettings userSettings = Json.Deserialize<UserSettings>(stream);
				if (userSettings != null && userSettings.Version != null && userSettings.Version >= new Version(2023, 0, 0))
				{
					Instance.Update(userSettings);
				}
			}
			catch
			{
			}
		}

		public static void Save()
		{
			string persistentDataPath = Application.persistentDataPath;
			string path = persistentDataPath + "/UserSettings.json";
			try
			{
				if (!Directory.Exists(persistentDataPath))
				{
					Directory.CreateDirectory(persistentDataPath);
				}
				using FileStream stream = File.OpenWrite(path);
				Json.Serialize(stream, Instance);
			}
			catch
			{
			}
		}
	}
}
