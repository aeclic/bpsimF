#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Bpsim.Parts.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEditor;
using UnityEngine;
using Collider = Unity.Physics.Collider;

namespace Bpsim.QA
{
    public static partial class AnvilParityQA
    {
        static readonly string[] WheelGroups = { "7_CartWheel", "8_NormalWheel", "9_SmallWheel", "10_MotorWheel", "38_StickyWheel" };

        public static void RunActualWheelPrefabsBatch()
        {
            string label = "a";
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; ++i) if (args[i] == "-wheel-label") label = args[i + 1];
            var checks = new List<Measurement>();
            int colliders = 0;
            foreach (string group in WheelGroups)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameObject/PartGroup_" + group + ".prefab");
                foreach (var collider in prefab.GetComponentsInChildren<UnityEngine.Collider>(true))
                {
                    colliders++;
                    float friction = collider.sharedMaterial == null ? -1 : collider.sharedMaterial.dynamicFriction;
                    WheelCheck(checks, "prefab-material-" + group + "-" + colliders, friction == .025f || friction == .05f || friction == .7f);
                }
            }
            WheelCheck(checks, "actual-prefab-collider-count", colliders == 76);
            var wheels = new[] { "7_CartWheel/Part_CartWheel_0", "8_NormalWheel/Part_NormalWheel_0",
                "9_SmallWheel/Part_SmallWheel_0", "10_MotorWheel/Part_MotorWheel_0",
                "10_MotorWheel/Part_MotorWheel_7", "38_StickyWheel/Part_StickyWheel_0" };
            foreach (var backend in new[] { SimulationType.UnityPhysics, SimulationType.HavokPhysics, SimulationType.AnvilPhysics })
            {
                foreach (string wheel in wheels)
                {
                    try
                    {
                        ActualWheelCoast(checks, backend, wheel, false, false);
                        ActualWheelCoast(checks, backend, wheel, true, false);
                    }
                    catch (Exception ex) { Add(checks, backend, wheel, "exception", "count", 1, 0, ex.ToString()); }
                }
                try { ActualWheelCoast(checks, backend, wheels[0], false, true); }
                catch (Exception ex) { Add(checks, backend, "welded-chassis", "exception", "count", 1, 0, ex.ToString()); }
            }
            int failures = checks.FindAll(m => !m.pass).Count;
            var report = new Report { label = label, runId = Guid.NewGuid().ToString("N"), collectedAtUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion, fingerprint = WheelFingerprint(),
                physicsAssemblySha256 = FileHash(typeof(PhysicsWorld).Assembly.Location), havokApi = s_HavokApi,
                pass = failures == 0, failures = failures, measurements = checks.ToArray() };
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/wheel-actual-" + label + ".json"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(report, true), new System.Text.UTF8Encoding(false));
            Debug.Log("[ActualWheelPrefabs] " + (report.pass ? "PASS" : "FAIL") + " " + (checks.Count - failures) + "/" + checks.Count);
        }

        static BlobAssetReference<Collider> ActualPartShape(Fixture fixture, GameObject part, bool defaultMaterial)
        {
            var leaves = new List<BlobAssetReference<Collider>>();
            var frames = new List<RigidTransform>();
            try
            {
                foreach (var collider in part.GetComponentsInChildren<UnityEngine.Collider>(true))
                {
                    if (!collider.enabled) continue;
                    var leaf = PartPhysics.CreateCollider(collider);
                    if (defaultMaterial) leaf.Value.SetFriction(.5f);
                    leaves.Add(leaf);
                    RigidTransform frame = RigidTransform.identity;
                    if (part.name.Contains("Wheel"))
                    {
                        Transform child = collider.transform;
                        while (child != part.transform)
                        {
                            frame = math.mul(new RigidTransform(child.localRotation, child.localPosition), frame);
                            child = child.parent;
                        }
                    }
                    frames.Add(frame);
                }
                return fixture.PrefabCompound(leaves.ToArray(), frames.ToArray());
            }
            finally { foreach (var leaf in leaves) leaf.Dispose(); }
        }

        static GameObject ActualPart(string name)
        {
            string[] pieces = name.Split('/');
            return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameObject/PartGroup_" + pieces[0] + ".prefab").transform.Find(pieces[1]).gameObject;
        }

        sealed partial class Fixture
        {
            public BlobAssetReference<Collider> PrefabCompound(BlobAssetReference<Collider>[] leaves, RigidTransform[] frames)
            {
                using (var children = new NativeArray<CompoundCollider.ColliderBlobInstance>(leaves.Length, Allocator.Temp))
                {
                    var writable = children;
                    for (int i = 0; i < leaves.Length; i++) writable[i] = new CompoundCollider.ColliderBlobInstance
                    { Collider = leaves[i], CompoundFromChild = frames[i] };
                    var shape = CompoundCollider.Create(children);
                    m_Shapes.Add(shape);
                    return shape;
                }
            }
        }

        static void ActualWheelCoast(List<Measurement> output, SimulationType backend, string name, bool oldMaterial, bool chassis)
        {
            string scenario = (chassis ? "welded-chassis" : name) + (oldMaterial ? "-old-control" : "-fixed");
            using (var f = new Fixture(backend, chassis ? 5 : 1, chassis ? 4 : 0, statics: 2))
            {
                f.Gravity = new float3(0f, -9.81f, 0f);
                var wheel = ActualPartShape(f, ActualPart(name), oldMaterial);
                float bottom = wheel.Value.CalculateAabb().Min.y;
                float y = .005f - bottom;
                f.Static(0, new float3(0f, -.5f, 0f), f.Box(new float3(200f, 1f, 2f)));
                if (chassis)
                {
                    var frame = ActualPartShape(f, ActualPart("6_MetalFrame/Part_MetalFrame_0"), false);
                    f.Body(0, new float3(-1, y + 1, 0), shape: frame);
                    f.Body(1, new float3(0, y + 1, 0), shape: frame);
                    f.Body(2, new float3(1, y + 1, 0), shape: frame);
                    f.Body(3, new float3(-1, y, 0), shape: wheel);
                    f.Body(4, new float3(1, y, 0), shape: wheel);
                    f.Weld(0, 0, 1); f.Weld(1, 1, 2); f.Weld(2, 0, 3); f.Weld(3, 2, 4);
                }
                else f.Body(0, new float3(0, y, 0), shape: wheel);
                for (int i = 0; i < f.World.NumDynamicBodies; i++)
                {
                    var mv = f.World.MotionVelocities[i];
                    // Individual wheel: axle held by its support. Chassis: welds retain real pitch inertia.
                    if (!chassis) mv.InverseInertia = float3.zero;
                    f.MotionVelocity(i, mv);
                    var motion = f.Motion(i); motion.LinearDamping = .2f; f.Motion(i, motion);
                }
                for (int i = 0; i < 50; i++) f.Step();
                float start = f.Position(0).x;
                for (int i = 0; i < f.World.NumDynamicBodies; i++) f.LinearVelocity(i, new float3(5, 0, 0));
                for (int i = 0; i < 150; i++)
                {
                    f.Step();
                    if (chassis && i % 25 == 0)
                        Debug.Log("[ActualWheelTrace] " + backend + " step=" + i + " frame=" + f.Position(0)
                            + " leftWheel=" + f.Position(3) + " rightWheel=" + f.Position(4)
                            + " v=" + f.Velocity(0) + " omega=" + f.Omega(0) + " anchorDrift=" + f.AnchorDrift());
                }
                float speed = math.abs(f.Velocity(0).x), distance = math.abs(f.Position(0).x - start);
                Add(output, backend, scenario, "coast-error", "count", oldMaterial ? (speed < .5f ? 0 : 1) : (speed > 1.4f && distance > 7f ? 0 : 1), 0);
                Add(output, backend, scenario, "speed", "m/s", speed, 5.01f);
                Add(output, backend, scenario, "distance", "m", distance, 15.1f);
            }
        }
    }
}
#endif
