#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Bpsim.Parts;
using Bpsim.Parts.Gameplay;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Bpsim.QA
{
    public static partial class BpleLightStutterQA
    {
        [Serializable]
        public sealed class PreviewSample
        {
            public double milliseconds;
            public long allocatedBytes;
        }

        [Serializable]
        public sealed class PreviewCase
        {
            public string scenario, stateSha256;
            public int sources, visible, warmup = 16, sampleCount = 128;
            public double p50Ms, p95Ms, p99Ms, maxMs, meanAllocatedBytes;
            public List<PreviewSample> samples = new List<PreviewSample>();
        }

        [Serializable]
        public sealed class PreviewReport
        {
            public string label, runId, utc, probeSha256, renderingSha256;
            public List<PreviewCase> cases = new List<PreviewCase>();
            public List<Check> checks = new List<Check>();
            public List<string> errors = new List<string>();
        }

        static readonly FieldInfo PreviewSources = typeof(BpleLightRuntime).GetField("m_previews", InstanceFlags);
        static readonly MethodInfo PreviewTick = typeof(BpleLightRuntime).GetMethod("LateUpdate", InstanceFlags);

        static string PreviewHash(string path)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }

        static Dictionary<Entity, BpleLightRuntime.Source> Previews(BpleLightRuntime runtime)
            => (Dictionary<Entity, BpleLightRuntime.Source>)PreviewSources.GetValue(runtime);

        static void PreviewCheck(PreviewReport report, string name, bool pass)
            => report.checks.Add(new Check { name = name, pass = pass });

        static string PreviewState(Dictionary<Entity, BpleLightRuntime.Source> sources)
        {
            var em = PartGameplayUtil.Em;
            using (var hash = SHA256.Create())
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                foreach (var source in sources.Values.OrderBy(s => em.GetComponentData<PartTransform>(s.Part).X)
                    .ThenBy(s => em.GetComponentData<PartTransform>(s.Part).Y))
                {
                    var grid = em.GetComponentData<PartTransform>(source.Part);
                    writer.Write(grid.X); writer.Write(grid.Y);
                    writer.Write((int)source.Info.PartType); writer.Write(source.Info.PartIndex);
                    writer.Write(source.Position.x); writer.Write(source.Position.y);
                    writer.Write(source.Direction.x); writer.Write(source.Direction.y);
                    writer.Write(source.Shape.Length); writer.Write(source.Shape.HalfWidth); writer.Write(source.Shape.Angle);
                    writer.Write(source.Visual != null && source.Visual.activeSelf);
                    if (source.Mesh != null)
                    {
                        writer.Write(source.Mesh.vertexCount);
                        foreach (Vector3 vertex in source.Mesh.vertices)
                        { writer.Write(vertex.x); writer.Write(vertex.y); writer.Write(vertex.z); }
                        foreach (int index in source.Mesh.triangles) writer.Write(index);
                    }
                    writer.Write(source.Color.r); writer.Write(source.Color.g); writer.Write(source.Color.b); writer.Write(source.Color.a);
                }
                writer.Flush();
                return BitConverter.ToString(hash.ComputeHash(stream.ToArray())).Replace("-", "").ToLowerInvariant();
            }
        }

        static Entity PreviewPlace(PartManager manager, PartScene scene, PartType type, int x, int y)
        {
            var info = new PartTypeInfo(type, 0);
            Entity prefab = manager.EntitySpawner.FindPrefab(info);
            if (prefab == Entity.Null) throw new InvalidOperationException("Missing light prefab: " + info);
            var request = new PartCreationRequest(prefab, scene.SceneID, info, x, y, 0, false);
            if (!scene.Unmanaged.PlacePartAt(in request, false) || !scene.Unmanaged.FindPartAt(x, y, out Entity part))
                throw new InvalidOperationException("Light fixture placement failed");
            PartGameplayUtil.Em.SetComponentData(part, LocalTransform.FromPosition(new float3(x, y, 0)));
            return part;
        }

        /// <summary>CPU-only build-preview probe in a temporary PartScene. Requires stable Play Mode,
        /// a ready manager, and no running simulation. Original scene/settings are restored in finally.
        /// It calls the real LateUpdate path synchronously; GPU/night render and physics are excluded.</summary>
        public static string RunPreviewProbe(string label)
        {
            if (string.IsNullOrEmpty(label) || label != Path.GetFileName(label)
                || label.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("Supply a simple report label", nameof(label));
            var manager = PartManager.Instance;
            if (!Application.isPlaying || manager?.System == null || manager.IsSimulating)
                return "blocked: requires ready Play Mode without an active simulation";
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var report = new PreviewReport { label = label, runId = Guid.NewGuid().ToString("N"), utc = DateTime.UtcNow.ToString("O"),
                probeSha256 = PreviewHash(Path.Combine(root, "Assets/Scripts/Assembly-CSharp/Bpsim/QA/BpleLightStutterQA.PreviewProbe.cs")),
                renderingSha256 = PreviewHash(Path.Combine(root, "Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Gameplay/BpleLightRuntime.Rendering.cs")) };
            PartScene original = manager.ActiveScene, fixture = null;
            int originalCount = original != null ? original.Unmanaged.Parts.Length : 0;
            var settings = UserSettings.Instance.SimulationSettings;
            bool lightSystem = settings.BpleLightSystem;
            var runtime = BpleLightRuntime.Ensure();
            var tick = (Action)PreviewTick.CreateDelegate(typeof(Action), runtime);
            try
            {
                PartGameplayUtil.Em.CompleteAllTrackedJobs();
                settings.BpleLightSystem = true;
                string name = "BpleLightPreviewFixture-" + report.runId;
                manager.LoadEmptyScene(name);
                fixture = UnityEngine.Object.FindObjectsOfType<PartScene>(true).Single(scene => scene.SceneName == name);
                manager.SelectScene(fixture.SceneID);
                foreach (string scenario in new[] { "empty", "spot-256", "point-256" })
                {
                    fixture.Unmanaged.ClearParts();
                    tick();
                    if (scenario != "empty")
                        for (int i = 0; i < 256; ++i)
                            PreviewPlace(manager, fixture, scenario == "spot-256" ? PartType.SpotLight : PartType.PointLight, i % 16, i / 16);
                    for (int i = 0; i < 16; ++i) tick();
                    var result = new PreviewCase { scenario = scenario };
                    for (int i = 0; i < 128; ++i)
                    {
                        long allocated = GC.GetAllocatedBytesForCurrentThread();
                        long start = Stopwatch.GetTimestamp();
                        tick();
                        long end = Stopwatch.GetTimestamp();
                        var sample = new PreviewSample { milliseconds = (end - start) * 1000.0 / Stopwatch.Frequency,
                            allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated };
                        result.samples.Add(sample);
                        result.meanAllocatedBytes += sample.allocatedBytes;
                    }
                    var sources = Previews(runtime);
                    result.sources = sources.Count;
                    result.visible = sources.Values.Count(source => source.Visual != null && source.Visual.activeSelf);
                    result.stateSha256 = PreviewState(sources);
                    double[] times = result.samples.Select(s => s.milliseconds).OrderBy(value => value).ToArray();
                    result.p50Ms = times[63]; result.p95Ms = times[121]; result.p99Ms = times[126]; result.maxMs = times[127];
                    result.meanAllocatedBytes /= 128;
                    report.cases.Add(result);
                    int expected = scenario == "empty" ? 0 : 256;
                    PreviewCheck(report, scenario + " membership", result.sources == expected);
                    PreviewCheck(report, scenario + " visible", result.visible == expected);
                    if (sources.Count > 0)
                    {
                        var source = sources.Values.First();
                        var pose = PartGameplayUtil.Em.GetComponentData<LocalTransform>(source.Part);
                        Vector2 before = source.Position;
                        pose.Position.x += 1.25f;
                        PartGameplayUtil.Em.SetComponentData(source.Part, pose);
                        tick();
                        PreviewCheck(report, scenario + " pose refreshed", Vector2.Distance(source.Position, before + Vector2.right * 1.25f) < .00001f);
                        PreviewPlace(manager, fixture, scenario == "spot-256" ? PartType.SpotLight : PartType.PointLight, 16, 0);
                        tick();
                        PreviewCheck(report, scenario + " new light discovered", Previews(runtime).Count == 257);
                    }
                    fixture.Unmanaged.ClearParts();
                    tick();
                    PreviewCheck(report, scenario + " removed lights discarded", Previews(runtime).Count == 0);
                }
            }
            catch (Exception error) { report.errors.Add(error.ToString()); }
            finally
            {
                if (fixture != null) fixture.Unmanaged.ClearParts();
                settings.BpleLightSystem = lightSystem;
                if (original != null)
                {
                    manager.SelectScene(original.SceneID);
                    PreviewCheck(report, "original build preserved", original.Unmanaged.Parts.Length == originalCount);
                    PreviewCheck(report, "original scene restored", manager.ActiveScene == original);
                }
                if (fixture != null) manager.UnloadScene(fixture.SceneID);
                tick();
            }
            string path = Path.Combine(root, "Logs/bple-preview-" + label + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(report, true), new System.Text.UTF8Encoding(false));
            string resultText = "cases=" + report.cases.Count + " failedChecks=" + report.checks.Count(c => !c.pass)
                + " errors=" + report.errors.Count + " report=" + path;
            Debug.Log("[BplePreviewProbe] " + resultText);
            return resultText;
        }
    }
}
#endif
