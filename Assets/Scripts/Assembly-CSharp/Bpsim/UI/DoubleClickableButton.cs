using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.Serialization;

namespace Bpsim.UI
{
	internal class DoubleClickableButton : UIBehaviour, IPointerClickHandler, IEventSystemHandler
	{
		[Serializable]
		public class ButtonClickedEvent : UnityEvent
		{
		}

		[SerializeField]
		[FormerlySerializedAs("onDoubleClick")]
		private ButtonClickedEvent m_OnDoubleClick = new ButtonClickedEvent();

		private ClickCounter m_counter;

		public ButtonClickedEvent onDoubleClick
		{
			get
			{
				return m_OnDoubleClick;
			}
			set
			{
				m_OnDoubleClick = value;
			}
		}

		protected override void Awake()
		{
			base.Awake();
			m_counter = new ClickCounter(0.3f);
		}

		public void OnPointerClick(PointerEventData eventData)
		{
			if (IsActive() && eventData.button == PointerEventData.InputButton.Left)
			{
				m_counter.Click();
				if (m_counter.ClickCount == 2)
				{
					m_OnDoubleClick?.Invoke();
				}
			}
		}
	}
}
