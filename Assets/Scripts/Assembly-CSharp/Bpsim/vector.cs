using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace Bpsim
{
	public static class vector
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float cross(float2 x, float2 y)
		{
			return x.x * y.y - x.y * y.x;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float2 rotate(float2 x, float2 y)
		{
			return math.mul(new float2x2(x.x, 0f - x.y, x.y, x.x), y);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float2 invrotate(float2 x, float2 y)
		{
			return math.mul(new float2x2(x.x, x.y, 0f - x.y, x.x), y);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Deconstruct(this float2 value, out float x, out float y)
		{
			x = value.x;
			y = value.y;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Deconstruct(this float3 value, out float x, out float y, out float z)
		{
			x = value.x;
			y = value.y;
			z = value.z;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Deconstruct(this float4 value, out float x, out float y, out float z, out float w)
		{
			x = value.x;
			y = value.y;
			z = value.z;
			w = value.w;
		}
	}
}
