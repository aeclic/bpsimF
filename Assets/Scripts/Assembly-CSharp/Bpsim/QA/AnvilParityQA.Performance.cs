#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using Bpsim.Physics.Anvil;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Math = System.Math;

namespace Bpsim.QA
{
    public static partial class AnvilParityQA
    {
        const int PerformanceWarmup = 64;
        const int PerformanceSamples = 256;

        [Serializable]
        public struct PerformanceSample
        {
            public double totalMs, prepareMs, syncMs, stepMs, readMs, eventsMs, nativeMs;
            public long allocatedBytes;
            public int contacts;
        }

        [Serializable]
        public sealed class TimingDistribution
        {
            public double mean, p50, p95, p99, max;
            public int over16ms, over33ms;
        }

        [Serializable]
        public sealed class PerformanceCase
        {
            public string backend, scenario, trajectorySha256;
            public int bodies, joints, iterations, warmup, sampleCount;
            public bool finiteThresholds, sleeping;
            public double timeStep, meanAllocatedBytes;
            public TimingDistribution total, sync, step, read, events, native, interopAndSampling;
            public List<PerformanceSample> samples = new List<PerformanceSample>();
        }

        [Serializable]
        public sealed class PerformanceReport
        {
            public string label, runId, utc, unity, fingerprint, benchmarkSha256, nativeSha256, havokSha256;
            public int schema = 1;
            public List<PerformanceCase> cases = new List<PerformanceCase>();
            public List<string> errors = new List<string>();
        }

        static double Milliseconds(long a, long b) => (b - a) * 1000.0 / Stopwatch.Frequency;

        static TimingDistribution Distribution(PerformanceCase result, Func<PerformanceSample, double> value)
        {
            var numbers = new double[result.samples.Count];
            var output = new TimingDistribution();
            for (int i = 0; i < numbers.Length; ++i)
            {
                numbers[i] = value(result.samples[i]);
                output.mean += numbers[i];
                if (numbers[i] > 1000.0 / 60) ++output.over16ms;
                if (numbers[i] > 1000.0 / 30) ++output.over33ms;
            }
            Array.Sort(numbers);
            output.mean /= numbers.Length;
            output.p50 = numbers[(int)Math.Ceiling(numbers.Length * .50) - 1];
            output.p95 = numbers[(int)Math.Ceiling(numbers.Length * .95) - 1];
            output.p99 = numbers[(int)Math.Ceiling(numbers.Length * .99) - 1];
            output.max = numbers[numbers.Length - 1];
            return output;
        }

        static PerformanceCase PerformanceWorkload(SimulationType backend, string scenario)
        {
            bool contacts = scenario == "contact-stack-512-q8";
            bool flight = scenario == "free-flight-512-q8";
            bool finite = !scenario.Contains("unlimited");
            int count = contacts || flight ? 512 : 1024;
            int iterations = scenario.EndsWith("q4") ? 4 : 8;
            const int groupSize = 32;
            int joints = contacts || flight ? 0 : count / groupSize * (groupSize - 1);
            var output = new PerformanceCase { backend = backend.ToString(), scenario = scenario,
                bodies = count, joints = joints, iterations = iterations, warmup = PerformanceWarmup,
                sampleCount = PerformanceSamples, finiteThresholds = finite, sleeping = false, timeStep = .02 };
            using (var fixture = new Fixture(backend, count, joints, contacts ? 2 : 1))
            using (var events = new NativeList<AnvilContactEvent>(65536, Allocator.Persistent))
            using (var digest = SHA256.Create())
            {
                fixture.SolverIterations = iterations;
                fixture.SubSteps = iterations;
                fixture.Gravity = contacts ? new float3(0, -9.81f, 0) : float3.zero;
                var shape = fixture.Box(new float3(.5f));
                for (int i = 0; i < count; ++i)
                {
                    float3 position = contacts ? new float3((i % 16 - 8) * .51f, .25f + i / 16 * .51f, 0)
                        : new float3(i % 32 * .7f, 20 + i / 32 * 1.5f, 0);
                    fixture.Body(i, position, shape: shape);
                    if (!contacts) fixture.LinearVelocity(i, new float3(2, 0, 0));
                    if (flight) fixture.AngularVelocity(i, new float3(0, 0, .3f));
                    var motion = fixture.Motion(i);
                    motion.LinearDamping = .1f;
                    motion.AngularDamping = .2f;
                    fixture.Motion(i, motion);
                }
                if (contacts) fixture.Static(0, new float3(0, -.5f, 0), fixture.Box(new float3(100, 1, 4)));
                for (int i = 0, index = 0; i < count && joints > 0; ++i)
                    if (i % groupSize != groupSize - 1)
                    {
                        float3 threshold = new float3(finite ? 1000000f : float.PositiveInfinity);
                        fixture.Weld(index++, i, i + 1, threshold, threshold);
                    }
                var writableEvents = events;
                for (int i = 0; i < PerformanceWarmup; ++i) fixture.PerformanceStep(ref writableEvents);
                var state = new float[count * 13];
                var bytes = new byte[state.Length * sizeof(float)];
                GC.Collect();
                GC.WaitForPendingFinalizers();
                for (int frame = 0; frame < PerformanceSamples; ++frame)
                {
                    var sample = fixture.PerformanceStep(ref writableEvents);
                    output.samples.Add(sample);
                    output.meanAllocatedBytes += sample.allocatedBytes;
                    // Record the entire state trajectory outside the timed/allocated window.
                    // Reused buffers keep instrumentation from injecting per-body GC pressure.
                    var motions = fixture.World.MotionDatas;
                    var velocities = fixture.World.MotionVelocities;
                    for (int i = 0; i < count; ++i)
                    {
                        var transform = motions[i].WorldFromMotion;
                        var velocity = velocities[i];
                        if (!math.all(math.isfinite(transform.pos)) || !math.all(math.isfinite(transform.rot.value))
                            || !math.all(math.isfinite(velocity.LinearVelocity)) || !math.all(math.isfinite(velocity.AngularVelocity)))
                            throw new InvalidOperationException("Non-finite benchmark body state");
                        int offset = i * 13;
                        state[offset] = transform.pos.x; state[offset + 1] = transform.pos.y; state[offset + 2] = transform.pos.z;
                        state[offset + 3] = transform.rot.value.x; state[offset + 4] = transform.rot.value.y;
                        state[offset + 5] = transform.rot.value.z; state[offset + 6] = transform.rot.value.w;
                        state[offset + 7] = velocity.LinearVelocity.x; state[offset + 8] = velocity.LinearVelocity.y;
                        state[offset + 9] = velocity.LinearVelocity.z; state[offset + 10] = velocity.AngularVelocity.x;
                        state[offset + 11] = velocity.AngularVelocity.y; state[offset + 12] = velocity.AngularVelocity.z;
                    }
                    Buffer.BlockCopy(state, 0, bytes, 0, bytes.Length);
                    digest.TransformBlock(bytes, 0, bytes.Length, bytes, 0);
                }
                digest.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                output.trajectorySha256 = BitConverter.ToString(digest.Hash).Replace("-", "").ToLowerInvariant();
            }
            output.meanAllocatedBytes /= PerformanceSamples;
            output.total = Distribution(output, s => s.totalMs);
            output.sync = Distribution(output, s => s.syncMs);
            output.step = Distribution(output, s => s.stepMs);
            output.read = Distribution(output, s => s.readMs);
            output.events = Distribution(output, s => s.eventsMs);
            output.native = Distribution(output, s => s.nativeMs);
            output.interopAndSampling = Distribution(output, s => s.stepMs - s.nativeMs);
            return output;
        }

        /// <summary>Private fixed-input CPU benchmark. Run the batch runner twice independently.
        /// Reports raw timings, allocations and full trajectory hashes. No scenes or default ECS world.
        /// The timing oracle excludes gameplay, rendering/GPU and production BuildPhysicsWorld.</summary>
        public static string RunPerformance(string label)
        {
            if (string.IsNullOrEmpty(label) || label != Path.GetFileName(label)
                || label.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("Supply a simple report label", nameof(label));
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var report = new PerformanceReport { label = label, runId = Guid.NewGuid().ToString("N"),
                utc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion, fingerprint = Fingerprint(),
                benchmarkSha256 = FileHash(Path.Combine(root, "Assets/Scripts/Assembly-CSharp/Bpsim/QA/AnvilParityQA.Performance.cs")),
                nativeSha256 = FileHash(Path.Combine(root, "Assets/Plugins/x86_64/" + AnvilNative.Library + ".dll")),
                havokSha256 = FileHash(Path.Combine(root, "Assets/Plugins/x86_64/HavokNative.dll")) };
            foreach (SimulationType backend in new[] { SimulationType.UnityPhysics, SimulationType.HavokPhysics, SimulationType.AnvilPhysics })
                foreach (string scenario in new[] { "free-flight-512-q8", "welded-1024-q4", "welded-1024-q8",
                    "welded-unlimited-1024-q8", "contact-stack-512-q8" })
                {
                    try
                    {
                        var result = PerformanceWorkload(backend, scenario);
                        report.cases.Add(result);
                        Debug.Log("[AnvilPerformance] " + backend + " " + scenario + " p95=" + result.total.p95.ToString("F3")
                            + " ms native=" + result.native.p95.ToString("F3") + " ms allocation=" + result.meanAllocatedBytes);
                    }
                    catch (Exception exception) { report.errors.Add(backend + " " + scenario + ": " + exception); }
                }
            string path = Path.Combine(root, "Logs/anvil-performance-" + label + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(report, true), new System.Text.UTF8Encoding(false));
            return "cases=" + report.cases.Count + " errors=" + report.errors.Count + " report=" + path;
        }

        public static void RunPerformanceBatch()
        {
            string label = "batch";
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; ++i)
                if (args[i] == "-anvil-parity-label") label = args[i + 1];
            Debug.Log("[AnvilPerformance] " + RunPerformance(label));
        }

        sealed partial class Fixture
        {
            public PerformanceSample PerformanceStep(ref NativeList<AnvilContactEvent> contacts)
            {
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                var bodies = World.Bodies;
                for (int i = 0; i < World.NumDynamicBodies; ++i)
                {
                    var body = bodies[i];
                    body.WorldFromBody = math.mul(Motion(i).WorldFromMotion, math.inverse(Motion(i).BodyFromMotion));
                    bodies[i] = body;
                }
                World.UpdateIndexMaps();
                long prepared = Stopwatch.GetTimestamp(), synced, stepped, read;
                b3Profile profile = default;
                int contactCount = 0;
                if (m_Native != null)
                {
                    m_Native.SetGravity(Gravity);
                    m_Native.SyncToNative(ref World);
                    m_Native.SyncJointsToNative(ref World, TimeStep);
                    synced = Stopwatch.GetTimestamp();
                    profile = m_Native.Step(TimeStep);
                    stepped = Stopwatch.GetTimestamp();
                    m_Native.SyncFromNative(ref World);
                    read = Stopwatch.GetTimestamp();
                    m_Native.CollectJointEvents(m_Events);
                    contactCount = m_Native.CollectContactEvents(contacts, 65536);
                }
                else
                {
                    World.CollisionWorld.BuildBroadphase(ref World, TimeStep, Gravity);
                    synced = Stopwatch.GetTimestamp();
                    var input = new SimulationStepInput { World = World, TimeStep = TimeStep, Gravity = Gravity,
                        NumSolverIterations = SolverIterations, HaveStaticBodiesChanged = m_StaticChanged.AsReadOnly(),
                        SynchronizeCollisionWorld = false };
                    m_Reference.ScheduleStepJobs(input, default, false);
                    m_Reference.FinalJobHandle.Complete();
                    stepped = Stopwatch.GetTimestamp();
                    read = stepped;
                }
                long end = Stopwatch.GetTimestamp();
                return new PerformanceSample { totalMs = Milliseconds(start, end), prepareMs = Milliseconds(start, prepared),
                    syncMs = Milliseconds(prepared, synced), stepMs = Milliseconds(synced, stepped),
                    readMs = Milliseconds(stepped, read), eventsMs = Milliseconds(read, end), nativeMs = profile.step,
                    allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated, contacts = contactCount };
            }
        }
    }
}
#endif
