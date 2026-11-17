#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Bpsim.Parts;
using Bpsim.Parts.Gameplay;
using Bpsim.Parts.Simulation;
using Bpsim.UI;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

namespace Bpsim.QA
{
	public sealed partial class ScaleLightsUmbrellaQA
	{
		private Entity SimPart(PartType type, int index)
		{
			foreach (Entity part in PartGameplayUtil.Simulator.Parts)
				if (PartGameplayUtil.TypeInfo(part).PartType == type && PartGameplayUtil.TypeInfo(part).PartIndex == index) return part;
			throw new InvalidOperationException("Missing test part " + type + "#" + index);
		}

		private static int VisibleLampSprites(Entity root, bool active)
		{
			int count = 0;
			foreach (var item in PartGameplayUtil.Em.GetBuffer<LinkedEntityGroup>(root))
				if (PartGameplayUtil.Em.HasComponent<PointLightVisual>(item.Value)
					&& PartGameplayUtil.Em.GetComponentData<PointLightVisual>(item.Value).Active == active
					&& !PartGameplayUtil.Em.HasComponent<DisableRendering>(item.Value)) count++;
			return count;
		}

		private IEnumerator CheckLightContracts()
		{
			CheckNativeCollisionCases();
			var settings = UserSettings.Instance.SimulationSettings;
			settings.CreateJoints = true;
			settings.BpleLightSystem = settings.BpleSeparatedLightButtons = true;
			var spriteMap = PartButtonList.BuildSpriteInfoMap(CoreManager.Instance.Resources.LoadAsset<Texture2D>("PartButton_Texture"));
			foreach (var type in new[] { PartType.PointLight, PartType.SpotLight })
			for (int index = 0; index < (type == PartType.PointLight ? 7 : 4); index++)
			{
				yield return ResetScene();
				Place(PartType.MetalFrame, 0, 0, 0);
				Entity placed = Place(type, index, 0, 0);
				Check("light fixture " + type + "#" + index, placed != Entity.Null);
				if (placed == Entity.Null) continue;
				yield return null;
				PartManager.Instance.StartSimulation();
				Entity part = SimPart(type, index);
				var behavior = PartGameplayManager.Instance.Find(part);
				var source = BpleLightRuntime.Instance.Find(part);
				Check("light starts off " + type + "#" + index, !behavior.Enabled && !source.Activated && !source.EntityEnabled);
				Check("light mass " + type + "#" + index, math.abs(PartVariantPhysics.Mass(part) - 1) < .0001f);
				var aspect = PartGameplayUtil.Em.GetAspect<PartAspect>(part);
				var button = BasePart.GetTriggerButtonInfo(aspect).Single();
				int direction = type == PartType.PointLight ? (index == 4 ? 4 : 0) : 1;
				Check("light direction/component button " + type + "#" + index,
					button.Value.PartIndex == direction && button.Value.ComponentIndex == aspect.ConnectedComponent
					&& spriteMap.ContainsKey(new PartButtonInfo(PartButtonType.Trigger, 0, type, direction, -1)));
				Check("light toggle semantics " + type + "#" + index, button.Consistent == !(type == PartType.PointLight && index == 5));
				settings.BpleDarkness = type == PartType.PointLight && index == 4;
				behavior.Trigger();
				BpleLightRuntime.Instance.Step(.02f);
				Check("light activates " + type + "#" + index, behavior.Enabled && source.Activated);
				if (type == PartType.PointLight) Check("active sprite " + index, VisibleLampSprites(part, true) == 1 && VisibleLampSprites(part, false) == 0);
				Check("entity light variant " + type + "#" + index, source.Shape.Type == source.Profile.entityType
					&& source.EntityEnabled == (source.Profile.entityType >= 0));
				if (source.Shape.Type >= 0)
				{
					if (source.Shape.Type == 4) Check("legendary follows assembly center/radius", Vector2.Distance(source.Position, Vector2.zero) < .0001f
						&& Mathf.Abs(source.Shape.Length - 4) < .0001f && Mathf.Abs(source.Shape.HalfWidth - .1875f) < .0001f);
					else Check("BPLE dimensions " + type + "#" + index, Mathf.Abs(source.Shape.Length - source.Profile.length) < .0001f
						&& Mathf.Abs(source.Shape.HalfWidth - source.Profile.halfWidth) < .0001f && source.Shape.Angle == source.Profile.angle);
					var mesh = new Mesh();
					BpleLightRuntime.BuildMesh(mesh, source.Shape);
					Check("actual emitted mesh " + type + "#" + index, mesh.vertexCount == (source.Shape.Type <= 1 ? 4 : 302));
					Destroy(mesh);
				}
				if (index == 2 && type == PartType.PointLight)
				{
					float expected = 7 + Mathf.Sin(1.25f * 19) * Mathf.Sin(1.25f * 13) * .2f;
					Check("lantern realtime flicker", Mathf.Abs(BpleLightRuntime.IlluminationRadius(source, .3f, 1.25f) - expected) < .0001f);
				}
				if (type == PartType.SpotLight)
				{
					Check("queryable trigger pillar " + index, source.Query != Entity.Null && PartGameplayUtil.Em.Exists(source.Query));
					if (source.Query != Entity.Null && PartGameplayUtil.Em.Exists(source.Query))
					{
						var collider = PartGameplayUtil.Em.GetComponentData<PhysicsCollider>(source.Query).Value;
						var ray = new RaycastInput { Start = new float3(source.Shape.Length * .5f, -1, 0), End = new float3(source.Shape.Length * .5f, 1, 0), Filter = CollisionFilter.Default };
						Check("pillar ray queries " + index, collider.Value.CastRay(ray, out Unity.Physics.RaycastHit hit) && hit.Fraction > 0 && hit.Fraction < 1);
					}
					((SpotLightBehavior)behavior).Tick(.02f);
					Check("assembled spotlight suppresses legacy cone " + index, math.all(((SpotLightBehavior)behavior).ConeScale == float3.zero));
				}
				if (settings.BpleDarkness) Check("night vision actually on", BpleLightRuntime.Instance.NightVisionOn);
				yield return null;
				if (type == PartType.PointLight && index == 0) Capture("point-shield", 0, 4, 6);
				if (type == PartType.SpotLight && index == 1) Capture("spot-pillar", 0, 4, 6);
				behavior.Trigger();
				BpleLightRuntime.Instance.Step(.02f);
				Check("light switches off " + type + "#" + index, !behavior.Enabled && !source.EntityEnabled);
				if (type == PartType.PointLight) Check("inactive sprite restored " + index, VisibleLampSprites(part, false) == 1);
				if (settings.BpleDarkness) Check("night vision restored", !BpleLightRuntime.Instance.NightVisionOn);
				settings.BpleDarkness = false;
			}
			foreach (var test in new[] { (PartType.SpotLight, 1, -1.2f, 3f, .4f, 3f, 80f, 0f),
				(PartType.PointLight, 0, 0f, 10f, 0f, 8.4f, 0f, -80f), (PartType.PointLight, 2, 0f, 7f, 0f, 8.6f, 0f, 80f),
				(PartType.PointLight, 4, 0f, 6f, 0f, 4.4f, 0f, -80f) })
			{
				yield return ResetScene();
				Place(PartType.MetalFrame, 0, 0, 0); Place(test.Item1, test.Item2, 0, 0);
				Entity target = Place(PartType.MetalFrame, 0, 3, -3);
				var free = new PartFreeTransform { Offset = new float2(test.Item3 - 3, test.Item4 + 3), Scale = new float2(1) };
				m_testScene.Unmanaged.TrySetPartFree(target, in free);
				yield return null;
				PartManager.Instance.StartSimulation();
				Entity lamp = SimPart(test.Item1, test.Item2);
				foreach (Entity candidate in PartGameplayUtil.Simulator.Parts)
					if (PartGameplayUtil.Em.GetComponentData<PartTransform>(candidate).X == 3) target = candidate;
				PartGameplayManager.Instance.Find(lamp).Trigger();
				var em = PartGameplayUtil.Em;
				var pose = em.GetComponentData<LocalTransform>(target);
				pose.Position.xy = new float2(test.Item5, test.Item6); em.SetComponentData(target, pose);
				var velocity = em.GetComponentData<PhysicsVelocity>(target); velocity.Linear = new float3(test.Item7, test.Item8, 0); em.SetComponentData(target, velocity);
				BpleLightRuntime.Instance.Step(.02f);
				velocity = em.GetComponentData<PhysicsVelocity>(target);
				Check("real ECS light contact " + test.Item1 + "#" + test.Item2, BpleLightRuntime.Instance.ContactCount > 0
					&& math.length(velocity.Linear.xy) < 1, "velocity=" + velocity.Linear + " pos=" + em.GetComponentData<LocalTransform>(target).Position);
			}
			yield return ResetScene();
			Place(PartType.PointLight, 1, 0, 0);
			yield return null;
			settings.BpleLightSystem = false;
			PartManager.Instance.StartSimulation();
			var mushroom = (PointLightBehavior)PartGameplayManager.Instance.Find(SimPart(PartType.PointLight, 1));
			mushroom.Trigger(); Check("legacy mushroom not toggleable", !mushroom.Enabled && !mushroom.CanTrigger);
			yield return ResetScene();
			settings.BpleLightSystem = true;
			Place(PartType.SpotLight, 0, 0, 0);
			yield return null;
			PartManager.Instance.StartSimulation();
			var singleSpot = (SpotLightBehavior)PartGameplayManager.Instance.Find(SimPart(PartType.SpotLight, 0));
			singleSpot.Trigger(); singleSpot.Tick(.1f);
			Check("single spotlight keeps legacy cone, no physical pillar", math.abs(singleSpot.ConeScale.x - 3) < .0001f
				&& !BpleLightRuntime.Instance.Find(singleSpot.Part).EntityEnabled);
			yield return ResetScene();
			Check("light cleanup", BpleLightRuntime.Instance.SourceCount == 0 && !BpleLightRuntime.Instance.NightVisionOn);
		}

		private IEnumerator CheckUmbrellaContracts()
		{
			var settings = UserSettings.Instance.SimulationSettings;
			settings.CreateJoints = settings.BpleSpecialUmbrellas = true;
			for (int index = 0; index < 7; index++)
			{
				yield return ResetScene();
				Place(PartType.MetalFrame, 0, 0, -1); Place(PartType.Umbrella, index, 0, 0);
				yield return null;
				PartManager.Instance.StartSimulation();
				var umbrella = (UmbrellaBehavior)PartGameplayManager.Instance.Find(SimPart(PartType.Umbrella, index));
				Check("umbrella initial state " + index, umbrella.Open && !umbrella.Enabled && umbrella.CanTrigger && umbrella.Mode == index);
				Check("umbrella mass " + index, math.abs(PartVariantPhysics.Mass(umbrella.Part) - .5f) < .0001f);
				var aspect = PartGameplayUtil.Em.GetAspect<PartAspect>(umbrella.Part);
				Check("umbrella directional/component key " + index, BasePart.GetTriggerButtonInfo(aspect).Single().Value.PartIndex == 1
					&& BasePart.GetTriggerButtonInfo(aspect).Single().Value.ComponentIndex == aspect.ConnectedComponent);
				Check("no separate angle slider " + index, !BasePart.GetSliderButtonInfo(aspect).Any());
				var em = PartGameplayUtil.Em;
				var mass = em.GetComponentData<PhysicsMass>(umbrella.Part);
				var velocity = em.GetComponentData<PhysicsVelocity>(umbrella.Part);
				velocity.Linear = new float3(-2, -3, 0);
				velocity.Angular = math.rotate(math.inverse(math.mul(PartVariantPhysics.Rotation(umbrella.Part), mass.Transform.rot)), new float3(0, 0, .4f));
				em.SetComponentData(umbrella.Part, velocity);
				umbrella.Tick(.02f);
				float3 expected = index == 0 ? new float3(2, 9, 0) : (index == 2 || index == 4 ? float3.zero
					: new float3(index == 1 ? 6.4f : (index == 5 ? 19.2f : 12.8f), 0, 0));
				Check("BPLE umbrella force " + index, math.distance(umbrella.LastForce, expected) < .001f, umbrella.LastForce);
				foreach (var item in em.GetBuffer<LinkedEntityGroup>(umbrella.Part)) if (em.HasComponent<UmbrellaVisual>(item.Value))
					Check("special canopy initialized at BPLE origin " + index, math.length(em.GetComponentData<LocalTransform>(item.Value).Position.xy) < .0001f);
			}
			yield return ResetScene();
			Place(PartType.MetalFrame, 0, 0, -1); Place(PartType.Umbrella, 0, 0, 0);
			yield return null;
			PartManager.Instance.StartSimulation();
			var canopy = (UmbrellaBehavior)PartGameplayManager.Instance.Find(SimPart(PartType.Umbrella, 0));
			canopy.Trigger(); canopy.Tick(.125f);
			CheckCanopy(canopy, .925f, 1.125f, -.08125f, "cubic fold midpoint");
			canopy.Tick(.125f); Check("stroke uses strict > 0.25 second boundary", canopy.Open && canopy.Enabled);
			canopy.Tick(.001f); Check("fold completes", !canopy.Open && !canopy.Enabled);
			PartGameplayUtil.Em.CompleteAllTrackedJobs();
			Unity.Entities.World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<Unity.Transforms.TransformSystemGroup>().Update();
			PartGameplayUtil.Em.CompleteAllTrackedJobs();
			Capture("umbrella-folded", 0, 0, 2);
			canopy.Trigger(); canopy.Tick(.125f);
			CheckCanopy(canopy, .409375f, 1.984375f, -.46796875f, "sixth-power unfold midpoint");
			canopy.Tick(.126f); Check("unfold completes", canopy.Open && !canopy.Enabled);
			canopy.Trigger(); canopy.Tick(.1f); canopy.Trigger();
			var before = CanopyScale(canopy.Part); canopy.Tick(.1f);
			Check("press pauses stroke without resetting progress", !canopy.Enabled && math.all(before == CanopyScale(canopy.Part)));
			canopy.Trigger(); canopy.Tick(.151f); Check("press resumes same stroke", !canopy.Open && !canopy.Enabled);
			yield return ResetScene();
			Place(PartType.Umbrella, 3, 0, 0); yield return null; PartManager.Instance.StartSimulation();
			var detached = (UmbrellaBehavior)PartGameplayManager.Instance.Find(SimPart(PartType.Umbrella, 3));
			detached.Trigger(); Check("detached umbrella does not stroke or steer", !detached.Enabled && !detached.CanTrigger && detached.Mode == 0);
			yield return ResetScene();
			settings.BpleSpecialUmbrellas = false;
			Place(PartType.MetalFrame, 0, 0, -1); Place(PartType.Umbrella, 3, 0, 0); yield return null; PartManager.Instance.StartSimulation();
			var legacy = (UmbrellaBehavior)PartGameplayManager.Instance.Find(SimPart(PartType.Umbrella, 3));
			Check("legacy umbrella mass/mode", legacy.Mode == 0 && math.abs(PartVariantPhysics.Mass(legacy.Part) - .2f) < .0001f);
			yield return ResetScene();
		}

		private static float3 CanopyScale(Entity part)
		{
			foreach (var item in PartGameplayUtil.Em.GetBuffer<LinkedEntityGroup>(part)) if (PartGameplayUtil.Em.HasComponent<UmbrellaVisual>(item.Value))
			{
				var matrix = PartGameplayUtil.Em.GetComponentData<PostTransformScale>(item.Value).Value;
				return new float3(matrix.c0.x, matrix.c1.y, matrix.c2.z);
			}
			return float3.zero;
		}
		private static void CheckCanopy(UmbrellaBehavior canopy, float x, float y, float positionY, string name)
		{
			float3 scale = CanopyScale(canopy.Part);
			Check(name + " visual scale", math.distance(scale, new float3(x, y, 1)) < .0001f, scale);
			foreach (var item in PartGameplayUtil.Em.GetBuffer<LinkedEntityGroup>(canopy.Part)) if (PartGameplayUtil.Em.HasComponent<UmbrellaVisual>(item.Value))
				Check(name + " visual position", math.abs(PartGameplayUtil.Em.GetComponentData<LocalTransform>(item.Value).Position.y - positionY) < .0001f);
			var bounds = PartGameplayUtil.Em.GetComponentData<PhysicsCollider>(canopy.Part).Value.Value.CalculateAabb();
			Check(name + " real canopy collider width", math.abs(bounds.Extents.x - x) < .0001f, bounds.Extents.x);
		}
		private static void Capture(string name, float x, float y, float size)
		{
			string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "_codex_work", "qa", "bple-oracle"));
			Directory.CreateDirectory(folder);
			BpsimPlayQA.Capture(Path.Combine(folder, name + ".png") + ",1280,720," + x + "," + y + "," + size);
		}

		[Serializable] private sealed class CollisionFixtures { public CollisionCase[] cases; }
		[Serializable] private sealed class CollisionCase
		{
			public int type, sides, count;
			public float length, halfWidth, angle, time, separation;
			public float[] lightPosition0, lightPosition1, lightDirection0, lightDirection1, position0, position1, direction0, direction1, bounds, contactPoint, contactNormal, relativeVelocity;
			public bool hit;
		}
		private static Vector2 Vector(float[] value) => new Vector2(value[0], value[1]);
		private static void CheckNativeCollisionCases()
		{
			string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "_codex_work", "qa", "bple-oracle", "native-cases.json"));
			if (!File.Exists(path)) { Check("compiled BPLE collision fixtures present", false, "run Tools/BPSim/verify_bple_lights_umbrella.py --native-cases first"); return; }
			var fixtures = JsonUtility.FromJson<CollisionFixtures>(File.ReadAllText(path));
			var collision = new BpleLightCollision();
			int failures = 0; float maximum = 0;
			foreach (var sample in fixtures.cases)
			{
				var light = new BpleLightEmitter { Type = sample.type, Sides = sample.sides, Length = sample.length, HalfWidth = sample.halfWidth, Angle = sample.angle,
					Data = new BpleLightCollision.LightData { Position0 = Vector(sample.lightPosition0), Position1 = Vector(sample.lightPosition1), Direction0 = Vector(sample.lightDirection0), Direction1 = Vector(sample.lightDirection1) } };
				var motion = new BpleLightCollision.CCDData { Position0 = Vector(sample.position0), Position1 = Vector(sample.position1), Direction0 = Vector(sample.direction0), Direction1 = Vector(sample.direction1), DeltaTime = .02f,
					Bounds = new BpleLightBounds { Type = (int)sample.bounds[0], A = sample.bounds[3], B = sample.bounds[4], R = sample.bounds[5] } };
				bool hit = collision.Cast(light, ref motion, out var result);
				bool equal = hit == sample.hit;
				if (hit && sample.hit)
				{
					float error = Mathf.Max(Mathf.Abs(result.TimeOfImpact - sample.time), Mathf.Abs(result.ContactSeparation - sample.separation),
						Vector2.Distance(result.ContactPoint, Vector(sample.contactPoint)), Vector2.Distance(result.ContactNormal, Vector(sample.contactNormal)),
						Vector2.Distance(result.RelativeVelocity, Vector(sample.relativeVelocity)));
					maximum = Mathf.Max(maximum, error); equal &= error < .0001f && result.ContactCount == sample.count;
				}
				if (!equal) failures++;
			}
			Check("Unity/native BPLE collision cases", failures == 0, "cases=" + fixtures.cases.Length + " mismatches=" + failures + " maxError=" + maximum);
		}
	}
}
#endif
