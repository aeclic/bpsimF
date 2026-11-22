#if UNITY_EDITOR || BPSIM_SAVE_BENCHMARK
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Bpsim.Background;
using Bpsim.Levels;
using Bpsim.Parts;
using Bpsim.Parts.Gameplay;
using Bpsim.Parts.Simulation;
using Bpsim.Parts.Skin;
using Bpsim.Physics;
using Bpsim.Physics.Anvil;
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
	///   solver=     模拟开始时写入的 SolverIterationCount，默认不改（-1）；Anvil 下映射成 4–16 原生子步
	///   workers=    Anvil box3d 求解线程数，默认不改（-1，AnvilWorld 默认 1）；需在 Anvil 世界创建前设置
	///   gravity=    模拟开始时写入的 GravityY，默认 0（悬空自旋，避免落地噪音）
	///   spin=       二分阶段使用的角速度，默认 32
	///   bisect=     1/0 是否跑系统二分，默认 1
	///   mode=       rigid/drive/both，默认 both（rigid = 每帧写刚体速度场，drive = 只给质心附近加角速度）
	/// 结果同时写到 Logs/spinperf.log。
	/// </remarks>
	public class SpinPerfProbe : MonoBehaviour
	{
		private const string LogFile = "Logs/spinperf.log";
		private const int FixedStartupSteps = 4;

		private static SpinPerfProbe s_instance;

		private static readonly StringBuilder s_report = new StringBuilder();

		private static string s_status = "idle";

		private static string s_logPath = LogFile;

		private static string s_jsonPath;

		[Serializable]
		private sealed class SaveRunReport
		{
			public int schema = 2;
			public string runId;
			public string utc;
			public string unity;
			public string host;
			public string spec;
			public string inputPath;
			public string inputSha256;
			public string requestedBackend;
			public string actualBackend;
			public string error;
			public string physicsAssemblyId;
			public string nativeSha256;
			public int nativeBridgeVersion;
			public bool nativeWarmStarting;
			public int startupSteps;
			public int loadedJointEntities;
			public int parts;
			public int joints;
			public int components;
			public int jointEntities;
			public int solverIterations;
			public int workerCount;
			public float fixedTimeStep;
			public float gravityX;
			public float gravityY;
			public List<SavePhaseReport> phases = new List<SavePhaseReport>();
		}

		[Serializable]
		private sealed class SavePhaseReport
		{
			public string label;
			public int frames;
			public int jointsBefore;
			public int jointsAfter;
			public int componentsBefore;
			public int componentsAfter;
			public double frameP50Ms;
			public double frameP95Ms;
			public double frameP99Ms;
			public double frameMaxMs;
			public double physicsP50Ms;
			public double physicsP95Ms;
			public double physicsP99Ms;
			public double physicsMaxMs;
			public int completedPhysicsSteps;
			public int anvilStepDelta;
			public string stateSha256;
			public double centroidX;
			public double centroidY;
			public double meanSpeed;
			public double maxSpeed;
			public double meanAngularSpeed;
			public double maxAngularSpeed;
			public int fixedJointCount;
			public double rmsFixedAnchorError;
			public double maxFixedAnchorError;
			public int nonFiniteParts;
			public List<float> frameTimesMs = new List<float>();
			public List<PhysicsStepSample> physicsSteps = new List<PhysicsStepSample>();
		}

		[Serializable]
		private struct PhysicsStepSample
		{
			public double totalMs;
			public double worldBuildWaitMs;
			public double syncToMs;
			public double stepMs;
			public double syncFromMs;
			public double treeMs;
			public double nativeMs;
			public double nativeSolveMs;
			public double nativePrepareMs;
			public double nativeWarmStartMs;
			public double nativeSolveImpulseMs;
			public double nativeRelaxMs;
			public int anvilStepDelta;
			public bool integratedSubsteps;
		}

		private static SaveRunReport s_runReport;

		private static List<PhysicsStepSample> s_physicsSamples;

		private static long s_physicsStart;

		private static int s_anvilStepStart;

		private static int s_physicsAnvilStepStart;

		private static bool s_capturePhysics;

		private static int s_measureSteps;

		private static int s_captureTargetSteps;

		private static bool s_fixedClock;
		private static float s_captureDeltaBackup;
		private static FixedStepSimulationSystemGroup s_captureGroup;
		private static IRateManager s_rateBackup;
		private static int s_completedPhysicsSteps;
		private static int s_traceFirstTick;
		private static int s_traceSteps;

		private static bool s_backendOverride;

		private static SimulationType s_backendBackup;

		private static bool s_workersChanged;

		private static int s_workersBackup;

		private static int s_originalSceneId = -1;

		private static int s_testSceneId = -1;

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

#if BPSIM_SAVE_BENCHMARK && !UNITY_EDITOR
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void RunPlayerBenchmark()
		{
			string[] args = Environment.GetCommandLineArgs();
			int index = Array.IndexOf(args, "-bpsimSaveBenchmark");
			if (index < 0) return;
			if (index + 1 == args.Length)
			{
				Application.Quit(2);
				return;
			}
			Ensure().StartCoroutine(PlayerBenchmark(args[index + 1]));
		}

		private static IEnumerator PlayerBenchmark(string spec)
		{
			Application.runInBackground = true;
			QualitySettings.vSyncCount = 0;
			float deadline = Time.realtimeSinceStartup + 120f;
			while ((PartManager.Instance == null || !PartManager.Instance.IsReady || UserSettings.Instance == null)
				&& Time.realtimeSinceStartup < deadline)
				yield return null;
			if (!Run(spec).StartsWith("started:", StringComparison.Ordinal))
			{
				Application.Quit(2);
				yield break;
			}
			while (s_status == "running") yield return null;
			bool complete = s_runReport.error == null && s_runReport.phases.Count == 2 && File.Exists(s_jsonPath);
			Application.Quit(complete ? 0 : 1);
		}
#endif

		public static string Run(string spec)
		{
			if (s_status == "running")
			{
				return "already running\n" + s_report;
			}
			string actualSpec = spec ?? string.Empty;
			string label = SpecWord(actualSpec, "label", null);
			if (!string.IsNullOrEmpty(label) && !IsSafeLabel(label))
			{
				return "error: label may contain only letters, digits, '_' and '-'";
			}
			SpinPerfProbe probe = Ensure();
			s_report.Length = 0;
			s_logPath = string.IsNullOrEmpty(label) ? LogFile : Path.Combine("Logs", "spinperf-" + label + ".log");
			s_jsonPath = string.IsNullOrEmpty(label) ? null : Path.Combine("Logs", "spinperf-" + label + ".json");
			s_runReport = new SaveRunReport
			{
				runId = Guid.NewGuid().ToString("N"),
				utc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
				unity = Application.unityVersion,
				host = Application.isEditor ? "Editor" : "Player",
				spec = actualSpec,
				requestedBackend = SpecWord(actualSpec, "backend", ""),
				physicsAssemblyId = typeof(AnvilWorld).Module.ModuleVersionId.ToString("N")
			};
			s_measureSteps = math.max(0, SpecInt(actualSpec, "steps", 0));
			s_status = "running";
			GameObject go = probe.gameObject;
			go.name = "[SpinPerfProbe]";
			probe.StartCoroutine(probe.RunRoutine(actualSpec));
			return "started: " + spec;
		}

		public static string Stop()
		{
			if (s_instance != null)
			{
				s_instance.StopAllCoroutines();
			}
			s_status = "stopped";
			RestoreProbeState();
			WriteLog();
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
			RestoreProbeState();
			s_status = "done";
			Line("=== done ===");
			WriteLog();
		}

		private static void RestoreProbeState()
		{
			s_capturePhysics = false;
			s_physicsSamples = null;
			s_captureTargetSteps = 0;
			s_traceSteps = 0;
			if (s_fixedClock)
			{
				Time.captureDeltaTime = s_captureDeltaBackup;
				if (s_captureGroup != null && s_captureGroup.World.IsCreated)
					s_captureGroup.RateManager = s_rateBackup;
				s_captureGroup = null;
				s_rateBackup = null;
				s_fixedClock = false;
			}
			if (s_backendOverride)
			{
				PartManager manager = PartManager.Instance;
				if (manager != null && manager.IsSimulating)
				{
					manager.EndSimulation();
				}
				if (AnvilStepSystem.Anvil != null && AnvilStepSystem.Anvil.IsCreated)
				{
					AnvilStepSystem.Anvil.Dispose();
				}
				if (UserSettings.Instance != null)
				{
					UserSettings.Instance.SimulationSettings.SimulationType = s_backendBackup;
				}
				if (manager != null && s_testSceneId >= 0)
				{
					manager.UnloadScene(s_testSceneId);
					if (s_originalSceneId >= 0)
					{
						manager.SelectScene(s_originalSceneId);
					}
				}
				s_testSceneId = -1;
				s_originalSceneId = -1;
				s_backendOverride = false;
			}
			if (s_workersChanged)
			{
				AnvilStepSystem.WorkerCount = s_workersBackup;
				s_workersChanged = false;
			}
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
		}

		private IEnumerator RunCore(string spec)
		{
			float duration = SpecFloat(spec, "dur", 2.5f);
			int solver = SpecInt(spec, "solver", -1);
			int workers = SpecInt(spec, "workers", -1);
			int warmupSteps = math.max(0, SpecInt(spec, "warmupsteps", 0));
			string backendName = SpecWord(spec, "backend", null);
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
			if (!string.IsNullOrEmpty(backendName) && manager.IsSimulating)
			{
				Line("error: backend benchmark refuses to replace an active simulation");
				yield break;
			}

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
			s_originalSceneId = manager.ActiveScene == null ? -1 : manager.ActiveScene.SceneID;
			if (UserSettings.Instance == null)
			{
				Line("error: UserSettings is unavailable");
				yield break;
			}
			if (s_runReport != null)
			{
				s_runReport.inputPath = Path.GetFullPath(path);
				s_runReport.inputSha256 = HashFile(path);
			}
			SimulationType requestedBackend = SimulationType.UnityPhysics;
			if (!string.IsNullOrEmpty(backendName))
			{
				if (!Enum.TryParse(backendName, true, out requestedBackend) || !Enum.IsDefined(typeof(SimulationType), requestedBackend))
				{
					Line("error: unknown backend " + backendName);
					yield break;
				}
				s_backendBackup = UserSettings.Instance.SimulationSettings.SimulationType;
				s_backendOverride = true;
			}

			if (!float.IsNaN(gravity) || solver >= 0 || infinite >= 0 || !float.IsNaN(strengthFactor) || createJoints >= 0 || selfCollide >= 0 || mega >= 0 || !float.IsNaN(megaBreak) || !string.IsNullOrEmpty(backendName))
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
				if (!string.IsNullOrEmpty(backendName))
				{
					settings.SimulationType = requestedBackend;
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

			if (warmupSteps > 0 && s_measureSteps > 0)
			{
				if (Time.timeScale != 1f)
				{
					RejectRun("Fixed-step benchmark requires timeScale=1");
					yield break;
				}
				int remainingJoints = JointEntityCount(PartGameplayUtil.Em);
				if (remainingJoints != 0)
				{
					RejectRun("Fixed-step benchmark requires a clean world. Existing joint entities: " + remainingJoints);
					yield break;
				}
				s_captureDeltaBackup = Time.captureDeltaTime;
				float fixedTimeStep = UserSettings.Instance.SimulationSettings.FixedTimeStep;
				s_captureGroup = PartGameplayUtil.Em.World.GetExistingSystemManaged<FixedStepSimulationSystemGroup>();
				s_rateBackup = s_captureGroup.RateManager;
				s_captureGroup.RateManager = new RateUtils.FixedRateSimpleManager(fixedTimeStep);
				Time.captureDeltaTime = fixedTimeStep;
				s_fixedClock = true;
			}

			string error = null;
			bool loaded = false;
			PartScene loadedScene = null;
			Action<PartScene> sceneLoaded = scene => loadedScene = scene;
			manager.SceneLoaded += sceneLoaded;
			try
			{
				using (Stream stream = File.OpenRead(path))
				{
					manager.LoadScene(stream, Path.GetFileNameWithoutExtension(path), SchematicsFormat.Auto);
				}
				loaded = loadedScene != null;
				if (loaded)
				{
					manager.SelectScene(loadedScene.SceneID);
					s_testSceneId = loadedScene.SceneID;
				}
			}
			catch (Exception exception)
			{
				error = exception.ToString();
			}
			finally
			{
				manager.SceneLoaded -= sceneLoaded;
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

			if (workers > 0)
			{
				if (!s_workersChanged)
				{
					s_workersBackup = AnvilStepSystem.WorkerCount;
					s_workersChanged = true;
				}
				AnvilStepSystem.WorkerCount = workers;
				Line("anvil workers=" + workers + " (applies on next Anvil step)");
			}
			System.Diagnostics.Stopwatch startWatch = System.Diagnostics.Stopwatch.StartNew();
			int startupTick = s_completedPhysicsSteps;
			int startupAnvil = AnvilStepSystem.StepCount;
			manager.StartSimulation();
			startWatch.Stop();
			s_traceFirstTick = startupTick;
			s_traceSteps = math.clamp(SpecInt(spec, "tracesteps", 0), 0, 256);
			if (SpecInt(spec, "traceignorehalfcolliders", 0) != 0)
			{
				EntityManager entityManager = PartGameplayUtil.Em;
				using EntityQuery query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<HingePlateCollisionNetwork>(), ComponentType.ReadOnly<PhysicsCollider>());
				using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
				foreach (Entity entity in entities)
					if (entityManager.GetComponentData<HingePlateCollisionNetwork>(entity).IsHalf)
						entityManager.RemoveComponent<PhysicsCollider>(entity);
			}
			if (s_fixedClock)
			{
				s_runReport.loadedJointEntities = JointEntityCount(PartGameplayUtil.Em);
				Line("start: loaded joint entities=" + s_runReport.loadedJointEntities);
			}
			Line("start: StartSimulation " + F((float)startWatch.Elapsed.TotalMilliseconds) + "ms");
			if (s_fixedClock)
			{
				float deadline = Time.realtimeSinceStartup + math.clamp(SpecInt(spec, "startupseconds", 180), 30, 600);
				while (s_completedPhysicsSteps - startupTick < FixedStartupSteps && Time.realtimeSinceStartup < deadline)
					yield return null;
				s_runReport.startupSteps = s_completedPhysicsSteps - startupTick;
				if (s_runReport.startupSteps != FixedStartupSteps)
				{
					RejectRun("Fixed-step startup ran " + s_runReport.startupSteps + " ticks; expected " + FixedStartupSteps);
					yield break;
				}
			}
			else
			{
				yield return null;
				yield return null;
			}

			// 早退开关：在 warmup 之前关掉整块玩法或 BPLE 灯运行时，用来把「初始 5250 关节时的
			// 物理开销」与「玩法开销」分开，而不是等二分阶段部件已经散架。
			if (SpecInt(spec, "gameplay", 1) == 0)
			{
				PartGameplayManager earlyGameplay = PartGameplayManager.Instance;
				if (earlyGameplay != null)
				{
					earlyGameplay.enabled = false;
					Line("early: PartGameplayManager disabled (physics/systems only)");
				}
			}
			if (SpecInt(spec, "lights", 1) == 0)
			{
				BpleLightRuntime.Instance?.End();
				Line("early: BpleLightRuntime.End() (behaviors only, no light contact scan)");
			}
			if (SpecInt(spec, "lightprofile", 0) != 0)
			{
				BpleLightRuntime.SetProfiling(true);
				Line("early: BpleLightRuntime profiling enabled");
			}

			PartSimulator sim = manager.PartSimulator;
			if (sim == null)
			{
				Line("error: simulator is null after StartSimulation");
				yield break;
			}
			EntityManager jointsEm = PartGameplayUtil.Em;
			int jointEntities = JointEntityCount(jointsEm);
			SimulationType actualBackend = SimulationType.UnityPhysics;
			using (EntityQuery stepQuery = jointsEm.CreateEntityQuery(ComponentType.ReadOnly<PhysicsStep>()))
			{
				if (stepQuery.CalculateEntityCount() == 1)
				{
					actualBackend = stepQuery.GetSingleton<PhysicsStep>().SimulationType;
				}
			}
			if (s_fixedClock && actualBackend != SimulationType.AnvilPhysics)
			{
				using EntityQuery simulationQuery = jointsEm.CreateEntityQuery(ComponentType.ReadOnly<SimulationSingleton>());
				if (!simulationQuery.TryGetSingleton(out SimulationSingleton simulation) || simulation.Type != actualBackend)
				{
					RejectRun("The scheduled simulation does not match PhysicsStep");
					yield break;
				}
			}
			if (s_fixedClock && actualBackend == SimulationType.AnvilPhysics && AnvilStepSystem.StepCount - startupAnvil != FixedStartupSteps)
			{
				RejectRun("Anvil did not advance every startup tick; inspect the Unity console");
				yield break;
			}
			if (s_runReport != null)
			{
				s_runReport.actualBackend = actualBackend.ToString();
				s_runReport.nativeBridgeVersion = actualBackend == SimulationType.AnvilPhysics ? AnvilWorld.NativeBridgeVersion : 0;
				s_runReport.nativeWarmStarting = actualBackend == SimulationType.AnvilPhysics && AnvilStepSystem.Anvil?.WarmStartingEnabled == true;
				if (actualBackend != SimulationType.UnityPhysics)
				{
					string library = actualBackend == SimulationType.AnvilPhysics ? AnvilNative.Library : "HavokNative";
					string nativePath = Path.Combine(Application.dataPath, "Plugins", "x86_64", library + ".dll");
					s_runReport.nativeSha256 = File.Exists(nativePath) ? HashFile(nativePath) : null;
				}
				s_runReport.parts = sim.Unmanaged.Parts.Length;
				s_runReport.joints = sim.Unmanaged.Joints.Length;
				s_runReport.components = sim.Unmanaged.ConnectedComponents.Length;
				s_runReport.jointEntities = jointEntities;
				s_runReport.solverIterations = UserSettings.Instance.SimulationSettings.SolverIterationCount;
				s_runReport.workerCount = AnvilStepSystem.WorkerCount;
				s_runReport.fixedTimeStep = UserSettings.Instance.SimulationSettings.FixedTimeStep;
				s_runReport.gravityX = UserSettings.Instance.SimulationSettings.GravityX;
				s_runReport.gravityY = UserSettings.Instance.SimulationSettings.GravityY;
			}
			Line("backend: requested=" + (string.IsNullOrEmpty(backendName) ? "unchanged" : backendName)
				+ " actual=" + actualBackend + " AnvilActive=" + AnvilStepSystem.Active);
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
					yield return StartCoroutine(Phase("rigid-nj w=" + F(spin), ModeRigid, spin, duration, s_measureSteps));
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

			if (s_fixedClock)
			{
				int startTick = s_completedPhysicsSteps;
				yield return Phase("warmup", ModeNone, 0f, 0.5f, warmupSteps);
				if (s_completedPhysicsSteps - startTick != warmupSteps)
				{
					RejectRun("Unmatched or unmeasured ticks at the warmup boundary");
					yield break;
				}
			}
			else
			{
				yield return StartCoroutine(Phase("warmup", ModeNone, 0f, 0.5f, warmupSteps));
			}

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
				yield return Phase("single w=" + F(spin), ModeRigid, spin, duration, s_measureSteps);
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

		private IEnumerator Phase(string label, int mode, float omega, float seconds, int targetPhysicsSteps = 0)
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
			List<PhysicsStepSample> physicsSamples = new List<PhysicsStepSample>(math.max(256, targetPhysicsSteps));
			s_physicsSamples = physicsSamples;
			s_captureTargetSteps = targetPhysicsSteps;
			s_physicsStart = 0;
			s_anvilStepStart = AnvilStepSystem.StepCount;
			s_capturePhysics = true;
			int firstTick = s_completedPhysicsSteps;
			float started = Time.realtimeSinceStartup;
			long frameStart = System.Diagnostics.Stopwatch.GetTimestamp();
			int frames = 0;
			int longFrames = 0;
			while (targetPhysicsSteps > 0 ? physicsSamples.Count < targetPhysicsSteps : Time.realtimeSinceStartup - started < seconds)
			{
				if (targetPhysicsSteps > 0 && Time.realtimeSinceStartup - started > 300f)
				{
					Line("[" + label + "] fixed-step capture timed out at " + physicsSamples.Count + "/" + targetPhysicsSteps);
					break;
				}
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
				long frameEnd = System.Diagnostics.Stopwatch.GetTimestamp();
				float delta = s_fixedClock ? (float)((frameEnd - frameStart) / (double)System.Diagnostics.Stopwatch.Frequency) : Time.unscaledDeltaTime;
				frameStart = frameEnd;
				deltas.Add(delta);
				if (delta > 0.033f)
				{
					longFrames++;
				}
			}
			s_capturePhysics = false;
			s_physicsSamples = null;
			s_captureTargetSteps = 0;
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
			SavePhaseReport phaseReport = new SavePhaseReport
			{
				label = label,
				completedPhysicsSteps = s_completedPhysicsSteps - firstTick,
				frames = frames,
				jointsBefore = jointsBefore,
				jointsAfter = sim.Unmanaged.Joints.Length,
				componentsBefore = componentsBefore,
				componentsAfter = sim.Unmanaged.ConnectedComponents.Length,
				frameP50Ms = p50,
				frameP95Ms = p95,
				frameP99Ms = p99,
				frameMaxMs = max
			};
			for (int i = 0; i < deltas.Count; i++)
			{
				phaseReport.frameTimesMs.Add(deltas[i] * 1000f);
			}
			phaseReport.physicsSteps.AddRange(physicsSamples);
			double[] physicsTimes = new double[physicsSamples.Count];
			for (int i = 0; i < physicsSamples.Count; i++)
			{
				physicsTimes[i] = physicsSamples[i].totalMs;
			}
			Array.Sort(physicsTimes);
			phaseReport.physicsP50Ms = Percentile(physicsTimes, 0.50);
			phaseReport.physicsP95Ms = Percentile(physicsTimes, 0.95);
			phaseReport.physicsP99Ms = Percentile(physicsTimes, 0.99);
			phaseReport.physicsMaxMs = Percentile(physicsTimes, 1.00);
			CaptureState(sim, phaseReport);
			if (s_fixedClock)
			{
				CaptureFixedAnchorError(PartGameplayUtil.Em, phaseReport);
				if (targetPhysicsSteps > 0 && (phaseReport.completedPhysicsSteps != targetPhysicsSteps || physicsSamples.Count != targetPhysicsSteps))
					RejectRun(label + " did not execute and sample exactly " + targetPhysicsSteps + " ticks");
			}
			if (s_runReport != null)
			{
				s_runReport.phases.Add(phaseReport);
			}
			Line("  physics pipeline: samples=" + physicsSamples.Count + " p50/p95/p99/max="
				+ phaseReport.physicsP50Ms.ToString("F3", CultureInfo.InvariantCulture) + "/"
				+ phaseReport.physicsP95Ms.ToString("F3", CultureInfo.InvariantCulture) + "/"
				+ phaseReport.physicsP99Ms.ToString("F3", CultureInfo.InvariantCulture) + "/"
				+ phaseReport.physicsMaxMs.ToString("F3", CultureInfo.InvariantCulture) + "ms"
				+ " state=" + phaseReport.stateSha256
				+ " AnvilSteps=" + (AnvilStepSystem.StepCount - s_anvilStepStart));

			phaseReport.anvilStepDelta = AnvilStepSystem.StepCount - s_anvilStepStart;
			if (phaseReport.anvilStepDelta > 0)
			{
				b3Profile anvilProfile = AnvilStepSystem.LastProfile;
				Line("  anvil: step=" + F(anvilProfile.step) + " pairs=" + F(anvilProfile.pairs)
					+ " collide=" + F(anvilProfile.collide) + " solve=" + F(anvilProfile.solve)
					+ " prepare=" + F(anvilProfile.prepareConstraints) + " integratePos=" + F(anvilProfile.integratePositions)
					+ " splitIslands=" + F(anvilProfile.splitIslands) + " transforms=" + F(anvilProfile.transforms)
					+ " syncTo=" + F((float)AnvilStepSystem.LastSyncToMs)
					+ " syncFrom=" + F((float)AnvilStepSystem.LastSyncFromMs));
				AnvilWorld anvilWorld = AnvilStepSystem.Anvil;
				if (anvilWorld != null && anvilWorld.IsCreated)
				{
					b3Counters counters = anvilWorld.GetCounters();
					Line("  anvil counters: bodies=" + counters.bodyCount + " shapes=" + counters.shapeCount
						+ " contacts=" + counters.contactCount + " joints=" + counters.jointCount
						+ " islands=" + counters.islandCount);
				}
			}
		}

		internal static void BeginPhysicsStepTiming()
		{
			if (s_capturePhysics)
			{
				s_physicsStart = System.Diagnostics.Stopwatch.GetTimestamp();
				s_physicsAnvilStepStart = AnvilStepSystem.StepCount;
			}
		}

		internal static void CompletePhysicsStepTiming(EntityManager entityManager)
		{
			s_completedPhysicsSteps++;
			if (s_traceSteps > 0 && s_completedPhysicsSteps - s_traceFirstTick <= s_traceSteps)
				TracePhysicsStep(entityManager, s_completedPhysicsSteps - s_traceFirstTick);
			if (!s_capturePhysics || s_physicsSamples == null || s_physicsStart == 0
				|| (s_captureTargetSteps > 0 && s_physicsSamples.Count >= s_captureTargetSteps))
			{
				return;
			}
			if (s_fixedClock)
			{
				entityManager.CompleteDependencyBeforeRO<LocalTransform>();
				entityManager.CompleteDependencyBeforeRO<PhysicsVelocity>();
			}
			long end = System.Diagnostics.Stopwatch.GetTimestamp();
			PhysicsStepSample sample = new PhysicsStepSample
			{
				totalMs = (end - s_physicsStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency,
				anvilStepDelta = AnvilStepSystem.StepCount - s_physicsAnvilStepStart
			};
			if (sample.anvilStepDelta > 0)
			{
				sample.worldBuildWaitMs = AnvilStepSystem.LastWorldBuildWaitMs;
				sample.syncToMs = AnvilStepSystem.LastSyncToMs;
				sample.stepMs = AnvilStepSystem.LastStepMs;
				sample.syncFromMs = AnvilStepSystem.LastSyncFromMs;
				sample.treeMs = AnvilStepSystem.LastTreeMs;
				b3Profile profile = AnvilStepSystem.LastProfile;
				sample.nativeMs = profile.step;
				sample.nativeSolveMs = profile.solve;
				sample.nativePrepareMs = profile.prepareConstraints;
				sample.nativeWarmStartMs = profile.warmStart;
				sample.nativeSolveImpulseMs = profile.solveImpulses;
				sample.nativeRelaxMs = profile.relaxImpulses;
				sample.integratedSubsteps = AnvilStepSystem.Anvil.UsedIntegratedSubsteps;
			}
			s_physicsSamples.Add(sample);
			s_physicsStart = 0;
		}

		private static void TracePhysicsStep(EntityManager manager, int step)
		{
			using EntityQuery worlds = manager.CreateEntityQuery(ComponentType.ReadOnly<PhysicsWorldSingleton>());
			using EntityQuery contacts = manager.CreateEntityQuery(ComponentType.ReadOnly<AnvilContactsSingleton>());
			manager.CompleteDependencyBeforeRO<PhysicsWorldSingleton>();
			PhysicsWorld world = worlds.GetSingleton<PhysicsWorldSingleton>().PhysicsWorld;
			int fastest = -1;
			int fastestAngular = -1;
			float maxLinearSquared = -1f;
			float maxAngularSquared = -1f;
			int nonFiniteBodies = 0;
			for (int index = 0; index < world.NumDynamicBodies; index++)
			{
				MotionVelocity velocity = world.MotionVelocities[index];
				RigidTransform transform = world.MotionDatas[index].WorldFromMotion;
				if (!math.all(math.isfinite(velocity.LinearVelocity)) || !math.all(math.isfinite(velocity.AngularVelocity))
					|| !math.all(math.isfinite(transform.pos)) || !math.all(math.isfinite(transform.rot.value)))
				{
					nonFiniteBodies++;
					continue;
				}
				float linearSquared = math.lengthsq(velocity.LinearVelocity);
				float angularSquared = math.lengthsq(velocity.AngularVelocity);
				if (linearSquared > maxLinearSquared)
				{
					fastest = index;
					maxLinearSquared = linearSquared;
				}
				if (angularSquared > maxAngularSquared)
				{
					fastestAngular = index;
					maxAngularSquared = angularSquared;
				}
			}
			if (fastest < 0)
			{
				Line($"trace tick={step} joints={world.NumJoints} nonfinite={nonFiniteBodies}");
				return;
			}
			Entity fastestEntity = world.Bodies[fastest].Entity;
			Entity fastestAngularEntity = world.Bodies[fastestAngular].Entity;
			Line($"trace tick={step} joints={world.NumJoints} nonfinite={nonFiniteBodies} linear={F(math.sqrt(maxLinearSquared))} {TraceBody(manager, fastestEntity)} "
				+ $"angular={F(math.sqrt(maxAngularSquared))} {TraceBody(manager, fastestAngularEntity)}");
			if (!contacts.TryGetSingleton(out AnvilContactsSingleton contactEvents) || !contactEvents.Active) return;
			using var breaks = new NativeList<Entity>(Allocator.Temp);
			AnvilStepSystem.Anvil.CollectJointEvents(breaks);
			Line($"trace contacts={contactEvents.Count} breakEvents={breaks.Length}");
			for (int index = 0; index < math.min(4, breaks.Length); index++)
			{
				Entity joint = breaks[index];
				if (!manager.HasComponent<PhysicsConstrainedBodyPair>(joint)) continue;
				PhysicsConstrainedBodyPair pair = manager.GetComponentData<PhysicsConstrainedBodyPair>(joint);
				JointType type = manager.GetComponentData<PhysicsJoint>(joint).JointType;
				Line($"trace break {type} {TraceBody(manager, pair.EntityA)} -> {TraceBody(manager, pair.EntityB)}");
			}
			AnvilContactEvent largest = default;
			for (int index = 0; index < contactEvents.Count; index++)
			{
				AnvilContactEvent contact = contactEvents.Events[index];
				if (contact.Impulse > largest.Impulse) largest = contact;
			}
			if (largest.Impulse > 0f)
				Line($"trace contact impulse={F(largest.Impulse)} {TraceBody(manager, largest.EntityA)} -> {TraceBody(manager, largest.EntityB)}");
		}

		private static string TraceBody(EntityManager manager, Entity entity)
		{
			if (entity == Entity.Null || !manager.Exists(entity)) return "static";
			bool half = manager.HasComponent<HingePlateCollisionNetwork>(entity)
				&& manager.GetComponentData<HingePlateCollisionNetwork>(entity).IsHalf;
			PartTypeInfo type = manager.HasComponent<PartTypeValue>(entity) ? manager.GetComponentData<PartTypeValue>(entity).BaseValue : default;
			float3 position = manager.HasComponent<LocalTransform>(entity) ? manager.GetComponentData<LocalTransform>(entity).Position : default;
			return $"{entity.Index}/{manager.GetName(entity)}/{type.PartType}.{type.PartIndex}/half={half}@{position}";
		}

		private static double Percentile(double[] sorted, double percentile)
		{
			if (sorted == null || sorted.Length == 0)
			{
				return 0.0;
			}
			int index = (int)System.Math.Ceiling(sorted.Length * percentile) - 1;
			index = System.Math.Max(0, System.Math.Min(sorted.Length - 1, index));
			return sorted[index];
		}

		private static void CaptureState(PartSimulator sim, SavePhaseReport report)
		{
			EntityManager em = PartGameplayUtil.Em;
			NativeList<Entity> parts = sim.Unmanaged.Parts;
			double x = 0.0;
			double y = 0.0;
			double speed = 0.0;
			double angularSpeed = 0.0;
			float maxSpeed = 0f;
			float maxAngularSpeed = 0f;
			int counted = 0;
			using (MemoryStream stream = new MemoryStream(parts.Length * 48))
			using (BinaryWriter writer = new BinaryWriter(stream))
			{
				writer.Write(parts.Length);
				for (int i = 0; i < parts.Length; i++)
				{
					Entity entity = parts[i];
					bool exists = entity != Entity.Null && em.Exists(entity);
					writer.Write(exists);
					if (!exists)
					{
						continue;
					}
					bool hasTransform = em.HasComponent<LocalTransform>(entity);
					writer.Write(hasTransform);
					if (hasTransform)
					{
						LocalTransform transform = em.GetComponentData<LocalTransform>(entity);
						if (!math.all(math.isfinite(transform.Position)) || !math.all(math.isfinite(transform.Rotation.value)))
							report.nonFiniteParts++;
						writer.Write(transform.Position.x);
						writer.Write(transform.Position.y);
						writer.Write(transform.Position.z);
						writer.Write(transform.Rotation.value.x);
						writer.Write(transform.Rotation.value.y);
						writer.Write(transform.Rotation.value.z);
						writer.Write(transform.Rotation.value.w);
						writer.Write(transform.Scale);
						x += transform.Position.x;
						y += transform.Position.y;
						counted++;
					}
					bool hasVelocity = em.HasComponent<PhysicsVelocity>(entity);
					writer.Write(hasVelocity);
					if (hasVelocity)
					{
						PhysicsVelocity velocity = em.GetComponentData<PhysicsVelocity>(entity);
						if (!math.all(math.isfinite(velocity.Linear)) || !math.all(math.isfinite(velocity.Angular)))
							report.nonFiniteParts++;
						writer.Write(velocity.Linear.x);
						writer.Write(velocity.Linear.y);
						writer.Write(velocity.Linear.z);
						writer.Write(velocity.Angular.x);
						writer.Write(velocity.Angular.y);
						writer.Write(velocity.Angular.z);
						float linearMagnitude = math.length(velocity.Linear);
						float angularMagnitude = math.length(velocity.Angular);
						speed += linearMagnitude;
						angularSpeed += angularMagnitude;
						maxSpeed = math.max(maxSpeed, linearMagnitude);
						maxAngularSpeed = math.max(maxAngularSpeed, angularMagnitude);
					}
				}
				writer.Flush();
				using (SHA256 sha = SHA256.Create())
				{
					report.stateSha256 = BitConverter.ToString(sha.ComputeHash(stream.GetBuffer(), 0, (int)stream.Length))
						.Replace("-", string.Empty).ToLowerInvariant();
				}
			}
			report.centroidX = counted == 0 ? 0.0 : x / counted;
			report.centroidY = counted == 0 ? 0.0 : y / counted;
			report.meanSpeed = counted == 0 ? 0.0 : speed / counted;
			report.maxSpeed = maxSpeed;
			report.meanAngularSpeed = counted == 0 ? 0.0 : angularSpeed / counted;
			report.maxAngularSpeed = maxAngularSpeed;
		}

		private static void CaptureFixedAnchorError(EntityManager manager, SavePhaseReport report)
		{
			using EntityQuery query = manager.CreateEntityQuery(ComponentType.ReadOnly<PhysicsJoint>(), ComponentType.ReadOnly<PhysicsConstrainedBodyPair>());
			using NativeArray<PhysicsJoint> joints = query.ToComponentDataArray<PhysicsJoint>(Allocator.Temp);
			using NativeArray<PhysicsConstrainedBodyPair> pairs = query.ToComponentDataArray<PhysicsConstrainedBodyPair>(Allocator.Temp);
			double squaredErrorSum = 0.0;
			for (int index = 0; index < joints.Length; index++)
			{
				PhysicsJoint joint = joints[index];
				if (joint.JointType != JointType.Fixed) continue;
				PhysicsConstrainedBodyPair pair = pairs[index];
				float3 anchorA = WorldAnchor(manager, pair.EntityA, joint.BodyAFromJoint.Position);
				float3 anchorB = WorldAnchor(manager, pair.EntityB, joint.BodyBFromJoint.Position);
				double squaredError = math.distancesq(anchorA, anchorB);
				squaredErrorSum += squaredError;
				report.maxFixedAnchorError = System.Math.Max(report.maxFixedAnchorError, System.Math.Sqrt(squaredError));
				report.fixedJointCount++;
			}
			if (report.fixedJointCount > 0)
				report.rmsFixedAnchorError = System.Math.Sqrt(squaredErrorSum / report.fixedJointCount);
		}

		private static float3 WorldAnchor(EntityManager manager, Entity entity, float3 anchor)
		{
			if (!manager.HasComponent<LocalTransform>(entity)) return anchor;
			LocalTransform transform = manager.GetComponentData<LocalTransform>(entity);
			return transform.Position + math.rotate(transform.Rotation, anchor);
		}

		private static void RejectRun(string reason)
		{
			s_runReport.error = reason;
			Line("error: " + reason);
		}

		private static string HashFile(string path)
		{
			using (SHA256 sha = SHA256.Create())
			{
				return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", string.Empty).ToLowerInvariant();
			}
		}

		private static bool IsSafeLabel(string value)
		{
			for (int i = 0; i < value.Length; i++)
			{
				char character = value[i];
				if (!char.IsLetterOrDigit(character) && character != '_' && character != '-')
				{
					return false;
				}
			}
			return true;
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
				using EntityQuery query = em.CreateEntityQuery(ComponentType.ReadOnly<PhysicsJoint>());
				return query.CalculateEntityCount();
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
				File.WriteAllText(s_logPath, s_report.ToString(), new UTF8Encoding(false));
				if (!string.IsNullOrEmpty(s_jsonPath) && s_runReport != null)
				{
					File.WriteAllText(s_jsonPath, JsonUtility.ToJson(s_runReport, true), new UTF8Encoding(false));
				}
			}
			catch (Exception exception)
			{
				Debug.LogWarning("[SpinPerf] write log failed: " + exception.Message);
			}
		}

		#endregion
	}

	[UpdateInGroup(typeof(PhysicsSystemGroup), OrderFirst = true)]
	public partial class SpinPerfPhysicsStartSystem : SystemBase
	{
		protected override void OnUpdate()
		{
			SpinPerfProbe.BeginPhysicsStepTiming();
		}
	}

	[UpdateInGroup(typeof(PhysicsSystemGroup), OrderLast = true)]
	public partial class SpinPerfPhysicsEndSystem : SystemBase
	{
		protected override void OnUpdate()
		{
			SpinPerfProbe.CompletePhysicsStepTiming(EntityManager);
		}
	}
}
#endif
