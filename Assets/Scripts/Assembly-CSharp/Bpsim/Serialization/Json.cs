using System.IO;

namespace Bpsim.Serialization
{
	public static class Json
	{
		public static IJsonService Service { get; set; }

		static Json()
		{
			Service = new JsonService();
		}

		public static string Serialize<T>(T value)
		{
			return Service.Serialize(value);
		}

		public static string Serialize<T>(T value, bool indented)
		{
			return Service.Serialize(value, indented);
		}

		public static void Serialize<T>(Stream stream, T value)
		{
			Service.Serialize(stream, value);
		}

		public static void Serialize<T>(Stream stream, T value, bool indented)
		{
			Service.Serialize(stream, value, indented);
		}

		public static T Deserialize<T>(string text)
		{
			return Service.Deserialize<T>(text);
		}

		public static T Deserialize<T>(Stream stream)
		{
			return Service.Deserialize<T>(stream);
		}
	}
}
