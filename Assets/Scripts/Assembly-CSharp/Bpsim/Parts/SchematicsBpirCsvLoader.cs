using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Bpsim.Parts
{
	/// <summary>
	/// BPIR（Bad Piggies Innovation Rebuild）14 列 CSV 读取器。
	///
	/// 列序（<c>PartPhysicsOverride</c> 的 <c>ContraptionDataset.AddPart</c>）：
	/// <code>
	/// 0 type | 1 index | 2 x | 3 y | 4 rotation | 5 flipped | 6 mass | 7 gravity |
	/// 8 drag | 9 size | 10 power | 11 suction | 12 members | 13 layer
	/// </code>
	/// 注意尾部与 bpsim 自己的 13 列 <see cref="SchematicsFormat.Csv" /> 语义完全冲突
	/// （那边第 6..12 列是自由变换），所以必须分开成一种格式，不能靠嗅探混读。
	///
	/// 映射规则：
	/// * 第 0 列就是 BPIR 的 <c>SortedPartType</c>，数值与 bpsim <see cref="PartType" /> 的
	///   1..48 完全一致，直接搬；
	/// * 第 4 列是 <c>m_gridRotation</c>：0..3 = 0/90/180/270 度，4..7 = 45/135/225/315 度
	///   （只有八向部件会用 4..7）。八向部件原样交给 <c>BasePart.RotateTo</c>；
	///   非八向部件会把多出来的 45 度折进 <c>@rotZ</c>（连续角），否则会被 <c>rotation % 4</c> 吃掉；
	/// * 第 13 列是 BPIR 的建造层 1..6，直接进 <see cref="Schematics.Unit.Layer" />——
	///   这是「图层之间的连接关系」的唯一来源（层 2..N 只与同层邻居和正下方层 1 件焊接）；
	/// * 第 12 列 members 是 <c>key=value;key=value</c> 串，其中
	///   <c>@offX/@offY/@rotZ/@stretchX/@stretchY/@shape</c> 有 bpsim 载体，
	///   其余（<c>@layer</c>、<c>@wheelScale</c>、<c>@dockMerge</c>、<c>组件:成员</c>）没有，
	///   全部计入 <see cref="LastWarnings" />，不静默丢。
	/// </summary>
	internal class SchematicsBpirCsvLoader : ISchematicsLoader
	{
		/// <summary>BPIR 存档至少要有 6 列（BPRC 的 <c>BpirSaveConverter.ReadCsv</c> 同款下限）。</summary>
		public const int MinColumnCount = 6;

		/// <summary>完整 14 列（含 members 与 layer）时的列数。</summary>
		public const int ColumnCount = 14;

		/// <summary>BPIR 的坐标上限；越界视为损坏数据（BPRC 会整份拒绝，这里只跳这一件）。</summary>
		public const int MaxCoordinate = 1000000;

		/// <summary>BPIR 的 <c>@layer</c> 取值范围（深度偏移，clamp 后 × 0.01 世界单位）。</summary>
		public const int MinDepthLayer = -50;

		public const int MaxDepthLayer = 50;

		public static SchematicsBpirCsvLoader Default { get; } = new SchematicsBpirCsvLoader();

		private readonly List<string> m_warnings = new List<string>();

		private readonly Dictionary<string, int> m_unknownMembers = new Dictionary<string, int>();

		/// <summary>上一次 <see cref="Read(TextReader)" /> 收集到的告警（面向玩家/QA 的可读文本）。</summary>
		public IReadOnlyList<string> LastWarnings => m_warnings;

		/// <summary>上一次读取时成功解析的行数。</summary>
		public int LastParsedRows { get; private set; }

		/// <summary>上一次读取时因非法数据被跳过的行数。</summary>
		public int LastSkippedRows { get; private set; }

		/// <summary>上一次读取时带非空 members 的行数。</summary>
		public int LastMemberRows { get; private set; }

		public Schematics Read(Stream stream)
		{
			using StreamReader reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, 1024, leaveOpen: true);
			return Read(reader);
		}

		public Schematics Read(TextReader reader)
		{
			IFormatProvider invariantInfo = NumberFormatInfo.InvariantInfo;
			m_warnings.Clear();
			m_unknownMembers.Clear();
			LastParsedRows = 0;
			LastSkippedRows = 0;
			LastMemberRows = 0;
			Schematics schematics = new Schematics();
			int lineNumber = 0;
			while (reader.Peek() >= 0)
			{
				string text = reader.ReadLine();
				lineNumber++;
				if (string.IsNullOrWhiteSpace(text))
				{
					continue;
				}
				string line = text.Trim();
				if (line.Length == 0 || line[0] == '#')
				{
					continue;
				}
				if (TryReadRow(line, lineNumber, invariantInfo, out Schematics.Unit unit))
				{
					schematics.Units.Add(unit);
					LastParsedRows++;
				}
				else
				{
					LastSkippedRows++;
				}
			}
			FlushUnknownMembers();
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
			foreach (Schematics.Unit unit in schematics.Units)
			{
				writer.Write(unit.Type);
				writer.Write(',');
				writer.Write(unit.Index);
				writer.Write(',');
				writer.Write(unit.X);
				writer.Write(',');
				writer.Write(unit.Y);
				writer.Write(',');
				writer.Write(unit.Rotation);
				writer.Write(',');
				writer.Write(unit.Flipped ? 1 : 0);
				writer.Write(',');
				// mass / gravity / drag / size / power / suction：bpsim 没有逐件物理覆盖载体，
				// 一律写 -1（BPIR 的「未设置」），把差异体现在成员串的 stretch 上。
				writer.Write(Format(-1, invariantInfo));
				writer.Write(',');
				writer.Write(Format(-1, invariantInfo));
				writer.Write(',');
				writer.Write(Format(-1, invariantInfo));
				writer.Write(',');
				writer.Write(Format(-1, invariantInfo));
				writer.Write(',');
				writer.Write(Format(-1, invariantInfo));
				writer.Write(',');
				writer.Write(Format(-1, invariantInfo));
				writer.Write(',');
				writer.Write(EncodeMembers(unit, invariantInfo));
				writer.Write(',');
				writer.Write(unit.Layer);
				writer.WriteLine();
			}
		}

		private static string Format(float value, IFormatProvider provider)
		{
			return value.ToString("0.#####", provider);
		}

		private static string EncodeMembers(Schematics.Unit unit, IFormatProvider provider)
		{
			StringBuilder builder = new StringBuilder(48);
			if (unit.OffsetX != 0f)
			{
				builder.Append("@offX=").Append(Format(unit.OffsetX, provider));
			}
			if (unit.OffsetY != 0f)
			{
				Append(builder).Append("@offY=").Append(Format(unit.OffsetY, provider));
			}
			if (unit.RotationDegrees != 0f)
			{
				Append(builder).Append("@rotZ=").Append(Format(unit.RotationDegrees, provider));
			}
			if (unit.ScaleX != 1f)
			{
				Append(builder).Append("@stretchX=").Append(Format(unit.ScaleX, provider));
			}
			if (unit.ScaleY != 1f)
			{
				Append(builder).Append("@stretchY=").Append(Format(unit.ScaleY, provider));
			}
			if (unit.FrameShape != 0)
			{
				Append(builder).Append("@shape=").Append(unit.FrameShape);
			}
			return builder.ToString();
		}

		private static StringBuilder Append(StringBuilder builder)
		{
			if (builder.Length > 0)
			{
				builder.Append(';');
			}
			return builder;
		}

		private bool TryReadRow(string line, int lineNumber, IFormatProvider provider, out Schematics.Unit unit)
		{
			unit = default(Schematics.Unit);
			string[] fields = line.Split(',');
			if (fields.Length < MinColumnCount)
			{
				Warn($"第 {lineNumber} 行：只有 {fields.Length} 列，BPIR 存档至少要 {MinColumnCount} 列，已跳过");
				return false;
			}
			if (!TryInt(fields[0], provider, out int type) || !TryInt(fields[1], provider, out int index) ||
				!TryInt(fields[2], provider, out int x) || !TryInt(fields[3], provider, out int y) ||
				!TryInt(fields[4], provider, out int rotation))
			{
				Warn($"第 {lineNumber} 行：type/index/x/y/rotation 不是整数，已跳过：{line}");
				return false;
			}
			if (!TryInt(fields[5], provider, out int flippedValue))
			{
				Warn($"第 {lineNumber} 行：翻转值不是整数，已跳过：{line}");
				return false;
			}
			if (flippedValue != 0 && flippedValue != 1)
			{
				Warn($"第 {lineNumber} 行：翻转值应为 0 或 1（实际 {flippedValue}），按非 0 处理");
			}
			if (type < (int)PartType.Pig || type > (int)PartType.MechanicalPart)
			{
				Warn($"第 {lineNumber} 行：部件类型 {type} 超出 BPIR 可映射范围 1..{(int)PartType.MechanicalPart}（BPRC 的扩展类型在 bpsim 里没有对应件），已跳过");
				return false;
			}
			if (index < 0)
			{
				Warn($"第 {lineNumber} 行：部件索引 {index} 非法，已跳过");
				return false;
			}
			if (Math.Abs((long)x) > MaxCoordinate || Math.Abs((long)y) > MaxCoordinate)
			{
				Warn($"第 {lineNumber} 行：坐标 ({x}, {y}) 超出 ±{MaxCoordinate}，视为损坏数据，已跳过");
				return false;
			}
			rotation = NormalizeRotation(rotation, lineNumber);
			bool flipped = flippedValue != 0;
			float sizeMultiplier = TryFloat(fields, 9, provider, out float size) && size > 0f ? size : 1f;
			float offsetX = 0f;
			float offsetY = 0f;
			float rotationDegrees = 0f;
			float stretchX = 1f;
			float stretchY = 1f;
			int frameShape = 0;
			int layer = PartBuildLayer.MinLayer;
			string members = ReadMembersField(fields, lineNumber, provider, out bool hasLayer, out int parsedLayer);
			if (hasLayer)
			{
				layer = parsedLayer;
			}
			PartTypeInfo info = new PartTypeInfo((PartType)type, index);
			if (!string.IsNullOrEmpty(members))
			{
				LastMemberRows++;
				ReadMembers(members, lineNumber, provider, info, ref offsetX, ref offsetY, ref rotationDegrees, ref stretchX, ref stretchY, ref frameShape);
			}
			// 非八向部件放不下 45 度增量（BasePart.RotateTo 会 rotation %= 4），
			// 把余下的 45 度折进连续角，视觉朝向才不会丢。
			if (rotation >= 4 && !BasePart.IsEightWay(info))
			{
				rotationDegrees += 45f;
				rotation -= 4;
			}
			unit = new Schematics.Unit(x, y, type, index, rotation, flipped, offsetX, offsetY, rotationDegrees, stretchX * sizeMultiplier, stretchY * sizeMultiplier, layer, frameShape).Normalized();
			return true;
		}

		private int NormalizeRotation(int rotation, int lineNumber)
		{
			if (rotation >= 0 && rotation <= 7)
			{
				return rotation;
			}
			int wrapped = ((rotation % 8) + 8) % 8;
			Warn($"第 {lineNumber} 行：旋转值 {rotation} 超出 0..7，按 {wrapped} 处理");
			return wrapped;
		}

		private string ReadMembersField(string[] fields, int lineNumber, IFormatProvider provider, out bool hasLayer, out int layer)
		{
			hasLayer = false;
			layer = PartBuildLayer.MinLayer;
			if (fields.Length == ColumnCount - 1)
			{
				// 13 列：只有 members，没有 layer。
				return fields[12];
			}
			if (fields.Length < ColumnCount)
			{
				return string.Empty;
			}
			if (fields.Length > ColumnCount)
			{
				// members 自带的逗号（例如某些 @shape=x,y）会把尾巴顶出 14 列：
				// 最后一列仍然是 layer，中间全部并回 members。
				int count = fields.Length - 13;
				string joined = string.Join(",", fields, 12, count);
				if (TryInt(fields[fields.Length - 1], provider, out int parsed))
				{
					hasLayer = true;
					layer = parsed;
				}
				else
				{
					Warn($"第 {lineNumber} 行：列数 {fields.Length} 且最后一列不是图层号，按 14 列读");
					return fields[12];
				}
				return joined;
			}
			if (TryInt(fields[13], provider, out int value))
			{
				hasLayer = true;
				layer = value;
			}
			else
			{
				Warn($"第 {lineNumber} 行：图层列 \"{fields[13]}\" 不是整数，按第 1 层处理");
			}
			return fields[12];
		}

		private void ReadMembers(string members, int lineNumber, IFormatProvider provider, PartTypeInfo info, ref float offsetX, ref float offsetY, ref float rotationDegrees, ref float stretchX, ref float stretchY, ref int frameShape)
		{
			string[] entries = members.Split(';');
			for (int i = 0; i < entries.Length; i++)
			{
				string entry = entries[i].Trim();
				if (entry.Length == 0)
				{
					continue;
				}
				int separator = entry.IndexOf('=');
				if (separator <= 0 || separator >= entry.Length - 1)
				{
					Warn($"第 {lineNumber} 行：无效的成员参数 \"{entry}\"，已忽略");
					continue;
				}
				string key = entry.Substring(0, separator).Trim();
				string raw = entry.Substring(separator + 1).Trim();
				switch (key)
				{
				case "@offX":
					offsetX = Clamp(TryFloat(raw, provider, out float ox, key, lineNumber) ? ox : 0f, -1f, 1f);
					break;
				case "@offY":
					offsetY = Clamp(TryFloat(raw, provider, out float oy, key, lineNumber) ? oy : 0f, -1f, 1f);
					break;
				case "@rotZ":
					// BPIR 用 Mathf.DeltaAngle(0, v) 归一，@rotZ 是「替换」原生姿态的绝对角而不是叠加，
					// 但 bpsim 的 PartFreeTransformSystem 是 math.mul(local.Rotation, RotateZ(deg))，
					// 二维下同为绕 z 轴旋转、左右乘等价，直接搬即可。
					rotationDegrees = TryFloat(raw, provider, out float rz, key, lineNumber) ? Mathf.DeltaAngle(0f, rz) : 0f;
					break;
				case "@stretchX":
					stretchX = TryFloat(raw, provider, out float sx, key, lineNumber) ? sx : 1f;
					break;
				case "@stretchY":
					stretchY = TryFloat(raw, provider, out float sy, key, lineNumber) ? sy : 1f;
					break;
				case "@shape":
					frameShape = ParseFrameShape(raw, lineNumber);
					break;
				case "@layer":
					// BPIR 的 @layer 是与建造层无关的深度偏移（-50..50，×0.01 世界单位），
					// bpsim 的 Schematics.Unit / PartFreeTransform 都没有对应通道。
					if (TryFloat(raw, provider, out float depth, key, lineNumber))
					{
						AddUnknownMember("@layer(深度偏移 " + Mathf.Clamp(Mathf.RoundToInt(depth), MinDepthLayer, MaxDepthLayer) + ")");
					}
					break;
				default:
					AddUnknownMember(key);
					break;
				}
			}
			if (!FrameShape.Supports(info) && frameShape != 0)
			{
				Warn($"第 {lineNumber} 行：@shape={frameShape} 但 {info.PartType}[{info.PartIndex}] 不是容器框，形状已忽略");
				frameShape = 0;
			}
		}

		private int ParseFrameShape(string raw, int lineNumber)
		{
			// @shape 的值可能是 "2" 或 "2,1"（后者在切分时最后一列会被当作 layer，
			// 这里只取逗号前的第一段，与 BPRC 的 BpirSaveConverter 行为保持一致）。
			int comma = raw.IndexOf(',');
			string value = comma >= 0 ? raw.Substring(0, comma) : raw;
			if (!int.TryParse(value, NumberStyles.Integer, NumberFormatInfo.InvariantInfo, out int shape))
			{
				Warn($"第 {lineNumber} 行：@shape 值 \"{raw}\" 不是整数，已忽略");
				return 0;
			}
			return FrameShape.Clamp(shape);
		}

		private bool TryFloat(string raw, IFormatProvider provider, out float value, string key, int lineNumber)
		{
			if (float.TryParse(raw, NumberStyles.Float, provider, out value) && !float.IsNaN(value) && !float.IsInfinity(value))
			{
				return true;
			}
			value = 0f;
			Warn($"第 {lineNumber} 行：成员 {key} 的值 \"{raw}\" 不是有限数，按 0 处理");
			return false;
		}

		private bool TryFloat(string[] fields, int index, IFormatProvider provider, out float value)
		{
			value = 0f;
			if (index >= fields.Length)
			{
				return false;
			}
			string raw = fields[index].Trim();
			if (raw.Length == 0)
			{
				return false;
			}
			return float.TryParse(raw, NumberStyles.Float, provider, out value) && !float.IsNaN(value) && !float.IsInfinity(value);
		}

		private static bool TryInt(string raw, IFormatProvider provider, out int value)
		{
			return int.TryParse(raw.Trim(), NumberStyles.Integer, provider, out value);
		}

		private static float Clamp(float value, float min, float max)
		{
			if (value < min)
			{
				return min;
			}
			return value > max ? max : value;
		}

		private void Warn(string message)
		{
			m_warnings.Add(message);
		}

		private void AddUnknownMember(string key)
		{
			m_unknownMembers.TryGetValue(key, out int count);
			m_unknownMembers[key] = count + 1;
		}

		private void FlushUnknownMembers()
		{
			if (m_unknownMembers.Count == 0)
			{
				return;
			}
			StringBuilder builder = new StringBuilder();
			builder.Append("bpsim 没有载体的部件成员（已忽略）：");
			bool first = true;
			foreach (KeyValuePair<string, int> pair in m_unknownMembers)
			{
				if (!first)
				{
					builder.Append(", ");
				}
				builder.Append(pair.Key).Append('×').Append(pair.Value);
				first = false;
			}
			m_warnings.Add(builder.ToString());
		}
	}
}
