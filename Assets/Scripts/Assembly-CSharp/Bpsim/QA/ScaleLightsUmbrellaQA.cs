#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Text;
using Bpsim.Parts;
using Bpsim.Parts.Gameplay;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Bpsim.QA
{
	/// <summary>Isolated, replayable regression instrument; never clears the user's build scene.</summary>
	public sealed partial class ScaleLightsUmbrellaQA : MonoBehaviour
	{
		private static readonly StringBuilder Report = new StringBuilder();
		private static string s_status = "idle";
		private static int s_failed;
		private static int s_passed;
		private static ScaleLightsUmbrellaQA s_runner;
		private PartScene m_testScene;

		public static string Run(string scope)
		{
			if (s_status == "running") return "busy";
			if (!Application.isPlaying) return "enter Play Mode first";
			if (s_runner == null)
			{
				var go = new GameObject("[ScaleLightsUmbrellaQA]") { hideFlags = HideFlags.DontSave };
				DontDestroyOnLoad(go);
				s_runner = go.AddComponent<ScaleLightsUmbrellaQA>();
			}
			Report.Clear();
			s_failed = s_passed = 0;
			s_status = "running";
			s_runner.StartCoroutine(s_runner.CheckAll(scope));
			return "started:" + scope;
		}

		public static string Status() => s_status + "\n" + Report;

		private static void Check(string name, bool success, object detail = null)
		{
			if (success) s_passed++; else s_failed++;
			Report.Append(success ? "PASS " : "FAIL ").Append(name);
			if (detail != null) Report.Append(" | ").Append(detail);
			Report.Append('\n');
		}

		private IEnumerator CheckAll(string scope)
		{
			for (int i = 0; i < 600 && PartManager.Instance?.System == null; i++) yield return null;
			var manager = PartManager.Instance;
			if (manager?.System == null) { Check("manager ready", false); Finish(); yield break; }
			PartScene original = manager.ActiveScene;
			bool wasSimulating = manager.IsSimulating;
			int originalCount = original != null ? original.Unmanaged.Parts.Length : 0;
			var settings = UserSettings.Instance.SimulationSettings;
			var savedSettings = new SimulationSettings();
			savedSettings.Update(settings);
			try
			{
				manager.EndSimulation();
				settings.GravityX = settings.GravityY = 0;
				settings.CreateJoints = false;
				settings.MegaStructure = false;
				manager.LoadEmptyScene("ScaleLightsUmbrellaQA");
				m_testScene = manager.ActiveScene;
				yield return null;
				yield return null;
				if (scope == "scale" || scope == "all") yield return CheckScale();
				if (scope == "lights" || scope == "all") yield return CheckLights();
				if (scope == "umbrella" || scope == "all") yield return CheckUmbrella();
			}
			finally
			{
				manager.EndSimulation();
				settings.Update(savedSettings);
				if (original != null)
				{
					manager.SelectScene(original.SceneID);
					Check("user build preserved", original.Unmanaged.Parts.Length == originalCount, originalCount);
				}
				if (m_testScene != null && m_testScene != original) manager.UnloadScene(m_testScene.SceneID);
				m_testScene = null;
				// Preserve the user's build, not a destructive simulation time-line.
				if (wasSimulating && original != null) manager.StartSimulation();
				Finish();
			}
		}

		private Entity Place(PartType type, int index, int x, int y, int rotation = 0, bool flipped = false)
		{
			var manager = PartManager.Instance;
			var info = new PartTypeInfo(type, index);
			Entity prefab = manager.EntitySpawner.FindPrefab(info);
			if (prefab == Entity.Null) return Entity.Null;
			var request = new PartCreationRequest(prefab, m_testScene.SceneID, info, x, y, rotation, flipped);
			if (!m_testScene.Unmanaged.PlacePartAt(in request, false)) return Entity.Null;
			return m_testScene.Unmanaged.FindPartAt(x, y, out var part) ? part : Entity.Null;
		}

		private IEnumerator ResetScene()
		{
			PartManager.Instance.EndSimulation();
			m_testScene.Unmanaged.ClearParts();
			yield return null;
		}

		private IEnumerator CheckScale()
		{
			foreach (float factor in new[] { .1f, .5f, 2f })
			{
				yield return ResetScene();
				Entity first = Place(PartType.MetalFrame, 0, 0, 0);
				Entity second = Place(PartType.MetalFrame, 0, 1, 0);
				Entity childPart = Place(PartType.TNT, 0, 3, 2);
				Check("scale fixtures", first != Entity.Null && second != Entity.Null && childPart != Entity.Null);
				if (first == Entity.Null || second == Entity.Null || childPart == Entity.Null) continue;
				m_testScene.Unmanaged.TryScaleAllPartsFree(factor, new float2(1.25f, -.75f));
				m_testScene.Unmanaged.TryRotatePartFree(childPart, 33f);
				m_testScene.Unmanaged.TryScalePartFree(childPart, new float2(1.5f, .75f) * factor);
				yield return null;
				yield return null;
				var em = PartManager.Instance.System.EntityManager;
				em.CompleteAllTrackedJobs();
				var built = new LocalTransform[3];
				var builtScale = new PostTransformScale[3];
				var colliderSizes = new float3[3];
				Entity[] parts = { first, second, childPart };
				for (int i = 0; i < parts.Length; i++)
				{
					built[i] = em.GetComponentData<LocalTransform>(parts[i]);
					builtScale[i] = em.GetComponentData<PostTransformScale>(parts[i]);
					colliderSizes[i] = em.GetComponentData<Unity.Physics.PhysicsCollider>(parts[i]).Value.Value.CalculateAabb().Extents;
				}
				PartManager.Instance.StartSimulation();
				var simulated = PartManager.Instance.PartSimulator.Unmanaged.Parts;
				for (int i = 0; i < parts.Length; i++)
				{
					var local = em.GetComponentData<LocalTransform>(simulated[i]);
					var scale = em.GetComponentData<PostTransformScale>(simulated[i]);
					Check("build/simulation pose scale=" + factor + " part=" + i,
						math.distance(local.Position, built[i].Position) < .0001f && math.abs(math.dot(local.Rotation.value, built[i].Rotation.value)) > .99999f,
						"build=" + built[i].Position + " run=" + local.Position);
					Check("build/simulation visual scale=" + factor + " part=" + i,
						math.lengthsq(scale.Value.c0 - builtScale[i].Value.c0) + math.lengthsq(scale.Value.c1 - builtScale[i].Value.c1) < 1e-8f);
					float2 freeScale = m_testScene.Unmanaged.GetPartFreeTransform(parts[i]).Scale;
					float3 size = em.GetComponentData<Unity.Physics.PhysicsCollider>(simulated[i]).Value.Value.CalculateAabb().Extents;
					Check("scaled physical collider=" + factor + " part=" + i,
						math.distance(size.xy, colliderSizes[i].xy * freeScale) < .0001f, size);
				}
			}
			yield return ResetScene();
			Entity sameFrame = Place(PartType.MetalFrame, 0, 2, -1);
			var free = new PartFreeTransform { Offset = new float2(.37f, -.24f), RotationDegrees = 33f, Scale = new float2(1.5f, .75f) };
			m_testScene.Unmanaged.TrySetPartFree(sameFrame, in free);
			PartBuildLayerUtility.SetLayer(PartManager.Instance.System.EntityManager, sameFrame, PartBuildLayer.MinLayer + 2);
			PartManager.Instance.StartSimulation();
			var entityManager = PartManager.Instance.System.EntityManager;
			Entity sim = PartManager.Instance.PartSimulator.Unmanaged.Parts[0];
			var seeded = entityManager.GetComponentData<LocalTransform>(sim);
			Check("same-frame free pose", math.distance(seeded.Position.xy, new float2(2.37f, -1.24f)) < .0001f, seeded.Position);
			Check("same-frame rotation", math.abs(math.dot(seeded.Rotation.value, quaternion.RotateZ(math.radians(33)).value)) > .99999f);
			Check("same-frame layer depth", math.abs(seeded.Position.z - (-.1f - 2 * PartBuildLayer.DepthStep)) < .0001f, seeded.Position.z);
			var sameFrameScale = entityManager.GetComponentData<PostTransformScale>(sim).Value;
			Check("same-frame visual scale", math.abs(sameFrameScale.c0.x - 1.5f) < .0001f && math.abs(sameFrameScale.c1.y - .75f) < .0001f);
			yield return ResetScene();
		}

		private IEnumerator CheckLights() => CheckLightContracts();

		private IEnumerator CheckUmbrella() => CheckUmbrellaContracts();

		private static void Finish()
		{
			s_status = (s_failed == 0 ? "done:PASS" : "done:FAIL") + " pass=" + s_passed + " fail=" + s_failed;
			string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "bpsim-scale-lights-umbrella.log"));
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			File.WriteAllText(path, Status(), new UTF8Encoding(false));
			Debug.Log("[ScaleLightsUmbrellaQA] " + Status());
		}
	}
}
#endif
