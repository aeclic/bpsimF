using Unity.Mathematics;

namespace Bpsim.Physics
{
	public static class MathExtensions
	{
		public static float2 ToRightDirection(this quaternion q)
		{
			float4 value = q.value;
			return new float2(1f - 2f * (value.y * value.y + value.z * value.z), 2f * (value.x * value.y + value.w * value.z));
		}

		public static float2 ToUpDirection(this quaternion q)
		{
			float4 value = q.value;
			return new float2(2f * (value.x * value.y - value.z * value.w), 1f - 2f * (value.x * value.x + value.z * value.z));
		}

		public static float3 ToEulerAngles(this quaternion q)
		{
			float4 value = q.value;
			float4 @float = value * value.wwww * new float4(2f);
			float4 float2 = value * value.yzxw * new float4(2f);
			float4 float3 = value * value;
			float num = float2.y - @float.x;
			float3 result;
			if (num * num < 0.99999595f)
			{
				float y = float2.x + @float.z;
				float x = float3.y + float3.w - float3.x - float3.z;
				float y2 = float2.z + @float.y;
				float x2 = float3.z + float3.w - float3.x - float3.y;
				result = new float3(math.atan2(y, x), 0f - math.asin(num), math.atan2(y2, x2));
			}
			else
			{
				num = math.clamp(num, -1f, 1f);
				float4 float4 = new float4(float2.z, @float.y, float2.y, @float.x);
				float y3 = 2f * (float4.x * float4.w + float4.y * float4.z);
				float x3 = math.csum(float4 * float4 * new float4(-1f, 1f, -1f, 1f));
				result = new float3(math.atan2(y3, x3), 0f - math.asin(num), 0f);
			}
			return result;
		}
	}
}
