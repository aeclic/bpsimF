using System.Runtime.CompilerServices;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Bpsim.Parts
{
	public struct ColoredFrame : IComponentData, IQueryTypeParameter
	{
		public bool IsTransparent;

		public bool HasChanged;

		public float4 Color;

		public static float4 GetColor(PartTypeInfo info)
		{
			if (info.BelongsTo(BasePart.TransparentFrames))
			{
				return new float4(1f);
			}
			int num = info.PartIndex - 12;
			if (num >= 108)
			{
				float num2 = 1f - (float)(num - 108) / 10f;
				return new float4(num2, num2, num2, 1f);
			}
			int num3 = num % 18;
			int num4 = num / 18;
			float h = (float)num3 / 18f;
			int num5 = num4 % 2;
			float s = num5 switch
			{
				0 => 0.7f, 
				1 => 0.4f, 
				_ => throw new SwitchExpressionException(num5), 
			};
			num5 = num4 / 2;
			Color color = UnityEngine.Color.HSVToRGB(h, s, num5 switch
			{
				0 => 0.9f, 
				1 => 0.6f, 
				2 => 0.3f, 
				_ => throw new SwitchExpressionException(num5), 
			});
			return new float4(color.r, color.g, color.b, color.a);
		}
	}
}
