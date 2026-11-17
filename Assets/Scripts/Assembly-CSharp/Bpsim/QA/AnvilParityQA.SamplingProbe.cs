#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Bpsim.Physics.Anvil;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Bpsim.QA
{
    public static partial class AnvilParityQA
    {
        [Serializable]
        public sealed class SamplingProbeReport
        {
            public string label, runId, utc, fingerprint, probeSha256;
            public int contacts, defaultUniqueHashes, mixedUniqueHashes, defaultLargestGroup, mixedLargestGroup;
            public double defaultP50Ms, defaultP95Ms, mixedP50Ms, mixedP95Ms;
            public bool pass;
            public ulong[] keys;
        }

        sealed class ProbePairComparer : IEqualityComparer<ulong>
        {
            public bool Equals(ulong a, ulong b) => a == b;
            public int GetHashCode(ulong value)
            {
                unchecked
                {
                    value ^= value >> 33;
                    value *= 0xff51afd7ed558ccdUL;
                    value ^= value >> 33;
                    value *= 0xc4ceb9fe1a85ec53UL;
                    value ^= value >> 33;
                    return (int)(value ^ (value >> 32));
                }
            }
        }

        static double[] ProbeDictionary(ulong[] keys, IEqualityComparer<ulong> comparer)
        {
            var dictionary = new Dictionary<ulong, long>(keys.Length, comparer);
            var samples = new double[128];
            for (int trial = -16; trial < samples.Length; ++trial)
            {
                long start = Stopwatch.GetTimestamp();
                dictionary.Clear();
                for (int substep = 0; substep < 8; ++substep)
                    foreach (ulong key in keys)
                    {
                        dictionary.TryGetValue(key, out long sum);
                        dictionary[key] = sum + 1;
                    }
                long end = Stopwatch.GetTimestamp();
                if (trial >= 0) samples[trial] = Milliseconds(start, end);
                if (dictionary.Count != keys.Length || dictionary.Values.Any(value => value != 8))
                    throw new InvalidOperationException("Dictionary comparison changed key/value results");
            }
            Array.Sort(samples);
            return samples;
        }

        /// <summary>Root-cause probe: actual private-world pair keys, not invented favorable keys.
        /// Does not touch scenes/gameplay. Micro-timings do not replace the full adapter benchmark.</summary>
        public static string RunSamplingProbe(string label)
        {
            if (string.IsNullOrEmpty(label) || label != Path.GetFileName(label)
                || label.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("Supply a simple report label", nameof(label));
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var report = new SamplingProbeReport { label = label, runId = Guid.NewGuid().ToString("N"),
                utc = DateTime.UtcNow.ToString("O"), fingerprint = Fingerprint(),
                probeSha256 = FileHash(Path.Combine(root, "Assets/Scripts/Assembly-CSharp/Bpsim/QA/AnvilParityQA.SamplingProbe.cs")) };
            using (var fixture = new Fixture(SimulationType.AnvilPhysics, 512, 0, 2))
            {
                fixture.SubSteps = 8;
                fixture.Gravity = new float3(0, -9.81f, 0);
                var shape = fixture.Box(new float3(.5f));
                for (int i = 0; i < 512; ++i)
                    fixture.Body(i, new float3((i % 16 - 8) * .51f, .25f + i / 16 * .51f, 0), shape: shape);
                fixture.Static(0, new float3(0, -.5f, 0), fixture.Box(new float3(100, 1, 4)));
                for (int i = 0; i < 64; ++i) fixture.Step();
                report.keys = fixture.ContactPairKeys();
            }
            report.contacts = report.keys.Length;
            var mixed = new ProbePairComparer();
            var defaults = report.keys.GroupBy(key => key.GetHashCode()).ToArray();
            var mixes = report.keys.GroupBy(key => mixed.GetHashCode(key)).ToArray();
            report.defaultUniqueHashes = defaults.Length;
            report.mixedUniqueHashes = mixes.Length;
            report.defaultLargestGroup = defaults.Max(group => group.Count());
            report.mixedLargestGroup = mixes.Max(group => group.Count());
            var slow = ProbeDictionary(report.keys, EqualityComparer<ulong>.Default);
            var fast = ProbeDictionary(report.keys, mixed);
            report.defaultP50Ms = slow[63]; report.defaultP95Ms = slow[121];
            report.mixedP50Ms = fast[63]; report.mixedP95Ms = fast[121];
            report.pass = report.contacts >= 512 && report.defaultLargestGroup >= 32
                && report.mixedLargestGroup <= report.defaultLargestGroup / 2
                && report.mixedP95Ms < report.defaultP95Ms * .5;
            string path = Path.Combine(root, "Logs/anvil-sampling-" + label + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(report, true), new System.Text.UTF8Encoding(false));
            return "pass=" + report.pass + " contacts=" + report.contacts + " hashes=" + report.defaultUniqueHashes
                + "->" + report.mixedUniqueHashes + " largest=" + report.defaultLargestGroup + "->" + report.mixedLargestGroup
                + " p95=" + report.defaultP95Ms.ToString("F3") + "->" + report.mixedP95Ms.ToString("F3") + " ms report=" + path;
        }

        public static void RunSamplingProbeBatch()
        {
            string label = "batch";
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; ++i)
                if (args[i] == "-anvil-parity-label") label = args[i + 1];
            Debug.Log("[AnvilSamplingProbe] " + RunSamplingProbe(label));
        }

        sealed partial class Fixture
        {
            public ulong[] ContactPairKeys()
            {
                var field = typeof(AnvilWorld).GetField("m_FrameContacts", InstanceMembers);
                var contacts = (IDictionary)field.GetValue(m_Native);
                var keys = new ulong[contacts.Count];
                contacts.Keys.CopyTo(keys, 0);
                return keys;
            }
        }
    }
}
#endif
