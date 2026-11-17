using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Extensions;
using Unity.Transforms;

namespace Bpsim.Parts.Simulation
{
	[UpdateInGroup(typeof(PartSimulationSystemGroup))]
	internal partial class BellowsSystem : SystemBase
	{
		private EntityQuery m_query;
		protected override void OnCreate()
		{
			m_query = GetEntityQuery(ComponentType.ReadWrite<Bellows>(), ComponentType.ReadOnly<PartTag>(),
				ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadWrite<PhysicsVelocity>(),
				ComponentType.ReadOnly<PhysicsMass>());
		}
		protected override void OnUpdate()
		{
			PartManager manager = PartManager.Instance;
			if (manager == null || !manager.IsSimulating) return;
			float dt = World.Time.DeltaTime;
			if (!(dt > 0f)) return;
			Dependency.Complete();
			using var parts = m_query.ToEntityArray(Allocator.Temp);
			bool hasAlien = false;
			for (int i = 0; i < parts.Length; i++)
				hasAlien |= EntityManager.GetComponentData<Bellows>(parts[i]).Alien;
			foreach (Entity part in parts)
			{
				if (!SuspensionRuntime.IsSimulationPart(EntityManager, part)) continue;
				Bellows state = EntityManager.GetComponentData<Bellows>(part);
				if (!state.Initialized)
				{
					state.Initialized = true;
					state.OriginalVisualScale = EntityManager.HasComponent<PostTransformScale>(part)
						? EntityManager.GetComponentData<PostTransformScale>(part).Value : float3x3.identity;
				}
				if (state.TriggerRequest)
				{
					state.TriggerRequest = false;
					if (!state.Enabled && Connected(manager, part))
					{
						state.Enabled = true;
						state.Elapsed = 0f;
						state.DeliveredImpulse = 0f;
						state.Cycles++;
					}
				}
				if (state.Enabled)
				{
					float impulse = BellowsMath.Impulse(state.Elapsed, state.Elapsed + dt, state.Force);
					if (impulse > 0f)
					{
						LocalTransform pose = EntityManager.GetComponentData<LocalTransform>(part);
						float3 up = math.rotate(pose.Rotation, new float3(0, 1, 0));
						PhysicsVelocity velocity = EntityManager.GetComponentData<PhysicsVelocity>(part);
						velocity.ApplyImpulse(EntityManager.GetComponentData<PhysicsMass>(part),
							pose.Position, pose.Rotation, up * impulse, pose.Position + up * .5f);
						EntityManager.SetComponentData(part, velocity);
						state.DeliveredImpulse += impulse;
					}
					state.Elapsed += dt;
					// Own readiness uses this bellows' cooldown; visual speed follows the
					// scene-wide alien flag, as the APK compression helper does.
					state.Enabled = state.Elapsed < BellowsMath.Cooldown(state.Alien);
					state.Compression = state.Enabled ? BellowsMath.Compression(state.Elapsed, hasAlien) : 1f;
				}
				else state.Compression = 1f;
				if (EntityManager.HasComponent<PostTransformScale>(part))
					EntityManager.SetComponentData(part, new PostTransformScale
					{
						Value = math.mul(state.OriginalVisualScale, float3x3.Scale(new float3(1f, state.Compression, 1f)))
					});
				EntityManager.SetComponentData(part, state);
			}
		}
		private bool Connected(PartManager manager, Entity part)
		{
			if (manager.PartSimulator == null || !EntityManager.HasComponent<PartConnectedComponent>(part)) return false;
			int index = EntityManager.GetComponentData<PartConnectedComponent>(part).Index;
			var groups = manager.PartSimulator.Unmanaged.ConnectedComponents;
			return index >= 0 && index < groups.Length && groups[index].PartCount > 1;
		}
	}
}
