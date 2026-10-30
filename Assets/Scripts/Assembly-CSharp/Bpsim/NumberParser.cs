using System;
using System.Globalization;

namespace Bpsim
{
	public class NumberParser : IParser<sbyte>, IParser<byte>, IParser<short>, IParser<ushort>, IParser<int>, IParser<uint>, IParser<long>, IParser<ulong>, IParser<float>, IParser<double>
	{
		public static NumberParser Default { get; } = new NumberParser();

		sbyte IParser<sbyte>.Read(string s, IFormatProvider provider)
		{
			return sbyte.Parse(s, provider);
		}

		bool IParser<sbyte>.TryRead(string s, IFormatProvider provider, out sbyte result)
		{
			return sbyte.TryParse(s, NumberStyles.Integer, provider, out result);
		}

		string IParser<sbyte>.Write(sbyte value, IFormatProvider provider)
		{
			return value.ToString(provider);
		}

		string IParser<sbyte>.Write(sbyte value, string format, IFormatProvider provider)
		{
			return value.ToString(format, provider);
		}

		byte IParser<byte>.Read(string s, IFormatProvider provider)
		{
			return byte.Parse(s, provider);
		}

		bool IParser<byte>.TryRead(string s, IFormatProvider provider, out byte result)
		{
			return byte.TryParse(s, NumberStyles.Integer, provider, out result);
		}

		string IParser<byte>.Write(byte value, IFormatProvider provider)
		{
			return value.ToString(provider);
		}

		string IParser<byte>.Write(byte value, string format, IFormatProvider provider)
		{
			return value.ToString(format, provider);
		}

		short IParser<short>.Read(string s, IFormatProvider provider)
		{
			return short.Parse(s, provider);
		}

		bool IParser<short>.TryRead(string s, IFormatProvider provider, out short result)
		{
			return short.TryParse(s, NumberStyles.Integer, provider, out result);
		}

		string IParser<short>.Write(short value, IFormatProvider provider)
		{
			return value.ToString(provider);
		}

		string IParser<short>.Write(short value, string format, IFormatProvider provider)
		{
			return value.ToString(format, provider);
		}

		ushort IParser<ushort>.Read(string s, IFormatProvider provider)
		{
			return ushort.Parse(s, provider);
		}

		bool IParser<ushort>.TryRead(string s, IFormatProvider provider, out ushort result)
		{
			return ushort.TryParse(s, NumberStyles.Integer, provider, out result);
		}

		string IParser<ushort>.Write(ushort value, IFormatProvider provider)
		{
			return value.ToString(provider);
		}

		string IParser<ushort>.Write(ushort value, string format, IFormatProvider provider)
		{
			return value.ToString(format, provider);
		}

		int IParser<int>.Read(string s, IFormatProvider provider)
		{
			return int.Parse(s, provider);
		}

		bool IParser<int>.TryRead(string s, IFormatProvider provider, out int result)
		{
			return int.TryParse(s, NumberStyles.Integer, provider, out result);
		}

		string IParser<int>.Write(int value, IFormatProvider provider)
		{
			return value.ToString(provider);
		}

		string IParser<int>.Write(int value, string format, IFormatProvider provider)
		{
			return value.ToString(format, provider);
		}

		uint IParser<uint>.Read(string s, IFormatProvider provider)
		{
			return uint.Parse(s, provider);
		}

		bool IParser<uint>.TryRead(string s, IFormatProvider provider, out uint result)
		{
			return uint.TryParse(s, NumberStyles.Integer, provider, out result);
		}

		string IParser<uint>.Write(uint value, IFormatProvider provider)
		{
			return value.ToString(provider);
		}

		string IParser<uint>.Write(uint value, string format, IFormatProvider provider)
		{
			return value.ToString(format, provider);
		}

		long IParser<long>.Read(string s, IFormatProvider provider)
		{
			return long.Parse(s, provider);
		}

		bool IParser<long>.TryRead(string s, IFormatProvider provider, out long result)
		{
			return long.TryParse(s, NumberStyles.Integer, provider, out result);
		}

		string IParser<long>.Write(long value, IFormatProvider provider)
		{
			return value.ToString(provider);
		}

		string IParser<long>.Write(long value, string format, IFormatProvider provider)
		{
			return value.ToString(format, provider);
		}

		ulong IParser<ulong>.Read(string s, IFormatProvider provider)
		{
			return ulong.Parse(s, provider);
		}

		bool IParser<ulong>.TryRead(string s, IFormatProvider provider, out ulong result)
		{
			return ulong.TryParse(s, NumberStyles.Integer, provider, out result);
		}

		string IParser<ulong>.Write(ulong value, IFormatProvider provider)
		{
			return value.ToString(provider);
		}

		string IParser<ulong>.Write(ulong value, string format, IFormatProvider provider)
		{
			return value.ToString(format, provider);
		}

		float IParser<float>.Read(string s, IFormatProvider provider)
		{
			return float.Parse(s, provider);
		}

		bool IParser<float>.TryRead(string s, IFormatProvider provider, out float result)
		{
			return float.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, provider, out result);
		}

		string IParser<float>.Write(float value, IFormatProvider provider)
		{
			return value.ToString(provider);
		}

		string IParser<float>.Write(float value, string format, IFormatProvider provider)
		{
			return value.ToString(format, provider);
		}

		double IParser<double>.Read(string s, IFormatProvider provider)
		{
			return double.Parse(s, provider);
		}

		bool IParser<double>.TryRead(string s, IFormatProvider provider, out double result)
		{
			return double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, provider, out result);
		}

		string IParser<double>.Write(double value, IFormatProvider provider)
		{
			return value.ToString(provider);
		}

		string IParser<double>.Write(double value, string format, IFormatProvider provider)
		{
			return value.ToString(format, provider);
		}
	}
}
