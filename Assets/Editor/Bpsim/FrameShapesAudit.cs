using System;
using System.IO;
using System.Text;
using Bpsim.Parts;
using Bpsim.Parts.Simulation;
using Bpsim.Rendering;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEditor;
using UnityEngine;

namespace Bpsim.EditorTools
{
	public static class FrameShapesAudit
	{
		private static readonly StringBuilder Log = new();
		private static void Check(string name, bool pass)
		{
			Log.AppendLine((pass ? "PASS " : "FAIL ") + name);
			if (!pass) throw new Exception(name);
		}
		public static string Run()
		{
			if (EditorApplication.isPlaying) return "Run the isolated audit in edit mode.";
			Log.Clear();
			try
			{
				for (int shape = 0; shape <= 3; shape++)
				{
					var unit = new Schematics.Unit(1, 2, (int)PartType.MetalFrame, 132, 3, true, .2f, -.3f, 15, 2, .5f, 2, shape);
					Check("transform helpers preserve shape " + shape,
						unit.WithPosition(5, 6).WithPartType((int)PartType.WoodenFrame, 0).WithRotation(2, false).WithLayer(3).WithFreeTransform(1, 2, 30, 3, 4).Normalized().FrameShape == shape);
					foreach (var format in new[] { SchematicsFormat.Json, SchematicsFormat.Csv, SchematicsFormat.Xml, SchematicsFormat.EncryptedXml })
					{
						var data = new Schematics(); data.Units.Add(unit);
						using var stream = new MemoryStream();
						var loader = Schematics.CreateLoader(format);
						loader.Write(stream, data); stream.Position = 0;
						var read = loader.Read(stream).Units[0];
						Check(format + " roundtrip " + shape, read.FrameShape == shape && read.Index == unit.Index && read.Layer == 2 && math.abs(read.ScaleY - .5f) < .001f);
					}
				}
				Check("boxes excluded", !FrameShape.Supports(BasePart.WoodenBox) && !FrameShape.Supports(BasePart.MetalBox));
				Check("glass/coloured/special frames supported", FrameShape.Supports(new PartTypeInfo(PartType.MetalFrame, 132)) && FrameShape.Supports(new PartTypeInfo(PartType.MetalFrame, 12)) && FrameShape.Supports(BasePart.BracketFrame));
				var box = PartPhysics.CreateBox(float3.zero, new float3(1));
				try
				{
					for (int shape = 1; shape <= 3; shape++)
					{
						var hull = FrameShapeSystem.BuildHull(shape, box);
						var scaled = FrameShapeSystem.BuildHull(shape, box, new float2(2, .5f));
						try
						{
							var a = hull.Value.CalculateAabb(); var b = scaled.Value.CalculateAabb();
							Check("convex + nonuniform scale " + shape, hull.Value.Type == Unity.Physics.ColliderType.Convex && math.abs(b.Extents.x / a.Extents.x - 2) < .01f && math.abs(b.Extents.y / a.Extents.y - .5f) < .01f);
							Check("cut corner ray misses " + shape, !Ray(hull, .43f, shape == 2 ? .3f : -.43f));
							Check("interior ray hits " + shape, Ray(hull, -.1f, shape == 2 ? -.3f : .1f));
						}
						finally { hull.Dispose(); scaled.Dispose(); }
					}
					TestWorld(box);
				}
				finally { box.Dispose(); }
				var shader = Shader.Find("Bpsim/URP/Unlit_Texture_Instancing");
				Check("shader exists", shader != null);
				foreach (var message in ShaderUtil.GetShaderMessages(shader))
					Check("shader " + message.message, message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error);
				Log.AppendLine("FAILURES: 0");
			}
			catch (Exception ex) { Log.AppendLine("FAILURES: 1\n" + ex); }
			Directory.CreateDirectory("Builds/FrameShapesQA");
			File.WriteAllText("Builds/FrameShapesQA/core-tests.txt", Log.ToString());
			return Log.ToString();
		}
		private static bool Ray(BlobAssetReference<Unity.Physics.Collider> hull, float x, float y)
			=> hull.Value.CastRay(new RaycastInput { Start = new float3(x,y,-2), End = new float3(x,y,2), Filter = CollisionFilter.Default });
		private static void TestWorld(BlobAssetReference<Unity.Physics.Collider> original)
		{
			var previous = World.DefaultGameObjectInjectionWorld;
			using var world = new World("FrameShapes isolated audit");
			World.DefaultGameObjectInjectionWorld = world;
			try
			{
				var em = world.EntityManager;
				var entity = em.CreateEntity(typeof(FrameShape), typeof(FrameShapeDirty), typeof(FrameShapeOriginalCollider), typeof(PhysicsCollider), typeof(BlendFactor));
				em.SetComponentData(entity, new FrameShapeOriginalCollider { Value = original });
				em.SetComponentData(entity, new PhysicsCollider { Value = original });
				em.SetComponentData(entity, new BlendFactor { Value = new float4(.2f,.7f,0,0) });
				em.AddBuffer<LinkedEntityGroup>(entity).Add(entity);
				FrameShapeSystem.SetEnabled(true);
				FrameShapeSystem.SetShape(em, entity, 3);
				var custom = em.GetComponentData<PhysicsCollider>(entity).Value;
				Check("dirty update creates convex", custom.Value.Type == Unity.Physics.ColliderType.Convex);
				var blend = em.GetComponentData<BlendFactor>(entity).Value;
				Check("BRG shape channels preserve tint", blend.z == 3 && blend.x == .2f && blend.y == .7f);
				Check("clean entities leave query", !em.IsComponentEnabled<FrameShapeDirty>(entity));
				FrameShapeSystem.SetEnabled(false);
				Check("disable restores collider but retains saved shape", em.GetComponentData<PhysicsCollider>(entity).Value == original && em.GetComponentData<FrameShape>(entity).Value == 3 && em.GetComponentData<BlendFactor>(entity).Value.z == 0);
				FrameShapeSystem.SetEnabled(true);
				Check("re-enable reuses cached collider", em.GetComponentData<PhysicsCollider>(entity).Value == custom);
				FrameShapeSystem.SetEnabled(false);
			}
			finally { World.DefaultGameObjectInjectionWorld = previous; }
		}
	}
}
