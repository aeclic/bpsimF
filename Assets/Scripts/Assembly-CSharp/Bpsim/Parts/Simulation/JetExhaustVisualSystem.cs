using Bpsim.Parts.Gameplay;
using Bpsim.Parts;
using Unity.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Rendering;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;

namespace Bpsim.Parts.Simulation
{
	[UpdateInGroup(typeof(PresentationSystemGroup))]
	[UpdateAfter(typeof(UpdatePresentationSystemGroup))]
	[UpdateBefore(typeof(SceneRenderSystem))]
	internal partial class JetExhaustVisualSystem : SystemBase
	{
		private EntityQuery m_visuals;

		private readonly Dictionary<Entity, LocalTransform> m_initialTransforms = new Dictionary<Entity, LocalTransform>();

		protected override void OnCreate()
		{
			m_visuals = GetEntityQuery(new EntityQueryDesc
			{
				All = new[] { ComponentType.ReadOnly<JetExhaustVisual>(), ComponentType.ReadOnly<Parent>() },
				None = new[] { ComponentType.ReadOnly<Prefab>() }
			});
		}

		protected override void OnUpdate()
		{
			PartManager manager = PartManager.Instance;
			bool simulating = manager != null && manager.IsSimulating;
			float cameraScale = 1f;
			if (SceneCamera.Instance != null && SceneCamera.Instance.Camera != null && SceneCamera.Instance.Camera.orthographic)
			{
				cameraScale = Mathf.Clamp(10f / Mathf.Max(1f, SceneCamera.Instance.Camera.orthographicSize), .35f, 2.5f);
			}
			using NativeArray<Entity> visuals = m_visuals.ToEntityArray(Allocator.Temp);
			foreach (Entity visual in visuals)
			{
				if (!EntityManager.HasComponent<LocalTransform>(visual))
				{
					continue;
				}
				if (!m_initialTransforms.TryGetValue(visual, out LocalTransform initial))
				{
					initial = EntityManager.GetComponentData<LocalTransform>(visual);
					m_initialTransforms.Add(visual, initial);
				}
				Entity root = EntityManager.GetComponentData<Parent>(visual).Value;
				float thrustRatio = 0f;
				bool burning = false;
				if (simulating && EntityManager.Exists(root)
					&& PartGameplayManager.Instance?.Find(root) is JetEngineBehavior engine
					&& engine.Enabled && EntityManager.HasComponent<FuelConsumer>(root))
				{
					FuelConsumer fuel = EntityManager.GetComponentData<FuelConsumer>(root);
					burning = fuel.Requesting != 0 && fuel.Supplied > 0f;
					if (burning && fuel.Required > 1E-06f)
					{
						thrustRatio = Mathf.Clamp01(fuel.Supplied / fuel.Required);
					}
				}
				if (burning)
				{
					LocalTransform pose = initial;
					float length = cameraScale * Mathf.Lerp(.35f, 1f, thrustRatio);
					pose.Scale = initial.Scale * length;
					EntityManager.SetComponentData(visual, pose);
				}
				bool hidden = EntityManager.HasComponent<DisableRendering>(visual);
				if (burning && hidden) EntityManager.RemoveComponent<DisableRendering>(visual);
				else if (!burning && !hidden) EntityManager.AddComponent<DisableRendering>(visual);
				if (!burning && !hidden)
				{
					EntityManager.SetComponentData(visual, initial);
				}
			}
			foreach (Entity visual in new List<Entity>(m_initialTransforms.Keys))
			{
				if (!EntityManager.Exists(visual)) m_initialTransforms.Remove(visual);
			}
		}
	}
}
