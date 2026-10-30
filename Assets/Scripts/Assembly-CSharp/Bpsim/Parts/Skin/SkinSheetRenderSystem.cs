using Bpsim.Parts;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Bpsim.Parts.Skin
{
	/// <summary>
	/// 蒙皮曲面的绘制：每帧用 <c>Graphics.RenderMesh</c> 提交自己那份 625 顶点网格。
	/// </summary>
	/// <remarks>
	/// 为什么不走 <see cref="SceneRenderSystem"/>：那条批处理管线（BatchRendererGroup）
	/// 只登记了一张四边形的 <c>Mesh</c> 和 <c>Part_Texture_0</c> 图集材质，
	/// 画的是「一张 sprite 贴在一格大小的面上」；蒙皮是任意四角 + 曲边组成的自由曲面，
	/// 每个部件顶点都不同，没法当成同一种网格的实例去批处理。
	/// 所以这里走即时模式：部件数量很少（正常情况下个位数），一部件一次 draw call 完全够用，
	/// 换来的是「形状数据在组件里、网格在托管缓存里」这种干净的 ECS 分工。
	/// </remarks>
	[UpdateInGroup(typeof(PresentationSystemGroup))]
	internal partial class SkinSheetRenderSystem : SystemBase
	{
		/// <summary>稍微往镜头方向挪一点，避免和同部件的底板 sprite 在 z=0 共面闪烁。</summary>
		private const float ZOffset = -0.01f;

		/// <summary>上一帧真正提交给 <c>Graphics.RenderMesh</c> 的蒙皮数量（QA 探针）。</summary>
		internal static int LastDrawn;

		/// <summary>上一帧提交时的 <c>Time.frameCount</c>（QA 用来判断系统确实在跑）。</summary>
		internal static int LastDrawnFrame;

		protected override void OnUpdate()
		{
			EntityManager em = base.EntityManager;
			SkinSheetRegistry registry = SkinSheetRegistry.Instance;
			EntityQuery query = GetEntityQuery(ComponentType.ReadOnly<SkinSheet>(), ComponentType.ReadOnly<SkinSheetMesh>(), ComponentType.ReadOnly<LocalToWorld>());
			using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
			LastDrawn = 0;
			for (int i = 0; i < entities.Length; i++)
			{
				Entity entity = entities[i];
				SkinSheetMesh meshData = em.GetComponentData<SkinSheetMesh>(entities[i]);
				if (meshData.Handle <= 0)
				{
					continue;
				}
				Mesh mesh = registry.GetMesh(meshData.Handle);
				if (mesh == null)
				{
					continue;
				}
				SkinSheet sheet = em.GetComponentData<SkinSheet>(entity);
				Material material = registry.GetMaterial(meshData.Handle, sheet.TextureHandle, sheet.Color, sheet.TextureMode);
				if (material == null)
				{
					continue;
				}
				float4x4 world = em.GetComponentData<LocalToWorld>(entity).Value;
				float3 origin = world.c3.xyz + new float3(0f, 0f, ZOffset);
				float4x4 shifted = world;
				shifted.c3 = new float4(origin, 1f);
				Bounds localBounds = mesh.bounds;
				float3 extent = localBounds.extents;
				// 世界包围盒只用 XY 半径按变换后的尺度放大，够 culling 用且比逐顶点算便宜。
				float scaleX = math.length(world.c0.xyz);
				float scaleY = math.length(world.c1.xyz);
				float radius = math.max(extent.x * scaleX, extent.y * scaleY) + 0.05f;
				RenderParams renderParams = new RenderParams(material)
				{
					worldBounds = new Bounds(origin, new Vector3(radius * 2f, radius * 2f, 0.2f)),
					shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
					receiveShadows = false
				};
				Graphics.RenderMesh(in renderParams, mesh, 0, (Matrix4x4)shifted);
				LastDrawn++;
			}
			LastDrawnFrame = UnityEngine.Time.frameCount;
		}
	}
}
