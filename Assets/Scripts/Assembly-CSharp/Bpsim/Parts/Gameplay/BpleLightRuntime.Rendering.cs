using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bpsim.Parts.Gameplay
{
	internal sealed partial class BpleLightRuntime
	{
		private Material m_entityMaterial, m_nightMaterial;
		private Mesh m_quad;
		private MaterialPropertyBlock m_maskProperties;
		private readonly HashSet<Entity> m_alivePreviews = new HashSet<Entity>();
		private const int CircleSegments = 150;

		private void Awake()
		{
			// Unity native objects cannot be created by MonoBehaviour field initializers.
			m_maskProperties = new MaterialPropertyBlock();
			s_instance = this;
			RenderPipelineManager.endCameraRendering += RenderDarkness;
		}

		private void LateUpdate()
		{
			if (PartManager.Instance?.System == null) return;
			if (!m_running) RefreshPreviews();
			foreach (Source source in (m_running ? m_sources : m_previews).Values)
			{
				if (!Em.Exists(source.Part)) continue;
				if (!m_running) RefreshPose(source);
				bool visible = m_running ? source.EntityEnabled : LightSystem && source.Shape.Type >= 0
					&& source.Shape.Type != 4 && HasBuildConnection(source);
				if (!visible)
				{
					if (source.Visual != null) source.Visual.SetActive(false);
					continue;
				}
				EnsureVisual(source);
				if (source.Visual == null) continue;
				source.Visual.SetActive(true);
				if (source.MeshLength != source.Shape.Length || source.MeshWidth != source.Shape.HalfWidth)
				{
					BuildMesh(source.Mesh, source.Shape);
					source.MeshLength = source.Shape.Length; source.MeshWidth = source.Shape.HalfWidth;
				}
				source.Visual.transform.position = new Vector3(source.Position.x, source.Position.y, source.RootPosition.z - .5f);
				source.Visual.transform.rotation = Quaternion.AngleAxis(Mathf.Atan2(source.Direction.y, source.Direction.x) * Mathf.Rad2Deg, Vector3.forward);
				source.Color = m_running ? LightColor(source) : new Color(1, 1, 1, .15f);
				source.Properties.SetColor("_Color", source.Color);
				source.Visual.GetComponent<MeshRenderer>().SetPropertyBlock(source.Properties);
			}
		}

		private void RefreshPreviews()
		{
			var manager = PartManager.Instance;
			if (manager.ActiveScene == null) { ClearSources(m_previews); return; }
			HashSet<Entity> alive = m_alivePreviews;
			alive.Clear();
			using (var query = Em.CreateEntityQuery(ComponentType.ReadOnly<BpleLightSourceTag>(), ComponentType.ReadOnly<PartSceneID>()))
			using (var entities = query.ToEntityArray(Allocator.Temp))
				foreach (Entity entity in entities)
				{
					if (Em.GetComponentData<PartSceneID>(entity).Value != manager.ActiveScene.SceneID) continue;
					alive.Add(entity);
					if (!m_previews.ContainsKey(entity)) m_previews[entity] = NewSource(entity, PartGameplayUtil.TypeInfo(entity));
				}
			m_removed.Clear();
			foreach (Entity entity in m_previews.Keys) if (!alive.Contains(entity)) m_removed.Add(entity);
			foreach (Entity entity in m_removed) { DestroyVisual(m_previews[entity]); m_previews.Remove(entity); }
		}

		private static bool HasBuildConnection(Source source)
		{
			if (source.EnclosedInto != Entity.Null) return true;
			var scene = PartManager.Instance.ActiveScene;
			var grid = Em.GetComponentData<PartTransform>(source.Part);
			return scene != null && (scene.Unmanaged.FindPartAt(grid.X - 1, grid.Y, out _) || scene.Unmanaged.FindPartAt(grid.X + 1, grid.Y, out _)
				|| scene.Unmanaged.FindPartAt(grid.X, grid.Y - 1, out _) || scene.Unmanaged.FindPartAt(grid.X, grid.Y + 1, out _));
		}

		private void EnsureVisual(Source source)
		{
			if (source.Visual != null) return;
			if (m_entityMaterial == null) m_entityMaterial = Resources.Load<Material>("Bpsim/BpleEntityLight");
			if (m_entityMaterial == null) { Debug.LogError("[BPLE Lights] Missing entity-light material"); return; }
			source.Visual = new GameObject("INLight " + source.Info) { hideFlags = HideFlags.DontSave };
			source.Visual.transform.SetParent(transform, false);
			source.Mesh = new Mesh { name = "BPLE entity light " + source.Info };
			source.Mesh.MarkDynamic();
			source.Visual.AddComponent<MeshFilter>().sharedMesh = source.Mesh;
			var renderer = source.Visual.AddComponent<MeshRenderer>();
			renderer.sharedMaterial = m_entityMaterial;
			renderer.shadowCastingMode = ShadowCastingMode.Off;
			renderer.receiveShadows = false;
			source.Properties = new MaterialPropertyBlock();
		}

		internal static void BuildMesh(Mesh mesh, BpleLightEmitter shape)
		{
			mesh.Clear();
			if (shape.Type <= 1)
			{
				mesh.vertices = new[] { new Vector3(shape.Length, shape.HalfWidth), new Vector3(0, shape.HalfWidth),
					new Vector3(0, -shape.HalfWidth), new Vector3(shape.Length, -shape.HalfWidth) };
				mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
			}
			else
			{
				var vertices = new Vector3[(CircleSegments + 1) * 2];
				var indices = new int[CircleSegments * 6];
				float step = Mathf.PI / 180f * shape.Angle / CircleSegments;
				for (int i = 0; i <= CircleSegments; i++)
				{
					float c = Mathf.Cos((i - CircleSegments / 2) * step), s = Mathf.Sin((i - CircleSegments / 2) * step);
					vertices[i] = new Vector3((shape.Length - shape.HalfWidth) * c, (shape.Length - shape.HalfWidth) * s);
					vertices[i + CircleSegments + 1] = new Vector3((shape.Length + shape.HalfWidth) * c, (shape.Length + shape.HalfWidth) * s);
				}
				for (int i = 0; i < CircleSegments; i++)
				{
					indices[i * 6] = i; indices[i * 6 + 1] = i + 1; indices[i * 6 + 2] = i + CircleSegments + 1;
					indices[i * 6 + 3] = i + 1; indices[i * 6 + 4] = i + CircleSegments + 1; indices[i * 6 + 5] = i + CircleSegments + 2;
				}
				mesh.vertices = vertices; mesh.triangles = indices;
			}
			mesh.RecalculateBounds();
		}

		private static Color LightColor(Source source)
		{
			Color full;
			switch (source.Shape.Type)
			{
			case 0: full = new Color(.5f, .75f, 1, .5f); break;
			case 1: full = new Color(.5f, .65f, 1, .5f); break;
			case 3: full = new Color(.5f, .6f, 1, .5f); break;
			default: full = new Color(.5f, .7f, 1, .5f); break;
			}
			Color empty = new Color(1, .25f, .25f, .1f);
			if (source.EnclosedInto != Entity.Null && Em.Exists(source.EnclosedInto) && Em.HasComponent<ColoredFrame>(source.EnclosedInto))
			{
				var color = Em.GetComponentData<ColoredFrame>(source.EnclosedInto).Color;
				full = new Color(color.x, color.y, color.z, .5f); empty = new Color(color.x, color.y, color.z, .1f);
			}
			if (source.Shape.Type == 4) { full.a /= source.ComponentSize; empty.a /= source.ComponentSize; }
			return Color.Lerp(empty, full, source.Electricity > 0 ? Mathf.Sqrt(source.Electricity / source.Capacity) : 0);
		}

		internal static float IlluminationRadius(Source source, float elapsed, float realtime)
		{
			if (!source.HasToggled) return 0;
			float onTime = source.Profile.turnOnCurve[source.Profile.turnOnCurve.Length - 1].time;
			float offTime = source.Profile.turnOffCurve[source.Profile.turnOffCurve.Length - 1].time;
			if (source.Activated && elapsed >= onTime) source.Flickering = source.Profile.flicker != 0;
			if (!source.Activated && elapsed >= offTime) source.Flickering = false;
			float scale = source.Profile.Transition(source.Activated, elapsed);
			float size = source.Profile.sourceSize * scale;
			if (source.Flickering) size = source.Profile.sourceSize + Mathf.Sin(realtime * source.Profile.mainSinSpeed)
				* Mathf.Sin(realtime * source.Profile.subSinSpeed) * source.Profile.flicker;
			return Mathf.Max(0, size * Mathf.Min(source.ScaleX, source.ScaleY));
		}

		private void RenderDarkness(ScriptableRenderContext context, Camera camera)
		{
			if (!m_running || !UserSettings.Instance.SimulationSettings.BpleDarkness || SceneCamera.Instance?.Camera != camera) return;
			if (m_nightMaterial == null) m_nightMaterial = Resources.Load<Material>("Bpsim/BpleNightLighting");
			if (m_nightMaterial == null) return;
			if (m_quad == null)
			{
				m_quad = new Mesh { name = "BPLE light mask quad" };
				m_quad.vertices = new[] { new Vector3(-.5f, -.5f), new Vector3(.5f, -.5f), new Vector3(.5f, .5f), new Vector3(-.5f, .5f) };
				m_quad.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
				m_quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
			}
			var cmd = CommandBufferPool.Get("BPLE night lighting");
			int sceneTexture = Shader.PropertyToID("_BpleSceneTexture");
			cmd.GetTemporaryRT(sceneTexture, camera.pixelWidth, camera.pixelHeight, 0, FilterMode.Bilinear, RenderTextureFormat.DefaultHDR);
			cmd.Blit(BuiltinRenderTextureType.CameraTarget, sceneTexture);
			cmd.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
			m_maskProperties.Clear(); m_maskProperties.SetFloat("_BpleNightVision", m_nightVision ? 1 : 0);
			cmd.DrawMesh(m_quad, Matrix4x4.identity, m_nightMaterial, 0, 0, m_maskProperties);
			foreach (Source source in m_sources.Values)
			{
				if (!Em.Exists(source.Part)) continue;
				float radius = IlluminationRadius(source, Mathf.Max(0, Time.time - source.SwitchedAt), Time.realtimeSinceStartup);
				if (radius <= .001f) continue;
				m_maskProperties.Clear();
				m_maskProperties.SetVector("_BpleLightOrigin", source.RootPosition);
				m_maskProperties.SetVector("_BpleLightInfo", new Vector4(radius, source.Profile.borderWidth, source.Profile.sourceType, source.Profile.beamAngle));
				m_maskProperties.SetVector("_BpleLightDirection", new Vector4(source.Direction.x, source.Direction.y, source.Profile.beamCut, 0));
				m_maskProperties.SetFloat("_BpleNightVision", m_nightVision ? 1 : 0);
				var matrix = Matrix4x4.TRS(source.RootPosition, Quaternion.identity, new Vector3(2 * (radius + 1), 2 * (radius + 1), 1));
				cmd.DrawMesh(m_quad, matrix, m_nightMaterial, 0, 1, m_maskProperties);
			}
			cmd.ReleaseTemporaryRT(sceneTexture);
			context.ExecuteCommandBuffer(cmd);
			CommandBufferPool.Release(cmd);
		}

		private void ReleaseRendering()
		{
			RenderPipelineManager.endCameraRendering -= RenderDarkness;
			if (m_quad != null) Destroy(m_quad);
		}

		internal void ApplyControlFrameAlpha()
		{
			if (!m_running) return;
			foreach (Entity frame in m_autoFrames)
				PartGameplayUtil.SetAlpha(frame, .25f, VoidLightBehavior.AlphaRestore);
		}
	}

	[UpdateInGroup(typeof(PresentationSystemGroup))]
	[UpdateAfter(typeof(UpdatePresentationSystemGroup))]
	[UpdateBefore(typeof(Bpsim.Parts.SceneRenderSystem))]
	internal partial class VoidLightVisualSystem : SystemBase
	{
		protected override void OnUpdate()
		{
			BpleLightRuntime.Instance?.ApplyControlFrameAlpha();
		}
	}
}
