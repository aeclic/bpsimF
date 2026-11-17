#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using DshBridge;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Bpsim.Perf
{
    public static class EditorProfilerQA
    {
        static readonly ProfilerMarker ProbeMarker = new ProfilerMarker("Bpsim.EditorProfiler.QA.PositiveControl");
        static ProfilerRecorder cpuProbe, allocationProbe;
        static byte[] retainedAllocation;
        static double probeStart;
        static string probeId;
        static Dictionary<string, object> probeResult;

        static List<Dictionary<string, object>> tests;
        static void Check(string name, bool pass) => tests.Add(new Dictionary<string, object> { { "name", name }, { "pass", pass } });

        public static object Run()
        {
            bool playing = EditorApplication.isPlaying;
            bool profiler = UnityEngine.Profiling.Profiler.enabled;
            bool binaryLog = UnityEngine.Profiling.Profiler.enableBinaryLog;
            string scene = SceneManager.GetActiveScene().path;
            tests = new List<Dictionary<string, object>>();
            var stats = EditorProfilerData.Distribution(new double[] { 4, 1, 3, 2 }, 4);
            Check("distribution mean/nearest-rank percentiles", (double)stats["mean"] == 2.5 &&
                (double)stats["p50"] == 2 && (double)stats["p95"] == 4 && (double)stats["max"] == 4);
            Check("empty distribution is null, not zero", EditorProfilerData.Distribution(new double[0], 0)["mean"] == null);
            var history = new EditorProfilerHistory("qa_session", 3);
            for (int i = 0; i < 5; i++) history.Add(new Dictionary<string, object>());
            var recent = history.Read(2, 0, "");
            var selected = (List<Dictionary<string, object>>)recent["samples"];
            Check("bounded history returns newest samples", selected.Count == 2 && (long)selected[0]["sequence"] == 4);
            var incremental = history.Read(1, 3, "qa_session");
            Check("incremental history does not skip ahead", (long)incremental["nextSequence"] == 4 && (bool)incremental["hasMore"]);
            Check("truncated history is visible", (bool)history.Read(2, 1, "qa_session")["historyTruncated"]);
            Check("domain reload resets cursor", (bool)history.Read(2, 4, "previous_session")["cursorReset"]);
            Check("future cursor resets safely", (bool)history.Read(2, 999, "")["cursorReset"]);
            foreach (var invalid in new[] {
                new Dictionary<string, object> { { "count", 0L } },
                new Dictionary<string, object> { { "count", 121L } },
                new Dictionary<string, object> { { "count", true } },
                new Dictionary<string, object> { { "afterSequence", -1L } },
                new Dictionary<string, object> { { "count", 1.5 } },
                new Dictionary<string, object> { { "sessionId", "../escape" } },
                new Dictionary<string, object> { { "clear", true } } })
            {
                bool rejected = false;
                try { EditorProfilerData.ValidateQuery(invalid, out _, out _, out _); }
                catch (ArgumentException) { rejected = true; }
                Check("invalid read-only query rejected: " + DshBridgeServer.Json.Serialize(invalid), rejected);
            }
            string root = Directory.GetParent(Application.dataPath).FullName;
            string scratchRoot = Path.GetFullPath(Path.Combine(root, ".runtime"));
            string scratch = Path.GetFullPath(Path.Combine(scratchRoot, "editor-profiler-qa-" + Guid.NewGuid().ToString("N")));
            if (!scratch.StartsWith(scratchRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("QA scratch directory escaped its root");
            try
            {
                var writer = new EditorProfilerLog(scratch, "qa_session", "{\"type\":\"header\"}", 512, 3);
                for (int i = 0; i < 20; i++) writer.Enqueue("{\"sequence\":" + i + ",\"payload\":\"" + new string('x', 160) + "\"}");
                writer.Dispose();
                var status = writer.Status();
                var files = Directory.GetFiles(scratch, "editor-*.jsonl");
                Check("writer drains on shutdown without drops", (long)status["writtenLines"] == 20 && (long)status["droppedLines"] == 0 && status["error"] == null);
                Check("log rotation limits retained files", files.Length == 3);
                bool bounded = true, complete = true;
                foreach (string file in files)
                {
                    bounded &= new FileInfo(file).Length <= 512;
                    foreach (string line in File.ReadAllLines(file)) complete &= DshBridgeServer.Json.Parse(line) is Dictionary<string, object>;
                }
                Check("rotated logs stay within byte limit", bounded);
                Check("each retained JSONL line is complete JSON", complete);
                Check("stopped writer rejects new data", !writer.Enqueue("{}"));
                string badDirectory = Path.Combine(scratch, "not-a-directory");
                File.WriteAllText(badDirectory, "block", new System.Text.UTF8Encoding(false));
                var failed = new EditorProfilerLog(badDirectory, "failed", "{}", 512, 3);
                failed.Enqueue("{}");
                failed.Dispose();
                var failure = failed.Status();
                Check("disk failure is visible, not thrown on Editor thread", (long)failure["droppedLines"] == 1 && failure["error"] != null);
            }
            catch (Exception exception) { tests.Add(new Dictionary<string, object> { { "name", "writer exception" }, { "pass", false }, { "error", exception.ToString() } }); }
            finally { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); }
            Check("validation preserves scene/Play Mode/global profiler flags", playing == EditorApplication.isPlaying &&
                profiler == UnityEngine.Profiling.Profiler.enabled && binaryLog == UnityEngine.Profiling.Profiler.enableBinaryLog &&
                scene == SceneManager.GetActiveScene().path);
            int passed = 0;
            foreach (var test in tests) if ((bool)test["pass"]) passed++;
            return new Dictionary<string, object> { { "pass", passed }, { "fail", tests.Count - passed }, { "tests", tests } };
        }

        // Explicit, leased QA only. Injects a known marker and 1 MiB allocation in Edit Mode;
        // never changes global Profiler settings, scenes, or the user's Play Mode state.
        public static object StartCounterProbe()
        {
            if (EditorApplication.isPlaying || probeStart != 0)
                throw new InvalidOperationException("Counter positive control requires idle Edit Mode and no active probe");
            probeId = Guid.NewGuid().ToString("N");
            cpuProbe = ProfilerRecorder.StartNew(ProbeMarker, 64);
            allocationProbe = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 64);
            probeResult = new Dictionary<string, object> { { "runId", probeId }, { "state", "running" } };
            probeStart = EditorApplication.timeSinceStartup;
            AssemblyReloadEvents.beforeAssemblyReload += CancelProbe;
            EditorApplication.quitting += CancelProbe;
            EditorApplication.update += Inject;
            return probeResult;
        }

        static void Inject()
        {
            EditorApplication.update -= Inject;
            retainedAllocation = new byte[1024 * 1024];
            using (ProbeMarker.Auto())
            {
                var watch = Stopwatch.StartNew();
                while (watch.Elapsed.TotalMilliseconds < 5) { }
            }
            EditorApplication.update += FinishProbe;
        }

        static void FinishProbe()
        {
            if (EditorApplication.timeSinceStartup - probeStart < 2) return;
            EditorApplication.update -= FinishProbe;
            AssemblyReloadEvents.beforeAssemblyReload -= CancelProbe;
            EditorApplication.quitting -= CancelProbe;
            var cpu = new List<ProfilerRecorderSample>();
            var allocations = new List<ProfilerRecorderSample>();
            if (cpuProbe.Valid) cpuProbe.CopyTo(cpu);
            if (allocationProbe.Valid) allocationProbe.CopyTo(allocations);
            long cpuMax = 0, allocationMax = 0;
            foreach (var sample in cpu) cpuMax = Math.Max(cpuMax, sample.Value);
            foreach (var sample in allocations) allocationMax = Math.Max(allocationMax, sample.Value);
            probeResult = new Dictionary<string, object> { { "runId", probeId }, { "state", "done" },
                { "cpuAvailable", cpuProbe.Valid }, { "allocationAvailable", allocationProbe.Valid },
                { "cpuMaxMs", cpuMax * 1e-6 }, { "allocatedMaxBytes", allocationMax },
                { "pass", cpuMax >= 1000000 && allocationMax >= 1024 * 1024 } };
            cpuProbe.Dispose();
            allocationProbe.Dispose();
            GC.KeepAlive(retainedAllocation);
            retainedAllocation = null;
            probeStart = 0;
        }

        public static object CounterProbeStatus() => probeResult ?? new Dictionary<string, object> { { "state", "idle" } };

        static void CancelProbe()
        {
            EditorApplication.update -= Inject;
            EditorApplication.update -= FinishProbe;
            AssemblyReloadEvents.beforeAssemblyReload -= CancelProbe;
            EditorApplication.quitting -= CancelProbe;
            cpuProbe.Dispose();
            allocationProbe.Dispose();
            retainedAllocation = null;
            probeStart = 0;
        }
    }
}
#endif
