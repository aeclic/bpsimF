using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace Bpsim.Parts.Gameplay
{
	internal sealed partial class BpleLightRuntime
	{
		private readonly HashSet<Entity> m_autoFrames = new HashSet<Entity>();
		private readonly Dictionary<Entity, CollisionFilter> m_autoFilters = new Dictionary<Entity, CollisionFilter>();
		private readonly List<(Source, VoidLightBehavior)> m_controlLights = new List<(Source, VoidLightBehavior)>();
		private readonly HashSet<Entity> m_hiddenControlFrames = new HashSet<Entity>();
		private readonly List<Entity> m_removedControlFrames = new List<Entity>();

		private void UpdateControlLights(float dt, bool refreshBodies = false)
		{
			m_controlLights.Clear();
			foreach (Source source in m_sources.Values)
				if (source.Info.PartType == PartType.PointLight && source.Info.PartIndex == 6
					&& PartGameplayManager.Instance.Find(source.Part) is VoidLightBehavior behavior) m_controlLights.Add((source, behavior));
			if (m_controlLights.Count == 0 && m_autoFrames.Count == 0) return;
			if (refreshBodies)
			{
				bool needsBodies = false;
				foreach (var pair in m_controlLights)
					if (pair.Item2.Enabled && pair.Item1.EnclosedInto != Entity.Null) { needsBodies = true; break; }
				if (needsBodies) CaptureBodies();
			}
			foreach (var pair in m_controlLights)
			{
				Source source = pair.Item1;
				VoidLightBehavior behavior = pair.Item2;
				if (!behavior.Enabled || source.EnclosedInto == Entity.Null)
				{ behavior.SetDetected(false, true, dt); continue; }
				bool detected = behavior.Full, collided = false;
				float2 origin = Em.GetComponentData<LocalTransform>(source.Part).Position.xy;
				float2 down = -source.Direction;
				foreach (Body body in m_bodies.Values)
				{
					if (!Em.Exists(body.Entity)) continue;
					float2 delta = Em.GetComponentData<LocalTransform>(body.Entity).Position.xy - origin;
					float x = math.dot(down, delta), y = down.y * delta.x - down.x * delta.y;
					bool inDetect = !behavior.Full && x >= -2 && x < 6.5f && y >= -2.5f && y < 2.5f;
					bool inCollision = x >= -2.5f && x < 2.5f && y >= -2.5f && y < 2.5f;
					int component = PartGameplayUtil.ConnectedComponent(body.Entity);
					bool isPart = Em.HasComponent<PartTypeValue>(body.Entity);
					detected |= inDetect && (!isPart || component != source.Component);
					collided |= inCollision && isPart && component != source.Component;
					if (detected && collided) break;
				}
				behavior.SetDetected(detected, false, dt);
				behavior.SetCollided(collided);
			}
			m_hiddenControlFrames.Clear();
			foreach (Entity part in PartGameplayUtil.Simulator.Parts)
			{
				if (!Em.Exists(part)) continue;
				PartType type = PartVariants.Resolve(PartGameplayUtil.TypeInfo(part)).PartType;
				if (type != PartType.WoodenFrame && type != PartType.MetalFrame) continue;
				float2 position = Em.GetComponentData<LocalTransform>(part).Position.xy;
				foreach (var pair in m_controlLights)
				{
					Source source = pair.Item1;
					if (!pair.Item2.Enabled || !pair.Item2.Detected || source.Component != PartGameplayUtil.ConnectedComponent(part)) continue;
					float2 delta = position - Em.GetComponentData<LocalTransform>(source.Part).Position.xy;
					float2 down = -source.Direction;
					float x = math.dot(down, delta), y = down.y * delta.x - down.x * delta.y;
					if (x >= -2.5f && x < 2.5f && y >= -2.5f && y < 2.5f) { m_hiddenControlFrames.Add(part); break; }
				}
			}
			m_removedControlFrames.Clear();
			foreach (Entity part in m_autoFrames) if (!m_hiddenControlFrames.Contains(part)) m_removedControlFrames.Add(part);
			foreach (Entity part in m_removedControlFrames)
			{
				PartGameplayUtil.SetCollisionEnabled(part, true, m_autoFilters);
				PartGameplayUtil.RestoreAlphaFor(part, VoidLightBehavior.AlphaRestore);
				m_autoFrames.Remove(part);
			}
			foreach (Entity part in m_hiddenControlFrames)
			{
				PartGameplayUtil.SetCollisionEnabled(part, false, m_autoFilters);
				PartGameplayUtil.SetAlpha(part, .25f, VoidLightBehavior.AlphaRestore);
				m_autoFrames.Add(part);
			}
		}

		private void RestoreControlFrames()
		{
			if (PartManager.Instance?.System != null)
				foreach (Entity part in m_autoFrames)
				{
					PartGameplayUtil.SetCollisionEnabled(part, true, m_autoFilters);
					PartGameplayUtil.RestoreAlphaFor(part, VoidLightBehavior.AlphaRestore);
				}
			m_autoFrames.Clear(); m_autoFilters.Clear();
		}
	}
}
