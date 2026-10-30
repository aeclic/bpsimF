using System;
using System.Collections.Generic;
using System.Reflection;

namespace Bpsim.ComponentModel
{
	public class DependencyProxy<TSource> : DependencyObject where TSource : ObservableObject
	{
		private TSource m_source;

		private Dictionary<string, DependencyProperty> m_properties;

		public TSource Source => m_source;

		public IReadOnlyDictionary<string, DependencyProperty> Properties => m_properties;

		public DependencyProxy(TSource source)
		{
			PropertyInfo[] properties = typeof(TSource).GetProperties();
			m_source = source;
			m_properties = new Dictionary<string, DependencyProperty>(properties.Length);
			PropertyInfo[] array = properties;
			foreach (PropertyInfo property in array)
			{
				Register(property);
			}
		}

		public DependencyProperty GetProperty(string name)
		{
			return m_properties[name];
		}

		public DependencyProperty<T> GetProperty<T>(string name)
		{
			return (DependencyProperty<T>)m_properties[name];
		}

		public void Register(string name)
		{
			Register(typeof(TSource).GetProperty(name));
		}

		public void Unregister(string name)
		{
			m_properties[name].Unbind();
			m_properties.Remove(name);
		}

		public void Unregister()
		{
			foreach (DependencyProperty value in m_properties.Values)
			{
				value.Unbind();
			}
		}

		private void Register(PropertyInfo property)
		{
			GetType().GetMethod("RegisterGeneric", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).MakeGenericMethod(property.PropertyType).Invoke(this, new object[1] { property });
		}

		private void RegisterGeneric<TProperty>(PropertyInfo property)
		{
			Func<TProperty> getter = (Func<TProperty>)Delegate.CreateDelegate(typeof(Func<TProperty>), m_source, property.GetGetMethod());
			Action<TProperty> setter = (Action<TProperty>)Delegate.CreateDelegate(typeof(Action<TProperty>), m_source, property.GetSetMethod());
			DependencyProperty<TProperty> dependencyProperty = new DependencyProperty<TProperty>();
			m_source.Bind(property.Name, delegate
			{
				dependencyProperty.Value = getter();
			});
			dependencyProperty.Bind(delegate
			{
				setter(dependencyProperty.Value);
				dependencyProperty.Value = getter();
			});
			m_properties.Add(property.Name, dependencyProperty);
		}
	}
}
