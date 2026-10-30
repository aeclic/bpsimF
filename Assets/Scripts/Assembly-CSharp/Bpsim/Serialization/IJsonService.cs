using System.IO;

namespace Bpsim.Serialization
{
	public interface IJsonService
	{
		string Serialize<T>(T value);

		string Serialize<T>(T value, bool indented);

		void Serialize<T>(Stream stream, T value);

		void Serialize<T>(Stream stream, T value, bool indented);

		T Deserialize<T>(string text);

		T Deserialize<T>(Stream stream);
	}
}
