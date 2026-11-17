#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Bpsim.Parts.Gameplay;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bpsim.QA
{
    /// <summary>Lifecycle instrument: inactive construction, explicit editor-thread Awake, and cleanup.
    /// Invoke RunLifecycle(label) through the leased bridge. Never replaces a live light singleton.
    /// A passing edit-mode report is not a rendered-pixel or GPU performance claim.</summary>
    public static partial class BpleLightStutterQA
    {
        [Serializable]
        public sealed class Check
        {
            public string name;
            public bool pass;
            public string detail;
        }

        [Serializable]
        public sealed class LifecycleReport
        {
            public string label, runId, utc, unity;
            public bool playing;
            public int constructorErrors, failures;
            public List<Check> checks = new List<Check>();
        }

        const BindingFlags InstanceFlags = BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly FieldInfo MaskProperties = typeof(BpleLightRuntime).GetField("m_maskProperties", InstanceFlags);
        static readonly MethodInfo AwakeMethod = typeof(BpleLightRuntime).GetMethod("Awake", InstanceFlags);
        static readonly MethodInfo DestroyMethod = typeof(BpleLightRuntime).GetMethod("OnDestroy", InstanceFlags);
        static readonly FieldInfo CameraCallbacks = typeof(RenderPipelineManager).GetField("endCameraRendering",
            BindingFlags.Static | BindingFlags.NonPublic);

        static void Add(LifecycleReport report, string name, bool pass, object detail = null)
        {
            report.checks.Add(new Check { name = name, pass = pass, detail = detail?.ToString() });
            if (!pass) ++report.failures;
        }

        static int CallbackCount(BpleLightRuntime runtime)
        {
            if (CameraCallbacks == null) return -1;
            var callbacks = CameraCallbacks.GetValue(null) as Delegate;
            int count = 0;
            if (callbacks != null)
                foreach (Delegate callback in callbacks.GetInvocationList())
                    if (ReferenceEquals(callback.Target, runtime)) ++count;
            return count;
        }

        public static string Readiness()
        {
            var manager = Bpsim.Parts.PartManager.Instance;
            if (!Application.isPlaying || manager?.System == null || BpleLightRuntime.Instance == null) return "waiting";
            if (manager.IsSimulating) return "simulating";
            return manager.ActiveScene != null ? "ready" : "waiting";
        }

        public static string AllocationCounterProbe()
        {
            long threadBefore = GC.GetAllocatedBytesForCurrentThread();
            long heapBefore = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            var allocation = new byte[1024 * 1024];
            long threadAfter = GC.GetAllocatedBytesForCurrentThread();
            long heapAfter = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            GC.KeepAlive(allocation);
            return "allocated-positive-control=1048576 thread-counter-delta=" + (threadAfter - threadBefore)
                + " mono-used-delta=" + (heapAfter - heapBefore);
        }

        public static string RunLifecycle(string label)
        {
            if (string.IsNullOrEmpty(label) || label != Path.GetFileName(label)
                || label.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("Supply a simple report label", nameof(label));
            var report = new LifecycleReport { label = label, runId = Guid.NewGuid().ToString("N"),
                utc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion, playing = Application.isPlaying };
            if (Application.isPlaying || BpleLightRuntime.Instance != null)
            {
                // Read-only checks are safe even while the user's vehicle is running.
                var live = BpleLightRuntime.Instance;
                Add(report, "live singleton exists", live != null);
                Add(report, "properties initialized", live != null && MaskProperties.GetValue(live) != null);
                Add(report, "Ensure idempotent", live != null && ReferenceEquals(live, BpleLightRuntime.Ensure()));
                Add(report, "exactly one camera callback", live != null && CallbackCount(live) == 1);
            }
            else
            {
                Application.LogCallback log = (message, stack, type) =>
                {
                    if ((type == LogType.Exception || type == LogType.Error)
                        && (message.Contains("CreateImpl") || stack.Contains("BpleLightRuntime..ctor")))
                        ++report.constructorErrors;
                };
                Application.logMessageReceived += log;
                try
                {
                    for (int iteration = 0; iteration < 16; ++iteration)
                    {
                        GameObject host = null;
                        BpleLightRuntime runtime = null;
                        bool awoken = false;
                        try
                        {
                            host = new GameObject("[BpleLightStutterQA fixture]") { hideFlags = HideFlags.HideAndDontSave };
                            host.SetActive(false);
                            runtime = host.AddComponent<BpleLightRuntime>();
                            Add(report, "inactive constructor has no native properties " + iteration,
                                runtime != null && MaskProperties.GetValue(runtime) == null);
                            Add(report, "inactive construction does not register singleton " + iteration,
                                BpleLightRuntime.Instance == null);
                            AwakeMethod.Invoke(runtime, null);
                            awoken = true;
                            Add(report, "Awake initializes native properties " + iteration, MaskProperties.GetValue(runtime) != null);
                            bool same = true;
                            for (int i = 0; i < 64; ++i) same &= ReferenceEquals(runtime, BpleLightRuntime.Ensure());
                            Add(report, "repeated Ensure has one singleton " + iteration, same);
                            Add(report, "one rendering subscription " + iteration, CallbackCount(runtime) == 1);
                        }
                        catch (Exception error)
                        {
                            Add(report, "lifecycle exception " + iteration, false, error);
                        }
                        finally
                        {
                            // Normal MonoBehaviour callbacks do not run in edit mode; invoke the same
                            // cleanup explicitly, then destroy only the instrument-owned inactive host.
                            if (runtime != null && awoken) DestroyMethod.Invoke(runtime, null);
                            if (runtime != null)
                                Add(report, "subscription released " + iteration, CallbackCount(runtime) == 0);
                            if (host != null) UnityEngine.Object.DestroyImmediate(host);
                            Add(report, "singleton released " + iteration, BpleLightRuntime.Instance == null);
                        }
                    }
                }
                finally { Application.logMessageReceived -= log; }
                Add(report, "zero constructor errors", report.constructorErrors == 0, report.constructorErrors);
            }
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/bple-light-lifecycle-" + label + ".json"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(report, true), new System.Text.UTF8Encoding(false));
            string result = "checks=" + report.checks.Count + " failures=" + report.failures
                + " constructorErrors=" + report.constructorErrors + " report=" + path;
            Debug.Log("[BpleLightStutterQA] " + result);
            return result;
        }
    }
}
#endif
