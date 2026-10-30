#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using Bpsim.Parts;
using Bpsim.Parts.Gameplay;
using Bpsim.Parts.Simulation;
using Bpsim.UI;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;

namespace Bpsim.QA
{
	public sealed partial class PartGameplayQA
	{
		private static string OtherResult(bool ok, string detail) => (ok ? "PASS " : "FAIL ") + detail;

		private IEnumerator OtherPartsRoutine(bool unityPhysics)
		{
			var settings = UserSettings.Instance.SimulationSettings;
			var saved = new SimulationSettings();
			saved.Update(settings);
			try
			{
				settings.SimulationType = unityPhysics ? SimulationType.UnityPhysics : SimulationType.HavokPhysics;
				settings.GravityX = settings.GravityY = 0f;
				settings.MegaStructure = false;
				Line("other parts backend=" + settings.SimulationType);
				// Enumerate actual templates: startup and HUD coverage include every available skin,
				// including passive families which deliberately have no managed behavior.
				var sprites = PartButtonList.BuildSpriteInfoMap(null);
				for (int id = 1; id < (int)PartType.Max; id++)
				{
					var type = (PartType)id;
					if (type == PartType.TNT || !PartManager.Instance.Factory.PartCollection.TryFindParts(type, out var entry)) continue;
					var indices = new List<int>(entry.Data.Keys);
					yield return Scenario("skins-" + type, scene => {
						for (int i = 0; i < indices.Count; i++) Place(scene, type, indices[i], i % 16 * 5, i / 16 * 5, 0);
					}, () => {}, scene => {
						var missing = new List<string>();
						int buttons = 0;
						foreach (int index in indices)
						{
							Entity part = FindPart(type, index);
							if (!PartGameplayUtil.Alive(part) || !math.all(math.isfinite(PartGameplayUtil.Position(part))))
							{ missing.Add(index + ":entity"); continue; }
							var info = new PartTypeInfo(type, index);
							if (!BasePart.IsTriggerable(info)) continue;
							foreach (var button in BasePart.GetTriggerButtonInfo(PartGameplayUtil.Em.GetAspect<PartAspect>(part)))
							{
								buttons++;
								if (!PartButtonList.HasSprite(sprites, button.Value)) missing.Add(index + ":icon");
							}
						}
						return OtherResult(missing.Count == 0, type + " skins=" + indices.Count + " HUD=" + buttons + " missing=" + string.Join(",", missing));
					}, 0.06f);
				}

				// These are source-confirmed interactive families; checking HUD eligibility explicitly
				// catches the gap which direct ProcessTouch-only tests previously missed.
				foreach (var type in new[] { PartType.PointLight, PartType.SpotLight, PartType.Umbrella,
					PartType.PoweredUmbrella, PartType.SpringBoxingGlove, PartType.Bellows,
					PartType.Balloon, PartType.Balloons2, PartType.Balloons3, PartType.Pumpkin,
					PartType.Kicker, PartType.Wings, PartType.Tailplane, PartType.MetalWing, PartType.MetalTail })
				{
					var entry = PartManager.Instance.Factory.PartCollection.FindParts(type);
					int eligible = 0;
					foreach (int index in entry.Data.Keys) if (BasePart.IsTriggerable(new PartTypeInfo(type, index))) eligible++;
					int expected = entry.Data.Count - (type == PartType.Kicker ? 1 : 0);
					Line("   result: " + OtherResult(eligible == expected, "HUD access " + type + " " + eligible + "/" + expected));
				}

				for (int rotation = 0; rotation < 8; rotation++)
				{
					int mode = rotation;
					yield return Scenario("fuel-orientation-" + mode, scene => Place(scene, PartType.JetEngine, 4, 0, 0, mode), () => {}, scene => {
						Entity part = FindPart(PartType.JetEngine, 4);
						var fuel = PartGameplayUtil.Em.GetComponentData<FuelContainer>(part);
						bool infinite = mode == 2 || mode == 3 || mode == 5 || mode == 6;
						float expected = infinite ? 1E10f : 4f;
						PartGameplayUtil.TryGetGrid(part, out _, out _, out int actualMode, out _);
						return OtherResult(actualMode == mode && fuel.Capacity == expected && (fuel.Infinite != 0) == infinite,
							"fuel rotation=" + actualMode + " capacity=" + fuel.Capacity + " infinite=" + fuel.Infinite);
					}, 0.06f);
				}

				bool wheelOn = false, wheelOff = false;
				yield return Scenario("smallwheel-motor", scene => {
					Place(scene, PartType.Engine, 0, -1, 0, 0);
					Place(scene, PartType.SmallWheel, 7, 0, 0, 0);
					CreateGround(-0.5f);
				}, () => {
					Entity part = FindPart(PartType.SmallWheel, 7);
					PartSimulation.ProcessTouch(part);
					wheelOn = PartGameplayUtil.Em.GetComponentData<Wheel>(part).Enabled;
				}, scene => {
					Entity part = FindPart(PartType.SmallWheel, 7);
					var wheel = PartGameplayUtil.Em.GetComponentData<Wheel>(part);
					PartSimulation.ProcessTouch(part);
					wheelOff = !PartGameplayUtil.Em.GetComponentData<Wheel>(part).Enabled;
					return OtherResult(wheelOn && wheelOff && wheel.Kind == WheelKind.Motor && wheel.Throttle > 0f
						&& BasePart.IsTriggerable(new PartTypeInfo(PartType.SmallWheel, 7))
						&& !BasePart.IsTriggerable(new PartTypeInfo(PartType.SmallWheel, 6)),
						"SmallWheel#7 on/off=" + wheelOn + "/" + wheelOff + " throttle=" + wheel.Throttle + " power=" + wheel.PowerFactor);
				}, 0.6f);

				foreach (var test in new[] { (PartType.Rocket, 0), (PartType.CokeBottle, 4),
					(PartType.PointLight, 2), (PartType.JetEngine, 4), (PartType.JetEngine, 5),
					(PartType.SmallWheel, 7), (PartType.Egg, 1), (PartType.GrapplingHook, 6) })
				{
					Entity generated = Entity.Null;
					bool activation = false;
					AutoGunProjectileSystem.ResetProbes();
					yield return Scenario("generated-" + test.Item1 + "-" + test.Item2,
						scene => Place(scene, PartType.MetalFrame, 0, -30, 0, 0), () => {
							generated = PartGameplayUtil.SpawnPart(new PartTypeInfo(test.Item1, test.Item2), 0, 0, 0, false, ref PartGameplayUtil.Simulator);
							StartCoroutine(ActivateGenerated(generated, value => activation = value));
						}, scene => {
							var manager = PartGameplayManager.Instance;
							bool isolated = PartGameplayUtil.ConnectedComponent(generated) != PartGameplayUtil.ConnectedComponent(FindPart(PartType.MetalFrame, 0));
						isolated &= PartCollisionFilter.CountMismatches(PartGameplayUtil.Em, PartGameplayUtil.Simulator) == 0;
							bool effect = activation;
							if (test.Item1 == PartType.Rocket || test.Item1 == PartType.CokeBottle)
								effect &= PartGameplayUtil.Position(generated).x > 1f;
							if (test.Item1 == PartType.JetEngine && test.Item2 == 4)
								effect &= PartGameplayUtil.Em.HasComponent<FuelContainer>(generated);
							if (test.Item1 == PartType.JetEngine && test.Item2 == 5)
								effect &= PartGameplayUtil.Em.GetComponentData<FuelTubeLink>(generated).Open == 0;
							if (test.Item1 == PartType.GrapplingHook)
								effect &= AutoGunProjectileSpawner.SpawnCount > 0;
							return OtherResult(effect && isolated && manager.HasBehavior(generated),
								"generated " + test.Item1 + "#" + test.Item2 + " activated=" + activation + " isolated=" + isolated + " " + manager.Find(generated)?.Describe());
						}, 0.65f);
				}

				// Exercise the actual generator path, including deferred initialization of a skin
				// whose Init adds components (the old TNT-only registration left this tank inert).
				yield return Scenario("generator-fuelbox", scene => {
					Place(scene, PartType.JetEngine, 4, -1, 0, 2);
					Place(scene, PartType.GrapplingHook, 8, 0, 0, 0);
				}, TriggerFirst(PartType.GrapplingHook, 8), scene => {
					Entity copy = Entity.Null;
					foreach (Entity part in PartGameplayUtil.Simulator.Parts)
						if (PartGameplayUtil.Em.GetComponentData<PartTransform>(part).X == 9) copy = part;
					bool valid = PartGameplayUtil.Alive(copy) && PartGameplayManager.Instance.Find(copy) is FuelBoxBehavior
						&& PartGameplayUtil.Em.GetComponentData<FuelContainer>(copy).Infinite != 0;
					return OtherResult(valid, "generator copies fuel-box mode and initializes container");
				}, 0.1f);

				for (int index = 1; index <= 4; index++)
				{
					int skin = index;
					yield return Scenario("single-generator-" + skin, scene => {
						Place(scene, PartType.WoodenFrame, 0, 0, 0, 0);
						Place(scene, PartType.GrapplingHook, skin, 0, 0, 0, true);
						Place(scene, PartType.JetEngine, 4, -1, 0, 0);
					}, TriggerFirst(PartType.GrapplingHook, skin), scene => {
						Entity copy = Entity.Null;
						foreach (Entity part in PartGameplayUtil.Simulator.Parts)
							if (PartGameplayUtil.Em.GetComponentData<PartTransform>(part).X == skin + 3) copy = part;
						bool correct = PartGameplayUtil.Alive(copy) && PartGameplayManager.Instance.Find(copy) is FuelBoxBehavior
							&& PartGameplayManager.Instance.Find(FindPart(PartType.GrapplingHook, skin)).Kind == PartGameplayKind.Generator;
						return OtherResult(correct, "enclosed hook#" + skin + " copies template at distance " + (skin + 3));
					}, 0.1f);
				}
			}
			finally { settings.Update(saved); }
		}

		private static IEnumerator ActivateGenerated(Entity part, System.Action<bool> result)
		{
			yield return new WaitForFixedUpdate();
			yield return new WaitForFixedUpdate();
			var behavior = PartGameplayManager.Instance.Find(part);
			if (behavior == null) { result(false); yield break; }
			bool before = behavior.Enabled;
			PartSimulation.ProcessTouch(part);
			result(behavior is FuelBoxBehavior || behavior.Enabled != before);
		}
	}
}
#endif
