using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bpsim.Templates
{
	public class ComponentConverter : JsonConverter<ComponentTemplate>
	{
		public override ComponentTemplate Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			using JsonDocument jsonDocument = JsonDocument.ParseValue(ref reader);
			if (!jsonDocument.RootElement.TryGetProperty("type", out var value))
			{
				throw new JsonException();
			}
			Type returnType = ComponentTemplate.Resolve(Enum.Parse<ComponentType>(value.GetString()));
			return (ComponentTemplate)jsonDocument.Deserialize(returnType, options);
		}

		public override void Write(Utf8JsonWriter writer, ComponentTemplate value, JsonSerializerOptions options)
		{
			JsonSerializer.Serialize(writer, (object)value, options);
		}
	}
}
