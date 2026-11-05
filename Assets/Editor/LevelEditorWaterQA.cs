#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using Bpsim.Modding;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

namespace Bpsim.EditorTools
{
	public static class LevelEditorWaterQA
	{
		public static string RunMath()
		{
			JObject report = new JObject { ["passed"] = true };
			JArray names = new JArray();
			Action<bool, string> check = (condition, name) =>
			{
				if (!condition) throw new InvalidOperationException(name);
				names.Add(name);
			};
			try
			{
				var enumerator = new Unity.Entities.ChunkEntityEnumerator(false, default(Unity.Burst.Intrinsics.v128), 1);
				int firstEntity;
				report["importedChunkEnumeratorWorks"] = enumerator.NextEntityIndex(out firstEntity);
				ModFluidVolume water = new ModFluidVolume
				{
					Left = -10, Surface = 0, Width = 20, Depth = 20, Density = 1f,
					BuoyancyMultiplier = 1, LinearDrag = .5f, AngularDrag = 0,
					WaveLength = 2.5f, PhysicsEnabled = 1
				};
				NativeArray<ModFluidVolume> volumeValues = new NativeArray<ModFluidVolume>(1, Allocator.Temp);
				try
				{
					volumeValues[0] = water;
					check(volumeValues[0].PhysicsEnabled == 1 && Near(volumeValues[0].Left, -10), "Fluid parameters survive NativeArray transfer");
				}
				finally { volumeValues.Dispose(); }
				NativeArray<ModFluidContact> contactValues = new NativeArray<ModFluidContact>(1, Allocator.Temp);
				try
				{
					contactValues[0] = new ModFluidContact { Speed = 3, Position = new float2(2, 1), Entering = true };
					check(contactValues[0].Entering && Near(contactValues[0].Speed, 3), "Contact data survives NativeArray transfer");
				}
				finally { contactValues.Dispose(); }
				float2 center; float total;
				float area = ModFluidMath.SubmergedArea(float2.zero, new float2(1), quaternion.identity, -10, 10, -10, 0, out center, out total);
				check(Near(area, 2) && Near(total, 4) && Near(center.x, 0) && Near(center.y, -.5f), "Half-submerged area and center of buoyancy");
				area = ModFluidMath.SubmergedArea(float2.zero, new float2(1), quaternion.identity, 0, 10, -10, 0, out center, out total);
				check(Near(area, 1) && Near(center.x, .5f) && Near(center.y, -.5f), "Water side and bottom clipping");
				area = ModFluidMath.SubmergedArea(float2.zero, new float2(1), quaternion.RotateZ(math.PI / 4), -10, 10, -10, 0, out center, out total);
				check(Near(area, 2) && Near(total, 4) && math.all(math.isfinite(center)), "Rotated hull submersion");
				area = ModFluidMath.SubmergedArea(new float2(100000), new float2(1), quaternion.identity, 99990, 100010, 99990, 100000, out center, out total);
				check(Near(area, 2) && Near(total, 4) && Near(center.y, 99999.5f), "Large-coordinate area does not lose float precision");
				area = ModFluidMath.SubmergedArea(new float2(0, 3), new float2(1), quaternion.identity, -10, 10, -10, 0, out center, out total);
				check(Near(area, 0), "Dry hull has no displacement");
				area = ModFluidMath.SubmergedArea(new float2(0, -12), new float2(1), quaternion.identity, -10, 10, -10, 0, out center, out total);
				check(Near(area, 0), "Fluid has a finite bottom");
				check(Near(ModFluidMath.ShapeFill(new float3(2, 2, 1), 2), .5f) &&
					Near(ModFluidMath.ShapeFill(new float3(2, 2, 0), 0), 1), "Collider volume scales displaced area with a safe bounds fallback");
				ModFluidVolume wave = water; wave.WaveAmplitude = 1; wave.WaveLength = 4; wave.WaveSpeed = 0;
				float waveSurface = ModFluidMath.SurfaceAt(wave, -9, 0);
				float flatArea = ModFluidMath.SubmergedArea(float2.zero, new float2(.5f), quaternion.identity, -10, 10, -10, 0, out center, out total);
				float waveArea = ModFluidMath.SubmergedArea(float2.zero, new float2(.5f), quaternion.identity, -10, 10, -10, waveSurface, out center, out total);
				check(waveSurface > 0 && waveArea > flatArea, "Local wave height changes displaced area");
				PhysicsMass mass = PhysicsMass.CreateDynamic(MassProperties.UnitSphere, 1);
				LocalTransform transform = LocalTransform.FromPosition(float3.zero);
				PhysicsVelocity initial = default;
				PhysicsVelocity result = ModFluidMath.Apply(water, initial, mass, .5f, 1, new float2(0, -10), .02f);
				check(Near(result.Linear.y, .1f), "BPLE buoyancy impulse uses gravity and submerged bounds area");
				initial.Angular.z = .4f;
				result = ModFluidMath.Apply(water, initial, mass, .5f, 1, new float2(0, -10), .02f);
				check(Near(result.Angular.z, initial.Angular.z), "Central buoyancy adds no artificial part torque");
				result = ModFluidMath.Apply(water, default, mass, .5f, 1, new float2(0, -10), .02f,
					0, float2.zero, new float2(.5f, 0));
				check(result.Angular.z > 0, "Off-center buoyancy produces the expected restoring torque");
				ModFluidVolume noPhysics = water; noPhysics.PhysicsEnabled = 0;
				result = ModFluidMath.Apply(noPhysics, initial, mass, 1, 1, new float2(0, -10), .02f);
				check(math.lengthsq(result.Linear) == 0, "Physics opt-out produces no force");
				PhysicsMass fixedMass = mass; fixedMass.InverseMass = 0;
				result = ModFluidMath.Apply(water, initial, fixedMass, 1, 1, new float2(0, -10), .02f);
				check(math.lengthsq(result.Linear) == 0, "Kinematic parts are not moved");
				ModFluidVolume current = water; current.Density = 0; current.Flow = new float2(2, 0); current.LinearDrag = 100;
				initial.Linear.x = 10;
				result = ModFluidMath.Apply(current, initial, mass, 1, 1, float2.zero, 10);
				check(result.Linear.x >= 2 && result.Linear.x < 2.001f, "Strong drag remains stable without reversing relative velocity");
				initial = default;
				result = ModFluidMath.Apply(current, initial, mass, 1, 1, float2.zero, .02f);
				check(result.Linear.x > 0 && result.Linear.x < 2, "Water flow accelerates submerged parts");
				current.LinearDrag = 0; current.FlowAcceleration = 2;
				result = ModFluidMath.Apply(current, initial, mass, 1, 1, float2.zero, .02f);
				check(Near(result.Linear.x, .04f), "Explicit current acceleration works without drag");
				PhysicsVelocity horizontal = default; horizontal.Linear.x = 2;
				result = ModFluidMath.Apply(water, horizontal, mass, .5f, 1, float2.zero, .02f, .1f);
				check(Near(result.Linear.x, 2 * math.exp(-.5f * .5f * .02f)), "Partially submerged hull receives immersion-proportional horizontal drag");
				result = ModFluidMath.Apply(water, horizontal, mass, .5f, 1, float2.zero, .02f, -.1f);
				check(Near(result.Linear.x, 2 * math.exp(-.5f * .5f * .02f)), "Horizontal drag follows submerged area instead of a center threshold");
				PhysicsMass heavyMass = PhysicsMass.CreateDynamic(MassProperties.UnitSphere, 2);
				result = ModFluidMath.Apply(water, horizontal, heavyMass, .5f, 1, float2.zero, .02f, -.1f);
				check(Near(result.Linear.x, 2 * math.exp(-.5f * .5f * .02f / 2)), "Horizontal water drag scales with part mass");
				ModFluidVolume angularWater = water; angularWater.AngularDrag = 1.2f;
				initial.Angular.z = 2;
				result = ModFluidMath.Apply(angularWater, initial, mass, 1, 1, float2.zero, .1f, -1);
				check(result.Angular.z < 2 && result.Angular.z > 0, "Optional angular water resistance damps rotation");
				result = ModFluidMath.Apply(water, initial, mass, 1, 1, new float2(0, -10), 0);
				check(result.Linear.Equals(initial.Linear) && result.Angular.Equals(initial.Angular), "Paused timestep is a no-op");
				ModFluidVolume extreme = water; extreme.Density = 1000; extreme.BuoyancyMultiplier = 100;
				result = ModFluidMath.Apply(extreme, default, mass, 4, 4, new float2(0, -10), 1);
				check(math.all(math.isfinite(result.Linear)) && math.length(result.Linear) <= 20.001f && math.length(result.Angular) <= .001f,
					"Extreme density/force has bounded finite impulses");

				float y = .8f;
				PhysicsMass buoyantMass = PhysicsMass.CreateDynamic(MassProperties.UnitSphere, .6f);
				PhysicsVelocity floating = default;
				for (int i = 0; i < 3000; i++)
				{
					const float dt = 1f / 60;
					floating.Linear.y -= 10 * dt;
					area = ModFluidMath.SubmergedArea(new float2(0, y), new float2(.5f), quaternion.identity, -10, 10, -20, 0, out center, out total);
					floating = ModFluidMath.Apply(water, floating, buoyantMass, area, total, new float2(0, -10), dt, y);
					y += floating.Linear.y * dt;
				}
				check(y > -.25f && y < .05f && math.abs(floating.Linear.y) < .01f,
					"A lighter hull settles at BPLE's partial-displacement equilibrium");
				ModFluidVolume invalid = water; invalid.Flow.x = float.NaN;
				bool rejected = false;
				try { ModFluidMath.Validate(invalid); } catch (ArgumentException) { rejected = true; }
				check(rejected, "NaN force input is rejected before scheduling jobs");
			}
			catch (Exception ex) { report["passed"] = false; report["error"] = ex.ToString(); }
			report["checks"] = names.Count; report["names"] = names;
			string json = report.ToString(Formatting.Indented);
			string directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_codex_work", "level-editor-mod");
			Directory.CreateDirectory(directory);
			File.WriteAllText(Path.Combine(directory, "water-math-tests.json"), json, new UTF8Encoding(false));
			return json;
		}

		private static bool Near(float a, float b) { return math.abs(a - b) < .001f; }
	}
}
#endif
