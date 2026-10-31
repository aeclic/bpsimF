using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Bpsim.Parts.Skin
{
	/// <summary>
	/// 蒙皮形状/贴图的托管侧缓存。ECS 组件只存句柄，真正的 Unity 对象都在这里。
	/// </summary>
	/// <remarks>
	/// 这是 ECS 与 Unity 渲染对象之间唯一的一层「胶水」：<c>SkinSheetSystem</c> 算好
	/// 顶点后调 <see cref="EnsureMesh"/> 上传，<c>SkinSheetRenderSystem</c> 用
	/// <see cref="GetMesh"/> + <see cref="GetMaterial"/> 提交绘制。之所以要这一层而不是
	/// 让每个部件挂 MonoBehaviour，是因为 <c>Mesh</c>/<c>Material</c> 是托管对象，
	/// 一旦挂到部件上，形状就又被「一个组件实例」绑死了，无法被 job、存档或 QA 直接读写。
	/// </remarks>
	public sealed class SkinSheetRegistry
	{
		private sealed class Entry
		{
			public Mesh Mesh;

			public Material Material;

			public int Version = int.MinValue;

			/// <summary>最后一次被点名到的帧号（<see cref="BeginFrame"/> 递增）。</summary>
			public int LastFrame;
		}

		/// <summary>贴图文件目录（与 BPRC 的 <c>SkinTextures</c> 保持同一布局）。</summary>
		public static string TextureDirectory => Path.Combine(Application.persistentDataPath, "SkinTextures");

		private static SkinSheetRegistry s_instance;

		private readonly Dictionary<int, Entry> m_entries = new Dictionary<int, Entry>();

		private readonly Dictionary<string, int> m_textureHandles = new Dictionary<string, int>(StringComparer.Ordinal);

		private readonly List<Texture2D> m_textures = new List<Texture2D>();

		private Material m_template;

		private Texture2D m_white;

		private int m_defaultTextureHandle = NoTexture;

		private int m_nextHandle = 1;

		public static SkinSheetRegistry Instance => (s_instance ?? (s_instance = new SkinSheetRegistry()));

		/// <summary>没有贴图时使用的白板句柄。</summary>
		public const int NoTexture = -1;

		private int m_frame;

		/// <summary>开始新的一帧：句柄的存活状态在本帧内由 <see cref="Touch"/> 重新认定。</summary>
		public void BeginFrame()
		{
			m_frame++;
		}

		/// <summary>标记某个句柄这一帧仍在使用（由 <c>SkinSheetSystem</c> 对每个蒙皮实体调用）。</summary>
		public void Touch(int handle)
		{
			if (handle > 0 && m_entries.TryGetValue(handle, out var entry))
			{
				entry.LastFrame = m_frame;
			}
		}

		/// <summary>结束本帧：一帧都没被点到的句柄，说明对应实体已经销毁，释放它的网格/材质。</summary>
		public void EndFrame()
		{
			List<int> stale = null;
			foreach (KeyValuePair<int, Entry> pair in m_entries)
			{
				if (pair.Value.LastFrame == m_frame)
				{
					continue;
				}
				(stale = stale ?? new List<int>()).Add(pair.Key);
			}
			if (stale == null)
			{
				return;
			}
			for (int i = 0; i < stale.Count; i++)
			{
				Release(stale[i]);
			}
		}

		/// <summary>分配一个新的网格句柄。</summary>
		public int Allocate()
		{
			int handle = m_nextHandle++;
			m_entries[handle] = new Entry
			{
				LastFrame = m_frame
			};
			return handle;
		}

		public void Release(int handle)
		{
			if (handle > 0 && m_entries.TryGetValue(handle, out var entry))
			{
				ReleaseEntry(entry);
				m_entries.Remove(handle);
			}
		}

		/// <summary>
		/// 上传（必要时重建）网格。顶点数组必须是 <see cref="SkinSheetMeshBuilder.VisualVertexCount"/> 个。
		/// </summary>
		public Mesh EnsureMesh(int handle, int version, NativeArray<float3> positions, NativeArray<float2> uvs, NativeArray<float4> colors, NativeArray<int> indices, float2 boundsMin, float2 boundsMax)
		{
			if (!m_entries.TryGetValue(handle, out var entry))
			{
				entry = new Entry();
				m_entries[handle] = entry;
			}
			if (entry.Mesh == null)
			{
				entry.Mesh = new Mesh
				{
					name = "SkinSheet_" + handle,
					hideFlags = HideFlags.DontSave
				};
				entry.Mesh.MarkDynamic();
			}
			entry.Mesh.Clear();
			entry.Mesh.SetVertices(positions);
			entry.Mesh.SetUVs(0, uvs);
			entry.Mesh.SetColors(colors);
			entry.Mesh.SetIndices(indices, MeshTopology.Triangles, 0, calculateBounds: false);
			// 直接写入包围盒：顶点是这一帧刚算出来的，没必要让 Mesh 再遍历 625 个顶点。
			entry.Mesh.bounds = new Bounds
			{
				center = new Vector3((boundsMin.x + boundsMax.x) * 0.5f, (boundsMin.y + boundsMax.y) * 0.5f, 0f),
				size = new Vector3(Mathf.Max(boundsMax.x - boundsMin.x, 1E-04f), Mathf.Max(boundsMax.y - boundsMin.y, 1E-04f), SkinSheetGeometry.Depth)
			};
			entry.Version = version;
			return entry.Mesh;
		}

		public Mesh GetMesh(int handle)
		{
			return (m_entries.TryGetValue(handle, out var entry) ? entry.Mesh : null);
		}

		private Material Template
		{
			get
			{
				if (m_template == null)
				{
					// 必须是工程里的材质资源：运行时 Shader.Find 在打包后拿不到未引用的着色器。
					m_template = Resources.Load<Material>("SkinSheet_Default");
					if (m_template == null)
					{
						Debug.LogWarning("[SkinSheet] 缺少 Resources/SkinSheet_Default.mat，蒙皮将不可见");
					}
				}
				return m_template;
			}
		}

		/// <summary>
		/// 取该部件当前贴图对应的材质。贴图为空时用纯白贴图，于是曲面只剩顶点色。
		/// </summary>
		public Material GetMaterial(int handle, int textureHandle, float4 color, int textureMode = 0)
		{
			if (!m_entries.TryGetValue(handle, out var entry))
			{
				return null;
			}
			Material template = Template;
			if (template == null)
			{
				return null;
			}
			if (entry.Material == null)
			{
				entry.Material = new Material(template)
				{
					name = "SkinSheetMat_" + handle,
					hideFlags = HideFlags.DontSave
				};
			}
			Texture2D texture = GetTexture(textureHandle);
			if (texture == null)
			{
				// 没贴图时给一张 1x1 白：否则 mainTexture 为 null 会让下面的 wrapMode 赋值空引用，
				// 而且曲面会退化成不可见的全透明。
				texture = WhiteTexture;
			}
			if (entry.Material.mainTexture != texture)
			{
				entry.Material.mainTexture = texture;
			}
			// 平铺模式要 Repeat；拉伸模式必须 Clamp，否则曲面外采样会绕回来。
			entry.Material.mainTexture.wrapMode = textureMode == 1 ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
			entry.Material.color = new Color(color.x, color.y, color.z, color.w);
			return entry.Material;
		}

		/// <summary>共用的 1x1 白贴图（无蒙皮贴图时的底色）。</summary>
		private Texture2D WhiteTexture
		{
			get
			{
				if (m_white == null)
				{
					m_white = new Texture2D(1, 1, TextureFormat.RGBA32, false)
					{
						name = "SkinSheetWhite",
						hideFlags = HideFlags.DontSave,
						filterMode = FilterMode.Bilinear,
						wrapMode = TextureWrapMode.Clamp
					};
					m_white.SetPixel(0, 0, Color.white);
					m_white.Apply();
				}
				return m_white;
			}
		}

		/// <summary>把 <paramref name="id"/>（内容哈希）登记成句柄，必要时从磁盘载入 PNG。</summary>
		public int GetOrLoadTexture(string id)
		{
			if (string.IsNullOrEmpty(id))
			{
				return NoTexture;
			}
			if (m_textureHandles.TryGetValue(id, out int handle))
			{
				return handle;
			}
			Texture2D texture = LoadTexture(id);
			if (texture == null)
			{
				return NoTexture;
			}
			handle = m_textures.Count;
			m_textures.Add(texture);
			m_textureHandles[id] = handle;
			return handle;
		}

		public int GetOrLoadTextureById(int handle)
		{
			return handle;
		}

		public Texture2D GetTexture(int handle)
		{
			if (handle < 0 || handle >= m_textures.Count)
			{
				return null;
			}
			return m_textures[handle];
		}

		/// <summary>
		/// 默认蒙皮布面。颜色和编织纹理与部件列表的 SkinPartIcon 保持一致，
		/// 让刚放置的部件在场景中和列表里的预览有相同的视觉语义。
		/// </summary>
		public int GetDefaultTextureHandle()
		{
			if (m_defaultTextureHandle >= 0 && GetTexture(m_defaultTextureHandle) != null)
			{
				return m_defaultTextureHandle;
			}
			const int size = 64;
			Color32 clear = new Color32(0, 0, 0, 0);
			Color32 frame = new Color32(206, 172, 122, 255);
			Color32 cloth = new Color32(240, 227, 203, 255);
			Color32 weave = new Color32(203, 178, 138, 255);
			Color32 handle = new Color32(146, 109, 68, 255);
			Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
			{
				name = "SkinSheetDefaultCloth",
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp,
				hideFlags = HideFlags.DontSave
			};
			Color32[] pixels = new Color32[size * size];
			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					Color32 pixel = clear;
					if (RoundBox(x, y, 31.5f, 31.5f, 27f, 27f, 11f) <= 0f)
					{
						pixel = frame;
						if (RoundBox(x, y, 31.5f, 31.5f, 21f, 21f, 7f) <= 0f) pixel = ((x + y) & 7) == 0 ? weave : cloth;
					}
					if (Dot(x, y, 12f, 12f) || Dot(x, y, 51f, 12f) || Dot(x, y, 12f, 51f) || Dot(x, y, 51f, 51f)) pixel = handle;
					pixels[y * size + x] = pixel;
				}
			}
			texture.SetPixels32(pixels);
			texture.Apply(false, false);
			m_defaultTextureHandle = m_textures.Count;
			m_textures.Add(texture);
			return m_defaultTextureHandle;
		}

		private static float RoundBox(float x, float y, float cx, float cy, float halfX, float halfY, float radius)
		{
			float dx = Mathf.Abs(x - cx) - (halfX - radius);
			float dy = Mathf.Abs(y - cy) - (halfY - radius);
			float ax = Mathf.Max(dx, 0f);
			float ay = Mathf.Max(dy, 0f);
			return Mathf.Sqrt(ax * ax + ay * ay) + Mathf.Min(Mathf.Max(dx, dy), 0f) - radius;
		}

		private static bool Dot(float x, float y, float cx, float cy)
		{
			float dx = x - cx;
			float dy = y - cy;
			return dx * dx + dy * dy <= 10f;
		}

		/// <summary>句柄反查内容哈希；面板要显示「当前用的是哪张」时用。</summary>
		public string GetTextureId(int handle)
		{
			foreach (KeyValuePair<string, int> pair in m_textureHandles)
			{
				if (pair.Value == handle)
				{
					return pair.Key;
				}
			}
			return null;
		}

		public Texture2D CreateTexture(Texture2D source)
		{
			if (m_template == null)
			{
				m_template = Resources.Load<Material>("SkinSheet_Default");
			}
			return source;
		}

		/// <summary>把一张贴图存成 <c>SkinTextures/&lt;sha256&gt;.png</c>，返回内容哈希。</summary>
		public static string StoreTexture(Texture2D texture)
		{
			byte[] png = texture.EncodeToPNG();
			return StoreTextureBytes(png);
		}

		public static string StoreTextureBytes(byte[] png)
		{
			using (SHA256 sha = SHA256.Create())
			{
				string id = BitConverter.ToString(sha.ComputeHash(png)).Replace("-", string.Empty).ToLowerInvariant();
				Directory.CreateDirectory(TextureDirectory);
				string target = Path.Combine(TextureDirectory, id + ".png");
				if (!File.Exists(target))
				{
					File.WriteAllBytes(target, png);
				}
				return id;
			}
		}

		/// <summary>64 位十六进制内容哈希才算合法 id（拒绝任何从蓝图/网页传进来的路径）。</summary>
		public static bool IsValidTextureId(string id)
		{
			if (id == null || id.Length != 64)
			{
				return false;
			}
			for (int i = 0; i < id.Length; i++)
			{
				char c = id[i];
				bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
				if (!hex)
				{
					return false;
				}
			}
			return true;
		}

		private static Texture2D LoadTexture(string id)
		{
			if (!IsValidTextureId(id))
			{
				Debug.LogWarning("[SkinSheet] 非法蒙皮贴图 id");
				return null;
			}
			string path = Path.Combine(TextureDirectory, id + ".png");
			if (!File.Exists(path))
			{
				return null;
			}
			try
			{
				byte[] bytes = File.ReadAllBytes(path);
				Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false)
				{
					name = "SkinTexture_" + id,
					hideFlags = HideFlags.DontSave,
					wrapMode = TextureWrapMode.Repeat,
					filterMode = FilterMode.Bilinear
				};
				if (texture.LoadImage(bytes))
				{
					return texture;
				}
				DestroyObject(texture);
			}
			catch (Exception ex)
			{
				Debug.LogWarning("[SkinSheet] 蒙皮贴图载入失败: " + ex.Message);
			}
			return null;
		}

		public void Dispose()
		{
			foreach (Entry value in m_entries.Values)
			{
				ReleaseEntry(value);
			}
			m_entries.Clear();
			foreach (Texture2D texture in m_textures)
			{
				if (texture != null)
				{
					DestroyObject(texture);
				}
			}
			m_textures.Clear();
			m_textureHandles.Clear();
			m_defaultTextureHandle = NoTexture;
		}

		// 编辑模式下 Object.Destroy 会报错（"Destroy may not be called from edit mode"），
		// 而蒙皮网格/材质的重建在编辑器里也会走到这里，所以按运行状态分派。
		private static void DestroyObject(UnityEngine.Object value)
		{
			if (value == null)
			{
				return;
			}
			if (Application.isPlaying)
			{
				UnityEngine.Object.Destroy(value);
			}
			else
			{
				UnityEngine.Object.DestroyImmediate(value);
			}
		}

		private static void ReleaseEntry(Entry entry)
		{
			if (entry.Mesh != null)
			{
				DestroyObject(entry.Mesh);
			}
			if (entry.Material != null)
			{
				DestroyObject(entry.Material);
			}
		}
	}
}
