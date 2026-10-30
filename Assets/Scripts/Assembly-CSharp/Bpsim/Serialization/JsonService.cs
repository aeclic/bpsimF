using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnityEngine;

namespace Bpsim.Serialization
{
	internal class JsonService : IJsonService
	{
		private class Vector2Converter : JsonConverter<Vector2>
		{
			public override Vector2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
			{
				string text = reader.GetString();
				float[] array = new float[2];
				if (!ReadFloatArray(text, 2, array))
				{
					throw new JsonException("Error parsing Vector2 string.");
				}
				return new Vector2(array[0], array[1]);
			}

			public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options)
			{
				string value2 = WriteFloatArray(value.x, value.y);
				writer.WriteStringValue(value2);
			}
		}

		private class Vector3Converter : JsonConverter<Vector3>
		{
			public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
			{
				string text = reader.GetString();
				float[] array = new float[3];
				if (!ReadFloatArray(text, 3, array))
				{
					throw new JsonException("Error parsing Vector3 string.");
				}
				return new Vector3(array[0], array[1], array[2]);
			}

			public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
			{
				string value2 = WriteFloatArray(value.x, value.y, value.z);
				writer.WriteStringValue(value2);
			}
		}

		private class Vector4Converter : JsonConverter<Vector4>
		{
			public override Vector4 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
			{
				string text = reader.GetString();
				float[] array = new float[4];
				if (!ReadFloatArray(text, 4, array))
				{
					throw new JsonException("Error parsing Vector4 string.");
				}
				return new Vector4(array[0], array[1], array[2], array[3]);
			}

			public override void Write(Utf8JsonWriter writer, Vector4 value, JsonSerializerOptions options)
			{
				string value2 = WriteFloatArray(value.x, value.y, value.z, value.w);
				writer.WriteStringValue(value2);
			}
		}

		private class QuaternionConverter : JsonConverter<Quaternion>
		{
			public override Quaternion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
			{
				string text = reader.GetString();
				float[] array = new float[4];
				if (!ReadFloatArray(text, 4, array))
				{
					throw new JsonException("Error parsing Quaternion string.");
				}
				return new Quaternion(array[0], array[1], array[2], array[3]);
			}

			public override void Write(Utf8JsonWriter writer, Quaternion value, JsonSerializerOptions options)
			{
				string value2 = WriteFloatArray(value.x, value.y, value.z, value.w);
				writer.WriteStringValue(value2);
			}
		}

		private JsonSerializerOptions m_option;

		private JsonSerializerOptions m_indentedOption;

		public JsonService()
		{
			m_option = new JsonSerializerOptions(JsonSerializerOptions.Default);
			m_option.IncludeFields = true;
			m_option.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
			m_option.Converters.Add(new JsonStringEnumConverter());
			m_option.Converters.Add(new Vector2Converter());
			m_option.Converters.Add(new Vector3Converter());
			m_option.Converters.Add(new Vector4Converter());
			m_option.Converters.Add(new QuaternionConverter());
			m_indentedOption = new JsonSerializerOptions(m_option);
			m_indentedOption.WriteIndented = true;
		}

		public string Serialize<T>(T value)
		{
			return JsonSerializer.Serialize(value, m_option);
		}

		public string Serialize<T>(T value, bool indented)
		{
			return JsonSerializer.Serialize(value, indented ? m_indentedOption : m_option);
		}

		public void Serialize<T>(Stream stream, T value)
		{
			JsonSerializer.Serialize(stream, value, m_option);
		}

		public void Serialize<T>(Stream stream, T value, bool indented)
		{
			JsonSerializer.Serialize(stream, value, indented ? m_indentedOption : m_option);
		}

		public T Deserialize<T>(string text)
		{
			return JsonSerializer.Deserialize<T>(text, m_option);
		}

		public T Deserialize<T>(Stream stream)
		{
			return JsonSerializer.Deserialize<T>(stream, m_option);
		}

		private static bool ReadFloatArray(string text, int count, float[] result)
		{
			if (text == null)
			{
				return false;
			}
			string[] array = text.Split(',');
			if (array.Length != count)
			{
				return false;
			}
			for (int i = 0; i < count; i++)
			{
				result[i] = float.Parse(array[i]);
			}
			return true;
		}

		private static string WriteFloatArray(params float[] values)
		{
			return string.Join(',', values);
		}
	}
}
