using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Bpsim.Parts
{
	internal class SchematicsCsvLoader : ISchematicsLoader
	{
		public static SchematicsCsvLoader Default { get; } = new SchematicsCsvLoader();

		public Schematics Read(Stream stream)
		{
			using StreamReader reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, 1024, leaveOpen: true);
			return Read(reader);
		}

		public Schematics Read(TextReader reader)
		{
			IFormatProvider invariantInfo = NumberFormatInfo.InvariantInfo;
			Schematics schematics = new Schematics();
			while (reader.Peek() >= 0)
			{
				string text = reader.ReadLine();
				if (!string.IsNullOrEmpty(text))
				{
					string[] array = text.Split(',');
					int type = int.Parse(array[0], invariantInfo);
					int index = int.Parse(array[1], invariantInfo);
					int x = int.Parse(array[2], invariantInfo);
					int y = int.Parse(array[3], invariantInfo);
					int rotation = int.Parse(array[4], invariantInfo);
					bool flipped = Convert.ToBoolean(int.Parse(array[5], invariantInfo));
					float offsetX = 0f;
					float offsetY = 0f;
					float rotationDegrees = 0f;
					float scaleX = 1f;
					float scaleY = 1f;
					int layer = PartBuildLayer.MinLayer;
					if (array.Length >= 11)
					{
						offsetX = float.Parse(array[6], invariantInfo);
						offsetY = float.Parse(array[7], invariantInfo);
						rotationDegrees = float.Parse(array[8], invariantInfo);
						scaleX = float.Parse(array[9], invariantInfo);
						scaleY = float.Parse(array[10], invariantInfo);
					}
					if (array.Length >= 12)
					{
						layer = int.Parse(array[11], invariantInfo);
					}
					int frameShape = array.Length >= 13 ? int.Parse(array[12], invariantInfo) : 0;
					schematics.Units.Add(new Schematics.Unit(x, y, type, index, rotation, flipped, offsetX, offsetY, rotationDegrees, scaleX, scaleY, layer, frameShape).Normalized());
				}
			}
			return schematics;
		}

		public void Write(Stream stream, Schematics schematics)
		{
			using StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 1024, leaveOpen: true);
			Write(writer, schematics);
		}

		public void Write(TextWriter writer, Schematics schematics)
		{
			IFormatProvider invariantInfo = NumberFormatInfo.InvariantInfo;
			char value = ',';
			foreach (Schematics.Unit unit in schematics.Units)
			{
				writer.Write(unit.Type);
				writer.Write(value);
				writer.Write(unit.Index);
				writer.Write(value);
				writer.Write(unit.X);
				writer.Write(value);
				writer.Write(unit.Y);
				writer.Write(value);
				writer.Write(unit.Rotation);
				writer.Write(value);
				writer.Write(Convert.ToInt32(unit.Flipped));
				writer.Write(value);
				writer.Write(unit.OffsetX.ToString("R", invariantInfo));
				writer.Write(value);
				writer.Write(unit.OffsetY.ToString("R", invariantInfo));
				writer.Write(value);
				writer.Write(unit.RotationDegrees.ToString("R", invariantInfo));
				writer.Write(value);
				writer.Write(unit.ScaleX.ToString("R", invariantInfo));
				writer.Write(value);
				writer.Write(unit.ScaleY.ToString("R", invariantInfo));
				writer.Write(value);
				writer.Write(unit.Layer);
				writer.Write(value);
				writer.Write(unit.FrameShape);
				writer.WriteLine();
			}
		}
	}
}
