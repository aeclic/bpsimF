using UnityEngine;

namespace Bpsim.UI
{
	/// <summary>
	/// 一套界面配色：只存 主色/副色/灰度基准 三个自由度，
	/// 面板、按钮、文字等全部颜色都由 <see cref="Shade"/> / <see cref="Mix"/> 推导，
	/// 保证任意配色下明暗层次一致（移植自 taffy 的 UiPalette）。
	/// </summary>
	public sealed class UiPalette
	{
		public readonly string Id;

		public readonly string Name;

		public readonly Color Accent;

		public readonly Color Second;

		public readonly float Gray;

		public bool IsSai => Id == UiTheme.SaiId;
		private static Color Rgb(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f, 1f);

		public UiPalette(string id, string name, Color accent, Color second, float gray)
		{
			Id = id;
			Name = name;
			Accent = accent;
			Second = second;
			Gray = gray;
		}

		/// <summary>灰度基准乘一个明暗系数：&lt;1 更暗，&gt;1 更亮。</summary>
		public Color Shade(float factor)
		{
			float v = Mathf.Clamp01(Gray * factor);
			return new Color(v, v, v, 1f);
		}

		/// <summary>把灰度基准向某个色调混合，amount 越大越接近该色调。</summary>
		public Color Mix(Color tint, float amount)
		{
			return Color.Lerp(Shade(1f), tint, Mathf.Clamp01(amount));
		}

		public Color Backdrop => new Color(0f, 0f, 0f, 0.62f);

		public Color AppBackground => Shade(0.62f);

		// 带少量主题色的冷灰底板：主色再亮，也不会让整片 UI 发白。
		public Color Panel => IsSai ? Rgb(40, 40, 40) : Color.Lerp(new Color(Gray * 0.86f, Gray * 0.95f, Gray * 1.1f, 1f), Accent, 0.035f);

		public Color Header => IsSai ? Rgb(45, 45, 45) : Color.Lerp(Panel, Accent, 0.1f);

		public Color Entry => IsSai ? Rgb(40, 40, 40) : Color.Lerp(Panel, Color.white, 0.045f);

		public Color Field => IsSai ? Rgb(32, 32, 32) : Color.Lerp(Panel, Color.black, 0.22f);

		public Color BarButton => IsSai ? Rgb(96, 96, 96) : Color.Lerp(Panel, Color.white, 0.1f);

		public Color Button => IsSai ? Rgb(96, 96, 96) : Mix(Accent, 0.32f);

		/// <summary>工具栏按钮底板：默认配色即原版深蓝，彩色配色向主色靠拢。</summary>
		public Color ToolbarButton => IsSai ? Rgb(45, 45, 45) : Mix(Accent, Id == UiTheme.DefaultId ? 1f : 0.36f);

		public Color Selection => IsSai ? Rgb(96, 96, 128) : ToolbarButton;
		public Color SelectionEdge => IsSai ? Rgb(135, 135, 195) : AccentBright;
		public Color SectionText => IsSai ? Rgb(160, 128, 64) : TextMuted;

		/// <summary>「激活/播放」语义绿：任何配色下都保持绿色系，只随副色微调。</summary>
		public Color Success => Color.Lerp(new Color(0.11f, 0.52f, 0.23f, 1f), Second, 0.18f);

		/// <summary>
		/// 高亮强调文字（如 HUD 倍速百分比）：默认配色保留原版 Besiege 青色，
		/// 其余配色把主色提亮到深色面板上可读的程度。
		/// </summary>
		public Color AccentBright => IsSai ? Rgb(135, 135, 195) : Id == "default"
			? new Color(0.075f, 0.922f, 0.753f, 1f)
			: Color.Lerp(Accent, Color.white, 0.45f);

		public Color ButtonMuted => IsSai ? Rgb(45, 45, 45) : Color.Lerp(Panel, Color.white, 0.09f);

		public Color Danger => new Color(0.55f, 0.16f, 0.16f, 1f);

		public Color DangerStrong => new Color(0.72f, 0.2f, 0.2f, 1f);

		public Color Text => IsSai ? Rgb(208, 208, 208) : new Color(0.96f, 0.96f, 0.97f, 1f);

		public Color TextMuted => IsSai ? Rgb(154, 154, 154) : new Color(0.83f, 0.87f, 0.93f, 0.74f);

		public Color TextOnLight => new Color(0.1f, 0.1f, 0.12f, 1f);

		public Color Divider => IsSai ? Rgb(72, 72, 72) : new Color(0.78f, 0.86f, 1f, 0.13f);

		public Color SliderTrack => IsSai ? Rgb(32, 32, 32) : Shade(0.78f);

		public Color SliderFill => Accent;

		public Color Handle => new Color(0.93f, 0.93f, 0.94f, 1f);

		public Color ScrollTrack => IsSai ? Rgb(32, 32, 32) : new Color(1f, 1f, 1f, 0.05f);

		public Color ScrollHandle => IsSai ? Rgb(96, 96, 96) : Mix(Accent, 0.35f);

		public Color Hover => new Color(Accent.r, Accent.g, Accent.b, 0.16f);
	}
}
