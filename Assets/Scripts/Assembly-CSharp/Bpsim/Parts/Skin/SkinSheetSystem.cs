using Bpsim.Parts;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

namespace Bpsim.Parts.Skin
{
	/// <summary>
	/// 蒙皮曲面重建系统：把 <see cref="SkinSheet"/> 的参数变化变成新的 <c>Mesh</c> 和凸包碰撞体。
	/// </summary>
	/// <remarks>
	/// 这是把 BPRC 的 <c>SkinPartGeometry.Rebuild / BuildCollision</c> 拆成 ECS 的一步：
	/// 形状只存在组件里，谁改了参数就自增 <see cref="SkinSheet.Version"/>，本系统据此重建。
	/// 没有任何部件脚本在 LateUpdate 里轮询自己要不要重建。
	/// </remarks>
	[UpdateInGroup(typeof(SimulationSystemGroup))]
	internal partial class SkinSheetSystem : SystemBase
	{
		/// <summary>凸包生成参数：薄片需要一点倒角，否则退化三角形的面法线会抖动。</summary>
		private static readonly ConvexHullGenerationParameters HullParameters = new ConvexHullGenerationParameters
		{
			BevelRadius = 0.02f,
			MinimumAngle = 2f,
			SimplificationTolerance = 0.01f
		};

		/// <summary>
		/// 被替换下来的旧碰撞体必须在本系统还活着的时候才能释放，
		/// 而它们可能仍被物理世界的宽相引用，所以先攒着、统一在销毁时释放。
		/// </summary>
		private readonly System.Collections.Generic.List<BlobAssetReference<Unity.Physics.Collider>> m_retiredColliders = new System.Collections.Generic.List<BlobAssetReference<Unity.Physics.Collider>>();

		protected override void OnDestroy()
		{
			foreach (BlobAssetReference<Unity.Physics.Collider> retired in m_retiredColliders)
			{
				if (retired.IsCreated)
				{
					retired.Dispose();
				}
			}
			m_retiredColliders.Clear();
		}

		protected override void OnUpdate()
		{
			EntityManager em = base.EntityManager;
			SkinSheetRegistry registry = SkinSheetRegistry.Instance;
			registry.BeginFrame();
			EntityQuery query = GetEntityQuery(ComponentType.ReadWrite<SkinSheet>(), ComponentType.ReadWrite<SkinSheetMesh>(), ComponentType.ReadOnly<LocalTransform>());
			using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
			for (int i = 0; i < entities.Length; i++)
			{
				Entity entity = entities[i];
				SkinSheet sheet = em.GetComponentData<SkinSheet>(entity);
				SkinSheetMesh mesh = em.GetComponentData<SkinSheetMesh>(entity);
				LocalTransform transform = em.GetComponentData<LocalTransform>(entity);
				float2 scale = new float2(math.abs(transform.Scale), math.abs(transform.Scale));
				if (mesh.Handle <= 0)
				{
					mesh.Handle = registry.Allocate();
					mesh.BuiltVersion = int.MinValue;
				}
				bool scaleChanged = math.lengthsq(mesh.BuiltScale - scale) > 1E-10f;
				if (mesh.BuiltVersion != sheet.Version || scaleChanged)
				{
					Rebuild(in sheet, in mesh, scale, registry, em, entity);
					mesh.BuiltVersion = sheet.Version;
					mesh.BuiltScale = scale;
					em.SetComponentData(entity, mesh);
				}
				// 点名：这一帧还活着，句柄不会被 SkinSheetRegistry.EndFrame 回收。
				registry.Touch(mesh.Handle);
			}
			registry.EndFrame();
		}

		private void Rebuild(in SkinSheet sheet, in SkinSheetMesh meshData, float2 scale, SkinSheetRegistry registry, EntityManager em, Entity entity)
		{
			using NativeList<float3> positions = new NativeList<float3>(SkinSheetMeshBuilder.VisualVertexCount, Allocator.Temp);
			using NativeList<float2> uvs = new NativeList<float2>(SkinSheetMeshBuilder.VisualVertexCount, Allocator.Temp);
			using NativeList<float4> colors = new NativeList<float4>(SkinSheetMeshBuilder.VisualVertexCount, Allocator.Temp);
			using NativeList<int> indices = new NativeList<int>((SkinSheetMeshBuilder.VisualSide - 1) * (SkinSheetMeshBuilder.VisualSide - 1) * 6, Allocator.Temp);
			float aspect = 1f;
			Texture2D texture = registry.GetTexture(sheet.TextureHandle);
			if (texture != null && texture.width > 0)
			{
				aspect = (float)texture.height / (float)texture.width;
			}
			SkinSheetMeshBuilder.BuildVisual(in sheet, scale, aspect, positions, uvs, colors, indices, out float2 boundsMin, out float2 boundsMax);
			registry.EnsureMesh(meshData.Handle, sheet.Version, positions.AsArray(), uvs.AsArray(), colors.AsArray(), indices.AsArray(), boundsMin, boundsMax);
			RebuildCollider(in sheet, em, entity);
		}

		/// <summary>
		/// 重建凸包碰撞体。
		/// </summary>
		/// <remarks>
		/// 只在非模拟状态下改：Unity Physics 的碰撞体是烘焙进 <c>PhysicsWorld</c> 的，
		/// 飞行途中换掉刚体的 <c>PhysicsCollider</c> 需要把刚体摘出世界再加回去，
		/// 而建造阶段形状本来就该自由编辑、模拟阶段又拖不动角点，所以直接跳过。
		/// </remarks>
		private void RebuildCollider(in SkinSheet sheet, EntityManager em, Entity entity)
		{
			PartManager manager = PartManager.Instance;
			if (manager != null && manager.IsSimulating)
			{
				return;
			}
			if (!em.HasComponent<PhysicsCollider>(entity))
			{
				return;
			}
			using NativeList<float3> points = new NativeList<float3>(SkinSheetMeshBuilder.CollisionSide * SkinSheetMeshBuilder.CollisionSide * 2, Allocator.Temp);
			SkinSheetMeshBuilder.BuildConvexPoints(in sheet, points);
			BlobAssetReference<Unity.Physics.Collider> hull = ConvexCollider.Create(points.AsArray(), HullParameters, CollisionFilter.Default);
			if (!hull.IsCreated)
			{
				return;
			}
			PhysicsCollider previous = em.GetComponentData<PhysicsCollider>(entity);
			em.SetComponentData(entity, new PhysicsCollider
			{
				Value = hull
			});
			if (previous.Value.IsCreated)
			{
				m_retiredColliders.Add(previous.Value);
			}
		}
	}
}
