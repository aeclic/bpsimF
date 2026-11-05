using System;
using System.Collections.Generic;
using EPOOutline;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bpsim.Factions
{
	/// <summary>One combined alpha-cutout target per faction/atlas chunk, not one GameObject per part.</summary>
	public sealed class EpoFactionMask : IDisposable
	{
		public const int OutlineLayer = 63;
		// Stay within UInt16 indices on Android too, even for a huge assembly.
		public const int MaxVertices = 60000;
		private readonly List<Vector3> m_vertices = new List<Vector3>();
		private readonly List<Vector2> m_uvs = new List<Vector2>();
		private readonly List<int> m_indices = new List<int>();
		private readonly Material m_material;
		public GameObject GameObject { get; }
		public Mesh Mesh { get; }
		public Outlinable Outlinable { get; }
		public void SetAtlas(Texture texture) => m_material.mainTexture=texture;
		public int VertexCount => m_vertices.Count;

		public EpoFactionMask(int teams, Texture atlas, Shader targetShader)
		{
			GameObject = new GameObject("[EPO faction " + teams + "]") { hideFlags = HideFlags.HideAndDontSave };
			Mesh = new Mesh { name = "EPO faction silhouette", hideFlags = HideFlags.HideAndDontSave };
			Mesh.MarkDynamic();
			GameObject.AddComponent<MeshFilter>().sharedMesh = Mesh;
			m_material = new Material(targetShader) { mainTexture = atlas, hideFlags = HideFlags.HideAndDontSave };
			MeshRenderer renderer = GameObject.AddComponent<MeshRenderer>();
			renderer.sharedMaterial = m_material;
			renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
			Outlinable = GameObject.AddComponent<Outlinable>();
			Outlinable.OutlineLayer = OutlineLayer;
			Outlinable.OutlineParameters.Color = FactionRules.OutlineColor(teams);
			Outlinable.OutlineParameters.BlurShift = .65f;
			Outlinable.AddTarget(new OutlineTarget(renderer, "_MainTex", .08f) { CullMode = CullMode.Off, CutoutMask = EPOOutline.ColorMask.A });
		}

		public void Begin() { m_vertices.Clear(); m_uvs.Clear(); m_indices.Clear(); }
		public void Commit()
		{
			if (m_indices.Count == 0) { GameObject.SetActive(false); return; }
			Mesh.Clear(); Mesh.SetVertices(m_vertices); Mesh.SetUVs(0, m_uvs); Mesh.SetTriangles(m_indices, 0, true);
			GameObject.SetActive(true);
		}

		public void Append(float4x4 matrix, float4 rect, float4 atlasRect, float4 blend)
		{
			if (blend.z < .5f)
			{
				int start = m_vertices.Count;
				Vertex(new float2(0,0),matrix,rect,atlasRect,blend); Vertex(new float2(1,0),matrix,rect,atlasRect,blend);
				Vertex(new float2(1,1),matrix,rect,atlasRect,blend); Vertex(new float2(0,1),matrix,rect,atlasRect,blend);
				Triangle(start,start+1,start+2); Triangle(start,start+2,start+3); return;
			}
			// Match the original generated-frame shader's triangle / semicircle / circle silhouette.
			bool filled = blend.w >= .5f;
			int sides = blend.z < 1.5f ? 3 : 48;
			int origin = m_vertices.Count;
			if (filled)
			{
				float2 center = blend.z < 1.5f ? new float2(1f/3f,2f/3f) : blend.z < 2.5f ? new float2(.5f,.2f) : new float2(.5f,.5f);
				Vertex(center,matrix,rect,atlasRect,blend);
				for (int i=0; i<sides; i++) Vertex(Boundary(i,sides,blend.z,false),matrix,rect,atlasRect,blend);
				for (int i=0; i<sides; i++) Triangle(origin,origin+1+i,origin+1+(i+1)%sides);
			}
			else
			{
				for (int i=0; i<sides; i++)
				{
					Vertex(Boundary(i,sides,blend.z,false),matrix,rect,atlasRect,blend);
					Vertex(Boundary(i,sides,blend.z,true),matrix,rect,atlasRect,blend);
				}
				for (int i=0; i<sides; i++)
				{
					int a=origin+2*i, b=origin+2*((i+1)%sides);
					Triangle(a,b,b+1); Triangle(a,b+1,a+1);
				}
			}
		}

		private static float2 Boundary(int i,int count,float shape,bool inner)
		{
			if (shape < 1.5f)
			{
				if (!inner) return i==0 ? float2.zero : i==1 ? new float2(0,1) : new float2(1,1);
				return i==0 ? new float2(.13f,.13f+.13f/.70710678f) : i==1 ? new float2(.13f,.87f) : new float2(.87f-.13f/.70710678f,.87f);
			}
			float radius = inner ? .37f : .5f;
			float angle;
			float2 center;
			if (shape < 2.5f)
			{
				float inset = inner ? math.asin(.13f/radius) : 0f;
				angle = math.lerp(inset,math.PI-inset,(float)i/(count-1)); center = new float2(.5f,0);
			}
			else { angle = 2f*math.PI*i/count; center = new float2(.5f,.5f); }
			return center+new float2(math.cos(angle),math.sin(angle))*radius;
		}
		private void Vertex(float2 p,float4x4 matrix,float4 rect,float4 uv,float4 blend)
		{
			float2 sample = p;
			if (blend.z>.5f && blend.w<.5f)
			{
				float edge = blend.z<1.5f ? math.min(math.min(p.x,1-p.y),(p.y-p.x)*.70710678f)
					: blend.z<2.5f ? math.min(p.y,.5f-math.length(new float2(p.x-.5f,p.y))) : .5f-math.length(p-.5f);
				sample = new float2(p.x,math.clamp(edge,0,.13f));
			}
			float2 local = (p-.5f)*rect.zw+rect.xy;
			m_vertices.Add(math.transform(matrix,new float3(local,0)));
			// Half-texel inset prevents bilinear sampling of adjacent atlas sprites.
			float2 texel = new float2(.5f/m_material.mainTexture.width,.5f/m_material.mainTexture.height);
			m_uvs.Add(math.clamp(uv.xy+sample*uv.zw,uv.xy+texel,uv.xy+uv.zw-texel));
		}
		private void Triangle(int a,int b,int c) { m_indices.Add(a); m_indices.Add(b); m_indices.Add(c); }
		public void Dispose()
		{
			GameObject.SetActive(false);
			if (Application.isPlaying) { UnityEngine.Object.Destroy(GameObject); UnityEngine.Object.Destroy(Mesh); UnityEngine.Object.Destroy(m_material); }
			else { UnityEngine.Object.DestroyImmediate(GameObject); UnityEngine.Object.DestroyImmediate(Mesh); UnityEngine.Object.DestroyImmediate(m_material); }
		}
	}
}
