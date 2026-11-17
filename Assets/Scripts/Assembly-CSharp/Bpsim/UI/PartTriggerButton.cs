using Bpsim.Parts;
using Bpsim.Parts.Gameplay;
using Bpsim.Parts.Simulation;
using Unity.Entities;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Bpsim.UI
{
	public class PartTriggerButton : PartButton
	{
		private enum TriggerButtonState
		{
			Disabled = 0,
			Highlighted = 1,
			Pressed = 2,
			Enabled = 3
		}

		private UIButton m_button;

		private Image m_texture;

		private TriggerButtonState m_state;

		private bool m_consistent;

		private bool m_multiple;

		private Color m_color;

		private Color m_highlightedColor;

		private Color m_pressedColor;

		private const float UmbrellaDragThreshold = 4f;

		private const float UmbrellaDegreesPerPixel = 2f;

		private bool m_umbrellaPressPending;

		private bool m_umbrellaWasDragged;

		private Vector2 m_umbrellaPressPosition;

		private float m_umbrellaStartAngle;

		public bool Enabled => m_state == TriggerButtonState.Enabled;

		public bool Consistent => m_consistent;

		public bool Multiple => m_multiple;

		public UIButton Button => m_button;

		protected override void Awake()
		{
			base.Awake();
			m_button = base.transform.Find("Button").GetComponent<UIButton>();
			m_button.PointerDown += OnPointerDown;
			m_button.PointerClick += OnPointerClick;
			m_button.Drag += OnPointerDrag;
			m_texture = m_button.GetComponent<Image>();
			m_color = m_disabledColor;
		}

		private void OnEnable()
		{
			m_texture.canvasRenderer.SetColor(m_color);
		}

		public void SetConsistent(bool consistent)
		{
			m_consistent = consistent;
		}

		public void SetMultiple(bool multiple)
		{
			m_multiple = multiple;
		}

		public override void Initialize()
		{
			base.Initialize();
			m_highlightedColor = Color.Lerp(m_disabledColor, m_enabledColor, 0.5f);
			m_pressedColor = m_enabledColor;
			UpdateState(colorTint: false);
			m_texture.canvasRenderer.SetColor(m_color);
		}

		public void OnTriggered()
		{
			TriggerMultipleParts();
		}

		private void TriggerMultipleParts()
		{
			EntityManager entityManager = PartManager.Instance.System.EntityManager;
			if (!m_multiple)
			{
				// BPLE one-shot groups consume one live runtime body, not the entire cluster.
				PartSimulation.TriggerFirstEnabled(m_parts);
				return;
			}
			bool flag = false;
			foreach (Entity part in m_parts)
			{
				if (BasePart.IsEnabled(part, entityManager))
				{
					flag = true;
					break;
				}
			}
			foreach (Entity part2 in m_parts)
			{
				if (!m_consistent || (!flag ^ BasePart.IsEnabled(part2, entityManager)))
				{
					PartSimulation.SendButtonEvent(PartButtonEvent.CreateTriggerEvent(part2));
				}
			}
		}

		private void OnPointerDown(PointerEventData eventData)
		{
			if (eventData.button != PointerEventData.InputButton.Left)
			{
				return;
			}
			// Directional umbrellas have a single BPLE-style control: tap toggles the canopy,
			// horizontal drag adjusts the angle. Defer the toggle to PointerClick so a drag
			// does not also flip the umbrella.
			if (TryBeginUmbrellaDrag(eventData))
			{
				return;
			}
			// Non-directional parts and every other part type still stroke on press.
			OnTriggered();
		}

		private void OnPointerClick(PointerEventData eventData)
		{
			if (!m_umbrellaPressPending || m_umbrellaWasDragged)
			{
				m_umbrellaPressPending = false;
				m_umbrellaWasDragged = false;
				return;
			}
			m_umbrellaPressPending = false;
			TriggerMultipleParts();
		}

		private void OnPointerDrag(PointerEventData eventData)
		{
			if (!m_umbrellaPressPending || eventData.button != PointerEventData.InputButton.Left)
			{
				return;
			}
			float horizontal = eventData.position.x - m_umbrellaPressPosition.x;
			if (!m_umbrellaWasDragged)
			{
				if (Mathf.Abs(horizontal) < UmbrellaDragThreshold)
				{
					return;
				}
				m_umbrellaWasDragged = true;
			}
			ApplyUmbrellaAngle(UmbrellaAngleForDrag(m_umbrellaStartAngle, horizontal));
		}

		/// <summary>
		/// Pure angle mapping shared by the HUD and QA: two degrees per pixel, clamped to the
		/// BPLE slider range [-180, 180].
		/// </summary>
		internal static float UmbrellaAngleForDrag(float startAngle, float horizontalDelta)
		{
			return Mathf.Clamp(startAngle + UmbrellaDegreesPerPixel * horizontalDelta, -180f, 180f);
		}

		private bool TryBeginUmbrellaDrag(PointerEventData eventData)
		{
			if (!TryFindDirectionalUmbrella(out UmbrellaBehavior first))
			{
				return false;
			}
			m_umbrellaPressPending = true;
			m_umbrellaWasDragged = false;
			m_umbrellaPressPosition = eventData.position;
			m_umbrellaStartAngle = Mathf.Rad2Deg * first.DeltaAngle;
			return true;
		}

		private bool TryFindDirectionalUmbrella(out UmbrellaBehavior first)
		{
			first = null;
			if (m_info.PartType != PartType.Umbrella || PartGameplayManager.Instance == null)
			{
				return false;
			}
			foreach (Entity part in m_parts)
			{
				if (PartGameplayManager.Instance.Find(part) is UmbrellaBehavior umbrella
					&& (umbrella.Mode == 3 || umbrella.Mode == 5))
				{
					first = umbrella;
					return true;
				}
			}
			return false;
		}

		private void ApplyUmbrellaAngle(float angle)
		{
			if (PartGameplayManager.Instance == null)
			{
				return;
			}
			foreach (Entity part in m_parts)
			{
				if (PartGameplayManager.Instance.Find(part) is UmbrellaBehavior umbrella
					&& (umbrella.Mode == 3 || umbrella.Mode == 5))
				{
					PartSimulation.SendButtonEvent(PartButtonEvent.CreateSliderEvent(part, angle));
				}
			}
		}

		private void Update()
		{
			UpdateState(colorTint: true);
		}

		private void UpdateState(bool colorTint)
		{
			EntityManager entityManager = PartManager.Instance.System.EntityManager;
			bool flag = false;
			foreach (Entity part in m_parts)
			{
				if (BasePart.IsEnabled(part, entityManager))
				{
					flag = true;
					break;
				}
			}
			TriggerButtonState state = m_state;
			if (flag)
			{
				m_state = TriggerButtonState.Enabled;
				m_color = m_enabledColor;
			}
			else if (m_button.IsPointerDown && m_button.IsPointerInside)
			{
				m_state = TriggerButtonState.Pressed;
				m_color = m_pressedColor;
			}
			else if (m_button.IsPointerInside)
			{
				m_state = TriggerButtonState.Highlighted;
				m_color = m_highlightedColor;
			}
			else
			{
				m_state = TriggerButtonState.Disabled;
				m_color = m_disabledColor;
			}
			if (colorTint && state != m_state)
			{
				m_texture.CrossFadeColor(m_color, 0.1f, ignoreTimeScale: true, useAlpha: true);
			}
		}

		public override void Reset()
		{
			base.Reset();
			m_state = TriggerButtonState.Disabled;
			m_color = m_disabledColor;
			m_umbrellaPressPending = false;
			m_umbrellaWasDragged = false;
			m_umbrellaStartAngle = 0f;
			m_umbrellaPressPosition = Vector2.zero;
		}
	}
}
