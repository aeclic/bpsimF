using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Bpsim.UI
{
	/// <summary>滑条当前控制什么。</summary>
	internal enum HudSliderMode
	{
		/// <summary>建造界面：镜头缩放（0 = 最远、1 = 最近）。</summary>
		Zoom,

		/// <summary>模拟界面：时间倍速（0 = 0 倍、0.5 = 1 倍、1 = 2 倍）。</summary>
		TimeScale
	}

	/// <summary>
	/// 抬头播放键右边那条<b>横向</b>滑条，样式照 Besiege 的时间滑条：一条细灰横线、
	/// 一颗白色圆旋钮，左边是一个表示当前用途的图标（倍速时还有一行青色百分比），
	/// 没有填充条也没有底板。
	///
	/// 同一条滑条在两个界面里干两件事（Besiege 的时间滑条也是「建造时禁用、模拟时生效」）：
	/// 建造界面 = 镜头缩放，模拟界面 = 时间倍速。
	///
	/// 缩放只写 <see cref="T:Bpsim.SceneCamera" /> 的公开接口 <c>ScaleTo</c>，
	/// 数值按<b>指数</b>映射（线性映射会让前半段几乎看不出变化），
	/// 所以滚轮缩放与滑条永远落在同一个区间（<c>MinSize</c>..<c>MaxSize</c>）。
	///
	/// 倍速写 <c>Time.timeScale</c>：ECS 的 <c>UpdateWorldTimeSystem</c> 用
	/// <c>Time.deltaTime</c> 推进世界时间，<c>FixedStepSimulationSystemGroup</c> 的
	/// catch-up 速率跟着它走，于是<b>物理步长不变、每秒步数变</b> —— 慢放不会让物理变粗糙。
	/// </summary>
	internal class HudSlider : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
	{
		/// <summary>滑条拉到顶 = 2 倍速（Besiege 的百分比就是倍速 × 100）。</summary>
		internal const float MaxTimeScale = 2f;

		/// <summary>旋钮直径（Besiege 的旋钮在 1080p 下大约 15px）。</summary>
		private const float KnobSize = 16f;

		/// <summary>滑槽两端各留出的空白（半个旋钮 + 一点余量），旋钮不会跑出滑槽。</summary>
		private const float KnobInset = 12f;

		/// <summary>行内两个元素之间的空隙。缩放模式靠它保证「图标→滑槽」和「滑槽→图标」一样宽。</summary>
		private const float Gap = 12f;

		/// <summary>行首、行尾的留边。</summary>
		private const float Pad = 2f;

		/// <summary>模式图标 / 右端缩小图标的占位（方形）。</summary>
		private const float IconSlot = 22f;

		/// <summary>倍速百分比文字占的宽度（"200%" 在 16px 下约 36，留 44 够用）。</summary>
		private const float LabelWidth = 44f;

		private RectTransform m_track;

		private RectTransform m_knob;

		private Text m_label;

		private Image m_modeIcon;

		private Image m_tailIcon;

		private Sprite m_zoomIn;

		private Sprite m_zoomOut;

		private Sprite m_clock;

		private HudSliderMode m_mode = HudSliderMode.Zoom;

		/// <summary>缩放档位（-1 = 还没从相机读过）。</summary>
		private float m_zoomValue = -1f;

		/// <summary>倍速档位，0.5 = 1 倍。</summary>
		private float m_timeValue = 0.5f;

		private bool m_dragging;

		internal HudSliderMode Mode => m_mode;

		/// <summary>当前档位 0..1（缩放模式是缩放档，倍速模式是倍速档）。</summary>
		internal float Value => (m_mode == HudSliderMode.Zoom) ? m_zoomValue : m_timeValue;

		internal bool IsDragging => m_dragging;

		/// <summary>当前档位换算出来的时间倍速（缩放模式下也照样给，便于探针读数）。</summary>
		internal float TimeScale => m_timeValue * MaxTimeScale;

		/// <summary>顶上那行百分比文字（缩放模式为空）。</summary>
		internal string LabelText => (m_label == null || !m_label.enabled) ? null : m_label.text;

		internal void Initialize(RectTransform track, RectTransform knob, Text label, Image modeIcon, Image tailIcon, Sprite zoomIn, Sprite zoomOut, Sprite clock)
		{
			m_track = track;
			m_knob = knob;
			m_label = label;
			m_modeIcon = modeIcon;
			m_tailIcon = tailIcon;
			m_zoomIn = zoomIn;
			m_zoomOut = zoomOut;
			m_clock = clock;
			m_zoomValue = -1f;
			m_timeValue = 0.5f;
			m_mode = HudSliderMode.Zoom;
			SyncFromCamera();
			ApplyModeVisuals();
		}

		/// <summary>切到某个用途。从倍速切回缩放时会把全局时间恢复成 1 倍。</summary>
		internal void SetMode(HudSliderMode mode)
		{
			bool changed = (m_mode != mode);
			m_mode = mode;
			if (mode == HudSliderMode.TimeScale)
			{
				if (changed)
				{
					// 每次进模拟都从 100% 开始，不要沿用上一次的慢放。
					m_timeValue = 0.5f;
				}
				ApplyTimeScale();
			}
			else
			{
				RestoreTimeScale();
				SyncFromCamera();
			}
			ApplyModeVisuals();
			RefreshVisual();
		}

		/// <summary>直接设档位（0..1）。缩放模式改相机尺寸，倍速模式改全局时间。验收探针也走这里。</summary>
		internal void SetValue(float value)
		{
			value = Mathf.Clamp01(value);
			if (m_mode == HudSliderMode.Zoom)
			{
				m_zoomValue = value;
				ApplyZoom();
			}
			else
			{
				m_timeValue = value;
				ApplyTimeScale();
			}
			RefreshVisual();
		}

		/// <summary>把相机当前的缩放读回滑条。滚轮缩放之后旋钮会自己跟过去（只在缩放模式生效）。</summary>
		internal void SyncFromCamera()
		{
			if (m_dragging || m_mode != HudSliderMode.Zoom)
			{
				return;
			}
			SceneCamera camera = SceneCamera.Instance;
			if (camera == null)
			{
				return;
			}
			GetRange(out float min, out float max);
			float size = Mathf.Clamp(camera.TargetSize, min, max);
			float value = Mathf.Clamp01(1f - Mathf.Log(size / min) / Mathf.Log(max / min));
			if (Mathf.Abs(value - m_zoomValue) < 0.0005f)
			{
				return;
			}
			m_zoomValue = value;
			RefreshVisual();
		}

		public void OnPointerDown(PointerEventData eventData)
		{
			m_dragging = true;
			DragTo(eventData);
		}

		public void OnDrag(PointerEventData eventData)
		{
			DragTo(eventData);
		}

		public void OnPointerUp(PointerEventData eventData)
		{
			m_dragging = false;
			if (m_mode == HudSliderMode.Zoom)
			{
				SyncFromCamera();
			}
		}

		private void OnDisable()
		{
			m_dragging = false;
			RestoreTimeScale();
		}

		private void OnDestroy()
		{
			RestoreTimeScale();
		}

		private void DragTo(PointerEventData eventData)
		{
			if (m_track == null)
			{
				return;
			}
			if (RectTransformUtility.ScreenPointToLocalPointInRectangle(m_track, eventData.position, null, out Vector2 local))
			{
				// 和 RefreshVisual 用同一段行程，旋钮才会正好落在指针下面。
				// 滑槽的 pivot 在左边缘（anchor/pivot 都是 (0, 0.5)），所以 local.x 本身就是
				// 「距左边缘多远」，不能再按中心原点加半宽 —— 那是竖向版本的写法。
				float width = m_track.rect.width;
				float travel = Mathf.Max(1f, width - KnobInset * 2f);
				float fromLeft = local.x - m_track.rect.xMin;
				SetValue((fromLeft - KnobInset) / travel);
			}
		}

		private static void GetRange(out float min, out float max)
		{
			SceneCamera camera = SceneCamera.Instance;
			min = 1f;
			max = 500f;
			if (camera != null)
			{
				min = Mathf.Max(0.01f, camera.MinSize);
				max = Mathf.Max(min * 1.01f, camera.MaxSize);
			}
		}

		private void ApplyZoom()
		{
			SceneCamera camera = SceneCamera.Instance;
			if (camera == null)
			{
				return;
			}
			GetRange(out float min, out float max);
			camera.ScaleTo(min * Mathf.Pow(max / min, 1f - m_zoomValue));
		}

		private void ApplyTimeScale()
		{
			Time.timeScale = m_timeValue * MaxTimeScale;
		}

		private static void RestoreTimeScale()
		{
			if (!Mathf.Approximately(Time.timeScale, 1f))
			{
				Time.timeScale = 1f;
			}
		}

		/// <summary>图标和百分比随用途切换：缩放是上下两个放大镜、不显示数字；倍速是时钟 + 百分比。</summary>
		private void ApplyModeVisuals()
		{
			bool zoom = (m_mode == HudSliderMode.Zoom);
			if (m_modeIcon != null)
			{
				Sprite sprite = (zoom ? m_zoomIn : m_clock);
				m_modeIcon.sprite = sprite;
				m_modeIcon.enabled = (sprite != null);
			}
			if (m_tailIcon != null)
			{
				// 倍速只需要一个时钟，底端那个「缩小」图标会误导。
				m_tailIcon.gameObject.SetActive(zoom);
				if (zoom)
				{
					m_tailIcon.sprite = m_zoomOut;
					m_tailIcon.enabled = (m_zoomOut != null);
				}
			}
			if (m_label != null)
			{
				m_label.enabled = !zoom;
			}
			LayoutRow();
		}

		/// <summary>
		/// 按当前用途重排这一行，并同步收缩热区宽度。
		/// 缩放模式没有百分比文字，如果把它原来的槽位留在那里，图标和滑槽之间就会空出一大块
		/// （左边 64、右边 10）。这里直接按「谁可见就给谁让位」重排，
		/// 于是图标→滑槽、滑槽→图标的空隙都是 <see cref="Gap" />，左右对称。
		/// </summary>
		private void LayoutRow()
		{
			RectTransform rect = transform as RectTransform;
			if (rect == null || m_track == null)
			{
				return;
			}
			bool zoom = (m_mode == HudSliderMode.Zoom);
			float x = Pad;
			if (m_modeIcon != null)
			{
				PlaceInRow(m_modeIcon.rectTransform, ref x, IconSlot, IconSlot);
			}
			if (!zoom && m_label != null)
			{
				PlaceInRow(m_label.rectTransform, ref x, LabelWidth, -1f);
			}
			float trackWidth = m_track.sizeDelta.x;
			PlaceInRow(m_track, ref x, trackWidth, -1f);
			if (zoom && m_tailIcon != null)
			{
				PlaceInRow(m_tailIcon.rectTransform, ref x, IconSlot, IconSlot);
			}
			rect.sizeDelta = new Vector2(x - Gap + Pad, rect.sizeDelta.y);
		}

		/// <summary>把一项排到行内 <paramref name="x" /> 处并推进游标；<paramref name="height" /> ≤ 0 表示沿用原高。</summary>
		private static void PlaceInRow(RectTransform item, ref float x, float width, float height)
		{
			if (item == null)
			{
				return;
			}
			item.anchorMin = new Vector2(0f, 0.5f);
			item.anchorMax = new Vector2(0f, 0.5f);
			item.pivot = new Vector2(0f, 0.5f);
			item.sizeDelta = new Vector2(width, (height > 0f) ? height : item.sizeDelta.y);
			item.anchoredPosition = new Vector2(x, 0f);
			x += width + Gap;
		}

		private void RefreshVisual()
		{
			if (m_track == null)
			{
				return;
			}
			float width = m_track.rect.width;
			float travel = Mathf.Max(1f, width - KnobInset * 2f);
			float value = Mathf.Clamp01(Value);
			if (m_knob != null)
			{
				m_knob.sizeDelta = new Vector2(KnobSize, KnobSize);
				m_knob.anchoredPosition = new Vector2(KnobInset + travel * value, 0f);
			}
			if (m_label != null && m_label.enabled)
			{
				m_label.text = Mathf.RoundToInt(m_timeValue * MaxTimeScale * 100f) + "%";
			}
		}
	}
}
