using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Bpsim
{
	public class ResourceManager
	{
		public class Builder
		{
			private IDictionary[] m_assetMap;

			public Builder()
			{
				m_assetMap = new IDictionary[9];
			}

			public void Add<T>(string name, T value)
			{
				ResourceKind kind = GetKind(typeof(T));
				Dictionary<string, T> dictionary = GetMap<T>(kind) ?? new Dictionary<string, T>();
				dictionary.Add(name, value);
				SetMap(kind, dictionary);
			}

			public void AddRange<T>(IDictionary<string, T> source)
			{
				ResourceKind kind = GetKind(typeof(T));
				Dictionary<string, T> dictionary = GetMap<T>(kind) ?? new Dictionary<string, T>(source.Count);
				foreach (KeyValuePair<string, T> item in source)
				{
					dictionary.Add(item.Key, item.Value);
				}
				SetMap(kind, dictionary);
			}

			public void AddRange<T>(ICollection<T> source, Func<T, string> keySelector)
			{
				ResourceKind kind = GetKind(typeof(T));
				Dictionary<string, T> dictionary = GetMap<T>(kind) ?? new Dictionary<string, T>(source.Count);
				foreach (T item in source)
				{
					dictionary.Add(keySelector(item), item);
				}
				SetMap(kind, dictionary);
			}

			public ResourceManager Build()
			{
				int num = 9;
				IDictionary[] array = new IDictionary[num];
				Array.Copy(m_assetMap, array, num);
				return new ResourceManager(array);
			}

			private Dictionary<string, T> GetMap<T>(ResourceKind kind)
			{
				return (Dictionary<string, T>)m_assetMap[(int)kind];
			}

			private void SetMap<T>(ResourceKind kind, Dictionary<string, T> map)
			{
				m_assetMap[(int)kind] = map;
			}
		}

		private IDictionary[] m_assetMap;

		public ResourceManager()
		{
			m_assetMap = new IDictionary[9];
		}

		private ResourceManager(IDictionary[] map)
		{
			m_assetMap = map;
		}

		public object LoadAsset(ResourceKind kind, string name)
		{
			return m_assetMap[(int)kind][name];
		}

		public T LoadAsset<T>(string name)
		{
			return GetMap<T>(GetKind(typeof(T)))[name];
		}

		// LoadAsset<T> indexes the map directly, so a name that is not registered throws
		// KeyNotFoundException.  Callers that poll for an asset (for example a system whose
		// manager has not built the resource table yet) need a non-throwing probe instead.
		public bool TryLoadAsset<T>(string name, out T asset)
		{
			asset = default(T);
			IDictionary map = m_assetMap[(int)GetKind(typeof(T))];
			if (map == null || name == null || !map.Contains(name))
			{
				return false;
			}
			asset = (T)map[name];
			return asset != null;
		}

		private Dictionary<string, T> GetMap<T>(ResourceKind kind)
		{
			return (Dictionary<string, T>)m_assetMap[(int)kind];
		}

		private static ResourceKind GetKind(Type type)
		{
			if (type == typeof(string))
			{
				return ResourceKind.String;
			}
			if (type == typeof(Font))
			{
				return ResourceKind.Font;
			}
			if (type == typeof(GameObject))
			{
				return ResourceKind.GameObject;
			}
			if (type == typeof(Material))
			{
				return ResourceKind.Material;
			}
			if (type == typeof(ScriptableObject))
			{
				return ResourceKind.ScriptableObject;
			}
			if (type == typeof(Shader))
			{
				return ResourceKind.Shader;
			}
			if (type == typeof(TextAsset))
			{
				return ResourceKind.TextAsset;
			}
			if (type == typeof(Texture2D))
			{
				return ResourceKind.Texture2D;
			}
			return ResourceKind.None;
		}
	}
}
