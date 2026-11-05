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
			int lineNumber = 0;
			while (reader.Peek() >= 0)
			{
				string text = reader.ReadLine();
				lineNumber++;
				// 纯空白行、行首空白的行都要跳过：AI 生成的夹具里常见表头行与缩进。
				if (string.IsNullOrWhiteSpace(text))
				{
					continue;
				}
				string line = text.Trim();
				// '#' 开头是注释（含可选的 `#bpsim,1,cols=N` 版本头），不参与解析。
				if (line.Length == 0 || line[0] == '#')
				{
					continue;
				}
				string[] array = line.Split(',');
				// BPIR 导出是 14 列，第 13 列（index 12）是 `@offX=0.5;@layer=-3` 这类注解串，
				// 不是 bpsim 的 FrameShape。以前这里直接 int.Parse("") 崩在中间，看不出原因；
				// 现在显式拒绝并指路到 BPIR 导入模式。
				if (array.Length == 14 && !IsIntegerLiteral(array[12]))
				{
					throw new FormatException($"第 {lineNumber} 行第 13 列「{array[12].Trim()}」不是整数：这是 BPIR（原版 Innovation Rebuild）的 14 列导出，请改用「BPIR 存档」格式加载。");
				}
				int type = ParseInt(array, 0, lineNumber, "Type", invariantInfo);
				int index = ParseInt(array, 1, lineNumber, "Index", invariantInfo);
				int x = ParseInt(array, 2, lineNumber, "X", invariantInfo);
				int y = ParseInt(array, 3, lineNumber, "Y", invariantInfo);
				int rotation = ParseInt(array, 4, lineNumber, "Rotation", invariantInfo);
				bool flipped = Convert.ToBoolean(ParseInt(array, 5, lineNumber, "Flipped", invariantInfo));
				float offsetX = 0f;
				float offsetY = 0f;
				float rotationDegrees = 0f;
				float scaleX = 1f;
				float scaleY = 1f;
				int layer = PartBuildLayer.MinLayer;
				if (array.Length >= 11)
				{
					offsetX = ParseFloat(array, 6, lineNumber, "OffsetX", invariantInfo);
					offsetY = ParseFloat(array, 7, lineNumber, "OffsetY", invariantInfo);
					rotationDegrees = ParseFloat(array, 8, lineNumber, "RotationDegrees", invariantInfo);
					scaleX = ParseFloat(array, 9, lineNumber, "ScaleX", invariantInfo);
					scaleY = ParseFloat(array, 10, lineNumber, "ScaleY", invariantInfo);
				}
				if (array.Length >= 12)
				{
					layer = ParseInt(array, 11, lineNumber, "Layer", invariantInfo);
				}
				int frameShape = array.Length >= 13 ? ParseInt(array, 12, lineNumber, "FrameShape", invariantInfo) : 0;
				schematics.Units.Add(new Schematics.Unit(x, y, type, index, rotation, flipped, offsetX, offsetY, rotationDegrees, scaleX, scaleY, layer, frameShape).Normalized());
			}
			return schematics;
		}

		/// <summary>整数字面量判定，只用来区分 BPIR 注解串与真正的列值。</summary>
		private static bool IsIntegerLiteral(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return false;
			}
			for (int i = 0; i < value.Length; i++)
			{
				char c = value[i];
				if (!char.IsDigit(c) && !(i == 0 && (c == '-' || c == '+')))
				{
					return false;
				}
			}
			return true;
		}

		private static int ParseInt(string[] array, int column, int lineNumber, string name, IFormatProvider provider)
		{
			if (column >= array.Length)
			{
				throw new FormatException($"第 {lineNumber} 行缺少第 {column + 1} 列「{name}」：该行只有 {array.Length} 列。");
			}
			string text = array[column].Trim();
			if (int.TryParse(text, NumberStyles.Integer, provider, out int value))
			{
				return value;
			}
			// 容忍 `43.0` 这种被 Excel / 手写工具写成小数的整数值。
			if (float.TryParse(text, NumberStyles.Float, provider, out float parsed) && Math.Abs(parsed - Math.Round(parsed)) < 0.0001f)
			{
				return (int)Math.Round(parsed);
			}
			throw new FormatException($"第 {lineNumber} 行第 {column + 1} 列「{name}」=「{text}」不是整数。");
		}

		private static float ParseFloat(string[] array, int column, int lineNumber, string name, IFormatProvider provider)
		{
			if (column >= array.Length)
			{
				throw new FormatException($"第 {lineNumber} 行缺少第 {column + 1} 列「{name}」：该行只有 {array.Length} 列。");
			}
			string text = array[column].Trim();
			if (float.TryParse(text, NumberStyles.Float, provider, out float value))
			{
				return value;
			}
			throw new FormatException($"第 {lineNumber} 行第 {column + 1} 列「{name}」=「{text}」不是数字。");
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
