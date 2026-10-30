#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Bpsim.Background;
using Bpsim.Levels;
using Bpsim.Parts;
using Bpsim.Parts.Gameplay;
using Bpsim.Parts.Simulation;
using Bpsim.Parts.Skin;
using Bpsim.Physics;
using Bpsim.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;
using UnityEngine;

namespace Bpsim.QA
{
	/// <summary>
	/// 诊断「载具旋转越快越卡」的探针：加载一份 schematics、开始模拟，按给定角速度把整台车
	/// 驱动成刚体自旋，同时采样帧时间，并可选地逐个关掉系统做二分，看卡顿到底落在谁身上。
	/// </summary>
	/// <remarks>
	///   invoke_static type=Bpsim.QA.SpinPerfProbe method=Run args=["file=不败荣光;dur=2.5;bisect=1"]
	///   invoke_static type=Bpsim.QA.SpinPerfProbe method=Status
	/// spec（分号分隔的 key=value）：
	///   file=       schematics 路径（相对工程根或绝对路径），默认「不败荣光」
	///   dur=        每个阶段的采样秒数，默认 2.5
	///   solver=     模拟开始时写入的 SolverIterationCount，默认不改（-1）
	///   gravity=    模拟开始时写入的 GravityY，默认 0（悬空自旋，避免落地噪音）
	///   spin=       二分阶段使用的角速度，默认 32
	///   bisect=     1/0 是否跑系统二分，默认 1
	///   mode=       rigid/drive/both，默认 both（rigid = 每帧写刚体速度场，drive = 只给质心附近加角速度）
	/// 结果同时写到 Logs/spinperf.log。
	/// </remarks>
	public class SpinPerfProbe : MonoBehaviour
	{
		private const string LogFile = "Logs/spinperf.log";

		private static SpinPerfProbe s_instance;

		private static readonly StringBuilder s_report = new StringBuilder();

		private static string s_status = "idle";

		private static float s_gravityBackup = -9.81f;

		private static bool s_gravityChanged;

		private static bool s_settingsChanged;

		private static bool s_infiniteBackup;

		private static float s_factorBackup;

		private static bool s_createJointsBackup;

		private static int s_solverBackup;

		private static bool s_selfCollideBackup;

		// 是否让焊接连通块内部也互相碰撞（= 改动前的原生行为；selfcollide=-1 表示不改用户设置）
		private static bool s_selfCollideChanged;

		// 巨构优化（mega=1）：整车并成一个刚体。mega=-1 表示不改用户设置。
		private static bool s_megaBackup;

		private static bool s_megaChanged;

		private static float s_megaBreakBackup;

		// 巨构撞击测试：起始部件数，用于统计掉件数量。
		private static int s_megaPartsStart;

		// 巨构撞击测试用的地面实体（测试结束销毁）。
		private static Entity s_testGround;

		private static BlobAssetReference<Unity.Physics.Collider> s_testGroundBlob;

		// rigid 自旋时是否写线速度/角速度（lin=0/ang=0 用来分开看两者的代价）
		private static bool s_linear = true;

		/// <summary>是否每帧把测试速度场写进部件（writev=0 关闭；巨构模式的真实动力学测试需要关）。</summary>
		private static bool s_writeVelocity = true;

		private static float s_translation;

		private static bool s_angular = true;

		// fix=1（默认）：按连通块分配碰撞过滤（PartCollisionFilter）；fix=0 还原成旧行为做对照。
		private static bool fixed_ = true;

		// mode
		private const int ModeNone = 0;
		private const int ModeRigid = 1;
		private const int ModeDrive = 2;

		public static string Status()
		{
			return s_status + "\n" + s_report;
		}

		public static string Run(string spec)
		{
			if (s_status == "running")
			{
				return "already running\n" + s_report;
			}
			SpinPerfProbe probe = Ensure();
			s_report.Length = 0;
			s_status = "running";
			GameObject go = probe.gameObject;
			go.name = "[SpinPerfProbe]";
			probe.StartCoroutine(probe.RunRoutine(spec ?? string.Empty));
			return "started: " + spec;
		}

		public static string Stop()
		{
			if (s_instance != null)
			{
				s_instance.StopAllCoroutines();
			}
			s_status = "stopped";
			RestoreSystems();
			return Status();
		}

		private static SpinPerfProbe Ensure()
		{
			if (s_instance != null)
			{
				return s_instance;
			}
			GameObject gameObject = new GameObject("[SpinPerfProbe]");
			gameObject.hideFlags = HideFlags.DontSave;
			UnityEngine.Object.DontDestroyOnLoad(gameObject);
			s_instance = gameObject.AddComponent<SpinPerfProbe>();
			return s_instance;
		}

		private static void Line(string text)
		{
			s_report.AppendLine(text);
			Debug.Log("[SpinPerf] " + text);
		}

		private static string F(float value)
		{
			return value.ToString("F2", CultureInfo.InvariantCulture);
		}

		private IEnumerator RunRoutine(string spec)
		{
			yield return StartCoroutine(RunCore(spec));
			RestoreSystems();
			if (s_gravityChanged && UserSettings.Instance != null)
			{
				UserSettings.Instance.SimulationSettings.GravityY = s_gravityBackup;
				s_gravityChanged = false;
				Line("gravity restored to " + F(s_gravityBackup));
			}
			if (s_settingsChanged && UserSettings.Instance != null)
			{
				SimulationSettings settings = UserSettings.Instance.SimulationSettings;
				settings.InfiniteConnectionStrength = s_infiniteBackup;
				settings.ConnectionStrengthFactor = s_factorBackup;
				settings.CreateJoints = s_createJointsBackup;
				if (s_solverBackup >= 0)
				{
					settings.SolverIterationCount = s_solverBackup;
				}
				s_settingsChanged = false;
				Line("settings restored: solver=" + settings.SolverIterationCount + " joints=" + settings.CreateJoints
					+ " strengthFactor=" + F(settings.ConnectionStrengthFactor)
					+ " infinite=" + settings.InfiniteConnectionStrength);
			}
			if (s_selfCollideChanged && UserSettings.Instance != null)
			{
				UserSettings.Instance.SimulationSettings.PartSelfCollision = s_selfCollideBackup;
				s_selfCollideChanged = false;
				Line("selfCollision restored to " + s_selfCollideBackup);
			}
			if (s_megaChanged && UserSettings.Instance != null)
			{
				UserSettings.Instance.SimulationSettings.MegaStructure = s_megaBackup;
				UserSettings.Instance.SimulationSettings.MegaStructureBreakSpeed = s_megaBreakBackup;
				s_megaChanged = false;
				Line("mega restored to " + s_megaBackup + " breakSpeed=" + F(s_megaBreakBackup));
			}
			if (s_testGround != Entity.Null)
			{
				EntityManager cleanupEm = PartGameplayUtil.Em;
				if (cleanupEm.World != null && cleanupEm.Exists(s_testGround))
				{
					cleanupEm.DestroyEntity(s_testGround);
				}
				s_testGround = Entity.Null;
				if (s_testGroundBlob.IsCreated)
				{
					s_testGroundBlob.Dispose();
				}
				Line("megadrop ground destroyed");
			}
			s_status = "done";
			Line("=== done ===");
			WriteLog();
		}

		private IEnumerator RunCore(string spec)
		{
			float duration = SpecFloat(spec, "dur", 2.5f);
			int solver = SpecInt(spec, "solver", -1);
			float gravity = SpecFloat(spec, "gravity", float.NaN);
			float spin = SpecFloat(spec, "spin", 32f);
			bool bisect = SpecInt(spec, "bisect", 1) != 0;
			// 没有关节（mega 模式）时是否仍然跑二分。默认 0 = 保持旧行为（打两档就收工）。
			bool forceBisect = SpecInt(spec, "forcebisect", 0) != 0;
			// 组级二分（groupbisect=1）：逐个关掉整个 system group。
			bool groupBisect = SpecInt(spec, "groupbisect", 0) != 0;
			// 系统级二分（sysbisect=1）：PartSimulationSystemGroup 里逐个系统关。
			bool sysBisect = SpecInt(spec, "sysbisect", 0) != 0;			string modeWord = SpecWord(spec, "mode", "both");
			string file = SpecWord(spec, "file", "不败荣光");
			bool hold = SpecInt(spec, "hold", 0) != 0;
			s_linear = SpecInt(spec, "lin", 1) != 0;
			s_writeVelocity = SpecInt(spec, "writev", 1) != 0;
			s_translation = SpecFloat(spec, "trans", 0f);
			bool noCollide = SpecInt(spec, "nocollide", 0) != 0;
			s_angular = SpecInt(spec, "ang", 1) != 0;
			bool skipSweep = SpecInt(spec, "sweep", 1) == 0;
			int infinite = SpecInt(spec, "inf", -1);
			float strengthFactor = SpecFloat(spec, "factor", float.NaN);
			int createJoints = SpecInt(spec, "createjoints", -1);
			int selfCollide = SpecInt(spec, "selfcollide", -1);
			// 巨构优化：mega=1 整车刚体化（省启动与运行时），mega=0 强制关，-1 不改。
			int mega = SpecInt(spec, "mega", -1);
			float megaBreak = SpecFloat(spec, "breakspeed", float.NaN);
			// megadrop=<速度>：巨构模式下给整车一个向下初速，撞地面验证受击掉件。
			float megaDrop = SpecFloat(spec, "megadrop", 0f);
			// megadetach=<数量>：直接摘掉前 N 个部件，验证掉件链路的机械部分（不依赖碰撞）。
			int megaDetach = SpecInt(spec, "megadetach", 0);
			fixed_ = SpecInt(spec, "fix", 1) != 0;
			bool reapply = SpecInt(spec, "reapply", 0) != 0;
			// 相邻焊接件到底碰不碰：运行时把关节的 PhysicsConstrainedBodyPair.EnableCollision 改掉再测。
			// -1 = 不动（保持生产代码建关节时的 enableCollision:false），0/1 = 全部关/开。
			int jointCollide = SpecInt(spec, "jointcollide", -1);

			Line("=== spin perf probe ===");
			Line("spec: " + spec);

			PartManager manager = PartManager.Instance;
			if (manager == null)
			{
				Line("error: no PartManager (还没启动到 MainScene 流程？)");
				yield break;
			}
			Line("worlds: manager.IsReady=" + manager.IsReady + " simulating=" + manager.IsSimulating);

			if (manager.IsSimulating)
			{
				manager.EndSimulation();
				yield return null;
			}

			string path = file;
			if (!Path.IsPathRooted(path))
			{
				path = Path.Combine(Directory.GetCurrentDirectory(), path);
			}
			Line("load: " + path + " exists=" + File.Exists(path));
			if (!File.Exists(path))
			{
				Line("error: schematics file not found");
				yield break;
			}

			if (!float.IsNaN(gravity) || solver >= 0 || infinite >= 0 || !float.IsNaN(strengthFactor) || createJoints >= 0 || selfCollide >= 0 || mega >= 0 || !float.IsNaN(megaBreak))
			{
				SimulationSettings settings = UserSettings.Instance.SimulationSettings;
				if (!s_settingsChanged)
				{
					s_infiniteBackup = settings.InfiniteConnectionStrength;
					s_factorBackup = settings.ConnectionStrengthFactor;
					s_createJointsBackup = settings.CreateJoints;
					s_solverBackup = settings.SolverIterationCount;
					s_settingsChanged = true;
				}
				if (!float.IsNaN(gravity))
				{
					s_gravityBackup = settings.GravityY;
					s_gravityChanged = true;
					settings.GravityY = gravity;
				}
				if (solver >= 0)
				{
					settings.SolverIterationCount = solver;
				}
				if (infinite >= 0)
				{
					settings.InfiniteConnectionStrength = infinite != 0;
				}
				if (!float.IsNaN(strengthFactor))
				{
					settings.ConnectionStrengthFactor = strengthFactor;
				}
				if (createJoints >= 0)
				{
					settings.CreateJoints = createJoints != 0;
				}
				if (selfCollide >= 0)
				{
					s_selfCollideBackup = settings.PartSelfCollision;
					s_selfCollideChanged = true;
					settings.PartSelfCollision = selfCollide != 0;
				}
				if (mega >= 0 || !float.IsNaN(megaBreak))
				{
					if (!s_megaChanged)
					{
						s_megaBackup = settings.MegaStructure;
						s_megaBreakBackup = settings.MegaStructureBreakSpeed;
						s_megaChanged = true;
					}
					if (mega >= 0)
					{
						settings.MegaStructure = mega != 0;
					}
					if (!float.IsNaN(megaBreak))
					{
						settings.MegaStructureBreakSpeed = megaBreak;
					}
				}
				Line("settings: gravityY=" + F(settings.GravityY) + " solver=" + settings.SolverIterationCount
					+ " fixedStep=" + F(settings.FixedTimeStep) + " joints=" + settings.CreateJoints
					+ " strengthFactor=" + F(settings.ConnectionStrengthFactor)
					+ " infinite=" + settings.InfiniteConnectionStrength
					+ " selfCollision=" + settings.PartSelfCollision
					+ " mega=" + settings.MegaStructure + " breakSpeed=" + F(settings.MegaStructureBreakSpeed));
			}

			string error = null;
			bool loaded = false;
			try
			{
				loaded = LevelSession.LoadSceneFile(path, SchematicsFormat.Csv, out error);
			}
			catch (Exception exception)
			{
				error = exception.ToString();
			}
			Line("load result=" + loaded + " error=" + (error ?? "none"));
			if (!loaded)
			{
				Line("error: load failed");
				yield break;
			}
			yield return null;
			PartScene scene = manager.ActiveScene;
			if (scene == null)
			{
				Line("error: no active scene after load");
				yield break;
			}
			PartSceneUnmanaged build = scene.Unmanaged;
			Line("build scene: parts=" + build.Parts.Length
				+ " center=" + F(build.GetPartsCenter().x) + "," + F(build.GetPartsCenter().y));

			// 新载入的场景实体这一帧只有 LocalTransform/LocalToWorld，WorldTransform 由
			// Unity.Transforms.LocalToWorldSystem 在下一个 TransformSystemGroup 更新时补上，
			// 而 BuildJointsAndComponentsJob 要用 WorldTransform 建关节。等两帧再开模拟，
			// 否则关节会一根都建不出来（job 里的异常被 job 系统吞掉，表现就是 joints=0）。
			yield return null;
			yield return null;
			if (hold)
			{
				bool wasEnabled = Toggle<PartSimulationSystem>(false);
				Line("hold: PartSimulationSystem disabled=" + wasEnabled + " (先确认关节有没有被建出来)");
			}

			System.Diagnostics.Stopwatch startWatch = System.Diagnostics.Stopwatch.StartNew();
			manager.StartSimulation();
			startWatch.Stop();
			Line("start: StartSimulation " + F((float)startWatch.Elapsed.TotalMilliseconds) + "ms");
			yield return null;
			yield return null;

			PartSimulator sim = manager.PartSimulator;
			if (sim == null)
			{
				Line("error: simulator is null after StartSimulation");
				yield break;
			}
			EntityManager jointsEm = PartGameplayUtil.Em;
			int jointEntities = JointEntityCount(jointsEm);
			Line("simulation: parts=" + sim.Unmanaged.Parts.Length + " joints=" + sim.Unmanaged.Joints.Length
				+ " components=" + sim.Unmanaged.ConnectedComponents.Length + " jointEntities=" + jointEntities);
			Line("mega: active=" + MegaStructure.Active + " body=" + MegaStructure.Body.Index + ":" + MegaStructure.Body.Version
				+ " parts=" + MegaStructure.PartCount + " mass=" + F(MegaStructure.TotalMass)
				+ " keyBits=" + MegaStructure.ChildKeyBits + " breakSpeed=" + F(MegaStructure.BreakSpeed));
			// megadrop=<速度>：给整车刚体一个向下初速，用来撞地面验证「受击掉件」。
			s_megaPartsStart = MegaStructure.PartCount;
			if (MegaStructure.Active && megaDrop > 0f)
			{
				EntityManager dropEm = PartGameplayUtil.Em;
				Entity body = MegaStructure.Body;
				if (dropEm.HasComponent<PhysicsVelocity>(body))
				{
					PhysicsVelocity dropVelocity = dropEm.GetComponentData<PhysicsVelocity>(body);
					dropVelocity.Linear = new float3(0f, -megaDrop, 0f);
					dropVelocity.Angular = float3.zero;
					dropEm.SetComponentData(body, dropVelocity);
					// 直接改整车速度后必须重新镜像一次：Drive 的期望基准还停在旧速度上，
					// 不刷新就会把这次改动当成玩家施力、反向抵消（实测 -20 立刻变 -1.5）。
					MegaStructure.SyncMirror(dropEm);
					Line("megadrop: body linear=" + F(-megaDrop) + " parts=" + s_megaPartsStart);
				}
				// CSV 里没有地形，撞地测试自己造一块静态大板，保证真的产生接触。
				// 注意：整车刚体的原点在建造空间原点，复合体子件的偏移是绝对坐标，
				// 所以地面位置要按「部件包围盒最低点」算，不能按刚体 LocalTransform。
				LocalTransform bodyTransform = dropEm.GetComponentData<LocalTransform>(body);
				NativeList<Entity> dropParts = sim.Unmanaged.Parts;
				float minY = float.MaxValue;
				for (int i = 0; i < dropParts.Length; i++)
				{
					if (dropEm.HasComponent<LocalTransform>(dropParts[i]))
					{
						minY = math.min(minY, dropEm.GetComponentData<LocalTransform>(dropParts[i]).Position.y);
					}
				}
				if (minY == float.MaxValue)
				{
					minY = bodyTransform.Position.y;
				}
				float groundY = minY - 3f;
				BlobAssetReference<Unity.Physics.Collider> groundBlob = Unity.Physics.BoxCollider.Create(
					new Unity.Physics.BoxGeometry
					{
						Center = float3.zero,
						Orientation = quaternion.identity,
						Size = new float3(800f, 2f, 800f),
					}, CollisionFilter.Default);
				Entity ground = dropEm.CreateEntity();
				dropEm.SetName(ground, "MegaTestGround");
				float3 groundPosition = new float3(bodyTransform.Position.x, groundY, bodyTransform.Position.z);
				dropEm.AddComponentData(ground, LocalTransform.FromPosition(groundPosition));
				// 静态体同样需要 LocalToWorld：BuildPhysicsWorld 从它读世界矩阵，
				// 只给 LocalTransform 的话这一帧地面不在物理世界里。
				dropEm.AddComponentData(ground, new LocalToWorld
				{
					Value = float4x4.TRS(groundPosition, quaternion.identity, new float3(1f, 1f, 1f)),
				});
				dropEm.AddSharedComponent(ground, new PhysicsWorldIndex(0u));
				dropEm.AddComponentData(ground, new PhysicsCollider { Value = groundBlob });
				s_testGround = ground;
				s_testGroundBlob = groundBlob;
				Line("megadrop ground: y=" + F(groundY) + " size=800x2x800 bodyY=" + F(bodyTransform.Position.y) + " minPartY=" + F(minY));
			}
			// 确定性掉件验证：不依赖碰撞，直接把前 N 个部件从整车复合体里摘出来。
			if (MegaStructure.Active && megaDetach > 0)
			{
				int detachCount = math.min(megaDetach, MegaStructure.PartCount);
				for (int i = 0; i < detachCount; i++)
				{
					MegaStructure.RequestDetach(MegaStructure.Children[i]);
				}
				Line("megadetach: requested=" + detachCount);
			}
			Line("diag: " + Diagnose(manager));
			Line("frame: targetFrameRate=" + Application.targetFrameRate + " timeScale=" + F(Time.timeScale)
				+ " fixedDelta=" + F(Time.fixedDeltaTime));

			// 修复开关（对照实验）：fix=0 把碰撞过滤恢复成默认（旧行为，所有部件互相碰撞）。
			if (!fixed_)
			{
				int reset = PartCollisionFilter.Reset(jointsEm, sim.Unmanaged);
				Line("fix=0: 碰撞过滤已还原为 Default，changed=" + reset);
			}
			else
			{
				int reapplied = PartCollisionFilter.Apply(jointsEm, sim.Unmanaged);
				Line("fix=1: 连通块过滤已就位（重复 apply 改动=" + reapplied + "）");
			}
			Line(PartCollisionFilter.Describe(jointsEm, sim.Unmanaged));

			int pairTotal;
			int pairColliding;
			CountJointPairs(jointsEm, out pairTotal, out pairColliding);
			Line("joint pairs: total=" + pairTotal + " collisionEnabled=" + pairColliding
				+ " (PhysicsConstrainedBodyPair.EnableCollision=0 表示这一对不生成接触)");
			if (jointCollide >= 0)
			{
				int switched = SetJointCollision(jointsEm, jointCollide != 0);
				CountJointPairs(jointsEm, out pairTotal, out pairColliding);
				Line("jointcollide=" + jointCollide + ": changed=" + switched
					+ " → total=" + pairTotal + " collisionEnabled=" + pairColliding);
			}

			if (noCollide)
			{
				Dictionary<Entity, CollisionFilter> cache = new Dictionary<Entity, CollisionFilter>();
				NativeList<Entity> parts = sim.Unmanaged.Parts;
				int disabled = 0;
				for (int i = 0; i < parts.Length; i++)
				{
					disabled += PartGameplayUtil.SetCollisionEnabled(parts[i], false, cache);
				}
				Line("nocollide: disabled=" + disabled + "/" + parts.Length + " (所有部件 CollidesWith=0)");
			}

			if (jointEntities == 0)
			{
				Line("WARNING: jointEntities=0，车没有被焊成整体，测出来的不是用户说的那种卡顿；先跑两档看看");
				// 巨构模式（mega=1）下本来就没有关节（整车是一个刚体），此时仍然要能跑二分，
				// 否则没法回答「剩下的每步开销落在谁身上」。forcebisect=1 跳过这段提前返回。
				if (!forceBisect)
				{
					yield return StartCoroutine(Phase("idle-nj w=0", ModeNone, 0f, duration));
					yield return StartCoroutine(Phase("rigid-nj w=32", ModeRigid, 32f, duration));
					Line("diag end: " + Diagnose(manager));
					yield break;
				}
				Line("forcebisect=1: 没有关节也继续往下跑二分（mega 模式）");
				yield return StartCoroutine(Phase("idle-nj w=0", ModeNone, 0f, duration));
			}

			if (hold)
			{
				Line("hold: re-enabling PartSimulationSystem for 0.6s to see how many joints survive");
				Toggle<PartSimulationSystem>(true);
				yield return StartCoroutine(Phase("after-enable w=0", ModeNone, 0f, 0.6f));
				Line("diag after enable: " + Diagnose(manager) + " jointEntities=" + JointEntityCount(jointsEm));
			}

			// 让世界稳定一帧再开始测。
			yield return StartCoroutine(Phase("warmup", ModeNone, 0f, 0.5f));

			// 1) ω 扫描：刚体自旋（关节不承受应力），看平均帧时间有没有随 ω 增长。
			//    sweep=0 时只测 spin 这一档（这样每次调用都是干净样本：新载入 + 一个 ω）。
			if (!skipSweep)
			{
				float[] sweep = { 0f, 2f, 8f, 16f, 32f, 64f };
				for (int i = 0; i < sweep.Length; i++)
				{
					yield return StartCoroutine(Phase("rigid w=" + F(sweep[i]), ModeRigid, sweep[i], duration));
				}
			}
			else
			{
				yield return StartCoroutine(Phase("single w=" + F(spin), ModeRigid, spin, duration));
			}

			// 诊断：阶段结束后在主线程重新 apply 一次，看能不能把断链后遗漏的过滤补上。
			if (reapply)
			{
				EntityManager em2 = PartGameplayUtil.Em;
				int changed = PartCollisionFilter.Apply(em2, sim.Unmanaged);
				Line("reapply: changed=" + changed
					+ " mismatches=" + PartCollisionFilter.CountMismatches(em2, sim.Unmanaged));
				Line(PartCollisionFilter.Describe(em2, sim.Unmanaged));
			}

			// 2) drive 模式：只把角速度塞给质心附近的部件，让 5250 根关节去传递（会断，也会抖）。
			if (modeWord == "drive" || modeWord == "both")
			{
				yield return StartCoroutine(Phase("drive w=" + F(spin), ModeDrive, spin, duration));
			}

			// 3) 二分：以 spin 的刚体自旋为基准，逐组关系统。
			if (bisect)
			{
				yield return StartCoroutine(Phase("bisect base w=" + F(spin), ModeRigid, spin, duration));

				Line("-- render off --");
				SetRenderSystems(false);
				yield return StartCoroutine(Phase("no-render w=" + F(spin), ModeRigid, spin, duration));

				Line("-- render off + gameplay off --");
				SetGameplay(false);
				yield return StartCoroutine(Phase("no-render no-gameplay w=" + F(spin), ModeRigid, spin, duration));

				Line("-- gameplay off (render back on) --");
				SetRenderSystems(true);
				yield return StartCoroutine(Phase("no-gameplay w=" + F(spin), ModeRigid, spin, duration));

				SetGameplay(true);
				yield return StartCoroutine(Phase("restored w=" + F(spin), ModeRigid, spin, duration));

				yield return StartCoroutine(TogglePhase<PartSimulationSystem>("no-PartSimulationSystem", spin, duration));
				yield return StartCoroutine(TogglePhase<ConstraintSystem>("no-ConstraintSystem", spin, duration));
				yield return StartCoroutine(TogglePhase<FrameJointSystem>("no-FrameJointSystem", spin, duration));
				yield return StartCoroutine(TogglePhase<CustomBackgroundSystem>("no-CustomBackgroundSystem", spin, duration));
				// 巨构模式的三个系统（mega=0 时它们是空转，关掉看不出差别，属正常）。
				yield return StartCoroutine(TogglePhase<MegaStructureDriveSystem>("no-MegaDrive", spin, duration));
				yield return StartCoroutine(TogglePhase<MegaStructureMirrorSystem>("no-MegaMirror", spin, duration));
				yield return StartCoroutine(TogglePhase<MegaStructureDamageSystem>("no-MegaDamage", spin, duration));

				Line("-- transparent frame systems off --");
				SetTransparentFrames(false);
				yield return StartCoroutine(Phase("no-TransparentFrame w=" + F(spin), ModeRigid, spin, duration));
				SetTransparentFrames(true);
			}

			// 4) 组级二分：逐个关掉整个 system group，定位「剩下的开销在哪个阶段」。
			//    巨构模式下单个系统都关掉看不出差别时，只有这一层能说明问题。
			if (groupBisect)
			{
				yield return StartCoroutine(Phase("group base w=" + F(spin), ModeRigid, spin, duration));
				yield return StartCoroutine(TogglePhase<PartSimulationSystemGroup>("no-PartSimulationGroup", spin, duration));
				yield return StartCoroutine(TogglePhase<PhysicsSimulationGroup>("no-PhysicsSimulationGroup", spin, duration));
				yield return StartCoroutine(TogglePhase<FixedStepSimulationSystemGroup>("no-FixedStepGroup", spin, duration));
				yield return StartCoroutine(TogglePhase<TransformSystemGroup>("no-TransformGroup", spin, duration));
				yield return StartCoroutine(TogglePhase<PresentationSystemGroup>("no-PresentationGroup", spin, duration));
				yield return StartCoroutine(TogglePhase<SimulationSystemGroup>("no-SimulationGroup", spin, duration));
			}

			// 5) 系统级二分（sysbisect=1）：把 PartSimulationSystemGroup 里的每个玩法系统逐个关掉。
			//    组级二分已经把开销定位到这个组；再往下只能靠逐个开关。
			if (sysBisect)
			{
				yield return StartCoroutine(Phase("sys base w=" + F(spin), ModeRigid, spin, duration));
				string[] names =
				{
					"Bpsim.Parts.Simulation.WheelSystem",
					"Bpsim.Parts.Simulation.RopeSystem",
					"Bpsim.Parts.Simulation.EggSystem",
					"Bpsim.Parts.Simulation.FuelSystem",
					"Bpsim.Parts.Simulation.CircuitSystem",
					"Bpsim.Parts.Simulation.SandbagSystem",
					"Bpsim.Parts.Simulation.AutoGunProjectileSystem",
					"Bpsim.Parts.Simulation.FanPropellerSystem",
					"Bpsim.Parts.Simulation.MechanicalGateSystem",
					"Bpsim.Parts.Simulation.FixedPumpkinSystem",
					"Bpsim.Parts.Simulation.PigMotionSystem",
					"Bpsim.Parts.Simulation.GearboxSystem",
					"Bpsim.Parts.Simulation.PartPowerSystem",
				};
				World worldB = World.DefaultGameObjectInjectionWorld;
				for (int i = 0; i < names.Length; i++)
				{
					Type type = Type.GetType(names[i] + ", Assembly-CSharp");
					if (type == null)
					{
						Line("sys " + names[i] + ": type not found");
						continue;
					}
					ComponentSystemBase sysBase = worldB.GetExistingSystemManaged(type);
					if (sysBase == null)
					{
						Line("sys " + names[i] + ": not present");
						continue;
					}
					bool wasEnabled = sysBase.Enabled;
					sysBase.Enabled = false;
					string shortName = names[i].Substring(names[i].LastIndexOf('.') + 1);
					yield return StartCoroutine(Phase("no-" + shortName, ModeRigid, spin, duration));
					sysBase.Enabled = wasEnabled;
				}
			}

			Line("systems after: " + SystemReport());
			if (MegaStructure.Active)
			{
				string bodyPosition = "?";
				EntityManager endEm = PartGameplayUtil.Em;
				if (endEm.World != null && MegaStructure.Body != Entity.Null && endEm.HasComponent<LocalTransform>(MegaStructure.Body))
				{
					float3 p = endEm.GetComponentData<LocalTransform>(MegaStructure.Body).Position;
					bodyPosition = F(p.x) + "," + F(p.y) + "," + F(p.z);
				}
				// 掉件后这些部件应该重新拿到 PhysicsWorldIndex（变成独立刚体）。
				int droppedWithPhysics = 0;
				int droppedChecked = 0;
				NativeList<Entity> endParts = PartManager.Instance.PartSimulator.Unmanaged.Parts;
				for (int i = 0; i < endParts.Length && droppedChecked < 4096; i++)
				{
					droppedChecked++;
					if (endEm.HasComponent<PhysicsWorldIndex>(endParts[i]))
					{
						droppedWithPhysics++;
					}
				}
				Line("mega end: parts=" + MegaStructure.PartCount + " detached=" + (s_megaPartsStart - MegaStructure.PartCount)
					+ " mass=" + F(MegaStructure.TotalMass) + " bodyAt=" + bodyPosition
					+ " partsWithPhysics=" + droppedWithPhysics + "/" + droppedChecked
					+ " evtsTotal=" + MegaStructure.TotalEvents + " peakImpact=" + F(MegaStructure.PeakImpactSpeed)
					+ " detachedTotal=" + MegaStructure.DetachedTotal);
			}
			PartSimulator finalSim = PartManager.Instance == null ? null : PartManager.Instance.PartSimulator;
			if (finalSim != null)
			{
				EntityManager em = PartGameplayUtil.Em;
				Line("filters end: components=" + finalSim.Unmanaged.ConnectedComponents.Length
					+ " mismatches=" + PartCollisionFilter.CountMismatches(em, finalSim.Unmanaged)
					+ " joints=" + finalSim.Unmanaged.Joints.Length);
				Line(PartCollisionFilter.Describe(em, finalSim.Unmanaged));
			}
		}

		private IEnumerator TogglePhase<T>(string label, float spin, float duration) where T : ComponentSystemBase
		{
			bool toggled = Toggle<T>(false);
			yield return StartCoroutine(Phase(label + (toggled ? "" : "(missing)") + " w=" + F(spin), ModeRigid, spin, duration));
			if (toggled)
			{
				Toggle<T>(true);
			}
		}

		private IEnumerator Phase(string label, int mode, float omega, float seconds)
		{
			PartManager manager = PartManager.Instance;
			PartSimulator sim = manager == null ? null : manager.PartSimulator;
			if (sim == null)
			{
				Line("[" + label + "] no simulator");
				yield break;
			}

			float3 center = Centroid(sim);
			int jointsBefore = sim.Unmanaged.Joints.Length;
			int componentsBefore = sim.Unmanaged.ConnectedComponents.Length;
			int gcBefore = GC.CollectionCount(0);
			List<float> deltas = new List<float>(4096);
			float started = Time.realtimeSinceStartup;
			int frames = 0;
			int longFrames = 0;
			while (Time.realtimeSinceStartup - started < seconds)
			{
				yield return null;
				frames++;
				// writev=0：不写速度场。巨构模式下写部件速度等于"假装玩家每步在施力"
				// （MegaStructureDriveSystem 会把"实际速度 - 期望速度"当冲量加到整车刚体上），
				// 做撞地/掉件这类要跑真实动力学的测试时必须关掉。
				if (!s_writeVelocity)
				{
				}
				else if (s_translation != 0f)
				{
					ApplyTranslation(sim, s_translation);
				}
				else if (mode != ModeNone && omega != 0f)
				{
					if (mode == ModeRigid)
					{
						SetRigidSpin(sim, omega, center);
					}
					else
					{
						DriveSpin(sim, omega);
					}
				}
				float delta = Time.unscaledDeltaTime;
				deltas.Add(delta);
				if (delta > 0.033f)
				{
					longFrames++;
				}
			}
			float elapsed = Time.realtimeSinceStartup - started;
			int gc = GC.CollectionCount(0) - gcBefore;
			deltas.Sort();
			float sum = 0f;
			for (int i = 0; i < deltas.Count; i++)
			{
				sum += deltas[i];
			}
			float p50 = deltas.Count == 0 ? 0f : deltas[deltas.Count / 2] * 1000f;
			float p95 = deltas.Count == 0 ? 0f : deltas[math.min(deltas.Count - 1, (int)(deltas.Count * 0.95f))] * 1000f;
			float p99 = deltas.Count == 0 ? 0f : deltas[math.min(deltas.Count - 1, (int)(deltas.Count * 0.99f))] * 1000f;
			float min = deltas.Count == 0 ? 0f : deltas[0] * 1000f;
			float max = deltas.Count == 0 ? 0f : deltas[deltas.Count - 1] * 1000f;
			float avg = deltas.Count == 0 ? 0f : sum / deltas.Count * 1000f;
			string spinStats = SpinStats(sim);
			// mega 模式：把「本步碰撞事件数 / 超阈值命中数 / 最大接近速度」带出来，
			// 用来区分「事件根本没产生（材质没开 RaiseEvents）」和「事件有但速度不够」。
			string megaStats = "";
			if (MegaStructure.Active)
			{
				Entity megaBody = MegaStructure.Body;
				EntityManager megaEm = PartGameplayUtil.Em;
				string bodyInfo = "";
				if (megaBody != Entity.Null && megaEm.Exists(megaBody) && megaEm.HasComponent<LocalTransform>(megaBody))
				{
					float3 bp = megaEm.GetComponentData<LocalTransform>(megaBody).Position;
					float3 bv = megaEm.HasComponent<PhysicsVelocity>(megaBody) ? megaEm.GetComponentData<PhysicsVelocity>(megaBody).Linear : float3.zero;
					bodyInfo = " bodyP=" + F(bp.y) + " bodyV=" + F(bv.y);
				}
				megaStats = " evts=" + MegaStructure.LastEventCount + " hits=" + MegaStructure.LastHitCount
					+ " maxSpeed=" + F(MegaStructure.LastMaxSpeed) + " carParts=" + MegaStructure.PartCount + bodyInfo;
			}
			Line("[" + label + "] frames=" + frames + " fps=" + F(frames / math.max(0.001f, elapsed))
				+ " ms avg/p50/p95/p99/max=" + F(avg) + "/" + F(p50) + "/" + F(p95) + "/" + F(p99) + "/" + F(max)
				+ " min=" + F(min) + " slow=" + longFrames + " gc0=" + gc
				+ " joints=" + jointsBefore + "->" + sim.Unmanaged.Joints.Length
				+ " comp=" + componentsBefore + "->" + sim.Unmanaged.ConnectedComponents.Length
				+ " " + spinStats + megaStats);
		}

		private static float3 Centroid(PartSimulator sim)
		{
			EntityManager em = PartGameplayUtil.Em;
			NativeList<Entity> parts = sim.Unmanaged.Parts;
			float3 sum = float3.zero;
			int count = 0;
			for (int i = 0; i < parts.Length; i++)
			{
				Entity entity = parts[i];
				if (entity == Entity.Null || !em.Exists(entity) || !em.HasComponent<LocalTransform>(entity))
				{
					continue;
				}
				sum += em.GetComponentData<LocalTransform>(entity).Position;
				count++;
			}
			return count == 0 ? float3.zero : sum / count;
		}

		/// <summary>整体平移：所有部件拿到同一线速度，关节相对速度为零（不断链），只放大宽相/窄相的运动膨胀。</summary>
		private static void ApplyTranslation(PartSimulator sim, float speed)
		{
			EntityManager em = PartGameplayUtil.Em;
			NativeList<Entity> parts = sim.Unmanaged.Parts;
			for (int i = 0; i < parts.Length; i++)
			{
				Entity entity = parts[i];
				if (entity == Entity.Null || !em.Exists(entity) || !em.HasComponent<PhysicsVelocity>(entity))
				{
					continue;
				}
				PhysicsVelocity velocity = em.GetComponentData<PhysicsVelocity>(entity);
				velocity.Linear = new float3(speed, 0f, 0f);
				velocity.Angular = float3.zero;
				em.SetComponentData(entity, velocity);
			}
		}

		/// <summary>刚体速度场：每个部件都拿到 ω×r 的线速度 + ω 的角速度，关节上几乎没有相对速度。</summary>
		private static void SetRigidSpin(PartSimulator sim, float omega, float3 center)
		{
			EntityManager em = PartGameplayUtil.Em;
			NativeList<Entity> parts = sim.Unmanaged.Parts;
			float3 axis = new float3(0f, 0f, omega);
			for (int i = 0; i < parts.Length; i++)
			{
				Entity entity = parts[i];
				if (entity == Entity.Null || !em.Exists(entity) || !em.HasComponent<PhysicsVelocity>(entity))
				{
					continue;
				}
				float3 position = em.GetComponentData<LocalTransform>(entity).Position;
				PhysicsVelocity velocity = em.GetComponentData<PhysicsVelocity>(entity);
				velocity.Linear = s_linear ? math.cross(axis, position - center) : float3.zero;
				float speed = math.length(velocity.Linear);
				if (speed > 500f)
				{
					velocity.Linear *= 500f / speed;
				}
				velocity.Angular = s_angular ? axis : float3.zero;
				em.SetComponentData(entity, velocity);
			}
		}

		/// <summary>扭矩模式：只给质心最近的部件加角速度，其余靠关节传递（真实开车时的传动路径）。</summary>
		private static void DriveSpin(PartSimulator sim, float omega)
		{
			EntityManager em = PartGameplayUtil.Em;
			NativeList<Entity> parts = sim.Unmanaged.Parts;
			if (parts.Length == 0)
			{
				return;
			}
			float3 center = Centroid(sim);
			Entity best = Entity.Null;
			float bestDistance = float.MaxValue;
			for (int i = 0; i < parts.Length; i++)
			{
				Entity entity = parts[i];
				if (entity == Entity.Null || !em.Exists(entity) || !em.HasComponent<PhysicsVelocity>(entity))
				{
					continue;
				}
				float distance = math.distancesq(em.GetComponentData<LocalTransform>(entity).Position, center);
				if (distance < bestDistance)
				{
					bestDistance = distance;
					best = entity;
				}
			}
			if (best == Entity.Null)
			{
				return;
			}
			PhysicsVelocity velocity = em.GetComponentData<PhysicsVelocity>(best);
			velocity.Angular = new float3(0f, 0f, omega);
			em.SetComponentData(best, velocity);
		}

		private static string SpinStats(PartSimulator sim)
		{
			EntityManager em = PartGameplayUtil.Em;
			NativeList<Entity> parts = sim.Unmanaged.Parts;
			double angle = 0.0;
			double linear = 0.0;
			double maxAngle = 0.0;
			int count = 0;
			for (int i = 0; i < parts.Length; i++)
			{
				Entity entity = parts[i];
				if (entity == Entity.Null || !em.Exists(entity) || !em.HasComponent<PhysicsVelocity>(entity))
				{
					continue;
				}
				PhysicsVelocity velocity = em.GetComponentData<PhysicsVelocity>(entity);
				double angular = math.length(velocity.Angular);
				angle += angular;
				linear += math.length(velocity.Linear);
				maxAngle = math.max(maxAngle, angular);
				count++;
			}
			if (count == 0)
			{
				return "spin=(none)";
			}
			return "avgW=" + F((float)(angle / count)) + " maxW=" + F((float)maxAngle) + " avgV=" + F((float)(linear / count));
		}


		/// <summary>检查为什么没有生成关节：扩展数据、PartMap、关节实体各自数一遍。</summary>
		/// <summary>数一遍所有关节实体上的 PhysicsConstrainedBodyPair.EnableCollision。</summary>
		private static void CountJointPairs(EntityManager em, out int total, out int colliding)
		{
			total = 0;
			colliding = 0;
			try
			{
				using EntityQuery query = em.CreateEntityQuery(ComponentType.ReadOnly<PhysicsConstrainedBodyPair>());
				using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
				total = entities.Length;
				for (int i = 0; i < entities.Length; i++)
				{
					PhysicsConstrainedBodyPair pair = em.GetComponentData<PhysicsConstrainedBodyPair>(entities[i]);
					if (pair.EnableCollision != 0)
					{
						colliding++;
					}
				}
			}
			catch (Exception)
			{
				total = -1;
				colliding = -1;
			}
		}

		/// <summary>把每根关节的 EnableCollision 改掉（只用于诊断对照）。</summary>
		private static int SetJointCollision(EntityManager em, bool enabled)
		{
			int changed = 0;
			try
			{
				int want = enabled ? 1 : 0;
				using EntityQuery query = em.CreateEntityQuery(ComponentType.ReadWrite<PhysicsConstrainedBodyPair>());
				using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
				for (int i = 0; i < entities.Length; i++)
				{
					PhysicsConstrainedBodyPair pair = em.GetComponentData<PhysicsConstrainedBodyPair>(entities[i]);
					if (pair.EnableCollision != want)
					{
						pair.EnableCollision = want;
						em.SetComponentData(entities[i], pair);
						changed++;
					}
				}
			}
			catch (Exception exception)
			{
				Line("jointcollide error: " + exception.Message);
				return -1;
			}

			return changed;
		}

		private static int JointEntityCount(EntityManager em)
		{
			try
			{
				return em.CreateEntityQuery(ComponentType.ReadOnly<PhysicsJoint>()).CalculateEntityCount();
			}
			catch (Exception)
			{
				return -1;
			}
		}

		private static string Diagnose(PartManager manager)
		{
			StringBuilder builder = new StringBuilder();
			EntityManager em = PartGameplayUtil.Em;
			PartSimulator sim = manager.PartSimulator;
			PartScene scene = manager.ActiveScene;
			Dictionary<PartTypeInfo, PartExtensionData> map = manager.Factory == null ? null : manager.Factory.PartExtensionMap;
			builder.Append("extMap=").Append(map == null ? -1 : map.Count);
			builder.Append(' ').Append(CircuitSystem.Stats());
			if (map != null)
			{
				builder.Append(" sample:");
				int shown = 0;
				foreach (KeyValuePair<PartTypeInfo, PartExtensionData> pair in map)
				{
					builder.Append(' ').Append(pair.Key).Append("=src").Append(pair.Value.IsConnectionSource ? 1 : 0)
						.Append("/dir").Append((int)pair.Value.ConnectionDirection)
						.Append("/str").Append(F(pair.Value.ConnectionStrength));
					if (++shown >= 4)
					{
						break;
					}
				}
			}
			if (scene != null)
			{
				builder.Append(" buildMap=").Append(scene.Unmanaged.PartMap.Count);
			}
			if (sim != null)
			{
				builder.Append(" simMap=").Append(sim.Unmanaged.PartMap.Count);
				try
				{
					builder.Append(" jointEntities=").Append(em.CreateEntityQuery(ComponentType.ReadOnly<PhysicsJoint>()).CalculateEntityCount());
					builder.Append(" constrPairs=").Append(em.CreateEntityQuery(ComponentType.ReadOnly<PhysicsConstrainedBodyPair>()).CalculateEntityCount());
				}
				catch (Exception exception)
				{
					builder.Append(" queryFailed=").Append(exception.Message);
				}
				NativeList<Entity> parts = sim.Unmanaged.Parts;
				int sources = 0;
				int withNeighbour = 0;
				int withJointState = 0;
				for (int i = 0; i < parts.Length; i++)
				{
					Entity entity = parts[i];
					if (!em.Exists(entity))
					{
						continue;
					}
					PartExtensionData extension = em.GetComponentData<PartExtensionComponent>(entity).Value;
					PartTransform partTransform = em.GetComponentData<PartTransform>(entity);
					if (extension.IsConnectionSource)
					{
						sources++;
					}
					byte layer = em.HasComponent<PartBuildLayer>(entity) ? PartBuildLayer.Clamp(em.GetComponentData<PartBuildLayer>(entity).Value) : PartBuildLayer.MinLayer;
					bool neighbour = false;
					for (int j = 0; j < 4 && !neighbour; j++)
					{
						BitDirection direction = BitDirectionExtensions.FromIndex(j);
						var (dx, dy) = direction.ToVector();
						neighbour = sim.Unmanaged.PartMap.TryGet(partTransform.X + dx, partTransform.Y + dy, layer, out PartGridInfo _);
					}
					if (neighbour)
					{
						withNeighbour++;
					}
					if (em.HasComponent<PartJointInfo>(entity))
					{
						withJointState++;
					}
				}
				builder.Append(" partsWithSource=").Append(sources).Append('/').Append(parts.Length);
				builder.Append(" partsWithNeighbour=").Append(withNeighbour);
				builder.Append(" partJointInfoOnPart=").Append(withJointState);
				if (parts.Length > 0 && em.Exists(parts[0]))
				{
					PartExtensionData extension = em.GetComponentData<PartExtensionComponent>(parts[0]).Value;
					PartTransform partTransform = em.GetComponentData<PartTransform>(parts[0]);
					PartTypeValue typeValue = em.GetComponentData<PartTypeValue>(parts[0]);
					builder.Append(" part0=(").Append(partTransform.X).Append(',').Append(partTransform.Y)
						.Append(") type=").Append(typeValue.Type).Append('#').Append(typeValue.Index)
						.Append(" src=").Append(extension.IsConnectionSource ? 1 : 0)
						.Append(" dir=").Append((int)extension.ConnectionDirection)
						.Append(" str=").Append(F(extension.ConnectionStrength))
						.Append(" map=").Append(sim.Unmanaged.PartMap.Contains(partTransform.X, partTransform.Y, PartBuildLayer.MinLayer) ? 1 : 0);
				}
			}
			World world = World.DefaultGameObjectInjectionWorld;
			if (world != null && world.IsCreated)
			{
				PartSimulationSystem system = world.GetExistingSystemManaged<PartSimulationSystem>();
				builder.Append(" partSimulationSystem=").Append(system == null ? "missing" : (system.Enabled ? "on" : "off"));
			}
			return builder.ToString();
		}

		#region systems


		private static bool Toggle<T>(bool enable) where T : ComponentSystemBase
		{
			try
			{
				World world = World.DefaultGameObjectInjectionWorld;
				if (world == null || !world.IsCreated)
				{
					return false;
				}
				T system = world.GetExistingSystemManaged<T>();
				if (system == null)
				{
					return false;
				}
				system.Enabled = enable;
				return true;
			}
			catch (Exception exception)
			{
				Line("toggle " + typeof(T).Name + "=" + enable + " failed: " + exception.Message);
				return false;
			}
		}

		private static void SetRenderSystems(bool enable)
		{
			Toggle<SceneRenderSystem>(enable);
			Toggle<PartRenderSystem>(enable);
			Toggle<SkinSheetRenderSystem>(enable);
			Toggle<SkinSheetSystem>(enable);
			Toggle<PartBuildLayerDimSystem>(enable);
			Toggle<MechanicalGateVisualSystem>(enable);
			Toggle<SpriteSystem>(enable);
		}

		private static void SetTransparentFrames(bool enable)
		{
			Toggle<TransparentFrameSystem>(enable);
			Toggle<ColoredFrameSystem>(enable);
		}

		private static void SetGameplay(bool enable)
		{
			PartGameplayManager gameplay = PartGameplayManager.Instance;
			if (gameplay == null)
			{
				Line("gameplay: no PartGameplayManager");
				return;
			}
			gameplay.enabled = enable;
			Line("gameplay.enabled=" + enable);
		}

		private static void RestoreSystems()
		{
			SetRenderSystems(true);
			SetTransparentFrames(true);
			World world = World.DefaultGameObjectInjectionWorld;
			if (world != null && world.IsCreated)
			{
				Toggle<PartSimulationSystem>(true);
				Toggle<ConstraintSystem>(true);
				Toggle<FrameJointSystem>(true);
				Toggle<CustomBackgroundSystem>(true);
			}
		}

		private static string SystemReport()
		{
			StringBuilder builder = new StringBuilder();
			Append(builder, "SceneRenderSystem", Exists<SceneRenderSystem>());
			Append(builder, "PartRenderSystem", Exists<PartRenderSystem>());
			Append(builder, "PartSimulationSystem", Exists<PartSimulationSystem>());
			Append(builder, "ConstraintSystem", Exists<ConstraintSystem>());
			Append(builder, "FrameJointSystem", Exists<FrameJointSystem>());
			Append(builder, "TransparentFrameSystem", Exists<TransparentFrameSystem>());
			Append(builder, "ColoredFrameSystem", Exists<ColoredFrameSystem>());
			Append(builder, "PartGameplayManager", PartGameplayManager.Instance != null);
			PartGameplayManager gameplay = PartGameplayManager.Instance;
			if (gameplay != null)
			{
				builder.Append(" behaviorsRunning=").Append(gameplay.IsRunning);
			}
			return builder.ToString();
		}

		private static void Append(StringBuilder builder, string name, bool exists)
		{
			builder.Append(name).Append('=').Append(exists ? "1" : "0").Append(' ');
		}

		private static bool Exists<T>() where T : ComponentSystemBase
		{
			try
			{
				World world = World.DefaultGameObjectInjectionWorld;
				return world != null && world.IsCreated && world.GetExistingSystemManaged<T>() != null;
			}
			catch (Exception)
			{
				return false;
			}
		}

		#endregion

		#region spec + log

		private static float SpecFloat(string spec, string key, float fallback)
		{
			string word = SpecWord(spec, key, null);
			return word == null ? fallback : ParseFloat(word, fallback);
		}

		private static int SpecInt(string spec, string key, int fallback)
		{
			string word = SpecWord(spec, key, null);
			return word == null ? fallback : (int)ParseFloat(word, fallback);
		}

		private static float ParseFloat(string text, float fallback)
		{
			float result;
			return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ? result : fallback;
		}

		private static string SpecWord(string spec, string key, string fallback)
		{
			if (string.IsNullOrEmpty(spec))
			{
				return fallback;
			}
			string[] parts = spec.Split(';');
			for (int i = 0; i < parts.Length; i++)
			{
				string part = parts[i].Trim();
				int index = part.IndexOf('=');
				if (index <= 0)
				{
					continue;
				}
				if (string.Equals(part.Substring(0, index).Trim(), key, StringComparison.OrdinalIgnoreCase))
				{
					return part.Substring(index + 1).Trim();
				}
			}
			return fallback;
		}

		private static void WriteLog()
		{
			try
			{
				Directory.CreateDirectory("Logs");
				File.WriteAllText(LogFile, s_report.ToString(), new UTF8Encoding(false));
			}
			catch (Exception exception)
			{
				Debug.LogWarning("[SpinPerf] write log failed: " + exception.Message);
			}
		}

		#endregion
	}
}
#endif
