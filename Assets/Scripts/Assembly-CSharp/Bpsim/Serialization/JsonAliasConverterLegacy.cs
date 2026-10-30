using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bpsim.Serialization
{
	public class JsonAliasConverterLegacy<T> : JsonConverter<T> where T : new()
	{
		private static Dictionary<string, string> s_aliasMap;

		public override bool CanWrite => false;

		static JsonAliasConverterLegacy()
		{
			s_aliasMap = new Dictionary<string, string>();
			PropertyInfo[] properties = typeof(T).GetProperties();
			foreach (PropertyInfo propertyInfo in properties)
			{
				object[] customAttributes = propertyInfo.GetCustomAttributes(inherit: true);
				for (int j = 0; j < customAttributes.Length; j++)
				{
					if (customAttributes[j] is JsonAliasAttribute jsonAliasAttribute)
					{
						s_aliasMap.Add(jsonAliasAttribute.Alias, propertyInfo.Name);
					}
				}
			}
		}

		private JObject Convert(JObject source)
		{
			JObject jObject = new JObject();
			foreach (JProperty item in source.Properties())
			{
				if (s_aliasMap.TryGetValue(item.Name, out var value))
				{
					jObject.Add(value, item.Value);
				}
				else
				{
					jObject.Add(item.Name, item.Value);
				}
			}
			return jObject;
		}

		public override T ReadJson(JsonReader reader, Type objectType, T existingValue, bool hasExistingValue, JsonSerializer serializer)
		{
			JObject source = JObject.Load(reader);
			JObject jObject = Convert(source);
			T val = new T();
			serializer.Populate(jObject.CreateReader(), val);
			return val;
		}

		public override void WriteJson(JsonWriter writer, T value, JsonSerializer serializer)
		{
			throw new NotImplementedException();
		}
	}
}
