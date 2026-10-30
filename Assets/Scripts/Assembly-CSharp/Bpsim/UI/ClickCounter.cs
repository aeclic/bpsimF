using UnityEngine;

namespace Bpsim.UI
{
	public class ClickCounter
	{
		private float m_threshold;

		private float m_clickTime;

		private int m_clickCount;

		public int ClickCount => m_clickCount;

		public ClickCounter(float threshold)
		{
			m_threshold = threshold;
		}

		public void Click()
		{
			if (Time.unscaledTime - m_clickTime > m_threshold)
			{
				m_clickCount = 0;
			}
			m_clickCount++;
			m_clickTime = Time.unscaledTime;
		}
	}
}
