using Unity.Mathematics;
using UnityEngine;

namespace Bpsim
{
	internal static class UnityExtensions
	{
		public static Vector2 WithX(this Vector2 vector, float x)
		{
			return new Vector2(x, vector.y);
		}

		public static Vector2 WithY(this Vector2 vector, float y)
		{
			return new Vector2(vector.x, y);
		}

		public static Vector3 WithZ(this Vector2 vector, float z)
		{
			return new Vector3(vector.x, vector.y, z);
		}

		public static Vector3 WithX(this Vector3 vector, float x)
		{
			return new Vector3(x, vector.y, vector.z);
		}

		public static Vector3 WithY(this Vector3 vector, float y)
		{
			return new Vector3(vector.x, y, vector.z);
		}

		public static Vector3 WithZ(this Vector3 vector, float z)
		{
			return new Vector3(vector.x, vector.y, z);
		}

		public static int2 ToInt2(this Vector2Int vector)
		{
			return new int2(vector.x, vector.y);
		}

		public static int3 ToInt3(this Vector3Int vector)
		{
			return new int3(vector.x, vector.y, vector.z);
		}

		public static int4 ToInt4(this RectInt rect)
		{
			return new int4(rect.xMin, rect.yMin, rect.width, rect.height);
		}

		public static float4 ToFloat4(this Rect rect)
		{
			return new float4(rect.xMin, rect.yMin, rect.width, rect.height);
		}

		public static T AddOrGetComponent<T>(this GameObject gameObject) where T : Component
		{
			T val = gameObject.GetComponent<T>();
			if (val == null)
			{
				val = gameObject.AddComponent<T>();
			}
			return val;
		}
	}
}
