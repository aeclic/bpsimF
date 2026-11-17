#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using DshBridge;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Bpsim.Perf
{
    [InitializeOnLoad]
    public static class EditorProfiler
    {
        const double IntervalSeconds = 1;
        const int RecorderCapacity = 512;
        static readonly string Session = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff", CultureInfo.InvariantCulture)
            + "-" + Guid.NewGuid().ToString("N");
        static readonly EditorProfilerHistory History = new EditorProfilerHistory(Session);
        static readonly List<Metric> Metrics = new List<Metric>();
        static readonly List<string> Errors = new List<string>();
        static readonly BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        static readonly Type AnvilType = FindType("Bpsim.Physics.Anvil.AnvilStepSystem");
        static readonly Type GameplayType = FindType("Bpsim.Parts.Gameplay.PartGameplayManager");
        static readonly Type PartManagerType = FindType("Bpsim.Parts.PartManager");
        static readonly Type LightType = FindType("Bpsim.Parts.Gameplay.BpleLightRuntime");
        static readonly EditorProfilerLog Log;
        static double windowStart, lastPublished, lastCollectionMs;
        static long lastAnvilStep;
        static bool stopped;

        static EditorProfiler()
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            var header = new Dictionary<string, object> { { "schema", 1 }, { "type", "editor_profiler_session" },
                { "sessionId", Session }, { "editorOnly", true }, { "utc", DateTime.UtcNow.ToString("o") },
                { "project", root }, { "unity", Application.unityVersion },
                { "editorProcessId", Process.GetCurrentProcess().Id }, { "intervalSeconds", IntervalSeconds },
                { "historyCapacity", EditorProfilerData.HistoryCapacity },
                { "semantics", "Editor measurements, including Editor overhead/waits; not a Player benchmark. Native markers are aggregated per recorded frame. Anvil timings are last-step snapshots only." } };
            Log = new EditorProfilerLog(Path.Combine(root, "Logs", "Profiler"), Session, DshBridgeServer.Json.Serialize(header));
            Discover();
            windowStart = Now();
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
        }

        static double Now() => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;

        static void Discover()
        {
            var handles = new List<ProfilerRecorderHandle>();
            var available = new Dictionary<string, ProfilerRecorderHandle>();
            try
            {
                ProfilerRecorderHandle.GetAvailable(handles);
                foreach (var handle in handles)
                {
                    var info = ProfilerRecorderHandle.GetDescription(handle);
                    if (!available.ContainsKey(info.Name)) available.Add(info.Name, handle);
                }
            }
            catch (Exception exception) { Errors.Add("Recorder discovery: " + exception.Message); }
            Add(available, "mainThreadMs", "Main Thread", true);
            Add(available, "playerLoopMs", "PlayerLoop", true);
            Add(available, "editorLoopMs", "EditorLoop", true);
            Add(available, "renderThreadMs", "Render Thread", true);
            Add(available, "gcAllocatedBytes", "GC Allocated In Frame", false);
            Add(available, "usedMemoryBytes", "Total Used Memory", false);
            Add(available, "gcUsedMemoryBytes", "GC Used Memory", false);
        }

        static void Add(Dictionary<string, ProfilerRecorderHandle> handles, string key, string marker, bool time)
        {
            var metric = new Metric { Key = key, Marker = marker, Unit = time ? "ms" : "bytes", Scale = time ? 1e-6 : 1 };
            Metrics.Add(metric);
            if (!handles.TryGetValue(marker, out var handle))
            {
                metric.Error = "Marker is not registered in this Editor configuration";
                return;
            }
            try
            {
                var description = ProfilerRecorderHandle.GetDescription(handle);
                metric.Category = description.Category.Name;
                metric.NativeUnit = description.UnitType.ToString();
                if (metric.NativeUnit != (time ? "TimeNanoseconds" : "Bytes"))
                {
                    metric.Error = "Unexpected native unit: " + metric.NativeUnit;
                    return;
                }
                metric.Recorder = new ProfilerRecorder(handle, RecorderCapacity,
                    ProfilerRecorderOptions.StartImmediately | ProfilerRecorderOptions.WrapAroundWhenCapacityReached |
                    ProfilerRecorderOptions.SumAllSamplesInFrame);
                if (!metric.Recorder.Valid) metric.Error = "ProfilerRecorder is invalid";
            }
            catch (Exception exception) { metric.Error = exception.GetType().Name + ": " + exception.Message; }
        }

        static void Update()
        {
            if (stopped) return;
            double now = Now();
            if (now - windowStart < IntervalSeconds) return;
            double begin = now;
            try
            {
                var measurements = new Dictionary<string, object>();
                foreach (Metric metric in Metrics) measurements[metric.Key] = metric.Read();
                var scene = SceneManager.GetActiveScene();
                var sample = new Dictionary<string, object> { { "schema", 1 }, { "type", "editor_profiler_sample" },
                    { "editorOnly", true }, { "utc", DateTime.UtcNow.ToString("o") },
                    { "windowSeconds", now - windowStart }, { "frame", Time.frameCount },
                    { "context", new Dictionary<string, object> {
                        { "mode", EditorApplication.isPlaying ? "play" : "edit" },
                        { "paused", EditorApplication.isPaused }, { "compiling", EditorApplication.isCompiling },
                        { "transitioning", EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying },
                        { "focused", Application.isFocused }, { "scene", scene.name }, { "scenePath", scene.path },
                        { "timeScale", Time.timeScale }, { "fixedDeltaTime", Time.fixedDeltaTime },
                        { "targetFrameRate", Application.targetFrameRate }, { "vSyncCount", QualitySettings.vSyncCount } } },
                    { "metrics", measurements }, { "runtime", RuntimeContext() } };
                sample["collectionMs"] = (Now() - begin) * 1000;
                History.Add(sample);
                lastPublished = now;
                Log.Enqueue(DshBridgeServer.Json.Serialize(sample));
                lastCollectionMs = (Now() - begin) * 1000;
            }
            catch (Exception exception)
            {
                if (Errors.Count == 8) Errors.RemoveAt(0);
                Errors.Add("Collection: " + exception.GetType().Name + ": " + exception.Message);
            }
            windowStart = now;
        }

        public static object Read(Dictionary<string, object> query)
        {
            EditorProfilerData.ValidateQuery(query, out int count, out long after, out string session);
            var result = History.Read(count, after, session);
            result["editorOnly"] = true;
            result["enabled"] = !stopped;
            result["intervalSeconds"] = IntervalSeconds;
            result["historyCapacity"] = EditorProfilerData.HistoryCapacity;
            result["sampleAgeSeconds"] = History.Latest == null ? null : (object)Math.Max(0, Now() - lastPublished);
            result["freshness"] = History.Latest == null ? "warming-up" : (Now() - lastPublished > 5 ? "stale" : "live");
            result["collectionAndEnqueueMs"] = lastCollectionMs;
            result["log"] = Log.Status();
            result["errors"] = new List<string>(Errors);
            result["semantics"] = "CPU marker durations include Editor overhead/waits. Missing data is null/unavailable, not zero. Anvil timings describe only the latest observed physics step; they are not window percentiles.";
            return result;
        }

        static Dictionary<string, object> RuntimeContext()
        {
            var result = new Dictionary<string, object>();
            try
            {
                object active = Static(AnvilType, "Active");
                long step = Convert.ToInt64(Static(AnvilType, "StepCount") ?? 0);
                bool running = active is bool value && value;
                var anvil = new Dictionary<string, object> { { "available", AnvilType != null }, { "active", running },
                    { "stepCount", step }, { "stepsSinceLastSummary", step >= lastAnvilStep ? step - lastAnvilStep : step },
                    { "freshStep", running && step != lastAnvilStep }, { "timingScope", "last-step-only" } };
                foreach (string name in new[] { "LastSyncToMs", "LastStepMs", "LastSyncFromMs", "LastTreeMs" })
                    anvil[name] = running && step != lastAnvilStep ? Static(AnvilType, name) : null;
                lastAnvilStep = step;
                result["anvil"] = anvil;
                object gameplay = Static(GameplayType, "Instance");
                result["behaviors"] = Instance(gameplay, "BehaviorCount");
                result["gameplayRunning"] = Instance(gameplay, "IsRunning");
                object manager = Static(PartManagerType, "Instance");
                result["simulating"] = Instance(manager, "IsSimulating");
                object light = Static(LightType, "Instance");
                result["lightSources"] = Instance(light, "SourceCount");
                result["lightContacts"] = Instance(light, "ContactCount");
            }
            catch (Exception exception) { result["error"] = exception.Message; }
            return result;
        }

        static Type FindType(string name)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(name, false);
                if (type != null) return type;
            }
            return null;
        }

        static object Static(Type type, string name) => type?.GetProperty(name, StaticFlags)?.GetValue(null);
        static object Instance(object instance, string name)
        {
            if (instance == null || (instance is UnityEngine.Object unityObject && !unityObject)) return null;
            return instance.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance);
        }

        static void Stop()
        {
            if (stopped) return;
            stopped = true;
            EditorApplication.update -= Update;
            AssemblyReloadEvents.beforeAssemblyReload -= Stop;
            EditorApplication.quitting -= Stop;
            foreach (Metric metric in Metrics) metric.Recorder.Dispose();
            Log.Dispose();
        }

        sealed class Metric
        {
            internal string Key, Marker, Unit, NativeUnit, Category, Error;
            internal double Scale;
            internal ProfilerRecorder Recorder;
            readonly List<ProfilerRecorderSample> samples = new List<ProfilerRecorderSample>(RecorderCapacity);
            readonly double[] values = new double[RecorderCapacity];

            internal Dictionary<string, object> Read()
            {
                var result = new Dictionary<string, object> { { "marker", Marker }, { "category", Category },
                    { "unit", Unit }, { "nativeUnit", NativeUnit }, { "recorderCapacity", RecorderCapacity } };
                result["aggregation"] = Unit == "ms" ? "duration-per-recorded-frame" :
                    (Key == "gcAllocatedBytes" ? "allocated-bytes-per-recorded-frame" : "memory-gauge");
                if (!Recorder.Valid)
                {
                    result["available"] = false;
                    result["state"] = "unsupported";
                    result["error"] = Error;
                    result["statistics"] = EditorProfilerData.Distribution(values, 0);
                    result["last"] = null;
                    return result;
                }
                try
                {
                    bool overflow = Recorder.WrappedAround;
                    Recorder.CopyTo(samples, true);
                    long invocations = 0;
                    int count = 0;
                    double last = 0;
                    for (int i = 0; i < samples.Count; i++)
                    {
                        // Reset can leave an uncompleted frame slot with Count == 0.
                        // A registered but unobserved marker must not become a zero-cost measurement.
                        if (samples[i].Count == 0) continue;
                        last = values[count++] = samples[i].Value * Scale;
                        invocations += samples[i].Count;
                    }
                    result["available"] = true;
                    result["state"] = count == 0 ? "empty-window" : "ready";
                    result["last"] = count == 0 ? null : (object)last;
                    result["statistics"] = EditorProfilerData.Distribution(values, count);
                    if (Unit == "bytes" && Key != "gcAllocatedBytes")
                        ((Dictionary<string, object>)result["statistics"])["sum"] = null;
                    result["windowFrameSlots"] = samples.Count;
                    result["recordedInvocations"] = invocations;
                    result["overflowed"] = overflow;
                }
                catch (Exception exception)
                {
                    result["available"] = false;
                    result["state"] = "error";
                    result["error"] = exception.Message;
                    result["last"] = null;
                    result["statistics"] = EditorProfilerData.Distribution(values, 0);
                }
                return result;
            }
        }
    }
}
#endif
