#if UNITY_EDITOR
using System.Collections;
using Bpsim.Parts;
using Bpsim.Parts.Gameplay;
using Bpsim.Parts.Simulation;
using Bpsim.UI;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

namespace Bpsim.QA
{
	public sealed partial class PartGameplayQA
	{
		private static string TntResult(bool passed, string detail) => (passed ? "PASS " : "FAIL ") + detail;
		private static TNTBehavior Charge(int skin) => PartGameplayManager.Instance.Find(FindPart(PartType.TNT, skin)) as TNTBehavior;

		private IEnumerator TntFamilyRoutine(bool unityPhysics)
		{
			SimulationSettings settings = UserSettings.Instance.SimulationSettings;
			var saved = new SimulationSettings();
			saved.Update(settings);
			try
			{
				settings.SimulationType = unityPhysics ? SimulationType.UnityPhysics : SimulationType.HavokPhysics;
				Line("TNT backend=" + settings.SimulationType);
				settings.GravityX = settings.GravityY = 0f;
				settings.MegaStructure = false;
				settings.TNTExplosionForce = settings.AlienTNTExplosionForce = 2f;
				settings.TNTExplosionRadius = settings.AlienTNTExplosionRadius = 1f;
				settings.AlienTNTExplosionCoolingTime = 0.1f;
				settings.AlienTNTTriggerGun = true;
				settings.BlasterTNTDestroy = false;
				foreach (var test in new (int skin, int rotation, int x, int y, float sign)[] {
					(0,0,3,0,1f),
					(1,0,0,-3,-1f), (1,1,3,0,1f),
					(2,0,3,0,-1f), (2,2,3,0,1f), (2,4,3,0,1f),
					(4,0,3,0,1f), (4,2,3,0,-1f), (4,4,3,0,-1f),
					(5,0,3,0,1f), (5,4,3,0,-1f) })
				{
					TNTBehavior charge = null;
					Entity source = Entity.Null;
					float3 impulseVelocity = float3.zero;
					bool hud = false;
					yield return Scenario("tnt-direction-" + test.skin + "-" + test.rotation,
						scene => { Place(scene, PartType.TNT, test.skin, 0, 0, test.rotation); Place(scene, PartType.MetalFrame, 0, test.x, test.y, 0); },
						() => {
							charge = Charge(test.skin); source = charge.Part;
							hud = PartButtonList.IsTntButtonHidden(source) == (test.skin == 1);
							PartSimulation.ProcessTouch(source);
							impulseVelocity = PartGameplayUtil.LinearVelocity(FindPart(PartType.MetalFrame, 0));
						},
						scene => TntResult(charge != null && charge.Detonations == 1 && hud
							&& (test.y == 0 ? impulseVelocity.x : impulseVelocity.y) * test.sign > 1f
							&& PartGameplayUtil.Alive(source) == (test.skin == 5),
							"skin=" + test.skin + " rotation=" + test.rotation + " deltaV=" + impulseVelocity + " hud=" + hud + " " + charge?.Describe()), 0.08f);
				}

				TNTBehavior epic = null;
				bool touchIgnored = false, thresholdHeld = false;
				float coneVelocity = 0f;
				yield return Scenario("tnt-epic-cone", scene => {
					Place(scene, PartType.TNT, 3, 0, 0, 0); Place(scene, PartType.MetalFrame, 0, 0, 1, 0);
				}, () => {
					epic = Charge(3); epic.Trigger();
					touchIgnored = epic.Detonations == 0 && PartButtonList.IsTntButtonHidden(epic.Part);
					epic.OnImpact(240f); thresholdHeld = epic.Detonations == 0;
					epic.OnImpact(241f);
					coneVelocity = PartGameplayUtil.LinearVelocity(FindPart(PartType.MetalFrame, 0)).y;
				}, scene => TntResult(touchIgnored && thresholdHeld && epic.Detonations == 1 && coneVelocity > 100f,
					"touchIgnored=" + touchIgnored + " threshold=" + thresholdHeld + " up=" + coneVelocity), 0.06f);
				yield return Scenario("tnt-epic-dud", scene => {
					Place(scene, PartType.TNT, 3, 0, 0, 0); Place(scene, PartType.MetalFrame, 0, 0, 1, 0);
				}, () => {
					epic = Charge(3); PartGameplayUtil.AddVelocity(epic.Part, new float3(13, 0, 0)); epic.Tick(0.02f);
					coneVelocity = PartGameplayUtil.LinearVelocity(FindPart(PartType.MetalFrame, 0)).y;
				}, scene => TntResult(epic.Dud && epic.Detonations == 1 && math.abs(coneVelocity) < 0.01f,
					"dud=" + epic.Dud + " targetV=" + coneVelocity), 0.06f);

				settings.TNTExplosionForce = settings.AlienTNTExplosionForce = 0f;
				TNTBehavior burning = null; bool diagonalBoundary = false, orientation = false;
				UnityEngine.Random.State randomState = UnityEngine.Random.state;
				yield return Scenario("tnt-diagonal-burn", scene => {
					Place(scene, PartType.TNT, 0, 0, 0, 4);
					for (int x = 2; x <= 6; x++) Place(scene, PartType.WoodenFrame, 0, x, 0, 0);
				}, () => {
					burning = Charge(0); var em = PartGameplayUtil.Em;
					orientation = math.dot(math.mul(PartGameplayUtil.Rotation(burning.Part), new float3(1,0,0)), new float3(1,0,0)) > 0.999f;
					bool visual = false; var linked = em.GetBuffer<LinkedEntityGroup>(burning.Part);
					for (int i = 0; i < linked.Length; i++) if (em.HasComponent<TNTVisualization>(linked[i].Value))
						visual |= math.abs(math.mul(em.GetComponentData<LocalTransform>(linked[i].Value).Rotation, new float3(1,0,0)).y - 0.70710678f) < 0.001f;
					orientation &= visual;
					burning.OnImpact(96f); diagonalBoundary = burning.Detonations == 0;
					UnityEngine.Random.InitState(42); burning.OnImpact(97f);
				}, scene => TntResult(orientation && diagonalBoundary && burning.BurntJoints > 0,
					"orthogonalCollider/tiltedArt=" + orientation + " threshold=" + diagonalBoundary + " burntJoints=" + burning.BurntJoints), 0.08f);
				UnityEngine.Random.state = randomState;

				Entity generated = Entity.Null;
				yield return Scenario("tnt-generated", scene => Place(scene, PartType.MetalFrame, 0, -5, 0, 0), () => {
					generated = PartGameplayUtil.SpawnPart(new PartTypeInfo(PartType.TNT, 6), 0, 0, 4, false, ref PartGameplayUtil.Simulator);
				}, scene => {
					var charge = PartGameplayManager.Instance.Find(generated) as TNTBehavior;
					bool registered = charge != null && charge.Released;
					charge?.Trigger();
					return TntResult(registered && charge.Detonations == 1, "generated diagonal blaster registered=" + registered);
				}, 0.08f);

				float radiusVelocity = 0f;
				settings.TNTExplosionRadius = 2f;
				settings.TNTExplosionForce = 2f;
				yield return Scenario("tnt-radius-setting", scene => {
					Place(scene, PartType.TNT, 0, 0, 0, 0); Place(scene, PartType.MetalFrame, 0, 12, 0, 0);
				}, () => { Charge(0).Trigger(); radiusVelocity = PartGameplayUtil.LinearVelocity(FindPart(PartType.MetalFrame, 0)).x; },
					scene => TntResult(radiusVelocity > 1f, "radius multiplier reaches 12m; deltaV=" + radiusVelocity), 0.06f);
				settings.TNTExplosionRadius = 1f;
				settings.TNTExplosionForce = 0f;
				foreach (int skin in new[] { 0, 1, 2, 4, 5, 6 })
				{
					TNTBehavior charge = null; bool lowHeld = false;
					float threshold = skin == 0 ? 4f : skin == 2 || skin == 4 ? 96f : skin == 6 ? 32f : float.PositiveInfinity;
					yield return Scenario("tnt-impact-threshold-" + skin, scene => Place(scene, PartType.TNT, skin, 0, 0, 0), () => {
						charge = Charge(skin); if (skin == 6) charge.Trigger();
						charge.OnImpact(threshold); lowHeld = charge.Detonations == 0;
						charge.OnImpact(float.IsInfinity(threshold) ? 1000f : threshold + 1f);
					}, scene => TntResult(lowHeld && charge.Detonations == (skin == 1 || skin == 5 ? 0 : 1),
						"skin=" + skin + " boundaryHeld=" + lowHeld + " detonations=" + charge.Detonations), 0.06f);
				}

				TNTBehavior alien = null;
				bool cooling = false;
				yield return Scenario("tnt-alien-cooldown-gun", scene => {
					Place(scene, PartType.TNT, 5, 0, 0, 0); Place(scene, PartType.GrapplingHook, 5, 1, 0, 0);
				}, () => { alien = Charge(5); alien.Trigger(); alien.Trigger(); cooling = alien.Detonations == 1; }, scene => {
					var gun = PartGameplayManager.Instance.Find(FindPart(PartType.GrapplingHook, 5));
					bool fired = gun != null && gun.Describe().Contains("shots=1");
					alien.Trigger();
					return TntResult(cooling && alien.Detonations == 2 && !alien.Spent && fired,
						"cooldown=" + cooling + " repeated=" + alien.Detonations + " gun=" + gun?.Describe());
				}, 0.3f);

				foreach (int sourceSkin in new[] { 0, 4, 5, -1 })
				{
					TNTBehavior[] charges = null;
					yield return Scenario("tnt-chain-" + sourceSkin, scene => {
						Place(scene, PartType.TNT, sourceSkin < 0 ? 0 : sourceSkin, 0, 0, 0);
						for (int skin = 0; skin < 7; skin++) Place(scene, PartType.TNT, skin, skin + 1, 1, 0);
					}, () => {
						charges = new TNTBehavior[7];
						var list = PartGameplayUtil.Simulator.Parts;
						// Keep references to the row, including the second copy of the source skin.
						for (int i = 0; i < list.Length; i++) {
							var grid = PartGameplayUtil.Em.GetComponentData<PartTransform>(list[i]);
							if (grid.Y == 1) charges[PartGameplayUtil.TypeInfo(list[i]).PartIndex] = PartGameplayManager.Instance.Find(list[i]) as TNTBehavior;
						}
						if (sourceSkin < 0) TNTBehavior.FromProjectile(new float3(4, 1, 0), 8f);
						else Charge(sourceSkin).Trigger();
					}, scene => {
						bool ok = true; string counts = "";
						for (int skin = 0; skin < 7; skin++) {
							bool expected = sourceSkin == -1 ? skin == 0 || skin == 1 || skin == 2 || skin == 5
								: sourceSkin == 5 ? false : skin == 0 || sourceSkin == 4 && skin == 4;
							ok &= charges[skin] != null && charges[skin].Detonations == (expected ? 1 : 0);
							counts += skin + ":" + charges[skin]?.Detonations + " ";
						}
						return TntResult(ok, "source=" + sourceSkin + " counts=" + counts);
					}, 0.08f);
				}

				TNTBehavior wave = null, peer = null;
				bool releaseOnly = false;
				yield return Scenario("tnt-blaster-release-chain-range", scene => {
					Place(scene, PartType.TNT, 6, 0, 0, 0); Place(scene, PartType.TNT, 6, 4, 0, 4);
					Place(scene, PartType.MetalFrame, 0, 12, 0, 0);
				}, () => {
					wave = Charge(6);
					var parts = PartGameplayUtil.Simulator.Parts;
					for (int i = 0; i < parts.Length; i++) if (PartGameplayUtil.TypeInfo(parts[i]).PartType == PartType.TNT
						&& parts[i] != wave.Part) peer = PartGameplayManager.Instance.Find(parts[i]) as TNTBehavior;
					wave.OnImpact(100f); wave.Trigger(); releaseOnly = wave.Detonations == 0 && wave.Released && peer.Released;
					wave.Trigger();
				}, scene => TntResult(releaseOnly && wave.Detonations == 1 && peer.Detonations == 1 && PartGameplayUtil.Alive(wave.Part)
					&& PartButtonList.IsTntButtonHidden(wave.Part) && PartGameplayUtil.Position(FindPart(PartType.MetalFrame, 0)).x > 12.05f,
					"releaseOnly=" + releaseOnly + " chain=" + peer?.Detonations + " distantX=" + PartGameplayUtil.Position(FindPart(PartType.MetalFrame, 0)).x), 0.35f);

				settings.BlasterTNTDestroy = true;
				foreach (bool mega in new[] { false, true })
				{
					settings.MegaStructure = mega;
					bool collisionDisabled = false;
					yield return Scenario("tnt-blaster-destroy-mega-" + mega, scene => {
						Place(scene, PartType.TNT, 6, 0, 0, 4); Place(scene, PartType.MetalFrame, 0, 0, -1, 0);
					}, () => { wave = Charge(6); wave.Trigger(); }, scene => {
						collisionDisabled = PartGameplayUtil.Alive(wave.Part) && !PartGameplayUtil.Em.HasComponent<PhysicsWorldIndex>(wave.Part)
							&& !PartGameplayUtil.Em.HasComponent<MegaPartOffset>(wave.Part);
						return TntResult(collisionDisabled, "hidden wave has no physics body; mega=" + mega);
					}, 0.1f);
					float bodyY = 0f;
					yield return Scenario("tnt-blaster-push-mega-" + mega, scene => {
						Place(scene, PartType.TNT, 6, 0, 0, 4); Place(scene, PartType.MetalFrame, 0, 0, -1, 0);
					}, () => { wave = Charge(6); wave.Trigger(); }, scene => {
						Entity target = FindPart(PartType.MetalFrame, 0);
						bodyY = PartGameplayUtil.Position(target).y;
						return TntResult(bodyY < -1.05f, "targetY=" + bodyY + " targetV=" + PartGameplayUtil.LinearVelocity(target)
							+ " invMass=" + PartGameplayUtil.Em.GetComponentData<PhysicsMass>(target).InverseMass
							+ " bodyV=" + (MegaStructure.Active ? PartGameplayUtil.LinearVelocity(MegaStructure.Body).ToString() : "none"));
					}, 0.35f);
					// Run a separate full-lifetime case; Scenario always returns to the build scene.
					yield return Scenario("tnt-blaster-retire-mega-" + mega, scene => {
						Place(scene, PartType.TNT, 6, 0, 0, 4); Place(scene, PartType.MetalFrame, 0, 0, -1, 0);
					}, () => { wave = Charge(6); wave.Trigger(); }, scene => TntResult(!PartGameplayUtil.Alive(wave.Part)
						&& PartGameplayUtil.Simulator.Parts.Length == 1, "removed after wave; parts=" + PartGameplayUtil.Simulator.Parts.Length), 2.2f);
				}

				settings.MegaStructure = false;
				settings.BlasterTNTDestroy = false;
				foreach (bool mega in new[] { false, true })
				{
					settings.MegaStructure = mega;
					TNTBehavior impact = null;
					yield return Scenario("tnt-real-ground-contact-mega-" + mega, scene => { Place(scene, PartType.TNT, 0, 0, 2, 0); CreateGround(0f); },
						() => {
							impact = Charge(0);
							float inverseMass = PartGameplayUtil.Em.GetComponentData<PhysicsMass>(impact.Part).InverseMass;
							TNTImpulse.Apply(impact.Part, new float3(0, -10f / inverseMass, 0));
						},
						scene => TntResult(impact.Detonations == 1 && !PartGameplayUtil.Alive(impact.Part), "physics contact: " + impact.Describe()), 0.35f);
				}

				foreach (bool mega in new[] { false, true })
				{
					settings.MegaStructure = mega;
					TNTBehavior charge = null; Entity[] children = null; bool connected = false;
					yield return Scenario("tnt-topology-mega-" + mega, scene => {
						Place(scene, PartType.MetalFrame, 0, 0, 0, 0); Place(scene, PartType.TNT, 0, 0, 0, 0, true);
					}, () => {
						charge = Charge(0); var em = PartGameplayUtil.Em;
						connected = mega ? MegaStructure.Active : PartGameplayUtil.Simulator.Joints.Length > 0;
						var group = em.GetBuffer<LinkedEntityGroup>(charge.Part); children = new Entity[group.Length];
						for (int i = 0; i < group.Length; i++) children[i] = group[i].Value;
						charge.Trigger();
					}, scene => {
						bool gone = true; foreach (Entity child in children) gone &= !PartGameplayUtil.Alive(child);
						bool grid = PartGameplayUtil.Simulator.PartMap.TryGet(0, 0, PartBuildLayer.MinLayer, out var cell)
							&& cell.PartContainer != Entity.Null && cell.Part == Entity.Null
							&& PartGameplayUtil.Em.GetComponentData<ContainedPart>(cell.PartContainer).Value == Entity.Null;
						return TntResult(connected && gone && grid && PartGameplayUtil.Simulator.Parts.Length == 1
							&& PartGameplayUtil.Simulator.Joints.Length == 0, "connected=" + connected + " linkedGone=" + gone + " framePreserved=" + grid);
					}, 0.12f);
				}
			}
			finally
			{
				if (PartManager.Instance.IsSimulating) PartManager.Instance.EndSimulation();
				DestroyGround();
				settings.Update(saved);
			}
		}
	}
}
#endif
