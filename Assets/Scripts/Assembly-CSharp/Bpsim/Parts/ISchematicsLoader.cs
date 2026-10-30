using System.IO;

namespace Bpsim.Parts
{
	public interface ISchematicsLoader
	{
		Schematics Read(Stream stream);

		void Write(Stream stream, Schematics schematics);
	}
}
