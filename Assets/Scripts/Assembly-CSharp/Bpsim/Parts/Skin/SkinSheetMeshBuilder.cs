using Unity.Collections;
using Unity.Mathematics;

namespace Bpsim.Parts.Skin
{
	/// <summary>
	/// 把 <see cref="SkinSheet"/> 采样成可以给 Unity 的 <c>Mesh</c> 用的顶点/索引数组。
	/// </summary>
	/// <remarks>
	/// 与 BPRC 一致：视觉曲面是单面的 25×25 网格（<see cref="SkinSheetGeometry.EdgeSegments"/> = 24 段），
	/// 碰撞用另一套 5×5 的低细分（<see cref="SkinSheetGeometry.CollisionSegments"/> = 4），
	/// 前后各偏移 <see cref="SkinSheetGeometry.Depth"/> 的一半，形成一片有厚度的薄板。
	/// 视觉和碰撞分开是有意的：PhysX/Jolt 的凸包顶点数上限与视觉精度无关，
	/// 而拖曲线时真正需要跟手的是看到的那张面。
	/// </remarks>
	public static class SkinSheetMeshBuilder
	{
		/// <summary>视觉网格的边长点数（25）。</summary>
		public const int VisualSide = SkinSheetGeometry.EdgeSegments + 1;

		/// <summary>视觉网格的顶点数（625）。</summary>
		public const int VisualVertexCount = VisualSide * VisualSide;

		/// <summary>碰撞网格的边长点数（5）。</summary>
		public const int CollisionSide = SkinSheetGeometry.CollisionSegments + 1;

		/// <summary>
		/// 生成视觉曲面。顶点色写 <see cref="SkinSheet.Color"/>，这样一张贴图材质可以被
		/// 所有蒙皮共用，各自染色走顶点色（着色器里 <c>tex * color * _Color</c>）。
		/// </summary>
		/// <param name="pixelAspect">贴图 高/宽，平铺模式下用来抵消非方形贴图的拉伸。</param>
		public static void BuildVisual(in SkinSheet sheet, float2 worldScale, float pixelAspect, NativeList<float3> positions, NativeList<float2> uvs, NativeList<float4> colors, NativeList<int> indices, out float2 boundsMin, out float2 boundsMax)
		{
			SkinSheetGeometry.ReadCorners(in sheet, out var bl, out var br, out var tr, out var tl);
			float aspect = math.max(1E-06f, pixelAspect);
			float tileX = math.max(0.01f, sheet.TileX);
			float tileY = math.max(0.01f, sheet.TileY);
			bool tiled = sheet.TextureMode == 1;
			// 图集裁剪：整张图是 (0,0,1,1)，从图集里取一块时只把 UV 压进那一块。
			float4 uvRect = ClampUVRect(sheet.UVRect);
			// 固定的板材本地原点：拖动角点只改变覆盖范围，绝不改变一格贴图的大小。
			float2 origin = new float2(-0.5f, -0.5f);
			boundsMin = new float2(float.MaxValue, float.MaxValue);
			boundsMax = new float2(float.MinValue, float.MinValue);
			for (int y = 0; y < VisualSide; y++)
			{
				for (int x = 0; x < VisualSide; x++)
				{
					float u = (float)x / (float)(VisualSide - 1);
					float v = (float)y / (float)(VisualSide - 1);
					float2 p = SkinSheetGeometry.Coons(bl, br, tr, tl, in sheet.Curve, in sheet.Along, u, v);
					positions.Add(new float3(p.x, p.y, 0f));
					float2 local = tiled ? new float2((p.x - origin.x) * worldScale.x * tileX, (p.y - origin.y) * worldScale.y * tileY / aspect) : new float2(u, v);
					uvs.Add(new float2(uvRect.x + local.x * uvRect.z, uvRect.y + local.y * uvRect.w));
					colors.Add(sheet.Color);
					boundsMin = math.min(boundsMin, p);
					boundsMax = math.max(boundsMax, p);
				}
			}
			for (int y = 0; y < VisualSide - 1; y++)
			{
				for (int x = 0; x < VisualSide - 1; x++)
				{
					int a = y * VisualSide + x;
					int b = a + 1;
					int d = (y + 1) * VisualSide + x;
					int e = d + 1;
					indices.Add(a);
					indices.Add(d);
					indices.Add(b);
					indices.Add(b);
					indices.Add(d);
					indices.Add(e);
				}
			}
		}

		/// <summary>
		/// 把 <see cref="SkinSheet.UVRect" /> 钳到合法的 UV 范围内（0..1），并保证宽高非零。
		/// </summary>
		public static float4 ClampUVRect(float4 rect)
		{
			const float MinSize = 0.001f;
			float x = math.clamp(rect.x, 0f, 1f - MinSize);
			float y = math.clamp(rect.y, 0f, 1f - MinSize);
			float w = math.clamp(rect.z, MinSize, 1f - x);
			float h = math.clamp(rect.w, MinSize, 1f - y);
			return new float4(x, y, w, h);
		}

		/// <summary>
		/// 生成凸包用的点云：5×5 的前后两层。
		/// </summary>
		public static void BuildConvexPoints(in SkinSheet sheet, NativeList<float3> points)
		{
			SkinSheetGeometry.ReadCorners(in sheet, out var bl, out var br, out var tr, out var tl);
			float half = SkinSheetGeometry.Depth * 0.5f;
			for (int y = 0; y < CollisionSide; y++)
			{
				for (int x = 0; x < CollisionSide; x++)
				{
					float u = (float)x / (float)(CollisionSide - 1);
					float v = (float)y / (float)(CollisionSide - 1);
					float2 p = SkinSheetGeometry.Coons(bl, br, tr, tl, in sheet.Curve, in sheet.Along, u, v);
					points.Add(new float3(p.x, p.y, 0f - half));
					points.Add(new float3(p.x, p.y, half));
				}
			}
		}
	}
}
