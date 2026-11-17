#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Bpsim.Parts;
using Bpsim.Parts.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;
using Collider = Unity.Physics.Collider;
using Material = Unity.Physics.Material;

namespace Bpsim.QA
{
    /// <summary>Impulse-only tread coasting contracts; no default world, scenes or gameplay settings.</summary>
    public static partial class AnvilParityQA
    {
        public static string RunWheelFriction(string label)
        {
            var checks = new List<Measurement>();
            MaterialContracts(checks);
            foreach (var backend in new[] { SimulationType.UnityPhysics, SimulationType.HavokPhysics, SimulationType.AnvilPhysics })
            {
                try
                {
                    CoastingContract(checks, backend, 0.025f);
                    CoastingContract(checks, backend, 0.05f);
                    CoastingContract(checks, backend, -1f);
                }
                catch (Exception ex)
                {
                    Add(checks, backend, "wheel-backend", "exception", "count", 1, 0, ex.ToString());
                }
            }
            int failures = checks.FindAll(m => !m.pass).Count;
            var report = new Report
            {
                label = label, runId = Guid.NewGuid().ToString("N"), collectedAtUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion, fingerprint = WheelFingerprint(),
                physicsAssemblySha256 = FileHash(typeof(PhysicsWorld).Assembly.Location), havokApi = s_HavokApi,
                pass = failures == 0, failures = failures, measurements = checks.ToArray()
            };
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/wheel-friction-" + label + ".json"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(report, true), new System.Text.UTF8Encoding(false));
            string summary = $"[WheelFriction] {(report.pass ? "PASS" : "FAIL")} {checks.Count - failures}/{checks.Count}; {path}";
            Debug.Log(summary);
            return summary;
        }

        public static void RunWheelFrictionBatch()
        {
            string label = "batch";
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; ++i) if (args[i] == "-wheel-label") label = args[i + 1];
            RunWheelFriction(label);
        }

        static string WheelFingerprint()
        {
            return FileHash(typeof(AnvilParityQA).Assembly.Location) + ":" + FileHash(typeof(PhysicsWorld).Assembly.Location);
        }

        static void WheelCheck(List<Measurement> output, string name, bool condition, string detail = "")
        {
            Add(output, SimulationType.UnityPhysics, name, "contract-error", "count", condition ? 0 : 1, 0, detail);
        }

        static PhysicMaterial SourceWheelMaterial(float friction)
        {
            return new PhysicMaterial("wheel-friction-qa")
            { dynamicFriction = friction, staticFriction = 0f, frictionCombine = PhysicMaterialCombine.Multiply,
                bounciness = 0f, bounceCombine = PhysicMaterialCombine.Multiply };
        }

        static unsafe void MaterialContracts(List<Measurement> output)
        {
            WheelCheck(output, "null-material-default", PartPhysics.ConvertMaterial(null).Equals(Material.Default));
            foreach (PhysicMaterialCombine policy in Enum.GetValues(typeof(PhysicMaterialCombine)))
            {
                var source = new PhysicMaterial { dynamicFriction = .025f, staticFriction = 0f, bounciness = .3f,
                    frictionCombine = policy, bounceCombine = policy };
                try
                {
                    var converted = PartPhysics.ConvertMaterial(source);
                    var expected = policy == PhysicMaterialCombine.Maximum ? Material.CombinePolicy.Maximum
                        : policy == PhysicMaterialCombine.Average ? Material.CombinePolicy.GeometricMean : Material.CombinePolicy.Minimum;
                    WheelCheck(output, "material-policy-" + policy, converted.Friction == .025f && converted.Restitution == .3f
                        && converted.FrictionCombinePolicy == expected && converted.RestitutionCombinePolicy == expected);
                }
                finally { UnityEngine.Object.DestroyImmediate(source); }
            }

            // Exercise the exact authored-collider creation path used by PartEntityFactory.
            foreach (var kind in new[] { typeof(UnityEngine.BoxCollider), typeof(UnityEngine.SphereCollider), typeof(UnityEngine.CapsuleCollider) })
            {
                var go = new GameObject("WheelMaterialQA");
                var source = SourceWheelMaterial(.025f);
                BlobAssetReference<Collider> blob = default;
                try
                {
                    var collider = (UnityEngine.Collider)go.AddComponent(kind);
                    collider.sharedMaterial = source;
                    blob = PartPhysics.CreateCollider(collider);
                    WheelCheck(output, "factory-" + kind.Name, blob.Value.GetFriction() == .025f && blob.Value.GetRestitution() == 0f);
                    var scaled = PartPhysics.CreateScaled(blob, new float2(.7f, 1.2f));
                    try { WheelCheck(output, "scaled-" + kind.Name, scaled.IsCreated && scaled.Value.GetFriction() == .025f); }
                    finally { if (scaled.IsCreated) scaled.Dispose(); }
                }
                finally
                {
                    if (blob.IsCreated) blob.Dispose();
                    UnityEngine.Object.DestroyImmediate(go);
                    UnityEngine.Object.DestroyImmediate(source);
                }
            }

            var treadSource = SourceWheelMaterial(.025f);
            var supportSource = new PhysicMaterial { dynamicFriction = .7f, frictionCombine = PhysicMaterialCombine.Average };
            var tread = PartPhysics.CreateSphere(float3.zero, .45f, PartPhysics.ConvertMaterial(treadSource));
            var support = PartPhysics.CreateBox(new float3(0f, .3f, 0f), new float3(.8f, .2f, 1f), PartPhysics.ConvertMaterial(supportSource));
            BlobAssetReference<Collider> compound = default;
            try
            {
                using (var children = new NativeArray<CompoundCollider.ColliderBlobInstance>(2, Allocator.Temp))
                {
                    var writable = children;
                    writable[0] = new CompoundCollider.ColliderBlobInstance { Collider = tread, CompoundFromChild = RigidTransform.identity };
                    writable[1] = new CompoundCollider.ColliderBlobInstance { Collider = support, CompoundFromChild = RigidTransform.identity };
                    compound = CompoundCollider.Create(children);
                }
                var ptr = (CompoundCollider*)compound.GetUnsafePtr();
                WheelCheck(output, "compound-tread-support", ptr->Children[0].Collider->GetFriction() == .025f
                    && ptr->Children[1].Collider->GetFriction() == .7f);
                var scaled = PartPhysics.CreateScaled(compound, new float2(.5f));
                try
                {
                    var scaledPtr = (CompoundCollider*)scaled.GetUnsafePtr();
                    WheelCheck(output, "scaled-compound-tread-support", scaledPtr->Children[0].Collider->GetFriction() == .025f
                        && scaledPtr->Children[1].Collider->GetFriction() == .7f);
                }
                finally { scaled.Dispose(); }
                var low = PartPhysics.ConvertMaterial(treadSource);
                WheelCheck(output, "authored-average-does-not-override-tread", Material.GetCombinedFriction(low,
                    PartPhysics.ConvertMaterial(supportSource)) == .025f);
                supportSource.dynamicFriction = 0f;
                WheelCheck(output, "zero-friction-surface", Material.GetCombinedFriction(low,
                    PartPhysics.ConvertMaterial(supportSource)) == 0f);
            }
            finally
            {
                if (compound.IsCreated) compound.Dispose();
                tread.Dispose(); support.Dispose();
                UnityEngine.Object.DestroyImmediate(treadSource); UnityEngine.Object.DestroyImmediate(supportSource);
            }
        }

        static void CoastingContract(List<Measurement> output, SimulationType backend, float friction)
        {
            string name = friction < 0f ? "default-friction-control" : "tread-" + friction.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
            var source = SourceWheelMaterial(friction < 0f ? .025f : friction);
            try
            {
                using (var f = new Fixture(backend, 1, statics: 2))
                {
                    f.Gravity = new float3(0f, -9.81f, 0f);
                    Material material = friction < 0f ? Material.Default : PartPhysics.ConvertMaterial(source);
                    f.Static(0, new float3(0f, -.5f, 0f), f.Box(new float3(200f, 1f, 2f)));
                    f.Body(0, new float3(0f, .46f, 0f), shape: f.Sphere(float3.zero, .45f, material));
                    var velocity = f.World.MotionVelocities[0];
                    // In-game WheelPivot rotation is visual: the assembled collider has no free axle.
                    velocity.InverseInertia = float3.zero;
                    f.MotionVelocity(0, velocity);
                    for (int i = 0; i < 40; ++i) f.Step();
                    f.LinearVelocity(0, new float3(5f, 0f, 0f)); // one push, never upload another force/velocity
                    float last = 5f, gain = 0f;
                    for (int i = 0; i < 150; ++i)
                    {
                        f.Step();
                        float speed = math.abs(f.Velocity(0).x);
                        gain = math.max(gain, speed - last);
                        last = speed;
                    }
                    float x = f.Position(0).x, speedAfter = f.Velocity(0).x;
                    Add(output, backend, name, "speed-gain", "m/s", gain, .002f);
                    Add(output, backend, name, "ground-height-error", "m", math.abs(f.Position(0).y - .45f), .06f);
                    Add(output, backend, name, "coasting-failure", "count", friction < 0f
                        ? (math.abs(speedAfter) < .5f ? 0 : 1) : (speedAfter > 3.3f && speedAfter <= 5.002f ? 0 : 1), 0);
                    // Raw values are deliberately retained for the independent A/A checker.
                    Add(output, backend, name, "final-speed", "m/s", math.abs(speedAfter), 5.002f);
                    Add(output, backend, name, "distance", "m", math.abs(x), 15.1f);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }
    }
}
#endif
