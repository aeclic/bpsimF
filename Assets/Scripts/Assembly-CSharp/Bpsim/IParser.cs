using System;

namespace Bpsim
{
	public interface IParser<T>
	{
		T Read(string s, IFormatProvider provider);

		bool TryRead(string s, IFormatProvider provider, out T result);

		string Write(T value, IFormatProvider provider);

		string Write(T value, string format, IFormatProvider provider);
	}
}
