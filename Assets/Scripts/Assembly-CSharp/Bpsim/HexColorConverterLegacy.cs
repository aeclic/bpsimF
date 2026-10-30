using System;
using Newtonsoft.Json;

namespace Bpsim
{
	internal class HexColorConverterLegacy : JsonConverter<HexColor>
	{
		public override HexColor ReadJson(JsonReader reader, Type objectType, HexColor existingValue, bool hasExistingValue, JsonSerializer serializer)
		{
			return HexColor.Parse((string)reader.Value);
		}

		public override void WriteJson(JsonWriter writer, HexColor value, JsonSerializer serializer)
		{
			writer.WriteValue(value.ToString());
		}
	}
}
