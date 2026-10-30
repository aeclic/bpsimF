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

namespace Bpsim.QA
{
	/// <summary>
	/// Play-mode acceptance harness for the reconstructed part behaviours
	/// (GrapplingHook / AutoGun / MultipartGenerator / AutoControlLight).
	/// Drive from the DshBridge:
	///   invoke_static type=Bpsim.QA.PartGameplayQA method=Run args=["all"]
	///   invoke_static type=Bpsim.QA.PartGameplayQA method=Status args=[]
	/// </summary>
	public sealed partial class PartGameplayQA : MonoBehaviour
	{
		private static PartGameplayQA s_instance;

		private static readonly StringBuilder s_report = new StringBuilder();

		private static string s_status = "idle";

		private static bool s_running;

		/// <summary>
		/// 轮子场景用的静态地面实体。它不是部件（不进 <c>simulator.Parts</c>），
		/// 场景切换时不会被 <c>PartScene</c> 销毁，所以要自己管生命周期。
		/// </summary>
		private static Entity s_ground;

		public static string LogPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "bpsim-gameplayqa.log"));

		private static PartGameplayQA Ensure()
		{
			if (s_instance != null)
			{
				return s_instance;
			}
			GameObject gameObject = new GameObject("[PartGameplayQA]");
			gameObject.hideFlags = HideFlags.DontSave;
			UnityEngine.Object.DontDestroyOnLoad(gameObject);
			s_instance = gameObject.AddComponent<PartGameplayQA>();
			return s_instance;
		}

		public static string Run(string scenario)
		{
			if (s_running)
			{
				return "busy: " + s_status;
			}
			s_running = true;
			s_status = "running:" + scenario;
			s_report.Length = 0;
			PartGameplayQA runner = Ensure();
			runner.StartCoroutine(runner.RunScenario(string.IsNullOrEmpty(scenario) ? "all" : scenario));
			return "started: " + scenario;
		}

		public static string Status()
		{
			return s_status + "\n" + s_report;
		}

		public static string Reset()
		{
			s_running = false;
			s_status = "idle";
			s_report.Length = 0;
			return "reset";
		}

		public static string Snapshot()
		{
			PartGameplayManager manager = PartGameplayManager.Instance;
			if (manager == null)
			{
				return "no manager";
			}
			StringBuilder stringBuilder = new StringBuilder(manager.Describe());
			PartManager partManager = PartManager.Instance;
			if (partManager != null && partManager.PartSimulator != null)
			{
				PartSimulatorUnmanaged simulator = partManager.PartSimulator.Unmanaged;
				stringBuilder.Append("\nparts=").Append(simulator.Parts.Length).Append(" joints=").Append(simulator.Joints.Length);
				for (int i = 0; i < simulator.Parts.Length; i++)
				{
					Entity entity = simulator.Parts[i];
					EntityManager entityManager = PartGameplayUtil.Em;
					if (entityManager.Exists(entity) && entityManager.HasComponent<PartTypeValue>(entity))
					{
						PartTypeValue type = entityManager.GetComponentData<PartTypeValue>(entity);
						float3 position = PartGameplayUtil.Position(entity);
						stringBuilder.Append("\n  ").Append(i).Append(' ').Append(type.Type).Append('#').Append(type.Index)
							.Append(" @(").Append(position.x.ToString("F2")).Append(',').Append(position.y.ToString("F2")).Append(')');
					}
				}
			}
			return stringBuilder.ToString();
		}

		private IEnumerator RunScenario(string scenario)
		{
			try
			{
				Line("=== part gameplay QA: " + scenario + " @ " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
				Line("env: playing=" + Application.isPlaying + " simulating=" + (PartManager.Instance != null && PartManager.Instance.IsSimulating));
				yield return null;
				if (scenario == "tntfamily" || scenario == "tntfamilyunity") yield return TntFamilyRoutine(scenario == "tntfamilyunity");
				if (scenario == "otherparts" || scenario == "otherpartsunity") yield return OtherPartsRoutine(scenario == "otherpartsunity");
				if (scenario == "variants" || scenario == "variantsunity") yield return VariantRoutine(scenario == "variantsunity");
				if (scenario == "hook" || scenario == "all")
				{
					yield return Scenario("hook", BuildHookScene, TriggerFirst(PartType.GrapplingHook, 0), VerifyHook);
				}
				if (scenario == "manualgun" || scenario == "all")
				{
					yield return Scenario("manualgunhit", BuildManualGunScene, TriggerManualGun, VerifyManualGunHit, 0.4f);
					yield return Scenario("manualgunfree", BuildManualGunFreeScene, TriggerManualGun, VerifyManualGunFree, 1.4f);
				}
				if (scenario == "gun" || scenario == "all")
				{
					// 机炮必须打真弹丸：生成实体 → 飞一段 → 撞到目标爆炸。
					// 弹丸初速 20 m/s，目标在 4.5 格外，0.3 s 内就到；给 0.9 s 让爆炸发生。
					yield return Scenario("gun", BuildGunScene, TriggerGun(), VerifyGun, 1.3f);
				}
				if (scenario == "gunfree" || scenario == "all")
				{
					// 前方没有任何目标的空旷场景：点了就应该一直突突。
					yield return Scenario("gunfree", BuildGunFreeScene, TriggerGun(), VerifyGunFree, 1.8f);
				}
				if (scenario == "gen" || scenario == "all")
				{
					yield return Scenario("gen", BuildGeneratorScene, TriggerFirst(PartType.GrapplingHook, 9), VerifyGenerator);
				}
				if (scenario == "light" || scenario == "all")
				{
					yield return Scenario("light", BuildLightScene, TriggerFirst(PartType.PointLight, 6), VerifyLight);
				}
				if (scenario == "pointlight" || scenario == "all")
				{
					yield return Scenario("pointlight", LampScene(PartType.PointLight, 0), TriggerFirst(PartType.PointLight, 0), scene => VerifyLampVisual(scene, PartType.PointLight, 0, true, false), 0.6f);
				}
				if (scenario == "mushroom" || scenario == "all")
				{
					yield return Scenario("mushroom", LampScene(PartType.PointLight, 1), TriggerFirst(PartType.PointLight, 1), scene => VerifyLampVisual(scene, PartType.PointLight, 1, true, false), 0.6f);
				}
				if (scenario == "lantern" || scenario == "all")
				{
					yield return Scenario("lantern", LampScene(PartType.PointLight, 2), TriggerFirst(PartType.PointLight, 2), scene => VerifyLampVisual(scene, PartType.PointLight, 2, true, false), 0.6f);
				}
				if (scenario == "alienlight" || scenario == "all")
				{
					yield return Scenario("alienlight", LampScene(PartType.PointLight, 4), TriggerFirst(PartType.PointLight, 4), scene => VerifyLampVisual(scene, PartType.PointLight, 4, true, false), 0.6f);
				}
				if (scenario == "decelerationlight" || scenario == "all")
				{
					// 索引 5 = BPSS Part_PointLight_06_SET（DecelerationLight）。原版
					// HasOnOffToggle=false，但 SetEnabled 仍走 OnTouch，所以点击照样换图。
					yield return Scenario("decelerationlight", LampScene(PartType.PointLight, 5), TriggerFirst(PartType.PointLight, 5), scene => VerifyLampVisual(scene, PartType.PointLight, 5, true, false), 0.6f);
				}
				if (scenario == "pointlightoff" || scenario == "all")
				{
					// 索引 3 = BPSS Part_PointLight_04_SET（普通灯）。点两次必须回到熄灭态：
					// 验证器要看到 Inactive 图可见、Active 图带 DisableRendering。
					yield return Scenario("pointlightoff", LampScene(PartType.PointLight, 3), ToggleTwice(PartType.PointLight, 3), scene => VerifyLampVisual(scene, PartType.PointLight, 3, false, false), 0.6f);
				}
				if (scenario == "spotlight" || scenario == "all")
				{
					yield return Scenario("spotlight", BuildSpotLightScene, TriggerFirst(PartType.SpotLight, 0), VerifySpotLight, 0.8f);
				}
				if (scenario == "pumpkin" || scenario == "all")
				{
					// 南瓜放下即固定（BPSS PostInitialize 里 m_enabled = true），点一下才松开，
					// 所以这里点的是「解冻」方向：触发时先记录当时的逆质量/Applied 当作
					// 「触发前真的是冻结态」的证据，再比对触发后是否确实掉下去。
					yield return Scenario("pumpkin", BuildPumpkinScene, TriggerPumpkin, VerifyPumpkin, 1.6f);
				}
				if (scenario == "fuel" || scenario == "all")
				{
					// 燃料箱 → 燃料管 → 喷气发动机，整排悬空：发动机必须在有油时才出力，
					// 且油箱油量要真的下降。
					yield return Scenario("fuel", BuildFuelScene, TriggerFirst(PartType.JetEngine, 0), VerifyFuel, 1.4f);
				}
				if (scenario == "fueldry" || scenario == "all")
				{
					// 把油箱掏空再点火：发动机应当自己熄火（BPSS ConsumeFuel 里
					// supplied <= 0 → SetEnabled(false)），不能凭空推进。
					yield return Scenario("fueldry", BuildFuelScene, TriggerFuelDry, VerifyFuelDry, 1.0f);
				}
				if (scenario == "fuelvalve" || scenario == "all")
				{
					// 关掉阀门 = 断开燃料连通组，此时发动机连启动都不该允许
					// （BPSS JetEngine.IsTriggerable() 要求组里有燃料箱）。
					yield return Scenario("fuelvalve", BuildFuelValveScene, TriggerFuelValve, VerifyFuelValve, 1.0f);
				}
				if (scenario == "rocket" || scenario == "all")
				{
					yield return Scenario("rocket", BuildRocketScene, TriggerFirst(PartType.Rocket, 0), VerifyRocket, 1.5f);
				}
				if (scenario == "pig" || scenario == "all")
				{
					// BPSS Pig.FixedUpdate：速度 < 1 m/s 时 drag/angularDrag 抬到 0.2 + 2.5*(1-speed)。
					// 关掉重力的目的是让注入的速度不被自由下落盖掉，只观察阻尼决策。
					yield return Scenario("pig", BuildPigScene, TriggerPigSlow, VerifyPigSlow, 0.4f);
				}
				if (scenario == "pigfast" || scenario == "all")
				{
					// 同一只猪在速度 > 1 m/s 时必须回落到 0.2 / 0.05。
					yield return Scenario("pigfast", BuildPigScene, TriggerPigFast, VerifyPigFast, 0.3f);
				}
				if (scenario == "kingpig" || scenario == "all")
				{
					// BPSS KingPig.Initialize：常数 drag 0.5 / angularDrag 1。
					yield return Scenario("kingpig", BuildKingPigScene, TriggerKingPig, VerifyHeavyPig, 0.4f);
				}
				if (scenario == "goldenpig" || scenario == "all")
				{
					// BPSS GoldenPig.Initialize：同样是 0.5 / 1（差别只在滚动音效）。
					yield return Scenario("goldenpig", BuildGoldenPigScene, TriggerGoldenPig, VerifyHeavyPig, 0.4f);
				}
				if (scenario == "levitation" || scenario == "all")
				{
					// BPSS PartLevitation：每步 AddForce(up * force)，未装框用 force、装框用 enclosedForce。
					// 原版 force 数值在 AssetBundle 里，bpsim 取不到，所以由 QA 现场挂组件来验证系统。
					yield return Scenario("levitation", BuildLevitationScene, TriggerLevitation, VerifyLevitation, 1.2f);
				}
				if (scenario == "tnt" || scenario == "all")
				{
					yield return Scenario("tnt", BuildTNTScene, TriggerFirst(PartType.TNT, 0), VerifyTNT, 2.6f);
				}
				if (scenario == "balloon" || scenario == "all")
				{
					// No trigger: the button pops the balloon (BPSS pops it on touch),
					// so the lift has to be observed passively.
					yield return Scenario("balloon", BuildBalloonScene, delegate
					{
					}, VerifyBalloon, 1.4f);
				}
				if (scenario == "umbrella" || scenario == "all")
				{
					yield return Scenario("umbrella", BuildUmbrellaScene, () => {}, VerifyUmbrella, 1.4f);
				}
				if (scenario == "poweredumbrella" || scenario == "all")
				{
					yield return Scenario("poweredumbrella", BuildPoweredUmbrellaScene, TriggerFirst(PartType.PoweredUmbrella, 0), VerifyPoweredUmbrella, 1.4f);
				}
				if (scenario == "spring" || scenario == "all")
				{
					yield return Scenario("spring", BuildSpringScene, TriggerFirst(PartType.Spring, 0), VerifySpring, 1.4f);
				}
				if (scenario == "kicker" || scenario == "all")
				{
					yield return Scenario("kicker", BuildKickerScene, TriggerFirst(PartType.Kicker, 0), VerifyKicker, 1.4f);
				}
				if (scenario == "timebomb" || scenario == "all")
				{
					yield return Scenario("timebomb", BuildTimeBombScene, TriggerFirst(PartType.TimeBomb, 0), VerifyTimeBomb, 2.8f);
				}
				if (scenario == "bellows" || scenario == "all")
				{
					yield return Scenario("bellows", BuildBellowsScene, TriggerFirst(PartType.Bellows, 0), VerifyBellows, 1.4f);
				}
				if (scenario == "motorwheel" || scenario == "all")
				{
					yield return Scenario("motorwheel", BuildMotorWheelScene, TriggerFirst(PartType.MotorWheel, 0), VerifyMotorWheel, 1.4f);
				}
				if (scenario == "stickywheel" || scenario == "all")
				{
					yield return Scenario("stickywheel", BuildStickyWheelScene, TriggerFirst(PartType.StickyWheel, 0), VerifyStickyWheel, 1.4f);
				}
				if (scenario == "offroadwheel" || scenario == "all")
				{
					// bpsim 把 BPSS 的 OffRoadWheel 收在 MotorWheel 索引 7。
					yield return Scenario("offroadwheel", BuildOffRoadWheelScene, TriggerFirst(PartType.MotorWheel, 7), VerifyOffRoadWheel, 1.4f);
				}
				if (scenario == "cartwheel" || scenario == "all")
				{
					yield return Scenario("cartwheel", BuildCartWheelScene, LaunchCartWheel, VerifyCartWheel, 0.8f);
				}
				if (scenario == "gearbox" || scenario == "all")
				{
					yield return Scenario("gearbox", BuildGearboxScene, TriggerWheelThenGearbox, VerifyGearbox, 1.8f);
				}
				if (scenario == "jetengine" || scenario == "all")
				{
					yield return Scenario("jetengine", BuildJetEngineScene, TriggerFirst(PartType.JetEngine, 0), VerifyJetEngine, 1.4f);
				}
				if (scenario == "gate" || scenario == "all")
				{
					// MechanicalPart 0-4 = BPSS MechanicalGate1-5。点击后按 0.2 格/FixedUpdate
					// 的曲线展开到目标长度（1 格），整段只要 5 个定步长帧。
					yield return Scenario("gate", BuildGateScene, TriggerFirst(PartType.MechanicalPart, 0), VerifyGate, 1.2f);
				}
				if (scenario == "sandbag" || scenario == "all")
				{
					// 沙袋 0-3：挂在木框下方 1 格（BPSS Initialize 的悬挂搜索距离默认 1），
					// 点击后 Drop()：销毁弹簧 + 上级件 +5·up、自己 −4·up 的瞬时冲量。
					// 等待只留 0.08 s（≈4 个定步长），这样两体的速度增量还没被重力淹没。
					yield return Scenario("sandbag", BuildSandbagScene, TriggerSandbag, VerifySandbag, 0.08f);
				}
				if (scenario == "sandbagspring" || scenario == "all")
				{
					// 沙袋弹簧（BPSS SpringJoint 等价物）：踹下去 6 m/s，绳长 0.5 之外必须被拉住。
					yield return Scenario("sandbagspring", BuildSandbagSpringScene, TriggerSandbagSpring, VerifySandbagSpring, 1.6f);
				}
				if (scenario == "rope" || scenario == "all")
				{
					// 绳索 0-3：BPSS Contraption 装配时 rope.Create(left,right) 建 8 节链，
					// 两端铰接到相邻 Frame/Kicker/同朝向 Rope；绳体本身 kinematic + mass 0。
					// 点击在最近节点处砍断（没有点击位置时取中点）。
					yield return Scenario("rope", BuildRopeScene, TriggerFirst(PartType.Rope, 0), VerifyRope, 0.6f);
				}
				if (scenario == "eggmassless" || scenario == "all")
				{
					// 蛋 2（正交朝向）= 失重蛋：BPSS Egg.SetEnabled 的 case 2 关掉同连通块
					// 所有刚体的 useGravity。单颗蛋自成一个连通块，所以只有它自己飘着；
					// 旁边的普通蛋照常下落 —— 用它当对照证明效果真的只作用于连通块。
					yield return Scenario("eggmassless", BuildEggMasslessScene, TriggerEggMassless, VerifyEggMassless, 1.2f);
				}
				if (scenario == "egggravity" || scenario == "all")
				{
					// 蛋 1 = 重力蛋（质量 50）：FixedUpdate 对全场刚体施加
					// force/d³·m_self·m_other 的吸引力，非重力蛋的对方还要吃反作用力。
					// 默认关闭（BPSS PostInitialize 只对 2/3/4/5 自动开启），所以这里点击开启。
					yield return Scenario("egggravity", BuildEggGravityScene, TriggerFirst(PartType.Egg, 1), VerifyEggGravity, 0.1f);
				}
				if (scenario == "eggghost" || scenario == "all")
				{
					// 蛋 3 = 穿透蛋：同连通块所有 Collider 的 enabled 置 false。
					// 默认开启，所以开局就应该穿过地面；点击关闭后碰撞体还原。
					yield return Scenario("eggghost", BuildEggGhostScene, TriggerEggGhost, VerifyEggGhost, 1.2f);
				}
				if (scenario == "eggvoid" || scenario == "all")
				{
					// 蛋 4 = 虚空蛋：每帧关掉半径内所有碰撞体（半径按朝向取表，
					// 正交朝向默认 8 格），离开半径的还原。默认开启。
					yield return Scenario("eggvoid", BuildEggVoidScene, TriggerEggVoid, VerifyEggVoid, 0.8f);
				}
				if (scenario == "eggalien" || scenario == "all")
				{
					// 蛋 5 = 异形胶蛋（AlienEgg）：同连通块所有关节不可断 ——
					// BPSS 把 joint.breakForce 设成 ∞，这里等价于把
					// PhysicsJoint 各条线性约束的 MaxImpulse 设成 ∞。
					yield return Scenario("eggalien", BuildEggAlienScene, TriggerEggAlien, VerifyEggAlien, 0.8f);
				}
				if (scenario == "wing" || scenario == "all")
				{
					yield return Scenario("wing", BuildWingScene, LaunchWing, VerifyWing, 0.6f);
				}
				Line("=== done ===");
			}
			finally
			{
				s_status = "done:" + scenario;
				s_running = false;
				DestroyGround();
				try
				{
					File.WriteAllText(LogPath, s_report.ToString());
				}
				catch (Exception exception)
				{
					Debug.LogWarning("[PartGameplayQA] log write failed: " + exception.Message);
				}
			}
		}

		public static string Demo()
		{
			if (s_running)
			{
				return "busy: " + s_status;
			}
			s_running = true;
			s_status = "demo:starting";
			s_report.Length = 0;
			PartGameplayQA runner = Ensure();
			runner.StartCoroutine(runner.DemoRoutine());
			return "started: demo";
		}

		public static string Stop()
		{
			string before = (PartGameplayManager.Instance != null) ? PartGameplayManager.Instance.Describe() : "no manager";
			if (PartManager.Instance != null && PartManager.Instance.IsSimulating)
			{
				PartManager.Instance.EndSimulation();
			}
			s_running = false;
			s_status = "stopped";
			return before;
		}

		private IEnumerator DemoRoutine()
		{
			try
			{
				PartManager manager = PartManager.Instance;
				if (manager.IsSimulating)
				{
					manager.EndSimulation();
					yield return null;
				}
				manager.LoadEmptyScene("GameplayDemo");
				yield return null;
				PartScene scene = manager.ActiveScene;
				Place(scene, PartType.MetalFrame, 0, 0, 0, 0);
				Place(scene, PartType.MetalFrame, 0, 1, 0, 0);
				Place(scene, PartType.MetalFrame, 0, 2, 0, 0);
				Place(scene, PartType.GrapplingHook, 0, 3, 0, 0);
				Place(scene, PartType.GrapplingHook, 6, 2, 1, 0);
				Place(scene, PartType.GrapplingHook, 9, 1, 1, 0);
				Place(scene, PartType.PointLight, 6, 1, 2, 0, overlay: true);
				Place(scene, PartType.WoodenFrame, 0, 0, 2, 0);
				Place(scene, PartType.WoodenFrame, 0, 9, 0, 0);
				Place(scene, PartType.MetalFrame, 0, 7, 1, 0);
				scene.Unmanaged.Update();
				yield return null;
				manager.StartSimulation();
				yield return null;
				Line("demo: placed " + DescribeParts(scene));
				yield return new WaitForSeconds(0.2f);
				Entity hook = FindPart(PartType.GrapplingHook, 0);
				Entity gun = FindPart(PartType.GrapplingHook, 6);
				Entity generator = FindPart(PartType.GrapplingHook, 9);
				Entity light = FindPart(PartType.PointLight, 6);
				if (hook != Entity.Null)
				{
					PartSimulation.ProcessTouch(hook);
				}
				if (gun != Entity.Null)
				{
					PartSimulation.ProcessTouch(gun);
				}
				if (generator != Entity.Null)
				{
					PartSimulation.ProcessTouch(generator);
				}
				if (light != Entity.Null)
				{
					PartSimulation.ProcessTouch(light);
				}
				yield return new WaitForSeconds(1.0f);
				Line("demo: " + Snapshot().Replace("\n", " | "));
				s_status = "demo:ready";
			}
			finally
			{
				s_running = false;
			}
		}

		private delegate void SceneBuilder(PartScene scene);

		private delegate string Verifier(PartScene scene);

		private IEnumerator Scenario(string name, SceneBuilder builder, Action trigger, Verifier verifier, float waitAfterTrigger = 1.4f)
		{
			Line("-- scenario " + name);
			PartManager manager = PartManager.Instance;
			if (manager == null)
			{
				Line("   FAIL: PartManager missing");
				yield break;
			}
			if (manager.IsSimulating)
			{
				manager.EndSimulation();
				yield return null;
			}
			// 上一个场景可能留下静态地面（它不属于任何 PartScene），先清掉。
			DestroyGround();
			manager.LoadEmptyScene("GameplayQA_" + name);
			yield return null;
			PartScene scene = manager.ActiveScene;
			if (scene == null)
			{
				Line("   FAIL: no active scene");
				yield break;
			}
			builder(scene);
			scene.Unmanaged.Update();
			yield return null;
			manager.StartSimulation();
			yield return null;
			Line("   placed: " + DescribeParts(scene));
			yield return new WaitForSeconds(0.2f);
			if (!manager.IsSimulating)
			{
				Line("   FAIL: simulation did not start");
				yield break;
			}
			try
			{
				trigger();
			}
			catch (Exception exception)
			{
				Line("   FAIL: trigger exception " + exception);
			}
			yield return new WaitForSeconds(waitAfterTrigger);
			Line("   state: " + Snapshot().Replace("\n", " | "));
			string result = "no verifier";
			try
			{
				result = verifier(scene);
			}
			catch (Exception exception)
			{
				result = "FAIL verifier exception: " + exception;
			}
			Line("   result: " + result);
			manager.EndSimulation();
			yield return null;
		}

		private static Action TriggerFirst(PartType type, int index)
		{
			return delegate
			{
				Entity part = FindPart(type, index);
				if (part == Entity.Null)
				{
					Debug.LogWarning("[PartGameplayQA] part not found " + type + "#" + index);
					return;
				}
				PartSimulation.ProcessTouch(part);
			};
		}

		/// <summary>连点两次 <paramref name="type"/>#<paramref name="index"/>（用来验「关回去」的方向）。</summary>
		/// <remarks>
		/// <c>PartSimulation.ProcessTouch</c> 没有同帧去重，直接转发到 <c>PartGameplayManager.Trigger</c>，
		/// 所以同一帧点两次就是翻转两次。
		/// </remarks>
		private static Action ToggleTwice(PartType type, int index)
		{
			return delegate
			{
				Entity part = FindPart(type, index);
				if (part == Entity.Null)
				{
					Debug.LogWarning("[PartGameplayQA] part not found " + type + "#" + index);
					return;
				}
				PartSimulation.ProcessTouch(part);
				PartSimulation.ProcessTouch(part);
			};
		}

		// ------------------------------------------------------------------
		// Scenes
		// ------------------------------------------------------------------

		private static void BuildHookScene(PartScene scene)
		{
			// Dynamic chassis + kinematic anchor so the tether visibly pulls the vehicle.
			Place(scene, PartType.MetalFrame, 0, 0, 0, 0);
			Place(scene, PartType.MetalFrame, 0, 1, 0, 0);
			Place(scene, PartType.MetalFrame, 0, 2, 0, 0);
			Place(scene, PartType.GrapplingHook, 0, 3, 0, 0);
			Place(scene, PartType.WoodenFrame, 0, 9, 0, 0);
		}

		private static void BuildGunScene(PartScene scene)
		{
			Place(scene, PartType.WoodenFrame, 0, 0, 0, 0);
			Place(scene, PartType.WoodenFrame, 0, 1, 0, 0);
			Place(scene, PartType.GrapplingHook, 6, 2, 0, 0);
			// 目标放在 3 格外。没有地面的话所有部件会立刻自由下落（0.2 s 就掉 0.2 格），
			// 弹丸 20 m/s 飞过去时目标早已不在炮口高度上，永远打不中——所以必须先铺地。
			Place(scene, PartType.MetalFrame, 0, 5, 0, 0);
			CreateGround(-0.5f);
		}

		private static void BuildManualGunScene(PartScene scene)
		{
			AutoGunProjectileSystem.ResetProbes();
			Place(scene, PartType.GrapplingHook, 5, 0, 0, 0);
			Place(scene, PartType.MetalFrame, 0, 4, 0, 0);
			CreateGround(-0.5f);
		}

		private static void BuildManualGunFreeScene(PartScene scene)
		{
			AutoGunProjectileSystem.ResetProbes();
			// Diagonal barrel with no targets: test rotation, no target gate and TTL.
			Place(scene, PartType.GrapplingHook, 5, 0, 0, 4);
		}

		private static bool s_manualInitialState;
		private static float3 s_manualMuzzle;
		private static float3 s_manualDirection;

		private static void TriggerManualGun()
		{
			Entity part = FindPart(PartType.GrapplingHook, 5);
			PartBehavior behavior = PartGameplayManager.Instance.Find(part);
			s_manualInitialState = behavior is ManualGunBehavior && !behavior.Enabled && AutoGunProjectileSpawner.SpawnCount == 0;
			// BPRC's default does not inherit the firing vehicle's velocity.
			PartGameplayUtil.AddVelocity(part, new float3(3f, 2f, 0f));
			PartSimulation.ProcessTouch(part);
			PartSimulation.ProcessTouch(part); // second touch during cooldown must be ignored
			using EntityQuery query = PartGameplayUtil.Em.CreateEntityQuery(typeof(AutoGunProjectile));
			using NativeArray<AutoGunProjectile> shots = query.ToComponentDataArray<AutoGunProjectile>(Allocator.Temp);
			s_manualInitialState &= shots.Length == 1 && AutoGunProjectileSpawner.SpawnCount == 1;
			if (shots.Length == 1)
			{
				AutoGunProjectile shot = shots[0];
				s_manualMuzzle = shot.Position;
				s_manualDirection = math.mul(PartGameplayUtil.Rotation(part), new float3(1f, 0f, 0f));
				s_manualInitialState &= !shot.Gravity && shot.ImpulseFalloff && shot.Radius == 8f
					&& math.distance(shot.Velocity, s_manualDirection * 40f) < 0.01f;
			}
		}

		private static bool ManualGunCanRefire()
		{
			Entity part = FindPart(PartType.GrapplingHook, 5);
			PartSimulation.ProcessTouch(part);
			return AutoGunProjectileSpawner.SpawnCount == 2;
		}

		private static string VerifyManualGunHit(PartScene scene)
		{
			float displacement = math.distance(PartGameplayUtil.Position(FindPart(PartType.MetalFrame, 0)).xy, new float2(4f, 0f));
			float travel = math.distance(AutoGunProjectileSystem.LastExplosion, s_manualMuzzle);
			bool single = AutoGunProjectileSpawner.SpawnCount == 1 && AutoGunProjectileSystem.ExplosionCount == 1;
			bool refire = ManualGunCanRefire(); // impact releases the lock before the 1s cooling time
			bool pass = s_manualInitialState && single && travel > 2f && travel < 5f && displacement > 1f
				&& AutoGunProjectileSystem.DrawnProjectiles > 0 && refire;
			return (pass ? "PASS" : "FAIL") + " manual impact initial=" + s_manualInitialState + " single=" + single
				+ " travel=" + travel.ToString("F2") + " targetMoved=" + displacement.ToString("F2") + " refire=" + refire;
		}

		private static string VerifyManualGunFree(PartScene scene)
		{
			float3 flight = AutoGunProjectileSystem.LastExplosion - s_manualMuzzle;
			float travel = math.length(flight);
			bool straight = math.dot(math.normalizesafe(flight), s_manualDirection) > 0.999f;
			bool single = AutoGunProjectileSpawner.SpawnCount == 1 && AutoGunProjectileSystem.ExplosionCount == 1;
			bool refire = ManualGunCanRefire();
			bool pass = s_manualInitialState && single && straight && travel >= 39f && travel < 45f && refire;
			return (pass ? "PASS" : "FAIL") + " manual TTL initial=" + s_manualInitialState + " single=" + single
				+ " straight=" + straight + " travel=" + travel.ToString("F2") + " refire=" + refire;
		}

		private static void BuildGeneratorScene(PartScene scene)
		{
			// 生成器朝 +x，模板取它正后方 1..3 格（BPSS MultipartGeneratorDetectionDistance = 3），
			// 所以后面连铺三块木框；复制体按 CopyDistance(8) + DetectionDistance(3) 落到 +x 侧。
			Place(scene, PartType.WoodenFrame, 0, -1, 0, 0);
			Place(scene, PartType.WoodenFrame, 0, 0, 0, 0);
			Place(scene, PartType.WoodenFrame, 0, 1, 0, 0);
			Place(scene, PartType.GrapplingHook, 9, 2, 0, 0);
		}

		private static void BuildLightScene(PartScene scene)
		{
			Place(scene, PartType.WoodenFrame, 0, 0, 0, 0);
			Place(scene, PartType.WoodenFrame, 0, 1, 0, 0);
			// AutoControlLight must be enclosed INTO a frame (BPSS: light.enclosedInto != null),
			// so the light is placed with overlay on the frame cell.
			Place(scene, PartType.PointLight, 6, 1, 0, 0, overlay: true);
			Place(scene, PartType.WoodenFrame, 0, 1, -3, 0);
		}

		/// <summary>
		/// 灯族布景：两块木框 + 一盏嵌进木框的灯。
		/// </summary>
		/// <remarks>
		/// BPSS 的灯都是可装入件（<c>enclosedInto</c>），所以照 <c>BuildLightScene</c> 的样子
		/// 用 overlay 放进木框格子里；验证器只看 sprite 显隐，与位置无关。
		/// </remarks>
		private static SceneBuilder LampScene(PartType type, int index)
		{
			return delegate(PartScene scene)
			{
				Place(scene, PartType.WoodenFrame, 0, 0, 0, 0);
				Place(scene, PartType.WoodenFrame, 0, 1, 0, 0);
				Place(scene, type, index, 1, 0, 0, overlay: true);
			};
		}

		private static void BuildSpotLightScene(PartScene scene)
		{
			// 聚光灯不可装入（BPSS SpotLight 没有 CanBeEnclosed），直接立在地上。
			Place(scene, PartType.SpotLight, 0, 0, 0, 0);
		}

		private static void BuildRocketScene(PartScene scene)
		{
			// Local +X rotates into world +Y; the rocket must climb against gravity.
			Place(scene, PartType.Rocket, 0, 0, 0, 1);
		}

		public static string DumpComponents()
		{
			PartManager manager = PartManager.Instance;
			if (manager == null || manager.PartSimulator == null)
			{
				return "no simulator";
			}
			PartSimulatorUnmanaged simulator = manager.PartSimulator.Unmanaged;
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append("components=").Append(simulator.ConnectedComponents.Length);
			for (int i = 0; i < simulator.ConnectedComponents.Length; i++)
			{
				ConnectedComponentInfo info = simulator.ConnectedComponents[i];
				stringBuilder.Append("\n  [").Append(i).Append("] parts=").Append(info.PartCount)
					.Append(" enginePower=").Append(info.EnginePower).Append(" consumption=").Append(info.PowerConsumption);
			}
			EntityManager entityManager = PartGameplayUtil.Em;
			for (int j = 0; j < simulator.Parts.Length; j++)
			{
				Entity entity = simulator.Parts[j];
				if (!entityManager.Exists(entity) || !entityManager.HasComponent<PartTypeValue>(entity))
				{
					continue;
				}
				PartTypeValue type = entityManager.GetComponentData<PartTypeValue>(entity);
				bool hasExtension = entityManager.HasComponent<PartExtensionComponent>(entity);
				stringBuilder.Append("\n  part ").Append(type.Type).Append('#').Append(type.Index)
					.Append(" cc=").Append(PartGameplayUtil.ConnectedComponent(entity))
					.Append(" extension=").Append(hasExtension);
				if (hasExtension)
				{
					PartExtensionComponent extension = entityManager.GetComponentData<PartExtensionComponent>(entity);
					stringBuilder.Append(" mass=").Append(extension.Value.Mass)
						.Append(" power=").Append(extension.Value.EnginePower)
						.Append(" consumption=").Append(extension.Value.PowerConsumption);
				}
			}
			return stringBuilder.ToString();
		}

		private static void BuildTNTScene(PartScene scene)
		{
			// Both bodies are free-falling in the empty QA scene, so the frame's
			// horizontal displacement relative to the charge isolates the blast
			// (gravity affects both equally).
			Place(scene, PartType.TNT, 0, 0, 0, 0);
			Place(scene, PartType.MetalFrame, 0, 3, 0, 0);
		}

		private static void BuildBalloonScene(PartScene scene)
		{
			// WoodenFrame is the kinematic anchor of the simulator (PartSimulator.IsKinematic),
			// so every load-bearing chassis in these scenes has to be a MetalFrame.
			// The balloon template is massless and cannot be connected, so it hangs in the
			// cell above the frame and lifts whatever dynamic parts sit next to it.
			Place(scene, PartType.MetalFrame, 0, 0, 0, 0);
			Place(scene, PartType.Balloon, 0, 0, 1, 0);
		}

		private static void BuildUmbrellaScene(PartScene scene)
		{
			Place(scene, PartType.MetalFrame, 0, 0, 0, 0);
			Place(scene, PartType.Umbrella, 0, 0, 1, 0);
		}

		private static void BuildPoweredUmbrellaScene(PartScene scene)
		{
			// BPSS gates the powered canopy on engine power (PowerConsumption = 100),
			// so the test rig needs a real engine in the same assembly.
			Place(scene, PartType.MetalFrame, 0, 0, 0, 0);
			Place(scene, PartType.Engine, 0, 1, 0, 0);
			Place(scene, PartType.PoweredUmbrella, 0, 0, 1, 0);
		}

		private static void BuildBellowsScene(PartScene scene)
		{
			// The bellows pushes along its own local up axis.  Its connection face
			// points Right (see PartExtensionMap), so the frame has to sit on its
			// right side for the two to be welded together.
			Place(scene, PartType.Bellows, 0, 0, 0, 0);
			Place(scene, PartType.MetalFrame, 0, 1, 0, 0);
			// Control body: same part, same drop height, no bellows.  Comparing the
			// two cancels gravity and isolates the 10 N boost.
			Place(scene, PartType.MetalFrame, 0, 6, 0, 0);
		}

		private static void BuildMotorWheelScene(PartScene scene)
		{
			// Wheels declare connectionDirection Up (they are targets, not joint
			// sources), so the chassis cell has to sit above the wheel.  The engine
			// is target-only as well and is welded to the frame's right face.
			Place(scene, PartType.MetalFrame, 0, 0, 0, 0);
			Place(scene, PartType.MotorWheel, 0, 0, -1, 0);
			Place(scene, PartType.Engine, 0, 1, 0, 0);
			// MotorWheel 的根球体：center (0,-0.075,0) / radius 0.45，root 在 y=-1，
			// 所以轮子最低点是 -1.525；地面放在 -1.5 让它一开始就轻微压在地上。
			CreateGround(-1.5f);
		}

		private static void BuildStickyWheelScene(PartScene scene)
		{
			Place(scene, PartType.MetalFrame, 0, 0, 0, 0);
			Place(scene, PartType.StickyWheel, 0, 0, -1, 0);
			Place(scene, PartType.Engine, 0, 1, 0, 0);
			CreateGround(-1.5f);
		}

		private static void BuildOffRoadWheelScene(PartScene scene)
		{
			Place(scene, PartType.MetalFrame, 0, 0, 0, 0);
			Place(scene, PartType.MotorWheel, 7, 0, -1, 0);
			Place(scene, PartType.Engine, 0, 1, 0, 0);
			// OffRoadWheel 的根球体是 center (0,-0.5,0) / radius 0.9（BPSS prefab 实测 0.9），
			// 最低点在 root.y - 1.4 = -2.4。
			CreateGround(-2.4f);
		}

		private static void BuildCartWheelScene(PartScene scene)
		{
			// CartWheel 不施力（BPSS 里它只做视觉滚动），所以单独放、由 LaunchCartWheel
			// 给一个水平初速，验证「滚起来」这件事本身。
			Place(scene, PartType.CartWheel, 0, 0, 0, 0);
			// CartWheel 根物体没有碰撞体，物理形状来自合并进根的 WheelPivot 球（0.36）。
			CreateGround(-0.4f);
		}

		private static void BuildGearboxScene(PartScene scene)
		{
			// 齿轮箱不是连接源（PartExtensionMap: isConnectionSource = false），
			// 所以它只能被金属框焊住 —— 放在框的左侧一格。
			Place(scene, PartType.MetalFrame, 0, 0, 0, 0);
			Place(scene, PartType.MotorWheel, 0, 0, -1, 0);
			Place(scene, PartType.Engine, 0, 1, 0, 0);
			Place(scene, PartType.Gearbox, 0, -1, 0, 0);
			CreateGround(-1.5f);
		}

		private static void LaunchCartWheel()
		{
			Entity wheel = FindPart(PartType.CartWheel, 0);
			if (wheel == Entity.Null)
			{
				Debug.LogWarning("[PartGameplayQA] cartwheel not found");
				return;
			}
			PartGameplayUtil.AddVelocity(wheel, new float3(6f, 0f, 0f));
		}

		/// <summary>先开轮子，0.8 s 后再按齿轮箱 —— 用来验证「倒车」这条链路。</summary>
		private static void TriggerWheelThenGearbox()
		{
			Entity wheel = FindPart(PartType.MotorWheel, 0);
			if (wheel != Entity.Null)
			{
				PartSimulation.ProcessTouch(wheel);
			}
			Ensure().StartCoroutine(ToggleGearboxLater(0.8f));
		}

		private static IEnumerator ToggleGearboxLater(float delay)
		{
			yield return new WaitForSeconds(delay);
			Entity gearbox = FindPart(PartType.Gearbox, 0);
			if (gearbox != Entity.Null)
			{
				PartSimulation.ProcessTouch(gearbox);
			}
		}

		/// <summary>沙袋场景的「触发前」快照，供 <see cref="VerifySandbag" /> 对照放手前后。</summary>
		private static bool s_bagBoundBefore;

		private static bool s_bagDroppedBefore;

		private static Entity s_bagAnchorBefore;

		private static float s_bagDistanceBefore;

		private static float s_bagAnchorVelocityBefore;

		private static void BuildSandbagScene(PartScene scene)
		{
			// BPSS Sandbag.Initialize() 沿 m_direction（默认 up）向上 SandbagConnectionDistance（1）格
			// 找悬挂点，只认 IsPartOfChassis() 或 Pig。所以木框放在沙袋正上方一格。
			Place(scene, PartType.MetalFrame, 0, 0, 1, 0);
			Place(scene, PartType.Sandbag, 0, 0, 0, 0);
			CreateGround(-3f);
		}

		private static void TriggerSandbag()
		{
			Entity sandbag = FindPart(PartType.Sandbag, 0);
			if (sandbag == Entity.Null)
			{
				Debug.LogWarning("[PartGameplayQA] sandbag not found");
				return;
			}
			EntityManager entityManager = PartGameplayUtil.Em;
			s_bagBoundBefore = false;
			s_bagDroppedBefore = true;
			s_bagAnchorBefore = Entity.Null;
			s_bagDistanceBefore = float.NaN;
			s_bagAnchorVelocityBefore = float.NaN;
			if (entityManager.HasComponent<Sandbag>(sandbag))
			{
				Sandbag bag = entityManager.GetComponentData<Sandbag>(sandbag);
				s_bagBoundBefore = bag.Bound;
				s_bagDroppedBefore = bag.Dropped;
				s_bagAnchorBefore = bag.Anchor;
				if (bag.Anchor != Entity.Null)
				{
					// 弹簧测的是「沙袋挂点（本地 up*0.5）」到「上级件原点」的距离。
					float3 point = PartGameplayUtil.Position(sandbag) + new float3(0f, Sandbag.AnchorOffset, 0f);
					float3 target = PartGameplayUtil.Position(bag.Anchor);
					s_bagDistanceBefore = math.length(point - target);
					if (entityManager.HasComponent<PhysicsVelocity>(bag.Anchor))
					{
						s_bagAnchorVelocityBefore = entityManager.GetComponentData<PhysicsVelocity>(bag.Anchor).Linear.y;
					}
				}
			}
			PartSimulation.ProcessTouch(sandbag);
		}

		private static void BuildSandbagSpringScene(PartScene scene)
		{
			// 只验证绳子（BPSS SpringJoint）：spring 100 / damper 10 / minDistance 0 /
			// maxDistance 0.5 —— 绳长之内不施力，超过上限才拉回来。
			Place(scene, PartType.MetalFrame, 0, 0, 1, 0);
			Place(scene, PartType.Sandbag, 0, 0, 0, 0);
			CreateGround(-6f);
		}

		private static void TriggerSandbagSpring()
		{
			Entity sandbag = FindPart(PartType.Sandbag, 0);
			if (sandbag == Entity.Null)
			{
				return;
			}
			// 6 m/s 往下踹：自由落体 1.4 s 会掉约 9.6 m，被绳子拉住则停在 maxDistance 附近。
			PartGameplayUtil.AddVelocity(sandbag, new float3(0f, -6f, 0f));
		}

		/// <summary>
		/// 沙袋验收：放手前挂在木框上（弹簧把距离压在 maxDistance 附近），
		/// 放手后弹簧消失（Anchor 清空），并且两个冲量方向相反、量级与 BPSS 一致。
		/// </summary>
		/// <remarks>
		/// BPSS <c>Drop()</c>：<c>connectedBody.AddForce(5f * Vector3.up, Impulse)</c> +
		/// <c>rigidbody.AddForce(-4f * Vector3.up, Impulse)</c>。Impulse 模式下速度增量 = 冲量 × 逆质量，
		/// 木框质量 1.0 → 约 +5 m/s；沙袋质量 1.1 → 约 −3.6 m/s。
		/// 触发后只等 0.08 s（≈4 个定步长），重力只吃掉约 0.78 m/s，所以符号与量级都还分辨得出来。
		/// </remarks>
		private static string VerifySandbag(PartScene scene)
		{
			Entity sandbag = FindPart(PartType.Sandbag, 0);
			EntityManager entityManager = PartGameplayUtil.Em;
			if (sandbag == Entity.Null || !entityManager.HasComponent<Sandbag>(sandbag))
			{
				return "FAIL sandbag component missing";
			}
			Sandbag bag = entityManager.GetComponentData<Sandbag>(sandbag);
			float selfVelocity = (entityManager.HasComponent<PhysicsVelocity>(sandbag)
				? entityManager.GetComponentData<PhysicsVelocity>(sandbag).Linear.y
				: float.NaN);
			float anchorVelocity = ((s_bagAnchorBefore != Entity.Null && entityManager.Exists(s_bagAnchorBefore)
				&& entityManager.HasComponent<PhysicsVelocity>(s_bagAnchorBefore))
				? entityManager.GetComponentData<PhysicsVelocity>(s_bagAnchorBefore).Linear.y
				: float.NaN);
			// BPSS 的挂接态：SpringJoint 的 minDistance=0、maxDistance=0.5，沙袋被瞬移到
			// 「上级件下方半格 + HangOffset」，所以初始距离 ≈ |HangOffset| ≈ 0.21，处在松弛区
			// （绳长之内不施力）。这里只要求「挂在有效上级件上且没超绳长」。
			bool hung = s_bagBoundBefore && !s_bagDroppedBefore && s_bagAnchorBefore != Entity.Null
				&& !float.IsNaN(s_bagDistanceBefore)
				&& s_bagDistanceBefore > 0.01f
				&& s_bagDistanceBefore <= bag.MaxDistance;
			bool released = bag.Dropped && bag.Anchor == Entity.Null;
			// 放手那一帧两个冲量方向相反：上级件被顶上去，沙袋被蹬下去。
			bool pushed = anchorVelocity > 1f && selfVelocity < -1f;
			return ((hung && released && pushed) ? "PASS" : "FAIL")
				+ " balloons=" + bag.Balloons
				+ " max=" + bag.MaxDistance.ToString("F2")
				+ " boundBefore=" + s_bagBoundBefore
				+ " droppedBefore=" + s_bagDroppedBefore
				+ " anchorBefore=" + (s_bagAnchorBefore != Entity.Null)
				+ " distanceBefore=" + s_bagDistanceBefore.ToString("F2")
				+ " droppedAfter=" + bag.Dropped
				+ " anchorAfter=" + bag.Anchor
				+ " anchorVy=" + anchorVelocity.ToString("F2")
				+ " selfVy=" + selfVelocity.ToString("F2")
				+ " anchorVyBefore=" + s_bagAnchorVelocityBefore.ToString("F2");
		}

		private static string VerifySandbagSpring(PartScene scene)
		{
			Entity sandbag = FindPart(PartType.Sandbag, 0);
			EntityManager entityManager = PartGameplayUtil.Em;
			if (sandbag == Entity.Null || !entityManager.HasComponent<Sandbag>(sandbag))
			{
				return "FAIL sandbag component missing";
			}
			Sandbag bag = entityManager.GetComponentData<Sandbag>(sandbag);
			if (bag.Anchor == Entity.Null || bag.Dropped)
			{
				return "FAIL not tethered dropped=" + bag.Dropped + " anchor=" + bag.Anchor;
			}
			float3 point = PartGameplayUtil.Position(sandbag) + new float3(0f, Sandbag.AnchorOffset, 0f);
			float3 target = PartGameplayUtil.Position(bag.Anchor);
			float distance = math.length(point - target);
			float velocity = (entityManager.HasComponent<PhysicsVelocity>(sandbag)
				? entityManager.GetComponentData<PhysicsVelocity>(sandbag).Linear.y
				: float.NaN);
			// 被绳子拉住：距离停在 maxDistance 附近（弹簧会把超出的部分推回去），
			// 且速度已收敛（自由落体 1.4 s 会是 -13.7 m/s 左右）。
			bool held = distance <= bag.MaxDistance + 0.15f && math.abs(velocity) < 2f;
			return (held ? "PASS" : "FAIL")
				+ " distance=" + distance.ToString("F2")
				+ " max=" + bag.MaxDistance.ToString("F2")
				+ " vy=" + velocity.ToString("F2")
				+ " dropped=" + bag.Dropped;
		}

		private static void BuildGateScene(PartScene scene)
		{
			// 闸门要焊在别的件上才有意义（它自己不是连接源），左边放一个铁框当基座。
			// 右边**不能**放东西：验收要沿 +x 打一条射线穿过门板，任何邻居都会先被命中。
			Place(scene, PartType.MetalFrame, 0, 0, 0, 0);
			Place(scene, PartType.MechanicalPart, 0, 1, 0, 0);
			CreateGround(-0.5f);
		}

		/// <summary>
		/// 机械闸门的验收：状态、门板变换、透明度、以及<b>真的挡住了东西</b>（对复合碰撞体打射线）。
		/// </summary>
		/// <remarks>
		/// 闸门放在 x=1，门板展开到 1 格时本地 x ∈ [0.5, 1.5]，即世界 x ∈ [1.5, 2.5]。
		/// 所以从世界 x=5 沿 −x 打一条 3 格长的射线，最近命中必须落在 2.5（门板外沿）。
		/// 门板收回时这条射线什么都打不到（基座铁框只占 [−0.5, 0.5]），返回 NaN。
		/// 这条射线同时证明 <see cref="MechanicalGateSystem.RebuildCollider" /> 重建的复合碰撞体已经进物理世界。
		/// </remarks>
		private static string VerifyGate(PartScene scene)
		{
			Entity gate = FindPart(PartType.MechanicalPart, 0);
			EntityManager entityManager = PartGameplayUtil.Em;
			if (gate == Entity.Null || !entityManager.HasComponent<MechanicalGate>(gate))
			{
				return "FAIL gate component missing";
			}
			MechanicalGate state = entityManager.GetComponentData<MechanicalGate>(gate);
			float bladeScaleX = ReadGateChildScale(gate, collider: true);
			float bladeX = ReadGateChildPositionX(gate, collider: true);
			float alpha = ReadGateRendererAlpha(gate);
			float3 origin = PartGameplayUtil.Position(gate);
			float hitX = RaycastAxisX(origin, 4.0f, -1f, 3.0f);
			bool extended = state.Enabled && state.Value > 0.95f;
			bool visualOk = math.abs(bladeScaleX - 1f) < 0.1f && math.abs(bladeX - 1f) < 0.1f;
			bool blocked = math.abs(hitX - 2.5f) < 0.15f;
			bool alphaOk = math.abs(alpha - 0.8f) < 0.05f;
			return ((extended && visualOk && blocked && alphaOk) ? "PASS" : "FAIL")
				+ " enabled=" + state.Enabled
				+ " value=" + state.Value.ToString("F2")
				+ " target=" + state.TargetLength.ToString("F2")
				+ " built=" + state.BuiltLength.ToString("F2")
				+ " colliderScaleX=" + bladeScaleX.ToString("F2")
				+ " colliderX=" + bladeX.ToString("F2")
				+ " alpha=" + alpha.ToString("F2")
				+ " opacity=" + state.Opacity.ToString("F2")
				+ " rayHitX=" + hitX.ToString("F2")
				+ " children=" + DescribeGateChildren(gate);
		}

		/// <summary>诊断用：把闸门每个子实体的标记 / 材质 alpha / 缩放列出来。</summary>
		private static string DescribeGateChildren(Entity gate)
		{
			EntityManager entityManager = PartGameplayUtil.Em;
			if (!entityManager.HasComponent<LinkedEntityGroup>(gate))
			{
				return "none";
			}
			DynamicBuffer<LinkedEntityGroup> group = entityManager.GetBuffer<LinkedEntityGroup>(gate);
			StringBuilder text = new StringBuilder();
			for (int i = 1; i < group.Length; i++)
			{
				Entity child = group[i].Value;
				if (i > 1)
				{
					text.Append(' ');
				}
				text.Append(i).Append(':');
				if (!entityManager.HasComponent<GateVisual>(child))
				{
					text.Append("plain");
					continue;
				}
				text.Append(entityManager.GetComponentData<GateVisual>(child).Collider ? "col" : "ren");
				text.Append(entityManager.HasComponent<Unity.Rendering.MaterialColor>(child)
					? "@" + entityManager.GetComponentData<Unity.Rendering.MaterialColor>(child).Value.w.ToString("F2")
					: "@nomc");
			}
			return text.ToString();
		}

		/// <summary>读 <c>GateCollider</c> / <c>GateRenderer</c> 子实体的 <c>PostTransformScale</c> 的 x。</summary>
		private static float ReadGateChildScale(Entity gate, bool collider)
		{
			Entity child = FindGateChild(gate, collider);
			EntityManager entityManager = PartGameplayUtil.Em;
			if (child == Entity.Null || !entityManager.HasComponent<PostTransformScale>(child))
			{
				return 0f;
			}
			float3x3 matrix = entityManager.GetComponentData<PostTransformScale>(child).Value;
			return matrix.c0.x;
		}

		private static float ReadGateChildPositionX(Entity gate, bool collider)
		{
			Entity child = FindGateChild(gate, collider);
			EntityManager entityManager = PartGameplayUtil.Em;
			if (child == Entity.Null || !entityManager.HasComponent<LocalTransform>(child))
			{
				return 0f;
			}
			return entityManager.GetComponentData<LocalTransform>(child).Position.x;
		}

		private static float ReadGateRendererAlpha(Entity gate)
		{
			Entity child = FindGateChild(gate, collider: false);
			EntityManager entityManager = PartGameplayUtil.Em;
			if (child == Entity.Null || !entityManager.HasComponent<Unity.Rendering.MaterialColor>(child))
			{
				return 0f;
			}
			return entityManager.GetComponentData<Unity.Rendering.MaterialColor>(child).Value.w;
		}

		private static Entity FindGateChild(Entity gate, bool collider)
		{
			EntityManager entityManager = PartGameplayUtil.Em;
			if (gate == Entity.Null || !entityManager.HasComponent<LinkedEntityGroup>(gate))
			{
				return Entity.Null;
			}
			DynamicBuffer<LinkedEntityGroup> group = entityManager.GetBuffer<LinkedEntityGroup>(gate);
			for (int i = 1; i < group.Length; i++)
			{
				Entity child = group[i].Value;
				if (entityManager.HasComponent<GateVisual>(child) && entityManager.GetComponentData<GateVisual>(child).Collider == collider)
				{
					return child;
				}
			}
			return Entity.Null;
		}

		/// <summary>沿 ±x 打一条射线，返回命中点的 x（相对 <paramref name="origin" /> 所在部件的世界位置）。</summary>
		private static float RaycastAxisX(float3 origin, float from, float direction, float distance)
		{
			EntityManager entityManager = PartGameplayUtil.Em;
			if (entityManager.World == null || !entityManager.World.IsCreated)
			{
				return float.NaN;
			}
			EntityQuery query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PhysicsWorldSingleton>());
			if (query.IsEmptyIgnoreFilter)
			{
				return float.NaN;
			}
			PhysicsWorldSingleton singleton = query.GetSingleton<PhysicsWorldSingleton>();
			float3 start = origin + new float3(from, 0f, 0f);
			RaycastInput input = new RaycastInput
			{
				Start = start,
				End = start + new float3(direction, 0f, 0f) * distance,
				Filter = CollisionFilter.Default
			};
			NativeList<Unity.Physics.RaycastHit> hits = new NativeList<Unity.Physics.RaycastHit>(8, Allocator.Temp);
			try
			{
				AllHitsCollector<Unity.Physics.RaycastHit> collector = new AllHitsCollector<Unity.Physics.RaycastHit>(1f, ref hits);
				if (!singleton.CollisionWorld.CastRay(input, ref collector) || hits.Length == 0)
				{
					return float.NaN;
				}
				float best = float.MaxValue;
				for (int i = 0; i < hits.Length; i++)
				{
					if (hits[i].Fraction < best)
					{
						best = hits[i].Fraction;
					}
				}
				return start.x + direction * distance * best;
			}
			finally
			{
				hits.Dispose();
			}
		}

		private static void BuildJetEngineScene(PartScene scene)
		{
			// The jet pushes along its own local right axis, so leave it at
			// rotation 0: thrust shows up as +x travel while gravity pulls it down.
			// 发动机现在要油才会转（对齐 BPSS 的 IsTriggerable：组里必须有燃料箱），
			// 所以后面挂一个油箱 + 一根燃料管；燃料管自己就是连接源，三件直接焊成一体。
			// 燃料件是喷气引擎组的皮肤（索引 4 = 燃料箱、2 = 燃料管、0 = 引擎本体）。
			Place(scene, PartType.JetEngine, 4, -2, 0, 0);
			Place(scene, PartType.JetEngine, 2, -1, 0, 0);
			Place(scene, PartType.JetEngine, 0, 0, 0, 0);
		}

		private static float s_wingLaunchY, s_wingLaunchTime;
		private static void BuildWingScene(PartScene scene)
		{
			// An unrotated wing has its chord along +x.  Launching it almost
			// horizontally gives a small positive angle of attack (about 14 degrees)
			// which the response curve maps to lift that reduces the descent.
			Place(scene, PartType.Wings, 0, 0, 0, 0);
		}

		private static void LaunchWing()
		{
			Entity wing = FindPart(PartType.Wings, 0);
			if (wing == Entity.Null)
			{
				Debug.LogWarning("[PartGameplayQA] wing not found");
				return;
			}
			s_wingLaunchY = PartVariantPhysics.Position(wing).y;
			s_wingLaunchTime = Time.time;
			PartGameplayUtil.Em.SetComponentData(wing, new PhysicsVelocity { Linear = new float3(8,-2,0) });
		}

		private static void BuildSpringScene(PartScene scene)
		{
			Place(scene, PartType.MetalFrame, 0, 0, 0, 0);
			Place(scene, PartType.Spring, 0, 0, 1, 0);
		}

		private static void BuildKickerScene(PartScene scene)
		{
			Place(scene, PartType.Kicker, 0, 0, 0, 0);
			Place(scene, PartType.MetalFrame, 0, -1, 0, 0);
			Place(scene, PartType.MetalFrame, 1, 1, 0, 0);
		}

		private static void BuildTimeBombScene(PartScene scene)
		{
			Place(scene, PartType.TimeBomb, 0, 0, 0, 0);
			Place(scene, PartType.MetalFrame, 0, 0, -3, 0);
		}

		// ------------------------------------------------------------------
		// Rope 0-3
		// ------------------------------------------------------------------

		private static void BuildRopeScene(PartScene scene)
		{
			// BPSS Contraption 装配阶段对 Rope 调 rope.Create(left, right)：沿绳子自身朝向
			// （Deg_0 竖直 / Deg_90 水平）前后各一格找 Frame / Kicker / 同朝向的 Rope。
			// 这里用上下两块木框（唯一 kinematic 的框架件）夹住一根竖直的绳子。
			Place(scene, PartType.WoodenFrame, 0, 0, 1, 0);
			Place(scene, PartType.Rope, 0, 0, 0, 0);
			Place(scene, PartType.WoodenFrame, 0, 0, -1, 0);
			CreateGround(-3f);
		}

		private static string VerifyRope(PartScene scene)
		{
			EntityManager entityManager = PartGameplayUtil.Em;
			Entity ropeEntity = FindPart(PartType.Rope, 0);
			if (ropeEntity == Entity.Null || !entityManager.HasComponent<Rope>(ropeEntity))
			{
				return "FAIL rope part missing";
			}
			Rope rope = entityManager.GetComponentData<Rope>(ropeEntity);
			int nodeCount = (entityManager.HasBuffer<RopeNode>(ropeEntity) ? entityManager.GetBuffer<RopeNode>(ropeEntity).Length : 0);
			float3 position = PartGameplayUtil.Position(ropeEntity);
			float inverseMass = (entityManager.HasComponent<PhysicsMass>(ropeEntity) ? entityManager.GetComponentData<PhysicsMass>(ropeEntity).InverseMass : -1f);
			float minSegment = float.MaxValue;
			float maxSegment = 0f;
			float totalLength = 0f;
			float cutGap = 0f;
			string dump = "";
			if (nodeCount >= 2)
			{
				DynamicBuffer<RopeNode> nodes = entityManager.GetBuffer<RopeNode>(ropeEntity);
				for (int i = 1; i < nodeCount; i++)
				{
					float length = math.length(nodes[i].Position - nodes[i - 1].Position);
					dump = dump + (i == 1 ? "" : ",") + length.ToString("F2");
					// 断口那一段不再有约束，两半各挂一端自然越拉越开 —— 它不是「段长」，
					// 单独记为 cutGap 校验（BPSS 断绳后同样是两段各挂一头的残绳）。
					if (rope.Cut && i == rope.CutIndex)
					{
						cutGap = length;
						continue;
					}
					minSegment = math.min(minSegment, length);
					maxSegment = math.max(maxSegment, length);
					totalLength += length;
				}
			}
			bool leftOk = rope.LeftPart != Entity.Null;
			bool rightOk = rope.RightPart != Entity.Null;
			bool built = rope.Built && nodeCount == Rope.SegmentCount;
			// 运动学刚体（PhysicsVelocity 存在但没有 PhysicsMass）在 Unity.Physics 里
			// 由 Collider 的 IsKinematic 决定；bpsim 的 AddPhysicsComponents 对
			// IsKinematic 为真的部件不挂 PhysicsMass —— 所以「没有 PhysicsMass」就是冻结。
			bool frozen = !entityManager.HasComponent<PhysicsMass>(ropeEntity) || math.abs(inverseMass) <= 1E-05f;
			bool stayed = math.abs(position.y) <= 0.05f;
			bool segmentsOk = (nodeCount < 2 || (minSegment >= 0.3f && maxSegment <= 0.7f));
			bool cutOk = rope.Cut && rope.CutIndex == nodeCount / 2 && cutGap > Rope.RestLength;
			bool pass = built && leftOk && rightOk && frozen && stayed && segmentsOk && cutOk;
			return (pass ? "PASS" : "FAIL")
				+ " built=" + built
				+ " nodes=" + nodeCount
				+ " left=" + leftOk + " right=" + rightOk
				+ " inverseMass=" + inverseMass.ToString("F3")
				+ " y=" + position.y.ToString("F3")
				+ " segment=" + minSegment.ToString("F2") + ".." + maxSegment.ToString("F2")
				+ " length=" + totalLength.ToString("F2")
				+ " cut=" + rope.Cut + " at=" + rope.CutIndex
				+ " gap=" + cutGap.ToString("F2")
				+ " segs=[" + dump + "]";
		}

		// ------------------------------------------------------------------
		// Egg 0-5（普通蛋 + 重力/失重/穿透/虚空四种特殊蛋 + 异形胶蛋）
		// ------------------------------------------------------------------

		private static long s_eggBeforeCollides = -1L;

		private static long s_eggBeforePeerCollides = -1L;

		private static long s_eggBeforeOuterCollides = -1L;

		private static float s_eggBeforeGravity = float.NaN;

		private static float s_eggBeforePeerGravity = float.NaN;

		private static string s_eggEffectDumpBefore = "none";

		private static float s_eggYBefore = float.NaN;

		private static int s_eggBeforeComponent = -1;

		private static int s_eggPeerComponent = -1;

		private static int s_eggBeforeFinite = -1;

		private static int s_eggBeforeInfinite = -1;

		/// <summary>
		/// 特殊蛋默认就是「开启」（BPSS <c>PostInitialize</c> 对 2/3/4/5 自动打开），
		/// 而 <see cref="Scenario" /> 只在 trigger 之后记 state —— 所以点击前先把
		/// 「开启时」的证据抓进静态字段，才能证明关掉之后真的还原了。
		/// </summary>
		private static void TouchAndCapture(Entity egg, Entity peer, Entity outer)
		{
			s_eggBeforeCollides = ReadCollidesWith(egg);
			s_eggBeforePeerCollides = ReadCollidesWith(peer);
			s_eggBeforeOuterCollides = ReadCollidesWith(outer);
			s_eggBeforeGravity = ReadGravityFactor(egg);
			s_eggBeforePeerGravity = ReadGravityFactor(peer);
			CountLinearThresholds(peer, out s_eggBeforeFinite, out s_eggBeforeInfinite);
			s_eggEffectDumpBefore = DescribeEggEffects(egg);
			s_eggYBefore = PartGameplayUtil.Position(egg).y;
			EntityManager entityManager = PartGameplayUtil.Em;
			s_eggBeforeComponent = entityManager.HasComponent<PartConnectedComponent>(egg) ? entityManager.GetComponentData<PartConnectedComponent>(egg).Index : -1;
			s_eggPeerComponent = entityManager.HasComponent<PartConnectedComponent>(peer) ? entityManager.GetComponentData<PartConnectedComponent>(peer).Index : -1;
			PartSimulation.ProcessTouch(egg);
		}

		/// <summary>诊断用：把蛋身上残留的 <see cref="EggEffect" /> 缓冲区打成一行。</summary>
		private static string DescribeEggEffects(Entity egg)
		{
			EntityManager entityManager = PartGameplayUtil.Em;
			if (egg == Entity.Null || !entityManager.HasComponent<EggEffect>(egg))
			{
				return "none";
			}
			DynamicBuffer<EggEffect> effects = entityManager.GetBuffer<EggEffect>(egg);
			string text = effects.Length.ToString();
			for (int i = 0; i < effects.Length; i++)
			{
				text += " [" + effects[i].Kind + "->" + effects[i].Target.Index + "]";
			}
			return text;
		}

		/// <summary>诊断用：把碰撞体相关状态打成一行（-1 到底是「没实体」还是「没碰撞体」）。</summary>
		private static string DescribeCollider(Entity entity)
		{
			EntityManager entityManager = PartGameplayUtil.Em;
			if (entity == Entity.Null)
			{
				return "null";
			}
			bool exists = entityManager.Exists(entity);
			bool has = exists && entityManager.HasComponent<PhysicsCollider>(entity);
			bool created = has && entityManager.GetComponentData<PhysicsCollider>(entity).Value.IsCreated;
			return entity.Index + "/" + entity.Version + " exists=" + exists + " has=" + has + " created=" + created
				+ " collides=" + ReadCollidesWith(entity);
		}

		/// <summary>诊断用：列出模拟器里每个部件的重力系数。</summary>
		private static string DescribeGravityFactors()
		{
			PartManager partManager = PartManager.Instance;
			PartSimulator simulator = partManager != null ? partManager.PartSimulator : null;
			if (simulator == null)
			{
				return "no-sim";
			}
			EntityManager entityManager = PartGameplayUtil.Em;
			NativeList<Entity> parts = simulator.UnmanagedRef.Value.Parts;
			string text = string.Empty;
			for (int i = 0; i < parts.Length; i++)
			{
				Entity part = parts[i];
				float value = ReadGravityFactor(part);
				text += " " + part.Index + ":" + (float.IsNaN(value) ? "none" : value.ToString("F2"));
			}
			return text.Trim();
		}

		private static void BuildEggMasslessScene(PartScene scene)
		{
			// 失重蛋（索引 2，正交朝向 → 走「关重力」分支而不是「无质量」分支）：
			// 它和嵌在里面的铁框属于同一连通块，两者的 useGravity 都要被关掉。
			Place(scene, PartType.MetalFrame, 0, 0, 0, 0);
			Place(scene, PartType.Egg, 2, 0, 0, 0, overlay: true);
			CreateGround(-6f);
		}

		private static void TriggerEggMassless()
		{
			TouchAndCapture(FindPart(PartType.Egg, 2), FindPart(PartType.MetalFrame, 0), Entity.Null);
		}

		private static string VerifyEggMassless(PartScene scene)
		{
			EntityManager entityManager = PartGameplayUtil.Em;
			Entity egg = FindPart(PartType.Egg, 2);
			Entity frame = FindPart(PartType.MetalFrame, 0);
			if (egg == Entity.Null || frame == Entity.Null)
			{
				return "FAIL egg or frame missing";
			}
			Egg state = entityManager.GetComponentData<Egg>(egg);
			float gravityAfter = ReadGravityFactor(egg);
			float peerGravityAfter = ReadGravityFactor(frame);
			float y = PartGameplayUtil.Position(egg).y;
			bool offBefore = s_eggBeforeGravity == 0f && s_eggBeforePeerGravity == 0f;
			bool restored = float.IsNaN(gravityAfter) && float.IsNaN(peerGravityAfter);
			bool fell = y < -0.5f;
			bool pass = state.Kind == EggKind.Massless && offBefore && restored && fell;
			return (pass ? "PASS" : "FAIL")
				+ " kind=" + state.Kind
				+ " enabled=" + state.Enabled + " applied=" + state.Applied
				+ " gravityBefore=" + s_eggBeforeGravity.ToString("F2") + "/" + s_eggBeforePeerGravity.ToString("F2")
				+ " gravityAfter=" + gravityAfter.ToString("F2") + "/" + peerGravityAfter.ToString("F2")
				+ " y=" + y.ToString("F2")
				+ " effects=" + DescribeEggEffects(egg)
				+ " effectsBefore=" + s_eggEffectDumpBefore
				+ " comp=" + s_eggBeforeComponent + "/" + s_eggPeerComponent
				+ " eggGravityComp=" + entityManager.HasComponent<Unity.Physics.PhysicsGravityFactor>(egg)
				+ " peerGravityComp=" + entityManager.HasComponent<Unity.Physics.PhysicsGravityFactor>(frame)
				+ " all=" + DescribeGravityFactors();
		}

		private static void BuildEggGravityScene(PartScene scene)
		{
			// 重力蛋（索引 1，质量 50）：把 2 格外的 1 kg 铁框吸过来，自己吃反作用力。
			Place(scene, PartType.Egg, 1, 0, 0, 0);
			Place(scene, PartType.MetalFrame, 0, 2, 0, 0);
			CreateGround(-3f);
		}

		private static string VerifyEggGravity(PartScene scene)
		{
			EntityManager entityManager = PartGameplayUtil.Em;
			Entity egg = FindPart(PartType.Egg, 1);
			Entity frame = FindPart(PartType.MetalFrame, 0);
			if (egg == Entity.Null || frame == Entity.Null)
			{
				return "FAIL egg or frame missing";
			}
			Egg state = entityManager.GetComponentData<Egg>(egg);
			float eggVx = ReadVelocityX(egg);
			float frameVx = ReadVelocityX(frame);
			float mass = (entityManager.HasComponent<PhysicsMass>(egg)
				? 1f / math.max(entityManager.GetComponentData<PhysicsMass>(egg).InverseMass, 1E-06f)
				: 0f);
			bool heavy = mass > 40f && mass < 60f;
			bool attracted = frameVx < -1f;
			// 反作用力按质量分配：蛋 50kg、铁框 1kg，所以蛋的速度只有铁框的 1/50。
			// BPSS 的 AddForce 也是同一套牛顿第三定律，不能要求两边速度同量级。
			bool reaction = eggVx > 0.02f && math.sign(eggVx) == -math.sign(frameVx);
			// 重力蛋是每帧持续施加的力，没有「已生效」的一次性状态位（BPSS 也没有），
			// 所以这里只校验开关、质量和两边的速度响应。
			bool pass = state.Enabled && heavy && attracted && reaction;
			return (pass ? "PASS" : "FAIL")
				+ " enabled=" + state.Enabled
				+ " mass=" + mass.ToString("F1")
				+ " eggVx=" + eggVx.ToString("F2") + " frameVx=" + frameVx.ToString("F2");
		}

		private static void BuildEggGhostScene(PartScene scene)
		{
			// 穿透蛋（索引 3）：同连通块的碰撞体全关。这里蛋单独成块，
			// 所以只关它自己；点击后应还原，落到地面上而不是穿过去。
			Place(scene, PartType.Egg, 3, 0, 0, 0);
			CreateGround(-0.5f);
		}

		private static void TriggerEggGhost()
		{
			TouchAndCapture(FindPart(PartType.Egg, 3), Entity.Null, Entity.Null);
		}

		private static string VerifyEggGhost(PartScene scene)
		{
			EntityManager entityManager = PartGameplayUtil.Em;
			Entity egg = FindPart(PartType.Egg, 3);
			if (egg == Entity.Null)
			{
				return "FAIL egg missing";
			}
			Egg state = entityManager.GetComponentData<Egg>(egg);
			long after = ReadCollidesWith(egg);
			float y = PartGameplayUtil.Position(egg).y;
			bool offBefore = s_eggBeforeCollides == 0;
			bool restored = after != 0 && after != -1;
			bool landed = y > -0.5f;
			bool pass = state.Kind == EggKind.Ghost && offBefore && restored && landed;
			return (pass ? "PASS" : "FAIL")
				+ " kind=" + state.Kind + " enabled=" + state.Enabled + " applied=" + state.Applied
				+ " collidesBefore=" + s_eggBeforeCollides + " collidesAfter=" + after
				+ " y=" + y.ToString("F2")
				+ " yBefore=" + s_eggYBefore.ToString("F2")
				+ " effects=" + DescribeEggEffects(egg)
				+ " effectsBefore=" + s_eggEffectDumpBefore
				+ " diag=" + DescribeCollider(egg);
		}

		private static void BuildEggVoidScene(PartScene scene)
		{
			// 虚空蛋（索引 4，正交朝向）：半径默认 8 格，圈内碰撞体全关、圈外不动。
			// 铁框 0 在 2 格内、铁框 2 在 12 格外，正好一内一外。
			Place(scene, PartType.Egg, 4, 0, 0, 0);
			Place(scene, PartType.MetalFrame, 0, 2, 0, 0);
			Place(scene, PartType.WoodenFrame, 0, 12, 0, 0);
			CreateGround(-3f);
		}

		private static void TriggerEggVoid()
		{
			TouchAndCapture(FindPart(PartType.Egg, 4), FindPart(PartType.MetalFrame, 0), FindPart(PartType.WoodenFrame, 0));
		}

		private static string VerifyEggVoid(PartScene scene)
		{
			EntityManager entityManager = PartGameplayUtil.Em;
			Entity egg = FindPart(PartType.Egg, 4);
			Entity inner = FindPart(PartType.MetalFrame, 0);
			Entity outer = FindPart(PartType.WoodenFrame, 0);
			if (egg == Entity.Null || inner == Entity.Null || outer == Entity.Null)
			{
				return "FAIL parts missing";
			}
			Egg state = entityManager.GetComponentData<Egg>(egg);
			long eggAfter = ReadCollidesWith(egg);
			long innerAfter = ReadCollidesWith(inner);
			long outerAfter = ReadCollidesWith(outer);
			bool voidedBefore = s_eggBeforeCollides == 0 && s_eggBeforePeerCollides == 0;
			bool untouchedBefore = s_eggBeforeOuterCollides != 0 && s_eggBeforeOuterCollides != -1;
			bool restored = eggAfter != 0 && eggAfter != -1 && innerAfter != 0 && innerAfter != -1;
			bool untouchedAfter = outerAfter == s_eggBeforeOuterCollides;
			bool pass = state.Kind == EggKind.Void && state.VoidRadius > 7.5f && state.VoidRadius < 8.5f
				&& voidedBefore && untouchedBefore && restored && untouchedAfter;
			return (pass ? "PASS" : "FAIL")
				+ " kind=" + state.Kind + " radius=" + state.VoidRadius.ToString("F1")
				+ " enabled=" + state.Enabled
				+ " collidesBefore=" + s_eggBeforeCollides + "/" + s_eggBeforePeerCollides + "/" + s_eggBeforeOuterCollides
				+ " collidesAfter=" + eggAfter + "/" + innerAfter + "/" + outerAfter
				+ " effects=" + DescribeEggEffects(egg)
				+ " effectsBefore=" + s_eggEffectDumpBefore
				+ " diag=" + DescribeCollider(egg) + " | " + DescribeCollider(inner) + " | " + DescribeCollider(outer);
		}

		private static void BuildEggAlienScene(PartScene scene)
		{
			// 异形胶蛋（索引 5）：把同连通块所有关节的 breakForce 设成 ∞。
			// 两块相邻木框互相焊接，蛋嵌在 (0,0) 的框里 —— 三件同块、两根关节。
			Place(scene, PartType.WoodenFrame, 0, 0, 0, 0);
			Place(scene, PartType.WoodenFrame, 0, 1, 0, 0);
			Place(scene, PartType.Egg, 5, 0, 0, 0, overlay: true);
			CreateGround(-3f);
		}

		private static void TriggerEggAlien()
		{
			TouchAndCapture(FindPart(PartType.Egg, 5), FindPart(PartType.WoodenFrame, 0), Entity.Null);
		}

		private static string VerifyEggAlien(PartScene scene)
		{
			EntityManager entityManager = PartGameplayUtil.Em;
			Entity egg = FindPart(PartType.Egg, 5);
			Entity frame = FindPart(PartType.WoodenFrame, 0);
			if (egg == Entity.Null || frame == Entity.Null)
			{
				return "FAIL egg or frame missing";
			}
			Egg state = entityManager.GetComponentData<Egg>(egg);
			CountLinearThresholds(frame, out int finiteAfter, out int infiniteAfter);
			bool gluedBefore = s_eggBeforeFinite == 0 && s_eggBeforeInfinite > 0;
			bool restored = finiteAfter > 0;
			bool pass = state.Kind == EggKind.Alien && gluedBefore && restored;
			return (pass ? "PASS" : "FAIL")
				+ " kind=" + state.Kind + " enabled=" + state.Enabled + " applied=" + state.Applied
				+ " finiteBefore=" + s_eggBeforeFinite + " infiniteBefore=" + s_eggBeforeInfinite
				+ " finiteAfter=" + finiteAfter + " infiniteAfter=" + infiniteAfter
				+ " effects=" + DescribeEggEffects(egg)
				+ " effectsBefore=" + s_eggEffectDumpBefore;
		}

		/// <summary>
		/// 读碰撞体的 <c>CollidesWith</c> 掩码；没有碰撞体或未创建返回 -1。
		/// </summary>
		/// <remarks>
		/// 必须用 <see cref="long" />：「碰撞全部物体」的掩码是 <c>0xFFFFFFFF</c>，
		/// 塞进 <see cref="int" /> 就是 -1，会和「没有碰撞体」的哨兵撞值 ——
		/// 于是「还原成全掩码」会被误判成「没还原」。
		/// </remarks>
		private static long ReadCollidesWith(Entity entity)
		{
			EntityManager entityManager = PartGameplayUtil.Em;
			if (entity == Entity.Null || !entityManager.HasComponent<PhysicsCollider>(entity))
			{
				return -1L;
			}
			PhysicsCollider collider = entityManager.GetComponentData<PhysicsCollider>(entity);
			if (!collider.Value.IsCreated)
			{
				return -1L;
			}
			return (long)collider.Value.Value.GetCollisionFilter().CollidesWith;
		}

		/// <summary>读重力系数；没有 <c>PhysicsGravityFactor</c> 组件返回 NaN（= 用世界默认重力）。</summary>
		private static float ReadGravityFactor(Entity entity)
		{
			EntityManager entityManager = PartGameplayUtil.Em;
			if (entity == Entity.Null || !entityManager.HasComponent<PhysicsGravityFactor>(entity))
			{
				return float.NaN;
			}
			return entityManager.GetComponentData<PhysicsGravityFactor>(entity).Value;
		}

		private static float ReadVelocityX(Entity entity)
		{
			EntityManager entityManager = PartGameplayUtil.Em;
			if (entity == Entity.Null || !entityManager.HasComponent<PhysicsVelocity>(entity))
			{
				return 0f;
			}
			return entityManager.GetComponentData<PhysicsVelocity>(entity).Linear.x;
		}

		/// <summary>
		/// 统计挂在 <paramref name="part" /> 上的关节里，线性约束的冲量阈值有多少条
		/// 是有限的（可断）、多少条是 ∞（不可断）。
		/// </summary>
		private static void CountLinearThresholds(Entity part, out int finite, out int infinite)
		{
			finite = 0;
			infinite = 0;
			EntityManager entityManager = PartGameplayUtil.Em;
			PartManager manager = PartManager.Instance;
			if (part == Entity.Null || manager == null || manager.PartSimulator == null)
			{
				return;
			}
			PartSimulatorUnmanaged simulator = manager.PartSimulator.Unmanaged;
			for (int i = 0; i < simulator.Joints.Length; i++)
			{
				Entity jointEntity = simulator.Joints[i];
				if (!entityManager.Exists(jointEntity)
					|| !entityManager.HasComponent<PhysicsJoint>(jointEntity)
					|| !entityManager.HasComponent<PhysicsConstrainedBodyPair>(jointEntity))
				{
					continue;
				}
				PhysicsConstrainedBodyPair pair = entityManager.GetComponentData<PhysicsConstrainedBodyPair>(jointEntity);
				if (pair.EntityA != part && pair.EntityB != part)
				{
					continue;
				}
				PhysicsJoint joint = entityManager.GetComponentData<PhysicsJoint>(jointEntity);
				int count = joint.GetConstraintCount();
				for (int c = 0; c < count; c++)
				{
					Constraint constraint = joint[c];
					if (constraint.Type != ConstraintType.Linear)
					{
						continue;
					}
					if (math.any(math.abs(constraint.MaxImpulse) == float.PositiveInfinity))
					{
						infinite++;
					}
					else
					{
						finite++;
					}
				}
			}
		}

		private static bool Place(PartScene scene, PartType type, int index, int x, int y, int rotation, bool overlay = false)
		{
			PartManager manager = PartManager.Instance;
			PartTypeInfo info = new PartTypeInfo(type, index);
			Entity prefab = manager.EntitySpawner.FindPrefab(info);
			if (prefab == Entity.Null)
			{
				Debug.LogWarning("[PartGameplayQA] no prefab " + type + "#" + index);
				return false;
			}
			bool placed = scene.Unmanaged.PlacePartAt(new PartCreationRequest(prefab, scene.SceneID, info, x, y, rotation, false), overlay);
			if (!placed)
			{
				Debug.LogWarning("[PartGameplayQA] rejected " + type + "#" + index + " at " + x + "," + y);
			}
			return placed;
		}

		/// <summary>
		/// 造一块静态地面。<c>topY</c> 是地面上表面高度。
		/// </summary>
		/// <remarks>
		/// BPSS 的轮子靠 <c>Physics.Raycast</c> 找地面，没命中就不出力（<c>m_hasContact = false</c>）。
		/// bpsim 的空场景里没有任何碰撞体，所以轮子用例必须先铺地，否则永远测不到推力。
		/// 静态体的条件（<c>PhysicsWorldData.StaticEntityGroup</c>）：
		/// <c>PhysicsCollider</c> + <c>PhysicsWorldIndex</c> + <c>LocalTransform</c>/<c>LocalToWorld</c>，
		/// 且**没有** <c>PhysicsVelocity</c>。
		/// </remarks>
		private static Entity CreateGround(float topY)
		{
			DestroyGround();
			EntityManager entityManager = PartGameplayUtil.Em;
			if (entityManager.World == null || !entityManager.World.IsCreated)
			{
				return Entity.Null;
			}
			Entity entity = entityManager.CreateEntity();
			float3 center = new float3(0f, topY - 0.5f, 0f);
			entityManager.AddComponentData(entity, LocalTransform.FromPosition(center));
			entityManager.AddComponentData(entity, new LocalToWorld
			{
				Value = float4x4.Translate(center)
			});
			entityManager.AddComponentData(entity, new PhysicsCollider
			{
				Value = PartPhysics.CreateBox(float3.zero, new float3(160f, 1f, 8f))
			});
			entityManager.AddSharedComponent(entity, new PhysicsWorldIndex(0));
			s_ground = entity;
			return entity;
		}

		private static void DestroyGround()
		{
			if (s_ground == Entity.Null)
			{
				return;
			}
			EntityManager entityManager = PartGameplayUtil.Em;
			if (entityManager.World != null && entityManager.World.IsCreated && entityManager.Exists(s_ground))
			{
				entityManager.DestroyEntity(s_ground);
			}
			s_ground = Entity.Null;
		}

		/// <summary>读一个部件实体的 <see cref="Wheel" /> 组件（没有则返回 default）。</summary>
		private static Wheel ReadWheel(PartType type, int index)
		{
			Entity entity = FindPart(type, index);
			EntityManager entityManager = PartGameplayUtil.Em;
			if (entity == Entity.Null || !entityManager.HasComponent<Wheel>(entity))
			{
				return default(Wheel);
			}
			return entityManager.GetComponentData<Wheel>(entity);
		}

		// ------------------------------------------------------------------
		// Verifiers
		// ------------------------------------------------------------------

		private static string VerifyHook(PartScene scene)
		{
			PartGameplayManager manager = PartGameplayManager.Instance;
			string state = (manager != null) ? manager.Describe() : "no manager";
			Entity hook = FindPart(PartType.GrapplingHook, 0);
			Entity target = FindPart(PartType.WoodenFrame, 0);
			float3 hookPosition = PartGameplayUtil.Position(hook);
			float3 targetPosition = PartGameplayUtil.Position(target);
			bool attached = state.Contains("attached=True");
			float pulled = math.distance(hookPosition.xy, new float2(3f, 0f));
			bool moved = pulled > 0.2f;
			return (attached && moved ? "PASS" : "FAIL") + " hook attached=" + attached + " pulled=" + pulled.ToString("F2")
				+ " hook@(" + hookPosition.x.ToString("F2") + "," + hookPosition.y.ToString("F2") + ")"
				+ " target@(" + targetPosition.x.ToString("F2") + "," + targetPosition.y.ToString("F2") + ")";
		}

		private static string VerifyGun(PartScene scene)
		{
			PartGameplayManager manager = PartGameplayManager.Instance;
			string state = (manager != null) ? manager.Describe() : "no manager";
			// 机炮现在是「开着就持续突突」（0.2 s 一发），所以不能再靠 shots=1..3 精确匹配。
			int shots = ParseCount(state, "shots=");
			bool fired = shots >= 2;
			bool reportedHit = ParseCount(state, "hits=") >= 1;
			// 真实弹丸：BPSS 的 AutoGun 打出去的是 ExplodingGrapplingHookProjectile。
			// 「开过火」之外还必须看到实体生成 + 爆炸 + 目标被爆炸冲量推开。
			int spawned = AutoGunProjectileSpawner.SpawnCount;
			int exploded = AutoGunProjectileSystem.ExplosionCount;
			Entity target = FindPart(PartType.MetalFrame, 0);
			float3 targetNow = PartGameplayUtil.Position(target);
			// 目标原位是 (5,0)。只有爆炸的径向冲量能把它推开这么远——弹丸按 TTL 空爆
			// （飞出 20 m 自毁）不会碰到目标，所以位移本身就是「弹丸真的命中了」的证据。
			float displacement = math.distance(targetNow.xy, new float2(5f, 0f));
			bool hitTarget = displacement > 1f;
			float3 explosion = AutoGunProjectileSystem.LastExplosion;
			float explosionFromMuzzle = (exploded > 0) ? math.distance(explosion.xy, new float2(2.55f, 0f)) : 0f;
			bool flew = exploded > 0 && explosionFromMuzzle > 1.2f;
			// 视觉：弹丸必须真的被提交绘制，否则就是用户看到的「没有子弹射出」。
			int drawn = AutoGunProjectileSystem.DrawnProjectiles;
			bool pass = fired && spawned >= 1 && exploded >= 1 && flew && hitTarget && drawn >= 1;
			return (pass ? "PASS" : "FAIL") + " autogun fired=" + fired + " spawned=" + spawned + " exploded=" + exploded
				+ " reportedHit=" + reportedHit + " flew=" + flew + " drawn=" + drawn
				+ " explosionFromMuzzle=" + explosionFromMuzzle.ToString("F2")
				+ " targetDisplacement=" + displacement.ToString("F2")
				+ " target@(" + targetNow.x.ToString("F2") + "," + targetNow.y.ToString("F2") + ") state=" + state.Replace("\n", " | ");
		}

		/// <summary>机炮场景的触发器：先清 QA 探针，再点一下机炮。</summary>
		private static Action TriggerGun()
		{
			return delegate
			{
				AutoGunProjectileSystem.ResetProbes();
				Entity part = FindPart(PartType.GrapplingHook, 6);
				if (part == Entity.Null)
				{
					Debug.LogWarning("[PartGameplayQA] part not found GrapplingHook#6");
					return;
				}
				PartSimulation.ProcessTouch(part);
			};
		}

		/// <summary>
		/// 空旷场景：车体 + 机炮，<b>前方没有任何目标</b>。
		/// </summary>
		/// <remarks>
		/// 这是用户实测的用法——点一下机炮就该一直朝前突突。初版实现要求锥形范围内有异体部件
		/// 才开火，于是空旷场景里点了完全没反应，所以单独加这个场景把「无目标也开火」钉住。
		/// </remarks>
		private static void BuildGunFreeScene(PartScene scene)
		{
			Place(scene, PartType.WoodenFrame, 0, 0, 0, 0);
			Place(scene, PartType.WoodenFrame, 0, 1, 0, 0);
			Place(scene, PartType.GrapplingHook, 6, 2, 0, 0);
			CreateGround(-3f);
		}

		private static string VerifyGunFree(PartScene scene)
		{
			PartGameplayManager manager = PartGameplayManager.Instance;
			string state = (manager != null) ? manager.Describe() : "no manager";
			int shots = ParseCount(state, "shots=");
			int spawned = AutoGunProjectileSpawner.SpawnCount;
			int exploded = AutoGunProjectileSystem.ExplosionCount;
			// 0.2 s 一发、场景跑 1.8 s → 至少 8 发；碰撞世界里没有任何异体部件，
			// 所以「生成了弹丸」+「飞到 TTL 自爆」就是判据，不要求命中目标。
			bool pass = shots >= 4 && spawned >= 4 && exploded >= 1;
			return (pass ? "PASS" : "FAIL") + " gunfree shots=" + shots + " spawned=" + spawned + " exploded=" + exploded
				+ " state=" + state.Replace("\n", " | ");
		}

		/// <summary>从 <c>Describe()</c> 文本里取出 <c>prefix</c> 后面的整数（取第一个匹配）。</summary>
		private static int ParseCount(string text, string prefix)
		{
			int at = text.IndexOf(prefix, System.StringComparison.Ordinal);
			if (at < 0)
			{
				return 0;
			}
			int i = at + prefix.Length;
			int value = 0;
			bool any = false;
			while (i < text.Length && text[i] >= '0' && text[i] <= '9')
			{
				value = value * 10 + (text[i] - '0');
				any = true;
				i++;
			}
			return any ? value : 0;
		}

		private static string VerifyGenerator(PartScene scene)
		{			PartSimulatorUnmanaged simulator = PartManager.Instance.PartSimulator.Unmanaged;
			EntityManager entityManager = PartGameplayUtil.Em;
			// 生成器在 (2,0) 朝 +x：索引 9 的 CopyDistance = 8，检测距离 3，
			// 于是后方第 1/2/3 格的模板分别落到 x = 2+10 / 2+9 / 2+8 = 12 / 11 / 10。
			bool[] found = new bool[3];
			int generated = 0;
			for (int i = 0; i < simulator.Parts.Length; i++)
			{
				Entity entity = simulator.Parts[i];
				if (!entityManager.Exists(entity) || !entityManager.HasComponent<PartTypeValue>(entity) || !entityManager.HasComponent<PartTransform>(entity))
				{
					continue;
				}
				PartTypeValue type = entityManager.GetComponentData<PartTypeValue>(entity);
				if (type.Type == PartType.GrapplingHook)
				{
					continue;
				}
				PartTransform transform = entityManager.GetComponentData<PartTransform>(entity);
				if (transform.X < 3)
				{
					continue;
				}
				generated++;
				if (transform.Y == 0)
				{
					int slot = transform.X - 10;
					if (slot >= 0 && slot < found.Length)
					{
						found[slot] = true;
					}
				}
			}
			bool chain = found[0] && found[1] && found[2];
			// 三个复制体彼此相邻，应当焊成一条链：4 个部件 → 至少 5 条连接（3 原生 + 2 复制链）。
			bool welded = simulator.Joints.Length >= 5;
			return (chain && welded ? "PASS" : "FAIL") + " generatedParts=" + generated
				+ " at10=" + found[0] + " at11=" + found[1] + " at12=" + found[2]
				+ " total=" + simulator.Parts.Length + " joints=" + simulator.Joints.Length
				+ " state=" + Snapshot().Replace("\n", " | ");
		}

		private static string VerifyLight(PartScene scene)
		{
			PartGameplayManager manager = PartGameplayManager.Instance;
			string state = (manager != null) ? manager.Describe() : "no manager";
			bool voided = state.Contains("voided=2") || state.Contains("voided=3") || state.Contains("voided=1");
			// AutoControlLight（索引 6）除了虚化，还要按「朝向 + 开关」在普通那对与 *Full 那对
			// 之间选一对显示（BPSS CurrentType）。prefab 里 *Full 的两张图原本都是可见的，
			// 所以这里同时校验「恰好只有 Active 普通图可见」。
			string visual = VerifyLampVisual(scene, PartType.PointLight, 6, true, false);
			bool visualOk = visual.StartsWith("PASS");
			return ((voided && visualOk) ? "PASS" : "FAIL") + " voidlight voided=" + voided
				+ " sprites=" + (visual.StartsWith("PASS") ? "ok" : "bad")
				+ " state=" + state.Replace("\n", " | ") + " | visual=" + visual.Replace("\n", " | ");
		}

		/// <summary>
		/// 灯族验证：点一次后，Active / Inactive 两张 sprite 的可见性必须互换。
		/// </summary>
		/// <remarks>
		/// BPSS <c>PointLight.OnTouch</c> 只做三件事：翻转 <c>activated</c>、
		/// <c>activeSprite.SetActive(activated)</c>、<c>inactiveSprite.SetActive(!activated)</c>
		/// （以及 <c>lightSource.isEnabled</c>，bpsim 没有光源层）。所以可观测的验收点就是贴图可见性，
		/// 由子实体上的 <see cref="PointLightVisual"/> + <c>DisableRendering</c> 表达。
		/// </remarks>
		private static string VerifyLampVisual(PartScene scene, PartType type, int index, bool expectOn, bool expectFull)
		{
			Entity part = FindPart(type, index);
			if (part == Entity.Null)
			{
				return "FAIL part missing " + type + "#" + index;
			}
			EntityManager entityManager = PartGameplayUtil.Em;
			if (!entityManager.HasBuffer<LinkedEntityGroup>(part))
			{
				return "FAIL no LinkedEntityGroup " + type + "#" + index;
			}
			int sprites = 0;
			int visibleWanted = 0;
			int hiddenWanted = 0;
			int wrong = 0;
			DynamicBuffer<LinkedEntityGroup> group = entityManager.GetBuffer<LinkedEntityGroup>(part);
			for (int i = 0; i < group.Length; i++)
			{
				Entity child = group[i].Value;
				if (!entityManager.Exists(child) || !entityManager.HasComponent<PointLightVisual>(child))
				{
					continue;
				}
				PointLightVisual visual = entityManager.GetComponentData<PointLightVisual>(child);
				bool wanted = visual.Active == expectOn && visual.Full == expectFull;
				bool visible = !entityManager.HasComponent<DisableRendering>(child);
				sprites++;
				if (wanted)
				{
					if (visible)
					{
						visibleWanted++;
					}
					else
					{
						wrong++;
					}
				}
				else if (visible)
				{
					wrong++;
				}
				else
				{
					hiddenWanted++;
				}
			}
			bool ok = sprites > 0 && wrong == 0 && visibleWanted > 0;
			return (ok ? "PASS" : "FAIL") + " " + type + "#" + index + " on=" + expectOn + " full=" + expectFull
				+ " sprites=" + sprites + " visible=" + visibleWanted + " hidden=" + hiddenWanted
				+ " wrong=" + wrong
				+ " state=" + ((PartGameplayManager.Instance != null) ? PartGameplayManager.Instance.Describe().Replace("\n", " | ") : "no manager");
		}

		/// <summary>
		/// 聚光灯验证：点击后灯锥容器 <c>Container</c> 的 x 缩放要从 0 往 3 涨。
		/// </summary>
		/// <remarks>
		/// BPSS <c>SpotLight.FixedUpdate</c>：
		/// <c>m_lightConeContainer.localScale = Lerp(cur, new Vector3(target,1,1), dt*30)</c>，
		/// <c>target = activated ? 3 : 0.001</c>。bpsim 用 <c>PostTransformScale</c> 承载非等比缩放
		/// （<c>LocalTransform.Scale</c> 只能等比），所以验收读这个组件。
		/// </remarks>
		private static string VerifySpotLight(PartScene scene)
		{
			Entity part = FindPart(PartType.SpotLight, 0);
			if (part == Entity.Null)
			{
				return "FAIL spotlight missing";
			}
			EntityManager entityManager = PartGameplayUtil.Em;
			if (!entityManager.HasBuffer<LinkedEntityGroup>(part))
			{
				return "FAIL no LinkedEntityGroup";
			}
			int found = 0;
			float scaleX = 0f;
			float scaleY = 0f;
			float localScale = 0f;
			float worldScaleX = 0f;
			DynamicBuffer<LinkedEntityGroup> group = entityManager.GetBuffer<LinkedEntityGroup>(part);
			for (int i = 0; i < group.Length; i++)
			{
				Entity child = group[i].Value;
				if (!entityManager.Exists(child) || !entityManager.HasComponent<SpotLightCone>(child))
				{
					continue;
				}
				found++;
				if (entityManager.HasComponent<PostTransformScale>(child))
				{
					float3x3 value = entityManager.GetComponentData<PostTransformScale>(child).Value;
					scaleX = math.length(value.c0);
					scaleY = math.length(value.c1);
				}
				if (entityManager.HasComponent<LocalTransform>(child))
				{
					localScale = entityManager.GetComponentData<LocalTransform>(child).Scale;
				}
				if (entityManager.HasComponent<LocalToWorld>(child))
				{
					worldScaleX = math.length(entityManager.GetComponentData<LocalToWorld>(child).Value.c0);
				}
			}
			bool grown = scaleX > 2f && math.abs(scaleY - 1f) < 0.01f;
			return (grown ? "PASS" : "FAIL") + " spotlight cone=(" + scaleX.ToString("F3") + "," + scaleY.ToString("F3")
				+ ") localScale=" + localScale.ToString("F3")
				+ " worldX=" + worldScaleX.ToString("F3")
				+ " found=" + found
				+ " state=" + ((PartGameplayManager.Instance != null) ? PartGameplayManager.Instance.Describe().Replace("\n", " | ") : "no manager");
		}

		private static string VerifyRocket(PartScene scene)
		{
			Entity rocket = FindPart(PartType.Rocket, 0);
			float3 position = PartGameplayUtil.Position(rocket);
			float3 velocity = PartGameplayUtil.LinearVelocity(rocket);
			bool moved = position.y > 0.5f && velocity.y > 1f && math.abs(position.x) < 0.5f;
			return (moved ? "PASS" : "FAIL") + " rocket travelY=" + position.y.ToString("F2")
				+ " speedY=" + velocity.y.ToString("F2") + " pos=(" + position.x.ToString("F2") + ","
				+ position.y.ToString("F2") + ") state=" + Snapshot().Replace("\n", " | ");
		}

		private static string VerifyTimeBomb(PartScene scene)
		{
			Entity target = FindPart(PartType.MetalFrame, 0);
			Entity bomb = FindPart(PartType.TimeBomb, 0);
			float3 targetPosition = PartGameplayUtil.Position(target);
			float3 bombPosition = PartGameplayUtil.Position(bomb);
			float separation = -targetPosition.y;
			return (bomb == Entity.Null && separation > 4f ? "PASS" : "FAIL") + " timebomb removed=" + (bomb == Entity.Null) + " travelDown=" + separation.ToString("F2")
				+ " target@(" + targetPosition.x.ToString("F2") + "," + targetPosition.y.ToString("F2") + ")"
				+ " bomb@(" + bombPosition.x.ToString("F2") + "," + bombPosition.y.ToString("F2") + ")"
				+ " state=" + Snapshot().Replace("\n", " | ");
		}

		private static string VerifyBalloon(PartScene scene)
		{
			Entity balloon = FindPart(PartType.Balloon, 0);
			Entity frame = FindPart(PartType.MetalFrame, 0);
			float3 position = PartGameplayUtil.Position(balloon);
			float3 framePosition = PartGameplayUtil.Position(frame);
			float rise = ComponentThrust.NearbyRiseSpeed(balloon, 1.6f);
			// The chassis was placed at cell y=0: free fall would put it near -9.6 after
			// 1.4 s, a lifted one climbs instead.
			bool lifted = framePosition.y > 1.5f;
			return (lifted ? "PASS" : "FAIL") + " balloon frame y=" + framePosition.y.ToString("F2")
				+ " rise=" + rise.ToString("F2") + " balloon@(" + position.x.ToString("F2") + ","
				+ position.y.ToString("F2") + ") state=" + Snapshot().Replace("\n", " | ");
		}

		private static string VerifyUmbrella(PartScene scene)
		{
			Entity umbrella = FindPart(PartType.Umbrella, 0);
			Entity frame = FindPart(PartType.MetalFrame, 0);
			float3 position = PartGameplayUtil.Position(umbrella);
			float3 velocity = PartGameplayUtil.LinearVelocity(umbrella);
			float3 framePosition = PartGameplayUtil.Position(frame);
			// Opened canopy clamps the descent: 1.4 s of free fall reaches about -9.6,
			// the braked contraption stays well above that.
			bool braked = framePosition.y > -6f && PartGameplayManager.Instance.Find(umbrella) is UmbrellaBehavior canopy && canopy.Open;
			return (braked ? "PASS" : "FAIL") + " umbrella y=" + position.y.ToString("F2")
				+ " frame y=" + framePosition.y.ToString("F2")
				+ " vy=" + velocity.y.ToString("F2") + " state=" + Snapshot().Replace("\n", " | ");
		}

		private static string VerifyPoweredUmbrella(PartScene scene)
		{
			Entity umbrella = FindPart(PartType.PoweredUmbrella, 0);
			Entity frame = FindPart(PartType.MetalFrame, 0);
			float3 position = PartGameplayUtil.Position(umbrella);
			float3 velocity = PartGameplayUtil.LinearVelocity(umbrella);
			float3 framePosition = PartGameplayUtil.Position(frame);
			// The powered canopy nearly cancels gravity, so the contraption hovers
			// while a free fall would be around -9.6 after the same 1.4 s.
			bool hovering = framePosition.y > -3f;
			return (hovering ? "PASS" : "FAIL") + " poweredumbrella y=" + position.y.ToString("F2")
				+ " frame y=" + framePosition.y.ToString("F2")
				+ " vy=" + velocity.y.ToString("F2") + " state=" + Snapshot().Replace("\n", " | ");
		}

		private static string VerifyBellows(PartScene scene)
		{
			Entity bellows = FindPart(PartType.Bellows, 0);
			float3 bellowsPosition = PartGameplayUtil.Position(bellows);
			// The boost lasts 0.5 s with a quadratic ramp, so the contraption gains a
			// little over 1 m/s.  Measure that against the control body (same part,
			// same drop height, no bellows) instead of an absolute height, which
			// would mostly be gravity.
			Entity control = Entity.Null;
			float best = float.MinValue;
			PartManager manager = PartManager.Instance;
			if (manager != null && manager.PartSimulator != null)
			{
				PartSimulatorUnmanaged simulator = manager.PartSimulator.Unmanaged;
				for (int i = 0; i < simulator.Parts.Length; i++)
				{
					Entity candidate = simulator.Parts[i];
					if (!PartGameplayUtil.Alive(candidate))
					{
						continue;
					}
					PartTypeInfo? candidateInfo = PartGameplayUtil.TypeInfo(candidate);
					if (!candidateInfo.HasValue || candidateInfo.Value.PartType != PartType.MetalFrame)
					{
						continue;
					}
					float x = PartGameplayUtil.Position(candidate).x;
					if (x > best)
					{
						best = x;
						control = candidate;
					}
				}
			}
			float3 controlPosition = PartGameplayUtil.Position(control);
			float gain = bellowsPosition.y - controlPosition.y;
			bool lifted = gain > 0.15f;
			return (lifted ? "PASS" : "FAIL") + " bellows gain=" + gain.ToString("F2")
				+ " bellows y=" + bellowsPosition.y.ToString("F2")
				+ " control y=" + controlPosition.y.ToString("F2")
				+ " state=" + Snapshot().Replace("\n", " | ");
		}

		private static string VerifyMotorWheel(PartScene scene)
		{
			PartGameplayManager manager = PartGameplayManager.Instance;
			string state = (manager != null) ? manager.Describe() : "no manager";
			Entity wheel = FindPart(PartType.MotorWheel, 0);
			Entity frame = FindPart(PartType.MetalFrame, 0);
			float3 position = PartGameplayUtil.Position(wheel);
			float3 velocity = PartGameplayUtil.LinearVelocity(wheel);
			float frameX = PartGameplayUtil.Position(frame).x;
			Wheel component = ReadWheel(PartType.MotorWheel, 0);
			// 轮子必须先真的碰到过地面（BPSS 的 m_hasContact 分支），否则说明射线/地面没接上，
			// 那时候即使位移是正的也不是轮子推出来的。用累计帧数而不是单帧快照：
			// 轮子加速后会有短暂的弹跳离地，只看最后一帧会误判成「从没接触过」。
			bool contact = component.HasContact || component.ContactTicks > 0;
			bool moved = frameX > 0.5f || position.x > 0.5f || velocity.x > 1f;
			return (contact && moved ? "PASS" : "FAIL") + " motorwheel contact=" + contact
				+ " radius=" + component.Radius.ToString("F2")
				+ " x=" + position.x.ToString("F2")
				+ " frameX=" + frameX.ToString("F2")
				+ " vx=" + velocity.x.ToString("F2")
				+ " throttle=" + component.Throttle.ToString("F2")
				+ " state=" + state.Replace("\n", " | ");
		}

		private static string VerifyStickyWheel(PartScene scene)
		{
			Entity wheel = FindPart(PartType.StickyWheel, 0);
			Entity frame = FindPart(PartType.MetalFrame, 0);
			float3 position = PartGameplayUtil.Position(wheel);
			float3 velocity = PartGameplayUtil.LinearVelocity(wheel);
			float frameX = PartGameplayUtil.Position(frame).x;
			Wheel component = ReadWheel(PartType.StickyWheel, 0);
			bool contact = component.HasContact || component.ContactTicks > 0;
			bool moved = frameX > 0.5f || position.x > 0.5f || velocity.x > 1f;
			return (contact && moved ? "PASS" : "FAIL") + " stickywheel contact=" + contact
				+ " kind=" + component.Kind
				+ " x=" + position.x.ToString("F2")
				+ " frameX=" + frameX.ToString("F2")
				+ " vx=" + velocity.x.ToString("F2")
				+ " state=" + Snapshot().Replace("\n", " | ");
		}

		private static string VerifyOffRoadWheel(PartScene scene)
		{
			Entity wheel = FindPart(PartType.MotorWheel, 7);
			Entity frame = FindPart(PartType.MetalFrame, 0);
			float3 position = PartGameplayUtil.Position(wheel);
			float3 velocity = PartGameplayUtil.LinearVelocity(wheel);
			float frameX = PartGameplayUtil.Position(frame).x;
			Wheel component = ReadWheel(PartType.MotorWheel, 7);
			bool contact = component.HasContact || component.ContactTicks > 0;
			bool moved = frameX > 0.5f || position.x > 0.5f || velocity.x > 1f;
			return (contact && moved ? "PASS" : "FAIL") + " offroadwheel contact=" + contact
				+ " kind=" + component.Kind
				+ " radius=" + component.Radius.ToString("F2")
				+ " x=" + position.x.ToString("F2")
				+ " frameX=" + frameX.ToString("F2")
				+ " vx=" + velocity.x.ToString("F2")
				+ " state=" + Snapshot().Replace("\n", " | ");
		}

		private static string VerifyCartWheel(PartScene scene)
		{
			Entity wheel = FindPart(PartType.CartWheel, 0);
			Wheel component = ReadWheel(PartType.CartWheel, 0);
			float3 position = PartGameplayUtil.Position(wheel);
			float3 velocity = PartGameplayUtil.LinearVelocity(wheel);
			// CartWheel 不施力，唯一可观测的输出就是视觉滚动角（BPSS m_angle）与
			// WheelPivot 的实际旋转。水平初速 6 m/s、周长 2π*0.36 ≈ 2.26，
			// 0.8 s 里角度应该累计到几百度。
			float angle = math.abs(component.Angle);
			float pivotRotation = ReadPivotRotation(PartType.CartWheel, 0);
			bool rolled = angle > 30f && pivotRotation > 0.05f;
			return (rolled ? "PASS" : "FAIL") + " cartwheel angle=" + angle.ToString("F2")
				+ " pivotSin=" + pivotRotation.ToString("F3")
				+ " radius=" + component.Radius.ToString("F2")
				+ " contact=" + component.HasContact
				+ " x=" + position.x.ToString("F2")
				+ " vx=" + velocity.x.ToString("F2");
		}

		private static string VerifyGearbox(PartScene scene)
		{
			Entity gearbox = FindPart(PartType.Gearbox, 0);
			Entity wheel = FindPart(PartType.MotorWheel, 0);
			EntityManager entityManager = PartGameplayUtil.Em;
			bool activated = gearbox != Entity.Null && entityManager.HasComponent<Gearbox>(gearbox)
				&& entityManager.GetComponentData<Gearbox>(gearbox).Activated;
			Wheel component = ReadWheel(PartType.MotorWheel, 0);
			// BPSS 的倒车不是齿轮箱自己出力，而是轮子每帧问 IsEnabled() 后把推力取反：
			// 所以这里断言的是「同一焊接组件里的轮子推力已经变负」。
			bool reversing = component.Throttle < 0f;
			float3 velocity = PartGameplayUtil.LinearVelocity(wheel);
			int visibleActive = CountVisibleGearboxSprite(gearbox, active: true);
			return (activated && reversing ? "PASS" : "FAIL") + " gearbox activated=" + activated
				+ " throttle=" + component.Throttle.ToString("F2")
				+ " reversing=" + reversing
				+ " vx=" + velocity.x.ToString("F2")
				+ " activeSprites=" + visibleActive
				+ " state=" + Snapshot().Replace("\n", " | ");
		}

		/// <summary>读轮子 <c>WheelPivot</c> 子实体当前旋转的 sin(θ/2) 幅值，用来判断它是否在转。</summary>
		private static float ReadPivotRotation(PartType type, int index)
		{
			Entity wheel = FindPart(type, index);
			EntityManager entityManager = PartGameplayUtil.Em;
			if (wheel == Entity.Null || !entityManager.HasComponent<LinkedEntityGroup>(wheel))
			{
				return 0f;
			}
			DynamicBuffer<LinkedEntityGroup> group = entityManager.GetBuffer<LinkedEntityGroup>(wheel);
			for (int i = 1; i < group.Length; i++)
			{
				Entity child = group[i].Value;
				if (!entityManager.HasComponent<WheelPivot>(child) || entityManager.GetComponentData<WheelPivot>(child).Fake)
				{
					continue;
				}
				if (!entityManager.HasComponent<LocalTransform>(child))
				{
					continue;
				}
				float4 value = entityManager.GetComponentData<LocalTransform>(child).Rotation.value;
				return math.abs(value.z);
			}
			return 0f;
		}

		/// <summary>统计齿轮箱某个贴图子物体当前是否可见（<c>DisableRendering</c> 取反）。</summary>
		private static int CountVisibleGearboxSprite(Entity gearbox, bool active)
		{
			EntityManager entityManager = PartGameplayUtil.Em;
			if (gearbox == Entity.Null || !entityManager.HasComponent<LinkedEntityGroup>(gearbox))
			{
				return 0;
			}
			int count = 0;
			DynamicBuffer<LinkedEntityGroup> group = entityManager.GetBuffer<LinkedEntityGroup>(gearbox);
			for (int i = 1; i < group.Length; i++)
			{
				Entity child = group[i].Value;
				if (!entityManager.HasComponent<GearboxVisual>(child))
				{
					continue;
				}
				if (entityManager.GetComponentData<GearboxVisual>(child).Active != active)
				{
					continue;
				}
				if (!entityManager.HasComponent<DisableRendering>(child))
				{
					count++;
				}
			}
			return count;
		}

		private static string VerifyJetEngine(PartScene scene)
		{
			PartGameplayManager manager = PartGameplayManager.Instance;
			string state = (manager != null) ? manager.Describe() : "no manager";
			Entity jet = FindPart(PartType.JetEngine, 0);
			float3 position = PartGameplayUtil.Position(jet);
			float3 velocity = PartGameplayUtil.LinearVelocity(jet);
			// Placed at rotation 0 the thrust is +x; free fall cannot produce any
			// horizontal motion, so 0.5 m is already unambiguous.
			bool thrust = position.x > 0.5f || velocity.x > 1f;
			return (thrust ? "PASS" : "FAIL") + " jetengine x=" + position.x.ToString("F2")
				+ " vx=" + velocity.x.ToString("F2")
				+ " state=" + state.Replace("\n", " | ");
		}

		private static string VerifyWing(PartScene scene)
		{
			PartGameplayManager manager = PartGameplayManager.Instance;
			string state = (manager != null) ? manager.Describe() : "no manager";
			Entity wing = FindPart(PartType.Wings, 0);
			float3 position = PartGameplayUtil.Position(wing);
			float3 velocity = PartGameplayUtil.LinearVelocity(wing);
			// Compare with the same launch and damping under gravity alone. BPSS
			// wings can glide downwards, so positive final vertical speed is not required.
			float elapsed = Time.time - s_wingLaunchTime;
			float gravity = UserSettings.Instance.SimulationSettings.GravityY;
			float decay = Mathf.Exp(-elapsed);
			float ballisticY = s_wingLaunchY + gravity * elapsed + (-2 - gravity) * (1 - decay);
			float ballisticSpeed = gravity + (-2 - gravity) * decay;
			bool lift = position.x > 2 && position.y > ballisticY + .25f && velocity.y > ballisticSpeed + .25f;
			return (lift ? "PASS" : "FAIL") + " wing x=" + position.x.ToString("F2")
				+ " y=" + position.y.ToString("F2")
				+ " vx=" + velocity.x.ToString("F2")
				+ " vy=" + velocity.y.ToString("F2")
				+ " state=" + state.Replace("\n", " | ");
		}

		private static string VerifySpring(PartScene scene)
		{
			PartGameplayManager manager = PartGameplayManager.Instance;
			string state = (manager != null) ? manager.Describe() : "no manager";
			bool fired = state.Contains("punches=1") || state.Contains("punches=2");
			return (fired ? "PASS" : "FAIL") + " spring fired=" + fired + " state=" + state.Replace("\n", " | ");
		}

		private static string VerifyKicker(PartScene scene)
		{
			Entity kicker = FindPart(PartType.Kicker, 0);
			float3 position = PartGameplayUtil.Position(kicker);
			float3 velocity = PartGameplayUtil.LinearVelocity(kicker);
			// A separator releases its two original fixed welds without adding launch impulse.
			bool fired = PartGameplayManager.Instance.Find(kicker) is KickerBehavior separator && separator.Fired
				&& PartGameplayUtil.ConnectedComponent(kicker) != PartGameplayUtil.ConnectedComponent(FindPart(PartType.MetalFrame, 0))
				&& PartGameplayUtil.Simulator.Joints.Length == 0;
			return (fired ? "PASS" : "FAIL") + " kicker y=" + position.y.ToString("F2")
				+ " vy=" + velocity.y.ToString("F2") + " state=" + Snapshot().Replace("\n", " | ");
		}


		private static string VerifyTNT(PartScene scene)
		{
			Entity target = FindPart(PartType.MetalFrame, 0);
			Entity charge = FindPart(PartType.TNT, 0);
			float3 targetPosition = PartGameplayUtil.Position(target);
			return (targetPosition.x > 4f && charge == Entity.Null ? "PASS" : "FAIL") + " tnt removed=" + (charge == Entity.Null)
				+ " target@(" + targetPosition.x.ToString("F2") + "," + targetPosition.y.ToString("F2") + ")"
				+ " state=" + Snapshot().Replace("\n", " | ");
		}

		// ------------------------------------------------------------------
		// Pumpkin (BPSS Pumpkin.cs + FixedPumpkinManager.cs)
		// ------------------------------------------------------------------

		private static float s_pumpkinYBefore;

		private static float s_pumpkinMassBefore;

		private static byte s_pumpkinAppliedBefore;

		private static void BuildPumpkinScene(PartScene scene)
		{
			// 南瓜和一个铁框焊在一起：冻结是按「焊接组件」生效的（原版
			// FixedPumpkinManager 遍历 StrictConnectedComponent），所以邻居必须一起被冻、
			// 一起被还原。整组放在空中、脚下没有地面，冻结与否立刻能看出来。
			Place(scene, PartType.Pumpkin, 0, 0, 0, 0);
			Place(scene, PartType.MetalFrame, 0, 1, 0, 0);
		}

		private static void TriggerPumpkin()
		{
			EntityManager entityManager = PartGameplayUtil.Em;
			Entity pumpkin = FindPart(PartType.Pumpkin, 0);
			if (pumpkin == Entity.Null)
			{
				Debug.LogWarning("[PartGameplayQA] part not found Pumpkin#0");
				return;
			}
			// 先取证：触发前这一刻它应该还是冻结态（逆质量 0、Applied 1）。
			s_pumpkinYBefore = PartGameplayUtil.Position(pumpkin).y;
			s_pumpkinMassBefore = (entityManager.HasComponent<PhysicsMass>(pumpkin) ? entityManager.GetComponentData<PhysicsMass>(pumpkin).InverseMass : -1f);
			s_pumpkinAppliedBefore = (entityManager.HasComponent<PumpkinPart>(pumpkin) ? entityManager.GetComponentData<PumpkinPart>(pumpkin).Applied : (byte)0);
			PartSimulation.ProcessTouch(pumpkin);
		}

		private static string VerifyPumpkin(PartScene scene)
		{
			EntityManager entityManager = PartGameplayUtil.Em;
			Entity pumpkin = FindPart(PartType.Pumpkin, 0);
			Entity frame = FindPart(PartType.MetalFrame, 0);
			if (pumpkin == Entity.Null)
			{
				return "FAIL pumpkin missing";
			}
			PumpkinPart state = (entityManager.HasComponent<PumpkinPart>(pumpkin) ? entityManager.GetComponentData<PumpkinPart>(pumpkin) : default(PumpkinPart));
			float pumpkinMass = (entityManager.HasComponent<PhysicsMass>(pumpkin) ? entityManager.GetComponentData<PhysicsMass>(pumpkin).InverseMass : -1f);
			float frameMass = (frame != Entity.Null && entityManager.HasComponent<PhysicsMass>(frame)) ? entityManager.GetComponentData<PhysicsMass>(frame).InverseMass : -1f;
			bool pumpkinFrozen = entityManager.HasComponent<PumpkinFrozen>(pumpkin);
			bool frameFrozen = frame != Entity.Null && entityManager.HasComponent<PumpkinFrozen>(frame);
			float pumpkinY = PartGameplayUtil.Position(pumpkin).y;
			float frameY = (frame != Entity.Null) ? PartGameplayUtil.Position(frame).y : 0f;
			// 触发前是冻结态（两个判据），触发后整组解冻（逆质量回来、标记消失）并且真的掉下去。
			bool wasFrozen = s_pumpkinMassBefore <= 1E-04f && s_pumpkinAppliedBefore != 0;
			bool restored = state.Enabled == 0 && state.Applied == 0 && !pumpkinFrozen && !frameFrozen;
			bool dynamicAgain = pumpkinMass > 1E-04f && frameMass > 1E-04f;
			bool fell = pumpkinY < s_pumpkinYBefore - 1f;
			bool passed = wasFrozen && restored && dynamicAgain && fell;
			return (passed ? "PASS" : "FAIL") + " enabled=" + (state.Enabled != 0)
				+ " applied=" + (state.Applied != 0)
				+ " massBefore=" + s_pumpkinMassBefore.ToString("F4")
				+ " appliedBefore=" + s_pumpkinAppliedBefore
				+ " massAfter=" + pumpkinMass.ToString("F4")
				+ " frameMassAfter=" + frameMass.ToString("F4")
				+ " frozenComp=" + pumpkinFrozen + "/" + frameFrozen
				+ " yBefore=" + s_pumpkinYBefore.ToString("F2")
				+ " yAfter=" + pumpkinY.ToString("F2")
				+ " frameY=" + frameY.ToString("F2")
				+ " state=" + Snapshot().Replace("\n", " | ");
		}

		// ------------------------------------------------------------------
		// Fuel (BPSS FuelBox.cs + FuelTube.cs + JetEngine 的燃料部分)
		// 燃料件不是独立 PartType，而是喷气引擎组的皮肤：
		//   索引 4 = 燃料箱(FuelBox.cs)、2/3 = 燃料管(FuelTube.cs Common/Auto)、
		//   5 = 燃料阀门(FuelTube.cs Valve)、0/1 = 发动机本体。
		// ------------------------------------------------------------------

		private static void BuildFuelScene(PartScene scene)
		{
			// 整排悬空、脚下无地面：只看发动机能不能把自己推走，避免和摩擦/支撑混在一起。
			// 不装框：WoodenFrame 那种容器件在这个模拟里撑不出可移动刚体（实测放进框后
			// 整排 y 恒为 0、速度恒为 0）。改用燃料管本身当连接源——BPSS 的 FuelTube 就是
			// 「连接邻居」的媒体，所以它的 PartExtensionMap 条目把 isConnectionSource 打开，
			// 首尾相接的燃料箱 / 发动机就能被它焊进同一个连通组件。
			Place(scene, PartType.JetEngine, 4, 0, 0, 0);
			Place(scene, PartType.JetEngine, 2, 1, 0, 0);
			Place(scene, PartType.JetEngine, 0, 2, 0, 0);
		}

		private static void BuildFuelValveScene(PartScene scene)
		{
			Place(scene, PartType.JetEngine, 4, 0, 0, 0);
			Place(scene, PartType.JetEngine, 5, 1, 0, 0);
			Place(scene, PartType.JetEngine, 0, 2, 0, 0);
		}

		private static void TriggerFuelDry()
		{
			// 先把油箱掏干净再点火：4 单位容量按 1/s 的抽油速率要 4 秒才耗尽，
			// QA 等不了那么久，所以直接把余量压到「几乎为空」。
			EntityManager entityManager = PartGameplayUtil.Em;
			Entity box = FindPart(PartType.JetEngine, 4);
			if (box != Entity.Null && entityManager.HasComponent<FuelContainer>(box))
			{
				FuelContainer container = entityManager.GetComponentData<FuelContainer>(box);
				container.Amount = 0.02f;
				entityManager.SetComponentData(box, container);
			}
			Entity engine = FindPart(PartType.JetEngine, 0);
			if (engine != Entity.Null)
			{
				PartSimulation.ProcessTouch(engine);
			}
		}

		private static void TriggerFuelValve()
		{
			Entity valve = FindPart(PartType.JetEngine, 5);
			if (valve != Entity.Null)
			{
				PartSimulation.ProcessTouch(valve);
			}
			Entity engine = FindPart(PartType.JetEngine, 0);
			if (engine != Entity.Null)
			{
				PartSimulation.ProcessTouch(engine);
			}
		}

		private static string VerifyFuel(PartScene scene)
		{
			EntityManager entityManager = PartGameplayUtil.Em;
			Entity box = FindPart(PartType.JetEngine, 4);
			Entity engine = FindPart(PartType.JetEngine, 0);
			if (box == Entity.Null || engine == Entity.Null)
			{
				return "FAIL fuel parts missing box=" + (box != Entity.Null) + " engine=" + (engine != Entity.Null);
			}
			FuelContainer container = entityManager.HasComponent<FuelContainer>(box) ? entityManager.GetComponentData<FuelContainer>(box) : default(FuelContainer);
			FuelConsumer consumer = entityManager.HasComponent<FuelConsumer>(engine) ? entityManager.GetComponentData<FuelConsumer>(engine) : default(FuelConsumer);
			float3 velocity = PartGameplayUtil.LinearVelocity(engine);
			float speed = math.length(velocity);
			bool engineOn = BasePart.IsEnabled(engine, entityManager);
			bool drained = container.Amount < container.Capacity - 1E-03f;
			bool fed = consumer.Supplied > 0f;
			bool moved = speed > 0.3f;
			bool passed = drained && fed && moved && engineOn;
			return (passed ? "PASS" : "FAIL") + " amount=" + container.Amount.ToString("F3")
				+ " capacity=" + container.Capacity.ToString("F1")
				+ " supplied=" + consumer.Supplied.ToString("F4")
				+ " required=" + consumer.Required.ToString("F4")
				+ " engineOn=" + engineOn
				+ " speed=" + speed.ToString("F2")
				+ " vx=" + velocity.x.ToString("F2")
				+ " state=" + Snapshot().Replace("\n", " | ");
		}

		private static string VerifyFuelDry(PartScene scene)
		{
			EntityManager entityManager = PartGameplayUtil.Em;
			Entity box = FindPart(PartType.JetEngine, 4);
			Entity engine = FindPart(PartType.JetEngine, 0);
			if (box == Entity.Null || engine == Entity.Null)
			{
				return "FAIL fueldry parts missing";
			}
			FuelContainer container = entityManager.HasComponent<FuelContainer>(box) ? entityManager.GetComponentData<FuelContainer>(box) : default(FuelContainer);
			FuelConsumer consumer = entityManager.HasComponent<FuelConsumer>(engine) ? entityManager.GetComponentData<FuelConsumer>(engine) : default(FuelConsumer);
			float speed = math.length(PartGameplayUtil.LinearVelocity(engine));
			bool engineOff = !BasePart.IsEnabled(engine, entityManager);
			bool stopped = consumer.Requesting == 0;
			bool empty = container.Amount <= 0.05f;
			bool passed = engineOff && stopped && empty;
			return (passed ? "PASS" : "FAIL") + " engineOff=" + engineOff
				+ " requesting=" + (consumer.Requesting != 0)
				+ " amount=" + container.Amount.ToString("F4")
				+ " speed=" + speed.ToString("F2")
				+ " state=" + Snapshot().Replace("\n", " | ");
		}

		private static string VerifyFuelValve(PartScene scene)
		{
			EntityManager entityManager = PartGameplayUtil.Em;
			Entity valve = FindPart(PartType.JetEngine, 5);
			Entity engine = FindPart(PartType.JetEngine, 0);
			if (valve == Entity.Null || engine == Entity.Null)
			{
				return "FAIL fuelvalve parts missing";
			}
			bool valveOpen = !entityManager.HasComponent<FuelTubeLink>(valve) || entityManager.GetComponentData<FuelTubeLink>(valve).Open != 0;
			bool engineOn = BasePart.IsEnabled(engine, entityManager);
			// 只看推进方向的水平速度：整排在自由下落，总速度含重力分量，不能拿来判推力。
			float thrustSpeed = math.abs(PartGameplayUtil.LinearVelocity(engine).x);
			// 阀门关死 → 断油 → 发动机必须拒绝启动（BPSS IsTriggerable 检查组里有油箱）。
			bool passed = !valveOpen && !engineOn && thrustSpeed < 0.3f;
			return (passed ? "PASS" : "FAIL") + " valveOpen=" + valveOpen
				+ " engineOn=" + engineOn
				+ " thrustSpeed=" + thrustSpeed.ToString("F2")
				+ " state=" + Snapshot().Replace("\n", " | ");
		}

		// ------------------------------------------------------------------
		// 猪族 / 悬浮件
		// ------------------------------------------------------------------

		/// <summary>
		/// 猪族每个型号都有自己的 prefab 索引（Pig 0–19 / KingPig 0–6 / GoldenPig 0–3）。
		/// 这里同时摆 5 个不同索引，确认 <c>CreateBehavior</c> 的映射对所有索引都成立，
		/// 而不只是 QA 惯用的索引 0。只有索引 0 参与速度相关的断言，其余只验组件是否到位。
		/// </summary>
		private static void BuildPigScene(PartScene scene)
		{
			Place(scene, PartType.Pig, 0, 0, 0, 0);
			Place(scene, PartType.Pig, 7, 3, 0, 0);
			Place(scene, PartType.Pig, 19, 6, 0, 0);
			Place(scene, PartType.KingPig, 6, 0, 3, 0);
			Place(scene, PartType.GoldenPig, 3, 3, 3, 0);
		}

		private static void BuildKingPigScene(PartScene scene)
		{
			Place(scene, PartType.KingPig, 0, 0, 0, 0);
		}

		private static void BuildGoldenPigScene(PartScene scene)
		{
			Place(scene, PartType.GoldenPig, 0, 0, 0, 0);
		}

		private static void BuildLevitationScene(PartScene scene)
		{
			// 悬空单件：抬升测试不能被地面托住。
			Place(scene, PartType.Pig, 0, 0, 0, 0);
		}

		/// <summary>
		/// 给猪注入一个已知速度并关掉重力。阻尼跟速度有关，不关重力的话自由下落
		/// 不到 0.15 s 就会把速度推过 1 m/s，测不到慢速区。
		/// </summary>
		private static void SetPigVelocity(PartType type, int index, float speed)
		{
			Entity part = FindPart(type, index);
			if (part == Entity.Null)
			{
				return;
			}
			EntityManager entityManager = PartGameplayUtil.Em;
			PhysicsGravityFactor gravity = new PhysicsGravityFactor
			{
				Value = 0f
			};
			if (entityManager.HasComponent<PhysicsGravityFactor>(part))
			{
				entityManager.SetComponentData(part, gravity);
			}
			else
			{
				entityManager.AddComponentData(part, gravity);
			}
			entityManager.SetComponentData(part, new PhysicsVelocity
			{
				Linear = new float3(speed, 0f, 0f),
				Angular = float3.zero
			});
		}

		private static void TriggerPigSlow()
		{
			SetPigVelocity(PartType.Pig, 0, 0.4f);
		}

		private static void TriggerPigFast()
		{
			SetPigVelocity(PartType.Pig, 0, 5f);
		}

		private static PartType s_heavyPigType = PartType.KingPig;

		private static void TriggerKingPig()
		{
			s_heavyPigType = PartType.KingPig;
			SetPigVelocity(PartType.KingPig, 0, 2f);
		}

		private static void TriggerGoldenPig()
		{
			s_heavyPigType = PartType.GoldenPig;
			SetPigVelocity(PartType.GoldenPig, 0, 2f);
		}

		private static float s_levitationYBefore;

		private static void TriggerLevitation()
		{
			Entity part = FindPart(PartType.Pig, 0);
			if (part == Entity.Null)
			{
				return;
			}
			s_levitationYBefore = PartGameplayUtil.Position(part).y;
			EntityManager entityManager = PartGameplayUtil.Em;
			// 原版 PartLevitation 的两个 force 字段来自 AssetBundle 里的 prefab，
			// bpsim 没有这份数据，所以由 QA 现场挂一个明确的推力（质量 2 → 15 m/s²，扣掉重力后净上升 5.2 m/s²）。
			PartLevitation levitation = new PartLevitation
			{
				Force = 30f,
				EnclosedForce = 30f
			};
			if (entityManager.HasComponent<PartLevitation>(part))
			{
				entityManager.SetComponentData(part, levitation);
			}
			else
			{
				entityManager.AddComponentData(part, levitation);
			}
		}

		private static string DampingOf(PartType type, int index, out PhysicsDamping damping, out float speed)
		{
			damping = default(PhysicsDamping);
			speed = 0f;
			Entity part = FindPart(type, index);
			if (part == Entity.Null)
			{
				return "FAIL part missing";
			}
			EntityManager entityManager = PartGameplayUtil.Em;
			if (!entityManager.HasComponent<PhysicsDamping>(part))
			{
				return "FAIL no PhysicsDamping";
			}
			damping = entityManager.GetComponentData<PhysicsDamping>(part);
			speed = math.length(PartGameplayUtil.LinearVelocity(part));
			return null;
		}

		/// <summary>BPSS Pig.FixedUpdate 的慢速区：drag 与 angularDrag 同为 0.2 + 2.5*(1-speed)。</summary>
		private static string VerifyPigSlow(PartScene scene)
		{
			// 先确认所有型号索引都拿到了阻尼组件（CreateBehavior 的映射对全部索引生效）。
			int pigCovered = 0;
			EntityManager manager = PartGameplayUtil.Em;
			PartTypeValue[] samples =
			{
				new PartTypeValue { Type = PartType.Pig, Index = 7 },
				new PartTypeValue { Type = PartType.Pig, Index = 19 },
				new PartTypeValue { Type = PartType.KingPig, Index = 6 },
				new PartTypeValue { Type = PartType.GoldenPig, Index = 3 }
			};
			for (int i = 0; i < samples.Length; i++)
			{
				Entity sample = FindPart(samples[i].Type, samples[i].Index);
				if (sample != Entity.Null && manager.HasComponent<PhysicsDamping>(sample) && manager.HasComponent<PigMotion>(sample))
				{
					pigCovered++;
				}
			}
			PhysicsDamping damping;
			float speed;
			string error = DampingOf(PartType.Pig, 0, out damping, out speed);
			if (error != null)
			{
				return error;
			}
			bool zone = damping.Linear > 1.2f && damping.Linear <= 2.71f;
			bool pair = math.abs(damping.Linear - damping.Angular) < 0.01f;
			bool passed = zone && pair && pigCovered == samples.Length;
			return (passed ? "PASS" : "FAIL") + " slow linear=" + damping.Linear.ToString("F3")
				+ " angular=" + damping.Angular.ToString("F3")
				+ " speed=" + speed.ToString("F2")
				+ " variants=" + pigCovered + "/" + samples.Length
				+ " state=" + Snapshot().Replace("\n", " | ");
		}

		/// <summary>同一只猪进入高速区后必须回落 0.2 / 0.05。</summary>
		private static string VerifyPigFast(PartScene scene)
		{
			PhysicsDamping damping;
			float speed;
			string error = DampingOf(PartType.Pig, 0, out damping, out speed);
			if (error != null)
			{
				return error;
			}
			bool passed = math.abs(damping.Linear - 0.2f) < 0.01f
				&& math.abs(damping.Angular - 0.05f) < 0.01f
				&& speed > 1f;
			return (passed ? "PASS" : "FAIL") + " fast linear=" + damping.Linear.ToString("F3")
				+ " angular=" + damping.Angular.ToString("F3")
				+ " speed=" + speed.ToString("F2")
				+ " state=" + Snapshot().Replace("\n", " | ");
		}

		/// <summary>猪王 / 金猪的常数阻尼 0.5 / 1。</summary>
		private static string VerifyHeavyPig(PartScene scene)
		{
			Entity part = FindPart(s_heavyPigType, 0);
			if (part == Entity.Null)
			{
				return "FAIL heavy pig missing type=" + s_heavyPigType;
			}
			EntityManager entityManager = PartGameplayUtil.Em;
			if (!entityManager.HasComponent<PhysicsDamping>(part))
			{
				return "FAIL no PhysicsDamping";
			}
			PhysicsDamping damping = entityManager.GetComponentData<PhysicsDamping>(part);
			float speed = math.length(PartGameplayUtil.LinearVelocity(part));
			bool passed = math.abs(damping.Linear - 0.5f) < 0.01f && math.abs(damping.Angular - 1f) < 0.01f;
			return (passed ? "PASS" : "FAIL") + " heavy(" + s_heavyPigType + ") linear=" + damping.Linear.ToString("F3")
				+ " angular=" + damping.Angular.ToString("F3")
				+ " speed=" + speed.ToString("F2")
				+ " state=" + Snapshot().Replace("\n", " | ");
		}

		private static string VerifyLevitation(PartScene scene)
		{
			Entity part = FindPart(PartType.Pig, 0);
			if (part == Entity.Null)
			{
				return "FAIL levitation part missing";
			}
			EntityManager entityManager = PartGameplayUtil.Em;
			float y = PartGameplayUtil.Position(part).y;
			bool hasComponent = entityManager.HasComponent<PartLevitation>(part);
			bool passed = hasComponent && y > s_levitationYBefore + 1f;
			return (passed ? "PASS" : "FAIL") + " yBefore=" + s_levitationYBefore.ToString("F2")
				+ " yAfter=" + y.ToString("F2")
				+ " applied=" + hasComponent
				+ " state=" + Snapshot().Replace("\n", " | ");
		}

		// ------------------------------------------------------------------
		// Helpers
		// ------------------------------------------------------------------

		private static Entity FindPart(PartType type, int index)
		{
			PartManager manager = PartManager.Instance;
			if (manager == null || manager.PartSimulator == null)
			{
				return Entity.Null;
			}
			PartSimulatorUnmanaged simulator = manager.PartSimulator.Unmanaged;
			EntityManager entityManager = PartGameplayUtil.Em;
			for (int i = 0; i < simulator.Parts.Length; i++)
			{
				Entity entity = simulator.Parts[i];
				if (!entityManager.Exists(entity) || !entityManager.HasComponent<PartTypeValue>(entity))
				{
					continue;
				}
				PartTypeValue value = entityManager.GetComponentData<PartTypeValue>(entity);
				if (value.Type == type && value.Index == index)
				{
					return entity;
				}
			}
			return Entity.Null;
		}

		private static string DescribeParts(PartScene scene)
		{
			PartSimulatorUnmanaged simulator = PartManager.Instance.PartSimulator.Unmanaged;
			EntityManager entityManager = PartGameplayUtil.Em;
			StringBuilder stringBuilder = new StringBuilder();
			for (int i = 0; i < simulator.Parts.Length; i++)
			{
				Entity entity = simulator.Parts[i];
				if (!entityManager.Exists(entity) || !entityManager.HasComponent<PartTypeValue>(entity))
				{
					continue;
				}
				PartTypeValue type = entityManager.GetComponentData<PartTypeValue>(entity);
				PartTransform transform = entityManager.GetComponentData<PartTransform>(entity);
				if (i > 0)
				{
					stringBuilder.Append("; ");
				}
				stringBuilder.Append(type.Type).Append('#').Append(type.Index).Append('@').Append(transform.X).Append(',').Append(transform.Y);
			}
			return stringBuilder.ToString();
		}

		private static void Line(string text)
		{
			s_report.AppendLine(text);
			Debug.Log("[PartGameplayQA] " + text);
		}
	}
}
#endif
