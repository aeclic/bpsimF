using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.UI
{
	/// <summary>
	/// 标记「颜色完全由自己的逻辑管理」的 UI 子树（如 <see cref="BuildHud"/> 的画布）。
	/// <see cref="UiThemeApplicator"/> 的通用启发式会跳过这些子树，避免双方互相覆盖。
	/// </summary>
	internal sealed class UiThemeOwned : MonoBehaviour
	{
	}

	/// <summary>
	/// 把当前 <see cref="UiTheme"/> 配色刷到一棵 UI 子树上：
	/// 按物体名 / 首次上色时的颜色启发式分类 Graphic；交互控件（Button / Toggle /
	/// InputField / Dropdown / Slider / Scrollbar）的 targetGraphic 也按同一套规则上色，
	/// 并把 ColorBlock 归一化成相对底板的明暗乘数，保证悬停/按下/禁用反馈随配色走。
	/// 跳过 Swatch 色板与 <see cref="UiThemeOwned"/> 标记的自管理子树；
	/// 主题只改变 Graphic.color；CanvasRenderer 的乘色通道留给 Selectable 的交互反馈。
	/// （移植自 taffy 的 UiThemeApplicator；IL 中被抹掉的泛型 GetComponent 类型按本仓库控件集实现）。
	/// </summary>
	public sealed class UiThemeApplicator : MonoBehaviour
	{
		public static float Duration = 0.28f;

		private bool m_applying;

		private void OnEnable()
		{
			UiTheme.Changed += OnThemeChanged;
			Apply(UiTheme.Current, instant: true);
		}

		private void OnDisable()
		{
			UiTheme.Changed -= OnThemeChanged;
		}

		private void OnThemeChanged(UiPalette palette)
		{
			Apply(palette, instant: false);
		}

		/// <summary>新面板立即上色；主 UI 下由根负责换色，独立面板自动订阅。</summary>
		internal static void ApplyTo(GameObject root)
		{
			if (root == null)
			{
				return;
			}
			// 独立画布/弹窗也能持续响应换色；主 UI 下的面板不重复订阅。
			if (root.GetComponentInParent<UiThemeApplicator>(true) == null)
			{
				root.AddComponent<UiThemeApplicator>();
			}
			ApplyTree(root, UiTheme.Current, instant: true);
		}

		private void Apply(UiPalette palette, bool instant)
		{
			if (m_applying || palette == null)
			{
				return;
			}
			m_applying = true;
			try
			{
				ApplyTree(gameObject, palette, instant);
			}
			finally
			{
				m_applying = false;
			}
		}

		private static void ApplyTree(GameObject root, UiPalette palette, bool instant)
		{
			foreach (Selectable selectable in root.GetComponentsInChildren<Selectable>(true))
			{
				if (!IsExcluded(selectable.transform)) BirthColors(selectable);
			}
			foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(includeInactive: true))
			{
				if (TryClassify(graphic, palette, out Color target))
				{
					Paint(graphic, target, instant);
				}
			}
			ApplySelectables(root, palette, instant);
			foreach (ToggleSwitch toggle in root.GetComponentsInChildren<ToggleSwitch>(includeInactive: true))
			{
				if (!HasInactiveTemplateAncestor(toggle.transform)) toggle.ApplyPalette(palette);
			}
		}

		private static bool HasInactiveTemplateAncestor(Transform transform)
		{
			for (Transform t = transform; t != null; t = t.parent)
				if (!t.gameObject.activeSelf && t.name.Contains("Template")) return true;
			return false;
		}

		/// <summary>
		/// 交互控件本体的底板 + ColorBlock。底板走与普通 Graphic 相同的分类；
		/// ColorBlock 归一化为乘数（正常/选中=白、悬停 1.15、按下 0.8、禁用 0.78 半透明），
		/// 这样任何配色下都有统一的交互反馈。transition=None 的按钮只刷底板——
		/// 它们的禁用/激活色由控件自己的逻辑负责。
		/// </summary>
		private static void ApplySelectables(GameObject root, UiPalette palette, bool instant)
		{
			foreach (Selectable selectable in root.GetComponentsInChildren<Selectable>(includeInactive: true))
			{
				if (selectable == null || selectable is ToggleSwitch)
				{
					continue;
				}
				GameObject go = selectable.gameObject;
				if (IsExcluded(go.transform))
				{
					continue;
				}
				// Scrollbar / Slider 的轨道与滑块由 UIExtensions.StyleScrollbars 或构建代码
				// 直接按配色上色；通用启发式会把它们误判成按钮/面板，这里只归一化 ColorBlock。
				if (selectable is Scrollbar || selectable is Slider)
				{
					NormalizeColorBlock(selectable, palette);
					continue;
				}
				Graphic plate = selectable.targetGraphic;
				if (plate != null && TryClassify(plate, palette, out Color target))
				{
					Paint(plate, target, instant);
					NormalizeColorBlock(selectable, palette);
				}
			}
		}

		/// <summary>
		/// ColorTint 控件的 ColorBlock 归一化为相对底板的明暗乘数：
		/// 正常/选中=白、悬停 1.15、按下 0.8、禁用 0.78 半透明。
		/// transition=None 的控件不动——它们的禁用/激活色由控件自己的逻辑负责。
		/// </summary>
		private static void NormalizeColorBlock(Selectable selectable, UiPalette palette)
		{
			if (selectable.transition != Selectable.Transition.ColorTint)
			{
				return;
			}
			ColorBlock colors = BirthColors(selectable);
			if (palette.Id == UiTheme.DefaultId && !(selectable is Slider) && !(selectable is Scrollbar))
			{
				selectable.colors = colors;
				return;
			}
			colors.normalColor = Color.white;
			colors.selectedColor = Color.white;
			colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
			colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
			colors.disabledColor = new Color(0.78f, 0.78f, 0.78f, 0.5f);
			colors.colorMultiplier = 1f;
			colors.fadeDuration = 0.12f;
			selectable.colors = colors;
		}

		private sealed class ColorBlockBox { public ColorBlock Value; }
		private static readonly ConditionalWeakTable<Selectable, ColorBlockBox> s_originalBlocks
			= new ConditionalWeakTable<Selectable, ColorBlockBox>();
		private static ColorBlock BirthColors(Selectable selectable)
			=> s_originalBlocks.GetValue(selectable, s => new ColorBlockBox { Value = s.colors }).Value;

		private static bool IsExcluded(Transform transform)
		{
			if (HasInactiveTemplateAncestor(transform)) return true;
			for (Transform t = transform; t != null; t = t.parent)
			{
				if (t.name.StartsWith("Swatch_") || t.GetComponent<UiThemeOwned>() != null
					|| t.GetComponent<ToggleSwitch>() != null) return true;
			}
			return false;
		}

		internal static void StyleSurface(Graphic graphic, UiSurfaceRole role)
		{
			if (graphic == null) return;
			var surface = graphic.GetComponent<UiThemeSurface>() ?? graphic.gameObject.AddComponent<UiThemeSurface>();
			surface.Role = role;
			graphic.color = surface.Color(UiTheme.Current);
		}

		private sealed class ColorBox
		{
			public Color Value;
		}

		/// <summary>
		/// 每个 Graphic 首次被分类时的颜色。之后切换配色都按这份「出生色」分类，
		/// 避免上一轮染上的主题色被误判成另一种角色（例如红色危险按钮被当成灰色按钮）。
		/// 弱引用表：Graphic 销毁后条目自动回收。
		/// </summary>
		private static readonly ConditionalWeakTable<Graphic, ColorBox> s_originalColors
			= new ConditionalWeakTable<Graphic, ColorBox>();

		private static Color BirthColor(Graphic graphic)
		{
			return s_originalColors.GetValue(graphic, g => new ColorBox { Value = g.color }).Value;
		}

		private static bool TryClassify(Graphic graphic, UiPalette palette, out Color target)
		{
			target = default(Color);
			if (graphic == null || IsExcluded(graphic.transform))
			{
				return false;
			}
			GameObject go = graphic.gameObject;
			string name = go.name;
			var surface = go.GetComponent<UiThemeSurface>();
			if (surface != null)
			{
				target = surface.Color(palette);
				return true;
			}
			Color color = BirthColor(graphic);
			bool classified = ClassifyGraphic(graphic, color, palette, out target);
			if (classified && palette.Id == UiTheme.DefaultId && !IsTrackOrHandle(graphic)) target = color;
			return classified;
		}

		private static bool IsTrackOrHandle(Graphic graphic)
		{
			for (Transform t = graphic.transform; t != null; t = t.parent)
			{
				if (t.GetComponent<Slider>() != null || t.GetComponent<Scrollbar>() != null) return true;
				if (t.GetComponent<Canvas>() != null) break;
			}
			return false;
		}

		private static Selectable OwnerOf(Graphic graphic)
		{
			for (Transform t = graphic.transform; t != null; t = t.parent)
			{
				Selectable owner = t.GetComponent<Selectable>();
				if (owner != null && owner.targetGraphic == graphic) return owner;
				if (t.GetComponent<Canvas>() != null) break;
			}
			return null;
		}

		private static bool ClassifyGraphic(Graphic graphic, Color color, UiPalette palette, out Color target)
		{
			target = default(Color);
			GameObject go = graphic.gameObject;
			string name = go.name;
			if (!(graphic is Text) && color.a > 0.15f && name != "Icon" && !name.StartsWith("Icon_"))
			{
				Selectable owner = OwnerOf(graphic);
				bool isPlate = !(graphic is RawImage rawPlate) || rawPlate.texture == null;
				if (owner != null && isPlate)
				{
					if (owner is InputField || owner is Dropdown) { target = palette.Field; return true; }
					if (owner is Scrollbar) { target = palette.ScrollHandle; return true; }
					if (owner is Slider) { target = palette.Handle; return true; }
					// Simple 无边框精灵通常是按钮的内容图标，不是可换色底板。
					if (graphic is Image glyph && glyph.sprite != null && glyph.type == Image.Type.Simple
						&& glyph.sprite.border.sqrMagnitude == 0f && !glyph.sprite.name.StartsWith("Background")) return false;
					Color effective = color * BirthColors(owner).normalColor;
					// 正常态透明的 targetGraphic 是点击区域/悬停覆盖层，不能变成实心色块。
					if (effective.a <= 0.15f) return false;
					return ClassifyButtonColor(effective, palette, out target);
				}
			}
			if (graphic is Text)
			{
				if (color.a < 0.2f)
				{
					return false;
				}
				// 饱和的彩色文字是刻意的强调色（如 HUD 倍速百分比），随主色走。
				float max = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
				float min = Mathf.Min(color.r, Mathf.Min(color.g, color.b));
				if (max > 0.001f && (max - min) / max > 0.3f)
				{
					target = new Color(palette.AccentBright.r, palette.AccentBright.g, palette.AccentBright.b, color.a);
					return true;
				}
				if (Grayscale(color) < 0.45f)
				{
					return false;
				}
				target = color.a >= 0.85f
					? new Color(palette.Text.r, palette.Text.g, palette.Text.b, color.a)
					: palette.TextMuted;
				return true;
			}
			if (graphic is Image image)
			{
				if (image.sprite != null)
				{
					if (image.sprite.name.StartsWith("Background")
						|| name == "Panel" || name.EndsWith("Sidebar") || name == "Header")
					{
						return ClassifyBlock(go, color, palette, out target);
					}
					if (image.type == Image.Type.Sliced || image.sprite.border.sqrMagnitude > 0f)
						return ClassifyBlock(go, color, palette, out target);
					return false;
				}
				if (color.a >= 0.9f)
				{
					return ClassifyBlock(go, color, palette, out target);
				}
				return false;
			}
			if (graphic is RawImage raw && raw.texture == null && color.a >= 0.9f)
			{
				return ClassifyBlock(go, color, palette, out target);
			}
			return false;
		}

		private static bool ClassifyBlock(GameObject go, Color color, UiPalette palette, out Color target)
		{
			target = default(Color);
			string name = go.name;
			if (name == "Header")
			{
				target = palette.Header;
				return true;
			}
			if (name == "Foreground")
			{
				target = palette.Accent;
				return true;
			}
			if (name.StartsWith("Button_") || go.GetComponent<Button>() != null)
			{
				return ClassifyButtonColor(color, palette, out target);
			}
			if (name.Contains("Input") || name == "Value" || name == "Dropdown"
				|| go.GetComponent<InputField>() != null || go.GetComponent<Dropdown>() != null)
			{
				target = palette.Field;
				return true;
			}
			if (name.Contains("Entry") || name.Contains("Template"))
			{
				target = palette.Entry;
				return true;
			}
			if (name == "Panel" || name.EndsWith("Sidebar") || name == "ActionBar" || name == "SceneTabView")
			{
				target = palette.Panel;
				return true;
			}
			Transform parent = go.transform.parent;
			if (name == "Background" && Grayscale(color) < 0.35f && parent != null
				&& parent.GetComponent<Slider>() != null)
			{
				target = palette.SliderTrack;
				return true;
			}
			if (name == "Fill")
			{
				target = palette.SliderFill;
				return true;
			}
			if (name == "Handle")
			{
				Transform handleParent = go.transform.parent;
				if (handleParent != null && handleParent.parent != null && handleParent.parent.GetComponent<Scrollbar>() != null)
				{
					target = palette.ScrollHandle;
				}
				else
				{
					target = palette.Handle;
				}
				return true;
			}
			if (name == "Background") { target = palette.Panel; return true; }
			// 滚动条轨道的精灵常被 StyleScrollbars 置空，按组件类型兜底。
			if (go.GetComponent<Scrollbar>() != null)
			{
				target = palette.ScrollTrack;
				return true;
			}
			return false;
		}

		private static bool ClassifyButtonColor(Color color, UiPalette palette, out Color target)
		{
			target = default(Color);
			float gray = Grayscale(color);
			if (gray > 0.9f) { target = palette.Button; return true; }
			if (gray > 0.72f)
			{
				target = palette.ButtonMuted;
				return true;
			}
			float r = color.r;
			float g = color.g;
			float b = color.b;
			if (r > 0.35f && r > g * 1.6f && r > b * 1.6f)
			{
				target = palette.Danger;
				return true;
			}
			if (g > r * 1.25f && g > b * 1.1f)
			{
				target = palette.Mix(palette.Second, 0.5f);
				return true;
			}
			if (Mathf.Abs(r - g) < 0.06f && Mathf.Abs(g - b) < 0.06f)
			{
				target = palette.BarButton;
				return true;
			}
			target = palette.Button;
			return true;
		}

		private static float Grayscale(Color color)
		{
			return color.grayscale;
		}

		private static void Paint(Graphic graphic, Color target, bool instant)
		{
			var tween = graphic.GetComponent<UiThemeColorTween>();
			if (instant || !graphic.gameObject.activeInHierarchy || Duration <= 0f)
			{
				if (tween != null) tween.Set(graphic, target, 0f);
				graphic.color = target;
			}
			else
			{
				if (tween == null) tween = graphic.gameObject.AddComponent<UiThemeColorTween>();
				tween.Set(graphic, target, Duration);
			}
		}
	}

	internal enum UiSurfaceRole { Panel, Header, Entry, Divider }
	internal sealed class UiThemeSurface : MonoBehaviour
	{
		public UiSurfaceRole Role;
		public Color Color(UiPalette palette)
		{
			switch (Role)
			{
			case UiSurfaceRole.Header: return palette.Header;
			case UiSurfaceRole.Entry: return palette.Entry;
			case UiSurfaceRole.Divider: return palette.Divider;
			default: return palette.Panel;
			}
		}
	}

	// 独立的顶点色渐变；重复切换从当前色重新开始，不累积 coroutine 或渲染乘色。
	internal sealed class UiThemeColorTween : MonoBehaviour
	{
		private Graphic m_graphic;
		private Color m_from, m_target;
		private float m_elapsed, m_duration;
		public void Set(Graphic graphic, Color target, float duration)
		{
			m_graphic = graphic;
			m_from = graphic.color;
			m_target = target;
			m_elapsed = 0f;
			m_duration = duration;
			if (duration <= 0f || !gameObject.activeInHierarchy)
			{
				graphic.color = target;
				enabled = false;
			}
			else enabled = true;
		}
		private void LateUpdate()
		{
			if (m_graphic == null) { enabled = false; return; }
			m_elapsed += Time.unscaledDeltaTime;
			float t = Mathf.Clamp01(m_elapsed / m_duration);
			m_graphic.color = Color.Lerp(m_from, m_target, t * t * (3f - 2f * t));
			if (t >= 1f) enabled = false;
		}
		private void OnDisable()
		{
			if (m_graphic != null) m_graphic.color = m_target;
			enabled = false;
		}
	}
}
