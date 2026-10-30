using System;
using System.IO;
using Bpsim.Serialization;

namespace Bpsim.Parts
{
	internal class SchematicsJsonLoader : ISchematicsLoader
	{
		public static SchematicsJsonLoader Default { get; } = new SchematicsJsonLoader();

		public Schematics Read(Stream stream)
		{
			Schematics schematics = Json.Deserialize<Schematics>(stream);
			if (!Validate(schematics))
			{
				throw new NullReferenceException();
			}
			schematics.Normalize();
			return schematics;
		}

		public void Write(Stream stream, Schematics schematics)
		{
			Json.Serialize(stream, schematics, indented: false);
		}

		private static bool Validate(Schematics schematics)
		{
			if (schematics != null)
			{
				return schematics.Units != null;
			}
			return false;
		}
	}
}
