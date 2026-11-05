#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Bpsim.Factions;
using Bpsim.Parts;
using Bpsim.Serialization;
using EPOOutline;
using Unity.Entities;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Bpsim.EditorTools
{
	/// <summary>Isolated edit-mode checks: does not load scenes or start/stop the shared editor.</summary>
	public static class FactionFeatureQA
	{
		[MenuItem("Bpsim/QA/Faction feature (isolated)")]
		public static void RunMenu() => Debug.Log(Run());

		public static void RefreshAssets() => AssetDatabase.Refresh();

		public static string Run()
		{
			var checks = new List<string>();
			void Check(bool passed, string name) { if (!passed) throw new Exception("Faction QA failed: " + name); checks.Add(name); }
			var defaults = new SimulationSettings();
			Check(defaults.BuildFactionOutlines && !defaults.RuntimeFactionOutlines && defaults.FactionOutlineWidth == 2.5f, "defaults: build on, runtime off");
			var old = Json.Deserialize<SimulationSettings>("{\"GravityY\":-5}");
			Check(old.BuildFactionOutlines && !old.RuntimeFactionOutlines, "legacy settings preserve defaults");
			defaults.RuntimeFactionOutlines = true; defaults.BuildFactionOutlines = false; defaults.FactionOutlineWidth = 4f;
			var saved = Json.Deserialize<SimulationSettings>(Json.Serialize(defaults));
			var updated = new SimulationSettings(); updated.Update(saved);
			Check(updated.RuntimeFactionOutlines && !updated.BuildFactionOutlines && updated.FactionOutlineWidth == 4f, "settings serialization and Update");
			updated.FactionOutlineWidth = float.NaN; updated.FactionOutlineWidth = 0f; updated.FactionOutlineWidth = 9f;
			Check(updated.FactionOutlineWidth == 4f, "invalid widths rejected");
			updated.Update(new SimulationSettings());
			Check(!updated.RuntimeFactionOutlines && updated.BuildFactionOutlines, "reset restores default-off runtime");
			for (int rotation = 0; rotation < 8; rotation++)
				Check(FactionRules.TeamMask(rotation) == 1 << (rotation % 4), "rotation " + rotation);
			Check(FactionRules.SameTeam(0, 0, 0, 0) && !FactionRules.SameTeam(0, 0, 1, 0), "unmarked connected vs separate vehicles");
			Check(FactionRules.SameTeam(0, 3, 1, 2) && !FactionRules.SameTeam(0, 1, 1, 2), "multi-marker alliance masks");
			Check(FactionRules.OutlineColor(0).a == 0 && FactionRules.OutlineColor(1) != FactionRules.OutlineColor(2), "palette and unmarked visibility");

			using (var world = new World("FactionFeatureQA isolated"))
			{
				var scene = new PartSceneUnmanaged(); scene.Initialize(12345, world.Unmanaged);
				try
				{
					EntityManager em = world.EntityManager;
					Entity Place(PartType type, int index, int x, int y, int rotation = 0, int layer = 1, BitDirection directions = BitDirection.Any)
					{
						Entity part = em.CreateEntity(typeof(PartTypeValue), typeof(PartTransform), typeof(PartExtensionComponent),
							typeof(ContainedPart), typeof(PartContainerValue), typeof(PartConnectedComponent), typeof(PartBuildLayer));
						em.SetComponentData(part, new PartTypeValue { Type = type, Index = index });
						em.SetComponentData(part, new PartTransform { X = x, Y = y, Rotation = rotation });
						em.SetComponentData(part, new PartBuildLayer { Value = (byte)layer });
						em.SetComponentData(part, new PartExtensionComponent { Value = new PartExtensionData { IsConnectionSource = true, ConnectionDirection = directions } });
						scene.Parts.Add(part);
						scene.PartMap.TryGet(x, y, layer, out var cell);
						if (BasePart.IsContainer(new PartTypeInfo(type,index))) cell.PartContainer = part; else cell.Part = part;
						scene.PartMap.Set(x, y, layer, cell);
						return part;
					}
					var topology = new BuildFactionTopology();
					Entity marker = Place(PartType.Kicker, 3, 0, 0);
					Entity red = Place(PartType.MetalFrame, 0, 1, 0);
					Entity blueMarker = Place(PartType.Kicker, 3, 10, 0, 1);
					Entity blue = Place(PartType.MetalFrame, 0, 11, 0);
					Entity neutral = Place(PartType.MetalFrame, 0, 20, 0);
					Entity diagonalMarker = Place(PartType.Kicker, 3, 30, 0, 4);
					Entity diagonal = Place(PartType.MetalFrame, 0, 31, 0);
					Entity contained = Place(PartType.Kicker, 3, 40, 0, 2);
					Entity frame = Place(PartType.MetalFrame, 0, 40, 0);
					Entity upper = Place(PartType.MetalFrame, 0, 1, 0, 0, 2);
					Entity blocked = Place(PartType.Kicker, 3, 50, 0, 0, 1, BitDirection.None);
					Entity blockedNeighbour = Place(PartType.MetalFrame, 0, 51, 0);
					Entity electricMarker = Place(PartType.Kicker, 3, 60, 0);
					Entity wireA = Place(PartType.ElectricalPart, 0, 61, 0);
					Entity wireB = Place(PartType.ElectricalPart, 0, 62, 0);
					var extension = em.GetComponentData<PartExtensionComponent>(wireB); extension.Value.ConnectionDirection = BitDirection.None;
					// Force the electrical-port rule to connect the two wires, not the mechanical one.
					em.SetComponentData(wireB, extension);
					Entity ordinaryElectronic = Place(PartType.ElectricalPart, 4, 0, 1, 0, 1, BitDirection.None);
					em.SetComponentData(frame, new ContainedPart { Value = contained });
					em.SetComponentData(contained, new PartContainerValue { Value = frame });
					topology.Rebuild(scene);
					Check(topology.GetTeams(red) == 1 && topology.GetTeams(blue) == 2 && topology.GetTeams(neutral) == 0, "build connected structures inherit team");
					Check(topology.GetTeams(diagonal) == 1, "diagonal marker retains cardinal faction");
					Check(topology.GetTeams(frame) == 4 && topology.GetTeams(upper) == 1, "contained markers and cross-layer welds");
					Check(topology.GetTeams(blockedNeighbour) == 0, "adjacency alone does not join vehicles");
					Check(topology.GetTeams(ordinaryElectronic) == 1, "ordinary electronics accept chassis joints");
					Check(topology.GetTeams(wireA) == 1 && topology.GetTeams(wireB) == 1, "electrical terminal connectivity");
					var transform = em.GetComponentData<PartTransform>(marker); transform.Rotation = 3; em.SetComponentData(marker, transform);
					topology.Rebuild(scene);
					Check(topology.GetTeams(red) == 8, "rotate marker updates preview");
					Entity extraMarker = Place(PartType.Kicker, 3, 2, 0, 1);
					topology.Rebuild(scene); Check(topology.GetTeams(red) == 10, "multiple markers accumulate alliances");
					em.DestroyEntity(marker); topology.Rebuild(scene);
					Check(topology.GetTeams(red) == 2, "removed marker clears its team");
					em.DestroyEntity(extraMarker); topology.Rebuild(scene);
					Check(topology.GetTeams(red) == 0, "last marker removed clears preview");
				}
				finally { scene.Dispose(); }
			}
            Shader shader = Resources.Load<Shader>("EpoFactionTarget");
            Check(shader != null && shader.isSupported && !ShaderUtil.ShaderHasError(shader), "ECS target shader imported and supported");
            var rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(FactionEpoSetup.RendererPath);
            Check(rendererData.rendererFeatures.Exists(feature => feature is URPOutlineFeature), "asset URPOutlineFeature installed in active 2D renderer");
            Check(Resources.Load<Material>("Easy performant outline/Materials/_Outline") != null, "asset outline material loaded, not custom outline shader");
			RenderProbe(Check);
			string result = Json.Serialize(new { passed = checks.Count, checks }, true);
			string path = Path.Combine(Application.dataPath, "../perf/faction-feature-qa.json");
			Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, result);
			return result;
		}

		private static void RenderProbe(Action<bool, string> check)
		{
			// An opaque neighbour in the right half deliberately stresses atlas-boundary clamping.
			var texture = new Texture2D(64, 32, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
			var pixels = new Color[64 * 32];
			for (int y = 0; y < 32; y++)
			for (int x = 0; x < 64; x++)
			{
				float dx = (x + .5f) / 32f - .5f, dy = (y + .5f) / 32f - .5f;
				pixels[y*64+x] = x >= 32 || dx*dx+dy*dy < .45f*.45f ? Color.white : Color.clear;
			}
			texture.SetPixels(pixels); texture.Apply();
            var target = new RenderTexture(1024, 256, 24, RenderTextureFormat.ARGB32); target.Create();
			var readback = new Texture2D(1024, 256, TextureFormat.RGBA32, false);
			var previous = RenderTexture.active;
            var cameraObject = new GameObject("[EPO isolated QA camera]") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
            camera.transform.position = new Vector3(10000,0,-10); camera.orthographic = true; camera.orthographicSize = 1f;
            camera.aspect = 4f; camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.03f,.03f,.03f,1); camera.allowHDR = false; camera.allowMSAA = false;
            camera.targetTexture = target;
            var outliner = cameraObject.AddComponent<Outliner>(); EpoFactionStyle.Apply(outliner,2.5f);
            var masks = new List<EpoFactionMask>();
			try
			{
				for (int i = 0; i < 4; i++)
				{
                    var mask = new EpoFactionMask(1 << i,texture,Resources.Load<Shader>("EpoFactionTarget")); masks.Add(mask);
                    mask.GameObject.layer = 31; mask.Begin();
                    mask.Append(Matrix4x4.Translate(new Vector3(9997f+i*2f,0,0)),new Unity.Mathematics.float4(0,0,1.5f,1.5f),
                        new Unity.Mathematics.float4(0,0,.5f,1),Unity.Mathematics.float4.zero); mask.Commit();
				}
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                check(RenderPipeline.SupportsRenderRequest(camera,request), "native URP single-camera render request supported");
                // Warm visibility just like EPO's standard Renderer targets. No fake IsVisible injection.
                for (int frame=0; frame<3; frame++) RenderPipeline.SubmitRenderRequest(camera,request);
				RenderTexture.active = target; readback.ReadPixels(new Rect(0,0,1024,256),0,0); readback.Apply();
                string path = Path.Combine(Application.dataPath,"../perf/faction-outline-epo-gpu-probe.png");
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path,readback.EncodeToPNG());
				for (int i = 0; i < 4; i++)
				{
					int colored = 0;
					for (int y = 0; y < 256; y++)
					for (int x = i*256; x < (i+1)*256; x++)
					{
						Color pixel = readback.GetPixel(x,y);
						if (Mathf.Max(pixel.r,Mathf.Max(pixel.g,pixel.b)) > .15f) colored++;
					}
                    check(colored > 200, "EPO GPU draws faction " + (i+1) + " outline (pixels=" + colored + ")");
                    Color inside = readback.GetPixel(i*256+128,128);
                    check(inside.r < .05f && inside.g < .05f && inside.b < .05f,"EPO leaves sprite interior unchanged " + (i+1));
					Color outside = readback.GetPixel(i*256+245,128);
					check(outside.r < .05f && outside.g < .05f && outside.b < .05f, "GPU clamps neighbouring atlas sprites " + (i+1));
				}
                foreach (string name in new[] { "_Outline", "_Dilate", "_Blur", "_FinalBlit" })
                    check(!ShaderUtil.ShaderHasError(Resources.Load<Material>("Easy performant outline/Materials/"+name).shader), "asset shader compiles after native draw: " + name);
			}
			finally
			{
				RenderTexture.active = previous;
				UnityEngine.Object.DestroyImmediate(readback); target.Release(); UnityEngine.Object.DestroyImmediate(target);
                foreach (var mask in masks) mask.Dispose();
                UnityEngine.Object.DestroyImmediate(cameraObject); UnityEngine.Object.DestroyImmediate(texture);
			}
		}
	}
}
#endif
