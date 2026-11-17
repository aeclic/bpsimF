using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Collections;

namespace Bpsim.Parts
{
	public class Schematics
	{
		public struct Unit
		{
			public int X;

			public int Y;

			public int Type;

			public int Index;

			public int Rotation;

			public bool Flipped;

			/// <summary>无网格模式下的世界平移偏移（BPRC 的 @gridlessX/@gridlessY）。</summary>
			public float OffsetX;

			public float OffsetY;

			/// <summary>叠在离散朝向之上的连续旋转角（BPRC 的 @fineAngle）。</summary>
			public float RotationDegrees;

			/// <summary>部件根节点附加缩放（BPRC 的 PartScaleX/PartScaleY）；旧档缺省为 1。</summary>
			public float ScaleX;

			public float ScaleY;

			/// <summary>建造图层（1..16）；旧档缺省为 1。</summary>
			public int Layer;

			public int FrameShape;

			public Unit(int x, int y, int type, int index, int rotation, bool flipped)
				: this(x, y, type, index, rotation, flipped, 0f, 0f, 0f, 1f, 1f, 1)
			{
			}

			public Unit(int x, int y, int type, int index, int rotation, bool flipped, float offsetX, float offsetY, float rotationDegrees, float scaleX, float scaleY)
				: this(x, y, type, index, rotation, flipped, offsetX, offsetY, rotationDegrees, scaleX, scaleY, 1)
			{
			}

			public Unit(int x, int y, int type, int index, int rotation, bool flipped, float offsetX, float offsetY, float rotationDegrees, float scaleX, float scaleY, int layer, int frameShape = 0)
			{
				X = x;
				Y = y;
				Type = type;
				Index = index;
				Rotation = rotation;
				Flipped = flipped;
				FrameShape = global::Bpsim.Parts.FrameShape.Clamp(frameShape);
				OffsetX = offsetX;
				OffsetY = offsetY;
				RotationDegrees = rotationDegrees;
				ScaleX = scaleX;
				ScaleY = scaleY;
				Layer = ((layer < PartBuildLayer.MinLayer) ? PartBuildLayer.MinLayer : ((layer > PartBuildLayer.MaxLayer) ? PartBuildLayer.MaxLayer : layer));
			}

			public Unit WithPosition(int x, int y)
			{
				return new Unit(x, y, Type, Index, Rotation, Flipped, OffsetX, OffsetY, RotationDegrees, ScaleX, ScaleY, Layer, FrameShape);
			}

			public Unit WithPartType(int type, int index)
			{
				return new Unit(X, Y, type, index, Rotation, Flipped, OffsetX, OffsetY, RotationDegrees, ScaleX, ScaleY, Layer, FrameShape);
			}

			public Unit WithRotation(int rotation, bool flipped)
			{
				return new Unit(X, Y, Type, Index, rotation, flipped, OffsetX, OffsetY, RotationDegrees, ScaleX, ScaleY, Layer, FrameShape);
			}

			public Unit WithFreeTransform(float offsetX, float offsetY, float rotationDegrees, float scaleX, float scaleY)
			{
				return new Unit(X, Y, Type, Index, Rotation, Flipped, offsetX, offsetY, rotationDegrees, scaleX, scaleY, Layer, FrameShape);
			}

			public Unit WithLayer(int layer)
			{
				return new Unit(X, Y, Type, Index, Rotation, Flipped, OffsetX, OffsetY, RotationDegrees, ScaleX, ScaleY, layer, FrameShape);
			}

			/// <summary>旧存档/手工编辑的 JSON 没有新字段时缩放是 0，读取时统一归一到 1。</summary>
			public Unit Normalized()
			{
				float scaleX = (ScaleX > 0f) ? ScaleX : 1f;
				float scaleY = (ScaleY > 0f) ? ScaleY : 1f;
				float offsetX = float.IsNaN(OffsetX) || float.IsInfinity(OffsetX) ? 0f : OffsetX;
				float offsetY = float.IsNaN(OffsetY) || float.IsInfinity(OffsetY) ? 0f : OffsetY;
				float rotationDegrees = float.IsNaN(RotationDegrees) || float.IsInfinity(RotationDegrees) ? 0f : RotationDegrees;
				return new Unit(X, Y, Type, Index, Rotation, Flipped, offsetX, offsetY, rotationDegrees, scaleX, scaleY, Layer, FrameShape);
			}
		}

		public List<Unit> Units { get; set; }

		/// <summary>把所有 unit 的自由变换字段整理到合法范围（旧档缺字段 → 偏移 0、角度 0、缩放 1）。</summary>
		public void Normalize()
		{
			if (Units == null)
			{
				return;
			}
			for (int i = 0; i < Units.Count; i++)
			{
				Units[i] = Units[i].Normalized();
			}
		}

		public Schematics()
			: this(0)
		{
		}

		public Schematics(int count)
		{
			Units = new List<Unit>(count);
		}

		public NativeArray<Unit> ToNative(Allocator allocator)
		{
			int count = Units.Count;
			NativeArray<Unit> result = new NativeArray<Unit>(Units.Count, allocator, NativeArrayOptions.UninitializedMemory);
			for (int i = 0; i < count; i++)
			{
				result[i] = Units[i];
			}
			return result;
		}

		public static Schematics FromNative(NativeArray<Unit> source)
		{
			int length = source.Length;
			Schematics schematics = new Schematics(length);
			for (int i = 0; i < length; i++)
			{
				schematics.Units.Add(source[i]);
			}
			return schematics;
		}

		/// <summary>
		/// 嗅探 <paramref name="stream" /> 的内容，返回它真正是哪种格式。可 seek 的流会
		/// 还原到读取前；不可 seek 的流会被消费到末尾，调用方应先自行缓冲后再复用。
		/// </summary>
		public static SchematicsFormat DetectFormat(Stream stream)
		{
			if (stream == null)
			{
				return SchematicsFormat.None;
			}
			long origin = stream.CanSeek ? stream.Position : 0L;
			using MemoryStream buffer = new MemoryStream();
			stream.CopyTo(buffer);
			if (stream.CanSeek)
			{
				stream.Position = origin;
			}
			return DetectBytes(buffer.GetBuffer(), (int)buffer.Length);
		}

		/// <summary>嗅探文本内容。和 <see cref="DetectFormat" /> 同源，便于单测。</summary>
		public static SchematicsFormat DetectTextFormat(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return SchematicsFormat.None;
			}
			byte[] bytes = Encoding.UTF8.GetBytes(text);
			return DetectBytes(bytes, bytes.Length);
		}

		private static SchematicsFormat DetectBytes(byte[] bytes, int length)
		{
			if (length <= 0)
			{
				return SchematicsFormat.None;
			}
			// AES 密文是任意字节，任何分支都不该把它当文本：先看有没有 NUL。
			bool hasNul = false;
			int scan = Math.Min(length, 4096);
			for (int i = 0; i < scan; i++)
			{
				if (bytes[i] == 0)
				{
					hasNul = true;
					break;
				}
			}
			if (hasNul)
			{
				return SchematicsFormat.EncryptedXml;
			}
			string text = Encoding.UTF8.GetString(bytes, 0, length);
			char first = FirstMeaningful(text);
			if (first == '{')
			{
				return SchematicsFormat.Json;
			}
			if (first == '<')
			{
				return SchematicsFormat.Xml;
			}
			string dataLine = FirstDataLine(text);
			if (dataLine == null)
			{
				// 非空但只有空白/注释的文本没有可识别格式。
				return SchematicsFormat.None;
			}
			return DetectDelimitedFormat(text, FirstMeaningful(dataLine));
		}

		private static char FirstMeaningful(string text)
		{
			// 有 BOM 时 UTF8.GetString 会留下 U+FEFF，先跳过空白与它。
			for (int i = 0; i < text.Length; i++)
			{
				char c = text[i];
				if (!char.IsWhiteSpace(c) && c != '\uFEFF')
				{
					return c;
				}
			}
			return '\0';
		}

		private static SchematicsFormat DetectDelimitedFormat(string text, char first)
		{
			if (!char.IsDigit(first) && first != '-' && first != '+')
			{
				// 既不是 JSON/XML 的开头，也不是数字开头：只可能是 AES 密文，
				// 交给加密 loader 去判定（解不开会抛 FormatException，由 UI 显示）。
				return SchematicsFormat.EncryptedXml;
			}
			string line = FirstDataLine(text);
			if (line == null)
			{
				return SchematicsFormat.EncryptedXml;
			}
			string[] fields = line.Split(',');
			// 生产者只有两家：bpsim 的 SchematicsCsvLoader 恒写 13 列，
			// BPIR 的 INContraptionDataManager.SaveAsCsv 恒写 14 列、旧版本导出 6/7 列。
			// 所以列数是主判据；只有 13 列这种「两边都可能」的宽度才看第 13 列内容。
			switch (fields.Length)
			{
			case 14:
				return SchematicsFormat.BpirCsv;
			case 13:
				// 第 13 列是整数 → bpsim 的 frameShape；是空/成员串 → BPIR 的 members。
				return LooksLikeBpsimWideCsv(fields) ? SchematicsFormat.Csv : SchematicsFormat.BpirCsv;
			case 7:
				// 第 7 列是浮点 → bpsim 窄档的 offsetX；否则是 BPIR 的 members 串。
				return IsFloatLiteral(fields[6]) ? SchematicsFormat.Csv : SchematicsFormat.BpirCsv;
			case 6:
				// 6 列时两种读法给出同样的 type/index/x/y/rotation/flipped，其余取默认；
				// 但 BPIR 还允许第 7 列带 members，所以按 BPIR 读不会丢东西。
				return SchematicsFormat.BpirCsv;
			default:
				// 8..12 列只可能来自 BPIR 的旧版导出（bpsim 从没写过这么宽的档）。
				return SchematicsFormat.BpirCsv;
			}
		}

		private static string FirstDataLine(string text)
		{
			int start = 0;
			while (start < text.Length)
			{
				int end = text.IndexOf('\n', start);
				string line = (end < 0 ? text.Substring(start) : text.Substring(start, end - start)).Trim();
				if (line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
				{
					return line;
				}
				if (end < 0)
				{
					break;
				}
				start = end + 1;
			}
			return null;
		}

		private static bool LooksLikeBpsimWideCsv(string[] fields)
		{
			// 14 列只可能来自 BPIR 的宽导出（前 12 列是数字，第 13 列 members），
			// 或者历史上被 Excel 补过一列的 bpsim 13 列档。区分点在第 13 列：
			// bpsim 在那里放 frameShape（整数），BPIR 放 members（空、@key 或带 ;）。
			string cell = fields.Length > 12 ? fields[12].Trim() : string.Empty;
			if (cell.Length == 0 || cell.IndexOf('@') >= 0 || cell.IndexOf(';') >= 0)
			{
				return false;
			}
			return IsIntegerLiteral(cell);
		}

		private static bool IsIntegerLiteral(string raw)
		{
			return int.TryParse(raw.Trim(), NumberStyles.Integer, NumberFormatInfo.InvariantInfo, out _);
		}

		private static bool IsFloatLiteral(string raw)
		{
			return float.TryParse(raw.Trim(), NumberStyles.Float, NumberFormatInfo.InvariantInfo, out _);
		}

		public static ISchematicsLoader CreateLoader(SchematicsFormat format)
		{
			return format switch
			{
				SchematicsFormat.Csv => SchematicsCsvLoader.Default, 
				SchematicsFormat.Json => SchematicsJsonLoader.Default, 
				SchematicsFormat.Xml => SchematicsXmlLoader.Default, 
				SchematicsFormat.EncryptedXml => SchematicsEncryptedXmlLoader.Default, 
				SchematicsFormat.BpirCsv => SchematicsBpirCsvLoader.Default, 
				_ => throw new InvalidOperationException($"加载格式 {format} 需要先读文件内容：用 Schematics.DetectFormat 判定。"),
			};
		}
	}
}
