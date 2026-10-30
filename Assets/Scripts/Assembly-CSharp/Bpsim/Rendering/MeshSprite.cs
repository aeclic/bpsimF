using System;
using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.Rendering
{
	public class MeshSprite : SpriteBase
	{
		private static VertexHelper s_vertexHelper;

		protected override void Awake()
		{
			base.Awake();
			if (s_vertexHelper == null)
			{
				s_vertexHelper = new VertexHelper();
			}
		}

		protected override void ApplySprite()
		{
			MeshFilter component = GetComponent<MeshFilter>();
			if (component == null)
			{
				throw new NullReferenceException();
			}
			if (!SpriteManager.Instance.TryGetMesh(m_id, out var mesh))
			{
				mesh = new Mesh();
				SpriteUtility.PopulateMesh(s_vertexHelper, in m_spriteRect);
				s_vertexHelper.FillMesh(mesh);
				SpriteManager.Instance.SetMesh(m_id, mesh);
			}
			component.mesh = mesh;
		}
	}
}
