using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Bpsim.Rendering
{
	public class SpriteMap
	{
		public readonly struct MapHeader
		{
			public readonly string Name;

			public readonly int Width;

			public readonly int Height;

			public readonly float Scale;

			public MapHeader(string name, int width, int height, float scale)
			{
				Name = name;
				Width = width;
				Height = height;
				Scale = scale;
			}
		}

		public readonly struct MapEntry
		{
			public readonly int X;

			public readonly int Y;

			public readonly int Width;

			public readonly int Height;

			public readonly int PivotX;

			public readonly int PivotY;

			public readonly float ScaleX;

			public readonly float ScaleY;

			public MapEntry(int x, int y, int width, int height, int pivotX, int pivotY, float scaleX, float scaleY)
			{
				X = x;
				Y = y;
				Width = width;
				Height = height;
				PivotX = pivotX;
				PivotY = pivotY;
				ScaleX = scaleX;
				ScaleY = scaleY;
			}
		}

		private MapHeader m_header;

		private Dictionary<string, MapEntry> m_data;

		public int Count => m_data.Count;

		public MapHeader Header => m_header;

		public IReadOnlyDictionary<string, MapEntry> Data => m_data;

		public SpriteMap(MapHeader header, Dictionary<string, MapEntry> data)
		{
			m_header = header;
			m_data = data;
		}

		public bool Contains(string name)
		{
			return m_data.ContainsKey(name);
		}

		public MapEntry Get(string name)
		{
			return m_data[name];
		}

		public bool TryGet(string name, out MapEntry result)
		{
			return m_data.TryGetValue(name, out result);
		}

		public static SpriteMap Read(string text)
		{
			using StringReader reader = new StringReader(text);
			return Read(reader);
		}

		public static SpriteMap Read(TextReader reader)
		{
			IFormatProvider invariantInfo = NumberFormatInfo.InvariantInfo;
			string[] array = reader.ReadLine().Split(',');
			string name = array[0].Trim();
			int width = int.Parse(array[1], invariantInfo);
			int height = int.Parse(array[2], invariantInfo);
			float scale = float.Parse(array[3], invariantInfo);
			MapHeader header = new MapHeader(name, width, height, scale);
			Dictionary<string, MapEntry> dictionary = new Dictionary<string, MapEntry>();
			SpriteMap result = new SpriteMap(header, dictionary);
			while (reader.Peek() >= 0)
			{
				string text = reader.ReadLine();
				if (!string.IsNullOrEmpty(text))
				{
					string[] array2 = text.Split(',');
					string key = array2[0].Trim();
					int x = int.Parse(array2[1], invariantInfo);
					int y = int.Parse(array2[2], invariantInfo);
					int width2 = int.Parse(array2[3], invariantInfo);
					int height2 = int.Parse(array2[4], invariantInfo);
					int pivotX = int.Parse(array2[5], invariantInfo);
					int pivotY = int.Parse(array2[6], invariantInfo);
					float scaleX = float.Parse(array2[7], invariantInfo);
					float scaleY = float.Parse(array2[8], invariantInfo);
					dictionary.Add(key, new MapEntry(x, y, width2, height2, pivotX, pivotY, scaleX, scaleY));
				}
			}
			return result;
		}

		public static string Write(SpriteMap map)
		{
			using StringWriter stringWriter = new StringWriter();
			Write(stringWriter, map);
			return stringWriter.ToString();
		}

		public static void Write(TextWriter writer, SpriteMap map)
		{
			writer.Write(map.Header.Name);
			writer.Write(',');
			writer.Write(map.Header.Width);
			writer.Write(',');
			writer.Write(map.Header.Height);
			writer.Write(',');
			writer.Write(map.Header.Scale);
			writer.WriteLine();
			foreach (KeyValuePair<string, MapEntry> datum in map.Data)
			{
				writer.Write(datum.Key);
				writer.Write(',');
				writer.Write(datum.Value.X);
				writer.Write(',');
				writer.Write(datum.Value.Y);
				writer.Write(',');
				writer.Write(datum.Value.Width);
				writer.Write(',');
				writer.Write(datum.Value.Height);
				writer.Write(',');
				writer.Write(datum.Value.PivotX);
				writer.Write(',');
				writer.Write(datum.Value.PivotY);
				writer.Write(',');
				writer.Write(datum.Value.ScaleX);
				writer.Write(',');
				writer.Write(datum.Value.ScaleY);
				writer.WriteLine();
			}
		}
	}
}
