using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bpsim
{
	internal class HexColorConverter : JsonConverter<HexColor>
	{
		public override HexColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			return HexColor.Parse(reader.GetString());
		}

		public override void Write(Utf8JsonWriter writer, HexColor value, JsonSerializerOptions options)
		{
			writer.WriteStringValue(value.ToString());
		}
	}
}
