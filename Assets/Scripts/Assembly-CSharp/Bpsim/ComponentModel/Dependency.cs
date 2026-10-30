using System;
using System.Collections.Immutable;

namespace Bpsim.ComponentModel
{
	public class Dependency
	{
		private ImmutableArray<Action> m_subscribers;

		public ImmutableArray<Action> Subscribers => m_subscribers;

		public Dependency()
		{
			m_subscribers = ImmutableArray<Action>.Empty;
		}

		public bool Contains(Action subscriber)
		{
			return m_subscribers.Contains(subscriber);
		}

		public void Subscribe(Action subscriber)
		{
			if (!m_subscribers.Contains(subscriber))
			{
				m_subscribers = m_subscribers.Add(subscriber);
			}
		}

		public void Unsubscribe(Action subscriber)
		{
			m_subscribers = m_subscribers.Remove(subscriber);
		}

		public void Invoke()
		{
			ImmutableArray<Action>.Enumerator enumerator = m_subscribers.GetEnumerator();
			while (enumerator.MoveNext())
			{
				enumerator.Current();
			}
		}

		public void Clear()
		{
			m_subscribers.Clear();
		}
	}
}
