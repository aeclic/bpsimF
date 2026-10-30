using Bpsim.Parts;
using Unity.Mathematics;
using UnityEngine;

namespace Bpsim.UI
{
	/// <summary>gizmo 的命中区域。机器工具复用 <see cref="FreeMove" /> / <see cref="Rotate" /> / <see cref="ScaleCorner" />。</summary>
	internal enum GizmoHandle
	{
		None,
		FreeMove,
		MoveX,
		MoveY,
		Rotate,
		ScaleCorner,
		ScaleEdgeX,
		ScaleEdgeY
	}

	internal struct GizmoHit
	{
		public GizmoHandle Handle;

		public int Index;

		public bool IsValid => Handle != GizmoHandle.None;
	}

	/// <summary>
	/// 部件/整车 gizmo 的纯数学部分：世界枢轴、旋转轴、手柄位置与屏幕空间命中。
	/// 和 <see cref="BuildToolController" /> 分开，方便验收探针直接测命中逻辑而不需要跑 Play。
	/// </summary>
	internal readonly struct GizmoFrame
	{
		public readonly float2 Pivot;

		/// <summary>视觉朝向（世界角，度）。部件 = 离散 90° 朝向 + 连续角。</summary>
		public readonly float RotationDegrees;

		public readonly float2 Scale;

		/// <summary>视觉半尺寸（已经乘上缩放）。</summary>
		public readonly float2 HalfExtents;

		public readonly bool Machine;

		public GizmoFrame(float2 pivot, float rotationDegrees, float2 scale, float2 halfExtents, bool machine)
		{
			Pivot = pivot;
			RotationDegrees = rotationDegrees;
			Scale = scale;
			HalfExtents = halfExtents;
			Machine = machine;
		}

		public static GizmoFrame ForPart(PartTransform transform, PartFreeTransform freeTransform)
		{
			PartFreeTransform free = PartFreeTransform.Sanitize(freeTransform);
			float rotation = 90f * transform.Rotation + free.RotationDegrees;
			return new GizmoFrame(new float2(transform.X, transform.Y) + free.Offset, rotation, free.Scale, 0.5f * free.Scale, machine: false);
		}

		public static GizmoFrame ForMachine(float2 min, float2 max)
		{
			float2 center = (min + max) * 0.5f;
			float2 half = math.max((max - min) * 0.5f, new float2(0.5f, 0.5f));
			return new GizmoFrame(center, 0f, new float2(1f, 1f), half, machine: true);
		}

		public float RingRadius => math.max(math.max(HalfExtents.x, HalfExtents.y) + 0.55f, 0.9f);

		public float ArrowLength => RingRadius + 0.55f;

		public void Axes(out float2 axisX, out float2 axisY)
		{
			float radians = math.radians(RotationDegrees);
			float cos = math.cos(radians);
			float sin = math.sin(radians);
			axisX = new float2(cos, sin);
			axisY = new float2(-sin, cos);
		}

		/// <summary>四角索引：0=(-,-)、1=(+,-)、2=(+,+)、3=(-,+)。</summary>
		public float2 Corner(int index)
		{
			Axes(out var axisX, out var axisY);
			float sx = ((index == 0 || index == 3) ? (-1f) : 1f);
			float sy = ((index == 0 || index == 1) ? (-1f) : 1f);
			return Pivot + axisX * (sx * HalfExtents.x) + axisY * (sy * HalfExtents.y);
		}

		/// <summary>四边中点索引：0=+X、1=-X、2=+Y、3=-Y。</summary>
		public float2 EdgePoint(int index)
		{
			Axes(out var axisX, out var axisY);
			return index switch
			{
				0 => Pivot + axisX * HalfExtents.x,
				1 => Pivot - axisX * HalfExtents.x,
				2 => Pivot + axisY * HalfExtents.y,
				_ => Pivot - axisY * HalfExtents.y,
			};
		}

		/// <summary>手柄对应的锚点（对角/对边），缩放时保持不动。</summary>
		public float2 ScaleAnchor(int index, bool edge)
		{
			Axes(out var axisX, out var axisY);
			if (edge)
			{
				return index switch
				{
					0 => Pivot - axisX * HalfExtents.x,
					1 => Pivot + axisX * HalfExtents.x,
					2 => Pivot - axisY * HalfExtents.y,
					_ => Pivot + axisY * HalfExtents.y,
				};
			}
			return Corner(index) - (Corner(index) - Pivot) * 2f;
		}
	}

	internal static class PartGizmoMath
	{
		public const float CenterRadius = 13f;

		public const float RingHitRadius = 10f;

		public const float ArrowHitRadius = 12f;

		public const float CornerHitRadius = 15f;

		public const float EdgeHitRadius = 13f;

		private static readonly float2[] CornerSigns =
		{
			new float2(-1f, -1f),
			new float2(1f, -1f),
			new float2(1f, 1f),
			new float2(-1f, 1f)
		};

		private static readonly float2[] EdgeSigns =
		{
			new float2(1f, 0f),
			new float2(-1f, 0f),
			new float2(0f, 1f),
			new float2(0f, -1f)
		};

		public static Vector2 Project(Camera camera, float2 world)
		{
			return camera.WorldToScreenPoint(new Vector3(world.x, world.y, -1f));
		}

		/// <summary>屏幕空间命中：中心 → 缩放手柄 → 箭头 → 圆环（先命中的优先）。</summary>
		public static bool HitTest(Vector2 screenPoint, Camera camera, in GizmoFrame frame, bool includeScale, float uiScale, out GizmoHit hit)
		{
			hit = default;
			if (camera == null)
			{
				return false;
			}
			uiScale = math.max(uiScale, 0.5f);
			Vector2 pivot = Project(camera, frame.Pivot);
			if ((screenPoint - pivot).magnitude <= CenterRadius * uiScale)
			{
				hit = new GizmoHit
				{
					Handle = GizmoHandle.FreeMove
				};
				return true;
			}
			if (includeScale)
			{
				for (int i = 0; i < 4; i++)
				{
					Vector2 handle = Project(camera, frame.Corner(i));
					if ((screenPoint - handle).magnitude <= CornerHitRadius * uiScale)
					{
						hit = new GizmoHit
						{
							Handle = frame.Machine ? GizmoHandle.ScaleCorner : GizmoHandle.ScaleCorner,
							Index = i
						};
						return true;
					}
				}
				if (!frame.Machine)
				{
					for (int j = 0; j < 4; j++)
					{
						Vector2 handle2 = Project(camera, frame.EdgePoint(j));
						if ((screenPoint - handle2).magnitude <= EdgeHitRadius * uiScale)
						{
							hit = new GizmoHit
							{
								Handle = ((j == 0 || j == 1) ? GizmoHandle.ScaleEdgeX : GizmoHandle.ScaleEdgeY),
								Index = j
							};
							return true;
						}
					}
				}
			}
			frame.Axes(out var axisX, out var axisY);
			float arrowLength = frame.ArrowLength;
			if (DistanceToSegment(screenPoint, pivot, Project(camera, frame.Pivot + axisX * arrowLength)) <= ArrowHitRadius * uiScale || DistanceToSegment(screenPoint, pivot, Project(camera, frame.Pivot - axisX * arrowLength)) <= ArrowHitRadius * uiScale)
			{
				hit = new GizmoHit
				{
					Handle = GizmoHandle.MoveX
				};
				return true;
			}
			if (DistanceToSegment(screenPoint, pivot, Project(camera, frame.Pivot + axisY * arrowLength)) <= ArrowHitRadius * uiScale || DistanceToSegment(screenPoint, pivot, Project(camera, frame.Pivot - axisY * arrowLength)) <= ArrowHitRadius * uiScale)
			{
				hit = new GizmoHit
				{
					Handle = GizmoHandle.MoveY
				};
				return true;
			}
			Vector2 previous = Project(camera, frame.Pivot + axisX * frame.RingRadius);
			for (int k = 1; k <= 48; k++)
			{
				float radians = math.radians(360f * (float)k / 48f);
				float2 point = frame.Pivot + new float2(math.cos(radians), math.sin(radians)) * frame.RingRadius;
				Vector2 current = Project(camera, point);
				if (DistanceToSegment(screenPoint, previous, current) <= RingHitRadius * uiScale)
				{
					hit = new GizmoHit
					{
						Handle = GizmoHandle.Rotate
					};
					return true;
				}
				previous = current;
			}
			return false;
		}

		public static float2 AxisSign(int cornerIndex)
		{
			return CornerSigns[math.clamp(cornerIndex, 0, CornerSigns.Length - 1)];
		}

		public static float2 EdgeSign(int edgeIndex)
		{
			return EdgeSigns[math.clamp(edgeIndex, 0, EdgeSigns.Length - 1)];
		}

		public static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
		{
			Vector2 segment = end - start;
			float lengthSquared = segment.sqrMagnitude;
			if (lengthSquared <= 0.0001f)
			{
				return (point - start).magnitude;
			}
			float t = math.clamp(Vector2.Dot(point - start, segment) / lengthSquared, 0f, 1f);
			return (point - (start + segment * t)).magnitude;
		}

		/// <summary>把角度差折到 [-180, 180]，避免拖动转圈时累计角跳变。</summary>
		public static float DeltaAngle(float fromRadians, float toRadians)
		{
			float delta = toRadians - fromRadians;
			while (delta > math.PI)
			{
				delta -= 2f * math.PI;
			}
			while (delta < -math.PI)
			{
				delta += 2f * math.PI;
			}
			return delta;
		}
	}
}
