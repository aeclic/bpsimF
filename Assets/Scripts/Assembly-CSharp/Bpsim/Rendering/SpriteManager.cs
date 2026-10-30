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

		private Dictionary<string, int> m_idMap;

		private Dictionary<int, Mesh> m_meshMap;

		public const float CameraDistance = 10f;

		public int SpriteCount => m_data.Length;

		public SpriteRect[] SpriteData => m_data;

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
			foreach (TextAsset textAsset in m_textAssets)
			{
				SpriteMap spriteMap = SpriteMap.Read(textAsset.text);
				num += spriteMap.Count;
				m_list.Add(spriteMap);
			}
			int num2 = 0;
			m_idMap = new Dictionary<string, int>(num);
			m_data = new SpriteRect[num];
			foreach (SpriteMap item in m_list)
			{
				foreach (KeyValuePair<string, SpriteMap.MapEntry> datum in item.Data)
				{
					m_idMap.Add(datum.Key, num2);
					m_data[num2] = GetSpriteRect(item.Header, datum.Value);
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
