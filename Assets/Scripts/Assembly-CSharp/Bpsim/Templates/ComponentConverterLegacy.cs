using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bpsim.Templates
{
	public class ComponentConverterLegacy : JsonConverter<ComponentTemplate>
	{
		public override bool CanWrite => false;

		public override ComponentTemplate ReadJson(JsonReader reader, Type objectType, ComponentTemplate existingValue, bool hasExistingValue, JsonSerializer serializer)
		{
			JObject jObject = JObject.Load(reader);
			JProperty jProperty = jObject.Property("Type", StringComparison.OrdinalIgnoreCase);
			if (jProperty == null)
			{
				jProperty = jObject.Property("类型", StringComparison.Ordinal);
			}
			if (jProperty == null)
			{
				return null;
			}
			ComponentTemplate componentTemplate = ComponentTemplate.Create(jProperty.Value.ToObject<ComponentType>());
			serializer.Populate(jObject.CreateReader(), componentTemplate);
			return componentTemplate;
		}

		public override void WriteJson(JsonWriter writer, ComponentTemplate value, JsonSerializer serializer)
		{
			throw new NotImplementedException();
		}
	}
}
