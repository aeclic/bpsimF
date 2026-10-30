using System.Collections.Generic;
using Bpsim.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

namespace Bpsim.Parts
{
	/// <summary>Shared convex hulls; only changed parts enter the update query. Rendering stays in BRG.</summary>
	[UpdateInGroup(typeof(InitializationSystemGroup))]
	public partial class FrameShapeSystem : SystemBase
	{
		private EntityQuery m_dirty;
		private EntityQuery m_all;
		private bool m_enabled;
		private readonly Dictionary<(int, float3, float3, CollisionFilter, float, float), BlobAssetReference<Collider>> m_hulls = new();
		public static bool FeatureEnabled { get; private set; }

		public static void SetEnabled(bool enabled)
		{
			if (FeatureEnabled == enabled) return;
			// Changing topology in a running PhysicsWorld also requires rebuilding mass and joints.
			if (PartManager.Instance != null && PartManager.Instance.IsSimulating)
				PartManager.Instance.EndSimulation();
			FeatureEnabled = enabled;
			var world = World.DefaultGameObjectInjectionWorld;
			if (world != null && world.IsCreated) world.GetOrCreateSystemManaged<FrameShapeSystem>().ApplyPending();
		}

		public static void SetShape(EntityManager em, Entity entity, int shape)
		{
			if (!FeatureEnabled || !em.HasComponent<FrameShape>(entity)) return;
			if (PartManager.Instance != null && PartManager.Instance.IsSimulating) return;
			em.SetComponentData(entity, new FrameShape { Value = FrameShape.Clamp(shape) });
			em.SetComponentEnabled<FrameShapeDirty>(entity, true);
			// A simulation may be started by the very next UI callback, before the next system tick.
			em.World.GetOrCreateSystemManaged<FrameShapeSystem>().ApplyPending();
		}

		protected override void OnCreate()
		{
			m_all = GetEntityQuery(ComponentType.ReadOnly<FrameShape>(), ComponentType.ReadOnly<FrameShapeOriginalCollider>());
			m_dirty = GetEntityQuery(ComponentType.ReadOnly<FrameShape>(), ComponentType.ReadOnly<FrameShapeOriginalCollider>(), ComponentType.ReadWrite<FrameShapeDirty>());
		}
		protected override void OnUpdate() => ApplyPending();

		public void ApplyPending()
		{
			var em = EntityManager;
			if (m_enabled != FeatureEnabled)
			{
				em.CompleteAllTrackedJobs();
				using var all = m_all.ToEntityArray(Allocator.Temp);
				foreach (var entity in all) em.SetComponentEnabled<FrameShapeDirty>(entity, true);
				m_enabled = FeatureEnabled;
			}
			if (m_dirty.IsEmpty) return;
			em.CompleteAllTrackedJobs();
			using var entities = m_dirty.ToEntityArray(Allocator.Temp);
			foreach (var entity in entities)
			{
				int shape = FeatureEnabled ? FrameShape.Clamp(em.GetComponentData<FrameShape>(entity).Value) : 0;
				var original = em.GetComponentData<FrameShapeOriginalCollider>(entity).Value;
				var collider = original;
				if (shape != 0 && original.IsCreated)
				{
					var bounds = original.Value.CalculateAabb();
					var key = (shape, bounds.Min, bounds.Max, original.Value.GetCollisionFilter(), original.Value.GetFriction(), original.Value.GetRestitution());
					if (!m_hulls.TryGetValue(key, out collider))
					{
						collider = BuildHull(shape, original);
						m_hulls.Add(key, collider);
					}
				}
				em.SetComponentData(entity, new PhysicsCollider { Value = collider });
				var linked = em.GetBuffer<LinkedEntityGroup>(entity);
				for (int i = 0; i < linked.Length; i++)
				{
					var child = linked[i].Value;
					if (!em.HasComponent<BlendFactor>(child)) continue;
					var blend = em.GetComponentData<BlendFactor>(child);
					// z = shape; w = glass fill. x/y retain the existing tint/texture weights.
					blend.Value.z = shape;
					blend.Value.w = child == entity ? 0 : 1;
					em.SetComponentData(child, blend);
				}
				em.SetComponentEnabled<FrameShapeDirty>(entity, false);
			}
		}

		public static BlobAssetReference<Collider> BuildHull(int shape, BlobAssetReference<Collider> original)
			=> BuildHull(shape, original, new float2(1f));

		public static BlobAssetReference<Collider> BuildHull(int shape, BlobAssetReference<Collider> original, float2 scale)
		{
			var aabb = original.Value.CalculateAabb();
			float3 center = aabb.Center;
			float3 half = aabb.Extents * .5f;
			using var points = new NativeList<float3>(70, Allocator.Temp);
			foreach (var p in Outline(shape))
			{
				points.Add(new float3((center.xy + p * half.xy) * scale, center.z - half.z));
				points.Add(new float3((center.xy + p * half.xy) * scale, center.z + half.z));
			}
			var material = Material.Default;
			material.Friction = original.Value.GetFriction();
			material.Restitution = original.Value.GetRestitution();
			return ConvexCollider.Create(points.AsArray(), new ConvexHullGenerationParameters
			{
				BevelRadius = 0, MinimumAngle = 0, SimplificationTolerance = 0
			}, original.Value.GetCollisionFilter(), material);
		}

		public static IEnumerable<float2> Outline(int shape)
		{
			if (shape == 1)
			{
				yield return new float2(-1, -1); yield return new float2(1, 1); yield return new float2(-1, 1);
				yield break;
			}
			int segments = shape == 2 ? 32 : 64;
			for (int i = 0; i < (shape == 2 ? segments + 1 : segments); i++)
			{
				float angle = (shape == 2 ? math.PI : 2 * math.PI) * i / segments;
				yield return new float2(math.cos(angle), math.sin(angle) + (shape == 2 ? -1 : 0));
			}
		}

		protected override void OnDestroy()
		{
			EntityManager.CompleteAllTrackedJobs();
			foreach (var hull in m_hulls.Values) if (hull.IsCreated) hull.Dispose();
			m_hulls.Clear();
			FeatureEnabled = false;
		}
	}
}
