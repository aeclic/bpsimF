using System;

namespace Bpsim
{
	public class BooleanParser : IParser<bool>
	{
		public static BooleanParser Default { get; } = new BooleanParser();

		bool IParser<bool>.Read(string s, IFormatProvider provider)
		{
			return bool.Parse(s);
		}

		bool IParser<bool>.TryRead(string s, IFormatProvider provider, out bool result)
		{
			return bool.TryParse(s, out result);
		}

		string IParser<bool>.Write(bool value, IFormatProvider provider)
		{
			return value.ToString();
		}

		string IParser<bool>.Write(bool value, string format, IFormatProvider provider)
		{
			throw new NotImplementedException();
		}
	}
}
