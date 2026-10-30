using Unity.Mathematics;

namespace Bpsim.Parts.Skin
{
	/// <summary>
	/// 蒙皮曲面的纯函数数学核心（Coons 曲面 + 两条 Catmull-Rom 边）。
	/// </summary>
	/// <remarks>
	/// 逐行对齐 BPRC <c>SkinPartGeometry.Coons/EdgePoint/Catmull</c>：
	/// 四角之间不是直线插值，而是「每边过可拖动中点、用 Besiege 的两段
	/// Catmull-Rom 拼出来」——中点被拖走时两边各自用端点外延的控制点
	/// （<c>before</c>/<c>after</c>，由端点与中点构成的旋转四元数算出）接上，
	/// 再把两段曲线用 Coons 双线性调和成曲面。
	/// </remarks>
	/// <remarks>
	/// 全部是 <c>static</c> + <c>in</c> 参数、不碰任何 Unity 对象，所以既能给
	/// 主线程的重建路径调用，也能原样丢进 Burst job。没有 <c>Vector2</c> 运算，
	/// 统一用 <see cref="float2"/> 以便进 job。
	/// </remarks>
	public static class SkinSheetGeometry
	{
		/// <summary>渲染网格每条边的细分段数（与 BPRC 一致，25 × 25 = 625 顶点）。</summary>
		public const int EdgeSegments = 24;

		/// <summary>凸碰撞网格每条边的细分段数（4 × 4 = 25 点，够贴形状又远低于凸包上限）。</summary>
		public const int CollisionSegments = 4;

		/// <summary>碰撞网格的厚度（前后各一半），让薄片有体积、能被物理稳定地碰到。</summary>
		public const float Depth = 0.04f;

		/// <summary>默认一格宽的方形蒙皮。</summary>
		public static float2 DefaultCorner(int index)
		{
			return index switch
			{
				0 => new float2(-0.5f, -0.5f), 
				1 => new float2(0.5f, -0.5f), 
				2 => new float2(0.5f, 0.5f), 
				_ => new float2(-0.5f, 0.5f), 
			};
		}

		/// <summary>把组件里的四个角按 BL/BR/TR/TL 顺序取出来。</summary>
		public static void ReadCorners(in SkinSheet sheet, out float2 bl, out float2 br, out float2 tr, out float2 tl)
		{
			bl = sheet.BottomLeft;
			br = sheet.BottomRight;
			tr = sheet.TopRight;
			tl = sheet.TopLeft;
		}

		/// <summary>按索引取角（0 = BL，1 = BR，2 = TR，3 = TL）。</summary>
		public static float2 Corner(float2 bl, float2 br, float2 tr, float2 tl, int index)
		{
			return index switch
			{
				0 => bl, 
				1 => br, 
				2 => tr, 
				_ => tl, 
			};
		}

		/// <summary>
		/// 第 <paramref name="edge"/> 条边的两个端点。
		/// 0 = 下（BL→BR）、1 = 上（TL→TR）、2 = 右（BR→TR）、3 = 左（BL→TL）。
		/// </summary>
		public static void EdgeEnds(float2 bl, float2 br, float2 tr, float2 tl, int edge, out float2 a, out float2 b)
		{
			a = ((edge == 0 || edge == 3) ? bl : ((edge == 1) ? tl : br));
			b = ((edge == 0) ? br : ((edge == 1 || edge == 2) ? tr : tl));
		}

		/// <summary>
		/// 边上的点（<paramref name="t"/> ∈ [0,1]）。
		/// 中点是「端点中点 + 法向 * Curve * 0.5 + 切向 * Along」。
		/// </summary>
		public static float2 EdgePoint(float2 bl, float2 br, float2 tr, float2 tl, in float4 curve, in float4 along, int edge, float t)
		{
			EdgeEnds(bl, br, tr, tl, edge, out var a, out var b);
			float2 d = math.normalizesafe(b - a, new float2(1f, 0f));
			float2 normal = ((edge == 0 || edge == 2) ? new float2(d.y, 0f - d.x) : new float2(0f - d.y, d.x));
			float2 mid = (a + b) * 0.5f + normal * (curve[edge] * 0.5f) + d * along[edge];
			float2 midOffset = mid - (a + b) * 0.5f;
			if (math.lengthsq(midOffset) < 1E-08f)
			{
				return math.lerp(a, b, t);
			}
			float2 va = a - mid;
			float2 vb = b - mid;
			float dot = math.dot(math.normalizesafe(-vb, new float2(1f, 0f)), math.normalizesafe(va, new float2(1f, 0f)));
			float angle = math.acos(math.clamp(dot, -1f, 1f));
			float cross = (0f - vb.x) * va.y - (0f - vb.y) * va.x;
			// Besiege 在拼端点外延的四元数之前会把 dot 夹到 45°，这里保持一致。
			if (angle > math.PI * 0.25f)
			{
				dot = math.cos(math.PI * 0.25f);
			}
			float2 q = math.normalize(new float2(cross / math.max(math.length(va) * math.length(vb), 1E-08f), 1f + dot));
			float2 before = a + Rotate(va, q);
			float2 after = b + RotateInverse(vb, q);
			return ((t <= 0.5f) ? Catmull(before, a, mid, b, t * 2f) : Catmull(a, mid, b, after, t * 2f - 1f));
		}

		/// <summary>
		/// Coons 曲面：两条对边线性插值相加后减掉双线性项，保证四条边都被精确穿过。
		/// </summary>
		public static float2 Coons(in SkinSheet sheet, float u, float v)
		{
			ReadCorners(in sheet, out var bl, out var br, out var tr, out var tl);
			return Coons(bl, br, tr, tl, in sheet.Curve, in sheet.Along, u, v);
		}

		public static float2 Coons(float2 bl, float2 br, float2 tr, float2 tl, in float4 curve, in float4 along, float u, float v)
		{
			float2 bottom = EdgePoint(bl, br, tr, tl, in curve, in along, 0, u);
			float2 top = EdgePoint(bl, br, tr, tl, in curve, in along, 1, u);
			float2 left = EdgePoint(bl, br, tr, tl, in curve, in along, 3, v);
			float2 right = EdgePoint(bl, br, tr, tl, in curve, in along, 2, v);
			float2 bilinear = (1f - u) * (1f - v) * bl + u * (1f - v) * br + u * v * tr + (1f - u) * v * tl;
			return math.lerp(bottom, top, v) + math.lerp(left, right, u) - bilinear;
		}

		/// <summary>均匀 Catmull-Rom 段（BPRC 用 0.5 系数的标准形式）。</summary>
		public static float2 Catmull(float2 a, float2 b, float2 c, float2 d, float t)
		{
			float t2 = t * t;
			float t3 = t2 * t;
			return 0.5f * ((-a + 3f * b - 3f * c + d) * t3 + (2f * a - 5f * b + 4f * c - d) * t2 + (0f - a + c) * t + 2f * b);
		}

		/// <summary>
		/// 用四元数 <c>(0, 0, z, w)</c> 旋转二维向量。
		/// 单位四元数绕 Z 轴转 θ 时 <c>sinθ = 2wz</c>、<c>cosθ = 1 - 2z²</c>。
		/// </summary>
		public static float2 Rotate(float2 value, float2 q)
		{
			float sin = 2f * q.y * q.x;
			float cos = 1f - 2f * q.x * q.x;
			return new float2(cos * value.x - sin * value.y, sin * value.x + cos * value.y);
		}

		/// <summary><see cref="Rotate"/> 的逆旋转。</summary>
		public static float2 RotateInverse(float2 value, float2 q)
		{
			float sin = 2f * q.y * q.x;
			float cos = 1f - 2f * q.x * q.x;
			return new float2(cos * value.x + sin * value.y, 0f - sin * value.x + cos * value.y);
		}

		/// <summary>
		/// 面积/朝向符号：> 0 表示四角按 BL→BR→TR→TL 是逆时针（正面朝 +Z）。
		/// 渲染时用它决定三角面的绕序，拖成翻转的「8 字」也不会变成背面剔除。
		/// </summary>
		public static float SignedArea(float2 bl, float2 br, float2 tr, float2 tl)
		{
			return 0.5f * ((br.x - bl.x) * (tr.y - bl.y) - (tr.x - bl.x) * (br.y - bl.y) + (tr.x - bl.x) * (tl.y - bl.y) - (tl.x - bl.x) * (tr.y - bl.y));
		}
	}
}
