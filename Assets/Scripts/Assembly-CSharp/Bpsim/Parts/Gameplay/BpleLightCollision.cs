using System;
using UnityEngine;

namespace Bpsim.Parts.Gameplay
{
	internal sealed class BpleLightEmitter
	{
		public int Type, Sides;
		public float Length, HalfWidth, Angle;
		public float Cos => (float)Math.Cos(Angle * (MathF.PI / 360f));
		public BpleLightCollision.LightData Data;
	}

	// The original BPLE continuous collision kernel. Verified against native IL on 4,000 fixed cases.
	internal sealed class BpleLightCollision
	{
		private readonly float m_maxDetectionDistance = 40f;
		public struct LightData { public Vector2 Position0, Position1, Direction0, Direction1; }
		public struct CCDData
		{
			public Vector2 Position0, Position1, Direction0, Direction1;
			public BpleLightBounds Bounds;
			public float Time, DeltaTime;
		}
		public readonly struct TOIResult
		{
			public readonly float TimeOfImpact, ContactSeparation;
			public readonly int ContactCount;
			public readonly Vector2 ContactPoint, ContactNormal, RelativeVelocity;
			public static TOIResult Default => default;
			public TOIResult(float time) : this(time, 0, default, default, 0, default) {}
			public TOIResult(float time, int count, Vector2 point, Vector2 normal, float separation, Vector2 velocity)
			{ TimeOfImpact=time; ContactCount=count; ContactPoint=point; ContactNormal=normal; ContactSeparation=separation; RelativeVelocity=velocity; }
		}
		public bool Cast(BpleLightEmitter light, ref CCDData motion, out TOIResult result)
		{
			result = default;
			return BroadPhaseDetect(light, ref motion) && ComputeTimeOfImpact(light, ref motion, out result);
		}
private bool BroadPhaseDetect(BpleLightEmitter light, ref CCDData data)
	{
		int type = light.Type;
		LightData data2 = light.Data;
		if (type == 0 || type == 1)
		{
			float valueX = data.Position0.x - data2.Position0.x;
			float valueY = data.Position0.y - data2.Position0.y;
			float valueX2 = data.Position1.x - data2.Position1.x;
			float valueY2 = data.Position1.y - data2.Position1.y;
			float x = data2.Direction0.x;
			float y = data2.Direction0.y;
			float x2 = data2.Direction1.x;
			float y2 = data2.Direction1.y;
			BpleLightMath.InvTransform2(valueX, valueY, x, y, out var resultX, out var resultY);
			BpleLightMath.InvTransform2(valueX2, valueY2, x2, y2, out var resultX2, out var resultY2);
			float maxDetectionDistance = m_maxDetectionDistance;
			float length = light.Length;
			float halfWidth = light.HalfWidth;
			if ((resultX < 0f - maxDetectionDistance || resultX > length + maxDetectionDistance) && (resultX2 < 0f - maxDetectionDistance || resultX2 > length + maxDetectionDistance))
			{
				return false;
			}
			if (Math.Abs(resultY) > halfWidth + maxDetectionDistance && Math.Abs(resultY2) > halfWidth + maxDetectionDistance)
			{
				return false;
			}
			return true;
		}
		float num = data.Position0.x - data2.Position0.x;
		float num2 = data.Position1.x - data2.Position1.x;
		float num3 = data.Position1.y - data2.Position1.y;
		float num4 = num * num + num * num;
		float num5 = num2 * num2 + num3 * num3;
		float num6 = light.Length + m_maxDetectionDistance;
		float num7 = num6 * num6;
		if (num4 > num7 && num5 > num7)
		{
			return false;
		}
		return true;
	}

	private bool ComputeTimeOfImpact(BpleLightEmitter light, ref CCDData data, out TOIResult result)
	{
		switch (light.Type)
		{
		case 0:
		case 1:
			return ComputeTimeOfImpactRect(light, ref data, out result);
		case 2:
		case 4:
			return ComputeTimeOfImpactOuterCircle(light, ref data, out result);
		default:
			return ComputeTimeOfImpactInnerCircle(light, ref data, out result);
		}
	}

	private bool ComputeTimeOfImpactRect(BpleLightEmitter light, ref CCDData data, out TOIResult result)
	{
		result = TOIResult.Default;
		LightData data2 = light.Data;
		float length = light.Length;
		float valueX = data.Position0.x - data2.Position0.x;
		float valueY = data.Position0.y - data2.Position0.y;
		float valueX2 = data.Position1.x - data2.Position1.x;
		float valueY2 = data.Position1.y - data2.Position1.y;
		float x = data2.Direction0.x;
		float y = data2.Direction0.y;
		float x2 = data2.Direction1.x;
		float y2 = data2.Direction1.y;
		BpleLightMath.InvTransform2(valueX, valueY, x, y, out var resultX, out var resultY);
		BpleLightMath.InvTransform2(valueX2, valueY2, x2, y2, out var resultX2, out var resultY2);
		if ((!(0f < resultX) || !(resultX < length)) && (!(0f < resultX2) || !(resultX2 < length)))
		{
			return false;
		}
		int sides = light.Sides;
		float halfWidth = light.HalfWidth;
		BpleLightBounds bounds = data.Bounds;
		BpleLightMath.InvTransform2(data.Direction1, data2.Direction1, out var resultX3, out var resultY3);
		float halfProjection = bounds.GetHalfProjection(0f - resultY3, resultX3);
		float num = resultY - halfProjection - halfWidth;
		float num2 = resultY2 - halfProjection - halfWidth;
		float num3 = resultY + halfProjection + halfWidth;
		float num4 = resultY2 + halfProjection + halfWidth;
		bool flag = num > -0.25f && num2 < 0f && num2 < num;
		bool flag2 = num3 < 0.25f && num4 > 0f && num4 > num3;
		if (sides != 0)
		{
			flag = flag && (sides & 1) == 0;
			flag2 = flag2 && (sides & 2) == 0;
		}
		if (!flag && !flag2)
		{
			return false;
		}
		int num5 = (flag ? 1 : (-1));
		float num6 = (flag ? num : (0f - num3));
		float num7 = (flag ? num2 : (0f - num4));
		float num8 = (0f - num6) / (num7 - num6);
		float contactSeparation = 0f;
		if (Math.Abs(num7 - num6) < 1E-05f || num8 < 0f)
		{
			num8 = 0f;
			contactSeparation = num7;
		}
		bounds.GetLocalContactPoint(resultX3, resultY3, out var pointX, out var pointY);
		Vector2 contactPoint = new Vector2(pointX, pointY);
		Vector2 contactNormal = new Vector2((float)(-num5) * y2, (float)num5 * x2);
		Vector2 relativeVelocity = new Vector2((float)num5 * (resultX2 - resultX), (float)num5 * (resultY2 - resultY));
		result = new TOIResult(num8, 1, contactPoint, contactNormal, contactSeparation, relativeVelocity);
		return true;
	}

	private bool ComputeTimeOfImpactOuterCircle(BpleLightEmitter light, ref CCDData data, out TOIResult result)
	{
		result = TOIResult.Default;
		LightData data2 = light.Data;
		float time = data.Time;
		Vector2 vector = (1f - time) * data2.Position0 + time * data2.Position1 - data.Position0;
		Vector2 vector2 = data2.Position1 - data.Position1;
		if (0f - BpleLightMath.Dot2(vector, data2.Direction0) <= light.Cos * BpleLightMath.Length2(vector) && 0f - BpleLightMath.Dot2(vector2, data2.Direction1) <= light.Cos * BpleLightMath.Length2(vector2))
		{
			return false;
		}
		float x = data.Direction1.x;
		float y = data.Direction1.y;
		float num = x * vector.x + y * vector.y;
		float num2 = x * vector.y - y * vector.x;
		float num3 = x * vector2.x + y * vector2.y;
		float num4 = x * vector2.y - y * vector2.x;
		float num5 = num3 - num;
		float num6 = num4 - num2;
		if (BpleLightMath.LengthSquared2(num5, num6) < 1E-10f)
		{
			return false;
		}
		float num7 = light.Length + light.HalfWidth;
		float num8 = num7 - 0.5f;
		float num9 = num7 * num7;
		float num10 = num8 * num8;
		BpleLightBounds bounds = data.Bounds;
		int count = 0;
		float x2 = 0f;
		float x3 = 0f;
		int count2 = 0;
		float x4 = 0f;
		float x5 = 0f;
		if (bounds.IsCircle)
		{
			LineAndCircleIntersection(num, num2, num5, num6, 0f, 0f, num7 + bounds.R, out count, out x2, out x3);
			LineAndCircleIntersection(num, num2, num5, num6, 0f, 0f, num8 + bounds.R, out count2, out x4, out x5);
		}
		else if (Math.Abs(num5) < 1E-05f)
		{
			float num11 = Math.Max(Math.Abs(num) - bounds.A, 0f);
			int num12 = ((num6 > 0f) ? 1 : (-1));
			if (num7 > num11)
			{
				float num13 = (float)num12 * ((float)Math.Sqrt(num9 - num11 * num11) + bounds.B);
				count = 2;
				x2 = (0f - num13 - num2) / num6;
				x3 = (num13 - num2) / num6;
			}
			if (num8 > num11)
			{
				float num14 = (float)num12 * ((float)Math.Sqrt(num10 - num11 * num11) + bounds.B);
				count2 = 2;
				x4 = (0f - num14 - num2) / num6;
				x5 = (num14 - num2) / num6;
			}
		}
		else if (Math.Abs(num6) < 1E-05f)
		{
			float num15 = Math.Max(Math.Abs(num2) - bounds.B, 0f);
			int num16 = ((num5 > 0f) ? 1 : (-1));
			if (num7 > num15)
			{
				float num17 = (float)num16 * ((float)Math.Sqrt(num9 - num15 * num15) + bounds.A);
				count = 2;
				x2 = (0f - num17 - num) / num5;
				x3 = (num17 - num) / num5;
			}
			if (num8 > num15)
			{
				float num18 = (float)num16 * ((float)Math.Sqrt(num10 - num15 * num15) + bounds.A);
				count2 = 2;
				x4 = (0f - num18 - num) / num5;
				x5 = (num18 - num) / num5;
			}
		}
		else
		{
			float item = (0f - bounds.A - num) / num5;
			float item2 = (bounds.A - num) / num5;
			float item3 = (0f - bounds.B - num2) / num6;
			float item4 = (bounds.B - num2) / num6;
			SortFour((item, 0), (item2, 1), (item3, 2), (item4, 3), out var b, out var b2, out var b3, out var b4);
			int num19 = -1;
			int num20 = -1;
			float num21 = float.NegativeInfinity;
			for (int i = 0; i < 5; i++)
			{
				(float, int) tuple = ((i >= 2) ? ((i == 2) ? b3 : b4) : ((i == 0) ? b : b2));
				float num22;
				if (i < 4)
				{
					(num22, _) = tuple;
				}
				else
				{
					num22 = float.PositiveInfinity;
				}
				float num23 = num22;
				int num24 = ((num5 > 0f) ? num19 : (-num19));
				int num25 = ((num6 > 0f) ? num20 : (-num20));
				float num26 = (float)num24 * num5;
				float num27 = (float)num24 * num - bounds.A;
				float num28 = (float)num25 * num6;
				float num29 = (float)num25 * num2 - bounds.B;
				if (num24 != 0 && num25 != 0)
				{
					BpleQuadratic quadraticFunction = new BpleQuadratic(num26 * num26 + num28 * num28, 2f * (num26 * num27 + num28 * num29), num27 * num27 + num29 * num29);
					BpleQuadratic.IntersectResult intersections = quadraticFunction.GetIntersections(num9);
					BpleQuadratic.IntersectResult intersections2 = quadraticFunction.GetIntersections(num10);
					for (int j = 0; j < intersections.Count; j++)
					{
						float num30 = intersections[j];
						if (num21 <= num30 && num30 < num23)
						{
							SetResult(num30, ref count, ref x2, ref x3);
						}
					}
					for (int k = 0; k < intersections2.Count; k++)
					{
						float num31 = intersections2[k];
						if (num21 <= num31 && num31 < num23)
						{
							SetResult(num31, ref count2, ref x4, ref x5);
						}
					}
				}
				else if (num24 != 0 && num25 == 0)
				{
					float num32 = (num7 - num27) / num26;
					float num33 = (num8 - num27) / num26;
					if (num21 <= num32 && num32 < num23)
					{
						SetResult(num32, ref count, ref x2, ref x3);
					}
					if (num21 <= num33 && num33 < num23)
					{
						SetResult(num33, ref count2, ref x4, ref x5);
					}
				}
				else if (num24 == 0 && num25 != 0)
				{
					float num34 = (num7 - num29) / num28;
					float num35 = (num8 - num29) / num28;
					if (num21 <= num34 && num34 < num23)
					{
						SetResult(num34, ref count, ref x2, ref x3);
					}
					if (num21 <= num35 && num35 < num23)
					{
						SetResult(num35, ref count2, ref x4, ref x5);
					}
				}
				num21 = num23;
				if (tuple.Item2 < 2)
				{
					num19++;
				}
				else
				{
					num20++;
				}
			}
		}
		if (count == 2 && ((count2 == 2) ? x4 : x3) >= 0f && x2 <= 1f)
		{
			float num36 = ((x2 > 0f) ? x2 : 0f);
			float num37 = num + num5 * num36;
			float num38 = num2 + num6 * num36;
			Vector2 vector3 = new Vector2(num37, num38);
			Vector2 vector4 = default(Vector2);
			Vector2 vector5 = default(Vector2);
			float num39 = 0f;
			if (bounds.IsCircle)
			{
				num39 = BpleLightMath.Length2(vector3);
				vector4 = bounds.R / num39 * vector3;
				vector5 = -1f / num39 * vector3;
				num39 -= bounds.R;
			}
			else
			{
				int num40 = ((num37 < 0f - bounds.A) ? (-1) : ((!(num37 < bounds.A)) ? 1 : 0));
				int num41 = ((num38 < 0f - bounds.B) ? (-1) : ((!(num38 < bounds.B)) ? 1 : 0));
				if (num40 == 0 && num41 == 0)
				{
					return false;
				}
				if (num40 != 0 && num41 != 0)
				{
					vector4 = new Vector2((float)num40 * bounds.A, (float)num41 * bounds.B);
					vector5 = vector4 - vector3;
					num39 = BpleLightMath.Length2(vector5);
					vector5 /= num39;
				}
				else if (num40 == 0 && num41 != 0)
				{
					vector4 = new Vector2(num37, (float)num41 * bounds.B);
					vector5 = new Vector2(0f, -num41);
					num39 = Math.Abs(num38) - bounds.B;
				}
				else if (num40 != 0 && num41 == 0)
				{
					vector4 = new Vector2((float)num40 * bounds.A, num38);
					vector5 = new Vector2(-num40, 0f);
					num39 = Math.Abs(num37) - bounds.A;
				}
			}
			result = new TOIResult(num36, 1, vector4, vector5, num39 - num7, default(Vector2));
			return true;
		}
		return false;
		static void SetResult(float value, ref int reference, ref float reference2, ref float reference3)
		{
			if (reference == 0)
			{
				reference = 1;
				reference2 = value;
			}
			else if (reference == 1 && Math.Abs(value - reference2) > 1E-05f)
			{
				reference = 2;
				if (value >= reference2)
				{
					reference3 = value;
				}
				else
				{
					reference3 = reference2;
					reference2 = value;
				}
			}
		}
	}

	private bool ComputeTimeOfImpactInnerCircle(BpleLightEmitter light, ref CCDData data, out TOIResult result)
	{
		result = TOIResult.Default;
		LightData data2 = light.Data;
		float time = data.Time;
		Vector2 vector = (1f - time) * data2.Position0 + time * data2.Position1 - data.Position0;
		Vector2 vector2 = data2.Position1 - data.Position1;
		if (0f - BpleLightMath.Dot2(vector, data2.Direction0) <= light.Cos * BpleLightMath.Length2(vector) && 0f - BpleLightMath.Dot2(vector2, data2.Direction1) <= light.Cos * BpleLightMath.Length2(vector2))
		{
			return false;
		}
		float x = data.Direction1.x;
		float y = data.Direction1.y;
		float num = x * vector.x + y * vector.y;
		float num2 = x * vector.y - y * vector.x;
		float num3 = x * vector2.x + y * vector2.y;
		float num4 = x * vector2.y - y * vector2.x;
		float num5 = num3 - num;
		float num6 = num4 - num2;
		if (BpleLightMath.LengthSquared2(num5, num6) < 1E-10f)
		{
			return false;
		}
		float num7 = light.Length - light.HalfWidth;
		float num8 = num7 + 0.5f;
		float num9 = num7 * num7;
		float num10 = num8 * num8;
		BpleLightBounds bounds = data.Bounds;
		int count = 0;
		float x2 = 0f;
		float x3 = 0f;
		int count2 = 0;
		float x4 = 0f;
		float x5 = 0f;
		if (bounds.IsCircle)
		{
			LineAndCircleIntersection(num, num2, num5, num6, 0f, 0f, num7 - bounds.R, out count, out x2, out x3);
			LineAndCircleIntersection(num, num2, num5, num6, 0f, 0f, num8 - bounds.R, out count2, out x4, out x5);
		}
		else if (Math.Abs(num5) < 1E-05f)
		{
			float num11 = Math.Abs(num) + bounds.A;
			int num12 = ((num6 > 0f) ? 1 : (-1));
			if (num7 > num11)
			{
				float num13 = (float)num12 * ((float)Math.Sqrt(num9 - num11 * num11) - bounds.B);
				count = 2;
				x2 = (0f - num13 - num2) / num6;
				x3 = (num13 - num2) / num6;
			}
			if (num8 > num11)
			{
				float num14 = (float)num12 * ((float)Math.Sqrt(num10 - num11 * num11) - bounds.B);
				count2 = 2;
				x4 = (0f - num14 - num2) / num6;
				x5 = (num14 - num2) / num6;
			}
		}
		else if (Math.Abs(num6) < 1E-05f)
		{
			float num15 = Math.Abs(num2) + bounds.B;
			int num16 = ((num5 > 0f) ? 1 : (-1));
			if (num7 > num15)
			{
				float num17 = (float)num16 * ((float)Math.Sqrt(num9 - num15 * num15) - bounds.A);
				count = 2;
				x2 = (0f - num17 - num) / num5;
				x3 = (num17 - num) / num5;
			}
			if (num8 > num15)
			{
				float num18 = (float)num16 * ((float)Math.Sqrt(num10 - num15 * num15) - bounds.A);
				count2 = 2;
				x4 = (0f - num18 - num) / num5;
				x5 = (num18 - num) / num5;
			}
		}
		else
		{
			float item = (0f - num) / num5;
			float item2 = (0f - num2) / num6;
			(float, int) tuple = (item, 0);
			(float, int) tuple2 = (item2, 1);
			if (tuple.CompareTo(tuple2) > 0)
			{
				(float, int) tuple3 = tuple;
				tuple = tuple2;
				tuple2 = tuple3;
			}
			int num19 = -1;
			int num20 = -1;
			float num21 = float.NegativeInfinity;
			for (int i = 0; i < 3; i++)
			{
				(float, int) tuple4 = ((i == 0) ? tuple : tuple2);
				float num22;
				if (i < 2)
				{
					(num22, _) = tuple4;
				}
				else
				{
					num22 = float.PositiveInfinity;
				}
				float num23 = num22;
				int num24 = ((num5 > 0f) ? num19 : (-num19));
				int num25 = ((num6 > 0f) ? num20 : (-num20));
				float num26 = (float)num24 * num5;
				float num27 = (float)num24 * num + bounds.A;
				float num28 = (float)num25 * num6;
				float num29 = (float)num25 * num2 + bounds.B;
				BpleQuadratic quadraticFunction = new BpleQuadratic(num26 * num26 + num28 * num28, 2f * (num26 * num27 + num28 * num29), num27 * num27 + num29 * num29);
				BpleQuadratic.IntersectResult intersections = quadraticFunction.GetIntersections(num9);
				BpleQuadratic.IntersectResult intersections2 = quadraticFunction.GetIntersections(num10);
				for (int j = 0; j < intersections.Count; j++)
				{
					float num30 = intersections[j];
					if (num21 <= num30 && num30 < num23)
					{
						SetResult(num30, ref count, ref x2, ref x3);
					}
				}
				for (int k = 0; k < intersections2.Count; k++)
				{
					float num31 = intersections2[k];
					if (num21 <= num31 && num31 < num23)
					{
						SetResult(num31, ref count2, ref x4, ref x5);
					}
				}
				num21 = num23;
				if (tuple4.Item2 == 0)
				{
					num19 += 2;
				}
				else
				{
					num20 += 2;
				}
			}
		}
		if (count2 == 2 && x5 >= 0f && ((count == 2) ? x3 : x4) <= 1f)
		{
			float num32 = ((count == 2 && x3 > 0f) ? x3 : 0f);
			float num33 = num + num5 * num32;
			float num34 = num2 + num6 * num32;
			int contactCount = 1;
			Vector2 vector3 = new Vector2(num33, num34);
			Vector2 vector4;
			Vector2 vector5;
			float num35;
			if (bounds.IsCircle)
			{
				num35 = BpleLightMath.Length2(vector3);
				vector4 = (0f - bounds.R) / num35 * vector3;
				vector5 = 1f / num35 * vector3;
				num35 += bounds.R;
			}
			else
			{
				int num36 = ((!(num33 < 0f)) ? 1 : (-1));
				int num37 = ((!(num34 < 0f)) ? 1 : (-1));
				num35 = BpleLightMath.Length2(num33 + (float)num36 * bounds.A, num34 + (float)num37 * bounds.B);
				if (Math.Abs(num33) / num7 < 0.02f)
				{
					contactCount = 2;
					num36 = 0;
				}
				else if (Math.Abs(num34) / num7 < 0.02f)
				{
					contactCount = 2;
					num37 = 0;
				}
				vector4 = new Vector2((float)(-num36) * bounds.A, (float)(-num37) * bounds.B);
				vector5 = vector3 - vector4;
				vector5 /= BpleLightMath.Length2(vector5);
			}
			result = new TOIResult(num32, contactCount, vector4, vector5, num7 - num35, default(Vector2));
			return true;
		}
		return false;
		static void SetResult(float value, ref int reference, ref float reference2, ref float reference3)
		{
			if (reference == 0)
			{
				reference = 1;
				reference2 = value;
			}
			else if (reference == 1 && Math.Abs(value - reference2) > 1E-05f)
			{
				reference = 2;
				if (value >= reference2)
				{
					reference3 = value;
				}
				else
				{
					reference3 = reference2;
					reference2 = value;
				}
			}
		}
	}

	private void LineAndCircleIntersection(float pX, float pY, float dX, float dY, float cX, float cY, float r, out int resultCount, out float result1, out float result2)
	{
		float num = pX - cX;
		float num2 = pY - cY;
		float num3 = dX * num + dY * num2;
		float num4 = dX * num2 - dY * num;
		float num5 = dX * dX + dY * dY;
		float num6 = r * r * num5 - num4 * num4;
		float num7 = 1E-10f;
		if (num6 < 0f - num7)
		{
			resultCount = 0;
			result1 = float.NaN;
			result2 = float.NaN;
		}
		else if (num6 < num7)
		{
			resultCount = 1;
			result1 = (0f - num3) / num5;
			result2 = (0f - num3) / num5;
		}
		else
		{
			float num8 = (float)Math.Sqrt(num6);
			resultCount = 2;
			result1 = (0f - num3 - num8) / num5;
			result2 = (0f - num3 + num8) / num5;
		}
	}

	private void SortFour<T>(T a1, T a2, T a3, T a4, out T b1, out T b2, out T b3, out T b4) where T : IComparable<T>
	{
		T val;
		T val2;
		if (a1.CompareTo(a2) <= 0)
		{
			val = a1;
			val2 = a2;
		}
		else
		{
			val = a2;
			val2 = a1;
		}
		T val3;
		T val4;
		if (a3.CompareTo(a4) <= 0)
		{
			val3 = a3;
			val4 = a4;
		}
		else
		{
			val3 = a4;
			val4 = a3;
		}
		if (val.CompareTo(val3) <= 0)
		{
			b1 = val;
			b2 = val3;
		}
		else
		{
			b1 = val3;
			b2 = val;
		}
		if (val2.CompareTo(val4) <= 0)
		{
			b3 = val2;
			b4 = val4;
		}
		else
		{
			b3 = val4;
			b4 = val2;
		}
		T other = b3;
		if (b2.CompareTo(other) > 0)
		{
			T val5 = b2;
			b2 = b3;
			b3 = val5;
		}
	}

	
	}


internal struct BpleLightBounds
{
	public int Type;

	public float X;

	public float Y;

	public float A;

	public float B;

	public float R;

	public bool IsRect => Type == 0;

	public bool IsCircle => Type == 1;

	public float GetHalfProjection(float directionX, float directionY)
	{
		if (Type == 0)
		{
			return ((directionX > 0f) ? directionX : (0f - directionX)) * A + ((directionY > 0f) ? directionY : (0f - directionY)) * B;
		}
		return R;
	}

	public float GetHalfProjection(Vector2 direction)
	{
		return GetHalfProjection(direction.x, direction.y);
	}

	public void GetLocalContactPoint(float directionX, float directionY, out float pointX, out float pointY)
	{
		if (Type == 0)
		{
			pointX = ((directionY > 0f) ? (0f - A) : A);
			pointY = ((directionX > 0f) ? (0f - B) : B);
		}
		else
		{
			pointX = 0f;
			pointY = 0f - R;
		}
	}

	public void GetContactPoint(float directionX, float directionY, out float pointX, out float pointY)
	{
		if (Type == 0)
		{
			float num = ((directionY > 0f) ? (0f - A) : A);
			float num2 = ((directionX > 0f) ? (0f - B) : B);
			pointX = directionX * num - directionY * num2;
			pointY = directionY * num + directionX * num2;
		}
		else
		{
			pointX = 0f;
			pointY = 0f - R;
		}
	}

	public Vector2 GetContactPoint(Vector2 direction)
	{
		GetContactPoint(direction.x, direction.y, out var pointX, out var pointY);
		return new Vector2(pointX, pointY);
	}
}


internal struct BpleQuadratic
{
	public readonly struct IntersectResult
	{
		public readonly int Count;

		public readonly float X1;

		public readonly float X2;

		public static IntersectResult None => default(IntersectResult);

		public float this[int index]
		{
			get
			{
				if (index < 0 || index >= Count)
				{
					throw new ArgumentOutOfRangeException("index");
				}
				if (index != 0)
				{
					return X2;
				}
				return X1;
			}
		}

		public IntersectResult(float x)
			: this(1, x, 0f)
		{
		}

		public IntersectResult(float x1, float x2)
			: this(2, x1, x2)
		{
		}

		private IntersectResult(int count, float x1, float x2)
		{
			Count = count;
			X1 = x1;
			X2 = x2;
		}
	}

	public float A;

	public float B;

	public float C;

	public BpleQuadratic(float a, float b, float c)
	{
		A = a;
		B = b;
		C = c;
	}

	public float GetValue(float x)
	{
		return A * x * x + B * x + C;
	}

	public IntersectResult GetIntersections(float y)
	{
		return SolveEquation(A, B, C - y);
	}

	public float GetMinValue(float left, float right)
	{
		if (A > 0f)
		{
			float num = (0f - B) / (2f * A);
			if (left <= num && num < right)
			{
				return GetValue(num);
			}
		}
		return Math.Min(GetValue(left), GetValue(right));
	}

	public float GetMaxValue(float left, float right)
	{
		if (A < 0f)
		{
			float num = (0f - B) / (2f * A);
			if (left <= num && num < right)
			{
				return GetValue(num);
			}
		}
		return Math.Max(GetValue(left), GetValue(right));
	}

	public static IntersectResult SolveEquation(float a, float b, float c)
	{
		if (a == 0f)
		{
			if (b == 0f)
			{
				return IntersectResult.None;
			}
			return new IntersectResult((0f - c) / b);
		}
		float num = b * b - 4f * a * c;
		if (num > 0f)
		{
			float num2 = (float)Math.Sqrt(num);
			return new IntersectResult((0f - b - num2) / (2f * a), (0f - b + num2) / (2f * a));
		}
		if (num == 0f)
		{
			return new IntersectResult((0f - b) / (2f * a));
		}
		return IntersectResult.None;
	}
}

	internal static class BpleLightMath
	{
		public static float Length2(Vector2 v) => (float)Math.Sqrt(v.x*v.x+v.y*v.y);
		public static float Length2(float x, float y) => (float)Math.Sqrt(x*x+y*y);
		public static float LengthSquared2(float x,float y) => x*x+y*y;
		public static float Dot2(Vector2 a,Vector2 b) => a.x*b.x+a.y*b.y;
		public static float Cross2(Vector2 a,Vector2 b) => a.x*b.y-a.y*b.x;
		public static Vector2 Transform2(Vector2 v,Vector2 d) => new Vector2(v.x*d.x-v.y*d.y,v.x*d.y+v.y*d.x);
		public static Vector2 InvTransform2(Vector2 v,Vector2 d) => new Vector2(v.x*d.x+v.y*d.y,-v.x*d.y+v.y*d.x);
		public static void InvTransform2(Vector2 v,Vector2 d,out float x,out float y) => InvTransform2(v.x,v.y,d.x,d.y,out x,out y);
		public static void InvTransform2(float vx,float vy,float dx,float dy,out float x,out float y) { x=vx*dx+vy*dy; y=-vx*dy+vy*dx; }
	}

}

