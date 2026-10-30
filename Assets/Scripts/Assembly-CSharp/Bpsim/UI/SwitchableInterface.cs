using System.Collections.Generic;
using UnityEngine;

namespace Bpsim.UI
{
	internal class SwitchableInterface : InterfaceBase
	{
		public struct UITransformValue
		{
			public Vector2 Position;

			public Vector2 Size;

			public UITransformValue(Vector2 position, Vector2 size)
			{
				Position = position;
				Size = size;
			}

			public UITransformValue(RectTransform rectTransform)
			{
				Position = rectTransform.anchoredPosition;
				Size = rectTransform.sizeDelta;
			}
		}

		[SerializeField]
		protected GameObject m_panel;

		protected bool m_inSidebar;

		protected float m_scale;

		protected float m_spacing;

		protected Vector2 m_padding;

		protected RectTransform m_rootTransform;

		protected UITransformValue m_rootTransformValue;

		protected Dictionary<RectTransform, UITransformValue> m_transformMap;

		public bool IsInSidebar => m_inSidebar;

		protected virtual void Awake()
		{
			m_spacing = 0f;
			m_scale = 1f;
			m_padding = Vector2.zero;
			Backup(m_panel);
		}

		private void Backup(GameObject panel)
		{
			m_rootTransform = (RectTransform)base.transform;
			m_rootTransformValue = new UITransformValue(m_rootTransform);
			m_transformMap = new Dictionary<RectTransform, UITransformValue>();
			for (int i = 0; i < panel.transform.childCount; i++)
			{
				if (panel.transform.GetChild(i) is RectTransform rectTransform)
				{
					m_transformMap.Add(rectTransform, new UITransformValue(rectTransform));
				}
			}
		}

		public void UpdateLayout(bool sidebar, Vector2 size)
		{
			if (m_inSidebar != sidebar)
			{
				m_inSidebar = sidebar;
				if (sidebar)
				{
					Move(size);
				}
				else
				{
					Restore();
				}
			}
		}

		private void Move(Vector2 size)
		{
			float num = 0f - m_padding.y;
			foreach (KeyValuePair<RectTransform, UITransformValue> item in m_transformMap)
			{
				RectTransform key = item.Key;
				float x = ((key.anchorMax.x - key.anchorMin.x < 1E-05f) ? m_padding.x : key.anchoredPosition.x);
				key.anchoredPosition = new Vector2(x, num);
				num -= item.Value.Size.y + m_spacing;
			}
			m_rootTransform.sizeDelta = new Vector2(size.x, 0f - num);
		}

		private void Restore()
		{
			Restore(m_rootTransform, m_rootTransformValue);
			m_rootTransform.anchorMin = new Vector2(0.5f, 0.5f);
			m_rootTransform.anchorMax = new Vector2(0.5f, 0.5f);
			foreach (KeyValuePair<RectTransform, UITransformValue> item in m_transformMap)
			{
				Restore(item.Key, item.Value);
			}
		}

		private void Restore(RectTransform rectTransform, UITransformValue value)
		{
			rectTransform.anchoredPosition = value.Position;
			rectTransform.sizeDelta = value.Size;
		}
	}
}
