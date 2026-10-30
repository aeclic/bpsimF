using System;
using System.Globalization;
using System.Text.Json.Serialization;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;

namespace Bpsim
{
	[System.Text.Json.Serialization.JsonConverter(typeof(HexColorConverter))]
	[Newtonsoft.Json.JsonConverter(typeof(HexColorConverterLegacy))]
	public readonly struct HexColor
	{
		private readonly uint m_rgba;

		public byte R => (byte)((m_rgba >> 24) & 0xFF);

		public byte G => (byte)((m_rgba >> 16) & 0xFF);

		public byte B => (byte)((m_rgba >> 8) & 0xFF);

		public byte A => (byte)(m_rgba & 0xFF);

		public uint RGBA => m_rgba;

		public static HexColor Clear => default(HexColor);

		public static HexColor White => new HexColor(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);

		public static HexColor Black => new HexColor(0, 0, 0, byte.MaxValue);

		public static HexColor Red => new HexColor(byte.MaxValue, 0, 0, byte.MaxValue);

		public static HexColor Green => new HexColor(0, byte.MaxValue, 0, byte.MaxValue);

		public static HexColor Blue => new HexColor(0, 0, byte.MaxValue, byte.MaxValue);

		public HexColor(byte r, byte g, byte b)
			: this(r, g, b, byte.MaxValue)
		{
		}

		public HexColor(byte r, byte g, byte b, byte a)
		{
			m_rgba = (uint)((r << 24) | (g << 16) | (b << 8) | a);
		}

		public HexColor(uint rgba)
		{
			m_rgba = rgba;
		}

		public override bool Equals(object other)
		{
			if (other is HexColor other2)
			{
				return Equals(other2);
			}
			return false;
		}

		public bool Equals(HexColor other)
		{
			return m_rgba == other.m_rgba;
		}

		public override int GetHashCode()
		{
			return (int)m_rgba;
		}

		public override string ToString()
		{
			return ToString(includeAlpha: true);
		}

		public string ToString(bool includeAlpha)
		{
			if (includeAlpha)
			{
				return "#" + m_rgba.ToString("X8", NumberFormatInfo.InvariantInfo);
			}
			return "#" + (m_rgba >> 8).ToString("X6", NumberFormatInfo.InvariantInfo);
		}

		public static HexColor Parse(string text)
		{
			if (TryParse(text, out var result))
			{
				return result;
			}
			throw new FormatException();
		}

		public static bool TryParse(string text, out HexColor result)
		{
			if (!string.IsNullOrEmpty(text) && text.StartsWith('#'))
			{
				uint result3;
				if (text.Length == 7)
				{
					if (uint.TryParse(text.AsSpan().Slice(1), NumberStyles.HexNumber, NumberFormatInfo.InvariantInfo, out var result2))
					{
						result = new HexColor((result2 << 8) | 0xFF);
						return true;
					}
				}
				else if (text.Length == 9 && uint.TryParse(text.AsSpan().Slice(1), NumberStyles.HexNumber, NumberFormatInfo.InvariantInfo, out result3))
				{
					result = new HexColor(result3);
					return true;
				}
			}
			result = default(HexColor);
			return false;
		}

		public static bool operator ==(HexColor left, HexColor right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(HexColor left, HexColor right)
		{
			return !(left == right);
		}

		public static explicit operator Color32(HexColor color)
		{
			return new Color32(color.R, color.G, color.B, color.A);
		}

		public static explicit operator Color(HexColor color)
		{
			return new Color((float)(int)color.R / 255f, (float)(int)color.G / 255f, (float)(int)color.B / 255f, (float)(int)color.A / 255f);
		}

		public static explicit operator Vector4(HexColor color)
		{
			return new Vector4((float)(int)color.R / 255f, (float)(int)color.G / 255f, (float)(int)color.B / 255f, (float)(int)color.A / 255f);
		}

		public static explicit operator float4(HexColor color)
		{
			return new Vector4((float)(int)color.R / 255f, (float)(int)color.G / 255f, (float)(int)color.B / 255f, (float)(int)color.A / 255f);
		}

		public static explicit operator HexColor(Color32 color)
		{
			return new HexColor(color.r, color.g, color.b, color.a);
		}

		public static explicit operator HexColor(Color color)
		{
			byte r = (byte)Math.Round(Math.Clamp(color.r, 0f, 1f) * 255f);
			byte g = (byte)Math.Round(Math.Clamp(color.g, 0f, 1f) * 255f);
			byte b = (byte)Math.Round(Math.Clamp(color.b, 0f, 1f) * 255f);
			byte a = (byte)Math.Round(Math.Clamp(color.a, 0f, 1f) * 255f);
			return new HexColor(r, g, b, a);
		}

		public static explicit operator HexColor(Vector4 color)
		{
			byte r = (byte)Math.Round(Math.Clamp(color.x, 0f, 1f) * 255f);
			byte g = (byte)Math.Round(Math.Clamp(color.y, 0f, 1f) * 255f);
			byte b = (byte)Math.Round(Math.Clamp(color.z, 0f, 1f) * 255f);
			byte a = (byte)Math.Round(Math.Clamp(color.w, 0f, 1f) * 255f);
			return new HexColor(r, g, b, a);
		}

		public static explicit operator HexColor(float4 color)
		{
			float4 @float = math.round(math.clamp(color, 0f, 1f) * 255f);
			return new HexColor((byte)@float.x, (byte)@float.y, (byte)@float.z, (byte)@float.w);
		}
	}
}
