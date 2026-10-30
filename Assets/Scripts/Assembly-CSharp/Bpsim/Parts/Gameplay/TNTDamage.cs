using System.Collections.Generic;
using Bpsim.Parts.Simulation;
using Bpsim.UI;
using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using Unity.Physics.Extensions;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Bpsim.Parts.Gameplay
{
	/// <summary>Mega children are velocity mirrors; consume their blast impulses before physics can overwrite them.</summary>
	internal static class TNTImpulse
	{
		private static readonly Dictionary<Entity, float3> Pending = new Dictionary<Entity, float3>();
		internal static void Clear() => Pending.Clear();
		internal static void Apply(Entity part, float3 impulse)
		{
			var em = PartGameplayUtil.Em;
			if (!em.HasComponent<PhysicsMass>(part) || math.lengthsq(impulse) == 0f) return;
			if (MegaStructure.Active && em.HasComponent<MegaPartOffset>(part))
			{
				Pending.TryGetValue(part, out float3 previous);
				Pending[part] = previous + impulse;
			}
			else PartGameplayUtil.AddVelocity(part, impulse * em.GetComponentData<PhysicsMass>(part).InverseMass);
		}
		internal static void Flush()
		{
			if (Pending.Count == 0) return;
			var em = PartGameplayUtil.Em;
			em.CompleteAllTrackedJobs();
			foreach (var entry in Pending)
			{
				if (!em.Exists(entry.Key)) continue;
				if (MegaStructure.Active && em.HasComponent<MegaPartOffset>(entry.Key) && em.Exists(MegaStructure.Body))
				{
					Entity body = MegaStructure.Body;
					var transform = em.GetComponentData<LocalTransform>(body);
					var velocity = em.GetComponentData<PhysicsVelocity>(body);
					velocity.ApplyImpulse(em.GetComponentData<PhysicsMass>(body), transform.Position, transform.Rotation,
						entry.Value, em.GetComponentData<LocalTransform>(entry.Key).Position);
					em.SetComponentData(body, velocity);
				}
				else if (em.HasComponent<PhysicsMass>(entry.Key))
					PartGameplayUtil.AddVelocity(entry.Key, entry.Value * em.GetComponentData<PhysicsMass>(entry.Key).InverseMass);
			}
			Pending.Clear();
		}
	}

	/// <summary>Batch structural changes after behavior iteration, never inside a simulator Parts enumeration.</summary>
	internal static class TNTDamage
	{
		private static readonly HashSet<Entity> Retired = new HashSet<Entity>();
		private static readonly HashSet<Entity> Detached = new HashSet<Entity>();
		private static readonly HashSet<Entity> BrokenJoints = new HashSet<Entity>();
		private static bool TopologyDirty;
		internal static void RefreshTopology() => TopologyDirty = true;
		internal static void BreakJoint(Entity joint) { if (joint != Entity.Null) BrokenJoints.Add(joint); }
		public static void Retire(Entity part) { Retired.Add(part); Detached.Add(part); }
		public static void Detach(Entity part) => Detached.Add(part);
		public static void Clear() { Retired.Clear(); Detached.Clear(); BrokenJoints.Clear(); TopologyDirty = false; }

		public static int BurnJoints(Entity part, float probability)
		{
			EntityManager em = PartGameplayUtil.Em;
			var joints = PartGameplayUtil.Simulator.Joints;
			int count = 0;
			for (int i = 0; i < joints.Length; i++)
			{
				Entity joint = joints[i];
				if (!em.Exists(joint) || BrokenJoints.Contains(joint)) continue;
				var pair = em.GetComponentData<PhysicsConstrainedBodyPair>(joint);
				if ((pair.EntityA == part || pair.EntityB == part) && UnityEngine.Random.value < probability)
				{ BrokenJoints.Add(joint); count++; }
			}
			if (count > 0) MegaStructure.RequestDetach(part);
			return count;
		}

		public static void Flush()
		{
			if (Retired.Count == 0 && Detached.Count == 0 && BrokenJoints.Count == 0 && !TopologyDirty) return;
			PartManager manager = PartManager.Instance;
			EntityManager em = PartGameplayUtil.Em;
			em.CompleteAllTrackedJobs();
			ref PartSimulatorUnmanaged simulator = ref PartGameplayUtil.Simulator;
			foreach (Entity part in Detached) MegaStructure.RequestDetach(part);
			// Remove the charge from the mega compound before destroying its child entity.
			MegaStructure.FlushDetachQueue(em);
			foreach (Entity part in Detached)
				if (em.Exists(part) && em.HasComponent<TNTNoCollisionTag>(part)) TNTCollisionSetup.DisableCollision(part);
			using (var query = em.CreateEntityQuery(ComponentType.ReadOnly<PhysicsConstrainedBodyPair>(), ComponentType.ReadOnly<PartJointInfo>()))
			using (var joints = query.ToEntityArray(Allocator.Temp))
			{
				for (int i = 0; i < joints.Length; i++)
				{
					Entity joint = joints[i];
					var pair = em.GetComponentData<PhysicsConstrainedBodyPair>(joint);
					if (BrokenJoints.Contains(joint) || Detached.Contains(pair.EntityA) || Detached.Contains(pair.EntityB)) em.DestroyEntity(joint);
				}
			}
			var jointList = simulator.Joints;
			for (int i = jointList.Length - 1; i >= 0; i--) if (!em.Exists(jointList[i])) jointList.RemoveAt(i);
			var parts = simulator.Parts;
			for (int i = parts.Length - 1; i >= 0; i--)
			{
				Entity part = parts[i];
				if (!Retired.Contains(part)) continue;
				if (em.Exists(part))
				{
					if (em.HasComponent<PartContainerValue>(part))
					{
						Entity container = em.GetComponentData<PartContainerValue>(part).Value;
						if (em.Exists(container) && em.HasComponent<ContainedPart>(container))
							em.SetComponentData(container, new ContainedPart { Value = Entity.Null });
					}
					if (em.HasComponent<PartTransform>(part))
					{
						var transform = em.GetComponentData<PartTransform>(part);
						int layer = PartBuildLayerUtility.GetLayer(em, part);
						if (simulator.PartMap.TryGet(transform.X, transform.Y, layer, out var grid))
						{
							if (grid.Part == part) grid.Part = Entity.Null;
							if (grid.PartContainer == part) grid.PartContainer = Entity.Null;
							if (grid.IsEmpty) simulator.PartMap.Remove(transform.X, transform.Y, layer);
							else simulator.PartMap.Set(transform.X, transform.Y, layer, grid);
						}
					}
					em.DestroyEntity(part);
				}
				parts.RemoveAt(i);
			}
			// Recompute topology and collision categories together; no phantom welds after a charge disappears.
			var pairs = manager.System.GetComponentLookup<PhysicsConstrainedBodyPair>(true);
			using (var set = PartSimulation.FindConnectedComponents(manager.PartSimulator.UnmanagedRef, pairs))
				PartSimulation.UpdateConnectedComponents(manager.PartSimulator.UnmanagedRef,
					manager.System.GetComponentLookup<PartConnectedComponent>(), manager.System.GetComponentLookup<PartExtensionComponent>(true), set);
			if (!UserSettings.Instance.SimulationSettings.PartSelfCollision) PartCollisionFilter.Apply(em, simulator);
			FuelSystem.MarkDirty();
			if (PartButtonList.Instance != null) PartButtonList.Instance.NeedsUpdate = true;
			Clear();
		}
	}
}
