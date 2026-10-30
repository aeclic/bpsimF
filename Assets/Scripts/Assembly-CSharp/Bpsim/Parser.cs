namespace Bpsim
{
	public static class Parser<T>
	{
		public static IParser<T> Default { get; }

		static Parser()
		{
			if (typeof(IParser<T>).IsAssignableFrom(typeof(BooleanParser)))
			{
				Default = (IParser<T>)BooleanParser.Default;
			}
			else if (typeof(IParser<T>).IsAssignableFrom(typeof(NumberParser)))
			{
				Default = (IParser<T>)NumberParser.Default;
			}
			else if (typeof(IParser<T>).IsAssignableFrom(typeof(StringParser)))
			{
				Default = (IParser<T>)StringParser.Default;
			}
			else if (typeof(T).IsEnum)
			{
				Default = EnumStringParser<T>.Default;
			}
		}
	}
}
