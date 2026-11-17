using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bpsim.UI
{
	/// <summary>
	/// 全局界面配色注册表：内置「默认」+ 8 套彩色配色，选择持久化到 PlayerPrefs，
	/// 切换时广播 <see cref="Changed"/>，由 <see cref="UiThemeApplicator"/> 等订阅者上色
	/// （移植自 taffy 的 UiTheme；「默认」是本仓库补上的出厂配色选项）。
	/// </summary>
	public static class UiTheme
	{
		public const string PrefKey = "bpsim.ui.palette";

		public const string DefaultId = "default";

		public const string SaiId = "sai-dark";

		private static List<UiPalette> s_palettes;

		private static UiPalette s_current;

		public static event Action<UiPalette> Changed;

		public static IReadOnlyList<UiPalette> Palettes
		{
			get
			{
				if (s_palettes == null)
				{
					s_palettes = Build();
				}
				return s_palettes;
			}
		}

		public static UiPalette Current
		{
			get
			{
				if (s_current == null)
				{
					string id = PlayerPrefs.GetString(PrefKey, DefaultId);
					s_current = Find(id) ?? Palettes[0];
				}
				return s_current;
			}
		}

		public static UiPalette Find(string id)
		{
			if (string.IsNullOrEmpty(id))
			{
				return null;
			}
			foreach (UiPalette palette in Palettes)
			{
				if (palette.Id == id)
				{
					return palette;
				}
			}
			return null;
		}

		public static void Set(string id)
		{
			UiPalette palette = Find(id);
			if (palette == null || palette == s_current)
			{
				return;
			}
			s_current = palette;
			PlayerPrefs.SetString(PrefKey, id);
			PlayerPrefs.Save();
			Changed?.Invoke(palette);
		}

		private static List<UiPalette> Build()
		{
			return new List<UiPalette>
			{
				// 「默认」：出厂观感——原版深蓝按钮 (0,0.196875,0.4) + 0.16 灰面板。
				// 排在第一位，重置设置与首次启动都落到它。
				new UiPalette("default", "默认", new Color(0f, 0.196875f, 0.4f, 1f), new Color(0.18f, 0.78f, 0.36f, 1f), 0.16f),
				new UiPalette(SaiId, "SAI 深灰", Hex("606080"), Hex("43BA55"), 40f / 255f),
				new UiPalette("red-green", "红绿", Hex("E23B3B"), Hex("3FBF6B"), 0.17f),
				new UiPalette("blue-orange", "蓝橙", Hex("2F80ED"), Hex("F2994A"), 0.16f),
				new UiPalette("yellow-purple", "黄紫", Hex("F2C94C"), Hex("9B51E0"), 0.18f),
				new UiPalette("black-white", "黑白", Hex("F2F2F2"), Hex("8A8A8A"), 0.15f),
				new UiPalette("red-blue", "红蓝", Hex("EB5757"), Hex("2D9CDB"), 0.17f),
				new UiPalette("yellow-blue", "黄蓝", Hex("F2C94C"), Hex("2F80ED"), 0.17f),
				new UiPalette("pink-green", "粉绿", Hex("F06BA8"), Hex("27AE60"), 0.18f),
				new UiPalette("orange-purple", "橙紫", Hex("F2994A"), Hex("BB6BD9"), 0.17f)
			};
		}

		private static Color Hex(string value)
		{
			ColorUtility.TryParseHtmlString("#" + value, out Color color);
			return color;
		}
	}
}
