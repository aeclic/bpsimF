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

namespace Bpsim.QA
{
	public sealed partial class PartGameplayQA
	{
		private static string VariantResult(bool pass, string detail) => (pass ? "PASS " : "FAIL ") + detail;
		private static float2 CanopyScale(Entity part)
		{
			var em = PartGameplayUtil.Em;
			foreach (var child in em.GetBuffer<LinkedEntityGroup>(part))
				if (em.HasComponent<UmbrellaVisual>(child.Value) && em.HasComponent<PostTransformScale>(child.Value))
				{
					var scale = em.GetComponentData<PostTransformScale>(child.Value).Value;
					return new float2(scale.c0.x, scale.c1.y);
				}
			return float2.zero;
		}
		private static IEnumerator ReopenCanopy(Entity part)
		{
			yield return new UnityEngine.WaitForSeconds(.3f);
			PartSimulation.ProcessTouch(part);
		}
		private static void MoveVariant(Entity part, float3 position)
		{
			var em = PartGameplayUtil.Em;
			var transform = em.GetComponentData<LocalTransform>(part);
			transform.Position = position;
			em.SetComponentData(part, transform);
			em.SetComponentData(part, new LocalToWorld { Value = float4x4.TRS(position, transform.Rotation, new float3(transform.Scale)) });
			em.SetComponentData(part, default(PhysicsVelocity));
		}
		private IEnumerator VariantRoutine(bool unity)
		{
			var settings = UserSettings.Instance.SimulationSettings;
			var saved = new SimulationSettings(); saved.Update(settings);
			try
			{
				settings.SimulationType = unity ? SimulationType.UnityPhysics : SimulationType.HavokPhysics;
				settings.GravityX = settings.GravityY = 0;
				settings.CreateJoints = true; settings.MegaStructure = false; settings.InfiniteConnectionStrength = true;
				Line("variant backend=" + settings.SimulationType);
				settings.MegaStructure = true;
				foreach (int skin in new[] { 0, 1, 2, 3, 4 })
				{
					int index = skin;
					yield return Scenario("separator-mega-" + index, scene => {
						Place(scene, PartType.Kicker, index, 0, 0, 0); Place(scene, PartType.MetalFrame, 1, 1, 0, 0);
					}, TriggerFirst(PartType.Kicker, index), scene => {
						bool joined = PartGameplayUtil.ConnectedComponent(FindPart(PartType.Kicker,index)) == PartGameplayUtil.ConnectedComponent(FindPart(PartType.MetalFrame,1));
						return VariantResult(!MegaStructure.Active && settings.MegaStructure && joined == (index == 3),
							"mega requested: independent separator bodies, setting preserved, joined=" + joined);
					}, .06f);
				}
				settings.MegaStructure = false;
				for (int skin = 0; skin < 5; skin++)
				{
					int index = skin;
					yield return Scenario("separator-" + index, scene => {
						Place(scene, PartType.MetalFrame, 0, -1, 0, 0);
						Place(scene, PartType.Kicker, index, 0, 0, 0);
						Place(scene, PartType.MetalFrame, 1, 1, 0, 0);
					}, TriggerFirst(PartType.Kicker, index), scene => {
						Entity part = FindPart(PartType.Kicker, index);
						var behavior = (KickerBehavior)PartGameplayManager.Instance.Find(part);
						bool separate = PartGameplayUtil.ConnectedComponent(part) != PartGameplayUtil.ConnectedComponent(FindPart(PartType.MetalFrame, 0));
						return VariantResult(index == 3 ? !behavior.Fired && !separate && !BasePart.IsTriggerable(new PartTypeInfo(PartType.Kicker, 3))
							: behavior.Fired && separate && PartGameplayUtil.Simulator.Joints.Length == 0,
							"separator#" + index + " separated=" + separate + " " + behavior.Describe());
					}, .08f);
				}
				foreach (int index in new[] { 1, 2, 4 })
				{
					yield return Scenario("connector-" + index, scene => {
						Place(scene, PartType.Kicker, index, 0, 0, 0);
						Place(scene, PartType.MetalFrame, 1, 1, 0, 0);
					}, () => {
						Entity part = FindPart(PartType.Kicker, index);
						PartSimulation.ProcessTouch(part); TNTDamage.Flush();
						if (index != 1) MoveVariant(FindPart(PartType.MetalFrame, 1), new float3(4,0,0));
						PartSimulation.ProcessTouch(part);
					}, scene => {
						Entity part = FindPart(PartType.Kicker, index), other = FindPart(PartType.MetalFrame, 1);
						var behavior = (KickerBehavior)PartGameplayManager.Instance.Find(part);
						float distance = math.distance(PartVariantPhysics.Position(part), PartVariantPhysics.Position(other));
						bool active = behavior.Enabled && behavior.LinkCount > 0 && PartGameplayUtil.ConnectedComponent(part) == PartGameplayUtil.ConnectedComponent(other);
						PartSimulation.ProcessTouch(part); TNTDamage.Flush();
						return VariantResult(active && distance < (index == 1 ? 1.3f : 3.9f) && !behavior.Enabled && behavior.LinkCount == 0,
							"connector#" + index + " active=" + active + " distance=" + distance + " toggle off=" + !behavior.Enabled);
					}, .3f);
				}
				yield return Scenario("marker-teams", scene => {
					Place(scene, PartType.Kicker, 3, 0, 0, 0); Place(scene, PartType.MetalFrame, 0, 1, 0, 0);
					Place(scene, PartType.Kicker, 3, 10, 0, 4); Place(scene, PartType.MetalFrame, 1, 11, 0, 0);
					Place(scene, PartType.Kicker, 3, 20, 0, 1); Place(scene, PartType.MetalFrame, 2, 21, 0, 0);
				}, () => {}, scene => {
					Entity a = FindPart(PartType.MetalFrame, 0), b = FindPart(PartType.MetalFrame, 1), c = FindPart(PartType.MetalFrame, 2);
					var team = PartGameplayUtil.Em.GetComponentData<PartMarkerTeam>(b);
					return VariantResult(MarkerTeams.SameTeam(a,b) && !MarkerTeams.SameTeam(a,c) && team.Teams == 1 && team.Diagonal,
						"marker team masks and diagonal collision flag");
				}, .06f);
				foreach (bool diagonal in new[] { false, true })
				{
					yield return Scenario("marker-collision-" + diagonal, scene => {
						Place(scene,PartType.Kicker,3,-3,0,0); Place(scene,PartType.MetalFrame,0,-2,0,0);
						Place(scene,PartType.Kicker,3,3,0,diagonal ? 4 : 0); Place(scene,PartType.MetalFrame,1,2,0,0);
					}, () => {
						foreach (Entity part in PartGameplayUtil.Simulator.Parts)
							PartGameplayUtil.Em.SetComponentData(part,new PhysicsVelocity { Linear = new float3(PartVariantPhysics.Position(part).x < 0 ? 4 : -4,0,0) });
					}, scene => {
						float ax = PartVariantPhysics.Position(FindPart(PartType.MetalFrame,0)).x;
						float bx = PartVariantPhysics.Position(FindPart(PartType.MetalFrame,1)).x;
						return VariantResult(diagonal ? ax > bx : ax < bx, "actual marker collision diagonal=" + diagonal + " positions=" + ax + "," + bx);
					}, .9f);
				}
				foreach (int index in new[] { 1, 3 })
				{
					yield return Scenario("tracking-" + index, scene => {
						Place(scene, PartType.RedRocket, index, 0, 0, 0); Place(scene, PartType.MetalFrame, 0, -1, 0, 0);
						Place(scene, PartType.MetalFrame, 1, 10, 3, 0);
					}, TriggerFirst(PartType.RedRocket, index), scene => {
						var rocket = (LaunchBehavior)PartGameplayManager.Instance.Find(FindPart(PartType.RedRocket, index));
						// The MonoBehaviour controller may run before ECS integrates this render frame.
						bool movedUp = PartVariantPhysics.Position(rocket.Part).y > .01f
							|| (rocket.GuidanceImpulse.y > .1f && PartGameplayUtil.LinearVelocity(rocket.Part).y > .1f);
						bool tracked = rocket.Target == FindPart(PartType.MetalFrame, 1) && movedUp;
						PartSimulation.ProcessTouch(rocket.Part);
						return VariantResult(tracked && rocket.Target == Entity.Null, "tracking#" + index + " target acquired, steers upward, switch clears target=" + tracked);
					}, .12f);
					yield return Scenario("tracking-filter-" + index, scene => {
						Place(scene, PartType.RedRocket, index, 0, 0, 0); Place(scene, PartType.Kicker, 3, -1, 0, 0);
						Place(scene, PartType.Kicker, 3, 10, 0, 0); Place(scene, PartType.MetalFrame, 1, 11, 0, 0);
					}, TriggerFirst(PartType.RedRocket, index), scene => {
						var rocket = (LaunchBehavior)PartGameplayManager.Instance.Find(FindPart(PartType.RedRocket, index));
						return VariantResult(rocket.Target == Entity.Null && math.lengthsq(rocket.LastGuidanceForce) == 0, "tracking#" + index + " ignores marked allies");
					}, .1f);
				}
				yield return Scenario("tracking-selection", scene => {
					Place(scene,PartType.RedRocket,1,0,0,0); Place(scene,PartType.MetalFrame,0,-1,0,0);
					Place(scene,PartType.MetalFrame,1,10,0,0); Place(scene,PartType.MetalFrame,2,12,0,0);
				}, () => {}, scene => {
					var rocket = (LaunchBehavior)PartGameplayManager.Instance.Find(FindPart(PartType.RedRocket,1));
					Entity a = FindPart(PartType.MetalFrame,1), b = FindPart(PartType.MetalFrame,2);
					rocket.Trigger(); rocket.TrackingDistance = 5; rocket.Tick(.02f);
					bool range = rocket.Target == Entity.Null;
					rocket.TrackingDistance = 320; rocket.TrackingAngle = 30;
					MoveVariant(a,new float3(10,10,0)); MoveVariant(b,new float3(12,12,0)); rocket.Tick(.02f);
					bool angle = rocket.Target == Entity.Null;
					rocket.TrackingAngle = 360; MoveVariant(a,new float3(10,0,0)); rocket.Tick(.02f);
					bool acquired = rocket.Target == a && math.abs(math.length(rocket.LastGuidanceForce) - 200) < .01f;
					MoveVariant(b,new float3(9,3,0)); rocket.Tick(.02f); bool held = rocket.Target == a;
					MoveVariant(b,new float3(5,2,0)); rocket.Tick(.02f); bool switched = rocket.Target == b;
					MoveVariant(b,new float3(1000,0,0)); rocket.Tick(.02f); rocket.Tick(.02f);
					return VariantResult(range && angle && acquired && held && switched && rocket.Target == a,
						"tracking range=" + range + " FOV=" + angle + " force=" + acquired + " hysteresis=" + held + " switch=" + switched + " reacquire=" + (rocket.Target == a));
				}, .02f);
				foreach (int index in new[] { 1, 3 })
				{
					yield return Scenario("rocket-singleton-" + index, scene => Place(scene,PartType.RedRocket,index,0,0,0),
						TriggerFirst(PartType.RedRocket,index), scene => {
						var rocket = (LaunchBehavior)PartGameplayManager.Instance.Find(FindPart(PartType.RedRocket,index));
						return VariantResult(rocket.Target == Entity.Null && PartGameplayUtil.LinearVelocity(rocket.Part).x > 18,
							"singleton special rocket uses ordinary propulsion beyond 18m/s");
					}, .3f);
				}
				foreach (int index in new[] { 1, 3 })
				{
					yield return Scenario("avoidance-" + index, scene => {
						Place(scene, PartType.Rocket, index, 0, 0, 0); Place(scene, PartType.MetalFrame, 0, -1, 0, 0);
						Entity wall = CreateGround(0);
						var em = PartGameplayUtil.Em;
						var t = LocalTransform.FromPositionRotation(new float3(5,0,0), quaternion.RotateZ(math.PI / 2));
						em.SetComponentData(wall, t); em.SetComponentData(wall, new LocalToWorld { Value = float4x4.TRS(t.Position,t.Rotation,new float3(1)) });
					}, TriggerFirst(PartType.Rocket, index), scene => {
						var rocket = (LaunchBehavior)PartGameplayManager.Instance.Find(FindPart(PartType.Rocket, index));
						float3 velocity = PartGameplayUtil.LinearVelocity(rocket.Part);
						return VariantResult(index == 1 ? rocket.GuidanceImpulse.x < -.1f && velocity.x < -1
							: math.abs(rocket.GuidanceImpulse.y) > .1f && math.abs(velocity.y) > 1,
							"avoidance#" + index + " impulse=" + rocket.GuidanceImpulse + " velocity=" + velocity);
					}, .06f);
				}
				foreach (PartType type in new[] { PartType.Wings, PartType.MetalWing, PartType.Tailplane, PartType.MetalTail })
				{
					foreach (bool flipped in new[] { false, true })
					{
						yield return Scenario("aero-" + type + "-" + flipped, scene => {
							Entity prefab = PartManager.Instance.EntitySpawner.FindPrefab(new PartTypeInfo(type,0));
							scene.Unmanaged.PlacePartAt(new PartCreationRequest(prefab, scene.SceneID, new PartTypeInfo(type,0),0,0,0,flipped),false);
						}, () => PartGameplayUtil.Em.SetComponentData(FindPart(type,0),new PhysicsVelocity { Linear = new float3(8,-2,0) }), scene => {
							var wing = (WingBehavior)PartGameplayManager.Instance.Find(FindPart(type,0));
							bool live = math.all(math.isfinite(wing.LastForce)) && math.length(wing.LastForce) > .01f && wing.IsTail == (type == PartType.Tailplane || type == PartType.MetalTail);
							float damping = PartGameplayUtil.Em.GetComponentData<PhysicsDamping>(wing.Part).Linear;
							PartSimulation.ProcessTouch(wing.Part); wing.Tick(.02f);
							return VariantResult(live && damping == 1 && math.lengthsq(wing.LastForce) == 0, "aero " + type + " flipped=" + flipped + " independent curve, force, damping, toggle");
						}, .04f);
					}
				}
				foreach (bool flipped in new[] { false, true })
				{
					yield return Scenario("aero-rotated-wind-" + flipped, scene => {
						var info = new PartTypeInfo(PartType.MetalWing,0);
						scene.Unmanaged.PlacePartAt(new PartCreationRequest(PartManager.Instance.EntitySpawner.FindPrefab(info),scene.SceneID,info,0,0,1,flipped),false);
					}, () => {}, scene => {
						Entity part = FindPart(PartType.MetalWing,0); var em = PartGameplayUtil.Em;
						var wing = (WingBehavior)PartGameplayManager.Instance.Find(part);
						em.SetComponentData(part,new PhysicsVelocity { Linear = new float3(2,8,0) }); wing.Tick(.02f);
						// BPSS Euler(180,0,90) mirrors the chord to world -Y, giving 165.964 degrees.
						bool response = math.abs(wing.LastAngle - (flipped ? 165.96376f : 14.03624f)) < .01f
							&& (flipped ? wing.LastCoefficient < 0 : wing.LastCoefficient > 1.5f) && wing.LastForce.x < 0;
						em.SetComponentData(part,new PhysicsVelocity { Linear = new float3(2,8,0) });
						em.AddComponentData(part,new PartAirflow { Velocity = new float3(2,8,0) }); wing.Tick(.02f);
						return VariantResult(response && math.lengthsq(wing.LastForce) == 0 && math.lengthsq(em.GetComponentData<PartAirflow>(part).Velocity) == 0,
							"90 degree wing mirrored=" + flipped + " reference curve=" + response + "; matching wind cancels lift and is consumed");
					}, .02f);
				}
				for (int index = 0; index <= 6; index++)
				{
					int skin = index;
					bool anchorCorrection = false;
					string anchorDetail = "";
					yield return Scenario("umbrella-variant-" + skin, scene => {
						Place(scene, PartType.MetalFrame, 0, 0,-1,0); Place(scene, PartType.Umbrella,skin,0,0,0);
					}, () => {
						var canopy = (UmbrellaBehavior)PartGameplayManager.Instance.Find(FindPart(PartType.Umbrella,skin));
						if (skin == 3 || skin == 5) PartSimulation.SendButtonEvent(PartButtonEvent.CreateSliderEvent(canopy.Part, 30));
						else
						{
							var initial = new PhysicsVelocity { Linear = new float3(0,-3,0), Angular = new float3(0,0,.1f) };
							var motionRotation = math.mul(PartVariantPhysics.Rotation(canopy.Part), PartGameplayUtil.Em.GetComponentData<PhysicsMass>(canopy.Part).Transform.rot);
							initial.Angular = math.rotate(math.inverse(motionRotation), new float3(0,0,.1f));
							PartGameplayUtil.Em.SetComponentData(canopy.Part,initial);
							if (skin == 2 || skin == 4)
							{
								canopy.Tick(.02f);
								anchorDetail = " cancelV=" + PartGameplayUtil.LinearVelocity(canopy.Part) + " omega=" + PartVariantPhysics.AngularSpeed(canopy.Part);
								anchorCorrection = math.length(PartGameplayUtil.LinearVelocity(canopy.Part)) < .001f
									&& math.abs(PartVariantPhysics.AngularSpeed(canopy.Part) + .028f) < .001f;
								PartGameplayUtil.Em.SetComponentData(canopy.Part,initial);
							}
						}
					}, scene => {
						var canopy = (UmbrellaBehavior)PartGameplayManager.Instance.Find(FindPart(PartType.Umbrella,skin));
						bool sourceMode = canopy.Mode == skin && canopy.Open && math.all(math.isfinite(canopy.LastForce));
						// Anchors cap each velocity correction at 4, not the final velocity after joint solving.
						bool response = (skin == 2 || skin == 4)
							? anchorCorrection && math.length(PartVariantPhysics.Position(canopy.Part).xy) < .5f
								&& math.all(math.isfinite(PartGameplayUtil.LinearVelocity(canopy.Part)))
							: math.length(canopy.LastForce) > .01f;
						int sliders = 0; foreach(var slider in BasePart.GetSliderButtonInfo(PartGameplayUtil.Em.GetAspect<PartAspect>(canopy.Part))) sliders++;
						return VariantResult(sourceMode && response && sliders == (skin == 3 || skin == 5 ? 1 : 0), "umbrella#" + skin + " response=" + response + anchorDetail + " sliders=" + sliders + " " + canopy.Describe());
					}, .06f);
				}
				yield return Scenario("umbrella-fold", BuildUmbrellaScene, TriggerFirst(PartType.Umbrella,0), scene => {
					var canopy = (UmbrellaBehavior)PartGameplayManager.Instance.Find(FindPart(PartType.Umbrella,0));
					return VariantResult(!canopy.Open && !canopy.Enabled && math.distance(CanopyScale(canopy.Part), new float2(.4f,2)) < .001f,
						"canopy closes after 0.25 seconds, visual folded, stroke stops; scale=" + CanopyScale(canopy.Part));
				}, .3f);
				yield return Scenario("umbrella-reopen", BuildUmbrellaScene, () => {
					Entity part = FindPart(PartType.Umbrella,0); PartSimulation.ProcessTouch(part); StartCoroutine(ReopenCanopy(part));
				}, scene => {
					var canopy = (UmbrellaBehavior)PartGameplayManager.Instance.Find(FindPart(PartType.Umbrella,0));
					return VariantResult(canopy.Open && !canopy.Enabled && math.distance(CanopyScale(canopy.Part),new float2(1)) < .001f,
						"canopy reopens, visual full scale=" + CanopyScale(canopy.Part));
				}, .65f);
				float3 blastVelocity = float3.zero, offAxisVelocity = float3.zero;
				yield return Scenario("timebomb-direction", scene => {
					Place(scene,PartType.TimeBomb,0,0,0,0); Place(scene,PartType.MetalFrame,0,0,-3,0); Place(scene,PartType.MetalFrame,1,3,0,0);
				}, () => {
					PartSimulation.ProcessTouch(FindPart(PartType.TimeBomb,0));
					blastVelocity = PartGameplayUtil.LinearVelocity(FindPart(PartType.MetalFrame,0));
					offAxisVelocity = PartGameplayUtil.LinearVelocity(FindPart(PartType.MetalFrame,1));
				}, scene => VariantResult(FindPart(PartType.TimeBomb,0) == Entity.Null && blastVelocity.y < -10
					&& math.length(offAxisVelocity) < .01f && PartVariantPhysics.Position(FindPart(PartType.MetalFrame,0)).y < -3.1f,
					"TimeBomb immediate, removed, directional cone; blast=" + blastVelocity + " offAxis=" + offAxisVelocity), .04f);
			}
			finally { settings.Update(saved); }
		}
	}
}
#endif
