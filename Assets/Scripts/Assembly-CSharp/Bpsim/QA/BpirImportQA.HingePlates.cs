#if UNITY_EDITOR
using System.Collections;
using Bpsim.Parts;
using Bpsim.Parts.Gameplay;
using Bpsim.Parts.Simulation;
using Bpsim.Physics.Anvil;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Extensions;
using Unity.Transforms;
using UnityEngine;

namespace Bpsim.QA
{
	public sealed partial class BpirImportQA
	{
		private IEnumerator CheckHingePlates(PartManager manager)
		{
			SimulationSettings settings = UserSettings.Instance.SimulationSettings;
			var previous = new SimulationSettings();
			previous.Update(settings);
			try
			{
				yield return ResetScene(manager, "BpirHingePlates");
				PartScene scene = manager.ActiveScene;
				PlacePart(scene, manager, new PartTypeInfo(PartType.MetalFrame, 0), 1, 3, 0, 1);
				PlacePart(scene, manager, new PartTypeInfo(PartType.Rope, 4), 0, 3, 0, 1);
				PlacePart(scene, manager, new PartTypeInfo(PartType.Rope, 5), 1, 3, 0, 1);
				PlacePart(scene, manager, new PartTypeInfo(PartType.MetalFrame, 0), 2, 3, 0, 1);
				PlacePart(scene, manager, new PartTypeInfo(PartType.Rope, 4), 2, 3, 0, 2);
				PlacePart(scene, manager, new PartTypeInfo(PartType.Kicker, 0), -1, 3, 0, 1);
				PlacePart(scene, manager, new PartTypeInfo(PartType.Rope, 6), 6, 3, 2, 1);
				PlacePart(scene, manager, new PartTypeInfo(PartType.Rope, 7), 7, 2, 5, 1);
				var scaledType = new PartTypeInfo(PartType.Rope, 4);
				var scaledRequest = new PartCreationRequest(manager.EntitySpawner.FindPrefab(scaledType), scene.SceneID, scaledType, 14, 3, 4, false)
					.WithFreeTransform(new PartFreeTransform { Offset = new float2(.4f, -.1f), Scale = new float2(.8f, 1.2f) });
				Check("hinge free transform fixture placed", scene.Unmanaged.PlacePartAt(in scaledRequest, false));
				for (int index = 0; index < 4; index++)
					PlacePart(scene, manager, new PartTypeInfo(PartType.Rope, index), index, 8, 0, 1);

				settings.SimulationType = SimulationType.HavokPhysics;
				settings.StableHingePlate = false;
				settings.CanHingePlateConnectComponents = true;
				settings.MegaStructure = true;
				settings.InfiniteConnectionStrength = false;
				settings.ConnectionStrengthFactor = 1f;
				manager.StartSimulation();
				manager.EndSimulation();
				yield return new WaitForSeconds(.04f);
				manager.StartSimulation();
				EntityManager em = manager.System.EntityManager;
				PartSimulatorUnmanaged simulation = manager.PartSimulator.Unmanaged;
				simulation.PartMap.TryGet(0, 3, 1, out PartGridInfo enclosed);
				simulation.PartMap.TryGet(1, 3, 1, out PartGridInfo track);
				simulation.PartMap.TryGet(2, 3, 1, out PartGridInfo frame);
				simulation.PartMap.TryGet(2, 3, 2, out PartGridInfo high);
				simulation.PartMap.TryGet(-1, 3, 1, out PartGridInfo kicker);
				simulation.PartMap.TryGet(6, 3, 1, out PartGridInfo diagonal);
				simulation.PartMap.TryGet(7, 2, 1, out PartGridInfo corner);
				simulation.PartMap.TryGet(14, 3, 1, out PartGridInfo scaled);
				HingePlate first = em.GetComponentData<HingePlate>(enclosed.Part);
				HingePlate second = em.GetComponentData<HingePlate>(track.Part);
				HingePlate upper = em.GetComponentData<HingePlate>(high.Part);
				HingePlate thinSilver = em.GetComponentData<HingePlate>(diagonal.Part);
				HingePlate thickSilver = em.GetComponentData<HingePlate>(corner.Part);
				HingePlate scaledPlate = em.GetComponentData<HingePlate>(scaled.Part);
				Check("hinge four variants keep two independent physical halves", !MegaStructure.Active && settings.MegaStructure
					&& CheckHalf(em, first.Left, .35f) && CheckHalf(em, first.Right, .35f)
					&& CheckHalf(em, second.Left, .6f) && CheckHalf(em, second.Right, .6f)
					&& CheckHalf(em, thinSilver.Left, .35f) && CheckHalf(em, thinSilver.Right, .35f)
					&& CheckHalf(em, thickSilver.Left, .6f) && CheckHalf(em, thickSilver.Right, .6f));
				Check("hinge simulated pose honors offset layout on the start frame",
					math.distance(em.GetComponentData<LocalTransform>(thinSilver.Right).Position.xy, new float2(6.25f, 2.5f)) < .0005f
					&& math.distance(em.GetComponentData<LocalTransform>(thickSilver.Left).Position.xy, new float2(6.5f, 2.25f)) < .0005f);
				Check("hinge kinematic root has no compound collider", !em.HasComponent<PhysicsCollider>(enclosed.Part)
					&& em.GetComponentData<PhysicsMass>(enclosed.Part).InverseMass == 0f);
				Check("hinge internal fixed pair and end pivots", PlateJoint(em, simulation, first.Left, first.Right, JointType.Fixed, .25f)
					&& PlateJoint(em, simulation, first.Right, first.Left, JointType.Fixed, -.25f));
				float breakImpulse = 3f * 1200f * Time.fixedDeltaTime;
				Check("hinge break force is triple the prefab strength and has no torque limit",
					PlateJoint(em, simulation, first.Left, first.Right, JointType.Fixed, .25f, breakImpulse: breakImpulse)
					&& PlateJoint(em, simulation, first.Right, second.Left, JointType.LimitedHinge, .25f, breakImpulse: breakImpulse));
				Check("hinge free offset and scale propagate to the bodies, colliders and pivots",
					math.distance(em.GetComponentData<LocalTransform>(scaledPlate.Left).Position.xy, new float2(14.6f, 3.5f)) < .0005f
					&& math.distance(em.GetComponentData<PhysicsCollider>(scaledPlate.Left).Value.Value.CalculateAabb().Extents, new float3(.48f, .42f, 1f)) < .0005f
					&& PlateJoint(em, simulation, scaledPlate.Left, scaledPlate.Right, JointType.Fixed, .20f));
				Check("hinge reciprocal centered neighbours", PlateJoint(em, simulation, first.Right, second.Left, JointType.LimitedHinge, .25f, 0f)
					&& PlateJoint(em, simulation, second.Left, first.Right, JointType.LimitedHinge, -.25f, 0f));
				Check("hinge diagonal signed limits", PlateJoint(em, simulation, thinSilver.Right, thickSilver.Left, JointType.LimitedHinge, .25f, -90f)
					&& PlateJoint(em, simulation, thickSilver.Left, thinSilver.Right, JointType.LimitedHinge, -.25f, 90f));
				Check("hinge mode 5 cannot wrap a mode 0 neighbour into phase 3",
					!HingePlate.TryConnect(scaledType, 5, scaledType, 0, new int2(-1, 0), false, out _, out _));
				Check("hinge common attaches to kicker but track ignores frames and other layers",
					PlateJoint(em, simulation, first.Left, kicker.Part, JointType.LimitedHinge, -.25f, 0f)
					&& !HasPlateJoint(em, simulation, second.Right, frame.PartContainer)
					&& !HasPlateJoint(em, simulation, second.Right, upper.Left)
					&& !HasPlateJoint(em, simulation, upper.Left, second.Right));
				HingePlateCollisionNetwork network = em.GetComponentData<HingePlateCollisionNetwork>(track.PartContainer);
				Check("hinge enclosing frame stays unwelded and ignores the entire linked plate network",
					!network.IsHalf && em.GetComponentData<HingePlateCollisionNetwork>(first.Left).Group == network.Group
					&& !em.HasComponent<HingePlateCollisionNetwork>(frame.PartContainer)
					&& ComponentOf(em, track.PartContainer) != ComponentOf(em, track.Part)
					&& ComponentOf(em, kicker.Part) == ComponentOf(em, track.Part));
				bool ropesUnchanged = true;
				for (int index = 0; index < 4; index++)
				{
					simulation.PartMap.TryGet(index, 8, 1, out PartGridInfo rope);
					ropesUnchanged &= em.HasComponent<Rope>(rope.Part) && !em.HasComponent<HingePlate>(rope.Part);
				}
				Check("rope indices 0–3 still use the rope solver", ropesUnchanged);

				float startX = em.GetComponentData<LocalTransform>(scaledPlate.Left).Position.x;
				em.SetComponentData(scaledPlate.Left, new PhysicsVelocity { Linear = new float3(4f, 0f, 0f) });
				yield return new WaitForSeconds(0.12f);
				float endX = em.GetComponentData<LocalTransform>(scaledPlate.Left).Position.x;
				float renderedX = em.GetComponentData<LocalToWorld>(scaledPlate.Left).Position.x;
				Check("hinge independently simulated halves carry their renderers", endX > startX + .01f && renderedX > startX + .01f,
					$"physics x={startX:F3}->{endX:F3}, artwork x={renderedX:F3}");
				float3 midpoint = .5f * (em.GetComponentData<LocalTransform>(scaledPlate.Left).Position + em.GetComponentData<LocalTransform>(scaledPlate.Right).Position);
				Check("hinge gameplay position follows the two bodies rather than the root", math.distancesq(midpoint, PartGameplayUtil.Position(scaled.Part)) < 1e-8f
					&& math.distancesq(midpoint, em.GetComponentData<LocalTransform>(scaled.Part).Position) > .01f);
				manager.EndSimulation();
				Check("hinge simulation cleanup destroys the split visual bodies", !em.Exists(first.Left) && !em.Exists(second.Right));
				yield return null;

				settings.StableHingePlate = true;
				settings.CanHingePlateConnectComponents = false;
				settings.InfiniteConnectionStrength = true;
				settings.SimulationType = SimulationType.AnvilPhysics;
				manager.StartSimulation();
				simulation = manager.PartSimulator.Unmanaged;
				simulation.PartMap.TryGet(0, 3, 1, out enclosed);
				simulation.PartMap.TryGet(1, 3, 1, out track);
				first = em.GetComponentData<HingePlate>(enclosed.Part);
				second = em.GetComponentData<HingePlate>(track.Part);
				Check("hinge stable mode welds endpoints without merging logical components", settings.StableHingePlate
					&& PlateJoint(em, simulation, first.Right, second.Left, JointType.Fixed, .25f, breakImpulse: float.PositiveInfinity)
					&& ComponentOf(em, enclosed.Part) != ComponentOf(em, track.Part));
				using (EntityQuery steps = em.CreateEntityQuery(ComponentType.ReadOnly<PhysicsStep>()))
				{
					SimulationType expected = AnvilWorld.SupportsCollisionTags ? SimulationType.AnvilPhysics : SimulationType.HavokPhysics;
					Check("hinge selects the supported backend and preserves the preference",
						steps.GetSingleton<PhysicsStep>().SimulationType == expected && settings.SimulationType == SimulationType.AnvilPhysics);
				}
				yield return new WaitForFixedUpdate();
				yield return CheckHingeContacts(manager, settings);
				yield return CheckGeneratedHingePlates(manager, settings);
				yield return CheckHingeBlast(manager, settings);
			}
			finally
			{
				if (manager.IsSimulating) manager.EndSimulation();
				settings.Update(previous);
			}
			yield return null;
		}

		private IEnumerator CheckHingeBlast(PartManager manager, SimulationSettings settings)
		{
			yield return ResetScene(manager, "BpirHingeBlast");
			PartScene scene = manager.ActiveScene;
			PlacePart(scene, manager, new PartTypeInfo(PartType.TNT, 0), 0, 12, 0, 1);
			PlacePart(scene, manager, new PartTypeInfo(PartType.Rope, 5), 0, 14, 0, 1);
			settings.TNTExplosionForce = 1f;
			settings.TNTExplosionRadius = 1f;
			manager.StartSimulation();
			yield return null;
			EntityManager em = manager.System.EntityManager;
			PartSimulatorUnmanaged simulation = manager.PartSimulator.Unmanaged;
			simulation.PartMap.TryGet(0, 12, 1, out PartGridInfo tnt);
			simulation.PartMap.TryGet(0, 14, 1, out PartGridInfo target);
			HingePlate plate = em.GetComponentData<HingePlate>(target.Part);
			PartSimulation.ProcessTouch(tnt.Part);
			float3 left = em.GetComponentData<PhysicsVelocity>(plate.Left).Linear;
			float3 right = em.GetComponentData<PhysicsVelocity>(plate.Right).Linear;
			float3 proxy = em.GetComponentData<PhysicsVelocity>(target.Part).Linear;
			Check("hinge blast applies opposing horizontal impulses to the two halves, not the proxy",
				left.x < -.05f && right.x > .05f && left.y > .05f && Near(left.y, right.y) && math.lengthsq(proxy) == 0f,
				$"left={left} right={right}");
		}

		private IEnumerator CheckGeneratedHingePlates(PartManager manager, SimulationSettings settings)
		{
			yield return ResetScene(manager, "BpirGeneratedHingePlates");
			PartScene scene = manager.ActiveScene;
			var thinSilver = new PartTypeInfo(PartType.Rope, 6);
			var thickSilver = new PartTypeInfo(PartType.Rope, 7);
			PlacePart(scene, manager, new PartTypeInfo(PartType.GrapplingHook, 8), 0, 15, 0, 2);
			PlacePart(scene, manager, thickSilver, -1, 15, 2, 2);
			var request = new PartCreationRequest(manager.EntitySpawner.FindPrefab(thinSilver), scene.SceneID, thinSilver, -2, 15, 2, false)
				.WithLayer(2).WithFreeTransform(new PartFreeTransform { Offset = new float2(.125f), Scale = new float2(.8f, 1.2f) });
			Check("hinge scaled generator template placed", scene.Unmanaged.PlacePartAt(in request, false));
			PlacePart(scene, manager, new PartTypeInfo(PartType.MetalFrame, 0), -3, 15, 0, 2);
			PlacePart(scene, manager, thickSilver, 10, 15, 2, 2);
			settings.SimulationType = SimulationType.HavokPhysics;
			settings.StableHingePlate = false;
			settings.CanHingePlateConnectComponents = true;
			manager.StartSimulation();
			EntityManager em = manager.System.EntityManager;
			PartSimulatorUnmanaged simulation = manager.PartSimulator.Unmanaged;
			simulation.PartMap.TryGet(0, 15, 2, out PartGridInfo generator);
			PartSimulation.ProcessTouch(generator.Part);
			bool hasTrack = simulation.PartMap.TryGet(9, 15, 2, out PartGridInfo track);
			bool hasCommon = simulation.PartMap.TryGet(8, 15, 2, out PartGridInfo common);
			bool hasFrame = simulation.PartMap.TryGet(7, 15, 2, out PartGridInfo frame);
			Check("hinge generator copies the whole batch into its own build layer", hasTrack && hasCommon && hasFrame);
			if (!hasTrack || !hasCommon || !hasFrame) yield break;

			HingePlate first = em.GetComponentData<HingePlate>(track.Part);
			HingePlate second = em.GetComponentData<HingePlate>(common.Part);
			simulation.PartMap.TryGet(10, 15, 2, out PartGridInfo existing);
			HingePlate outside = em.GetComponentData<HingePlate>(existing.Part);
			Check("hinge generated bodies use the six-layout pose and inherited scale", CheckHalf(em, first.Left, .6f)
				&& math.distance(em.GetComponentData<LocalTransform>(second.Left).Position.xy, new float2(7.925f, 14.525f)) < .0005f
				&& math.distance(em.GetComponentData<PhysicsCollider>(second.Left).Value.Value.CalculateAabb().Extents, new float3(.48f, .42f, 1f)) < .0005f
				&& PlateJoint(em, simulation, second.Left, second.Right, JointType.Fixed, .2f));
			Check("hinge generated endpoints connect inside the batch but not to a compatible scene neighbour",
				PlateJoint(em, simulation, first.Left, second.Right, JointType.LimitedHinge, -.25f)
				&& PlateJoint(em, simulation, second.Right, first.Left, JointType.LimitedHinge, .2f)
				&& PlateJoint(em, simulation, second.Left, frame.Part, JointType.LimitedHinge, -.2f)
				&& !HasPlateJoint(em, simulation, first.Right, outside.Left)
				&& em.GetComponentData<HingePlateCollisionNetwork>(first.Left).Group == em.GetComponentData<HingePlateCollisionNetwork>(second.Left).Group
				&& em.GetComponentData<HingePlateCollisionNetwork>(first.Left).Group != em.GetComponentData<HingePlateCollisionNetwork>(outside.Left).Group);
			yield return new WaitForFixedUpdate();
			Check("hinge generated component topology includes the copied plate endpoints", ComponentOf(em, track.Part) == ComponentOf(em, common.Part)
				&& ComponentOf(em, common.Part) == ComponentOf(em, frame.Part) && ComponentOf(em, common.Part) != ComponentOf(em, existing.Part));
		}

		private IEnumerator CheckHingeContacts(PartManager manager, SimulationSettings settings)
		{
			yield return ResetScene(manager, "BpirHingeContacts");
			PartScene scene = manager.ActiveScene;
			PlacePart(scene, manager, new PartTypeInfo(PartType.MetalFrame, 0), 0, 10, 0, 1);
			PlacePart(scene, manager, new PartTypeInfo(PartType.Rope, 5), 0, 10, 2, 1);
			PlacePart(scene, manager, new PartTypeInfo(PartType.Rope, 7), 1, 10, 2, 1);
			var wheelType = new PartTypeInfo(PartType.MotorWheel, 0);
			var wheelTransform = new PartFreeTransform { Offset = new float2(.2f, -.95f), Scale = new float2(1f) };
			var request = new PartCreationRequest(manager.EntitySpawner.FindPrefab(wheelType), scene.SceneID, wheelType, 1, 11, 0, false)
				.WithFreeTransform(wheelTransform);
			Check("hinge contact wheel fixture placed", scene.Unmanaged.PlacePartAt(in request, false));
			settings.PartSelfCollision = true;
			settings.InfiniteConnectionStrength = true;
			settings.StableHingePlate = false;
			settings.CanHingePlateConnectComponents = true;
			settings.MegaStructure = false;
			EntityManager em = manager.System.EntityManager;
			try
			{
				foreach (var backend in new[] { SimulationType.HavokPhysics, SimulationType.AnvilPhysics })
				{
					if (backend == SimulationType.AnvilPhysics && !AnvilWorld.SupportsCollisionTags) continue;
					settings.SimulationType = backend;
					for (int trial = 0; trial < 3; trial++)
					{
						manager.StartSimulation();
						PartSimulatorUnmanaged simulation = manager.PartSimulator.Unmanaged;
						simulation.PartMap.TryGet(0, 10, 1, out PartGridInfo frame);
						simulation.PartMap.TryGet(1, 11, 1, out PartGridInfo wheel);
						if (trial == 1) em.RemoveComponent<HingePlateCollisionNetwork>(frame.PartContainer);
						if (trial == 2) PartBuildLayerUtility.SetLayer(em, wheel.Part, 2);
						float3 frameStart = em.GetComponentData<LocalTransform>(frame.PartContainer).Position;
						float3 wheelStart = em.GetComponentData<LocalTransform>(wheel.Part).Position;
						using (EntityQuery steps = em.CreateEntityQuery(ComponentType.ReadOnly<PhysicsStep>()))
							Check($"{backend} hinge contact backend, trial {trial}", steps.GetSingleton<PhysicsStep>().SimulationType == backend);
						yield return new WaitForSeconds(.14f);
						if (backend == SimulationType.AnvilPhysics && trial == 0)
							Check("Anvil hinge plate simulation uses cold starts", AnvilStepSystem.Anvil?.WarmStartingEnabled == false);
						float frameTravel = math.distance(frameStart, em.GetComponentData<LocalTransform>(frame.PartContainer).Position);
						float wheelTravel = math.distance(wheelStart, em.GetComponentData<LocalTransform>(wheel.Part).Position);
						if (trial == 0)
							Check($"{backend} filters the enclosure but not the wheel", frameTravel < .001f && wheelTravel > .01f,
								$"frame={frameTravel:F4} wheel={wheelTravel:F4}");
						else if (trial == 1)
							Check($"{backend} moves the unfiltered enclosure", frameTravel > .01f, $"frame={frameTravel:F4}");
						else
							Check($"{backend} filters the wheel across build layers", wheelTravel < .001f, $"wheel={wheelTravel:F4}");
						manager.EndSimulation();
						yield return null;
					}
				}
			}
			finally
			{
				settings.SimulationType = SimulationType.HavokPhysics;
			}
		}

		private static int ComponentOf(EntityManager em, Entity part) => em.GetComponentData<PartConnectedComponent>(part).Index;

		private static bool CheckHalf(EntityManager em, Entity half, float height)
		{
			if (half == Entity.Null || em.HasComponent<Parent>(half) || !em.HasComponent<PhysicsWorldIndex>(half)) return false;
			var mass = em.GetComponentData<PhysicsMass>(half);
			var damping = em.GetComponentData<PhysicsDamping>(half);
			var collider = em.GetComponentData<PhysicsCollider>(half);
			return Near(mass.InverseMass, 1f) && math.all(mass.InverseInertia.xy == float2.zero) && mass.InverseInertia.z > 0f
				&& Near(damping.Linear, .2f) && Near(damping.Angular, .05f) && Near(collider.Value.Value.GetFriction(), .7f)
				&& math.distance(collider.Value.Value.CalculateAabb().Extents, new float3(.6f, height, 1f)) < .0005f;
		}

		private static bool HasPlateJoint(EntityManager em, PartSimulatorUnmanaged simulation, Entity from, Entity to)
		{
			foreach (Entity joint in simulation.Joints)
			{
				var pair = em.GetComponentData<PhysicsConstrainedBodyPair>(joint);
				if (pair.EntityA == from && pair.EntityB == to) return true;
			}
			return false;
		}

		private static bool PlateJoint(EntityManager em, PartSimulatorUnmanaged simulation, Entity from, Entity to, JointType type,
			float anchor, float angle = 0f, float? breakImpulse = null)
		{
			foreach (Entity entity in simulation.Joints)
			{
				var pair = em.GetComponentData<PhysicsConstrainedBodyPair>(entity);
				if (pair.EntityA != from || pair.EntityB != to || pair.EnableCollision != 0) continue;
				var joint = em.GetComponentData<PhysicsJoint>(entity);
				if (joint.JointType != type || !Near(joint.BodyAFromJoint.Position.x, anchor)) continue;
				if (breakImpulse.HasValue)
				{
					foreach (Constraint constraint in joint.GetConstraints())
					{
						float expected = constraint.Type == ConstraintType.Linear ? breakImpulse.Value : float.PositiveInfinity;
						if (!math.all(constraint.MaxImpulse == new float3(expected))) return false;
					}
				}
				if (type != JointType.LimitedHinge) return true;
				var range = joint.GetLimitedHingeRange();
				return Near(math.degrees(range.Min), angle - 15f) && Near(math.degrees(range.Max), angle + 15f);
			}
			return false;
		}
	}
}
#endif
