#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Bpsim.Parts.Simulation;
using Bpsim.Physics.Anvil;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Collider = Unity.Physics.Collider;
using RaycastHit = Unity.Physics.RaycastHit;
using Unity.Physics.Systems;
using Unity.Transforms;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Bpsim.QA
{
    /// <summary>
    /// 三后端物理基准的 batchmode 入口：不依赖 Play Mode、不依赖 HTTP 桥。
    /// </summary>
    /// <remarks>
    /// <code>
    /// Unity.exe -batchmode -quit -projectPath &lt;proj&gt; ^
    ///   -executeMethod Bpsim.QA.AnvilBenchBatch.Run -logFile Logs/anvilbench-batch.log
    /// </code>
    /// 参数走环境变量 <c>ANVIL_BENCH_SPEC</c>（分号分隔），默认
    /// <c>bodies=2000;steps=240;warmup=60;backends=UnityPhysics,HavokPhysics,AnvilPhysics</c>。
    /// <para>
    /// 计时对象是 <see cref="PhysicsSystemGroup" /> 的一次 Update —— 即一个完整物理步
    /// （BuildPhysicsWorld → 物理推进 → ExportPhysicsWorld）。三个后端走完全相同的路径，
    /// 只换中间那个推进器，所以差值就是推进器本身的成本。
    /// 时间由本方法用 <see cref="World.SetTime" /> 手动推进，与渲染帧率、渲染负载无关。
    /// </para>
    /// </remarks>
    public static class AnvilBenchBatch
    {
        private const string DefaultSpec =
            "bodies=2000;steps=240;warmup=60;backends=UnityPhysics,HavokPhysics,AnvilPhysics";

        public static void Run()
        {
            try
            {
                RunInner();
            }
            catch (Exception e)
            {
                Debug.LogError("[AnvilBenchBatch] EXCEPTION " + e);
                WriteLog("EXCEPTION\n" + e);
            }
        }

        private static void RunInner()
        {
            string spec = Environment.GetEnvironmentVariable("ANVIL_BENCH_SPEC");
            if (string.IsNullOrEmpty(spec))
            {
                spec = DefaultSpec;
            }

            int bodies = ParseInt(spec, "bodies", 2000);
            int steps = ParseInt(spec, "steps", 240);
            int warmup = ParseInt(spec, "warmup", 60);
            bool stack = ParseInt(spec, "stack", 1) != 0;
            float timeStep = ParseFloat(spec, "timestep", 0.02f);
            bool syncTree = ParseInt(spec, "tree", 1) != 0;
            // weld=1：把相邻两体用 fixed joint 焊起来（复现 bpsim 的部件连接负载）
            bool weldPairs = ParseInt(spec, "weld", 0) != 0;
            float weldThreshold = ParseFloat(spec, "weldThreshold", 50f);
            // contacts=1：每步采集 box3d 接触（供 TNT / 电路 / 巨型结构消费）；0 则关闭以看清纯物理开销
            bool collectContacts = ParseInt(spec, "contacts", 0) != 0;
            // rays：每步额外做多少次射线投射（走各自后端的 broadphase，用于对比查询开销）
            int raysPerStep = ParseInt(spec, "rays", 0);
            // workers：box3d 求解线程数（0=用默认 1）
            int workers = ParseInt(spec, "workers", 0);
            // sleep：box3d 是否允许休眠（-1=用默认 true，0/1=显式指定）
            int sleepSpec = ParseInt(spec, "sleep", -1);
            string[] backends = ParseList(spec, "backends", "UnityPhysics,HavokPhysics,AnvilPhysics");
            AnvilStepSystem.SyncBroadphaseTree = syncTree;
            Bpsim.Physics.Anvil.AnvilContactEventSystem.Collecting = collectContacts;
            AnvilStepSystem.WorkerCount = workers;
            AnvilStepSystem.EnableSleep = sleepSpec < 0 ? (bool?)null : sleepSpec != 0;

            // 本工程 Editor/批处理下 Burst 被强制关闭，managed 执行的 job safety 会误报越界，
            // 与 Assets/Scripts/BpsimCompat/BpsimPhysicsCompat.cs 保持一致。
            Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobDebuggerEnabled = false;

            StringBuilder report = new StringBuilder();
            report.AppendLine("=== Anvil bench (batch) ===");
            report.AppendLine("spec: " + spec);
            report.AppendLine("bodies=" + bodies + " steps=" + steps + " warmup=" + warmup
                + " timestep=" + timeStep.ToString("F4", CultureInfo.InvariantCulture)
                + " stack=" + (stack ? 1 : 0) + " syncTree=" + (syncTree ? 1 : 0)
                + " weld=" + (weldPairs ? 1 : 0) + " weldThreshold=" + weldThreshold.ToString("F2", CultureInfo.InvariantCulture)
                + " contacts=" + (collectContacts ? 1 : 0) + " workers=" + workers
                + " sleep=" + (sleepSpec < 0 ? "default" : (sleepSpec != 0 ? "1" : "0")));
            report.AppendLine("capabilities: " + DescribeCapabilities());
            report.AppendLine("note: Editor 下本工程 [BurstCompile] 全部 managed 回退，"
                + "Unity.Physics / Havok 不是 release 原生性能；Anvil 走 box3d 原生 DLL。");

            World world = null;
            try
            {
                world = DefaultWorldInitialization.Initialize("AnvilBenchWorld", false);
            }
            catch (Exception e)
            {
                report.AppendLine("world init failed: " + e);
                WriteLog(report.ToString());
                return;
            }

            EntityManager entityManager = world.EntityManager;
            PhysicsSystemGroup physicsGroup = world.GetExistingSystemManaged<PhysicsSystemGroup>();
            if (physicsGroup == null)
            {
                report.AppendLine("PhysicsSystemGroup missing");
                WriteLog(report.ToString());
                return;
            }

            float3 gravity = new float3(0f, -9.81f, 0f);
            Entity stepEntity = entityManager.CreateEntity();
            PhysicsStep physicsStep = PhysicsStep.Default;
            physicsStep.Gravity = gravity;
            physicsStep.SolverIterationCount = 8;
            physicsStep.SimulationType = SimulationType.UnityPhysics;
            entityManager.AddComponentData(stepEntity, physicsStep);

            bool first = true;
            foreach (string raw in backends)
            {
                string name = raw.Trim();
                if (name.Length == 0)
                {
                    continue;
                }
                SimulationType type;
                if (!TryParseSimulationType(name, out type))
                {
                    report.AppendLine("[skip] unknown backend '" + name + "'");
                    continue;
                }
                if (!first)
                {
                    report.AppendLine();
                }
                first = false;
                RunBackend(report, world, entityManager, physicsGroup, stepEntity, type, name,
                    bodies, steps, warmup, timeStep, stack, gravity, weldPairs, weldThreshold, raysPerStep);
            }

            report.AppendLine();
            report.AppendLine("=== done ===");
            WriteLog(report.ToString());

            try
            {
                Debug.Log("[AnvilBenchBatch]\n" + report);
            }
            catch
            {
                // ignore
            }
        }

        private static void RunBackend(StringBuilder report, World world, EntityManager entityManager,
            PhysicsSystemGroup physicsGroup, Entity stepEntity, SimulationType type, string name,
            int bodies, int steps, int warmup, float timeStep, bool stack, float3 gravity,
            bool weldPairs, float weldThreshold, int raysPerStep)
        {
            PhysicsStep step = entityManager.GetComponentData<PhysicsStep>(stepEntity);
            step.SimulationType = type;
            step.Gravity = gravity;
            entityManager.SetComponentData(stepEntity, step);

            NativeList<Entity> spawned = new NativeList<Entity>(bodies + 8, Allocator.Persistent);
            NativeList<Entity> weldA = new NativeList<Entity>(math.max(1, bodies / 2), Allocator.Persistent);
            NativeList<Entity> weldB = new NativeList<Entity>(math.max(1, bodies / 2), Allocator.Persistent);
            NativeList<float> weldRest = new NativeList<float>(math.max(1, bodies / 2), Allocator.Persistent);
            try
            {
                CreateScene(entityManager, spawned, bodies, stack, gravity, weldPairs, weldThreshold,
                    out int jointCount, weldA, weldB, weldRest);

                int total = warmup + steps;
                double[] all = new double[total];
                double[] syncTo = new double[total];
                double[] nativeStep = new double[total];
                double[] syncFrom = new double[total];
                double[] tree = new double[total];
                double[] rayMicros = new double[total];
                NativeList<RaycastHit> rayHits = new NativeList<RaycastHit>(64, Allocator.Temp);
                int rayHitsTotal = 0;
                try
                {
                double elapsed = 0.0;
                for (int i = 0; i < total; i++)
                {
                    elapsed += timeStep;
                    world.SetTime(new TimeData(elapsed, timeStep));
                    long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    physicsGroup.Update();
                    long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
                    all[i] = (t1 - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    if (type == SimulationType.AnvilPhysics)
                    {
                        syncTo[i] = AnvilStepSystem.LastSyncToMs;
                        nativeStep[i] = AnvilStepSystem.LastStepMs;
                        syncFrom[i] = AnvilStepSystem.LastSyncFromMs;
                        tree[i] = AnvilStepSystem.LastTreeMs;
                    }

                    // 可选的查询负载：从地面上方往下打射线，统计每步命中数。
                    // 放在 physicsGroup.Update() 之外，不污染 step 计时。
                    if (raysPerStep > 0)
                    {
                        long r0 = System.Diagnostics.Stopwatch.GetTimestamp();
                        rayHitsTotal += CastRays(type, entityManager, rayHits, raysPerStep);
                        long r1 = System.Diagnostics.Stopwatch.GetTimestamp();
                        rayMicros[i] = (r1 - r0) * 1e6 / System.Diagnostics.Stopwatch.Frequency / raysPerStep;
                    }
                }
                if (raysPerStep > 0)
                {
                    report.AppendLine("  rays: perStep=" + raysPerStep + " totalHits=" + rayHitsTotal
                        + " us/ray median=" + Median(rayMicros, warmup));
                }
                }
                finally
                {
                    if (rayHits.IsCreated) rayHits.Dispose();
                }

                List<double> samples = new List<double>(steps);
                for (int i = warmup; i < total; i++)
                {
                    samples.Add(all[i]);
                }

                report.AppendLine("--- backend " + name + " (" + type + ") ---");
                report.AppendLine("  scene: dynamic=" + bodies + " ground=1 joints=" + jointCount
                    + " weldThreshold=" + weldThreshold.ToString("F2", CultureInfo.InvariantCulture));
                report.AppendLine(Summarize(samples));

                if (type == SimulationType.AnvilPhysics)
                {
                    b3Profile p = AnvilStepSystem.LastProfile;
                    report.AppendLine("  box3d profile(ms): collide=" + F(p.collide) + " solve=" + F(p.solve)
                        + " constraints=" + F(p.constraints) + " integratePositions=" + F(p.integratePositions)
                        + " transforms=" + F(p.transforms) + " step=" + F(p.step));
                    report.AppendLine("  anvil breakdown(ms): syncTo=" + F((float)AnvilStepSystem.LastSyncToMs)
                        + " nativeStep=" + F((float)AnvilStepSystem.LastStepMs)
                        + " syncFrom=" + F((float)AnvilStepSystem.LastSyncFromMs)
                        + " tree=" + F((float)AnvilStepSystem.LastTreeMs));
                    report.AppendLine("  anvil breakdown median(ms): " + Median(syncTo, warmup) + " / "
                        + Median(nativeStep, warmup) + " / " + Median(syncFrom, warmup) + " / "
                        + Median(tree, warmup));
                    AnvilWorld anvil = AnvilStepSystem.Anvil;
                    if (anvil != null && anvil.IsCreated)
                    {
                        b3Counters c = anvil.GetCounters();
                        report.AppendLine("  box3d counters: bodies=" + c.bodyCount + " shapes=" + c.shapeCount
                            + " contacts=" + c.contactCount + " joints=" + c.jointCount
                            + " islands=" + c.islandCount + " bytes=" + c.byteCount);
                    }
                    report.AppendLine("  anvil steps=" + AnvilStepSystem.StepCount);
                    using (NativeList<Entity> jointEvents = new NativeList<Entity>(64, Allocator.Temp))
                    {
                        int n = AnvilStepSystem.CollectJointEvents(jointEvents);
                        report.AppendLine("  anvil joint events(last step): " + n);
                    }
                }

                if (jointCount > 0)
                {
                    float maxDrift = 0f;
                    float sumDrift = 0f;
                    for (int i = 0; i < weldA.Length; i++)
                    {
                        if (!entityManager.Exists(weldA[i]) || !entityManager.Exists(weldB[i])) continue;
                        LocalTransform la = entityManager.GetComponentData<LocalTransform>(weldA[i]);
                        LocalTransform lb = entityManager.GetComponentData<LocalTransform>(weldB[i]);
                        float drift = math.abs(math.distance(la.Position, lb.Position) - weldRest[i]);
                        sumDrift += drift;
                        maxDrift = math.max(maxDrift, drift);
                    }
                    report.AppendLine("  weld drift: avg=" + (weldA.Length > 0 ? (sumDrift / weldA.Length) : 0f).ToString("F5", CultureInfo.InvariantCulture)
                        + " max=" + maxDrift.ToString("F5", CultureInfo.InvariantCulture) + " pairs=" + weldA.Length);
                }

                using (EntityQuery q = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PhysicsWorldSingleton>()))
                {
                    if (q.CalculateEntityCount() > 0)
                    {
                        PhysicsWorld physicsWorld = q.GetSingleton<PhysicsWorldSingleton>().PhysicsWorld;
                        report.AppendLine("  physics world: dynamic=" + physicsWorld.NumDynamicBodies
                            + " static=" + physicsWorld.NumStaticBodies + " joints=" + physicsWorld.NumJoints);

                        // 正确性校验：堆塔跑完之后，最低一层应该还在地面上、并且没有掉穿/穿透地面。
                        int belowGround = 0;
                        double sumY = 0.0;
                        NativeArray<MotionData> motions = physicsWorld.MotionDatas;
                        NativeArray<RigidBody> dynamicBodies = physicsWorld.DynamicBodies;
                        for (int i = 0; i < dynamicBodies.Length; i++)
                        {
                            RigidTransform body = math.mul(motions[i].WorldFromMotion,
                                math.inverse(motions[i].BodyFromMotion));
                            sumY += body.pos.y;
                            if (body.pos.y < -1f)
                            {
                                belowGround++;
                            }
                        }
                        if (dynamicBodies.Length > 0)
                        {
                            report.AppendLine("  sanity: avgY=" + (sumY / dynamicBodies.Length)
                                .ToString("F4", CultureInfo.InvariantCulture)
                                + " belowGround=" + belowGround + "/" + dynamicBodies.Length);
                        }
                    }
                }
            }
            finally
            {
                if (spawned.IsCreated)
                {
                    for (int i = 0; i < spawned.Length; i++)
                    {
                        if (entityManager.Exists(spawned[i]))
                        {
                            entityManager.DestroyEntity(spawned[i]);
                        }
                    }
                    spawned.Dispose();
                }
                if (weldA.IsCreated) weldA.Dispose();
                if (weldB.IsCreated) weldB.Dispose();
                if (weldRest.IsCreated) weldRest.Dispose();
            }
        }

        private static void CreateScene(EntityManager entityManager, NativeList<Entity> spawned, int bodies,
            bool stack, float3 gravity, bool weldPairs, float weldThreshold,
            out int jointCount, NativeList<Entity> weldA, NativeList<Entity> weldB,
            NativeList<float> weldRest)
        {
            jointCount = 0;
            // 地面：静态 Box，顶面 y = 0。
            Entity ground = entityManager.CreateEntity();
            float3 groundCenter = new float3(0f, -0.5f, 0f);
            entityManager.AddComponentData(ground, LocalTransform.FromPosition(groundCenter));
            entityManager.AddComponentData(ground, new LocalToWorld { Value = float4x4.Translate(groundCenter) });
            entityManager.AddComponentData(ground, new PhysicsCollider
            {
                Value = PartPhysics.CreateBox(float3.zero, new float3(200f, 1f, 200f))
            });
            entityManager.AddSharedComponent(ground, new PhysicsWorldIndex(0));
            spawned.Add(ground);

            float3 half = new float3(0.25f, 0.25f, 0.25f);
            int columns = math.max(1, (int)math.sqrt(bodies));
            int rows = (bodies + columns - 1) / columns;
            const float spacing = 0.6f;
            int perLayer = columns * math.max(1, columns);
            Entity previousBody = Entity.Null;

            for (int i = 0; i < bodies; i++)
            {
                int column = i % columns;
                int row = i / columns;
                float3 position;
                if (stack)
                {
                    int layer = i / perLayer;
                    position = new float3(
                        (column - columns * 0.5f + 0.5f) * spacing,
                        0.3f + layer * 0.55f,
                        ((row % columns) - columns * 0.5f + 0.5f) * spacing);
                }
                else
                {
                    position = new float3((column - columns * 0.5f + 0.5f) * spacing, 0.3f,
                        (row - rows * 0.5f + 0.5f) * spacing);
                }

                Entity e = entityManager.CreateEntity();
                quaternion rotation = quaternion.identity;
                entityManager.AddComponentData(e, LocalTransform.FromPositionRotationScale(position, rotation, 0.5f));
                entityManager.AddComponentData(e, new LocalToWorld { Value = float4x4.TRS(position, rotation, 0.5f) });
                BlobAssetReference<Collider> box = PartPhysics.CreateBox(float3.zero, half * 2f);
                entityManager.AddComponentData(e, new PhysicsCollider { Value = box });
                entityManager.AddComponentData(e, PhysicsMass.CreateDynamic(box.Value.MassProperties, 1f));
                entityManager.AddComponentData(e, new PhysicsVelocity());
                entityManager.AddComponentData(e, new PhysicsDamping { Linear = 0.05f, Angular = 0.05f });
                entityManager.AddComponentData(e, new PhysicsGravityFactor { Value = 1f });
                entityManager.AddSharedComponent(e, new PhysicsWorldIndex(0));
                spawned.Add(e);

                // 关节场景：把相邻两体用 fixed joint 焊起来（模拟 bpsim 的部件连接）。
                if (weldPairs && previousBody != Entity.Null && (i & 1) == 1)
                {
                    Entity a = previousBody;
                    Entity b = e;
                    LocalTransform la = entityManager.GetComponentData<LocalTransform>(a);
                    LocalTransform lb = entityManager.GetComponentData<LocalTransform>(b);
                    RigidTransform ta = new RigidTransform(la.Rotation, la.Position);
                    RigidTransform tb = new RigidTransform(lb.Rotation, lb.Position);
                    float3 mid = (ta.pos + tb.pos) * 0.5f;
                    PhysicsJoint joint = PhysicsJoint.CreateFixed(
                        new BodyFrame(math.mul(math.inverse(ta), new RigidTransform(quaternion.identity, mid))),
                        new BodyFrame(math.mul(math.inverse(tb), new RigidTransform(quaternion.identity, mid))));
                    joint.SetImpulseEventThresholdAllConstraints(weldThreshold, float.PositiveInfinity);

                    Entity j = entityManager.CreateEntity();
                    entityManager.SetName(j, "JointEntity");
                    entityManager.AddSharedComponent(j, new PhysicsWorldIndex(0));
                    entityManager.AddComponentData(j, joint);
                    entityManager.AddComponentData(j, new PhysicsConstrainedBodyPair(a, b, false));
                    spawned.Add(j);
                    weldA.Add(a);
                    weldB.Add(b);
                    weldRest.Add(math.distance(ta.pos, tb.pos));
                    jointCount++;
                }

                previousBody = e;
            }
        }

        // ---- 射线查询负载 ----

        /// <summary>
        /// 从场景上方均匀打 <paramref name="count" /> 条竖直向下的射线，返回命中总数。
        /// 走的是各后端自己的 broadphase：UnityPhysics/Havok 用
        /// <c>CollisionWorld.CastRayClosest</c>，Anvil 用 box3d 的 <c>b3World_CastRayClosest</c>
        /// （即 <see cref="AnvilWorld.CastRay" />，与 PartBehaviorsGuidance 等消费点的目标一致）。
        /// </summary>
        private static int CastRays(SimulationType type, EntityManager entityManager, NativeList<RaycastHit> hits, int count)
        {
            int found = 0;
            using (EntityQuery q = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PhysicsWorldSingleton>()))
            {
                if (q.CalculateEntityCount() == 0)
                {
                    return 0;
                }
                CollisionWorld collisionWorld = q.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
                AnvilWorld anvil = AnvilStepSystem.Anvil;
                int side = (int)math.ceil(math.sqrt(count));
                float spacing = 24f / math.max(1, side);
                for (int i = 0; i < count; i++)
                {
                    float x = (i % side - side * 0.5f) * spacing;
                    float z = (i / side - side * 0.5f) * spacing;
                    float3 from = new float3(x, 12f, z);
                    float3 to = new float3(x, -4f, z);
                    if (type == SimulationType.AnvilPhysics)
                    {
                        if (anvil != null && anvil.IsCreated
                            && anvil.CastRay(from, to - from, 1f, out _, out _, out _))
                        {
                            found++;
                        }
                    }
                    else
                    {
                        RaycastInput input = new RaycastInput { Start = from, End = to, Filter = CollisionFilter.Default };
                        if (collisionWorld.CastRay(input, out RaycastHit hit))
                        {
                            found++;
                        }
                    }
                }
            }
            hits.Clear();
            return found;
        }

        // ---- 统计 ----

        private static string Median(double[] values, int skip)
        {
            if (values == null || values.Length <= skip)
            {
                return "n/a";
            }
            double[] copy = new double[values.Length - skip];
            Array.Copy(values, skip, copy, 0, copy.Length);
            Array.Sort(copy);
            return Percentile(copy, 0.5).ToString("F4", CultureInfo.InvariantCulture);
        }

        private static string Summarize(List<double> samples)
        {
            if (samples == null || samples.Count == 0)
            {
                return "  no samples";
            }
            double[] sorted = samples.ToArray();
            Array.Sort(sorted);
            double sum = 0.0;
            for (int i = 0; i < sorted.Length; i++)
            {
                sum += sorted[i];
            }
            double mean = sum / sorted.Length;
            return "  step ms: median=" + Percentile(sorted, 0.5).ToString("F4", CultureInfo.InvariantCulture)
                + " mean=" + mean.ToString("F4", CultureInfo.InvariantCulture)
                + " p95=" + Percentile(sorted, 0.95).ToString("F4", CultureInfo.InvariantCulture)
                + " p99=" + Percentile(sorted, 0.99).ToString("F4", CultureInfo.InvariantCulture)
                + " min=" + sorted[0].ToString("F4", CultureInfo.InvariantCulture)
                + " max=" + sorted[sorted.Length - 1].ToString("F4", CultureInfo.InvariantCulture)
                + " n=" + sorted.Length;
        }

        private static double Percentile(double[] sorted, double p)
        {
            if (sorted.Length == 0)
            {
                return 0.0;
            }
            double idx = p * (sorted.Length - 1);
            int lo = (int)math.floor(idx);
            int hi = (int)math.ceil(idx);
            if (lo == hi)
            {
                return sorted[lo];
            }
            double t = idx - lo;
            return sorted[lo] * (1.0 - t) + sorted[hi] * t;
        }

        private static string F(float v)
        {
            return v.ToString("F4", CultureInfo.InvariantCulture);
        }

        private static string DescribeCapabilities()
        {
            try
            {
                int caps = AnvilNative.anvil_Capabilities();
                return "box3d native ok, doublePrecision=" + ((caps & AnvilNative.CapabilityDoublePrecision) != 0)
                    + " avx2=" + ((caps & AnvilNative.CapabilityAvx2) != 0) + " (raw=" + caps + ")";
            }
            catch (Exception e)
            {
                return "box3d native NOT loaded: " + e.GetType().Name + ": " + e.Message;
            }
        }

        // ---- spec 解析 ----

        private static bool TryParseSimulationType(string name, out SimulationType type)
        {
            try
            {
                type = (SimulationType)Enum.Parse(typeof(SimulationType), name, true);
                return true;
            }
            catch
            {
                type = SimulationType.UnityPhysics;
                return false;
            }
        }

        private static int ParseInt(string spec, string key, int fallback)
        {
            return (int)ParseFloat(spec, key, fallback);
        }

        private static float ParseFloat(string spec, string key, float fallback)
        {
            foreach (string part in spec.Split(';'))
            {
                string token = part.Trim();
                int eq = token.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }
                if (token.Substring(0, eq).Trim() != key)
                {
                    continue;
                }
                float parsed;
                if (float.TryParse(token.Substring(eq + 1).Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out parsed))
                {
                    return parsed;
                }
            }
            return fallback;
        }

        private static string[] ParseList(string spec, string key, string fallback)
        {
            foreach (string part in spec.Split(';'))
            {
                string token = part.Trim();
                int eq = token.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }
                if (token.Substring(0, eq).Trim() != key)
                {
                    continue;
                }
                return token.Substring(eq + 1).Split(',');
            }
            return fallback.Split(',');
        }

        // ---- 输出 ----

        private static void WriteLog(string text)
        {
            try
            {
                const string dir = "Logs";
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(Path.Combine(dir, "anvilbench-batch.log"), text);
            }
            catch
            {
                // ignore
            }
        }
    }
}
#endif
