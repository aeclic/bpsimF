using System;
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
			}
		}
	}
}
