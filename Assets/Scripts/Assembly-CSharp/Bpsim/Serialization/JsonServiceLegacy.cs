using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using UnityEngine;

namespace Bpsim.Serialization
{
	internal class JsonServiceLegacy : IJsonService
	{
		private class Vector2Converter : JsonConverter<Vector2>
		{
			public override Vector2 ReadJson(JsonReader reader, Type objectType, Vector2 existingValue, bool hasExistingValue, JsonSerializer serializer)
			{
				if (reader.TokenType == JsonToken.Null)
				{
					throw new JsonSerializationException("Cannot convert null value to Vector2.");
				}
				if (reader.TokenType == JsonToken.String)
				{
					float[] array = ReadFloatArray((string)reader.Value);
					if (array.Length != 2)
					{
						throw new JsonSerializationException("Error parsing Vector2 string.");
					}
					return new Vector2(array[0], array[1]);
				}
				throw new JsonSerializationException("Unexpected token or value when parsing Vector2.");
			}

			public override void WriteJson(JsonWriter writer, Vector2 value, JsonSerializer serializer)
			{
				string value2 = WriteFloatArray(value.x, value.y);
				writer.WriteValue(value2);
			}
		}

		private class Vector3Converter : JsonConverter<Vector3>
		{
			public override Vector3 ReadJson(JsonReader reader, Type objectType, Vector3 existingValue, bool hasExistingValue, JsonSerializer serializer)
			{
				if (reader.TokenType == JsonToken.Null)
				{
					throw new JsonSerializationException("Cannot convert null value to Vector3.");
				}
				if (reader.TokenType == JsonToken.String)
				{
					float[] array = ReadFloatArray((string)reader.Value);
					if (array.Length != 3)
					{
						throw new JsonSerializationException("Error parsing Vector3 string.");
					}
					return new Vector3(array[0], array[1], array[2]);
				}
				throw new JsonSerializationException("Unexpected token or value when parsing Vector3.");
			}

			public override void WriteJson(JsonWriter writer, Vector3 value, JsonSerializer serializer)
			{
				string value2 = WriteFloatArray(value.x, value.y, value.z);
				writer.WriteValue(value2);
			}
		}

		private class Vector4Converter : JsonConverter<Vector4>
		{
			public override Vector4 ReadJson(JsonReader reader, Type objectType, Vector4 existingValue, bool hasExistingValue, JsonSerializer serializer)
			{
				if (reader.TokenType == JsonToken.Null)
				{
					throw new JsonSerializationException("Cannot convert null value to Quaternion.");
				}
				if (reader.TokenType == JsonToken.String)
				{
					float[] array = ReadFloatArray((string)reader.Value);
					if (array.Length != 4)
					{
						throw new JsonSerializationException("Error parsing Vector4 string.");
					}
					return new Vector4(array[0], array[1], array[2], array[3]);
				}
				throw new JsonSerializationException("Unexpected token or value when parsing Vector4.");
			}

			public override void WriteJson(JsonWriter writer, Vector4 value, JsonSerializer serializer)
			{
				string value2 = WriteFloatArray(value.x, value.y, value.z, value.w);
				writer.WriteValue(value2);
			}
		}

		private class QuaternionConverter : JsonConverter<Quaternion>
		{
			public override Quaternion ReadJson(JsonReader reader, Type objectType, Quaternion existingValue, bool hasExistingValue, JsonSerializer serializer)
			{
				if (reader.TokenType == JsonToken.Null)
				{
					throw new JsonSerializationException("Cannot convert null value to Quaternion.");
				}
				if (reader.TokenType == JsonToken.String)
				{
					float[] array = ReadFloatArray((string)reader.Value);
					if (array.Length != 4)
					{
						throw new JsonSerializationException("Error parsing Vector4 string.");
					}
					return new Quaternion(array[0], array[1], array[2], array[3]);
				}
				throw new JsonSerializationException("Unexpected token or value when parsing Vector4.");
			}

			public override void WriteJson(JsonWriter writer, Quaternion value, JsonSerializer serializer)
			{
				string value2 = WriteFloatArray(value.x, value.y, value.z, value.w);
				writer.WriteValue(value2);
			}
		}

		private class ContractResolver : DefaultContractResolver
		{
			private List<JsonConverter> m_converters;

			public ContractResolver(IEnumerable<JsonConverter> converters)
			{
				m_converters = new List<JsonConverter>(converters);
			}

			protected override JsonContract CreateContract(Type objectType)
			{
				JsonContract jsonContract = base.CreateContract(objectType);
				foreach (JsonConverter converter in m_converters)
				{
					if (converter.CanConvert(objectType))
					{
						jsonContract.Converter = converter;
						break;
					}
				}
				return jsonContract;
			}
		}

		private JsonSerializer m_serializer;

		private JsonSerializer m_indentedSerializer;

		public JsonServiceLegacy()
		{
			ContractResolver contractResolver = new ContractResolver(GetConverters())
			{
				NamingStrategy = new CamelCaseNamingStrategy()
			};
			m_serializer = JsonSerializer.CreateDefault();
			m_serializer.ContractResolver = contractResolver;
			m_serializer.Formatting = Formatting.None;
			m_indentedSerializer = JsonSerializer.CreateDefault();
			m_indentedSerializer.ContractResolver = contractResolver;
			m_indentedSerializer.Formatting = Formatting.Indented;
			static IEnumerable<JsonConverter> GetConverters()
			{
				yield return new StringEnumConverter();
				yield return new VersionConverter();
				yield return new Vector2Converter();
				yield return new Vector3Converter();
				yield return new Vector4Converter();
				yield return new QuaternionConverter();
			}
		}

		public string Serialize<T>(T value)
		{
			return Serialize(value, indented: true);
		}

		public string Serialize<T>(T value, bool indented)
		{
			using StringWriter stringWriter = new StringWriter(CultureInfo.InvariantCulture);
			Serialize(stringWriter, value, indented);
			return stringWriter.ToString();
		}

		public void Serialize<T>(Stream stream, T value)
		{
			Serialize(stream, value, indented: true);
		}

		public void Serialize<T>(Stream stream, T value, bool indented)
		{
			using StreamWriter writer = new StreamWriter(stream);
			Serialize(writer, value, indented);
		}

		private void Serialize<T>(TextWriter writer, T value, bool indented)
		{
			JsonSerializer jsonSerializer = (indented ? m_indentedSerializer : m_serializer);
			using JsonTextWriter jsonTextWriter = new JsonTextWriter(writer);
			jsonTextWriter.Indentation = 4;
			jsonTextWriter.IndentChar = ' ';
			jsonTextWriter.Formatting = jsonSerializer.Formatting;
			jsonSerializer.Serialize(jsonTextWriter, value, null);
		}

		public T Deserialize<T>(string text)
		{
			using StringReader reader = new StringReader(text);
			return Deserialize<T>(reader);
		}

		public T Deserialize<T>(Stream stream)
		{
			using StreamReader reader = new StreamReader(stream);
			return Deserialize<T>(reader);
		}

		private T Deserialize<T>(TextReader reader)
		{
			JsonSerializer serializer = m_serializer;
			using JsonTextReader reader2 = new JsonTextReader(reader);
			return (T)serializer.Deserialize(reader2, typeof(T));
		}

		private static float[] ReadFloatArray(string text)
		{
			string[] array = text.Split(',');
			float[] array2 = new float[array.Length];
			for (int i = 0; i < array.Length; i++)
			{
				array2[i] = float.Parse(array[i]);
			}
			return array2;
		}

		private static string WriteFloatArray(params float[] values)
		{
			return string.Join(',', values);
		}
	}
}
