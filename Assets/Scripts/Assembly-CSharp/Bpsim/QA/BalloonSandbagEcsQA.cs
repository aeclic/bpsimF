#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Bpsim.Parts;
using Bpsim.Parts.Gameplay;
using Bpsim.Parts.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using Collider = Unity.Physics.Collider;
using PhysicsMass = Unity.Physics.PhysicsMass;

namespace Bpsim.QA
{
	/// <summary>Controlled transient scenes; source data and native SpringJoint are the oracles.</summary>
	public sealed class BalloonSandbagEcsQA : MonoBehaviour
	{
		private static readonly StringBuilder Report = new StringBuilder();
		private static bool s_running;
		private static int s_pass, s_fail;
		private static string s_status = "idle";
		private readonly List<Entity> m_ground = new List<Entity>();
		private readonly List<BlobAssetReference<Collider>> m_blobs = new List<BlobAssetReference<Collider>>();
		private static EntityManager Em => PartManager.Instance.System.EntityManager;
		public static string Status() => s_status + " pass=" + s_pass + " fail=" + s_fail + "\n" + Report;
		public static string Inspect()
		{
			var text = new StringBuilder();
			text.AppendLine("gas=" + Count<BalloonBody>() + " bags=" + Count<Sandbag>() + " joints=" + Count<SuspensionJoint>()
				+ " lines=" + PartManager.Instance.System.World.GetExistingSystemManaged<SuspensionPresentationSystem>().LiveLineCount);
			using var query = Em.CreateEntityQuery(ComponentType.ReadOnly<SuspensionJoint>());
			using var joints = query.ToEntityArray(Allocator.Temp);
			foreach (Entity joint in joints)
			{
				var data = Em.GetComponentData<SuspensionJoint>(joint);
				text.AppendLine("joint " + joint + " owner=" + data.Owner + " alive=" + Em.Exists(data.Owner) + " world=" + data.WorldOnly);
			}
			return text.ToString();
		}
		public static string Run(string scenario = "all")
		{
			if (s_running) return "busy";
			if (!Application.isPlaying || PartManager.Instance == null || !PartManager.Instance.IsReady) return "requires ready Play Mode";
			if (PartManager.Instance.IsSimulating) return "stop user simulation before QA";
			Report.Clear(); s_pass = s_fail = 0; s_running = true; s_status = "running";
			var go = new GameObject("[BalloonSandbagEcsQA]"); go.hideFlags = HideFlags.DontSave;
			var runner = go.AddComponent<BalloonSandbagEcsQA>(); runner.StartCoroutine(runner.Suite(scenario));
			return "started";
		}
		private IEnumerator Suite(string scenario)
		{
			var manager = PartManager.Instance;
			int previous = manager.ActiveScene != null ? manager.ActiveScene.SceneID : -2;
			var scenes = new List<int>();
			try
			{
				CheckMath(); CheckClassicAnchors();
				var cases = new Dictionary<string, Func<IEnumerator>> {
					{ "neighbors", Neighbors }, { "multi", Multi }, { "search", Search },
					{ "collision", Collision }, { "generated", Generated } };
				foreach (var entry in cases)
				{
					if (scenario != "all" && scenario != entry.Key) continue;
					s_status = "running:" + entry.Key;
					PartScene created = null; Action<PartScene> handler = scene => created = scene;
					manager.SceneLoaded += handler;
					try { manager.LoadEmptyScene("SuspensionQA_" + entry.Key); }
					finally { manager.SceneLoaded -= handler; }
					if (created == null) throw new InvalidOperationException("No QA scene");
					created.State.CameraPosition = new Vector2(4, 101); created.State.CameraSize = 9;
					manager.SelectScene(created.SceneID); scenes.Add(created.SceneID);
					yield return null;
					IEnumerator steps = entry.Value();
					try
					{
						while (true)
						{
							bool next;
							try { next = steps.MoveNext(); }
							catch (Exception error) { Check(entry.Key + "-exception", false, error.ToString()); break; }
							if (!next) break;
							yield return steps.Current;
						}
					}
					finally { (steps as IDisposable)?.Dispose(); if (manager.IsSimulating) manager.EndSimulation(); ClearGround(); }
					yield return null;
					Check(entry.Key + "-stop-cleanup", Count<BalloonBody>() == 0 && Count<Sandbag>() == 0 && Count<SuspensionJoint>() == 0
						&& manager.System.World.GetExistingSystemManaged<SuspensionPresentationSystem>().LiveLineCount == 0, Inspect());
					bool pristine = true;
					foreach (Entity part in created.Unmanaged.Parts)
					{
						if (Em.HasComponent<Balloon>(part)) pristine &= !Em.GetComponentData<Balloon>(part).Initialized && !Em.HasComponent<PhysicsMass>(part);
						if (Em.HasComponent<Sandbag>(part)) pristine &= !Em.GetComponentData<Sandbag>(part).Bound && !Em.HasComponent<PhysicsMass>(part);
					}
					Check(entry.Key + "-build-entities-pristine", pristine);
				}
			}
			finally
			{
				if (manager.IsSimulating) manager.EndSimulation(); ClearGround();
				if (previous >= 0) manager.SelectScene(previous);
				foreach (int id in scenes) manager.UnloadScene(id);
				s_running = false; s_status = s_fail == 0 ? "PASS" : "FAIL";
				File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/balloon-sandbag-ecs-qa.log")), Status());
				Debug.Log("[BalloonSandbagEcsQA] " + Status()); Destroy(gameObject);
			}
		}
		private static void CheckMath()
		{
			Check("force-limiter", SuspensionMath.LimitBalloonForce(23, -10, 10) == 23
				&& SuspensionMath.LimitBalloonForce(23, 10, 10) == 23 && SuspensionMath.LimitBalloonForce(23, 11, 10) == 11.5f);
			SuspensionMath.SpringSoftness(100, 10, 11, out float frequency, out float damping);
			float mass = 1f / 11;
			Check("spring-coefficients", math.abs(frequency * frequency * 4 * math.PI * math.PI * mass - 100) < .001f
				&& math.abs(damping * 2 * math.sqrt(100 * mass) - 10) < .001f);
			var profile = BpleSuspensionProfiles.Get(new PartTypeInfo(PartType.Sandbag, 4));
			Check("source-heavy-bag", profile.mass == 5);
		}
		private static void CheckClassicAnchors()
		{
			for (int rotation = 0; rotation < 4; rotation++)
			{
				var anchor = new GameObject("[Suspension native anchor]"); var bag = new GameObject("[Suspension native bag]");
				anchor.hideFlags = bag.hideFlags = HideFlags.DontSave;
				try
				{
					anchor.transform.position = new Vector3(0, 1000, 0);
					var body = anchor.AddComponent<Rigidbody>(); body.isKinematic = true;
					bag.transform.rotation = Quaternion.Euler(0, 0, rotation * 90);
					bag.transform.position = anchor.transform.position - Vector3.up * .5f;
					bag.AddComponent<Rigidbody>().isKinematic = true;
					var joint = bag.AddComponent<SpringJoint>(); joint.connectedBody = body;
					joint.minDistance = 0; joint.maxDistance = .5f; joint.anchor = Vector3.up * .5f;
					joint.spring = 100; joint.damper = 10;
					Vector3 expected = anchor.transform.InverseTransformPoint(bag.transform.TransformPoint(joint.anchor));
					Check("native-auto-anchor-" + rotation, Vector3.Distance(expected, joint.connectedAnchor) < 1E-4f,
						"native=" + joint.connectedAnchor + " formula=" + expected);
				}
				finally { DestroyImmediate(bag); DestroyImmediate(anchor); }
			}
		}
		private IEnumerator Neighbors()
		{
			Place(PartType.Balloon, 0, 0, 101); Place(PartType.MetalFrame, 0, 0, 100);
			Place(PartType.MetalFrame, 0, 1, 101, offsetX: .55f); Place(PartType.MetalFrame, 0, 8, 101);
			Start(); Entity gas = Find(PartType.Balloon, 0), frame = Find(PartType.MetalFrame, 0), neighbor = Find(PartType.MetalFrame, 1), control = Find(PartType.MetalFrame, 8);
			Pin(frame); var body = Em.GetComponentData<BalloonBody>(gas);
			Check("balloon-physical-body", math.abs(Em.GetComponentData<PhysicsMass>(gas).InverseMass - 10) < 1E-4f
				&& body.Force == 23 && body.MaximumSpeed == 10 && body.Target == frame && Em.Exists(body.TetherJoint));
			Check("neighbor-not-connected", Em.GetComponentData<PartConnectedComponent>(neighbor).Index != Em.GetComponentData<PartConnectedComponent>(frame).Index);
			yield return new WaitForSeconds(.55f);
			float difference = Em.GetComponentData<LocalTransform>(neighbor).Position.y - Em.GetComponentData<LocalTransform>(control).Position.y;
			Check("no-proximity-lift", math.abs(difference) < .05f, "neighbor-control y=" + difference);
			Check("rope-visible", Em.GetComponentData<SuspensionRope>(gas).Visible && VisibleCount(gas) == 1
				&& (Application.isBatchMode || PartManager.Instance.System.World.GetExistingSystemManaged<SuspensionPresentationSystem>().LiveLineCount > 0));
			PartSimulation.ProcessTouch(gas); yield return new WaitForSeconds(.1f);
			Check("touch-pop-removes-body", !Em.Exists(gas)); PartSimulation.ProcessTouch(gas);
			Check("pop-cannot-reinflate", !Em.Exists(gas));
			var balance = Em.GetComponentData<BalloonBalancer>(frame);
			Check("last-pop-releases-balancer", balance.BalloonCount == 0 && !Em.Exists(balance.Joint));
		}
		private IEnumerator Multi()
		{
			Place(PartType.Balloons3, 0, 0, 101); Place(PartType.MetalFrame, 0, 0, 100);
			Place(PartType.Sandbag3, 0, 6, 100); Place(PartType.MetalFrame, 0, 6, 102);
			Start(); var gases = Parts(PartType.Balloons3); var bags = Parts(PartType.Sandbag3);
			Pin(Find(PartType.MetalFrame, 0)); Pin(Find(PartType.MetalFrame, 6));
			Check("three-balloon-bodies", gases.Count == 3 && math.abs(TotalMass(gases) - .3f) < 1E-4f);
			Check("three-independent-bags", bags.Count == 3 && math.abs(TotalMass(bags) - 4.5f) < 1E-4f);
			foreach (Entity part in bags)
			{
				var bag = Em.GetComponentData<Sandbag>(part); Sandbag.GetHangParameters(bag.Balloons, out var offset, out float distance);
				Check("bag-original-position-" + bag.Balloons, math.abs(bag.InitialPosition.x - (6 + offset.x)) < 1E-4f
					&& math.abs(bag.InitialPosition.y - (100 + offset.y)) < 1E-4f && bag.MaxDistance == distance && Em.Exists(bag.Joint), "initial=" + bag.InitialPosition);
				Check("single-bag-visual-" + bag.Balloons, VisibleCount(part) == 1);
			}
			yield return new WaitForSeconds(.12f);
			PartSimulation.TriggerFirstEnabled(gases); int requests = 0;
			foreach (Entity part in gases) if (Em.GetComponentData<BalloonBody>(part).PopRequest) requests++;
			Check("one-shot-pops-one-body", requests == 1);
			yield return new WaitForSeconds(.1f);
			var remaining = Parts(PartType.Balloons3);
			Check("two-gases-remain", remaining.Count == 2, "remaining=" + remaining.Count);
			PartSimulation.TriggerFirstEnabled(bags); yield return new WaitForSeconds(.1f);
			int dropped = 0; Entity released = Entity.Null;
			foreach (Entity part in bags) if (Em.GetComponentData<Sandbag>(part).Dropped) { dropped++; released = part; }
			Check("one-shot-releases-one-bag", dropped == 1 && !Em.GetComponentData<SuspensionRope>(released).Visible);
			PartSimulation.ProcessTouch(released); yield return new WaitForSeconds(.02f);
			Check("bag-release-irreversible", Em.GetComponentData<Sandbag>(released).Dropped && !Em.GetComponentData<Sandbag>(released).DropRequest);
		}
		private IEnumerator Search()
		{
			Place(PartType.Balloon, 0, 0, 101, rotation: 1); Place(PartType.MetalFrame, 0, 10, 101);
			Place(PartType.Sandbag, 0, 26, 101, rotation: 1); Place(PartType.MetalFrame, 0, 16, 101);
			Place(PartType.Balloon, 0, 32, 101, layer: 2); Place(PartType.MetalFrame, 0, 32, 100, layer: 1);
			Place(PartType.Sandbag, 4, 38, 101); Place(PartType.MetalFrame, 0, 38, 102);
			Start();
			var gas = Em.GetComponentData<BalloonBody>(Find(PartType.Balloon, 0)); var bag = Em.GetComponentData<Sandbag>(Find(PartType.Sandbag, 26));
			Check("rotated-ten-cell-balloon-search", gas.Target == Find(PartType.MetalFrame, 10));
			Check("rotated-ten-cell-bag-search", bag.Anchor == Find(PartType.MetalFrame, 16) && !bag.Dropped);
			Check("search-respects-layer", Em.GetComponentData<BalloonBody>(Find(PartType.Balloon, 32)).Target == Entity.Null);
			Check("heavy-bag-runtime-mass", math.abs(1f / Em.GetComponentData<PhysicsMass>(Find(PartType.Sandbag, 38)).InverseMass - 5) < 1E-4f);
			yield return null;
		}
		private IEnumerator Collision()
		{
			Place(PartType.Balloon, 0, 0, 101); Place(PartType.Balloon, 7, 8, 101); Start();
			Entity normal = Find(PartType.Balloon, 0), ghost = Find(PartType.Balloon, 8);
			Ground(0, 105, Em.GetComponentData<LocalTransform>(normal).Position.z); Ground(8, 105, Em.GetComponentData<LocalTransform>(ghost).Position.z);
			yield return new WaitForSeconds(1.3f);
			Check("ground-collision-pops-normal", !Em.Exists(normal));
			Check("unpop-skin-survives-collision", Em.Exists(ghost) && Em.GetComponentData<BalloonBody>(ghost).CollisionUnpoppable);
			PartSimulation.ProcessTouch(ghost); yield return new WaitForSeconds(.1f);
			Check("unpop-skin-still-manually-pops", !Em.Exists(ghost));
		}
		private IEnumerator Generated()
		{
			Place(PartType.MetalFrame, 0, 0, 100); Place(PartType.MetalFrame, 0, 4, 102);
			Place(PartType.Balloon, 0, 30, 101); Start(); PartSimulation.ProcessTouch(Find(PartType.Balloon, 30));
			Pin(Find(PartType.MetalFrame, 0)); Pin(Find(PartType.MetalFrame, 4));
			var simulator = PartManager.Instance.PartSimulator.Unmanaged;
			Entity gas = PartGameplayUtil.SpawnPart(new PartTypeInfo(PartType.Balloons3, 0), 0, 101, 0, false, ref simulator);
			Entity bag = PartGameplayUtil.SpawnPart(new PartTypeInfo(PartType.Sandbag2, 0), 4, 101, 0, false, ref simulator);
			yield return new WaitForSeconds(.2f);
			Check("generated-multi-bodies", Parts(PartType.Balloons3).Count == 3 && Parts(PartType.Sandbag2).Count == 2);
			simulator.PartMap.TryGet(0, 101, 1, out var gasSlot); simulator.PartMap.TryGet(4, 101, 1, out var bagSlot);
			Check("siblings-do-not-overwrite-slot", gasSlot.Part == gas && bagSlot.Part == bag);
			foreach (Entity part in Parts(PartType.Balloons3)) PartGameplayManager.Instance.Retire(part);
			yield return new WaitForSeconds(.1f);
			var balance = Em.GetComponentData<BalloonBalancer>(Find(PartType.MetalFrame, 0));
			Check("external-retire-cleans-balancer", balance.BalloonCount == 0 && !Em.Exists(balance.Joint));
		}
		private static void Place(PartType type, int index, int x, int y, int rotation = 0, int layer = 1, float offsetX = 0)
		{
			var manager = PartManager.Instance; var scene = manager.ActiveScene; var info = new PartTypeInfo(type, index);
			Entity prefab = manager.EntitySpawner.FindPrefab(info);
			if (prefab == Entity.Null || !scene.Unmanaged.PlacePartAt(new PartCreationRequest(prefab, scene.SceneID, info, x, y,
				rotation, false, offsetX, 0, 0, 1, 1, layer), false)) throw new InvalidOperationException("Place failed: " + info);
		}
		private static void Start() { PartManager.Instance.ActiveScene.Unmanaged.Update(); PartManager.Instance.StartSimulation(); }
		private static void Pin(Entity part)
		{
			var mass = Em.GetComponentData<PhysicsMass>(part); mass.InverseMass = 0; mass.InverseInertia = float3.zero;
			Em.SetComponentData(part, mass); Em.SetComponentData(part, PhysicsVelocity.Zero); SuspensionRuntime.Set(Em, part, new PhysicsGravityFactor { Value = 0 });
		}
		private static List<Entity> Parts(PartType type)
		{
			var result = new List<Entity>();
			foreach (Entity part in PartManager.Instance.PartSimulator.Unmanaged.Parts)
				if (Em.Exists(part) && Em.GetComponentData<PartTypeValue>(part).BaseType == type) result.Add(part);
			return result;
		}
		private static Entity Find(PartType type, int x)
		{
			foreach (Entity part in Parts(type)) if (Em.GetComponentData<PartTransform>(part).X == x) return part;
			throw new InvalidOperationException("Missing " + type + " x=" + x);
		}
		private static float TotalMass(List<Entity> parts) { float mass = 0; foreach (var part in parts) mass += 1 / Em.GetComponentData<PhysicsMass>(part).InverseMass; return mass; }
		private static int VisibleCount(Entity part)
		{
			int count = 0; foreach (var linked in Em.GetBuffer<LinkedEntityGroup>(part))
				if (Em.HasComponent<PartRenderInfo>(linked.Value) && !Em.HasComponent<DisableRendering>(linked.Value)) count++;
			return count;
		}
		private static int Count<T>() where T : unmanaged, IComponentData
		{
			// Build/prefab entities legitimately carry these state components without a Rigidbody.
			using var query = typeof(T) == typeof(BalloonBody) || typeof(T) == typeof(Sandbag)
				? Em.CreateEntityQuery(ComponentType.ReadOnly<T>(), ComponentType.ReadOnly<PhysicsMass>())
				: Em.CreateEntityQuery(ComponentType.ReadOnly<T>());
			return query.CalculateEntityCount();
		}
		private void Ground(float x, float y, float z)
		{
			var blob = PartPhysics.CreateBox(float3.zero, new float3(4, .4f, 1)); Entity entity = Em.CreateEntity();
			var pose = LocalTransform.FromPosition(new float3(x, y, z)); Em.AddComponentData(entity, pose); Em.AddComponentData(entity, (WorldTransform)pose);
			Em.AddComponentData(entity, new PhysicsCollider { Value = blob }); Em.AddComponentData(entity, PhysicsMass.CreateKinematic(blob.Value.MassProperties));
			Em.AddComponentData(entity, PhysicsVelocity.Zero); Em.AddSharedComponent(entity, new PhysicsWorldIndex(0u));
			m_ground.Add(entity); m_blobs.Add(blob);
		}
		private void ClearGround()
		{
			Em.CompleteAllTrackedJobs(); foreach (Entity entity in m_ground) if (Em.Exists(entity)) Em.DestroyEntity(entity);
			foreach (var blob in m_blobs) if (blob.IsCreated) blob.Dispose(); m_ground.Clear(); m_blobs.Clear();
		}
		private static void Check(string name, bool pass, string detail = "") { if (pass) s_pass++; else s_fail++; Report.AppendLine((pass ? "PASS " : "FAIL ") + name + " " + detail); }
	}
}
#endif
