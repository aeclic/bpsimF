using System;
using System.Collections.Generic;
using System.Numerics;
using Bpsim.Rendering;
using UnityEngine;

namespace Bpsim.Templates
{
	public static class TemplateRenderer
	{
		public class TextureInfo
		{
			public TransformInfo Transform { get; private set; }

			public Texture2D Texture { get; private set; }

			public HexColor Color { get; private set; }

			public string SpriteName { get; private set; }

			public TextureInfo(TransformInfo transform, Texture2D texture, HexColor color, string spriteName)
			{
				Transform = transform;
				Texture = texture;
				Color = color;
				SpriteName = spriteName;
			}
		}

		public readonly struct TransformInfo
		{
			public readonly UnityEngine.Vector3 Position;

			public readonly UnityEngine.Quaternion Rotation;

			public readonly UnityEngine.Vector3 Scale;

			public static readonly TransformInfo Identity = new TransformInfo(UnityEngine.Vector3.zero, UnityEngine.Quaternion.identity, UnityEngine.Vector3.one);

			public TransformInfo(UnityEngine.Vector3 position, UnityEngine.Quaternion rotation, UnityEngine.Vector3 scale)
			{
				Position = position;
				Rotation = rotation;
				Scale = scale;
			}
		}

		public readonly struct SpriteRect
		{
			public readonly int X;

			public readonly int Y;

			public readonly int Width;

			public readonly int Height;

			public SpriteRect(int x, int y, int width, int height)
			{
				X = x;
				Y = y;
				Width = width;
				Height = height;
			}

			public bool IsEmpty()
			{
				if (Width > 0)
				{
					return Height <= 0;
				}
				return true;
			}

			public static SpriteRect FromPositionSize(Vector2Int position, Vector2Int size)
			{
				return new SpriteRect(position.x, position.y, size.x, size.y);
			}

			public static SpriteRect FromMinMax(int xMin, int xMax, int yMin, int yMax)
			{
				return new SpriteRect(xMin, yMin, xMax - xMin, yMax - yMin);
			}

			public static SpriteRect Union(SpriteRect left, SpriteRect right)
			{
				bool num = left.IsEmpty();
				bool flag = right.IsEmpty();
				if (num)
				{
					if (flag)
					{
						return default(SpriteRect);
					}
					return right;
				}
				if (flag)
				{
					return left;
				}
				return UnionNotEmpty(left, right);
			}

			private static SpriteRect UnionNotEmpty(SpriteRect left, SpriteRect right)
			{
				int xMin = Math.Min(left.X, right.X);
				int xMax = Math.Max(left.X + left.Width, right.X + right.Width);
				int yMin = Math.Min(left.Y, right.Y);
				int yMax = Math.Max(left.Y + left.Height, right.Y + right.Height);
				return FromMinMax(xMin, xMax, yMin, yMax);
			}
		}

		public static List<TextureInfo> Build(GameObjectTemplate template, IResourceResolver resolver)
		{
			List<TextureInfo> list = new List<TextureInfo>();
			Visit(0, template, TransformInfo.Identity, resolver, list);
			list.Sort((TextureInfo x, TextureInfo y) => -x.Transform.Position.z.CompareTo(y.Transform.Position.z));
			return list;
		}

		private static void Visit(int depth, GameObjectTemplate template, TransformInfo parentTransform, IResourceResolver resolver, List<TextureInfo> result)
		{
			if (!template.Active)
			{
				return;
			}
			TransformTemplate component = template.GetComponent<TransformTemplate>();
			RendererTemplate component2 = template.GetComponent<RendererTemplate>();
			SpriteTemplate component3 = template.GetComponent<SpriteTemplate>();
			UnityEngine.Vector3 position = parentTransform.Position + parentTransform.Rotation * UnityEngine.Vector3.Scale(parentTransform.Scale, component.LocalPosition);
			UnityEngine.Quaternion rotation = parentTransform.Rotation * component.LocalRotation;
			UnityEngine.Vector3 scale = UnityEngine.Vector3.Scale(parentTransform.Scale, component.LocalScale);
			TransformInfo transformInfo = new TransformInfo(position, rotation, scale);
			if (component2 != null && component2.Enabled)
			{
				MaterialTemplate material = component2.Material;
				bool flag = !string.IsNullOrEmpty(material.Name);
				if (!string.IsNullOrEmpty(material.Texture))
				{
					Texture2D texture = resolver.ResolveTexture(material.Texture);
					result.Add(new TextureInfo(transformInfo, texture, material.Color, component3?.Name));
				}
				else if (flag)
				{
					Texture2D texture2 = (Texture2D)resolver.ResolveMaterial(material.Name).mainTexture;
					result.Add(new TextureInfo(transformInfo, texture2, material.Color, component3?.Name));
				}
			}
			foreach (GameObjectTemplate child in template.Children)
			{
				Visit(depth + 1, child, transformInfo, resolver, result);
			}
		}

		public static void Render(List<TextureInfo> textureInfo, int offsetX, int offsetY, float scale, ref NativeTexture result, out SpriteRect rect)
		{
			rect = default(SpriteRect);
			foreach (TextureInfo item in textureInfo)
			{
				if (item.Color.A != 0)
				{
					if (item.Texture == null || string.IsNullOrEmpty(item.SpriteName))
					{
						RenderSolidColor(item, offsetX, offsetY, scale, ref result, ref rect);
					}
					else
					{
						RenderSprite(item, offsetX, offsetY, scale, ref result, ref rect);
					}
				}
			}
		}

		private static void RenderSolidColor(TextureInfo info, int offsetX, int offsetY, float scale, ref NativeTexture result, ref SpriteRect rect)
		{
			Color32 color = (Color32)info.Color;
			Matrix3x2 matrix = Matrix3x2.CreateScale(0.5f, 0.5f) * GetWorldMatrix(info.Transform) * GetRenderMatrix(offsetX, offsetY, scale);
			Matrix3x2.Invert(matrix, out var result2);
			float num = Math.Abs(matrix.M11) + Math.Abs(matrix.M21);
			float num2 = Math.Abs(matrix.M12) + Math.Abs(matrix.M22);
			int num3 = (int)Math.Ceiling(matrix.M31 - num);
			int num4 = (int)Math.Floor(matrix.M31 + num);
			int num5 = (int)Math.Ceiling(matrix.M32 - num2);
			int num6 = (int)Math.Floor(matrix.M32 + num2);
			rect = SpriteRect.Union(rect, SpriteRect.FromMinMax(num3, num4, num5, num6));
			for (int i = num5; i <= num6; i++)
			{
				for (int j = num3; j <= num4; j++)
				{
					System.Numerics.Vector2 vector = System.Numerics.Vector2.Transform(new System.Numerics.Vector2(j, i), result2);
					if (!(vector.X < -1f) && !(vector.X > 1f) && !(vector.Y < -1f) && !(vector.Y > 1f))
					{
						Color32 pixel = result.GetPixel(j, i);
						float num7 = (float)(int)color.a / 255f;
						float num8 = (float)(int)pixel.a / 255f;
						float num9 = num7 + (1f - num7) * num8;
						float num10 = num7 / num9;
						float num11 = (float)(int)pixel.r + num10 * (float)(color.r - pixel.r);
						float num12 = (float)(int)pixel.g + num10 * (float)(color.g - pixel.g);
						float num13 = (float)(int)pixel.b + num10 * (float)(color.b - pixel.b);
						result.SetPixel(j, i, new Color32((byte)num11, (byte)num12, (byte)num13, (byte)(num9 * 255f)));
					}
				}
			}
		}

		private static void RenderSprite(TextureInfo info, int offsetX, int offsetY, float scale, ref NativeTexture result, ref SpriteRect rect)
		{
			Color color = (Color)info.Color;
			NativeTexture nativeTexture = new NativeTexture(info.Texture);
			SpriteManager instance = SpriteManager.Instance;
			int iD = instance.GetID(info.SpriteName);
			Bpsim.Rendering.SpriteRect data = instance.GetSprite(iD);
			float u = data.U;
			float v = data.V;
			float w = data.W;
			float h = data.H;
			Matrix3x2 matrix = GetSpriteMatrix(in data) * GetWorldMatrix(info.Transform) * GetRenderMatrix(offsetX, offsetY, scale);
			Matrix3x2.Invert(matrix, out var result2);
			float num = Math.Abs(matrix.M11) + Math.Abs(matrix.M21);
			float num2 = Math.Abs(matrix.M12) + Math.Abs(matrix.M22);
			int num3 = (int)Math.Ceiling(matrix.M31 - num);
			int num4 = (int)Math.Floor(matrix.M31 + num);
			int num5 = (int)Math.Ceiling(matrix.M32 - num2);
			int num6 = (int)Math.Floor(matrix.M32 + num2);
			rect = SpriteRect.Union(rect, SpriteRect.FromMinMax(num3, num4, num5, num6));
			for (int i = num5; i <= num6; i++)
			{
				for (int j = num3; j <= num4; j++)
				{
					System.Numerics.Vector2 vector = System.Numerics.Vector2.Transform(new System.Numerics.Vector2(j, i), result2);
					if (!(vector.X < -1f) && !(vector.X > 1f) && !(vector.Y < -1f) && !(vector.Y > 1f))
					{
						float u2 = u + 0.5f * (vector.X + 1f) * w;
						float v2 = v + 0.5f * (vector.Y + 1f) * h;
						Color32 pixelBilinear = nativeTexture.GetPixelBilinear(u2, v2);
						if (pixelBilinear.a != 0)
						{
							Color32 pixel = result.GetPixel(j, i);
							float num7 = (float)(int)pixelBilinear.a * color.a / 255f;
							float num8 = (float)(int)pixel.a / 255f;
							float num9 = num7 + (1f - num7) * num8;
							float num10 = num7 / num9;
							float num11 = (float)(int)pixel.r + num10 * ((float)(int)pixelBilinear.r * color.r - (float)(int)pixel.r);
							float num12 = (float)(int)pixel.g + num10 * ((float)(int)pixelBilinear.g * color.g - (float)(int)pixel.g);
							float num13 = (float)(int)pixel.b + num10 * ((float)(int)pixelBilinear.b * color.b - (float)(int)pixel.b);
							result.SetPixel(j, i, new Color32((byte)num11, (byte)num12, (byte)num13, (byte)(num9 * 255f)));
						}
					}
				}
			}
		}

		private static Matrix3x2 GetWorldMatrix(in TransformInfo transform)
		{
			UnityEngine.Matrix4x4 matrix4x = UnityEngine.Matrix4x4.Rotate(transform.Rotation);
			return new Matrix3x2(matrix4x.m00 * transform.Scale.x, matrix4x.m10 * transform.Scale.x, matrix4x.m01 * transform.Scale.y, matrix4x.m11 * transform.Scale.y, transform.Position.x, transform.Position.y);
		}

		private static Matrix3x2 GetSpriteMatrix(in Bpsim.Rendering.SpriteRect data)
		{
			return new Matrix3x2(0.5f * data.VertexW, 0f, 0f, 0.5f * data.VertexH, data.VertexX, data.VertexY);
		}

		private static Matrix3x2 GetRenderMatrix(int offsetX, int offsetY, float scale)
		{
			return new Matrix3x2(scale, 0f, 0f, scale, offsetX, offsetY);
		}
	}
}
