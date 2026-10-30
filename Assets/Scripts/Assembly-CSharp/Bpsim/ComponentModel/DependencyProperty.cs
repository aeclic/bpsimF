using System;
using System.Collections.Generic;

namespace Bpsim.ComponentModel
{
	public abstract class DependencyProperty
	{
		public abstract Type PropertyType { get; }

		public abstract object BoxedValue { get; set; }

		public abstract void Bind(Action subscriber);

		public abstract void Unbind();
	}
	public class DependencyProperty<T> : DependencyProperty
	{
		private T m_value;

		private Dependency m_dependency;

		public override Type PropertyType => typeof(T);

		public T RawValue
		{
			get
			{
				return m_value;
			}
			set
			{
				m_value = value;
			}
		}

		public T Value
		{
			get
			{
				return m_value;
			}
			set
			{
				if (!EqualityComparer<T>.Default.Equals(m_value, value))
				{
					m_value = value;
					m_dependency.Invoke();
				}
			}
		}

		public override object BoxedValue
		{
			get
			{
				return Value;
			}
			set
			{
				Value = (T)value;
			}
		}

		public DependencyProperty()
		{
			m_dependency = new Dependency();
		}

		public DependencyProperty(T rawValue)
		{
			m_value = rawValue;
			m_dependency = new Dependency();
		}

		public override void Bind(Action subscriber)
		{
			subscriber();
			m_dependency.Subscribe(subscriber);
		}

		public override void Unbind()
		{
			m_dependency.Clear();
		}
	}
}
