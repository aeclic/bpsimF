#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Bpsim.Physics.Anvil;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;
using Collider = Unity.Physics.Collider;
using CompoundCollider = Unity.Physics.CompoundCollider;
using Joint = Unity.Physics.Joint;
using MTransform = Unity.Physics.Math.MTransform;
using RaycastHit = Unity.Physics.RaycastHit;
using SphereCollider = Unity.Physics.SphereCollider;
using FloatRange = Unity.Physics.Math.FloatRange;

namespace Bpsim.QA
{
    /// <summary>Deterministic, scene-independent contract tests for the three physics backends.</summary>
    public static partial class AnvilParityQA
    {
        [Serializable]
        public sealed class Measurement
        {
            public string backend;
            public string scenario;
            public string metric;
            public string unit;
            public float value;
            public float tolerance;
            public bool pass;
            public bool referenceOnly;
            public string detail;
        }

        [Serializable]
        public sealed class Report
        {
            public string label;
            public string runId;
            public string collectedAtUtc;
            public string unityVersion;
            public string fingerprint;
            public string physicsAssemblySha256;
            public string havokApi;
            public bool pass;
            public int failures;
            public int referenceDeviations;
            public Measurement[] measurements;
        }

        const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly FieldInfo ConstraintBlockField = typeof(PhysicsJoint).GetField("m_Constraints", InstanceMembers);
        static string s_HavokApi;

        public static string Run(string label)
        {
            if (string.IsNullOrEmpty(label) || label.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || label != Path.GetFileName(label))
                throw new ArgumentException("Supply a simple report label", nameof(label));

            var measurements = new List<Measurement>();
            foreach (SimulationType backend in new[]
                     { SimulationType.UnityPhysics, SimulationType.HavokPhysics, SimulationType.AnvilPhysics })
            {
                try
                {
                    Debug.Log("[AnvilParity] start " + backend);
                    SparseUpload(measurements, backend);
                    SmallRepeatedImpulse(measurements, backend);
                    RotatedSpin(measurements, backend);
                    OffsetCentreOfMass(measurements, backend);
                    GravityFactors(measurements, backend);
                    ChangedMass(measurements, backend);
                    WeldedChain(measurements, backend);
                    JointMappings(measurements, backend);
                    SignedHingeLimits(measurements, backend);
                    WrappedHingeLimits(measurements, backend);
                    ReplacedBody(measurements, backend);
                    ScaledGeometry(measurements, backend);
                    BodyIdentityAfterGrowth(measurements, backend);
                    MovedStatic(measurements, backend);
                    ConnectionThresholds(measurements, backend);
                    DampingAndBodyType(measurements, backend);
                    HighSpeed(measurements, backend);
                    GeometryContracts(measurements, backend);
                    MassRatioAndTorque(measurements, backend);
                    ImpactContracts(measurements, backend);
                    ThresholdGrid(measurements, backend);
                    if (backend == SimulationType.AnvilPhysics) NativeContactAndSleep(measurements);
                }
                catch (Exception exception)
                {
                    Add(measurements, backend, "instrument", "exception", "count", 1f, 0f, exception.ToString());
                }
            }

            int failures = measurements.FindAll(m => !m.pass && !m.referenceOnly).Count;
            int referenceDeviations = measurements.FindAll(m => !m.pass && m.referenceOnly).Count;
            var report = new Report
            {
                label = label,
                runId = Guid.NewGuid().ToString("N"),
                collectedAtUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                fingerprint = Fingerprint(),
                physicsAssemblySha256 = FileHash(typeof(PhysicsWorld).Assembly.Location),
                havokApi = s_HavokApi,
                pass = failures == 0,
                failures = failures,
                referenceDeviations = referenceDeviations,
                measurements = measurements.ToArray()
            };
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/anvil-parity-" + label + ".json"));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonUtility.ToJson(report, true), new System.Text.UTF8Encoding(false));
            int required = measurements.FindAll(m => !m.referenceOnly).Count;
            string summary = $"[AnvilParity] {label}: {(report.pass ? "PASS" : "FAIL")} required={required - failures}/{required}; referenceDeviations={referenceDeviations}; {output}";
            Debug.Log(summary);
            return summary;
        }

        public static void RunBatch()
        {
            string label = "batch";
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; ++i)
                if (args[i] == "-anvil-parity-label") label = args[i + 1];
            Run(label);
        }

        static string FileHash(string path)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }

        static string Fingerprint()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            using (var hash = SHA256.Create())
            using (var bytes = new MemoryStream())
            {
                foreach (string relative in new[]
                         {
                             "Packages/com.unity.physics/Unity.Physics/Anvil/AnvilWorld.cs",
                             "Packages/com.unity.physics/Unity.Physics/Anvil/AnvilStepSystem.cs",
                             "Packages/com.unity.physics/Unity.Physics/Anvil/AnvilNative.cs",
                             "Assets/Plugins/x86_64/" + AnvilNative.Library + ".dll",
                             "Assets/Plugins/Havok.Physics.dll",
                             "Assets/Plugins/x86_64/HavokNative.dll",
                             "Assets/Scripts/Assembly-CSharp/Bpsim/QA/AnvilParityQA.cs"
                         })
                {
                    byte[] content = File.ReadAllBytes(Path.Combine(root, relative));
                    bytes.Write(content, 0, content.Length);
                }
                return BitConverter.ToString(hash.ComputeHash(bytes.ToArray())).Replace("-", "").ToLowerInvariant();
            }
        }

        static void Add(List<Measurement> output, SimulationType backend, string scenario, string metric,
            string unit, float error, float tolerance, string detail = null, bool referenceOnly = false)
        {
            output.Add(new Measurement
            {
                backend = backend.ToString(), scenario = scenario, metric = metric, unit = unit,
                value = math.isfinite(error) ? error : float.MaxValue,
                tolerance = tolerance, pass = math.isfinite(error) && error <= tolerance, detail = detail, referenceOnly = referenceOnly
            });
        }

        static void SparseUpload(List<Measurement> output, SimulationType backend)
        {
            using (var f = new Fixture(backend, 5))
            {
                for (int i = 0; i < 5; ++i) f.Body(i, new float3(i * 3f, 0f, 0f));
                f.Step();
                float3 input = new float3(4f, 1f, 0f);
                f.LinearVelocity(3, input);
                f.Step();
                Add(output, backend, "sparse-upload", "target-velocity-error", "m/s", math.distance(f.Velocity(3), input), 1e-4f);
                float passive = 0f;
                for (int i = 0; i < 5; ++i) if (i != 3) passive = math.max(passive, math.length(f.Velocity(i)));
                Add(output, backend, "sparse-upload", "untargeted-velocity", "m/s", passive, 1e-4f);
            }
        }

        static void RotatedSpin(List<Measurement> output, SimulationType backend)
        {
            using (var f = new Fixture(backend, 1))
            {
                f.Body(0, new float3(2f, 0f, 0f), rotation: quaternion.EulerXYZ(0.7f, -0.4f, 1.1f));
                var md = f.Motion(0);
                md.BodyFromMotion.rot = quaternion.EulerXYZ(0.3f, 0.4f, 0.5f);
                md.WorldFromMotion = math.mul(f.World.Bodies[0].WorldFromBody, md.BodyFromMotion);
                f.Motion(0, md);
                float3 omega = new float3(0f, 0f, 4f);
                f.AngularVelocity(0, omega);
                for (int i = 0; i < 20; ++i) f.Step();
                Add(output, backend, "rotated-spin", "world-omega-error", "rad/s", math.distance(f.Omega(0), omega), 1e-3f);
                Add(output, backend, "rotated-spin", "spurious-linear-velocity", "m/s", math.length(f.Velocity(0)), 1e-4f);
            }
        }

        static void SmallRepeatedImpulse(List<Measurement> output, SimulationType backend)
        {
            using (var f = new Fixture(backend, 3))
            {
                for (int i = 0; i < 3; ++i) f.Body(i, new float3(i * 3f, 0f, 0f));
                f.Step();
                for (int i = 0; i < 60; ++i)
                {
                    f.LinearVelocity(2, f.Velocity(2) + new float3(0.0005f, 0f, 0f));
                    f.Step();
                }
                Add(output, backend, "small-impulses", "accumulated-velocity-error", "m/s", math.abs(f.Velocity(2).x - 0.03f), 1e-4f,
                    "Actual velocity=" + f.Velocity(2) + "; shipped Havok deadband is a recorded reference deviation (F114), not successful conservation.",
                    referenceOnly: backend == SimulationType.HavokPhysics);
                Add(output, backend, "small-impulses", "untargeted-velocity", "m/s", math.max(math.length(f.Velocity(0)), math.length(f.Velocity(1))), 1e-4f);
            }
        }

        static void OffsetCentreOfMass(List<Measurement> output, SimulationType backend)
        {
            using (var f = new Fixture(backend, 1))
            {
                var shape = f.Sphere(new float3(0.7f, -0.2f, 0.1f));
                f.Body(0, new float3(2f, 0f, 0f), shape: shape);
                float3 initial = f.Motion(0).WorldFromMotion.pos;
                float3 velocity = new float3(1f, -0.5f, 0.25f);
                f.LinearVelocity(0, velocity);
                f.AngularVelocity(0, new float3(0f, 0f, 4f));
                for (int i = 0; i < 40; ++i) f.Step();
                float3 expected = initial + velocity * (40f * f.TimeStep);
                Add(output, backend, "offset-com", "com-trajectory-error", "m", math.distance(f.Motion(0).WorldFromMotion.pos, expected), 2e-4f);
                Add(output, backend, "offset-com", "linear-momentum-error", "kg m/s", math.distance(f.Momentum(), velocity), 1e-4f);
            }
        }

        static void GravityFactors(List<Measurement> output, SimulationType backend)
        {
            using (var f = new Fixture(backend, 5))
            {
                float[] factors = { 0f, 0.25f, 1f, 2f, -0.5f };
                f.Gravity = new float3(0f, -9.81f, 0f);
                for (int i = 0; i < factors.Length; ++i)
                {
                    f.Body(i, new float3(i * 3f, 0f, 0f));
                    var velocity = f.World.MotionVelocities[i];
                    velocity.GravityFactor = factors[i];
                    f.MotionVelocity(i, velocity);
                }
                for (int step = 0; step < 3; ++step) f.Step();
                for (int i = 0; i < factors.Length; ++i)
                    Add(output, backend, "gravity", "factor-" + i, "m/s",
                        math.distance(f.Velocity(i), f.Gravity * (f.TimeStep * 3f * factors[i])),
                        backend == SimulationType.HavokPhysics ? 0.02f : 1e-4f,
                        "actual=" + f.Velocity(i) + "; expected=" + f.Gravity * (f.TimeStep * 3f * factors[i]));
            }
        }

        static void ChangedMass(List<Measurement> output, SimulationType backend)
        {
            using (var f = new Fixture(backend, 2, 1))
            {
                f.Body(0, float3.zero);
                f.Body(1, new float3(2f, 0f, 0f));
                f.Weld(0, 0, 1);
                f.Step();
                var mv = f.World.MotionVelocities[1];
                mv.InverseMass /= 9f;
                mv.InverseInertia /= 9f;
                f.MotionVelocity(1, mv);
                f.LinearVelocity(0, new float3(10f, 0f, 0f));
                float3 initialMomentum = f.Momentum();
                for (int i = 0; i < 10; ++i) f.Step();
                Add(output, backend, "mass-change", "momentum-relative-error", "ratio",
                    math.distance(f.Momentum(), initialMomentum) / math.length(initialMomentum), 0.005f);
                Add(output, backend, "mass-change", "shared-velocity-error", "m/s",
                    math.max(math.distance(f.Velocity(0), new float3(1f, 0f, 0f)),
                        math.distance(f.Velocity(1), new float3(1f, 0f, 0f))), 0.005f);
            }
        }

        static void WeldedChain(List<Measurement> output, SimulationType backend)
        {
            using (var f = new Fixture(backend, 4, 3))
            {
                for (int i = 0; i < 4; ++i) f.Body(i, new float3(i, 0f, 0f));
                for (int i = 0; i < 3; ++i) f.Weld(i, i, i + 1);
                f.LinearVelocity(3, new float3(4f, 0f, 0f));
                float3 momentum = f.Momentum();
                float energy = f.Energy();
                float maximumEnergy = energy, drift = 0f;
                for (int step = 0; step < 60; ++step)
                {
                    f.Step();
                    maximumEnergy = math.max(maximumEnergy, f.Energy());
                    for (int i = 0; i < 3; ++i)
                        drift = math.max(drift, math.abs(math.distance(f.Position(i), f.Position(i + 1)) - 1f));
                }
                Add(output, backend, "welded-chain", "momentum-relative-error", "ratio", math.distance(f.Momentum(), momentum) / math.length(momentum), 0.005f);
                Add(output, backend, "welded-chain", "energy-gain", "ratio", math.max(0f, maximumEnergy / energy - 1f), 0.01f);
                Add(output, backend, "welded-chain", "max-connection-drift", "m", drift, 0.02f);
            }
        }

        static void JointMappings(List<Measurement> output, SimulationType backend)
        {
            using (var f = new Fixture(backend, 2, 1))
            {
                f.Body(0, float3.zero);
                f.Body(1, new float3(1f, 0f, 0f));
                var frameA = new BodyFrame
                {
                    Position = new float3(0.5f, 0f, 0f),
                    Axis = new float3(1f, 0f, 0f),
                    PerpendicularAxis = new float3(0f, 1f, 0f)
                };
                var frameB = new BodyFrame
                {
                    Position = new float3(-0.5f, 0f, 0f),
                    Axis = new float3(1f, 0f, 0f),
                    PerpendicularAxis = new float3(0f, 1f, 0f)
                };
                f.SetJoint(0, 0, 1, PhysicsJoint.CreateLimitedHinge(frameA, frameB, new FloatRange(-0.35f, 0.35f)));
                f.AngularVelocity(1, new float3(1f, 0f, 0f));
                float anchorDrift = 0f;
                for (int step = 0; step < 12; step++)
                {
                    f.Step();
                    anchorDrift = math.max(anchorDrift, f.AnchorDrift());
                }
                float relativeSpeed = math.dot(f.Omega(1) - f.Omega(0), new float3(1f, 0f, 0f));
                Add(output, backend, "joint-mapping", "limited-hinge-anchor-drift", "m", anchorDrift, 0.03f);
                Add(output, backend, "joint-mapping", "limited-hinge-axis-speed-error", "rad/s", math.abs(relativeSpeed - 1f), 0.08f);
                f.AngularVelocity(0, float3.zero);
                f.AngularVelocity(1, new float3(8f, 0f, 0f));
                for (int step = 0; step < 12; step++) f.Step();
                quaternion relativeRotation = math.mul(math.inverse(f.Rotation(0)), f.Rotation(1));
                float hingeAngle = 2f * math.atan2(relativeRotation.value.x, relativeRotation.value.w);
                Add(output, backend, "joint-mapping", "limited-hinge-limit-excess", "rad", math.max(0f, math.abs(hingeAngle) - 0.35f), 0.06f);
            }

            using (var f = new Fixture(backend, 2, 1))
            {
                f.Body(0, float3.zero);
                f.Body(1, new float3(1f, 0f, 0f));
                f.SetJoint(0, 0, 1, PhysicsJoint.CreateLimitedDistance(float3.zero, float3.zero, new FloatRange(0f, 1.5f)));
                f.LinearVelocity(1, new float3(6f, 0f, 0f));
                for (int step = 0; step < 24; step++) f.Step();
                float distance = math.distance(f.Position(0), f.Position(1));
                Add(output, backend, "joint-mapping", "limited-distance-upper-limit-excess", "m", math.max(0f, distance - 1.5f), 0.08f);
            }

            using (var f = new Fixture(backend, 2, 1))
            {
                f.Body(0, float3.zero);
                f.Body(1, new float3(1f, 0f, 0f));
                var frameA = new BodyFrame
                {
                    Position = new float3(0.5f, 0f, 0f),
                    Axis = new float3(1f, 0f, 0f),
                    PerpendicularAxis = new float3(0f, 1f, 0f)
                };
                var frameB = new BodyFrame
                {
                    Position = new float3(-0.5f, 0f, 0f),
                    Axis = new float3(1f, 0f, 0f),
                    PerpendicularAxis = new float3(0f, 1f, 0f)
                };
                f.SetJoint(0, 0, 1, PhysicsJoint.CreatePrismatic(frameA, frameB, new FloatRange(-0.5f, 0.5f)));
                f.LinearVelocity(1, new float3(0f, 2f, 0f));
                f.AngularVelocity(1, new float3(0f, 1f, 0f));
                for (int step = 0; step < 20; step++) f.Step();
                float3 separation = math.rotate(math.inverse(f.Rotation(0)), f.Position(1) - f.Position(0));
                Add(output, backend, "joint-mapping", "prismatic-perpendicular-drift", "m",
                    math.length(new float2(separation.y, separation.z)), 0.05f);
                Add(output, backend, "joint-mapping", "prismatic-relative-angular-speed", "rad/s",
                    math.length(f.Omega(1) - f.Omega(0)), 0.08f);
            }
        }

        static void SignedHingeLimits(List<Measurement> output, SimulationType backend)
        {
            const float halfWidth = math.PI / 12f;
            const float frameOffset = 0.19f;
            foreach (float centerSign in new[] { -1f, 1f })
            foreach (float edgeSign in new[] { -1f, 1f })
            {
                using (var fixture = new Fixture(backend, 2, 1))
                {
                    float center = centerSign * math.PI / 2f;
                    float edge = center + edgeSign * halfWidth;
                    string scenario = "signed-hinge-" + (centerSign < 0f ? "negative-" : "positive-")
                        + (edgeSign < 0f ? "lower" : "upper");
                    var frameA = new BodyFrame(new RigidTransform(quaternion.identity, new float3(0.5f, 0f, 0f)));
                    var frameB = new BodyFrame(new RigidTransform(quaternion.RotateX(frameOffset), new float3(-0.5f, 0f, 0f)));
                    fixture.Body(0, float3.zero);
                    fixture.Body(1, new float3(1f, 0f, 0f), rotation: quaternion.RotateX(-center - frameOffset));
                    fixture.SetJoint(0, 0, 1, PhysicsJoint.CreateLimitedHinge(frameA, frameB, new FloatRange(center - halfWidth, center + halfWidth)));

                    float TwistFromB()
                    {
                        quaternion worldFromA = math.mul(fixture.Rotation(0), frameA.AsRigidTransform().rot);
                        quaternion worldFromB = math.mul(fixture.Rotation(1), frameB.AsRigidTransform().rot);
                        quaternion relative = math.mul(math.inverse(worldFromB), worldFromA);
                        float angle = 2f * math.atan2(relative.value.x, relative.value.w);
                        return angle - 2f * math.PI * math.round((angle - center) / (2f * math.PI));
                    }

                    float restError = 0f;
                    for (int step = 0; step < 6; step++)
                    {
                        fixture.Step();
                        restError = math.max(restError, math.abs(TwistFromB() - center));
                    }
                    Add(output, backend, scenario, "rest-angle-error", "rad", restError, 0.01f);
                    for (int step = 0; step < 36; step++)
                    {
                        fixture.AngularVelocity(0, float3.zero);
                        fixture.AngularVelocity(1, new float3(-1.5f * edgeSign, 0f, 0f));
                        fixture.Step();
                    }
                    Add(output, backend, scenario, "driven-limit-error", "rad", math.abs(TwistFromB() - edge), 0.06f);
                }
            }
        }

        static void WrappedHingeLimits(List<Measurement> output, SimulationType backend)
        {
            const float halfWidth = math.PI / 12f;
            foreach (float centerSign in new[] { -1f, 1f })
            {
                using (var fixture = new Fixture(backend, 2, 1))
                {
                    float center = centerSign * math.PI / 2f;
                    float start = center + centerSign * (math.PI - 0.35f);
                    var frameA = new BodyFrame(new RigidTransform(quaternion.identity, new float3(0.5f, 0f, 0f)));
                    var frameB = new BodyFrame(new RigidTransform(quaternion.identity, new float3(-0.5f, 0f, 0f)));
                    fixture.Body(0, float3.zero);
                    quaternion initial = quaternion.RotateX(start);
                    fixture.Body(1, new float3(1f, 0f, 0f), rotation: math.inverse(initial));
                    fixture.SetJoint(0, 0, 1, PhysicsJoint.CreateLimitedHinge(frameA, frameB, new FloatRange(center - halfWidth, center + halfWidth)));

                    fixture.Step();
                    quaternion relative = math.mul(math.inverse(fixture.Rotation(1)), fixture.Rotation(0));
                    quaternion delta = math.mul(math.inverse(initial), relative);
                    float twist = 2f * math.atan2(delta.value.x, delta.value.w);
                    twist -= 2f * math.PI * math.round(twist / (2f * math.PI));
                    string scenario = centerSign < 0f ? "signed-hinge-negative-wrap" : "signed-hinge-positive-wrap";
                    Add(output, backend, scenario, "wrong-direction-correction", "rad", math.max(0f, centerSign * twist + 0.01f), 0f);
                }
            }
        }

        static void ReplacedBody(List<Measurement> output, SimulationType backend)
        {
            using (var f = new Fixture(backend, 2, 1))
            {
                f.Body(0, float3.zero);
                f.Body(1, new float3(2f, 0f, 0f));
                f.Weld(0, 0, 1);
                f.Step();
                var bodies = f.World.Bodies;
                var body = bodies[0];
                body.Collider = f.Sphere(float3.zero, 0.2f);
                bodies[0] = body;
                f.LinearVelocity(0, new float3(8f, 0f, 0f));
                for (int i = 0; i < 5; ++i) f.Step();
                Add(output, backend, "body-replacement", "shared-velocity-error", "m/s",
                    math.max(math.abs(f.Velocity(0).x - 4f), math.abs(f.Velocity(1).x - 4f)), 0.02f);
                Add(output, backend, "body-replacement", "connection-drift", "m", math.abs(math.distance(f.Position(0), f.Position(1)) - 2f), 0.02f);
            }
        }

        static void ConnectionThresholds(List<Measurement> output, SimulationType backend)
        {
            Threshold(output, backend, "linear-below", new float3(1.2f, 0f, 0f), float3.zero, new float3(0.8f), new float3(float.PositiveInfinity), false);
            Threshold(output, backend, "linear-above", new float3(2f, 0f, 0f), float3.zero, new float3(0.8f), new float3(float.PositiveInfinity), true);
            Threshold(output, backend, "diagonal-below", new float3(1.4f, 1.4f, 0f), float3.zero, new float3(0.8f), new float3(float.PositiveInfinity), false);
            Threshold(output, backend, "zero-unloaded", float3.zero, float3.zero, float3.zero, new float3(float.PositiveInfinity), false);
            Threshold(output, backend, "zero-loaded", new float3(0.1f, 0f, 0f), float3.zero, float3.zero, new float3(float.PositiveInfinity), true);
            Threshold(output, backend, "angular-above", float3.zero, new float3(0f, 0f, 2f), new float3(float.PositiveInfinity), new float3(0.02f), true);
            Threshold(output, backend, "angular-below", float3.zero, new float3(0f, 0f, 2f), new float3(float.PositiveInfinity), new float3(0.06f), false);
        }

        static void ScaledGeometry(List<Measurement> output, SimulationType backend)
        {
            using (var f = new Fixture(backend, 1))
            {
                f.Body(0, float3.zero, shape: f.Compound(f.Sphere(float3.zero), new float3(1f, 0f, 0f)));
                f.Scale(0, 2f);
                f.Step();
                bool hit = f.Ray(new float3(2f, 3f, 0f), new float3(0f, -6f, 0f), out var result);
                Add(output, backend, "scaled-compound", "missing-hit", "count", hit ? 0f : 1f, 0f);
                if (hit) Add(output, backend, "scaled-compound", "surface-height-error", "m", math.abs(result.Position.y - 0.5f), 1e-4f);
            }
            using (var f = new Fixture(backend, 2, 1))
            {
                f.Body(0, float3.zero);
                f.Body(1, new float3(2f, 0f, 0f));
                f.Scale(0, 2f);
                f.Weld(0, 0, 1);
                for (int i = 0; i < 5; ++i) f.Step();
                Add(output, backend, "scaled-joint", "rest-distance-error", "m", math.abs(math.distance(f.Position(0), f.Position(1)) - 2f), 1e-4f);
                Add(output, backend, "scaled-joint", "spurious-velocity", "m/s", math.max(math.length(f.Velocity(0)), math.length(f.Velocity(1))), 1e-4f);
            }
        }

        static void BodyIdentityAfterGrowth(List<Measurement> output, SimulationType backend)
        {
            using (var f = new Fixture(backend, 2))
            {
                f.Body(0, float3.zero);
                f.Body(1, new float3(3f, 0f, 0f));
                f.Step();
                f.World.Reset(1, 3, 0);
                for (int i = 0; i < 3; ++i) f.Body(i, new float3(i * 3f, 0f, 0f));
                f.DefaultStatic();
                f.Step();
                bool hit = f.Ray(new float3(6f, 3f, 0f), new float3(0f, -6f, 0f), out var result);
                Add(output, backend, "body-identity", "missing-new-body-hit", "count", hit ? 0f : 1f, 0f);
                Add(output, backend, "body-identity", "wrong-hit-entity", "count", hit && result.Entity == f.World.Bodies[2].Entity ? 0f : 1f, 0f);
            }
        }

        static void MovedStatic(List<Measurement> output, SimulationType backend)
        {
            using (var f = new Fixture(backend, 1, 0, 2))
            {
                f.Body(0, new float3(8f, 0f, 0f));
                var shape = f.Box(new float3(4f, 1f, 2f));
                f.Static(0, float3.zero, shape);
                f.Step();
                f.Static(0, new float3(0f, 3f, 0f), shape);
                f.Step();
                bool hit = f.Ray(new float3(0f, 10f, 0f), new float3(0f, -15f, 0f), out var result);
                Add(output, backend, "moved-static", "missing-floor-hit", "count", hit ? 0f : 1f, 0f);
                Add(output, backend, "moved-static", "floor-height-error", "m", hit ? math.abs(result.Position.y - 3.5f) : 15f, 1e-4f);
            }
        }

        static void Threshold(List<Measurement> output, SimulationType backend, string name, float3 linear,
            float3 angular, float3 linearLimit, float3 angularLimit, bool expected, float timeStep = 0.02f, int subSteps = 1)
        {
            using (var f = new Fixture(backend, 2, 1))
            {
                f.TimeStep = timeStep;
                f.SubSteps = subSteps;
                f.Body(0, float3.zero);
                f.Body(1, float3.zero);
                f.Weld(0, 0, 1, linearLimit, angularLimit);
                f.LinearVelocity(0, linear);
                f.AngularVelocity(0, angular);
                f.Step();
                Add(output, backend, "threshold-" + name, "event-mismatch", "count",
                    f.HadImpulse == expected ? 0f : 1f, 0f, "expected=" + expected + "; actual=" + f.HadImpulse);
            }
        }

        static void DampingAndBodyType(List<Measurement> output, SimulationType backend)
        {
            foreach (float rate in new[] { 0.3f, 4f, 60f })
            {
                using (var f = new Fixture(backend, 1))
                {
                    f.Body(0, float3.zero);
                    f.Gravity = new float3(0f, -3f, 0f);
                    var md = f.Motion(0);
                    md.LinearDamping = rate;
                    md.AngularDamping = rate;
                    f.Motion(0, md);
                    float3 velocity = new float3(2f, 0.5f, 0f), position = float3.zero;
                    float omega = 2f;
                    f.LinearVelocity(0, velocity);
                    f.AngularVelocity(0, new float3(0f, 0f, omega));
                    float decay = math.clamp(1f - rate * f.TimeStep, 0f, 1f);
                    for (int i = 0; i < 12; ++i)
                    {
                        velocity += f.Gravity * f.TimeStep;
                        position += velocity * f.TimeStep;
                        velocity *= decay;
                        omega *= decay;
                        f.Step();
                    }
                    string name = "damping-" + rate.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    float velocityTolerance = backend == SimulationType.HavokPhysics ? 0.1f : 2e-4f;
                    Add(output, backend, name, "linear-velocity-error", "m/s", math.distance(f.Velocity(0), velocity), velocityTolerance);
                    Add(output, backend, name, "world-omega-error", "rad/s", math.distance(f.Omega(0), new float3(0f, 0f, omega)), velocityTolerance);
                    Add(output, backend, name, "com-position-error", "m", math.distance(f.Motion(0).WorldFromMotion.pos, position), backend == SimulationType.HavokPhysics ? 0.06f : 5e-4f);
                }
            }
            using (var f = new Fixture(backend, 1))
            {
                f.Body(0, float3.zero);
                f.Gravity = new float3(0f, -9.81f, 0f);
                var dynamic = f.World.MotionVelocities[0];
                for (int i = 0; i < 10; ++i) f.Step();
                float3 frozenPosition = f.Motion(0).WorldFromMotion.pos;
                var frozen = dynamic;
                frozen.InverseMass = 0f;
                frozen.InverseInertia = float3.zero;
                frozen.GravityFactor = 0f;
                f.MotionVelocity(0, frozen);
                for (int i = 0; i < 5; ++i) f.Step();
                Add(output, backend, "body-type-change", "frozen-motion", "m", math.distance(f.Motion(0).WorldFromMotion.pos, frozenPosition), 1e-4f);
                f.MotionVelocity(0, dynamic);
                f.Step();
                Add(output, backend, "body-type-change", "unfrozen-gravity-error", "m/s", math.distance(f.Velocity(0), f.Gravity * f.TimeStep), backend == SimulationType.HavokPhysics ? 0.02f : 1e-4f);
            }
            using (var f = new Fixture(backend, 2, 1))
            {
                f.Body(0, float3.zero);
                f.Body(1, float3.zero);
                var mv = f.World.MotionVelocities[0];
                mv.InverseInertia.x = mv.InverseInertia.y = 0f;
                f.MotionVelocity(0, mv);
                f.Weld(0, 0, 1);
                f.AngularVelocity(0, new float3(0f, 0f, 2f));
                for (int i = 0; i < 10; ++i) f.Step();
                Add(output, backend, "partial-infinite-inertia", "free-axis-shared-omega-error", "rad/s",
                    math.max(math.abs(f.Omega(0).z - 1f), math.abs(f.Omega(1).z - 1f)), 1e-3f);
                Add(output, backend, "partial-infinite-inertia", "unforced-axis-rotation", "rad/s",
                    math.max(math.length(f.Omega(0).xy), math.length(f.Omega(1).xy)), 1e-4f);
            }
        }

        static void HighSpeed(List<Measurement> output, SimulationType backend)
        {
            using (var f = new Fixture(backend, 1))
            {
                f.Body(0, float3.zero);
                f.LinearVelocity(0, new float3(500f, 0f, 0f));
                f.AngularVelocity(0, new float3(0f, 0f, 100f));
                float energy = f.Energy();
                f.Step();
                Add(output, backend, "high-speed", "linear-speed-loss", "m/s", math.distance(f.Velocity(0), new float3(500f, 0f, 0f)), 1e-3f);
                Add(output, backend, "high-speed", "angular-speed-loss", "rad/s", math.distance(f.Omega(0), new float3(0f, 0f, 100f)), 1e-3f);
                Add(output, backend, "high-speed", "energy-gain", "ratio", math.max(0f, f.Energy() / energy - 1f), 0.001f);
            }
        }

        static void GeometryContracts(List<Measurement> output, SimulationType backend)
        {
            using (var f = new Fixture(backend, 1))
            {
                var leaf = f.Sphere(new float3(0.2f, 0f, 0f));
                var inner = f.Compound(leaf, new float3(0.4f, 0f, 0f));
                f.Body(0, float3.zero, shape: f.Compound(inner, new float3(0.3f, 0f, 0f)));
                f.Scale(0, 2f);
                f.Step();
                bool hit = f.Ray(new float3(1.8f, 3f, 0f), new float3(0f, -6f, 0f), out var result);
                Add(output, backend, "nested-compound", "surface-error", "m", hit ? math.abs(result.Position.y - 0.5f) : 6f, 1e-4f);
            }
            using (var f = new Fixture(backend, 1))
            {
                f.Body(0, float3.zero, shape: f.Convex());
                f.Step();
                bool hit = f.Ray(new float3(3f, 0f, 0f), new float3(-6f, 0f, 0f), out var result);
                Add(output, backend, "convex-blob", "surface-error", "m", hit ? math.abs(result.Position.x - 0.4f) : 6f, 1e-4f);
            }
            using (var f = new Fixture(backend, 1))
            {
                f.Body(0, float3.zero, shape: f.Cylinder());
                f.Step();
                bool hit = f.Ray(new float3(3f, 0f, 0f), new float3(-6f, 0f, 0f), out var result);
                Add(output, backend, "cylinder", "surface-error", "m", hit ? math.abs(result.Position.x - 0.25f) : 6f, 1e-4f);
            }
            using (var f = new Fixture(backend, 1, 0, 2))
            {
                f.Body(0, new float3(8f, 0f, 0f));
                f.Static(0, new float3(0f, 1f, 0f), f.MeshPlane());
                f.Step();
                bool hit = f.Ray(new float3(0f, 4f, 0f), new float3(0f, -6f, 0f), out var result);
                Add(output, backend, "static-mesh", "surface-error", "m", hit ? math.abs(result.Position.y - 1f) : 6f, 1e-4f);
            }
            using (var f = new Fixture(backend, 1))
            {
                var sphere = f.Sphere(float3.zero);
                f.Body(0, float3.zero, shape: f.Compound(new[] { sphere, sphere }, new[] { float3.zero, new float3(0f, 1f, 0f) }));
                f.Step();
                bool hit = f.Ray(new float3(0f, 3f, 0f), new float3(0f, -6f, 0f), out var result);
                Add(output, backend, "closest-compound-hit", "surface-error", "m", hit ? math.abs(result.Position.y - 1.25f) : 6f, 1e-4f);
            }
        }

        static void MassRatioAndTorque(List<Measurement> output, SimulationType backend)
        {
            using (var f = new Fixture(backend, 2, 1))
            {
                f.Body(0, float3.zero, mass: 0.25f);
                f.Body(1, new float3(2f, 0f, 0f), mass: 25f);
                f.Weld(0, 0, 1);
                f.LinearVelocity(0, new float3(8f, 0f, 0f));
                float3 momentum = f.Momentum();
                for (int i = 0; i < 20; ++i) f.Step();
                float expected = 2f / 25.25f;
                Add(output, backend, "mass-ratio-100", "momentum-relative-error", "ratio", math.distance(f.Momentum(), momentum) / math.length(momentum), 0.005f,
                    "Havok low-signal assembly motion loss is recorded separately (F121).", referenceOnly: backend == SimulationType.HavokPhysics);
                Add(output, backend, "mass-ratio-100", "shared-speed-error", "m/s", math.max(math.abs(f.Velocity(0).x - expected), math.abs(f.Velocity(1).x - expected)), 1e-3f,
                    referenceOnly: backend == SimulationType.HavokPhysics);
            }
            using (var f = new Fixture(backend, 2, 1))
            {
                f.Body(0, float3.zero, mass: 0.25f);
                f.Body(1, new float3(2f, 0f, 0f), mass: 25f);
                f.Weld(0, 0, 1);
                f.LinearVelocity(0, new float3(80f, 0f, 0f));
                float3 momentum = f.Momentum();
                for (int i = 0; i < 20; ++i) f.Step();
                float expected = 20f / 25.25f;
                Add(output, backend, "mass-ratio-100-high-signal", "momentum-relative-error", "ratio", math.distance(f.Momentum(), momentum) / math.length(momentum), 0.005f);
                Add(output, backend, "mass-ratio-100-high-signal", "shared-speed-error", "m/s", math.max(math.abs(f.Velocity(0).x - expected), math.abs(f.Velocity(1).x - expected)), 0.005f);
            }
            using (var f = new Fixture(backend, 4, 3))
            {
                for (int i = 0; i < 4; ++i) f.Body(i, new float3(i - 1.5f, 0f, 0f));
                for (int i = 0; i < 3; ++i) f.Weld(i, i, i + 1);
                f.LinearVelocity(3, new float3(0f, 3f, 0f));
                float3 momentum = f.Momentum(), angularMomentum = f.AngularMomentum();
                float energy = f.Energy(), maximumEnergy = energy, drift = 0f;
                for (int i = 0; i < 60; ++i)
                {
                    f.Step();
                    maximumEnergy = math.max(maximumEnergy, f.Energy());
                    drift = math.max(drift, f.AnchorDrift());
                }
                Add(output, backend, "off-centre-chain", "momentum-relative-error", "ratio", math.distance(f.Momentum(), momentum) / math.length(momentum), 0.005f);
                Add(output, backend, "off-centre-chain", "angular-momentum-relative-error", "ratio", math.distance(f.AngularMomentum(), angularMomentum) / math.length(angularMomentum), 0.02f);
                Add(output, backend, "off-centre-chain", "energy-gain", "ratio", math.max(0f, maximumEnergy / energy - 1f), 0.01f);
                Add(output, backend, "off-centre-chain", "max-anchor-drift", "m", drift, 0.04f);
            }
        }

        static void ImpactContracts(List<Measurement> output, SimulationType backend)
        {
            foreach (Unity.Physics.Material.CombinePolicy policy in Enum.GetValues(typeof(Unity.Physics.Material.CombinePolicy)))
            {
                using (var f = new Fixture(backend, 2))
                {
                    var materialA = Unity.Physics.Material.Default;
                    materialA.Friction = 0f;
                    materialA.Restitution = 0.09f;
                    materialA.RestitutionCombinePolicy = policy;
                    var materialB = materialA;
                    materialB.Restitution = 1f;
                    f.Body(0, new float3(-1f, 0f, 0f), shape: f.Sphere(float3.zero, material: materialA));
                    f.Body(1, new float3(1f, 0f, 0f), shape: f.Sphere(float3.zero, material: materialB));
                    f.LinearVelocity(0, new float3(8f, 0f, 0f));
                    float3 momentum = f.Momentum();
                    float energy = f.Energy(), maxEnergy = energy;
                    for (int i = 0; i < 40; ++i) { f.Step(); maxEnergy = math.max(maxEnergy, f.Energy()); }
                    float expected = Unity.Physics.Material.GetCombinedRestitution(materialA, materialB);
                    float actual = (f.Velocity(1).x - f.Velocity(0).x) / 8f;
                    string name = "restitution-" + policy;
                    Add(output, backend, name, "coefficient-error", "ratio", math.abs(actual - expected), 0.04f, "actual=" + actual + "; expected=" + expected);
                    Add(output, backend, name, "momentum-relative-error", "ratio", math.distance(f.Momentum(), momentum) / math.length(momentum), 0.005f,
                        "Shipped Havok fully elastic collision momentum loss is a visible reference deviation (F121).",
                        referenceOnly: backend == SimulationType.HavokPhysics && policy == Unity.Physics.Material.CombinePolicy.Maximum);
                    Add(output, backend, name, "energy-gain", "ratio", math.max(0f, maxEnergy / energy - 1f), 0.03f);
                }
            }
            using (var f = new Fixture(backend, 2))
            {
                var material = Unity.Physics.Material.Default;
                material.CollisionResponse = CollisionResponsePolicy.None;
                f.Body(0, new float3(-1f, 0f, 0f), shape: f.Sphere(float3.zero, material: material));
                f.Body(1, new float3(1f, 0f, 0f));
                f.LinearVelocity(0, new float3(8f, 0f, 0f));
                for (int i = 0; i < 20; ++i) f.Step();
                Add(output, backend, "no-collision-response", "unwanted-impulse", "m/s", math.max(math.abs(f.Velocity(0).x - 8f), math.length(f.Velocity(1))), 1e-4f);
            }
            using (var f = new Fixture(backend, 4, 4, 2))
            {
                f.Gravity = new float3(0f, -9.81f, 0f);
                f.Static(0, new float3(0f, -0.5f, 0f), f.Box(new float3(20f, 1f, 20f)));
                for (int i = 0; i < 4; ++i) f.Body(i, new float3((i % 2) * 0.8f - 0.4f, 2f + (i / 2) * 0.8f, 0f));
                f.Weld(0, 0, 1); f.Weld(1, 0, 2); f.Weld(2, 1, 3); f.Weld(3, 2, 3);
                float energy = f.Energy() + f.PotentialEnergy(), maximum = energy, drift = 0f;
                float minHeight = float.MaxValue;
                for (int i = 0; i < 240; ++i)
                {
                    f.Step();
                    maximum = math.max(maximum, f.Energy() + f.PotentialEnergy());
                    drift = math.max(drift, f.AnchorDrift());
                    for (int body = 0; body < 4; ++body) minHeight = math.min(minHeight, f.Position(body).y);
                }
                Add(output, backend, "assembly-drop", "total-energy-gain", "ratio", math.max(0f, maximum / energy - 1f), 0.03f);
                Add(output, backend, "assembly-drop", "max-anchor-drift", "m", drift, 0.04f);
                Add(output, backend, "assembly-drop", "below-floor-error", "m", math.max(0f, 0.15f - minHeight), 0f, "minimum centre height=" + minHeight);
            }
        }

        static void ThresholdGrid(List<Measurement> output, SimulationType backend)
        {
            foreach (float dt in new[] { 0.01f, 0.02f, 0.04f })
                foreach (int sub in new[] { 1, 2, 4 })
                {
                    string tag = "dt" + dt.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-sub" + sub;
                    Threshold(output, backend, "grid-below-" + tag, new float3(1.2f, 0f, 0f), float3.zero,
                        new float3(0.8f), new float3(float.PositiveInfinity), false, dt, sub);
                    Threshold(output, backend, "grid-above-" + tag, new float3(2f, 0f, 0f), float3.zero,
                        new float3(0.8f), new float3(float.PositiveInfinity), true, dt, sub);
                }
        }

        static void NativeContactAndSleep(List<Measurement> output)
        {
            if (AnvilWorld.SupportsCollisionTags)
            {
                NativeCollisionTags(output);
                NativeTeleport(output);
            }
            foreach (int sub in new[] { 1, 2, 4 })
            {
                using (var f = new Fixture(SimulationType.AnvilPhysics, 1, 0, 2))
                {
                    f.SubSteps = sub;
                    f.Static(0, new float3(0f, -0.5f, 0f), f.Box(new float3(10f, 1f, 10f)));
                    var material = Unity.Physics.Material.Default;
                    material.CollisionResponse = CollisionResponsePolicy.CollideRaiseCollisionEvents;
                    f.Body(0, new float3(0f, 0.25f, 0f), shape: f.Sphere(float3.zero, material: material));
                    f.LinearVelocity(0, new float3(0f, -4f, 0f));
                    f.Step();
                    float reaction = f.Velocity(0).y + 4f;
                    Add(output, SimulationType.AnvilPhysics, "contact-sum-sub" + sub, "impulse-accounting-error", "N s", math.abs(f.NativeContactImpulse() - reaction), 0.005f);
                }
                using (var f = new Fixture(SimulationType.AnvilPhysics, 1, 0, 2))
                {
                    f.SubSteps = sub;
                    f.Static(0, new float3(0f, -0.5f, 0f), f.Box(new float3(10f, 1f, 10f)));
                    f.Body(0, new float3(0f, 0.249f, 0f), shape: f.Box(new float3(0.5f)));
                    f.LinearVelocity(0, new float3(0f, -4f, 0f));
                    f.Step();
                    float reaction = f.Velocity(0).y + 4f;
                    float reported = f.NativeContactImpulse();
                    Add(output, SimulationType.AnvilPhysics, "contact-box-sub" + sub, "impulse-accounting-error", "N s",
                        math.abs(reported - reaction), 0.005f, $"measured {reported:F4}, momentum change {reaction:F4}");
                    Add(output, SimulationType.AnvilPhysics, "contact-box-sub" + sub, "minimum-reaction-shortfall", "N s",
                        math.max(0f, 1f - reaction), 0f);
                }
            }
            using (var f = new Fixture(SimulationType.AnvilPhysics, 2))
            {
                f.SubSteps = 8;
                f.Body(0, new float3(-0.7f, 0f, 0f));
                f.Body(1, new float3(0.7f, 0f, 0f));
                f.LinearVelocity(0, new float3(65f, 0f, 0f));
                f.LinearVelocity(1, new float3(-65f, 0f, 0f));
                f.Step();
                Add(output, SimulationType.AnvilPhysics, "swept-dynamic-pair", "crossed-centers", "m",
                    math.max(0f, f.Motion(0).WorldFromMotion.pos.x - f.Motion(1).WorldFromMotion.pos.x), 0f);
                Add(output, SimulationType.AnvilPhysics, "swept-dynamic-pair", "missed-impact", "m/s",
                    math.max(0f, f.Velocity(0).x - 1f), 0f);
            }
            using (var f = new Fixture(SimulationType.AnvilPhysics, 1, 0, 2))
            {
                f.SubSteps = 8;
                f.Static(0, new float3(0f, -0.5f, 0f), f.Box(new float3(10f, 1f, 10f)));
                f.Body(0, new float3(0f, 0.91f, 0f));
                f.LinearVelocity(0, new float3(0f, -60f, 0f));
                f.Step();
                Add(output, SimulationType.AnvilPhysics, "swept-static-contact", "missing-reaction", "N s",
                    math.max(0f, 1f - f.NativeContactImpulse()), 0f);
                Add(output, SimulationType.AnvilPhysics, "swept-static-contact", "penetration", "m",
                    math.max(0f, 0.25f - f.Motion(0).WorldFromMotion.pos.y), 0.025f);
            }
            using (var f = new Fixture(SimulationType.AnvilPhysics, 2, 1))
            {
                f.SubSteps = 8;
                f.Body(0, new float3(-0.5f, 0f, 0f));
                f.Body(1, new float3(0.5f, 0f, 0f));
                f.Weld(0, 0, 1);
                f.Step();
                Add(output, SimulationType.AnvilPhysics, "slow-weld-path", "wrong-step-path", "count",
                    f.UsedIntegratedSubsteps == (AnvilWorld.NativeBridgeVersion == 3) ? 0f : 1f, 0f);
            }
            using (var f = new Fixture(SimulationType.AnvilPhysics, 1, 0, 2, sleep: true))
            {
                f.Gravity = new float3(0f, -9.81f, 0f);
                f.Static(0, new float3(0f, -0.5f, 0f), f.Box(new float3(10f, 1f, 10f)));
                f.Body(0, new float3(0f, 0.25f, 0f));
                var md = f.Motion(0); md.LinearDamping = md.AngularDamping = 0.2f; f.Motion(0, md);
                for (int i = 0; i < 240; ++i) f.Step();
                Add(output, SimulationType.AnvilPhysics, "sleep-wake", "not-sleeping", "count", f.NativeAwake() ? 1f : 0f, 0f);
                Add(output, SimulationType.AnvilPhysics, "sleep-wake", "stale-sleeping-velocity", "m/s", math.length(f.Velocity(0)), 1e-4f);
                f.LinearVelocity(0, new float3(1f, 0f, 0f)); f.Step();
                Add(output, SimulationType.AnvilPhysics, "sleep-wake", "failed-wakeup", "count", f.NativeAwake() ? 0f : 1f, 0f);
                Add(output, SimulationType.AnvilPhysics, "sleep-wake", "missing-wakeup-response", "m/s", math.max(0f, 0.5f - f.Velocity(0).x), 0f);
            }
        }

        static void NativeTeleport(List<Measurement> output)
        {
            const SimulationType backend = SimulationType.AnvilPhysics;
            using (var f = new Fixture(backend, 2))
            {
                f.SubSteps = 1;
                f.Body(0, new float3(-20f, 0f, 0f));
                f.Body(1, float3.zero);
                f.Step();
                var motion = f.Motion(0);
                motion.WorldFromMotion.pos.x = -0.49f;
                f.Motion(0, motion);
                f.LinearVelocity(0, new float3(5f, 0f, 0f));
                f.Step();
                Add(output, backend, "dynamic-teleport", "lost-pose", "m",
                    math.max(0f, math.abs(f.Position(0).x + 0.49f) - 0.1f), 0f);
                Add(output, backend, "dynamic-teleport", "late-contact", "m/s",
                    math.max(0f, 0.5f - f.Velocity(1).x), 0f);
            }
        }

        static void NativeCollisionTags(List<Measurement> output)
        {
            const SimulationType backend = SimulationType.AnvilPhysics;
            using (var f = new Fixture(backend, 1, statics: 2))
            {
                f.SubSteps = 8;
                f.Static(0, new float3(0f, -0.5f, 0f), f.Box(new float3(10f, 1f, 10f)));
                f.Body(0, new float3(0f, 0.24f, 0f));
                f.Gravity = new float3(0f, -9.81f, 0f);
                int enclosingGroup = 0;
                byte layer = 1;
                f.CollisionTags = entity => entity.Index == 1 ? new AnvilCollisionTag(19, 1, true)
                    : new AnvilCollisionTag(enclosingGroup, layer, false);
                f.Step();
                Add(output, backend, "collision-tags", "same-layer-wheel-impulse-missing", "N s",
                    math.max(0f, 0.05f - f.NativeContactImpulse()), 0f);
                enclosingGroup = 19;
                f.Step();
                Add(output, backend, "collision-tags", "enclosing-frame-impulse", "N s", f.NativeContactImpulse(), 0f);
                enclosingGroup = 0;
                layer = 2;
                f.Step();
                Add(output, backend, "collision-tags", "other-layer-impulse", "N s", f.NativeContactImpulse(), 0f);
                layer = 1;
                f.Step();
                Add(output, backend, "collision-tags", "stationary-pair-did-not-refilter", "N s",
                    math.max(0f, 0.05f - f.NativeContactImpulse()), 0f);
            }

            using (var f = new Fixture(backend, 1, statics: 2))
            {
                f.SubSteps = 8;
                f.Static(0, new float3(0f, -0.5f, 0f), f.Box(new float3(10f, 1f, 10f)));
                f.Body(0, new float3(0f, 0.91f, 0f));
                f.LinearVelocity(0, new float3(0f, -60f, 0f));
                f.CollisionTags = entity => new AnvilCollisionTag(0, entity.Index == 1 ? (byte)1 : (byte)2, entity.Index == 1);
                f.Step();
                Add(output, backend, "collision-tags", "ccd-rejected-filtered-pair", "m",
                    math.abs(f.Position(0).y + 0.29f), 0.01f);
            }
        }

        sealed partial class Fixture : IDisposable
        {
            public PhysicsWorld World;
            public float TimeStep = 0.02f;
            public float3 Gravity;
            public bool HadImpulse;
            public int SolverIterations = 8;
            public int SubSteps { set { if (m_Native != null) m_Native.SubStepCount = value; } }
            public bool UsedIntegratedSubsteps => m_Native != null && m_Native.UsedIntegratedSubsteps;
            public Func<Entity, AnvilCollisionTag> CollisionTags;
            readonly ISimulation m_Reference;
            readonly AnvilWorld m_Native;
            readonly NativeReference<int> m_StaticChanged;
            readonly NativeList<Entity> m_Events;
            readonly List<BlobAssetReference<Collider>> m_Shapes = new List<BlobAssetReference<Collider>>();
            readonly BlobAssetReference<Collider> m_DefaultShape;

            public Fixture(SimulationType backend, int bodies, int joints = 0, int statics = 1, bool sleep = false)
            {
                World = new PhysicsWorld(statics, bodies, joints);
                m_StaticChanged = new NativeReference<int>(Allocator.Persistent);
                m_StaticChanged.Value = 1;
                m_Events = new NativeList<Entity>(Allocator.Persistent);
                m_DefaultShape = Sphere(float3.zero);
                DefaultStatic();
                try
                {
                    if (backend == SimulationType.AnvilPhysics)
                    {
                        m_Native = new AnvilWorld { EnableSleep = sleep };
                        m_Native.Create();
                    }
                    else if (backend == SimulationType.UnityPhysics)
                        m_Reference = Simulation.Create();
                    else
                        m_Reference = CreateHavok();
                }
                catch { Dispose(); throw; }
            }

            public BlobAssetReference<Collider> Sphere(float3 centre, float radius = 0.25f, Unity.Physics.Material? material = null)
            {
                var shape = SphereCollider.Create(new SphereGeometry { Center = centre, Radius = radius }, CollisionFilter.Default, material ?? Unity.Physics.Material.Default);
                m_Shapes.Add(shape);
                return shape;
            }

            public BlobAssetReference<Collider> Box(float3 size)
            {
                var shape = Unity.Physics.BoxCollider.Create(new BoxGeometry { Size = size, Orientation = quaternion.identity });
                m_Shapes.Add(shape);
                return shape;
            }

            public BlobAssetReference<Collider> Convex()
            {
                var points = new float3[8];
                for (int i = 0; i < 8; ++i) points[i] = new float3((i & 1) == 0 ? -0.4f : 0.4f,
                    (i & 2) == 0 ? -0.2f : 0.2f, (i & 4) == 0 ? -0.1f : 0.1f);
                using (var vertices = new NativeArray<float3>(points, Allocator.Temp))
                {
                    var parameters = ConvexHullGenerationParameters.Default;
                    parameters.BevelRadius = 0f;
                    var shape = ConvexCollider.Create(vertices, parameters);
                    m_Shapes.Add(shape);
                    return shape;
                }
            }

            public BlobAssetReference<Collider> Cylinder()
            {
                var shape = CylinderCollider.Create(new CylinderGeometry
                { Center = float3.zero, Orientation = quaternion.identity, Radius = 0.25f, Height = 1f, SideCount = 16, BevelRadius = 0f });
                m_Shapes.Add(shape);
                return shape;
            }

            public BlobAssetReference<Collider> MeshPlane()
            {
                using (var vertices = new NativeArray<float3>(new[]
                       { new float3(-2f, 0f, -2f), new float3(-2f, 0f, 2f), new float3(2f, 0f, 2f), new float3(2f, 0f, -2f) }, Allocator.Temp))
                using (var triangles = new NativeArray<int3>(new[] { new int3(0, 1, 2), new int3(0, 2, 3) }, Allocator.Temp))
                {
                    var shape = Unity.Physics.MeshCollider.Create(vertices, triangles);
                    m_Shapes.Add(shape);
                    return shape;
                }
            }

            public BlobAssetReference<Collider> Compound(BlobAssetReference<Collider>[] colliders, float3[] offsets)
            {
                using (var children = new NativeArray<CompoundCollider.ColliderBlobInstance>(colliders.Length, Allocator.Temp))
                {
                    var writable = children;
                    for (int i = 0; i < colliders.Length; ++i)
                        writable[i] = new CompoundCollider.ColliderBlobInstance
                        { Collider = colliders[i], CompoundFromChild = new RigidTransform(quaternion.identity, offsets[i]) };
                    var shape = CompoundCollider.Create(children);
                    m_Shapes.Add(shape);
                    return shape;
                }
            }

            public BlobAssetReference<Collider> Compound(BlobAssetReference<Collider> child, float3 offset)
            {
                using (var children = new NativeArray<CompoundCollider.ColliderBlobInstance>(1, Allocator.Temp))
                {
                    var writableChildren = children;
                    writableChildren[0] = new CompoundCollider.ColliderBlobInstance
                    { Collider = child, CompoundFromChild = new RigidTransform(quaternion.identity, offset) };
                    var shape = CompoundCollider.Create(children);
                    m_Shapes.Add(shape);
                    return shape;
                }
            }

            public void DefaultStatic()
            {
                var bodies = World.Bodies;
                bodies[World.NumBodies - 1] = new RigidBody { WorldFromBody = RigidTransform.identity, Scale = 1f, Entity = Entity.Null };
            }

            public void Static(int index, float3 position, BlobAssetReference<Collider> shape)
            {
                var bodies = World.StaticBodies;
                bodies[index] = new RigidBody
                { WorldFromBody = new RigidTransform(quaternion.identity, position), Scale = 1f, Collider = shape,
                    Entity = new Entity { Index = 500 + index, Version = 1 } };
            }

            public void Scale(int index, float scale)
            {
                var bodies = World.Bodies;
                var body = bodies[index];
                float ratio = scale / body.Scale;
                body.Scale = scale;
                bodies[index] = body;
                var motion = Motion(index);
                motion.BodyFromMotion.pos *= ratio;
                motion.WorldFromMotion = math.mul(body.WorldFromBody, motion.BodyFromMotion);
                Motion(index, motion);
                var mv = World.MotionVelocities[index];
                mv.InverseInertia /= ratio * ratio;
                MotionVelocity(index, mv);
            }

            public void Body(int index, float3 position, float mass = 1f, quaternion rotation = default,
                BlobAssetReference<Collider> shape = default)
            {
                if (math.lengthsq(rotation.value) == 0f) rotation = quaternion.identity;
                if (!shape.IsCreated) shape = m_DefaultShape;
                var physicsMass = PhysicsMass.CreateDynamic(shape.Value.MassProperties, mass);
                RigidTransform transform = new RigidTransform(rotation, position);
                RigidTransform bodyFromMotion = new RigidTransform(physicsMass.InertiaOrientation, physicsMass.CenterOfMass);
                var bodies = World.Bodies;
                bodies[index] = new RigidBody
                {
                    Entity = new Entity { Index = index + 1, Version = 1 }, Collider = shape,
                    WorldFromBody = transform, Scale = 1f
                };
                Motion(index, new MotionData { BodyFromMotion = bodyFromMotion, WorldFromMotion = math.mul(transform, bodyFromMotion) });
                MotionVelocity(index, new MotionVelocity
                {
                    InverseMass = physicsMass.InverseMass, InverseInertia = physicsMass.InverseInertia,
                    AngularExpansionFactor = physicsMass.AngularExpansionFactor, GravityFactor = 1f
                });
            }

            public void Weld(int index, int a, int b, float3? linear = null, float3? angular = null)
            {
                float3 anchor = (Position(a) + Position(b)) * 0.5f;
                RigidTransform transformA = World.Bodies[a].WorldFromBody;
                RigidTransform transformB = World.Bodies[b].WorldFromBody;
                var joint = PhysicsJoint.CreateFixed(
                    new BodyFrame(new RigidTransform(math.inverse(transformA.rot), math.transform(math.inverse(transformA), anchor))),
                    new BodyFrame(new RigidTransform(math.inverse(transformB.rot), math.transform(math.inverse(transformB), anchor))));
                joint.SetImpulseEventThresholdAllConstraints(linear ?? new float3(float.PositiveInfinity), angular ?? new float3(float.PositiveInfinity));
                var joints = World.Joints;
                joints[index] = new Joint
                {
                    BodyPair = new BodyIndexPair { BodyIndexA = a, BodyIndexB = b },
                    AFromJoint = new MTransform(joint.BodyAFromJoint.AsRigidTransform()),
                    BFromJoint = new MTransform(joint.BodyBFromJoint.AsRigidTransform()),
                    Constraints = (ConstraintBlock3)ConstraintBlockField.GetValue(joint),
                    Entity = new Entity { Index = 10000 + index, Version = 1 }, Version = joint.Version,
                    EnableCollision = 0
                };
            }

            public void SetJoint(int index, int a, int b, PhysicsJoint physicsJoint)
            {
                var joints = World.Joints;
                joints[index] = new Joint
                {
                    BodyPair = new BodyIndexPair { BodyIndexA = a, BodyIndexB = b },
                    AFromJoint = new MTransform(physicsJoint.BodyAFromJoint.AsRigidTransform()),
                    BFromJoint = new MTransform(physicsJoint.BodyBFromJoint.AsRigidTransform()),
                    Constraints = (ConstraintBlock3)ConstraintBlockField.GetValue(physicsJoint),
                    Entity = new Entity { Index = 20000 + index, Version = 1 },
                    Version = physicsJoint.Version,
                    EnableCollision = 0
                };
            }

            public MotionData Motion(int index) => World.MotionDatas[index];
            public void Motion(int index, MotionData data) { var motions = World.MotionDatas; motions[index] = data; }
            public void MotionVelocity(int index, MotionVelocity data) { var velocities = World.MotionVelocities; velocities[index] = data; }
            public float3 Velocity(int index) => World.MotionVelocities[index].LinearVelocity;
            public float3 Omega(int index) => math.rotate(Motion(index).WorldFromMotion.rot, World.MotionVelocities[index].AngularVelocity);
            public float3 Position(int index) => math.mul(Motion(index).WorldFromMotion, math.inverse(Motion(index).BodyFromMotion)).pos;
            public quaternion Rotation(int index) => math.mul(Motion(index).WorldFromMotion.rot, math.inverse(Motion(index).BodyFromMotion.rot));
            public void LinearVelocity(int index, float3 value) { var mv = World.MotionVelocities[index]; mv.LinearVelocity = value; MotionVelocity(index, mv); }
            public void AngularVelocity(int index, float3 worldValue)
            {
                var mv = World.MotionVelocities[index];
                mv.AngularVelocity = math.rotate(math.inverse(Motion(index).WorldFromMotion.rot), worldValue);
                MotionVelocity(index, mv);
            }

            public float3 Momentum()
            {
                float3 result = float3.zero;
                for (int i = 0; i < World.NumDynamicBodies; ++i)
                    if (World.MotionVelocities[i].InverseMass > 0f) result += Velocity(i) / World.MotionVelocities[i].InverseMass;
                return result;
            }

            public float Energy()
            {
                float result = 0f;
                for (int i = 0; i < World.NumDynamicBodies; ++i)
                {
                    var mv = World.MotionVelocities[i];
                    if (mv.InverseMass > 0f) result += 0.5f * math.lengthsq(mv.LinearVelocity) / mv.InverseMass;
                    float3 inertia = math.select(float3.zero, math.rcp(mv.InverseInertia), mv.InverseInertia > 0f);
                    result += 0.5f * math.dot(mv.AngularVelocity * mv.AngularVelocity, inertia);
                }
                return result;
            }

            public float PotentialEnergy()
            {
                float result = 0f;
                for (int i = 0; i < World.NumDynamicBodies; ++i)
                    if (World.MotionVelocities[i].InverseMass > 0f)
                        result -= math.dot(Gravity, Motion(i).WorldFromMotion.pos) / World.MotionVelocities[i].InverseMass;
                return result;
            }

            public float3 AngularMomentum()
            {
                float3 result = float3.zero;
                for (int i = 0; i < World.NumDynamicBodies; ++i)
                {
                    var mv = World.MotionVelocities[i];
                    if (mv.InverseMass > 0f) result += math.cross(Motion(i).WorldFromMotion.pos, mv.LinearVelocity / mv.InverseMass);
                    float3 inertia = math.select(float3.zero, math.rcp(mv.InverseInertia), mv.InverseInertia > 0f);
                    result += math.rotate(Motion(i).WorldFromMotion.rot, inertia * mv.AngularVelocity);
                }
                return result;
            }

            public float AnchorDrift()
            {
                float maximum = 0f;
                foreach (Joint joint in World.Joints)
                {
                    if (joint.BodyPair.BodyIndexA < 0 || joint.BodyPair.BodyIndexB < 0) continue;
                    int a = joint.BodyPair.BodyIndexA, b = joint.BodyPair.BodyIndexB;
                    RigidTransform transformA = math.mul(Motion(a).WorldFromMotion, math.inverse(Motion(a).BodyFromMotion));
                    RigidTransform transformB = math.mul(Motion(b).WorldFromMotion, math.inverse(Motion(b).BodyFromMotion));
                    maximum = math.max(maximum, math.distance(math.transform(transformA, joint.AFromJoint.Translation),
                        math.transform(transformB, joint.BFromJoint.Translation)));
                }
                return maximum;
            }

            public float NativeContactImpulse()
            {
                using (var contacts = new NativeList<AnvilContactEvent>(Allocator.Temp))
                {
                    m_Native.CollectContactEvents(contacts, 256);
                    float result = 0f;
                    foreach (var contact in contacts) result += contact.Impulse;
                    return result;
                }
            }

            public bool NativeAwake()
            {
                var ids = (NativeArray<b3BodyId>)typeof(AnvilWorld).GetField("m_MotionBodies", InstanceMembers).GetValue(m_Native);
                return AnvilNative.b3Body_IsAwake(ids[0]) != 0;
            }

            public bool Ray(float3 origin, float3 translation, out RaycastHit hit)
            {
                hit = default;
                if (m_Native != null)
                {
                    using (var results = new NativeList<RaycastHit>(Allocator.Temp))
                    {
                        m_Native.CastRayAll(results, origin, translation);
                        if (results.Length == 0) return false;
                        hit = results[0];
                        return true;
                    }
                }
                World.CollisionWorld.UpdateDynamicTree(ref World, TimeStep, Gravity);
                return World.CastRay(new RaycastInput
                { Start = origin, End = origin + translation, Filter = CollisionFilter.Default }, out hit);
            }

            public void Step()
            {
                var bodies = World.Bodies;
                for (int i = 0; i < World.NumDynamicBodies; ++i)
                {
                    var body = bodies[i];
                    body.WorldFromBody = math.mul(Motion(i).WorldFromMotion, math.inverse(Motion(i).BodyFromMotion));
                    bodies[i] = body;
                }
                World.UpdateIndexMaps();
                HadImpulse = false;
                if (m_Native != null)
                {
                    m_Native.SetGravity(Gravity);
                    m_Native.SyncToNative(ref World, CollisionTags);
                    m_Native.SyncJointsToNative(ref World, TimeStep);
                    m_Native.Step(TimeStep);
                    m_Native.SyncFromNative(ref World);
                    HadImpulse = m_Native.CollectJointEvents(m_Events) > 0;
                }
                else
                {
                    World.CollisionWorld.BuildBroadphase(ref World, TimeStep, Gravity);
                    var input = new SimulationStepInput
                    {
                        World = World, TimeStep = TimeStep, Gravity = Gravity, NumSolverIterations = SolverIterations,
                        HaveStaticBodiesChanged = m_StaticChanged.AsReadOnly(), SynchronizeCollisionWorld = false
                    };
                    m_Reference.ScheduleStepJobs(input, default, false);
                    m_Reference.FinalJobHandle.Complete();
                    HadImpulse = HasImpulseEvents(m_Reference);
                }
            }

            public void Dispose()
            {
                m_Reference?.FinalJobHandle.Complete();
                m_Reference?.Dispose();
                m_Native?.Dispose();
                World.Dispose();
                if (m_Events.IsCreated) m_Events.Dispose();
                if (m_StaticChanged.IsCreated) m_StaticChanged.Dispose();
                foreach (var collider in m_Shapes) if (collider.IsCreated) collider.Dispose();
                m_Shapes.Clear();
            }
        }

        static ISimulation CreateHavok()
        {
            var type = Assembly.Load("Havok.Physics").GetType("Havok.Physics.HavokSimulation", true);
            foreach (var method in type.GetMethods(StaticMembers))
            {
                if (method.Name != "Create" || !typeof(ISimulation).IsAssignableFrom(method.ReturnType)) continue;
                var arguments = DefaultArguments(method.GetParameters());
                if (arguments == null) continue;
                s_HavokApi = method + "; EnableSleeping=0";
                return (ISimulation)method.Invoke(null, arguments);
            }
            foreach (var constructor in type.GetConstructors(InstanceMembers))
            {
                var arguments = DefaultArguments(constructor.GetParameters());
                if (arguments == null) continue;
                s_HavokApi = constructor + "; EnableSleeping=0";
                return (ISimulation)constructor.Invoke(arguments);
            }
            throw new InvalidOperationException("No verified Havok simulation factory: " + type.FullName);
        }

        static object[] DefaultArguments(ParameterInfo[] parameters)
        {
            var arguments = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; ++i)
            {
                var type = parameters[i].ParameterType;
                var field = type.GetField("Default", StaticMembers);
                var property = type.GetProperty("Default", StaticMembers);
                if (field != null) arguments[i] = field.GetValue(null);
                else if (property != null) arguments[i] = property.GetValue(null);
                else if (parameters[i].HasDefaultValue) arguments[i] = parameters[i].DefaultValue;
                else return null;
                if (type.FullName == "Havok.Physics.HavokConfiguration")
                {
                    var sleeping = type.GetField("EnableSleeping", InstanceMembers);
                    if (sleeping == null) throw new InvalidOperationException("Cannot isolate Havok sleeping configuration");
                    sleeping.SetValue(arguments[i], Convert.ChangeType(0, sleeping.FieldType));
                }
            }
            return arguments;
        }

        static bool HasImpulseEvents(ISimulation simulation)
        {
            var property = simulation.GetType().GetProperty("ImpulseEvents", InstanceMembers);
            if (property == null) throw new InvalidOperationException("Reference backend does not expose impulse events: " + simulation.GetType());
            object events = property.GetValue(simulation);
            var enumeratorMethod = events.GetType().GetMethod("GetEnumerator", InstanceMembers);
            if (enumeratorMethod == null) throw new InvalidOperationException("Reference impulse events are not enumerable");
            object enumerator = enumeratorMethod.Invoke(events, null);
            return (bool)enumerator.GetType().GetMethod("MoveNext", InstanceMembers).Invoke(enumerator, null);
        }
    }
}
#endif
