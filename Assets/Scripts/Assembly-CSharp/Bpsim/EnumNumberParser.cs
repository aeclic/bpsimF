using System;
using System.Globalization;

namespace Bpsim
{
	public class EnumNumberParser<T> : IParser<T>
	{
		public static EnumNumberParser<T> Default { get; } = new EnumNumberParser<T>();

		T IParser<T>.Read(string s, IFormatProvider provider)
		{
			return (T)(object)int.Parse(s, provider);
		}

		bool IParser<T>.TryRead(string s, IFormatProvider provider, out T result)
		{
			int result3;
			bool result2 = int.TryParse(s, NumberStyles.Integer, provider, out result3);
			result = (T)(object)result3;
			return result2;
		}

		string IParser<T>.Write(T value, IFormatProvider provider)
		{
			return ((int)(object)value).ToString(provider);
		}

		string IParser<T>.Write(T value, string format, IFormatProvider provider)
		{
			return ((int)(object)value).ToString(format, provider);
		}
	}
}
