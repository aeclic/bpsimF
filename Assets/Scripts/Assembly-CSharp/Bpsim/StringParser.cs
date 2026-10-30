using System;

namespace Bpsim
{
	public class StringParser : IParser<char>, IParser<string>
	{
		public static StringParser Default { get; } = new StringParser();

		char IParser<char>.Read(string s, IFormatProvider provider)
		{
			return char.Parse(s);
		}

		bool IParser<char>.TryRead(string s, IFormatProvider provider, out char result)
		{
			return char.TryParse(s, out result);
		}

		string IParser<char>.Write(char value, IFormatProvider provider)
		{
			return value.ToString();
		}

		string IParser<char>.Write(char value, string format, IFormatProvider provider)
		{
			throw new NotImplementedException();
		}

		string IParser<string>.Read(string s, IFormatProvider provider)
		{
			return s;
		}

		bool IParser<string>.TryRead(string s, IFormatProvider provider, out string result)
		{
			result = s;
			return true;
		}

		string IParser<string>.Write(string value, IFormatProvider provider)
		{
			return value;
		}

		string IParser<string>.Write(string value, string format, IFormatProvider provider)
		{
			throw new NotImplementedException();
		}
	}
}
