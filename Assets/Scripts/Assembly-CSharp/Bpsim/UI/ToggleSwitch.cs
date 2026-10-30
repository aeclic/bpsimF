using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Bpsim.UI
{
	public class ToggleSwitch : Selectable, IPointerClickHandler, IEventSystemHandler, ISubmitHandler, ICanvasElement
	{
		[Serializable]
		public class ToggleEvent : UnityEvent<bool>
		{
		}

		[SerializeField]
		private bool m_isOn;

		[SerializeField]
		private Image m_border;

		[SerializeField]
		private Image m_background;

		[SerializeField]
		private Image m_ellipse;

		[SerializeField]
		private Color m_enabledColor;

		[SerializeField]
		private Color m_disabledColor;

		private ToggleEvent m_onValueChanged = new ToggleEvent();

		public bool IsOn
		{
			get
			{
				return m_isOn;
			}
			set
			{
				Set(value);
			}
		}

		public ToggleEvent OnValueChanged => m_onValueChanged;

		Transform ICanvasElement.transform => base.transform;

		protected ToggleSwitch()
		{
		}

		protected override void Start()
		{
			ResolveVisualReferences();
			PlayEffect(instant: true);
		}

		protected override void OnEnable()
		{
			base.OnEnable();
			ResolveVisualReferences();
			PlayEffect(instant: true);
		}

		protected override void OnDisable()
		{
			base.OnDisable();
		}

		public void SetIsOnWithoutNotify(bool value)
		{
			Set(value, sendCallback: false);
		}

		private void Set(bool value, bool sendCallback = true)
		{
			if (m_isOn != value)
			{
				m_isOn = value;
				PlayEffect(instant: false);
				if (sendCallback)
				{
					m_onValueChanged.Invoke(m_isOn);
				}
			}
		}

		private void PlayEffect(bool instant)
		{
			ResolveVisualReferences();
			float duration = (instant ? 0f : 0.2f);
			if (m_isOn)
			{
				UIExtensions.PlayLinearAnimation(0f, 1f, duration, ignoreTimeScale: true, PlayAnimation).Forget();
			}
			else
			{
				UIExtensions.PlayLinearAnimation(1f, 0f, duration, ignoreTimeScale: true, PlayAnimation).Forget();
			}
		}

		private void ResolveVisualReferences()
		{
			if (m_background == null)
			{
				m_background = base.transform.Find("Background")?.GetComponent<Image>();
			}
			if (m_ellipse == null)
			{
				m_ellipse = base.transform.Find("Ellipse")?.GetComponent<Image>();
			}
		}

		private void PlayAnimation(float t)
		{
			// A binding can outlive a dynamically rebuilt settings row. The animation task
			// may then run one more frame after its graphics have been destroyed.
			if (!isActiveAndEnabled || m_background == null || m_ellipse == null
				|| !m_background.isActiveAndEnabled || !m_ellipse.isActiveAndEnabled)
			{
				return;
			}
			CanvasRenderer backgroundRenderer = m_background.canvasRenderer;
			CanvasRenderer ellipseRenderer = m_ellipse.canvasRenderer;
			RectTransform ellipseTransform = m_ellipse.rectTransform;
			if (backgroundRenderer == null || ellipseRenderer == null || ellipseTransform == null)
			{
				return;
			}
			backgroundRenderer.SetAlpha(t);
			ellipseRenderer.SetColor(Color.Lerp(m_disabledColor, m_enabledColor, t));
			ellipseTransform.anchoredPosition = ellipseTransform.anchoredPosition.WithX(-50f + 100f * t);
		}

		private void InternalToggle()
		{
			if (IsActive() && IsInteractable())
			{
				Set(!m_isOn);
			}
		}

		public virtual void OnPointerClick(PointerEventData eventData)
		{
			if (eventData.button == PointerEventData.InputButton.Left)
			{
				InternalToggle();
			}
		}

		public virtual void OnSubmit(BaseEventData eventData)
		{
			InternalToggle();
		}

		public void Rebuild(CanvasUpdate executing)
		{
		}

		public void LayoutComplete()
		{
		}

		public void GraphicUpdateComplete()
		{
		}
	}
}
