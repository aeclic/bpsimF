using Unity.Mathematics;
using UnityEngine;

namespace Bpsim.Parts
{
	/// <summary>
	/// 建造模式下按视觉位置拾取部件用的几何测试。
	/// 格坐标 <see cref="PartTransform.X/Y" /> 只是放置/连接查询的依据；无网格平移、自由旋转和缩放都记录在
	/// <see cref="PartFreeTransform" /> 里，由 <see cref="PartFreeTransformSystem" /> 合成为最终渲染位姿。
	/// 因此命中测试必须把世界点按同一套变换逆回去，再与 <see cref="BasePart.GetGridRect" /> 给出的局部格矩形比较，
	/// 否则部件看起来挪走了、点它却还得点原来的格子。
	/// </summary>
	public static class PartPicking
	{
		/// <summary>部件视觉中心（格原点 + 自由偏移）。</summary>
		public static float2 GetVisualCenter(in PartTransform transform, in PartFreeTransform freeTransform)
		{
			PartFreeTransform free = PartFreeTransform.Sanitize(freeTransform);
			return new float2(transform.X, transform.Y) + free.Offset;
		}

		/// <summary>
		/// 网格旋转对应的角度：普通件每格 90°，八向件按 <see cref="BasePart.RotateTo" /> 的 45° 偏移。
		/// 镜像只改变贴图朝向，不改变 1×1 的占格矩形，故不参与命中。
		/// </summary>
		public static float GetBaseRotationDegrees(in PartTransform transform, PartTypeInfo info)
		{
			int rotation = transform.Rotation;
			if (info.PartType == PartType.ElectricalPart && (info.PartIndex == 1 || info.PartIndex == 3 || info.PartIndex == 38 || info.PartIndex == 39)) return 0f;
			if (BasePart.IsEightWay(info))
			{
				rotation = (rotation % 8 + 8) % 8;
				return 90f * rotation + ((rotation >= 4) ? -315f : 0f);
			}
			rotation = (rotation % 4 + 4) % 4;
			return 90f * rotation;
		}

		/// <summary>视觉角度 = 网格角度 + 自由连续角。</summary>
		public static float GetVisualRotationDegrees(in PartTransform transform, PartTypeInfo info, in PartFreeTransform freeTransform)
		{
			PartFreeTransform free = PartFreeTransform.Sanitize(freeTransform);
			return GetBaseRotationDegrees(transform, info) + free.RotationDegrees;
		}

		/// <summary>
		/// 局部格矩形（单位：格）。单格件中心 (0,0)、半径 0.5；KingPig/GoldenPig 为 3×2，
		/// 中心 (0,0.5)、半径 (1.5,1)，与 <see cref="BasePart.GetGridRect" /> 的 <c>RectInt(-1,0,3,2)</c> 对齐。
		/// </summary>
		public static void GetLocalBounds(PartTypeInfo info, out float2 center, out float2 halfExtents)
		{
			RectInt cells = BasePart.GetGridRect(info);
			center = new float2((cells.xMin + cells.xMax - 1) * 0.5f, (cells.yMin + cells.yMax - 1) * 0.5f);
			halfExtents = new float2(cells.width * 0.5f, cells.height * 0.5f);
		}

		/// <summary>把世界点逆变换到部件的局部格坐标（已反旋转、反缩放）。</summary>
		public static bool TryGetLocalPoint(in PartTransform transform, in PartFreeTransform freeTransform, PartTypeInfo info, float2 world, out float2 local)
		{
			PartFreeTransform free = PartFreeTransform.Sanitize(freeTransform);
			float2 pivot = new float2(transform.X, transform.Y) + free.Offset;
			float degrees = GetBaseRotationDegrees(transform, info) + free.RotationDegrees;
			local = Rotate(world - pivot, -degrees) / free.Scale;
			return math.all(math.isfinite(local));
		}

		/// <summary>世界点是否落在部件当前的视觉形状内。</summary>
		public static bool Contains(in PartTransform transform, in PartFreeTransform freeTransform, PartTypeInfo info, float2 world)
		{
			if (!TryGetLocalPoint(transform, freeTransform, info, world, out float2 local))
			{
				return false;
			}
			return ContainsLocal(info, local);
		}

		/// <summary>局部点是否落在按格矩形内（边界算命中）。</summary>
		public static bool ContainsLocal(PartTypeInfo info, float2 local)
		{
			GetLocalBounds(info, out float2 center, out float2 halfExtents);
			float2 delta = math.abs(local - center);
			return delta.x <= halfExtents.x && delta.y <= halfExtents.y;
		}

		private static float2 Rotate(float2 value, float degrees)
		{
			float radians = math.radians(degrees);
			float cos = math.cos(radians);
			float sin = math.sin(radians);
			return new float2(value.x * cos - value.y * sin, value.x * sin + value.y * cos);
		}
	}
}
