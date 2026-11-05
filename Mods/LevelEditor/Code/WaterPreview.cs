using Bpsim.Modding;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bpsim.LevelEditor
{
	/// <summary>Independent 2D water mesh, bounded spring ripples and optional ECS fluid registration.</summary>
	public sealed class WaterPreview : MonoBehaviour
	{
		private Mesh m_mesh;
		private Material m_material;
		private Material m_surfaceMaterial;
		private Mesh m_surfaceMesh;
		private MeshRenderer m_surfaceRenderer;
		private bool m_original;
		private WaterRipples m_ripples;
		private Material m_mirrorMaterial;
		private RenderTexture m_reflection;
		public WaterLightRays LightRays { get; private set; }
		public MeshRenderer SurfaceRenderer { get { return m_surfaceRenderer; } }
		private Vector3[] m_vertices;
		private Vector2[] m_surfaceData;
		private Color[] m_colors;
		private float[] m_heights, m_velocities, m_accelerations;
		private readonly float[] m_contactTimes = new float[32];
		private WaterSettings m_settings;
		private ModFluidVolume m_volume;
		private ModFluidHandle m_fluid;
		private float m_scale, m_width, m_depth, m_spacing;
		private int m_segments;
		private bool m_advanced;
		public MeshRenderer Renderer { get; private set; }
		public int RippleCount { get; private set; }
		public int ReceivedContacts { get; private set; }
		public int InactiveContacts { get; private set; }
		public int CooldownContacts { get; private set; }
		public bool NeedsCapture { get { return m_advanced; } }
		public float MaximumRipple { get; private set; }
		public float ReflectionHorizon { get; private set; }
		public Vector4 CaptureBounds { get { return new Vector4(transform.position.x,transform.position.y,m_width,m_depth); } }

		public static WaterPreview Create(Transform parent, JObject body, Vector3 anchor, float scale,
			WaterAssets assets, ModFluidSession fluids, WaterWorld world)
		{
			GameObject go = new GameObject("LevelEditorWater_" + (string)body["id"]);
			go.transform.SetParent(parent, false);
			go.transform.position = new Vector3((float)body["x"] * scale - anchor.x,
				(float)body["surface_y"] * scale - anchor.y, -.2f);
			WaterPreview water = go.AddComponent<WaterPreview>();
			water.Build(WaterSettings.Read(body), scale, assets, fluids);
			world.Add(water);
			return water;
		}

		private void Build(WaterSettings settings, float scale, WaterAssets assets, ModFluidSession fluids)
		{
			m_settings = settings; m_scale = scale;
			m_width = settings.Width * scale; m_depth = settings.Depth * scale;
			m_segments = Mathf.Clamp(Mathf.CeilToInt(settings.Width * 32f), 48, 16384);
			m_spacing = m_width / m_segments;
			m_heights = new float[m_segments + 1]; m_velocities = new float[m_segments + 1]; m_accelerations = new float[m_segments + 1];
			for (int i = 0; i < m_contactTimes.Length; i++) m_contactTimes[i] = -10f;
			m_vertices = new Vector3[(m_segments + 1) * 2];
			m_surfaceData = new Vector2[m_vertices.Length];
			Vector2[] uv = new Vector2[m_vertices.Length];
			Color[] colors = new Color[m_vertices.Length];
			m_colors = colors;
			int[] indices = new int[m_segments * 6];
			for (int i = 0; i <= m_segments; i++)
			{
				float fraction = (float)i / m_segments;
				int bottom = i + m_segments + 1;
				m_vertices[i] = new Vector3(fraction * m_width, 0, 0);
				m_vertices[bottom] = new Vector3(fraction * m_width, -m_depth, 0);
				uv[i] = new Vector2(fraction, 1); uv[bottom] = new Vector2(fraction, 0);
				colors[i] = colors[bottom] = Color.white;
				if (i == m_segments) continue;
				int offset = i * 6;
				indices[offset] = i; indices[offset + 1] = bottom; indices[offset + 2] = i + 1;
				indices[offset + 3] = i + 1; indices[offset + 4] = bottom; indices[offset + 5] = bottom + 1;
			}
			m_mesh = new Mesh { name = "Mod Interactive Water" };
			m_mesh.MarkDynamic();
			m_mesh.vertices = m_vertices; m_mesh.uv = uv; m_mesh.uv2 = m_surfaceData;
			m_mesh.colors = colors; m_mesh.triangles = indices;
			float maxHeight = Mathf.Min(m_depth * .45f, settings.WaveAmplitude * scale) +
				Mathf.Min(m_depth * .4f, settings.RippleStrength * 4f * scale);
			m_mesh.bounds = new Bounds(new Vector3(m_width * .5f, -m_depth * .5f, 0), new Vector3(m_width, m_depth + maxHeight * 2f, .1f));
			gameObject.AddComponent<MeshFilter>().sharedMesh = m_mesh;
			m_advanced = assets.HasAdvancedEffects;
			m_original = m_advanced && assets.HasOriginalPrograms;
			Shader shader = m_original ? assets.UnderwaterShader : m_advanced ? assets.WaterShader : Shader.Find("Bpsim/Unlit_Texture");
			if (shader != null)
			{
				m_material = m_original ? assets.CreateOriginalMaterial(false) : new Material(shader);
				m_material.name = "Mod Water " + settings.Name; m_material.renderQueue = 3080;
				Color color = settings.Color; color.a = settings.Opacity;
				if (!m_original) m_material.SetColor("_Color", color);
				if (m_advanced && !m_original)
				{
					m_material.SetVector("_WaterBounds", new Vector4(transform.position.x, transform.position.y, m_width, m_depth));
					m_material.SetVector("_Effects", new Vector4(Mathf.Clamp(settings.Refraction * scale, 0f, 4f), settings.Caustics,
						Mathf.Clamp01(settings.Reflection), settings.Foam));
					ApplyUltimateWaterLook(assets, settings, color);
				}
				else if (!m_original) m_material.SetTexture("_MainTex", Texture2D.whiteTexture);
				Renderer = gameObject.AddComponent<MeshRenderer>();
				Renderer.sharedMaterial = m_material;
				Renderer.shadowCastingMode = ShadowCastingMode.Off; Renderer.receiveShadows = false;
				Renderer.lightProbeUsage = LightProbeUsage.Off; Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
				if (m_original)
				{
					if (assets.MirrorShader != null) m_mirrorMaterial = new Material(assets.MirrorShader);
					if (assets.LightRaysShader != null && assets.LightRaysShader.isSupported && assets.LightRaysGeometry != null)
						LightRays = new WaterLightRays(assets,m_width,m_depth,scale);
					// Draw the asset surface behind BPSIM parts; the underwater pass composites last.
					m_surfaceMaterial = assets.CreateOriginalMaterial(true); m_surfaceMaterial.renderQueue = 2999;
					m_ripples=new WaterRipples();
					Vector4 bounds = new Vector4(transform.position.x, transform.position.y, m_width, m_depth);
					assets.SetLightingBounds(m_material,"Underwater",bounds,scale/8f);assets.SetLightingBounds(m_surfaceMaterial,"WaterSurface",bounds,scale/8f);
					foreach (Material layer in new[] { m_material, m_surfaceMaterial })
					{
						layer.SetVector("uw_Bounds", bounds); layer.SetFloat("uw_Scale", scale / 8f); layer.SetFloat("uw_SurfaceDepth", 294.121521f);
						layer.SetFloat("_WaveAmplitude",settings.WaveAmplitude*8f);layer.SetFloat("_WaveFrequency",Mathf.PI*2f/(settings.WaveLength*8f));layer.SetFloat("_WaveSpeed",settings.WaveSpeed);
					}
					GameObject surface = new GameObject("Ultimate Water 2.5D Surface"); surface.transform.SetParent(transform, false);
					m_surfaceMesh = assets.CreateSurfaceMesh(m_width,scale/8f);
					surface.AddComponent<MeshFilter>().sharedMesh = m_surfaceMesh;
					m_surfaceRenderer = surface.AddComponent<MeshRenderer>(); m_surfaceRenderer.sharedMaterial = m_surfaceMaterial;
					m_surfaceRenderer.sortingLayerID = Renderer.sortingLayerID;
					m_surfaceRenderer.sortingOrder = Renderer.sortingOrder + 1;
				}
			}
			m_volume = new ModFluidVolume
			{
				Left = transform.position.x, Surface = transform.position.y, Width = m_width, Depth = m_depth,
				Density = settings.Density, BuoyancyMultiplier = settings.Buoyancy, LinearDrag = settings.LinearDrag, AngularDrag = settings.AngularDrag,
				Flow = new float2(settings.FlowX, settings.FlowY) * scale, FlowAcceleration = settings.FlowAcceleration * scale,
				WaveAmplitude = settings.WaveAmplitude * scale, WaveLength = settings.WaveLength * scale, WaveSpeed = settings.WaveSpeed,
				PhysicsEnabled = (byte)(settings.PhysicsEnabled ? 1 : 0), CaptureContacts = (byte)(settings.RipplesEnabled ? 1 : 0)
			};
			if (settings.PhysicsEnabled || settings.RipplesEnabled) m_fluid = fluids.Add(m_volume, AddContact);
			UpdateMesh();
		}

		/// <summary>Applies the same extracted Ultimate Water 2D material profile used by the web renderer.</summary>
		private void ApplyUltimateWaterLook(WaterAssets assets, WaterSettings settings, Color tint)
		{
			JObject surface = assets.Style == null ? null : assets.Style["surface"] as JObject;
			JObject underwater = assets.Style == null ? null : assets.Style["underwater"] as JObject;
			m_material.SetTexture("_NormalTexture", assets.NormalTexture != null ? assets.NormalTexture : Texture2D.grayTexture);
			m_material.SetTexture("_FoamTexture", assets.FoamTexture != null ? assets.FoamTexture : Texture2D.blackTexture);
			m_material.SetTexture("_CausticTexture", assets.CausticTexture != null ? assets.CausticTexture : Texture2D.blackTexture);
			m_material.SetTexture("_SurfaceFoamTexture", assets.SurfaceFoamTexture != null ? assets.SurfaceFoamTexture : Texture2D.blackTexture);
			m_material.SetColor("_WaterColor", tint);
			m_material.SetColor("_BaseFarColor", ReadColor(surface, "baseFarColor", new Color(0f, .8068f, 1f, 1f)));
			m_material.SetColor("_UnderwaterTopColor", ReadColor(underwater, "topColor", Color.cyan));
			m_material.SetColor("_UnderwaterBottomColor", ReadColor(underwater, "bottomColor", Color.blue));
			m_material.SetFloat("_Opacity", settings.Opacity);
			m_material.SetFloat("_NormalStrength", ReadNumber(surface, "normalStrength", .251f));
			m_material.SetFloat("_Reflectivity", ReadNumber(surface, "reflectivity", .663f));
			m_material.SetFloat("_GradientHeight", Mathf.Max(.05f, ReadNumber(surface, "baseGradientDistance", 1.18f) * m_scale));
			m_material.SetFloat("_GradientFade", ReadNumber(surface, "baseGradientFade", .16f));
			m_material.SetVector("_NormalsTiling", ToVector4(ReadVector(surface, "normalsTiling", new Vector2(6.9f, 32.27f))));
			m_material.SetVector("_NormalTextureScale", ToVector4(ReadVector(surface, "normalTextureScale", new Vector2(1f, -.98f))));
			m_material.SetVector("_WaterSpeed", ToVector4(ReadVector(surface, "waterSpeed", new Vector2(.04f, 0f))));
			m_material.SetVector("_FoamTiling", ToVector4(ReadVector(surface, "foamTextureSize", new Vector2(1.23f, 2.96f))));
			m_material.SetVector("_FoamScrollSpeed", ToVector4(ReadVector(surface, "foamTextureScrollSpeed", new Vector2(.04f, 0f))));
			m_material.SetVector("_CausticTiling", ToVector4(ReadVector(surface, "rippleFoamTextureTiling", new Vector2(3f, 2f))));
			m_material.SetVector("_FoamStyle", new Vector4(ReadNumber(surface, "foamSharpness", 5.35f),
				ReadNumber(surface, "foamTextureOpacity", 1.83f), ReadNumber(surface, "rippleFoamWidth", .86f),
				ReadNumber(surface, "foamVoronoiOpacity", .598f)));
			m_material.SetVector("_UnderwaterStyle", new Vector4(ReadNumber(underwater, "gradientHeight", 5.08f) * m_scale,
				ReadNumber(underwater, "distortionStrength", .13f), 0f, 0f));
		}

		private static float ReadNumber(JObject source, string key, float fallback)
		{
			return source == null || source[key] == null ? fallback : (float)source[key];
		}

		private static Vector2 ReadVector(JObject source, string key, Vector2 fallback)
		{
			JArray value = source == null ? null : source[key] as JArray;
			return value == null || value.Count < 2 ? fallback : new Vector2((float)value[0], (float)value[1]);
		}

		private static Vector4 ToVector4(Vector2 value) { return new Vector4(value.x, value.y, 0f, 0f); }

		private static Color ReadColor(JObject source, string key, Color fallback)
		{
			JArray value = source == null ? null : source[key] as JArray;
			return value == null || value.Count < 4 ? fallback : new Color((float)value[0], (float)value[1], (float)value[2], (float)value[3]);
		}

		public void AddContact(ModFluidContact contact)
		{
			ReceivedContacts++;
			if (!m_settings.RipplesEnabled || m_settings.RippleStrength <= 0 || !isActiveAndEnabled) { InactiveContacts++; return; }
			float fraction = Mathf.Clamp01((contact.Position.x - transform.position.x) / m_width);
			int bin = Mathf.Clamp(Mathf.FloorToInt(fraction * 32f), 0, 31);
			float now = Time.time;
			if (now - m_contactTimes[bin] < .12f) { CooldownContacts++; return; }
			m_contactTimes[bin] = now;
			if(m_original)
			{
				float gameSpeed = contact.Speed / Mathf.Max(m_scale, .001f) + (contact.Entering ? .5f : 0f);
				float strength = m_settings.RippleStrength * Mathf.Clamp(gameSpeed, .1f, 4f) / .06f;
				m_ripples.Add((contact.Position.x-transform.position.x-m_width*.5f)/(m_scale/8f),1.6f,now,Mathf.Clamp(strength,.1f,4f));
			}
			int center = Mathf.RoundToInt(fraction * m_segments);
			int radius = Mathf.Clamp(Mathf.CeilToInt(contact.Radius / m_spacing * .5f), 1, 12);
			float impulse = m_settings.RippleStrength * m_scale * Mathf.Clamp(contact.Speed, .3f, 4f) * (contact.Entering ? -3f : 2f);
			for (int i = Mathf.Max(0, center - radius); i <= Mathf.Min(m_segments, center + radius); i++)
			{
				float distance = (float)(i - center) / radius;
				m_velocities[i] = Mathf.Clamp(m_velocities[i] + impulse * Mathf.Exp(-distance * distance * 3f), -m_depth * 4f, m_depth * 4f);
			}
			RippleCount++;
		}

		private void Update()
		{
			if (m_mesh == null) return;
			float dt = Mathf.Min(Time.deltaTime, .08f);
			float speed = m_settings.RippleSpeed * m_scale;
			float step = Mathf.Min(1f / 120f, m_spacing / Mathf.Max(speed, .01f) * .4f);
			int substeps = Mathf.Clamp(Mathf.CeilToInt(dt / Mathf.Max(step, .0001f)), 1, 24);
			float h = Mathf.Min(step, dt / substeps), stiffness = speed * speed / (m_spacing * m_spacing);
			float damping = Mathf.Exp(-m_settings.RippleDamping * h);
			float maxRipple = Mathf.Min(m_depth * .4f, Mathf.Max(.001f, m_settings.RippleStrength * m_scale * 4f));
			for (int substep = 0; substep < substeps && h > 0; substep++)
			{
				for (int i = 0; i <= m_segments; i++)
					m_accelerations[i] = stiffness * (m_heights[Mathf.Max(0, i - 1)] + m_heights[Mathf.Min(m_segments, i + 1)] - 2f * m_heights[i]);
				for (int i = 0; i <= m_segments; i++)
				{
					m_velocities[i] = (m_velocities[i] + m_accelerations[i] * h) * damping;
					m_heights[i] = Mathf.Clamp(m_heights[i] + m_velocities[i] * h, -maxRipple, maxRipple);
				}
			}
			UpdateMesh();
		}

		private void UpdateMesh()
		{
			MaximumRipple = 0;
			float time = Time.time;
			for (int i = 0; i <= m_segments; i++)
			{
				float worldX = transform.position.x + i * m_spacing;
				float surface = ModFluidMath.SurfaceAt(m_volume, worldX, time) + m_heights[i];
				m_vertices[i].y = m_original ? 0 : surface - transform.position.y;
				MaximumRipple = Mathf.Max(MaximumRipple, Mathf.Abs(m_heights[i]));
				m_surfaceData[i] = m_surfaceData[i + m_segments + 1] = new Vector2(surface, m_velocities[i]);
			}
			// Bake the ripple slope and amplitude into vertex colours: the shader tilts the
			// surface normal along the slope and adds crest foam from the amplitude.
			float rippleScale = 1f / Mathf.Max(MaximumRipple, .0005f);
			for (int i = 0; i <= m_segments; i++)
			{
				int left = Mathf.Max(0, i - 1), right = Mathf.Min(m_segments, i + 1);
				float slope = (m_heights[right] - m_heights[left]) / Mathf.Max(.0001f, (right - left) * m_spacing);
				m_colors[i] = m_colors[i + m_segments + 1] = new Color(
					Mathf.Clamp(slope * .35f + .5f, 0f, 1f),
					Mathf.Clamp01(Mathf.Abs(m_heights[i]) * rippleScale), 0f, 1f);
			}
			m_mesh.vertices = m_vertices; m_mesh.uv2 = m_surfaceData; m_mesh.colors = m_colors;
			if (m_original)
			{
				if (LightRays != null) LightRays.Update(time,m_width);
				m_ripples.Update(time,m_surfaceMaterial,m_material);
				m_material.SetFloat("uw_Clock", time); m_surfaceMaterial.SetFloat("uw_Clock", time);
			}
			if (m_material != null && m_advanced && !m_original)
				m_material.SetVector("_Animation", new Vector4(time, m_settings.FlowX * m_scale, m_settings.FlowY * m_scale, m_scale));
		}

		public void SetCapture(RenderTexture scene, RenderTexture underwater, RenderTexture terrain, RenderTexture rays, Vector4 rect, float reflectionHorizon)
		{
			if (!m_advanced || m_material == null) return;
			ReflectionHorizon = reflectionHorizon;
			if (!m_original)
			{
				m_material.SetTexture("_SceneTex", scene); m_material.SetTexture("_TerrainTex", terrain);
				m_material.SetVector("_CaptureRect", rect);
				m_material.SetVector("_CaptureTexelSize", new Vector4(1f / scene.width, 1f / scene.height, scene.width, scene.height));
				m_material.SetFloat("_HasCapture", 1);
			}
			if (m_original) foreach (Material layer in new[] { m_material, m_surfaceMaterial })
			{
				layer.SetTexture("_CameraSortingLayerTexture", scene);
				layer.SetTexture("_LocalUnderwaterSurfaceMap", underwater);
				layer.SetTexture("uw_Terrain", terrain); layer.SetVector("uw_CaptureRect", rect);
				if (layer.HasProperty("uw_Rays")) layer.SetTexture("uw_Rays",rays);
			}
			if (m_original && m_mirrorMaterial != null)
			{
				int width = Mathf.Max(1, scene.width / 2), height = Mathf.Max(1, scene.height / 2);
				if (m_reflection == null || m_reflection.width != width || m_reflection.height != height)
				{
					ReleaseReflection();
					m_reflection = new RenderTexture(width,height,0,RenderTextureFormat.ARGB32) { name="Ultimate Water Reflection", filterMode=FilterMode.Bilinear, wrapMode=TextureWrapMode.Clamp };
					m_reflection.Create();
				}
				m_mirrorMaterial.SetFloat("_Horizon", ReflectionHorizon);
				Graphics.Blit(scene,m_reflection,m_mirrorMaterial);
				m_surfaceMaterial.SetTexture("_LocalReflectionMap",m_reflection);
			}
		}

		public void ClearCapture()
		{
			if (!m_advanced || m_material == null) return;
			if (m_original) return;
			m_material.SetFloat("_HasCapture", 0);
			m_material.SetTexture("_SceneTex", null); m_material.SetTexture("_TerrainTex", null);
		}

		private void OnDisable()
		{
			ReleaseReflection();
			if (m_fluid != null) { m_fluid.Dispose(); m_fluid = null; }
			if (Renderer != null) Renderer.enabled = false;
			if (m_surfaceRenderer != null) m_surfaceRenderer.enabled = false;
		}

		private void ReleaseReflection()
		{
			if (m_reflection == null) return;
			m_reflection.Release(); Destroy(m_reflection); m_reflection = null;
		}

		private void OnDestroy()
		{
			OnDisable();
			if (m_mesh != null) Destroy(m_mesh);
			if (m_surfaceMesh != null) Destroy(m_surfaceMesh);
			if (m_surfaceMaterial != null) Destroy(m_surfaceMaterial);
			if (m_mirrorMaterial != null) Destroy(m_mirrorMaterial);
			if (LightRays != null) { LightRays.Dispose(); LightRays=null; }
			if (m_material != null) Destroy(m_material);
			if (m_ripples != null) m_ripples.Dispose();
		}
	}
}
