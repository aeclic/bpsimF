using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Bpsim.Levels
{
	[Serializable]
	internal sealed class LevelEntryJson
	{
		public string episode;

		public string scene;

		public string title;

		/// <summary>Path under <c>BpsimLevels/</c>, e.g. <c>levels/Level_05.bytes</c>.</summary>
		public string data;

		/// <summary>Path under <c>BpsimLevels/</c>, or empty when the level ships none.</summary>
		public string contraption;

		/// <summary>Prefab names, indexed by a level object's <c>prefabIndex</c>.</summary>
		public string[] prefabs;

		/// <summary>Asset names referenced by the level payload (textures), by index.</summary>
		public string[] references;
	}

	[Serializable]
	internal sealed class LevelIndexJson
	{
		public LevelEntryJson[] levels;
	}

	[Serializable]
	internal sealed class LevelCurveJson
	{
		public string texture;

		public float[] size;

		public bool fixedAngle;

		public float fadeThreshold;
	}

	/// <summary>
	/// One node of a compound prefab (a background parallax set, a goal flag, the glow
	/// behind a box…). <see cref="parent"/> indexes into the same array and always points
	/// backwards, so the importer can create parents before children. Nodes without an
	/// <see cref="atlas"/> are pure transforms (layer containers).
	/// </summary>
	[Serializable]
	internal sealed class LevelSpriteNodeJson
	{
		public string name;

		public int parent = -1;

		public float[] pos;

		public float[] rot;

		public float[] scale;

		/// <summary>Atlas file name; empty for a container node.</summary>
		public string atlas;

		public float[] uv;

		public float[] size;

		public float[] pivot;

		public bool flipX;

		/// <summary>Local offset of the source mesh's AABB centre (bare-mesh renderers).</summary>
		public float[] offset;

		/// <summary>
		/// RGBA tint for a quad whose material has no <c>_MainTex</c> (the parallax
		/// background's near/far fills use <c>_Custom/Unlit_Monochrome</c>, a flat
		/// colour). Empty for textured nodes.
		/// </summary>
		public float[] color;
	}

	/// <summary>
	/// One entry of <c>prefabs.json</c>: how the main project's prefab looked, reduced to
	/// what bpsim can draw. <see cref="kind"/> is <c>terrain</c>, <c>sprite</c>,
	/// <c>compound</c> or <c>none</c>; the fields that do not apply to a kind are left at
	/// their defaults.
	/// </summary>
	[Serializable]
	internal sealed class LevelPrefabJson
	{
		public string name;

		public string kind;

		// terrain
		public float tileWidth = 5f;

		public float tileHeight = 5f;

		public float offsetX;

		public float offsetY;

		public LevelCurveJson[] curve;

		// sprite
		public string atlas;

		public string id;

		public float[] uv;

		public float[] size;

		public float[] pivot;

		public bool flipX;

		// compound
		public LevelSpriteNodeJson[] sprites;
	}

	[Serializable]
	internal sealed class LevelPrefabTableJson
	{
		public LevelPrefabJson[] items;
	}

	/// <summary>
	/// Reads the pre-baked BPLE level resources that <c>Tools/BPSim/export_levels.py</c>
	/// writes into <c>StreamingAssets/BpsimLevels</c>: the level index, the reduced prefab
	/// table, and the raw <c>.bytes</c> payloads, contraptions and textures.
	/// </summary>
	/// <remarks>
	/// StreamingAssets is used rather than Resources because the payloads are large
	/// (~21 MB of level binaries) and are read exactly once per level, and because
	/// keeping the original bytes lets the C# parser stay a straight port of the main
	/// project's <c>LevelLoader</c>.
	/// </remarks>
	internal static class LevelCatalog
	{
		public const string FolderName = "BpsimLevels";

		private const string FillShaderResource = "BpsimLevels/Bpsim_Terrain_Fill";

		private const string CurveShaderResource = "BpsimLevels/Bpsim_Terrain_Curve";

		private const string SpriteShaderName = "Bpsim/Unlit_Texture";

		private static List<LevelEntryJson> s_levels;

		private static Dictionary<string, LevelPrefabJson> s_prefabs;

		private static readonly Dictionary<string, Texture2D> s_textures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

		private static readonly Dictionary<string, Material> s_fillMaterials = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);

		private static readonly Dictionary<string, Material> s_curveMaterials = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);

		private static readonly Dictionary<string, Material> s_spriteMaterials = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);

		private static Shader s_fillShader;

		private static Shader s_curveShader;

		private static Shader s_spriteShader;

		public static string Root => Path.Combine(Application.streamingAssetsPath, FolderName);

		public static bool IsAvailable => File.Exists(Path.Combine(Root, "index.json"));

		public static List<LevelEntryJson> Levels
		{
			get
			{
				EnsureIndex();
				return s_levels;
			}
		}

		public static LevelEntryJson FindLevel(string scene)
		{
			if (string.IsNullOrEmpty(scene))
			{
				return null;
			}
			List<LevelEntryJson> levels = Levels;
			for (int i = 0; i < levels.Count; i++)
			{
				if (string.Equals(levels[i].scene, scene, StringComparison.OrdinalIgnoreCase))
				{
					return levels[i];
				}
			}
			return null;
		}

		public static LevelPrefabJson FindPrefab(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return null;
			}
			EnsurePrefabs();
			s_prefabs.TryGetValue(name, out LevelPrefabJson prefab);
			return prefab;
		}

		public static byte[] ReadLevelBytes(LevelEntryJson entry)
		{
			if (entry == null || string.IsNullOrEmpty(entry.data))
			{
				return null;
			}
			string path = Path.Combine(Root, entry.data.Replace('/', Path.DirectorySeparatorChar));
			return File.Exists(path) ? File.ReadAllBytes(path) : null;
		}

		public static string ReadContraption(LevelEntryJson entry)
		{
			if (entry == null || string.IsNullOrEmpty(entry.contraption))
			{
				return null;
			}
			string path = Path.Combine(Root, "contraptions", entry.contraption);
			return File.Exists(path) ? File.ReadAllText(path) : null;
		}

		/// <summary>
		/// Resolves a level payload's asset reference (which carries the main project's
		/// file name, e.g. <c>Ground_Rocks_Texture.png</c>) to a loaded texture.
		/// </summary>
		public static Texture2D GetTexture(string reference, bool repeat)
		{
			if (string.IsNullOrEmpty(reference))
			{
				return null;
			}
			string name = Path.GetFileNameWithoutExtension(reference);
			string key = repeat ? name + "|repeat" : name + "|clamp";
			if (s_textures.TryGetValue(key, out Texture2D cached) && cached != null)
			{
				return cached;
			}
			string path = Path.Combine(Root, "textures", name + ".png");
			if (!File.Exists(path))
			{
				Debug.LogWarning($"[BpsimLevels] missing texture '{name}'");
				return null;
			}
			Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false)
			{
				name = name,
				filterMode = FilterMode.Bilinear,
				wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp,
				anisoLevel = 1
			};
			if (!texture.LoadImage(File.ReadAllBytes(path)))
			{
				UnityEngine.Object.Destroy(texture);
				Debug.LogWarning($"[BpsimLevels] cannot decode texture '{name}'");
				return null;
			}
			s_textures[key] = texture;
			return texture;
		}

		/// <summary>
		/// The opaque material for one terrain piece. Tinted per instance because the
		/// level binary stores a colour per terrain object.
		/// </summary>
		public static Material GetFillMaterial(string textureReference, Color color)
		{
			Texture2D texture = GetTexture(textureReference, repeat: true);
			string key = (texture != null ? texture.name : "<none>") + "|" + ColorUtility.ToHtmlStringRGBA(color);
			if (s_fillMaterials.TryGetValue(key, out Material cached) && cached != null)
			{
				return cached;
			}
			Shader shader = s_fillShader != null ? s_fillShader : (s_fillShader = LoadShader(FillShaderResource, "Bpsim/Terrain_Fill"));
			if (shader == null)
			{
				return null;
			}
			Material material = new Material(shader)
			{
				name = "BpsimTerrainFill_" + key,
				hideFlags = HideFlags.DontSave
			};
			if (texture != null)
			{
				material.mainTexture = texture;
			}
			material.SetColor("_Color", color);
			s_fillMaterials[key] = material;
			return material;
		}

		/// <summary>
		/// The blended material for one terrain piece's curve strip. Both splat textures
		/// and the control strip are per terrain object, so these are cached by identity.
		/// </summary>
		public static Material GetCurveMaterial(string splat0Reference, string splat1Reference, Vector2 size0, Vector2 size1, byte[] controlPng)
		{
			Texture2D splat0 = GetTexture(splat0Reference, repeat: true);
			Texture2D splat1 = GetTexture(splat1Reference, repeat: true);
			string key = string.Concat(splat0 != null ? splat0.name : "<none>", "|", splat1 != null ? splat1.name : "<none>", "|", size0.x.ToString("R"), "|", size0.y.ToString("R"), "|", controlPng != null ? controlPng.Length : 0);
			if (s_curveMaterials.TryGetValue(key, out Material cached) && cached != null)
			{
				return cached;
			}
			Shader shader = s_curveShader != null ? s_curveShader : (s_curveShader = LoadShader(CurveShaderResource, "Bpsim/Terrain_Curve"));
			if (shader == null)
			{
				return null;
			}
			Material material = new Material(shader)
			{
				name = "BpsimTerrainCurve_" + key,
				hideFlags = HideFlags.DontSave
			};
			if (splat0 != null)
			{
				material.SetTexture("_Splat0", splat0);
			}
			if (splat1 != null)
			{
				material.SetTexture("_Splat1", splat1);
			}
			material.SetVector("_SplatParams0", new Vector4(1f / Mathf.Max(size0.x, 0.0001f), size0.y, 0f, 0f));
			material.SetVector("_SplatParams1", new Vector4(1f / Mathf.Max(size1.x, 0.0001f), size1.y, 0f, 0f));
			Texture2D control = DecodeControlTexture(controlPng);
			if (control != null)
			{
				material.SetTexture("_Control", control);
				material.SetFloat("_ControlSize", control.width);
				material.SetFloat("_InvControlSize", 1f / control.width);
				material.SetFloat("_InvControlSizeHalf", 0.5f / control.width);
			}
			s_curveMaterials[key] = material;
			return material;
		}

		public static Material GetSpriteMaterial(string atlasReference)
		{
			Texture2D texture = GetTexture(atlasReference, repeat: false);
			string key = texture != null ? texture.name : "<none>";
			if (s_spriteMaterials.TryGetValue(key, out Material cached) && cached != null)
			{
				return cached;
			}
			Shader shader = s_spriteShader != null ? s_spriteShader : (s_spriteShader = Shader.Find(SpriteShaderName));
			if (shader == null)
			{
				return null;
			}
			Material material = new Material(shader)
			{
				name = "BpsimLevelSprite_" + key,
				hideFlags = HideFlags.DontSave
			};
			if (texture != null)
			{
				material.mainTexture = texture;
			}
			material.SetColor("_Color", Color.white);
			s_spriteMaterials[key] = material;
			return material;
		}

		/// <summary>
		/// Flat-colour material for a node whose source material has no texture at all
		/// (the parallax background's near/far fills). The unlit sprite shader already
		/// multiplies <c>_MainTex</c> by <c>_Color</c>, so leaving the texture at Unity's
		/// white default yields the plain colour the main project rendered.
		/// </summary>
		public static Material GetColorMaterial(Color color)
		{
			string key = "color|" + ColorUtility.ToHtmlStringRGBA(color);
			if (s_spriteMaterials.TryGetValue(key, out Material cached) && cached != null)
			{
				return cached;
			}
			Shader shader = s_spriteShader != null ? s_spriteShader : (s_spriteShader = Shader.Find(SpriteShaderName));
			if (shader == null)
			{
				return null;
			}
			Material material = new Material(shader)
			{
				name = "BpsimLevelFill_" + ColorUtility.ToHtmlStringRGBA(color),
				hideFlags = HideFlags.DontSave
			};
			material.SetColor("_Color", color);
			s_spriteMaterials[key] = material;
			return material;
		}

		private static Texture2D DecodeControlTexture(byte[] png)
		{
			if (png == null || png.Length == 0)
			{
				return null;
			}
			Texture2D texture = new Texture2D(1, 1, TextureFormat.ARGB32, mipChain: false)
			{
				name = "BpsimTerrainControl",
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp,
				anisoLevel = 1,
				hideFlags = HideFlags.DontSave
			};
			if (!texture.LoadImage(png))
			{
				UnityEngine.Object.Destroy(texture);
				return null;
			}
			return texture;
		}

		private static Shader LoadShader(string resourcePath, string shaderName)
		{
			Shader shader = Resources.Load<Shader>(resourcePath);
			if (shader == null)
			{
				shader = Shader.Find(shaderName);
			}
			if (shader == null)
			{
				Debug.LogError($"[BpsimLevels] shader '{shaderName}' is missing");
			}
			return shader;
		}

		private static void EnsureIndex()
		{
			if (s_levels != null)
			{
				return;
			}
			s_levels = new List<LevelEntryJson>();
			string path = Path.Combine(Root, "index.json");
			if (!File.Exists(path))
			{
				Debug.LogError($"[BpsimLevels] {path} is missing; run Tools/BPSim/export_levels.py --write");
				return;
			}
			LevelIndexJson index = JsonUtility.FromJson<LevelIndexJson>(File.ReadAllText(path));
			if (index != null && index.levels != null)
			{
				s_levels.AddRange(index.levels);
			}
		}

		private static void EnsurePrefabs()
		{
			if (s_prefabs != null)
			{
				return;
			}
			s_prefabs = new Dictionary<string, LevelPrefabJson>(StringComparer.OrdinalIgnoreCase);
			string path = Path.Combine(Root, "prefabs.json");
			if (!File.Exists(path))
			{
				Debug.LogError($"[BpsimLevels] {path} is missing");
				return;
			}
			LevelPrefabTableJson table = JsonUtility.FromJson<LevelPrefabTableJson>(File.ReadAllText(path));
			if (table == null || table.items == null)
			{
				return;
			}
			foreach (LevelPrefabJson item in table.items)
			{
				if (item != null && !string.IsNullOrEmpty(item.name))
				{
					s_prefabs[item.name] = item;
				}
			}
		}
	}
}
