using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bpsim.Rendering
{
	[ExecuteAlways]
	public class SpriteManager : UnitySingleton<SpriteManager>
	{
		[SerializeField]
		private List<TextAsset> m_textAssets;

		private bool m_initialized;

		private SpriteRect[] m_data;

		private List<SpriteMap> m_list;

		private Dictionary<string, SpriteMap> m_mapsByName;

		private Dictionary<string, int> m_idMap;

		private Dictionary<int, Mesh> m_meshMap;

		// 每条精灵落在哪个图集（SpriteMap 首列的 Name）。运行时追加的条目也要记，
		// 因为「换皮肤」的贴图来源可能不是 Part_Texture_0。
		private string[] m_atlasNames;

		private int m_version;

		public const float CameraDistance = 10f;

		public int SpriteCount => m_data.Length;

		public SpriteRect[] SpriteData => m_data;

		/// <summary>
		/// 精灵表版本号。运行时通过 <see cref="TryAddSprite"/> 追加条目后自增，
		/// <see cref="SpriteSystem"/> 用它在下一帧重建本地副本。
		/// </summary>
		public int Version => m_version;

		/// <summary>已注册的图集表（只读）。<c>Header.Name</c> 就是图集名。</summary>
		public IReadOnlyList<SpriteMap> Maps => m_list;

		/// <summary>取出某个图集（<c>SpriteMap.Header.Name</c>）里的全部条目。</summary>
		public IEnumerable<KeyValuePair<string, SpriteMap.MapEntry>> Entries(string atlasName)
		{
			if (m_list == null)
			{
				yield break;
			}
			foreach (SpriteMap map in m_list)
			{
				if (!string.IsNullOrEmpty(atlasName) && map.Header.Name != atlasName)
				{
					continue;
				}
				foreach (KeyValuePair<string, SpriteMap.MapEntry> datum in map.Data)
				{
					yield return datum;
				}
			}
		}

		/// <summary>取某个图集的头部信息（宽高与缩放）。</summary>
		public bool TryGetAtlasHeader(string atlasName, out SpriteMap.MapHeader header)
		{
			if (m_list != null)
			{
				foreach (SpriteMap map in m_list)
				{
					if (map.Header.Name == atlasName)
					{
						header = map.Header;
						return true;
					}
				}
			}
			header = default(SpriteMap.MapHeader);
			return false;
		}

		public new static SpriteManager Instance => UnitySingleton<SpriteManager>.Instance;

		protected override void Awake()
		{
			base.Awake();
			Initialize();
		}

		public void Initialize()
		{
			if (m_initialized)
			{
				return;
			}
			m_initialized = true;
			m_meshMap = new Dictionary<int, Mesh>();
			int num = 0;
			m_list = new List<SpriteMap>(m_textAssets.Count);
			m_mapsByName = new Dictionary<string, SpriteMap>(m_textAssets.Count);
			foreach (TextAsset textAsset in m_textAssets)
			{
				SpriteMap spriteMap = SpriteMap.Read(textAsset.text);
				num += spriteMap.Count;
				m_list.Add(spriteMap);
				m_mapsByName[spriteMap.Header.Name] = spriteMap;
			}
			int num2 = 0;
			m_idMap = new Dictionary<string, int>(num);
			m_data = new SpriteRect[num];
			m_atlasNames = new string[num];
			foreach (SpriteMap item in m_list)
			{
				foreach (KeyValuePair<string, SpriteMap.MapEntry> datum in item.Data)
				{
					m_idMap.Add(datum.Key, num2);
					m_data[num2] = GetSpriteRect(item.Header, datum.Value);
					m_atlasNames[num2] = item.Header.Name;
					num2++;
				}
			}
		}

		public bool HasID(string name)
		{
			return m_idMap.ContainsKey(name);
		}

		public int GetID(string name)
		{
			return m_idMap[name];
		}

		public bool TryGetID(string name, out int id)
		{
			return m_idMap.TryGetValue(name, out id);
		}

		public SpriteRect GetSprite(int id)
		{
			if (id < 0 || id >= m_data.Length)
			{
				throw new ArgumentOutOfRangeException("id");
			}
			return m_data[id];
		}

		public SpriteRect GetSprite(string name)
		{
			return GetSprite(GetID(name));
		}

		/// <summary>这条精灵属于哪个图集（未登记时返回 null）。</summary>
		public string GetSpriteAtlas(int id)
		{
			if (m_atlasNames == null || id < 0 || id >= m_atlasNames.Length)
			{
				return null;
			}
			return m_atlasNames[id];
		}

		public string GetSpriteAtlas(string name)
		{
			if (!TryGetID(name, out int id))
			{
				return null;
			}
			return GetSpriteAtlas(id);
		}

		/// <summary>
		/// 运行时往精灵表里追加一条（Mod 的换皮肤入口）。
		/// </summary>
		/// <param name="key">精灵键名，例如 <c>Part_WoodenFrame_9000_0</c>。</param>
		/// <param name="atlasName">所属图集（必须是已注册的 <c>SpriteMap.Header.Name</c>）。</param>
		/// <param name="entry">图集内的像素矩形与缩放。</param>
		/// <summary>
		/// 运行时注册一张新图集（Mod 自带的贴图 + 精灵表）。
		/// </summary>
		/// <remarks>
		/// 图集名就是 <see cref="SpriteMap.MapHeader.Name"/>，之后
		/// <see cref="TryAddSprite"/> 用它定位像素坐标，渲染端用
		/// <see cref="SpriteAtlasRegistry"/> 把图集名映射到一张独立材质。
		/// 必须在 <see cref="PartManager.Start"/> 之前调用，否则已经建好的
		/// ECS 预制体拿不到图集索引。
		/// </remarks>
		public bool TryAddAtlas(string atlasName, int width, int height, float scale, out string error)
		{
			if (string.IsNullOrEmpty(atlasName))
			{
				error = "图集名为空";
				return false;
			}
			if (m_data == null)
			{
				error = "SpriteManager 尚未初始化";
				return false;
			}
			if (TryGetAtlasHeader(atlasName, out SpriteMap.MapHeader _))
			{
				error = "图集 " + atlasName + " 已存在";
				return false;
			}
			SpriteMap map = new SpriteMap(new SpriteMap.MapHeader(atlasName, width, height, scale), new Dictionary<string, SpriteMap.MapEntry>());
			m_list.Add(map);
			m_mapsByName[atlasName] = map;
			m_version++;
			error = null;
			return true;
		}

		public bool HasAtlas(string atlasName)
		{
			return !string.IsNullOrEmpty(atlasName) && m_mapsByName != null && m_mapsByName.ContainsKey(atlasName);
		}

		public bool TryAddSprite(string key, string atlasName, in SpriteMap.MapEntry entry, out int id, out string error)
		{
			id = -1;
			if (string.IsNullOrEmpty(key))
			{
				error = "精灵键名为空";
				return false;
			}
			if (m_data == null)
			{
				error = "SpriteManager 尚未初始化";
				return false;
			}
			if (!TryGetAtlasHeader(atlasName, out SpriteMap.MapHeader header))
			{
				error = "找不到图集 " + atlasName;
				return false;
			}
			if (m_idMap.ContainsKey(key))
			{
				error = "精灵 " + key + " 已存在";
				return false;
			}
			SpriteRect rect = GetSpriteRect(header, entry);
			int num = m_data.Length;
			Array.Resize(ref m_data, num + 1);
			Array.Resize(ref m_atlasNames, num + 1);
			m_data[num] = rect;
			m_atlasNames[num] = atlasName;
			m_idMap[key] = num;
			m_version++;
			id = num;
			error = null;
			return true;
		}

		public bool TryGetMesh(int id, out Mesh mesh)
		{
			return m_meshMap.TryGetValue(id, out mesh);
		}

		public void SetMesh(int id, Mesh mesh)
		{
			m_meshMap[id] = mesh;
		}

		private static SpriteRect GetSpriteRect(in SpriteMap.MapHeader header, in SpriteMap.MapEntry value)
		{
			float vertexX = (float)(-2 * (int)((float)value.PivotX * value.ScaleX)) * header.Scale;
			float vertexY = (float)(-2 * (int)((float)value.PivotY * value.ScaleY)) * header.Scale;
			float vertexW = (float)(2 * (int)((float)value.Width * value.ScaleX)) * header.Scale;
			float vertexH = (float)(2 * (int)((float)value.Height * value.ScaleY)) * header.Scale;
			float u = (float)value.X / (float)header.Width;
			float num = (float)value.Y / (float)header.Height;
			float w = (float)value.Width / (float)header.Width;
			float num2 = (float)value.Height / (float)header.Height;
			return new SpriteRect(vertexX, vertexY, vertexW, vertexH, u, 1f - num - num2, w, num2);
		}
	}
}
