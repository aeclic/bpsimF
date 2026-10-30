using UnityEngine;

namespace Bpsim.UI
{
	/// <summary>
	/// 蒙皮部件专用的按钮图标（现画，不依赖图集）。
	/// </summary>
	/// <remarks>
	/// <para>
	/// 选择器图标模板里的 <c>PartIcon</c> 是一个 <c>RawImage</c>，贴图是整张
	/// <c>Texture2D/Part_Rendered_Texture_0.png</c> 部件图集，靠同物体上的
	/// <see cref="Bpsim.Rendering.UISprite"/> 把自己的 <c>uvRect</c> 收到对应那一格。
	/// </para>
	/// <para>
	/// 模板上的 <c>UISprite.m_name</c> 是空的，<c>SpriteBase.Apply()</c> 遇到空名字直接
	/// return，所以 <c>uvRect</c> 一直停在默认的 (0,0,1,1)。正常部件由
	/// <see cref="PartSelector.SetRenderedIcon"/> 赋一个
	/// <c>Part_&lt;Type&gt;_&lt;Index&gt;_Rendered</c> 名字把 uvRect 收窄；蒙皮是后加的
	/// <see cref="Bpsim.Parts.PartType"/>，图集里没有这一格，名字会留空，于是按钮上直接
	/// 铺开**整张图集**。这里现画一张 64×64 的「框里绷一张皮」补上。
	/// </para>
	/// </remarks>
	internal static class SkinPartIcon
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
			Color32 frame = new Color32(206, 172, 122, 255);
			Color32 cloth = new Color32(240, 227, 203, 255);
			Color32 weave = new Color32(203, 178, 138, 255);
			Color32 handle = new Color32(146, 109, 68, 255);

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
					if (RoundBox(x, y, center, center, 27f, 27f, 11f) <= 0f)
					{
						color = frame;
						if (RoundBox(x, y, center, center, 21f, 21f, 7f) <= 0f)
						{
							color = (((x + y) & 7) == 0) ? weave : cloth;
						}
					}
					if (Dot(x, y, 12f, 12f) || Dot(x, y, Size - 13f, 12f) || Dot(x, y, 12f, Size - 13f) || Dot(x, y, Size - 13f, Size - 13f))
					{
						color = handle;
					}
					pixels[y * Size + x] = color;
				}
			}

			Texture2D texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
			{
				name = "SkinPartIcon",
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

		private static bool Dot(float x, float y, float cx, float cy)
		{
			float dx = x - cx;
			float dy = y - cy;
			return dx * dx + dy * dy <= 10f;
		}
	}
}
