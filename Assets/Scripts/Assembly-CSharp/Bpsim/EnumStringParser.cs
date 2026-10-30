using System;

namespace Bpsim
{
	public class EnumStringParser<T> : IParser<T>
	{
		public static EnumStringParser<T> Default { get; } = new EnumStringParser<T>();

		T IParser<T>.Read(string s, IFormatProvider provider)
		{
			return (T)Enum.Parse(typeof(T), s, ignoreCase: false);
		}

		bool IParser<T>.TryRead(string s, IFormatProvider provider, out T result)
		{
			object result3;
			bool result2 = Enum.TryParse(typeof(T), s, ignoreCase: false, out result3);
			result = (T)result3;
			return result2;
		}

		string IParser<T>.Write(T value, IFormatProvider provider)
		{
			return value.ToString();
		}

		string IParser<T>.Write(T value, string format, IFormatProvider provider)
		{
			throw new NotImplementedException();
		}
	}
}
