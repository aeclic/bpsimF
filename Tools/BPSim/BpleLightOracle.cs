using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

// Executes only pure collision methods from BPLE's already-compiled assembly.
// No Unity executable launch, project writes, constructors, scenes or native physics calls.
internal static class BpleLightOracle
{
	private static Type vector;
	private static object Vec(float[] value) => Activator.CreateInstance(vector, value[0], value[1]);
	private static void Set(object instance, string name, object value) => instance.GetType().GetField(name,
		BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(instance, value);
	private static object Get(object instance, string name) => instance.GetType().GetField(name,
		BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(instance);
	private static float[] XY(object value) => new[] { (float)Get(value, "x"), (float)Get(value, "y") };
	private static object Empty(Type type) => RuntimeHelpers.GetUninitializedObject(type);
	private static float[] Direction(float radians) => new[] { (float)Math.Cos(radians), (float)Math.Sin(radians) };

	private static void Compare(string path, List<Case> cases)
	{
		Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
		Type collisionType = assembly.GetType("Bpsim.Parts.Gameplay.BpleLightCollision", true);
		Type emitterType = assembly.GetType("Bpsim.Parts.Gameplay.BpleLightEmitter", true);
		Type motionType = collisionType.GetNestedType("CCDData");
		Type boundsType = assembly.GetType("Bpsim.Parts.Gameplay.BpleLightBounds", true);
		object collision = Activator.CreateInstance(collisionType, true);
		MethodInfo cast = collisionType.GetMethod("Cast");
		int failed = 0;
		float maxError = 0;
		foreach (Case sample in cases)
		{
			object emitter = Activator.CreateInstance(emitterType, true);
			Set(emitter, "Type", sample.type); Set(emitter, "Sides", sample.sides); Set(emitter, "Length", sample.length);
			Set(emitter, "HalfWidth", sample.halfWidth); Set(emitter, "Angle", sample.angle);
			object data = Get(emitter, "Data");
			Set(data, "Position0", Vec(sample.lightPosition0)); Set(data, "Position1", Vec(sample.lightPosition1));
			Set(data, "Direction0", Vec(sample.lightDirection0)); Set(data, "Direction1", Vec(sample.lightDirection1)); Set(emitter, "Data", data);
			object motion = Activator.CreateInstance(motionType);
			Set(motion, "Position0", Vec(sample.position0)); Set(motion, "Position1", Vec(sample.position1));
			Set(motion, "Direction0", Vec(sample.direction0)); Set(motion, "Direction1", Vec(sample.direction1)); Set(motion, "DeltaTime", .02f);
			object bounds = Activator.CreateInstance(boundsType);
			Set(bounds, "Type", (int)sample.bounds[0]); Set(bounds, "A", sample.bounds[3]); Set(bounds, "B", sample.bounds[4]); Set(bounds, "R", sample.bounds[5]);
			Set(motion, "Bounds", bounds);
			object[] parameters = { emitter, motion, null };
			bool hit = (bool)cast.Invoke(collision, parameters);
			bool equal = hit == sample.hit;
			if (hit && sample.hit)
			{
				object result = parameters[2];
				var expected = new[] { sample.time, sample.separation }.Concat(sample.contactPoint).Concat(sample.contactNormal).Concat(sample.relativeVelocity).ToArray();
				var actual = new[] { (float)Get(result, "TimeOfImpact"), (float)Get(result, "ContactSeparation") }
					.Concat(XY(Get(result, "ContactPoint"))).Concat(XY(Get(result, "ContactNormal"))).Concat(XY(Get(result, "RelativeVelocity"))).ToArray();
				for (int i = 0; i < actual.Length; i++)
				{
					float error = Math.Abs(actual[i] - expected[i]);
					maxError = Math.Max(maxError, error); equal &= error <= .0001f;
				}
				equal &= (int)Get(result, "ContactCount") == sample.count;
			}
			if (!equal) failed++;
		}
		Console.WriteLine("BPLE vs repaired collision: cases=" + cases.Count + " mismatches=" + failed + " maxError=" + maxError);
		if (failed != 0) throw new InvalidOperationException("Collision contract mismatch");
	}

	public sealed class Case
	{
		public int type { get; set; }
		public int sides { get; set; }
		public float length { get; set; }
		public float halfWidth { get; set; }
		public float angle { get; set; }
		public float[] lightPosition0 { get; set; }
		public float[] lightPosition1 { get; set; }
		public float[] lightDirection0 { get; set; }
		public float[] lightDirection1 { get; set; }
		public float[] position0 { get; set; }
		public float[] position1 { get; set; }
		public float[] direction0 { get; set; }
		public float[] direction1 { get; set; }
		public float[] bounds { get; set; }
		public bool hit { get; set; }
		public float time { get; set; }
		public int count { get; set; }
		public float[] contactPoint { get; set; }
		public float[] contactNormal { get; set; }
		public float separation { get; set; }
		public float[] relativeVelocity { get; set; }
	}

	public static int Main(string[] args)
	{
		string oracle = Path.GetFullPath(args[0]);
		string unity = Path.GetFullPath(args[1]);
		string[] search = { Path.Combine(oracle, "Library/ScriptAssemblies"), Path.Combine(unity, "Data/Managed/UnityEngine"),
			Path.Combine(unity, "Data/Managed"), Path.Combine(oracle, "Assets/Plugins") };
		AssemblyLoadContext.Default.Resolving += (context, name) =>
		{
			foreach (string dir in search)
			{
				string path = Path.Combine(dir, name.Name + ".dll");
				if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
			}
			return null;
		};
		var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(oracle, "Library/ScriptAssemblies/Assembly-CSharp.dll"));
		Type managerType = assembly.GetType("EntityLightManager", true);
		Type lightType = assembly.GetType("EntityLight", true);
		Type motionType = managerType.GetNestedType("CCDData");
		Type boundsType = assembly.GetType("INBounds", true);
		vector = motionType.GetField("Position0").FieldType;
		object manager = Empty(managerType);
		Set(manager, "m_maxDetectionDistance", 40f);
		var cast = managerType.GetMethod("ComputeTimeOfImpact", BindingFlags.Instance | BindingFlags.NonPublic);
		var il = managerType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
			.Where(m => m.Name.StartsWith("ComputeTimeOfImpact"))
			.ToDictionary(m => m.Name, m => Convert.ToHexString(SHA256.HashData(m.GetMethodBody().GetILAsByteArray())));
		var cases = new List<Case>();
		var random = new Random(224445);
		float Next(float range) => (float)(random.NextDouble() * 2 - 1) * range;
		for (int type = 0; type < 5; type++)
		for (int i = 0; i < 800; i++)
		{
			float theta = Next(3.14f);
			var sample = new Case { type = type, sides = i % 3, length = 8, halfWidth = type == 1 ? .5f : .25f,
				angle = i % 2 == 0 ? 180 : 90, lightPosition0 = new[] { .1f, -.2f }, lightPosition1 = new[] { .15f, -.23f },
				lightDirection0 = Direction(theta), lightDirection1 = Direction(theta + .02f),
				position0 = new[] { Next(12), Next(12) }, direction0 = Direction(theta + .4f), direction1 = Direction(theta + .41f),
				bounds = new[] { (float)(i % 2), 0f, 0f, .5f, .35f, .4f } };
			if (type == 4) sample.angle = 360;
			sample.position1 = new[] { sample.position0[0] + Next(4), sample.position0[1] + Next(4) };
			object light = Empty(lightType);
			Set(light, "m_type", type); Set(light, "m_length", sample.length); Set(light, "m_halfWidth", sample.halfWidth);
			Set(light, "m_angle", sample.angle); Set(light, "m_cos", (float)Math.Cos(sample.angle * (MathF.PI / 360f))); Set(light, "m_sides", sample.sides);
			object data = Get(light, "m_data");
			Set(data, "Position0", Vec(sample.lightPosition0)); Set(data, "Position1", Vec(sample.lightPosition1));
			Set(data, "Direction0", Vec(sample.lightDirection0)); Set(data, "Direction1", Vec(sample.lightDirection1)); Set(light, "m_data", data);
			object motion = Activator.CreateInstance(motionType);
			Set(motion, "Position0", Vec(sample.position0)); Set(motion, "Position1", Vec(sample.position1));
			Set(motion, "Direction0", Vec(sample.direction0)); Set(motion, "Direction1", Vec(sample.direction1));
			Set(motion, "DeltaTime", .02f);
			object bounds = Activator.CreateInstance(boundsType);
			Set(bounds, "Type", (int)sample.bounds[0]); Set(bounds, "X", 0f); Set(bounds, "Y", 0f);
			Set(bounds, "A", sample.bounds[3]); Set(bounds, "B", sample.bounds[4]); Set(bounds, "R", sample.bounds[5]); Set(motion, "Bounds", bounds);
			object[] parameters = { light, motion, null };
			sample.hit = (bool)cast.Invoke(manager, parameters);
			object hit = parameters[2];
			sample.time = (float)Get(hit, "TimeOfImpact"); sample.count = (int)Get(hit, "ContactCount");
			sample.contactPoint = XY(Get(hit, "ContactPoint")); sample.contactNormal = XY(Get(hit, "ContactNormal"));
			sample.separation = (float)Get(hit, "ContactSeparation"); sample.relativeVelocity = XY(Get(hit, "RelativeVelocity"));
			cases.Add(sample);
		}
		string json = JsonSerializer.Serialize(new { il, cases }, new JsonSerializerOptions { WriteIndented = false });
		File.WriteAllText(args[2], json + "\n", new System.Text.UTF8Encoding(false));
		Console.WriteLine("PASS compiled BPLE collision oracle: cases=" + cases.Count + " hits=" + cases.Count(c => c.hit));
		Console.WriteLine("NATIVE SHA256=" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json + "\n"))));
		if (args.Length > 3) Compare(args[3], cases);
		return 0;
	}
}
