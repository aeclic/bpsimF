using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.Rendering
{
	public static class SpriteUtility
	{
		public static void PopulateMesh(VertexHelper vertexHelper, in SpriteRect value)
		{
			float vertexX = value.VertexX;
			float vertexY = value.VertexY;
			float num = 0.5f * value.VertexW;
			float num2 = 0.5f * value.VertexH;
			float u = value.U;
			float v = value.V;
			float w = value.W;
			float h = value.H;
			Color32 color = new Color32(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
			vertexHelper.Clear();
			vertexHelper.AddVert(new Vector3(vertexX - num, vertexY - num2), color, new Vector4(u, v));
			vertexHelper.AddVert(new Vector3(vertexX - num, vertexY + num2), color, new Vector4(u, v + h));
			vertexHelper.AddVert(new Vector3(vertexX + num, vertexY + num2), color, new Vector4(u + w, v + h));
			vertexHelper.AddVert(new Vector3(vertexX + num, vertexY - num2), color, new Vector4(u + w, v));
			vertexHelper.AddTriangle(0, 1, 2);
			vertexHelper.AddTriangle(2, 3, 0);
		}

		public static void PopulatePropertyBlock(MaterialPropertyBlock props, int vertexID, int uvID, in SpriteRect value)
		{
			props.SetVector(vertexID, new Vector4(value.VertexX, value.VertexY, value.VertexW, value.VertexH));
			props.SetVector(uvID, new Vector4(value.U, value.V, value.W, value.H));
		}

		public static void PopulateRawImage(RawImage rawImage, in SpriteRect value)
		{
			rawImage.uvRect = new Rect(value.U, value.V, value.W, value.H);
			rawImage.rectTransform.sizeDelta = new Vector2(value.VertexW, value.VertexH);
		}
	}
}
