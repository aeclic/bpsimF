using System;
using System.Collections.Generic;
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

		public static ISchematicsLoader CreateLoader(SchematicsFormat format)
		{
			return format switch
			{
				SchematicsFormat.Csv => SchematicsCsvLoader.Default, 
				SchematicsFormat.Json => SchematicsJsonLoader.Default, 
				SchematicsFormat.Xml => SchematicsXmlLoader.Default, 
				SchematicsFormat.EncryptedXml => SchematicsEncryptedXmlLoader.Default, 
				_ => throw new InvalidOperationException(), 
			};
		}
	}
}
