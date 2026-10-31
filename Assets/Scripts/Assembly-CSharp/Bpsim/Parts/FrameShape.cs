using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

namespace Bpsim.Parts
{
	// Persist only Value. Collider ownership stays with the prefab/system, never with a part clone.
	public struct FrameShape : IComponentData
	{
		public const int Rectangle = 0;
		public const int Triangle = 1;
		public const int Semicircle = 2;
		public const int Circle = 3;
		public const int Count = Circle + 1;
		public int Value;
		public static int Clamp(int value) => math.clamp(value, Rectangle, Circle);
		public static bool Supports(PartTypeInfo info) => BasePart.IsContainer(info);
		public static string Name(int value) => value == Triangle ? "直角三角框" : value == Semicircle ? "半圆框" : value == Circle ? "圆框" : "长方形框";
	}

	public struct FrameShapeDirty : IComponentData, IEnableableComponent { }
	public struct FrameShapeOriginalCollider : IComponentData
	{
		public BlobAssetReference<Collider> Value;
	}
}
