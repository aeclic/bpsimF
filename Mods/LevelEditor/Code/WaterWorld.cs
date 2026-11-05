using System;
using System.Collections.Generic;
using Bpsim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Bpsim.LevelEditor
{
	/// <summary>One camera-resolution scene capture for all pools; terrain masks use imported fill meshes.</summary>
	public sealed class WaterWorld : MonoBehaviour
	{
		private readonly List<WaterPreview> m_bodies = new List<WaterPreview>();
		private MeshFilter[] m_terrain;
		private Camera m_captureCamera;
		private RenderTexture m_scene, m_underwater, m_mask, m_rays;
		private Material m_maskMaterial;
		private CommandBuffer m_commands;
		private bool m_rendering, m_subscribed, m_failed;
		private int m_lastFrame = -1;
		private readonly Plane[] m_planes = new Plane[6];
		public int CaptureCount { get; private set; }
		public RenderTexture SceneTexture { get { return m_scene; } }
		public RenderTexture TerrainTexture { get { return m_mask; } }

		public void Initialize(Transform levelRoot, WaterAssets assets)
		{
			List<MeshFilter> terrain = new List<MeshFilter>();
			foreach (MeshFilter mesh in levelRoot.GetComponentsInChildren<MeshFilter>(true))
				if (mesh.name == "_fill" && mesh.sharedMesh != null) terrain.Add(mesh);
			m_terrain = terrain.ToArray();
			if (!assets.HasAdvancedEffects) return;
			m_maskMaterial = new Material(assets.MaskShader) { name = "Mod Water Terrain Mask" };
			m_commands = new CommandBuffer { name = "Level Editor Mod / Water Terrain Mask" };
			GameObject cameraObject = new GameObject("LevelEditorWaterCaptureCamera");
			cameraObject.transform.SetParent(transform, false);
			cameraObject.hideFlags = HideFlags.HideAndDontSave;
			m_captureCamera = cameraObject.AddComponent<Camera>();
			m_captureCamera.enabled = false;
			UniversalAdditionalCameraData data = m_captureCamera.GetUniversalAdditionalCameraData();
			data.renderType = CameraRenderType.Base;
			data.renderPostProcessing = false;
			data.requiresColorOption = CameraOverrideOption.Off;
			data.requiresDepthOption = CameraOverrideOption.Off;
			RenderPipelineManager.beginCameraRendering += BeginCamera;
			m_subscribed = true;
		}

		public void Add(WaterPreview water) { m_bodies.Add(water); }

		private void BeginCamera(ScriptableRenderContext context, Camera camera)
		{
			Camera gameCamera = SceneCamera.Instance == null ? null : SceneCamera.Instance.Camera;
			if (m_rendering || m_failed || camera != gameCamera || !camera.orthographic || m_lastFrame == Time.frameCount) return;
			bool visible = false, needsCapture = false;
			GeometryUtility.CalculateFrustumPlanes(camera, m_planes);
			foreach (WaterPreview body in m_bodies)
			{
				if (body == null || !body.isActiveAndEnabled || body.Renderer == null) continue;
				if (!GeometryUtility.TestPlanesAABB(m_planes, body.Renderer.bounds)) continue;
				visible = true;
				needsCapture |= body.NeedsCapture;
			}
			if (!visible || !needsCapture) return;
			m_lastFrame = Time.frameCount;
			m_rendering = true;
			try
			{
				EnsureTextures(camera);
				m_captureCamera.CopyFrom(camera);
				m_captureCamera.enabled = false;
				m_captureCamera.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
				m_captureCamera.targetTexture = m_scene;
				m_captureCamera.aspect = camera.aspect;
				m_captureCamera.rect = new Rect(0, 0, 1, 1);
				m_captureCamera.cullingMask = camera.cullingMask & ~(1 << 5);
				m_captureCamera.allowMSAA = false;
				// Hiding only our renderers avoids changing any host layer or culling mask.
				foreach (WaterPreview body in m_bodies) if (body != null)
				{
					if (body.Renderer != null) body.Renderer.enabled = false;
					if (body.SurfaceRenderer != null) body.SurfaceRenderer.enabled = false;
				}
				UniversalRenderPipeline.RenderSingleCamera(context, m_captureCamera);
				CaptureCount++;
				m_commands.Clear();
				m_commands.SetRenderTarget(m_mask);
				m_commands.ClearRenderTarget(false, true, Color.black);
				m_commands.SetViewProjectionMatrices(camera.worldToCameraMatrix, GL.GetGPUProjectionMatrix(camera.projectionMatrix, true));
				foreach (MeshFilter terrain in m_terrain)
					if (terrain != null && terrain.gameObject.activeInHierarchy && terrain.sharedMesh != null)
						m_commands.DrawMesh(terrain.sharedMesh, terrain.transform.localToWorldMatrix, m_maskMaterial, 0, 0);
				context.ExecuteCommandBuffer(m_commands);
				float height = camera.orthographicSize * 2f, width = height * camera.aspect;
				Vector4 rect = new Vector4(camera.transform.position.x - width * .5f,
					camera.transform.position.y - height * .5f, width, height);
				m_commands.Clear(); m_commands.SetRenderTarget(m_rays); m_commands.ClearRenderTarget(false,true,Color.clear);
				m_commands.SetViewProjectionMatrices(camera.worldToCameraMatrix,GL.GetGPUProjectionMatrix(camera.projectionMatrix,true));
				Graphics.Blit(m_scene,m_underwater);
				foreach (WaterPreview body in m_bodies)
				{
					if (body == null || !body.isActiveAndEnabled || body.LightRays == null) continue;
					Material material=body.LightRays.Material;
					material.SetVector("uw_CaptureRect",rect); material.SetTexture("uw_Terrain",m_mask);
						material.SetVector("uw_Bounds",body.CaptureBounds);
					m_commands.DrawMesh(body.LightRays.Mesh,body.transform.localToWorldMatrix,material);
				}
				context.ExecuteCommandBuffer(m_commands);
				foreach (WaterPreview body in m_bodies)
					if (body != null)
					{
						float horizon = camera.WorldToViewportPoint(body.transform.position).y;
						body.SetCapture(m_scene, m_underwater, m_mask, m_rays, rect, horizon);
					}
			}
			catch (Exception ex)
			{
				m_failed = true;
				Debug.LogWarning("[LevelEditor] Water capture unavailable; wave/ripple/physics fallback remains active: " + ex.Message);
				foreach (WaterPreview body in m_bodies) if (body != null) body.ClearCapture();
			}
			finally
			{
				foreach (WaterPreview body in m_bodies)
					if (body != null)
					{
						if (body.Renderer != null) body.Renderer.enabled = body.isActiveAndEnabled;
						if (body.SurfaceRenderer != null) body.SurfaceRenderer.enabled = body.isActiveAndEnabled;
					}
				m_rendering = false;
			}
		}

		private void EnsureTextures(Camera camera)
		{
			int width = Mathf.Max(64, camera.pixelWidth);
			int height = Mathf.Max(64, camera.pixelHeight);
			if (m_scene != null && m_scene.width == width && m_scene.height == height) return;
			ReleaseTextures();
			m_scene = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
			{
				name = "Mod Water Scene", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
				antiAliasing = 1, hideFlags = HideFlags.HideAndDontSave
			};
			m_underwater = new RenderTexture(Mathf.Max(1,width/2),Mathf.Max(1,height/2),0,RenderTextureFormat.ARGB32)
			{ name="Ultimate Water Underwater Capture",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave };
			m_mask = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
			{
				name = "Mod Water Solid Terrain", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
				hideFlags = HideFlags.HideAndDontSave
			};
			m_rays = new RenderTexture(Mathf.Max(1,width/2),Mathf.Max(1,height/2),0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear)
			{ name="Ultimate Water Light Rays",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp };
			m_scene.Create();m_underwater.Create();m_mask.Create();m_rays.Create();
		}

		private void ReleaseTextures()
		{
			if (m_captureCamera != null) m_captureCamera.targetTexture = null;
			foreach (WaterPreview body in m_bodies) if (body != null) body.ClearCapture();
			if (m_scene != null) { m_scene.Release(); Destroy(m_scene); m_scene = null; }
			if (m_underwater != null) { m_underwater.Release(); Destroy(m_underwater); m_underwater = null; }
			if (m_mask != null) { m_mask.Release(); Destroy(m_mask); m_mask = null; }
			if (m_rays != null) { m_rays.Release(); Destroy(m_rays); m_rays = null; }
		}

		private void OnDisable()
		{
			if (m_subscribed) RenderPipelineManager.beginCameraRendering -= BeginCamera;
			m_subscribed = false;
			ReleaseTextures();
		}

		private void OnDestroy()
		{
			OnDisable();
			if (m_commands != null) { m_commands.Dispose(); m_commands = null; }
			if (m_maskMaterial != null) Destroy(m_maskMaterial);
		}
	}
}
