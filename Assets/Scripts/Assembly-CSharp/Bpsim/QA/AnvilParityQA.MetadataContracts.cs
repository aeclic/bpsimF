#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Bpsim.Physics.Anvil;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;

namespace Bpsim.QA
{
    public static partial class AnvilParityQA
    {
        [Serializable]
        public sealed class MetadataContractReport
        {
            public string label, runId, utc, fingerprint, contractsSha256;
            public int failures;
            public List<Measurement> measurements = new List<Measurement>();
        }

        static void KeepJointVersion(Fixture fixture, byte version)
        {
            var joints = fixture.World.Joints;
            var joint = joints[0];
            joint.Version = version;
            joints[0] = joint;
        }

        static void MetadataThresholds(List<Measurement> output, SimulationType backend)
        {
            using (var fixture = new Fixture(backend, 2, 1))
            {
                fixture.Body(0, new float3(-.5f, 0, 0));
                fixture.Body(1, new float3(.5f, 0, 0));
                fixture.Weld(0, 0, 1);
                byte version = fixture.World.Joints[0].Version;
                fixture.Step();
                fixture.Weld(0, 0, 1, float3.zero);
                KeepJointVersion(fixture, version);
                fixture.LinearVelocity(0, new float3(1, 0, 0));
                fixture.Step();
                Add(output, backend, "metadata-no-version-threshold-on", "missed-event", "count", fixture.HadImpulse ? 0 : 1, 0);
                fixture.Weld(0, 0, 1);
                KeepJointVersion(fixture, version);
                fixture.LinearVelocity(0, new float3(3, 0, 0));
                fixture.Step();
                Add(output, backend, "metadata-no-version-threshold-off", "stale-event", "count", fixture.HadImpulse ? 1 : 0, 0);
            }
        }

        static void MetadataFrames(List<Measurement> output, SimulationType backend)
        {
            using (var fixture = new Fixture(backend, 2, 1))
            {
                fixture.Body(0, new float3(-.5f, 0, 0));
                fixture.Body(1, new float3(.5f, 0, 0));
                fixture.Weld(0, 0, 1);
                fixture.Step();
                var joints = fixture.World.Joints;
                var joint = joints[0];
                joint.AFromJoint.Translation += new float3(.2f, 0, 0);
                joints[0] = joint;
                for (int i = 0; i < 40; ++i) fixture.Step();
                var a = fixture.Motion(0); var b = fixture.Motion(1);
                var worldA = math.mul(a.WorldFromMotion, math.inverse(a.BodyFromMotion));
                var worldB = math.mul(b.WorldFromMotion, math.inverse(b.BodyFromMotion));
                float error = math.distance(math.transform(worldA, joint.AFromJoint.Translation),
                    math.transform(worldB, joint.BFromJoint.Translation));
                Add(output, backend, "metadata-no-version-frame", "anchor-error", "m", error, .01f);
            }
        }

        public static string RunMetadataContracts(string label)
        {
            if (string.IsNullOrEmpty(label) || label != Path.GetFileName(label)
                || label.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("Supply a simple report label", nameof(label));
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var report = new MetadataContractReport { label = label, runId = Guid.NewGuid().ToString("N"),
                utc = DateTime.UtcNow.ToString("O"), fingerprint = Fingerprint(),
                contractsSha256 = FileHash(Path.Combine(root, "Assets/Scripts/Assembly-CSharp/Bpsim/QA/AnvilParityQA.MetadataContracts.cs")) };
            foreach (SimulationType backend in new[] { SimulationType.UnityPhysics, SimulationType.AnvilPhysics })
            {
                try { MetadataThresholds(report.measurements, backend); MetadataFrames(report.measurements, backend); }
                catch (Exception error) { Add(report.measurements, backend, "metadata-instrument", "exception", "count", 1, 0, error.ToString()); }
            }
            try
            {
                using (var fixture = new Fixture(SimulationType.AnvilPhysics, 2, 1))
                using (var contacts = new NativeList<AnvilContactEvent>(Allocator.Persistent))
                {
                    fixture.Body(0, new float3(-.2f, 0, 0));
                    fixture.Body(1, new float3(.2f, 0, 0));
                    fixture.Weld(0, 0, 1);
                    fixture.Step();
                    Add(report.measurements, SimulationType.AnvilPhysics, "metadata-collision-off", "unexpected-contacts", "count", fixture.MetadataContactCount(contacts), 0);
                    var joints = fixture.World.Joints;
                    var joint = joints[0];
                    joint.EnableCollision = 1;
                    joints[0] = joint;
                    for (int i = 0; i < 20; ++i) fixture.Step();
                    Add(report.measurements, SimulationType.AnvilPhysics, "metadata-no-version-collision-on", "missing-contacts", "count", fixture.MetadataContactCount(contacts) > 0 ? 0 : 1, 0);
                }
            }
            catch (Exception error) { Add(report.measurements, SimulationType.AnvilPhysics, "metadata-instrument", "collision-exception", "count", 1, 0, error.ToString()); }
            report.failures = report.measurements.FindAll(m => !m.pass).Count;
            string path = Path.Combine(root, "Logs/anvil-metadata-" + label + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(report, true), new System.Text.UTF8Encoding(false));
            return "checks=" + report.measurements.Count + " failures=" + report.failures + " report=" + path;
        }

        public static void RunMetadataContractsBatch()
        {
            string label = "batch";
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; ++i)
                if (args[i] == "-anvil-parity-label") label = args[i + 1];
            Debug.Log("[AnvilMetadataContracts] " + RunMetadataContracts(label));
        }

        sealed partial class Fixture
        {
            public int MetadataContactCount(NativeList<AnvilContactEvent> contacts) => m_Native.CollectContactEvents(contacts, 65536);
        }
    }
}
#endif
