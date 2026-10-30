using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using Collider = Unity.Physics.Collider;
using Material = UnityEngine.Material;
using MeshCollider = Unity.Physics.MeshCollider;

namespace Bpsim.Levels
{
	/// <summary>Everything one imported level owns, so it can be torn down again.</summary>
	internal sealed class LevelImportResult
	{
		public GameObject Root;

		public string Scene;

		/// <summary>World-space bounds of the level's terrain pieces.</summary>
		public Bounds TerrainBounds;

		public bool HasTerrainBounds;

		/// <summary>World-space position the contraption should be built at.</summary>
		public Vector3 Start;

		public int TerrainCount;

		public int PropCount;

		public readonly List<Entity> Entities = new List<Entity>();

		public readonly List<BlobAssetReference<Collider>> Colliders = new List<BlobAssetReference<Collider>>();

		internal void Encapsulate(Vector3 center, Vector3 size)
		{
			Bounds bounds = new Bounds(center, size);
			if (!HasTerrainBounds)
			{
				TerrainBounds = bounds;
				HasTerrainBounds = true;
			}
			else
			{
				TerrainBounds.Encapsulate(bounds);
			}
		}
	}

	/// <summary>
	/// Turns a parsed BPLE level into GameObjects and static Unity.Physics colliders.
	/// </summary>
	/// <remarks>
	/// This is the bpsim counterpart of the main project's <c>LevelLoader.Awake</c>: it
	/// reproduces the transforms, the terrain fill/curve meshes and the terrain collision
	/// mesh, and drops everything the game builds out of prefabs that bpsim has no
	/// equivalent for (camera rigs, challenge triggers, sprite-animated props it cannot
	/// render). The whole level is shifted so that its <c>LevelStart</c> marker lands on
	/// the world origin, which is where a bpsim scene places its parts.
	/// </remarks>
	internal static class LevelImporter
	{
		/// <summary>
		/// Pushes the level behind the part plane. The main project layers a level as
		/// "smaller z = nearer": background sets sit at z = +15, terrain at 0, props at
		/// -5. bpsim parts live at z = 0, so the level has to move back far enough that
		/// even the props (the nearest layer of a level, raw z = -5) stay behind them.
		/// </summary>
		public const float DepthOffset = 6f;

		private const float PixelsPerWorldUnit = 768f / 20f;

		public static LevelImportResult Import(LevelEntryJson entry, Transform parent)
		{
			byte[] bytes = LevelCatalog.ReadLevelBytes(entry);
			if (bytes == null)
			{
				throw new InvalidOperationException($"level payload for '{entry?.scene}' is missing");
			}
			List<LevelObject> objects = LevelDocumentParser.Parse(bytes);
			LevelImportResult result = new LevelImportResult
			{
				Scene = entry.scene
			};
			// The main project's LevelLoader places every node at its absolute world
			// position (parents carry no scale), so the shift is applied to each node
			// rather than to the hierarchy root.
			Vector3 anchor = FindAnchor(objects);
			Vector3 offset = new Vector3(0f - anchor.x, 0f - anchor.y, DepthOffset);
			GameObject root = new GameObject("BpsimLevel_" + entry.scene);
			root.transform.SetParent(parent, false);
			result.Root = root;
			result.Start = new Vector3(0f, 0f, DepthOffset);
			foreach (LevelObject obj in objects)
			{
				BuildObject(obj, root.transform, entry, result, offset);
			}
			if (!result.HasTerrainBounds)
			{
				result.TerrainBounds = new Bounds(Vector3.zero, new Vector3(200f, 100f, 0f));
				result.HasTerrainBounds = true;
			}
			return result;
		}

		public static void Destroy(LevelImportResult result)
		{
			if (result == null)
			{
				return;
			}
			World world = World.DefaultGameObjectInjectionWorld;
			if (world != null && world.IsCreated)
			{
				EntityManager entityManager = world.EntityManager;
				foreach (Entity entity in result.Entities)
				{
					if (entityManager.Exists(entity))
					{
						entityManager.DestroyEntity(entity);
					}
				}
			}
			result.Entities.Clear();
			foreach (BlobAssetReference<Collider> collider in result.Colliders)
			{
				if (collider.IsCreated)
				{
					collider.Dispose();
				}
			}
			result.Colliders.Clear();
			if (result.Root != null)
			{
				UnityEngine.Object.Destroy(result.Root);
				result.Root = null;
			}
		}

		private static Vector3 FindAnchor(List<LevelObject> objects)
		{
			foreach (LevelObject obj in objects)
			{
				LevelObject start = obj.Find("LevelStart");
				if (start != null)
				{
					return start.Position;
				}
			}
			return Vector3.zero;
		}

		private static void BuildObject(LevelObject obj, Transform parent, LevelEntryJson entry, LevelImportResult result, Vector3 offset)
		{
			GameObject gameObject = new GameObject(obj.Name);
			gameObject.transform.SetParent(parent, false);
			gameObject.transform.position = obj.Position + offset;
			if (obj.IsGroup)
			{
				foreach (LevelObject child in obj.Children)
				{
					BuildObject(child, gameObject.transform, entry, result, offset);
				}
				return;
			}
			gameObject.transform.rotation = Quaternion.Euler(obj.Euler);
			gameObject.transform.localScale = obj.Scale;
			string prefabName = GetPrefabName(entry, obj.PrefabIndex);
			LevelPrefabJson prefab = LevelCatalog.FindPrefab(prefabName);
			if (obj.Terrain != null)
			{
				BuildTerrain(obj, gameObject, entry, prefab, result);
			}
			else if (prefab != null && prefab.kind == "sprite")
			{
				BuildSprite(gameObject, prefab, entry);
				result.PropCount++;
			}
			else if (prefab != null && prefab.kind == "compound")
			{
				BuildCompound(gameObject, prefab, result);
			}
			// Everything else is a marker or a scripted prop (LevelStart, DessertPlace,
			// challenge triggers, cameras): it has no drawable geometry in bpsim.
		}

		private static void BuildTerrain(LevelObject obj, GameObject gameObject, LevelEntryJson entry, LevelPrefabJson prefab, LevelImportResult result)
		{
			LevelTerrainData terrain = obj.Terrain;
			result.TerrainCount++;
			float tileWidth = prefab != null && prefab.kind == "terrain" && prefab.tileWidth > 0f ? prefab.tileWidth : 5f;
			float tileHeight = prefab != null && prefab.kind == "terrain" && prefab.tileHeight > 0f ? prefab.tileHeight : 5f;
			float offsetX = prefab != null && prefab.kind == "terrain" ? prefab.offsetX : terrain.FillTextureTileOffsetX;
			float offsetY = prefab != null && prefab.kind == "terrain" ? prefab.offsetY : terrain.FillTextureTileOffsetY;
			if (terrain.Fill != null && terrain.Fill.Vertices.Length >= 3 && terrain.Fill.Triangles.Length >= 3)
			{
				Vector2[] uv = new Vector2[terrain.Fill.Vertices.Length];
				for (int i = 0; i < uv.Length; i++)
				{
					uv[i] = new Vector2((terrain.Fill.Vertices[i].x - offsetX) / tileWidth, (terrain.Fill.Vertices[i].y - offsetY) / tileHeight);
				}
				Mesh mesh = CreateMesh(gameObject.name + "_fill", terrain.Fill.Vertices, uv, null, terrain.Fill.Triangles);
				AddMeshChild(gameObject.transform, "_fill", mesh, LevelCatalog.GetFillMaterial(ReferenceAt(entry, terrain.FillTextureIndex), terrain.Color));
				Encapsulate(result, gameObject.transform, mesh);
			}
			if (terrain.Curve != null && terrain.Curve.Vertices.Length >= 4 && terrain.Curve.Triangles.Length >= 3)
			{
				Mesh mesh = CreateMesh(gameObject.name + "_curve", terrain.Curve.Vertices, terrain.Curve.UV, terrain.Curve.Colors, terrain.Curve.Triangles);
				AddMeshChild(gameObject.transform, "_curve", mesh, BuildCurveMaterial(terrain, entry, prefab));
				Encapsulate(result, gameObject.transform, mesh);
			}
			if (terrain.HasCollider && terrain.Fill != null)
			{
				AddTerrainCollider(gameObject.transform, terrain.Fill, result);
			}
		}

		private static Material BuildCurveMaterial(LevelTerrainData terrain, LevelEntryJson entry, LevelPrefabJson prefab)
		{
			string splat0 = ReferenceAt(entry, terrain.CurveTextures.Count > 0 ? terrain.CurveTextures[0].TextureIndex : -1);
			string splat1 = ReferenceAt(entry, terrain.CurveTextures.Count > 1 ? terrain.CurveTextures[1].TextureIndex : -1);
			Vector2 size0 = terrain.CurveTextures.Count > 0 ? terrain.CurveTextures[0].Size : new Vector2(0.1f, 0.5f);
			Vector2 size1 = terrain.CurveTextures.Count > 1 ? terrain.CurveTextures[1].Size : new Vector2(0.1f, 0.1f);
			if (string.IsNullOrEmpty(splat0) && prefab != null && prefab.curve != null && prefab.curve.Length > 0)
			{
				splat0 = prefab.curve[0].texture;
			}
			if (string.IsNullOrEmpty(splat1) && prefab != null && prefab.curve != null && prefab.curve.Length > 1)
			{
				splat1 = prefab.curve[1].texture;
			}
			return LevelCatalog.GetCurveMaterial(splat0, splat1, size0, size1, terrain.ControlTexturePng);
		}

		private static void BuildSprite(GameObject gameObject, LevelPrefabJson prefab, LevelEntryJson entry)
		{
			BuildSpriteQuad(gameObject, prefab.atlas, prefab.uv, prefab.size, prefab.pivot, prefab.flipX, Vector3.zero, Color.white);
		}

		/// <summary>
		/// Builds a compound prefab (a background parallax set, a goal flag, the glow behind
		/// a box…) as a node tree, mirroring what <c>Instantiate</c> does in the main
		/// project. The exported nodes are already in parent-before-child order and carry
		/// their local TRS, so Unity composites the same way it did originally.
		/// </summary>
		/// <remarks>
		/// Container nodes without an atlas still have to exist: the parallax layers are
		/// containers whose children sit at wildly different local z values, and flattening
		/// them by hand would lose the rotation/non-uniform-scale composition.
		/// </remarks>
		private static void BuildCompound(GameObject gameObject, LevelPrefabJson prefab, LevelImportResult result)
		{
			LevelSpriteNodeJson[] nodes = prefab.sprites;
			if (nodes == null || nodes.Length == 0)
			{
				return;
			}
			Transform[] transforms = new Transform[nodes.Length];
			bool drewAnything = false;
			for (int i = 0; i < nodes.Length; i++)
			{
				LevelSpriteNodeJson node = nodes[i];
				if (node == null)
				{
					continue;
				}
				Transform parent = node.parent >= 0 && node.parent < i ? transforms[node.parent] : gameObject.transform;
				if (parent == null)
				{
					parent = gameObject.transform;
				}
				GameObject child = new GameObject(string.IsNullOrEmpty(node.name) ? "node" : node.name);
				child.transform.SetParent(parent, false);
				child.transform.localPosition = ReadVector3(node.pos, Vector3.zero);
				child.transform.localRotation = ReadQuaternion(node.rot);
				child.transform.localScale = ReadVector3(node.scale, Vector3.one);
				transforms[i] = child.transform;
				if (!string.IsNullOrEmpty(node.atlas) || (node.color != null && node.color.Length >= 4))
				{
					BuildSpriteQuad(child, node.atlas, node.uv, node.size, node.pivot, node.flipX, ReadVector3(node.offset, Vector3.zero), ReadColor(node.color));
					MeshFilter filter = child.GetComponent<MeshFilter>();
					if (filter != null && filter.sharedMesh != null)
					{
						Encapsulate(result, child.transform, filter.sharedMesh);
					}
					drewAnything = true;
				}
			}
			if (drewAnything)
			{
				result.PropCount++;
			}
		}

		/// <summary>Adds the four-vertex sprite quad to <paramref name="gameObject"/>.</summary>
		private static void BuildSpriteQuad(GameObject gameObject, string atlas, float[] uvValues, float[] sizeValues, float[] pivotValues, bool flipX, Vector3 meshOffset, Color tint)
		{
			Vector2 size = ReadVector2(sizeValues, Vector2.one);
			Vector2 pivot = ReadVector2(pivotValues, Vector2.zero);
			if (uvValues == null || uvValues.Length < 4 || size.x <= 0f || size.y <= 0f)
			{
				return;
			}
			float halfWidth = size.x * 0.5f;
			float halfHeight = size.y * 0.5f;
			// Sprite.CreateMesh offsets the quad by -2 * pivot * 10 / 768.
			float offsetX = -2f * pivot.x * 10f / PixelsPerWorldUnit + meshOffset.x;
			float offsetY = -2f * pivot.y * 10f / PixelsPerWorldUnit + meshOffset.y;
			Vector3[] vertices =
			{
				new Vector3(offsetX - halfWidth, offsetY - halfHeight, 0f),
				new Vector3(offsetX - halfWidth, offsetY + halfHeight, 0f),
				new Vector3(offsetX + halfWidth, offsetY + halfHeight, 0f),
				new Vector3(offsetX + halfWidth, offsetY - halfHeight, 0f)
			};
			float u0 = flipX ? uvValues[0] + uvValues[2] : uvValues[0];
			float u1 = flipX ? uvValues[0] : uvValues[0] + uvValues[2];
			Vector2[] uv =
			{
				new Vector2(u0, uvValues[1]),
				new Vector2(u0, uvValues[1] + uvValues[3]),
				new Vector2(u1, uvValues[1] + uvValues[3]),
				new Vector2(u1, uvValues[1])
			};
			int[] triangles = { 0, 1, 2, 2, 3, 0 };
			Mesh mesh = CreateMesh(gameObject.name, vertices, uv, null, triangles);
			MeshFilter filter = gameObject.AddComponent<MeshFilter>();
			filter.sharedMesh = mesh;
			MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
			Material material = string.IsNullOrEmpty(atlas)
				? LevelCatalog.GetColorMaterial(tint)
				: LevelCatalog.GetSpriteMaterial(atlas);
			if (material != null)
			{
				renderer.sharedMaterial = material;
			}
			renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			renderer.receiveShadows = false;
			renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
			renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
		}

		private static void AddMeshChild(Transform parent, string name, Mesh mesh, Material material)
		{
			GameObject child = new GameObject(name);
			child.transform.SetParent(parent, false);
			MeshFilter filter = child.AddComponent<MeshFilter>();
			filter.sharedMesh = mesh;
			MeshRenderer renderer = child.AddComponent<MeshRenderer>();
			if (material != null)
			{
				renderer.sharedMaterial = material;
			}
			renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			renderer.receiveShadows = false;
			renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
			renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
		}

		private static void Encapsulate(LevelImportResult result, Transform transform, Mesh mesh)
		{
			Bounds local = mesh.bounds;
			Vector3 scale = transform.lossyScale;
			result.Encapsulate(transform.TransformPoint(local.center), new Vector3(Mathf.Abs(local.size.x * scale.x), Mathf.Abs(local.size.y * scale.y), 0f));
		}

		/// <summary>Half the Z thickness of a terrain collision slab, matching the main
		/// project's <c>e2dConstants.COLLISION_MESH_Z_DEPTH / 2</c>.</summary>
		private const float ColliderHalfDepth = 5f;

		/// <summary>
		/// Builds the static collision for one terrain piece as a thick vertical slab whose
		/// outline is the fill polygon, exactly like the main project's
		/// <c>e2dTerrainColliderMesh.RebuildMesh</c> / <c>LevelLoader.CreateCollider</c>:
		/// only the polygon outline is extruded, the interior is never filled.
		/// </summary>
		/// <remarks>
		/// The slab straddles the part plane (z = 0) instead of the terrain mesh. The importer
		/// pushes the whole level back by <see cref="DepthOffset"/> while the contraption stays
		/// at z = 0, so a collider that follows the terrain's own z can never touch a part -
		/// that is why the previous flat triangle soup produced no collisions at all.
		/// </remarks>
		private static void AddTerrainCollider(Transform transform, LevelMeshData fill, LevelImportResult result)
		{
			World world = World.DefaultGameObjectInjectionWorld;
			if (world == null || !world.IsCreated || fill?.Vertices == null || fill.Triangles == null)
			{
				return;
			}
			int[] triangles = fill.Triangles;
			Vector3[] source = fill.Vertices;
			if (source.Length < 3 || triangles.Length < 3)
			{
				return;
			}
			// Weld the fill vertices on world XY so that edges shared by two triangles cancel
			// out and only the outline of the polygon survives. Extruding every triangle
			// instead would leave walls inside the solid that parts could bump into.
			Dictionary<Vector3, int> welded = new Dictionary<Vector3, int>(source.Length);
			int[] remap = new int[source.Length];
			List<Vector3> points = new List<Vector3>(source.Length);
			for (int i = 0; i < source.Length; i++)
			{
				Vector3 point = transform.TransformPoint(source[i]);
				point = new Vector3(Snap(point.x), Snap(point.y), 0f);
				if (!welded.TryGetValue(point, out int id))
				{
					id = points.Count;
					welded.Add(point, id);
					points.Add(point);
				}
				remap[i] = id;
			}
			// Directed edges: a boundary edge is one whose opposite never shows up, which keeps
			// the original winding (and therefore outward-facing walls) instead of a random one.
			HashSet<long> directed = new HashSet<long>(triangles.Length);
			for (int i = 0; i < triangles.Length - 2; i += 3)
			{
				int a = remap[triangles[i]];
				int b = remap[triangles[i + 1]];
				int c = remap[triangles[i + 2]];
				if (a != b)
				{
					directed.Add(EdgeKey(a, b));
				}
				if (b != c)
				{
					directed.Add(EdgeKey(b, c));
				}
				if (c != a)
				{
					directed.Add(EdgeKey(c, a));
				}
			}
			List<float3> vertices = new List<float3>(directed.Count * 4);
			List<int3> indices = new List<int3>(directed.Count * 2);
			foreach (long edge in directed)
			{
				int a = (int)(edge >> 32);
				int b = (int)(edge & 0xFFFFFFFFL);
				if (directed.Contains(EdgeKey(b, a)))
				{
					continue;
				}
				Vector3 pa = points[a];
				Vector3 pb = points[b];
				Vector3 delta = pb - pa;
				if (delta.sqrMagnitude < 1e-8f)
				{
					continue;
				}
				int baseIndex = vertices.Count;
				vertices.Add(new float3(pa.x, pa.y, 0f - ColliderHalfDepth));
				vertices.Add(new float3(pb.x, pb.y, 0f - ColliderHalfDepth));
				vertices.Add(new float3(pb.x, pb.y, ColliderHalfDepth));
				vertices.Add(new float3(pa.x, pa.y, ColliderHalfDepth));
				indices.Add(new int3(baseIndex, baseIndex + 1, baseIndex + 2));
				indices.Add(new int3(baseIndex, baseIndex + 2, baseIndex + 3));
			}
			if (indices.Count == 0)
			{
				return;
			}
			NativeArray<float3> vertexArray = new NativeArray<float3>(vertices.ToArray(), Allocator.Temp);
			NativeArray<int3> indexArray = new NativeArray<int3>(indices.ToArray(), Allocator.Temp);
			BlobAssetReference<Collider> collider;
			try
			{
				collider = MeshCollider.Create(vertexArray, indexArray);
			}
			catch (Exception exception)
			{
				Debug.LogWarning($"[BpsimLevels] terrain collider rejected: {exception.Message}");
				return;
			}
			finally
			{
				vertexArray.Dispose();
				indexArray.Dispose();
			}
			EntityManager entityManager = world.EntityManager;
			Entity entity = entityManager.CreateEntity();
			entityManager.AddComponentData(entity, LocalTransform.Identity);
			entityManager.AddComponentData(entity, new LocalToWorld
			{
				Value = float4x4.identity
			});
			entityManager.AddComponentData(entity, new PhysicsCollider
			{
				Value = collider
			});
			entityManager.AddSharedComponent(entity, new PhysicsWorldIndex(0));
			result.Entities.Add(entity);
			result.Colliders.Add(collider);
		}

		/// <summary>Quantises a coordinate so that vertices that are meant to coincide weld
		/// together even when they arrive through different indices.</summary>
		private static float Snap(float value)
		{
			return Mathf.Round(value * 1000f) * 0.001f;
		}

		private static long EdgeKey(int from, int to)
		{
			return ((long)from << 32) | (uint)to;
		}

		private static Mesh CreateMesh(string name, Vector3[] vertices, Vector2[] uv, Color[] colors, int[] triangles)
		{
			Mesh mesh = new Mesh
			{
				name = name
			};
			if (vertices.Length > 65000)
			{
				mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
			}
			mesh.vertices = vertices;
			if (uv != null)
			{
				mesh.uv = uv;
			}
			if (colors != null)
			{
				mesh.colors = colors;
			}
			mesh.triangles = triangles;
			mesh.RecalculateBounds();
			return mesh;
		}

		private static string GetPrefabName(LevelEntryJson entry, int index)
		{
			if (entry?.prefabs == null || index < 0 || index >= entry.prefabs.Length)
			{
				return null;
			}
			return entry.prefabs[index];
		}

		private static string ReferenceAt(LevelEntryJson entry, int index)
		{
			if (entry?.references == null || index < 0 || index >= entry.references.Length)
			{
				return null;
			}
			string value = entry.references[index];
			return string.IsNullOrEmpty(value) ? null : value;
		}

		private static Vector2 ReadVector2(float[] values, Vector2 fallback)
		{
			if (values == null || values.Length < 2)
			{
				return fallback;
			}
			return new Vector2(values[0], values[1]);
		}

		private static Vector3 ReadVector3(float[] values, Vector3 fallback)		{
			if (values == null || values.Length < 3)
			{
				return fallback;
			}
			return new Vector3(values[0], values[1], values[2]);
		}

		private static Color ReadColor(float[] values)
		{
			if (values == null || values.Length < 4)
			{
				return Color.white;
			}
			return new Color(values[0], values[1], values[2], values[3]);
		}

		private static Quaternion ReadQuaternion(float[] values)
		{
			if (values == null || values.Length < 4)
			{
				return Quaternion.identity;
			}
			Quaternion rotation = new Quaternion(values[0], values[1], values[2], values[3]);
			float length = Mathf.Sqrt(rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w);
			return length > 1e-5f ? new Quaternion(rotation.x / length, rotation.y / length, rotation.z / length, rotation.w / length) : Quaternion.identity;
		}
	}
}
