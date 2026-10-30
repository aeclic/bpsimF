using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

namespace Bpsim.Parts
{
	// Persist only Value. Collider ownership stays with the prefab/system, never with a part clone.
	public struct FrameShape : IComponentData
	{
		public int Value;
		public static int Clamp(int value) => math.clamp(value, 0, 3);
		public static bool Supports(PartTypeInfo info) => BasePart.IsContainer(info);
		public static string Name(int value) => value == 1 ? "直角三角框" : value == 2 ? "半圆框" : value == 3 ? "圆框" : "方框";
	}

	public struct FrameShapeDirty : IComponentData, IEnableableComponent { }
	public struct FrameShapeOriginalCollider : IComponentData
	{
		public BlobAssetReference<Collider> Value;
	}
}
