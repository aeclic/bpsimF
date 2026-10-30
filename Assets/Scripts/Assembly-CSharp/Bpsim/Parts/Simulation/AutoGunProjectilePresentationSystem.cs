using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Material = UnityEngine.Material;

namespace Bpsim.Parts.Simulation
{
	/// <summary>每个显示帧绘制弹丸，避免固定物理步之间出现一闪一闪的空帧。</summary>
	[UpdateInGroup(typeof(PresentationSystemGroup))]
	internal partial class AutoGunProjectilePresentationSystem : SystemBase
	{
		private const float MarkerSize = 0.22f;
		private const float VisualZ = -0.7f;
		private static readonly Color MarkerColor = new Color(0f, 1f, 1f, 0.92f);
		private static Mesh s_quad;
		private static Material s_material;
		private static MaterialPropertyBlock s_properties;
		private static readonly int ShaderColorID = Shader.PropertyToID("_Color");
		private EntityQuery m_projectileQuery;

		protected override void OnCreate()
		{
			EntityQueryBuilder builder = new EntityQueryBuilder(Allocator.Temp);
			m_projectileQuery = builder.WithAll<AutoGunProjectile>().Build(ref base.CheckedStateRef);
		}

		protected override void OnUpdate()
		{
			PartManager manager = PartManager.Instance;
			if (manager == null || !manager.IsSimulating)
			{
				return;
			}
			using NativeArray<AutoGunProjectile> projectiles = m_projectileQuery.ToComponentDataArray<AutoGunProjectile>(Allocator.Temp);
			for (int i = 0; i < projectiles.Length; i++)
			{
				Draw(projectiles[i]);
			}
		}

		private static void Draw(in AutoGunProjectile projectile)
		{
			Material material = GetMaterial();
			if (material == null)
			{
				return;
			}
			float3 position = projectile.Position;
			position.z = VisualZ;
			Matrix4x4 matrix = Matrix4x4.TRS(position, Quaternion.identity, new Vector3(MarkerSize, MarkerSize, 1f));
			RenderParams renderParams = new RenderParams(material)
			{
				worldBounds = new Bounds(position, new Vector3(MarkerSize * 2f, MarkerSize * 2f, 0.5f)),
				shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
				receiveShadows = false,
				matProps = GetProperties()
			};
			Graphics.RenderMesh(in renderParams, GetQuad(), 0, matrix);
			AutoGunProjectileSystem.DrawnProjectiles++;
		}

		private static MaterialPropertyBlock GetProperties()
		{
			s_properties ??= new MaterialPropertyBlock();
			s_properties.SetColor(ShaderColorID, MarkerColor);
			return s_properties;
		}

		private static Mesh GetQuad()
		{
			if (s_quad != null)
			{
				return s_quad;
			}
			Mesh mesh = new Mesh { name = "AutoGunProjectileQuad", hideFlags = HideFlags.DontSave };
			mesh.SetVertices(new List<Vector3>
			{
				new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
				new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f)
			});
			mesh.SetUVs(0, new List<Vector2>
			{
				new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f)
			});
			mesh.SetColors(new List<Color> { Color.white, Color.white, Color.white, Color.white });
			mesh.SetTriangles(new List<int> { 0, 1, 2, 0, 2, 3 }, 0, false);
			mesh.RecalculateBounds();
			s_quad = mesh;
			return s_quad;
		}

		private static Material GetMaterial()
		{
			if (s_material != null)
			{
				return s_material;
			}
			Shader shader = Shader.Find("Bpsim/Unlit_Texture") ?? Shader.Find("Sprites/Default");
			if (shader == null)
			{
				return null;
			}
			s_material = new Material(shader)
			{
				name = "AutoGunProjectileMaterial",
				hideFlags = HideFlags.DontSave,
				mainTexture = Texture2D.whiteTexture
			};
			return s_material;
		}
	}
}
