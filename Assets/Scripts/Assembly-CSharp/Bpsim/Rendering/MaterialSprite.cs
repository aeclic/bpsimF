using System;
using UnityEngine;

namespace Bpsim.Rendering
{
	[ExecuteAlways]
	public class MaterialSprite : SpriteBase
	{
		private static int s_vertexID = Shader.PropertyToID("_Rect");

		private static int s_uvID = Shader.PropertyToID("_UVRect");

		private static MaterialPropertyBlock s_props;

		protected override void ApplySprite()
		{
			Renderer component = GetComponent<Renderer>();
			if (component == null)
			{
				throw new NullReferenceException();
			}
			if (s_props == null)
			{
				s_props = new MaterialPropertyBlock();
			}
			component.GetPropertyBlock(s_props);
			SpriteUtility.PopulatePropertyBlock(s_props, s_vertexID, s_uvID, in m_spriteRect);
			component.SetPropertyBlock(s_props);
		}
	}
}
