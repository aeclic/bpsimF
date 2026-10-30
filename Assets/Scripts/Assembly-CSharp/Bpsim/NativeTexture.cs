using System;
using Unity.Collections;
using UnityEngine;

namespace Bpsim
{
	public struct NativeTexture
	{
		public readonly int Width;

		public readonly int Height;

		public NativeArray<Color32> Data;

		public Color32 this[int x, int y]
		{
			get
			{
				return GetPixel(x, y);
			}
			set
			{
				SetPixel(x, y, value);
			}
		}

		public NativeTexture(Texture2D texture)
		{
			Width = texture.width;
			Height = texture.height;
			Data = texture.GetRawTextureData<Color32>();
		}

		public Color32 GetPixel(int x, int y)
		{
			if (x < 0 || x >= Width)
			{
				throw new ArgumentOutOfRangeException("x");
			}
			if (y < 0 || y >= Height)
			{
				throw new ArgumentOutOfRangeException("y");
			}
			return Data[Width * y + x];
		}

		public void SetPixel(int x, int y, Color32 color)
		{
			if (x < 0 || x >= Width)
			{
				throw new ArgumentOutOfRangeException("x");
			}
			if (y < 0 || y >= Height)
			{
				throw new ArgumentOutOfRangeException("y");
			}
			Data[Width * y + x] = color;
		}

		public Color32 GetPixelBilinear(float u, float v)
		{
			float num = u * (float)Width;
			float num2 = v * (float)Height;
			int num3 = (int)num;
			int num4 = (int)num2;
			float num5;
			if (num3 < 0)
			{
				num3 = 0;
				num5 = 0f;
			}
			else if (num3 >= Width - 1)
			{
				num3 = Width - 2;
				num5 = 1f;
			}
			else
			{
				num5 = num - (float)num3;
			}
			float num6;
			if (num4 < 0)
			{
				num4 = 0;
				num6 = 0f;
			}
			else if (num4 >= Width - 1)
			{
				num4 = Width - 2;
				num6 = 1f;
			}
			else
			{
				num6 = num2 - (float)num4;
			}
			float num7 = num5 * num6;
			Color32 pixel = GetPixel(num3, num4);
			Color32 pixel2 = GetPixel(num3, num4 + 1);
			Color32 pixel3 = GetPixel(num3 + 1, num4);
			Color32 pixel4 = GetPixel(num3 + 1, num4 + 1);
			float num8 = (float)(int)pixel.r + num5 * (float)(pixel3.r - pixel.r) + num6 * (float)(pixel2.r - pixel.r) + num7 * (float)(pixel4.r - pixel2.r - pixel3.r + pixel.r);
			float num9 = (float)(int)pixel.g + num5 * (float)(pixel3.g - pixel.g) + num6 * (float)(pixel2.g - pixel.g) + num7 * (float)(pixel4.g - pixel2.g - pixel3.g + pixel.g);
			float num10 = (float)(int)pixel.b + num5 * (float)(pixel3.b - pixel.b) + num6 * (float)(pixel2.b - pixel.b) + num7 * (float)(pixel4.b - pixel2.b - pixel3.b + pixel.b);
			float num11 = (float)(int)pixel.a + num5 * (float)(pixel3.a - pixel.a) + num6 * (float)(pixel2.a - pixel.a) + num7 * (float)(pixel4.a - pixel2.a - pixel3.a + pixel.a);
			return new Color32((byte)num8, (byte)num9, (byte)num10, (byte)num11);
		}
	}
}
