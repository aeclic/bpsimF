using System;
using System.ComponentModel;
using System.Globalization;
using Bpsim.UI;
using UnityEngine.UI;

namespace Bpsim.ComponentModel
{
	public static class Binding
	{
		public static void Bind(this ObservableObject source, string propertyName, Action subscriber)
		{
			subscriber();
			source.PropertyChanged += delegate(object sender, PropertyChangedEventArgs args)
			{
				if (args.PropertyName == propertyName)
				{
					subscriber();
				}
			};
		}

		public static void Bind<T>(this DependencyProperty<T> target, DependencyProperty<T> source)
		{
			source.Bind(delegate
			{
				target.Value = source.Value;
			});
			target.Bind(delegate
			{
				source.Value = target.Value;
				target.Value = source.Value;
			});
		}

		public static void Bind(this Toggle toggle, DependencyProperty<bool> property)
		{
			property.Bind(delegate
			{
				toggle.isOn = property.Value;
			});
			toggle.onValueChanged.AddListener(delegate(bool value)
			{
				property.Value = value;
				toggle.isOn = property.Value;
			});
		}

		public static void Bind(this ToggleSwitch toggleSwitch, DependencyProperty<bool> property)
		{
			property.Bind(delegate
			{
				toggleSwitch.IsOn = property.Value;
			});
			toggleSwitch.OnValueChanged.AddListener(delegate(bool value)
			{
				property.Value = value;
				toggleSwitch.IsOn = property.Value;
			});
		}

		public static void Bind(this Dropdown dropdown, DependencyProperty<int> property)
		{
			property.Bind(delegate
			{
				dropdown.value = property.Value;
			});
			dropdown.onValueChanged.AddListener(delegate(int value)
			{
				property.Value = value;
				dropdown.value = property.Value;
			});
		}

		public static void Bind<T>(this Dropdown dropdown, DependencyProperty<T> property)
		{
			property.Bind(delegate
			{
				dropdown.value = (int)(object)property.Value;
			});
			dropdown.onValueChanged.AddListener(delegate(int value)
			{
				property.Value = (T)(object)value;
				dropdown.value = (int)(object)property.Value;
			});
		}

		public static void Bind<T>(this InputField target, DependencyProperty<T> property)
		{
			target.Bind(property, Parser<T>.Default);
		}

		public static void Bind<T>(this InputField target, DependencyProperty<T> property, IParser<T> parser)
		{
			property.Bind(delegate
			{
				target.text = parser.Write(property.Value, CultureInfo.InvariantCulture);
			});
			target.onEndEdit.AddListener(delegate(string text)
			{
				if (parser.TryRead(text, CultureInfo.InvariantCulture, out var result))
				{
					property.Value = result;
				}
				target.text = parser.Write(property.Value, CultureInfo.InvariantCulture);
			});
		}

		public static void Bind<TProperty, TElement>(this InputField target, DependencyProperty<TProperty> property, IParser<TElement> parser, Func<TProperty, TElement> getter, Func<TProperty, TElement, TProperty> setter)
		{
			property.Bind(delegate
			{
				target.text = parser.Write(getter(property.Value), CultureInfo.InvariantCulture);
			});
			target.onEndEdit.AddListener(delegate(string text)
			{
				if (parser.TryRead(text, CultureInfo.InvariantCulture, out var result))
				{
					property.Value = setter(property.Value, result);
				}
				target.text = parser.Write(getter(property.Value), CultureInfo.InvariantCulture);
			});
		}
	}
}
