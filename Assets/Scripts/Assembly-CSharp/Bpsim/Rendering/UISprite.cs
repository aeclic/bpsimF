using System;
using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.Rendering
{
	public class UISprite : SpriteBase
	{
		protected override void ApplySprite()
		{
			Graphic component = GetComponent<Graphic>();
			if (component == null)
			{
				throw new NullReferenceException();
			}
			if (component is RawImage rawImage)
			{
				SpriteUtility.PopulateRawImage(rawImage, in m_spriteRect);
				// 图标模板的 RawImage 贴的是 Part_Rendered_Texture_0，uvRect 是归一化的，
				// 所以换图集只需要换 texture；原版精灵（图集序号 0）不碰它。
				SpriteManager manager = SpriteManager.Instance;
				if (manager == null)
				{
					return;
				}
				int atlasIndex = PartAtlasRegistry.IndexOf(manager.GetSpriteAtlas(m_id));
				if (atlasIndex > 0)
				{
					Texture2D texture = PartAtlasRegistry.GetTexture(atlasIndex);
					if (texture != null)
					{
						rawImage.texture = texture;
					}
				}
			}
		}
	}
}
