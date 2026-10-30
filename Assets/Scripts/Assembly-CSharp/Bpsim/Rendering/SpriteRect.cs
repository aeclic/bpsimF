using System;

namespace Bpsim.Rendering
{
	public readonly struct SpriteRect : IEquatable<SpriteRect>
	{
		public readonly float VertexX;

		public readonly float VertexY;

		public readonly float VertexW;

		public readonly float VertexH;

		public readonly float U;

		public readonly float V;

		public readonly float W;

		public readonly float H;

		public SpriteRect(float vertexX, float vertexY, float vertexW, float vertexH, float u, float v, float w, float h)
		{
			VertexX = vertexX;
			VertexY = vertexY;
			VertexW = vertexW;
			VertexH = vertexH;
			U = u;
			V = v;
			W = w;
			H = h;
		}

		public override bool Equals(object obj)
		{
			if (obj is SpriteRect other)
			{
				return Equals(other);
			}
			return false;
		}

		public bool Equals(SpriteRect other)
		{
			if (VertexX == other.VertexX && VertexY == other.VertexY && VertexW == other.VertexW && VertexH == other.VertexH && U == other.U && V == other.V && W == other.W)
			{
				return H == other.H;
			}
			return false;
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(VertexX, VertexY, VertexW, VertexH, U, V, W, H);
		}
	}
}
