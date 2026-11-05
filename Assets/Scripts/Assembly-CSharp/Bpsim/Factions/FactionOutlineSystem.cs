using System.Collections.Generic;
using Bpsim.Parts;
using Bpsim.Parts.Gameplay;
using Bpsim.Rendering;
using EPOOutline;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

namespace Bpsim.Factions
{
	[UpdateInGroup(typeof(PresentationSystemGroup))]
	[UpdateAfter(typeof(UpdatePresentationSystemGroup))]
	internal partial class FactionOutlineSystem : SystemBase
	{
		private readonly BuildFactionTopology m_buildTeams = new BuildFactionTopology();
		private readonly Dictionary<Entity,Entity> m_roots = new Dictionary<Entity,Entity>();
		private readonly Dictionary<(int Teams,int Atlas),List<EpoFactionMask>> m_masks = new Dictionary<(int,int),List<EpoFactionMask>>();
		private EntityQuery m_renderers;
		private Outliner m_outliner;
		private bool m_cameraForcedTexture;
		private Shader m_targetShader;
		private Material m_baseMaterial;
		private PartScene m_scene;
		private bool m_simulating,m_haveFingerprint;
		private ulong m_fingerprint;
		private float m_nextTopologyCheck;

		protected override void OnCreate()
		{
			m_renderers = GetEntityQuery(new EntityQueryDesc {
				All = new[] { ComponentType.ReadOnly<PartRenderInfo>(),ComponentType.ReadOnly<LocalToWorld>(),
					ComponentType.ReadOnly<MeshRect>(),ComponentType.ReadOnly<MeshUVRect>(),ComponentType.ReadOnly<MaterialColor>() },
				None = new[] { ComponentType.ReadOnly<DisableRendering>() }
			});
		}
		protected override void OnUpdate()
		{
			PartManager manager = PartManager.Instance;
			SimulationSettings settings = UserSettings.Instance?.SimulationSettings;
			if (manager==null || !manager.IsReady || manager.ActiveScene==null || settings==null ||
				(manager.IsSimulating ? !settings.RuntimeFactionOutlines : !settings.BuildFactionOutlines)) { Hide(); return; }
			Camera camera = SceneCamera.Instance!=null ? SceneCamera.Instance.Camera : Camera.main;
			if (camera==null || !camera.isActiveAndEnabled || !ResourcesReady()) { Hide(); return; }
			bool simulating=manager.IsSimulating;
			if (m_scene!=manager.ActiveScene || m_simulating!=simulating)
			{
				m_scene=manager.ActiveScene; m_simulating=simulating; m_roots.Clear();
				m_haveFingerprint=false; m_nextTopologyCheck=0f;
			}
			EntityManager.CompleteAllTrackedJobs();
			if (!simulating)
			{
				if (UnityEngine.Time.unscaledTime>=m_nextTopologyCheck)
				{
					m_nextTopologyCheck=UnityEngine.Time.unscaledTime+.1f;
					ulong fingerprint=Fingerprint(m_scene);
					if (!m_haveFingerprint || fingerprint!=m_fingerprint)
					{
						m_buildTeams.Rebuild(m_scene); m_roots.Clear(); m_fingerprint=fingerprint; m_haveFingerprint=true;
					}
				}
				if (m_buildTeams.MarkerCount==0) { Hide(); return; }
			}
			foreach (var group in m_masks.Values) foreach (var mask in group) mask.Begin();
			using NativeArray<Entity> renderers=m_renderers.ToEntityArray(Allocator.Temp);
			bool any=false;
			foreach (Entity renderer in renderers)
			{
				Entity root=Root(renderer);
				if (root==Entity.Null) continue;
				int teams=simulating ? (EntityManager.HasComponent<PartMarkerTeam>(root) ? EntityManager.GetComponentData<PartMarkerTeam>(root).Teams : 0)
					: m_buildTeams.GetTeams(root);
				if (teams==0 || EntityManager.GetComponentData<MaterialColor>(renderer).Value.w<=.001f) continue;
				float4 rect=EntityManager.GetComponentData<MeshRect>(renderer).Value;
				if (math.any(math.abs(rect.zw)<1e-6f)) continue;
				float4x4 matrix=EntityManager.GetComponentData<LocalToWorld>(renderer).Value;
				if (math.lengthsq(matrix.c0.xyz)<1e-12f || math.lengthsq(matrix.c1.xyz)<1e-12f) continue;
				// A generated frame needs at most 96 vertices; split before overflowing mobile indices.
				int atlas=EntityManager.HasComponent<PartAtlasIndex>(renderer) ? EntityManager.GetComponentData<PartAtlasIndex>(renderer).Value : PartAtlasIndex.Default;
				Texture texture=PartAtlasRegistry.GetMaterial(atlas,m_baseMaterial)?.mainTexture;
				if (texture==null) continue;
				EpoFactionMask mask=GetMask(teams,atlas,texture,96);
				mask.Append(matrix,rect,EntityManager.GetComponentData<MeshUVRect>(renderer).Value,
					EntityManager.HasComponent<BlendFactor>(renderer) ? EntityManager.GetComponentData<BlendFactor>(renderer).Value : float4.zero);
				any=true;
			}
			foreach (var group in m_masks.Values) foreach (var mask in group) mask.Commit();
			if (!any) { Hide(); return; }
			if (m_outliner==null || m_outliner.gameObject!=camera.gameObject)
			{
				DetachOutliner();
				m_cameraForcedTexture=camera.forceIntoRenderTexture;
				m_outliner=camera.gameObject.AddComponent<Outliner>(); m_outliner.hideFlags=HideFlags.DontSave;
			}
			EpoFactionStyle.Apply(m_outliner,settings.FactionOutlineWidth); m_outliner.enabled=true;
		}
		private EpoFactionMask GetMask(int teams,int atlas,Texture texture,int reserve)
		{
			var key=(teams,atlas);
			if (!m_masks.TryGetValue(key,out var group)) { group=new List<EpoFactionMask>(); m_masks.Add(key,group); }
			foreach (var mask in group) if (mask.VertexCount+reserve<=EpoFactionMask.MaxVertices) { mask.SetAtlas(texture); return mask; }
			var created=new EpoFactionMask(teams,texture,m_targetShader); group.Add(created); return created;
		}
		private void Hide()
		{
			DetachOutliner();
			foreach (var group in m_masks.Values) foreach (var mask in group) if (mask.GameObject.activeSelf) mask.GameObject.SetActive(false);
		}
		private void DetachOutliner()
		{
			if (m_outliner==null) return;
			m_outliner.enabled=false;
			Camera camera=m_outliner.GetComponent<Camera>();
			if (camera!=null) camera.forceIntoRenderTexture=m_cameraForcedTexture;
			Object.Destroy(m_outliner); m_outliner=null;
		}
		private bool ResourcesReady()
		{
			if (m_targetShader!=null && m_baseMaterial!=null) return true;
			CoreManager core=CoreManager.Instance;
			if (core==null || core.Resources==null || !core.Resources.TryLoadAsset<Material>("Part_Texture_0",out var atlas)) return false;
			m_targetShader=Resources.Load<Shader>("EpoFactionTarget"); m_baseMaterial=atlas;
			return m_targetShader!=null && m_targetShader.isSupported && m_baseMaterial.mainTexture!=null;
		}
		private Entity Root(Entity renderer)
		{
			if (m_roots.TryGetValue(renderer,out Entity cached) && EntityManager.Exists(cached)) return cached;
			Entity current=renderer;
			for (int depth=0;depth<32 && EntityManager.Exists(current);depth++)
			{
				if (EntityManager.HasComponent<PartTypeValue>(current)) { m_roots[renderer]=current; return current; }
				if (!EntityManager.HasComponent<Parent>(current)) break;
				current=EntityManager.GetComponentData<Parent>(current).Value;
			}
			return Entity.Null;
		}
		private ulong Fingerprint(PartScene scene)
		{
			unchecked
			{
				ulong hash=14695981039346656037UL;
				foreach (Entity part in scene.Unmanaged.Parts)
				{
					if (!EntityManager.Exists(part)) continue;
					var transform=EntityManager.GetComponentData<PartTransform>(part); var type=EntityManager.GetComponentData<PartTypeValue>(part);
					int layer=EntityManager.HasComponent<PartBuildLayer>(part) ? EntityManager.GetComponentData<PartBuildLayer>(part).Value : 1;
					hash=(hash^(uint)part.Index)*1099511628211UL; hash=(hash^(uint)part.Version)*1099511628211UL;
					hash=(hash^math.hash(new int4(transform.X,transform.Y,transform.Rotation,layer)))*1099511628211UL;
					hash=(hash^math.hash(new int3((int)type.Type,type.Index,transform.Flipped ? 1 : 0)))*1099511628211UL;
				}
				return hash;
			}
		}
		protected override void OnDestroy()
		{
			Hide();
			foreach (var group in m_masks.Values) foreach (var mask in group) mask.Dispose(); m_masks.Clear(); m_roots.Clear();
		}
	}
}
