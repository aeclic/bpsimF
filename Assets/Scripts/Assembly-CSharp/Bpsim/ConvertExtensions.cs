using System;
using System.Collections.Generic;
using Bpsim.Parts;

namespace Bpsim
{
	internal static class ConvertExtensions
	{
		public class EnumConverter<TInput, TOutput> where TInput : struct, Enum where TOutput : struct, Enum
		{
			private Dictionary<TInput, TOutput> m_map;

			public EnumConverter()
				: this(ignoreCase: false)
			{
			}

			public EnumConverter(bool ignoreCase)
			{
				TInput[] array = (TInput[])Enum.GetValues(typeof(TInput));
				string[] names = Enum.GetNames(typeof(TInput));
				m_map = new Dictionary<TInput, TOutput>(array.Length);
				for (int i = 0; i < array.Length; i++)
				{
					if (Enum.TryParse<TOutput>(names[i], ignoreCase, out var result))
					{
						m_map.Add(array[i], result);
					}
				}
			}

			public TOutput Convert(TInput input)
			{
				m_map.TryGetValue(input, out var value);
				return value;
			}
		}

		private static EnumConverter<PartType, LegacyPartType> s_partTypeConverter;

		private static EnumConverter<LegacyPartType, PartType> s_legacyPartTypeConverter;

		static ConvertExtensions()
		{
			s_partTypeConverter = new EnumConverter<PartType, LegacyPartType>(ignoreCase: true);
			s_legacyPartTypeConverter = new EnumConverter<LegacyPartType, PartType>(ignoreCase: true);
		}

		public static LegacyPartType ToLegacyPartType(this PartType partType)
		{
			return s_partTypeConverter.Convert(partType);
		}

		public static PartType ToPartType(this LegacyPartType partType)
		{
			return s_legacyPartTypeConverter.Convert(partType);
		}
	}
}
