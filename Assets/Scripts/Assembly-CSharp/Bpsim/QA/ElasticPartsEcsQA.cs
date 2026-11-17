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
using Unity.Transforms;
using UnityEngine;

namespace Bpsim.QA
{
	/// <summary>Run/Status via bridge in Play Mode. Keeps the user's build scene; writes no save files.</summary>
	public sealed class ElasticPartsEcsQA : MonoBehaviour
	{
		private static readonly StringBuilder Report = new StringBuilder();
		private static bool s_running;
		private static int s_pass, s_fail;
		private static string s_status = "idle";
		private static EntityManager Em => PartManager.Instance.System.EntityManager;
		public static string Status() => s_status + " pass=" + s_pass + " fail=" + s_fail + "\n" + Report;
		public static string MathContracts()
		{
			if (s_running) return "busy";
			Report.Clear(); s_pass = s_fail = 0;
			CheckMath();
			return Status();
		}
		private static void CheckMath()
		{
			Check("force-boundaries", BellowsMath.Force(-.1f, 30f) == 0 && BellowsMath.Force(0, 30) == 0
				&& BellowsMath.Force(.5f, 30) == 0 && math.abs(BellowsMath.Force(.25f, 30) - 22.5f) < 1E-5f);
			Check("compression-regular", math.abs(BellowsMath.Compression(.5f, false) - .3f) < 1E-5f
				&& math.abs(BellowsMath.Compression(.8f, false) - .3f) < 1E-5f
				&& BellowsMath.Compression(1.1f, false) > .999f);
			Check("compression-alien", BellowsMath.Compression(.95f, true) > .999f
				&& BellowsMath.Compression(.95f, false) < .7f);
			foreach (float dt in new[] { .02f, .007f, .1f, .6f })
			{
				float impulse = 0f;
				for (float t = 0; t < .5f; t += dt) impulse += BellowsMath.Impulse(t, t + dt, 30f);
				Check("integral-dt-" + dt, math.abs(impulse - 10f) < 1E-4f, "impulse=" + impulse);
			}
			Check("profile-variants", Bellows.Create(new PartTypeInfo(PartType.Bellows, 6)).Alien
				&& Bellows.Create(new PartTypeInfo(PartType.Bellows, 6)).Force == 120
				&& !Bellows.Create(new PartTypeInfo(PartType.Bellows, 7)).Alien);
		}
		public static string Run()
		{
			if (s_running) return "busy";
			if (!Application.isPlaying || PartManager.Instance == null) return "requires Play Mode";
			if (PartManager.Instance.IsSimulating) return "stop the current simulation before QA";
			s_running = true; s_status = "running"; s_pass = s_fail = 0; Report.Clear();
			CheckMath();
			var host = new GameObject("[ElasticPartsEcsQA]");
			host.hideFlags = HideFlags.DontSave;
			host.AddComponent<ElasticPartsEcsQA>().StartCoroutine(host.GetComponent<ElasticPartsEcsQA>().Suite());
			return "started";
		}
		private IEnumerator Suite()
		{
			PartManager manager = PartManager.Instance;
			int previous = manager.ActiveScene != null ? manager.ActiveScene.SceneID : -2;
			var testScenes = new List<int>();
			try
			{
				foreach (Func<IEnumerator> factory in new Func<IEnumerator>[] { BellowsCase, SpringCase, EndpointCase })
				{
					PartScene created = null;
					Action<PartScene> loaded = scene => created = scene;
					manager.SceneLoaded += loaded;
					try { manager.LoadEmptyScene("ElasticPartsEcsQA"); }
					finally { manager.SceneLoaded -= loaded; }
					if (created == null) throw new InvalidOperationException("QA scene was not created");
					int current = created.SceneID;
					manager.SelectScene(current);
					testScenes.Add(current);
					yield return null;
					IEnumerator steps = factory();
					try
					{
						while (true)
						{
							bool next;
							try { next = steps.MoveNext(); }
							catch (Exception exception) { Check("case-exception", false, exception.ToString()); break; }
							if (!next) break;
							yield return steps.Current;
						}
					}
					finally { (steps as IDisposable)?.Dispose(); if (manager.IsSimulating) manager.EndSimulation(); }
					Check("endpoints-cleaned", Count<SpringEndpoint>() == 0 &&
						manager.System.World.GetExistingSystemManaged<ElasticSpringSystem>().OwnedColliderCount == 0);
				}
			}
			finally
			{
				if (manager.IsSimulating) manager.EndSimulation();
				if (previous >= 0) manager.SelectScene(previous);
				foreach (int id in testScenes) manager.UnloadScene(id);
				s_running = false; s_status = s_fail == 0 ? "PASS" : "FAIL";
				File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/elastic-parts-ecs-qa.log")), Status());
				Debug.Log("[ElasticPartsEcsQA] " + Status());
				Destroy(gameObject);
			}
		}
		private IEnumerator BellowsCase()
		{
			PartManager manager = PartManager.Instance;
			// Bellows' connection face is Right, not Up. Both pairs must have matching welds/mass.
			// Imported level terrain can remain in the world; put the fixture above it.
			Place(PartType.Bellows, 0, 0, 100); Place(PartType.MetalFrame, 0, 1, 100);
			Place(PartType.Bellows, 0, 8, 100); Place(PartType.MetalFrame, 0, 9, 100);
			manager.ActiveScene.Unmanaged.Update(); manager.StartSimulation();
			yield return new WaitForSeconds(.1f);
			Entity active = Find(PartType.Bellows, 0), control = Find(PartType.Bellows, 8);
			float3x3 original = Em.GetComponentData<PostTransformScale>(active).Value;
			PartSimulation.ProcessTouch(active);
			yield return new WaitForSeconds(.25f);
			Bellows state = Em.GetComponentData<Bellows>(active);
			float3x3 compressed = Em.GetComponentData<PostTransformScale>(active).Value;
			Check("bellows-real-compression", state.Compression < .9f && state.Compression >= .3f
				&& math.length(compressed.c1) < math.length(original.c1) * .9f
				&& math.distance(compressed.c0, original.c0) < 1E-5f && math.distance(compressed.c2, original.c2) < 1E-5f);
			PartSimulation.ProcessTouch(active);
			// Observe after the full .5 s force window plus a settling interval;
			// an early displacement sample was dominated by solver/frame phase.
			yield return new WaitForSeconds(.65f);
			float gain = Em.GetComponentData<LocalTransform>(active).Position.y - Em.GetComponentData<LocalTransform>(control).Position.y;
			state = Em.GetComponentData<Bellows>(active);
			Check("bellows-physical-gain", gain > .3f && state.DeliveredImpulse > 9.99f, "gain=" + gain + " impulse=" + state.DeliveredImpulse);
			Check("bellows-cooldown-ignores-retrigger", state.Cycles == 1);
			yield return new WaitForSeconds(.6f);
			state = Em.GetComponentData<Bellows>(active);
			Check("bellows-restores-scale", !state.Enabled && state.Compression == 1 &&
				math.distance(Em.GetComponentData<PostTransformScale>(active).Value.c1, original.c1) < 1E-5f);
			PartSimulation.ProcessTouch(active); yield return new WaitForSeconds(.06f);
			Check("bellows-second-cycle", Em.GetComponentData<Bellows>(active).Cycles == 2);
		}
		private IEnumerator SpringCase()
		{
			PartManager manager = PartManager.Instance;
			Place(PartType.MetalFrame, 0, 0, 100); Place(PartType.Spring, 0, 0, 101); Place(PartType.MetalFrame, 0, 0, 102);
			manager.ActiveScene.Unmanaged.Update(); manager.StartSimulation();
			yield return new WaitForSeconds(.1f);
			Entity spring = Find(PartType.Spring, 0), lower = Find(PartType.MetalFrame, 0, 100), upper = Find(PartType.MetalFrame, 0, 102);
			var fixedMass = Em.GetComponentData<PhysicsMass>(upper); fixedMass.InverseMass = 0; fixedMass.InverseInertia = float3.zero;
			Em.SetComponentData(upper, fixedMass); Em.SetComponentData(upper, PhysicsVelocity.Zero);
			var state = Em.GetComponentData<ElasticSpring>(spring);
			Check("spring-passive-joint", state.Initialized && !state.OwnsEndpoint && state.ConnectedBody == lower
				&& Em.Exists(state.Joint) && !MegaStructure.Active && Em.Exists(state.Visualization));
			var constraints = Em.GetComponentData<PhysicsJoint>(state.Joint).GetConstraints();
			bool soft = false;
			foreach (var constraint in constraints) soft |= constraint.Type == ConstraintType.Linear && constraint.SpringFrequency < 100f;
			Check("spring-soft-constraint", soft);
			float3 before = Em.GetComponentData<PhysicsVelocity>(lower).Linear;
			PartSimulation.ProcessTouch(spring);
			Check("spring-touch-is-not-radial-punch", math.distance(before, Em.GetComponentData<PhysicsVelocity>(lower).Linear) == 0);
			var velocity = Em.GetComponentData<PhysicsVelocity>(lower); velocity.Linear.y -= 3f; Em.SetComponentData(lower, velocity);
			float initial = state.Length, maximum = initial;
			for (int i = 0; i < 15; i++)
			{
				yield return new WaitForSeconds(.02f);
				state = Em.GetComponentData<ElasticSpring>(spring); maximum = math.max(maximum, state.Length);
			}
			Check("spring-stretches-without-touch", maximum > initial + .04f && maximum < 4f, "initial=" + initial + " max=" + maximum);
			Check("spring-visual-stretches", Em.HasComponent<SpringVisual>(state.Visualization)
				&& math.length(Em.GetComponentData<PostTransformScale>(state.Visualization).Value.c1) > 0);
		}
		private IEnumerator EndpointCase()
		{
			PartManager manager = PartManager.Instance;
			Place(PartType.Spring, 0, 0, 100); Place(PartType.MetalFrame, 0, 0, 101);
			manager.ActiveScene.Unmanaged.Update(); manager.StartSimulation();
			yield return new WaitForSeconds(.1f);
			Entity spring = Find(PartType.Spring, 0);
			var state = Em.GetComponentData<ElasticSpring>(spring);
			Entity oldEndpoint = state.ConnectedBody;
			Check("spring-owned-free-end", state.OwnsEndpoint && Em.HasComponent<SpringEndpoint>(oldEndpoint)
				&& Em.GetComponentData<SpringEndpoint>(oldEndpoint).Owner == spring && Em.Exists(state.Joint));
			manager.EndSimulation();
			Check("spring-endpoint-destroyed-on-stop", !Em.Exists(oldEndpoint) && Count<SpringEndpoint>() == 0);
			manager.StartSimulation(); yield return new WaitForSeconds(.1f);
			Check("spring-clean-restart", Count<SpringEndpoint>() == 1 && !Em.Exists(oldEndpoint), "count=" + Count<SpringEndpoint>() + " oldAlive=" + Em.Exists(oldEndpoint));
			PartSimulatorUnmanaged simulator = manager.PartSimulator.Unmanaged;
			Entity spawned = PartGameplayUtil.SpawnPart(new PartTypeInfo(PartType.Spring, 0), 7, 100, 0, false, ref simulator);
			yield return new WaitForSeconds(.1f);
			Check("generated-spring-initialized", Em.Exists(spawned) && Em.HasComponent<ElasticSpring>(spawned)
				&& Em.GetComponentData<ElasticSpring>(spawned).Initialized && Count<SpringEndpoint>() == 2, "count=" + Count<SpringEndpoint>());
			PartGameplayManager.Instance.Retire(spawned); yield return new WaitForSeconds(.1f);
			Check("generated-spring-orphan-cleanup", !Em.Exists(spawned) && Count<SpringEndpoint>() == 1, "count=" + Count<SpringEndpoint>());
		}
		private static void Place(PartType type, int index, int x, int y)
		{
			var manager = PartManager.Instance; var scene = manager.ActiveScene;
			var info = new PartTypeInfo(type, index); Entity prefab = manager.EntitySpawner.FindPrefab(info);
			if (prefab == Entity.Null || !scene.Unmanaged.PlacePartAt(new PartCreationRequest(prefab, scene.SceneID, info, x, y, 0, false), false))
				throw new InvalidOperationException("Cannot place " + type);
		}
		private static Entity Find(PartType type, int x, int? y = null)
		{
			foreach (Entity part in PartManager.Instance.PartSimulator.Unmanaged.Parts)
			{
				var info = Em.GetComponentData<PartTypeValue>(part); var grid = Em.GetComponentData<PartTransform>(part);
				if (info.BaseType == type && grid.X == x && (!y.HasValue || grid.Y == y.Value)) return part;
			}
			throw new InvalidOperationException("Missing " + type);
		}
		private static int Count<T>() where T : unmanaged, IComponentData
		{
			using var query = Em.CreateEntityQuery(ComponentType.ReadOnly<T>());
			return query.CalculateEntityCount();
		}
		private static void Check(string name, bool passed, string details = "")
		{
			if (passed) s_pass++; else s_fail++;
			Report.AppendLine((passed ? "PASS " : "FAIL ") + name + " " + details);
		}
	}
}
#endif
