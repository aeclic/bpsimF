using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.Serialization;

namespace Bpsim.UI
{
	internal class HoldableButton : UIBehaviour, IPointerDownHandler, IEventSystemHandler, IPointerUpHandler
	{
		[Serializable]
		public class ButtonClickedEvent : UnityEvent
		{
		}

		public enum ButtonState
		{
			None = 0,
			Pressed = 1,
			Holding = 2
		}

		[SerializeField]
		[FormerlySerializedAs("onPointerHold")]
		private ButtonClickedEvent m_OnPointerHold = new ButtonClickedEvent();

		private ButtonState m_state;

		private float m_threshold = 0.5f;

		private float m_pressTime;

		public ButtonClickedEvent onPointerHold
		{
			get
			{
				return m_OnPointerHold;
			}
			set
			{
				m_OnPointerHold = value;
			}
		}

		public void OnPointerDown(PointerEventData eventData)
		{
			if (IsActive() && eventData.button == PointerEventData.InputButton.Left)
			{
				m_state = ButtonState.Pressed;
				m_pressTime = Time.unscaledTime;
			}
		}

		public void OnPointerUp(PointerEventData eventData)
		{
			if (IsActive() && eventData.button == PointerEventData.InputButton.Left)
			{
				m_state = ButtonState.None;
				m_pressTime = 0f;
			}
		}

		private void Update()
		{
			if (m_state == ButtonState.Pressed && Time.unscaledTime - m_pressTime >= m_threshold)
			{
				m_state = ButtonState.Holding;
				m_OnPointerHold?.Invoke();
			}
		}
	}
}
