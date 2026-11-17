using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Bpsim.Parts.Simulation
{
	[UpdateInGroup(typeof(PartSimulationSystemGroup))]
	internal partial class SandbagSystem : SystemBase
	{
		private EntityQuery m_query;
		internal void InitializeParts()
		{
			var parts = PartManager.Instance.PartSimulator.Unmanaged.Parts;
			for (int i = 0; i < parts.Length; i++)
			{
				Entity part = parts[i];
				if (!EntityManager.HasComponent<Sandbag>(part)) continue;
				var bag = EntityManager.GetComponentData<Sandbag>(part);
				if (!bag.Bound) { Bind(part, ref bag); EntityManager.SetComponentData(part, bag); }
			}
		}
		protected override void OnCreate() => m_query = GetEntityQuery(ComponentType.ReadWrite<Sandbag>(),
			ComponentType.ReadOnly<PartTag>(), ComponentType.ReadOnly<LocalTransform>());
		protected override void OnUpdate()
		{
			var manager = PartManager.Instance;
			if (manager == null || !manager.IsSimulating || !(World.Time.DeltaTime > 0)) return;
			Dependency.Complete();
			using var parts = m_query.ToEntityArray(Allocator.Temp);
			foreach (Entity part in parts)
			{
				if (!SuspensionRuntime.IsSimulationPart(EntityManager, part)) continue;
				var bag = EntityManager.GetComponentData<Sandbag>(part);
				if (!bag.Bound) Bind(part, ref bag);
				if (!bag.Dropped && (!EntityManager.Exists(bag.Anchor) || !EntityManager.Exists(bag.Joint)))
				{
					SuspensionRuntime.RemoveTether(bag.Joint);
					bag.Dropped = true; bag.Anchor = Entity.Null; bag.Joint = Entity.Null;
				}
				if (bag.DropRequest && !bag.Dropped)
				{
					if (EntityManager.Exists(bag.Joint) && EntityManager.Exists(bag.Anchor))
					{
						SuspensionRuntime.AddImpulse(EntityManager, bag.Anchor, new float3(0, Sandbag.DropImpulseOnAnchor, 0));
						SuspensionRuntime.AddImpulse(EntityManager, part, new float3(0, Sandbag.DropImpulseOnSelf, 0));
					}
					SuspensionRuntime.RemoveTether(bag.Joint);
					bag.Dropped = true; bag.Anchor = Entity.Null; bag.Joint = Entity.Null;
				}
				bag.DropRequest = false;
				if (EntityManager.HasComponent<SuspensionRope>(part))
				{
					var rope = EntityManager.GetComponentData<SuspensionRope>(part);
					rope.Visible = !bag.Dropped; EntityManager.SetComponentData(part, rope);
				}
				EntityManager.SetComponentData(part, bag);
			}
		}
		private void Bind(Entity part, ref Sandbag bag)
		{
			bag.Bound = true;
			// Capture the build pose BEFORE cloning or any native/structural operation.
			var pose = EntityManager.GetComponentData<LocalTransform>(part);
			SuspensionRuntime.SpawnSiblings(EntityManager, part, bag.Balloons);
			SuspensionRuntime.ApplyPhysics(EntityManager, part);
			SuspensionRuntime.SelectSingleVisualization(EntityManager, part);
			bag.Anchor = SuspensionRuntime.FindTarget(EntityManager, part, false);
			if (EntityManager.Exists(bag.Anchor))
			{
				float3 temporary = EntityManager.GetComponentData<LocalTransform>(bag.Anchor).Position - new float3(0, .5f, 0);
				float3 local = new float3(0, .5f * SuspensionRuntime.Scale(EntityManager, part).y, 0);
				// Match SpringJoint.autoConfigureConnectedAnchor after the local anchor is assigned.
				bag.ConnectedAnchor = SuspensionRuntime.ToLocal(EntityManager, bag.Anchor, temporary + math.rotate(pose.Rotation, local));
				bag.LineAnchor = SuspensionRuntime.ToLocal(EntityManager, bag.Anchor, temporary);
				bag.Joint = SuspensionRuntime.CreateTether(EntityManager, part, bag.Anchor, local, bag.ConnectedAnchor, bag.MaxDistance);
				SuspensionRuntime.Set(EntityManager, part, new SuspensionRope { Target = bag.Anchor, LocalAnchor = local * .8f,
					ConnectedAnchor = bag.LineAnchor, Width = .05f, Visible = true });
			}
			else bag.Dropped = true;
			// BPLE restores the ORIGINAL cell position plus the variant offset, not anchor-.5+offset.
			pose.Position += bag.HangOffset;
			bag.InitialPosition = pose.Position;
			SuspensionRuntime.ResetPose(EntityManager, part, pose);
		}
	}
}
