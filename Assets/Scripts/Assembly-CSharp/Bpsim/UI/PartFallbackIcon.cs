using UnityEngine;

namespace Bpsim.UI
{
	/// <summary>
	/// 找不到渲染图时的占位图标（现画，不依赖图集）。
	/// </summary>
	/// <remarks>
	/// <para>
	/// 选择器图标模板里的 <c>PartIcon</c> 是一个 <c>RawImage</c>，贴图是整张
	/// <c>Texture2D/Part_Rendered_Texture_0.png</c>（4096×2048），靠同物体上的
	/// <see cref="Bpsim.Rendering.UISprite"/> 把自己的 <c>uvRect</c> 收到对应那一格。
	/// </para>
	/// <para>
	/// 模板上的 <c>UISprite.m_name</c> 是空的，<c>SpriteBase.Apply()</c> 遇到空名字直接
	/// return，所以 <c>uvRect</c> 一直停在默认的 (0,0,1,1) —— 名字留空的部件（后加的
	/// Mod 变体、迁移进来的部件）于是把<b>整张图集</b>铺在按钮上。
	/// 这里现画一张 64×64 的灰底方块补上，配合
	/// <see cref="PartSelector.SetRenderedIcon(RectTransform, string)"/> 里显式写
	/// <c>uvRect = (0,0,1,1)</c>，按钮上就只会显示这张小图。
	/// </para>
	/// </remarks>
	internal static class PartFallbackIcon
	{
		private const int Size = 64;

		private static Texture2D s_texture;

		/// <summary>图标贴图（懒生成，进程内复用）。</summary>
		public static Texture2D Texture
		{
			get
			{
				if (s_texture == null)
				{
					s_texture = Build();
				}
				return s_texture;
			}
		}

		private static Texture2D Build()
		{
			Color32 clear = new Color32(0, 0, 0, 0);
			Color32 frame = new Color32(150, 158, 168, 255);
			Color32 fill = new Color32(96, 104, 114, 255);
			Color32 mark = new Color32(190, 198, 208, 255);

			Color32[] pixels = new Color32[Size * Size];
			for (int i = 0; i < pixels.Length; i++)
			{
				pixels[i] = clear;
			}

			float center = (Size - 1) * 0.5f;
			for (int y = 0; y < Size; y++)
			{
				for (int x = 0; x < Size; x++)
				{
					Color32 color = clear;
					if (RoundBox(x, y, center, center, 26f, 26f, 8f) <= 0f)
					{
						color = frame;
						if (RoundBox(x, y, center, center, 20f, 20f, 5f) <= 0f)
						{
							color = fill;
						}
					}
					// 中间画一个简单的「问号棒」：竖条 + 上横条。
					if (color.a != 0 && y >= 20 && y <= 44 && x >= 29 && x <= 34)
					{
						color = mark;
					}
					if (color.a != 0 && y >= 40 && y <= 44 && x >= 29 && x <= 42)
					{
						color = mark;
					}
					pixels[y * Size + x] = color;
				}
			}

			Texture2D texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
			{
				name = "PartFallbackIcon",
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp,
				hideFlags = HideFlags.DontSave
			};
			texture.SetPixels32(pixels);
			texture.Apply(false, false);
			return texture;
		}

		/// <summary>圆角矩形的有符号距离，&lt;= 0 表示在内部。</summary>
		private static float RoundBox(float x, float y, float cx, float cy, float halfX, float halfY, float radius)
		{
			float dx = Mathf.Abs(x - cx) - (halfX - radius);
			float dy = Mathf.Abs(y - cy) - (halfY - radius);
			float ax = Mathf.Max(dx, 0f);
			float ay = Mathf.Max(dy, 0f);
			return Mathf.Sqrt(ax * ax + ay * ay) + Mathf.Min(Mathf.Max(dx, dy), 0f) - radius;
		}
	}
}
