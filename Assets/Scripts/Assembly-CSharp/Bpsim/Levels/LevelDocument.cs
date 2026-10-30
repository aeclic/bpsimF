using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Bpsim.Levels
{
	/// <summary>
	/// A triangle list read out of a BPLE level binary. Vertices are in the terrain
	/// object's local space; the importer never re-winds or welds them.
	/// </summary>
	internal sealed class LevelMeshData
	{
		public Vector3[] Vertices;

		/// <summary>Fill meshes are UV-mapped by the importer (see <see cref="LevelImporter"/>).</summary>
		public Vector2[] UV;

		public Color[] Colors;

		public int[] Triangles;
	}

	/// <summary>One entry of an e2dTerrain's <c>CurveTextures</c> list.</summary>
	internal sealed class LevelCurveTexture
	{
		/// <summary>Index into the level loader's <c>m_references</c> list.</summary>
		public int TextureIndex;

		public Vector2 Size;

		public bool FixedAngle;

		public float FadeThreshold;
	}

	/// <summary>
	/// The payload of a level object whose <c>dataType</c> is <c>Terrain</c>.
	/// Mirrors <c>LevelLoader.ReadTerrain</c> of the main project.
	/// </summary>
	internal sealed class LevelTerrainData
	{
		public float FillTextureTileOffsetX;

		public float FillTextureTileOffsetY;

		public LevelMeshData Fill;

		public Color Color = Color.white;

		/// <summary>Index into the level loader's <c>m_references</c> list.</summary>
		public int FillTextureIndex;

		public LevelMeshData Curve;

		public readonly List<LevelCurveTexture> CurveTextures = new List<LevelCurveTexture>();

		/// <summary>
		/// Encoded PNG of the per-segment control strip that picks which curve splat a
		/// segment uses, always 1 pixel tall. Null when the level carries none.
		/// </summary>
		public byte[] ControlTexturePng;

		public bool HasCollider;
	}

	/// <summary>One node of a level's object tree.</summary>
	internal sealed class LevelObject
	{
		public string Name;

		/// <summary>Index into the level loader's <c>m_prefabs</c> list, or -1 for a group.</summary>
		public int PrefabIndex = -1;

		public Vector3 Position;

		public Vector3 Euler;

		public Vector3 Scale = Vector3.one;

		public LevelTerrainData Terrain;

		public readonly List<LevelObject> Children = new List<LevelObject>();

		public bool IsGroup => PrefabIndex < 0;

		public LevelObject Find(string name)
		{
			if (Name == name)
			{
				return this;
			}
			foreach (LevelObject child in Children)
			{
				LevelObject found = child.Find(name);
				if (found != null)
				{
					return found;
				}
			}
			return null;
		}
	}

	/// <summary>
	/// Little-endian port of the level binary reader used by the main project's
	/// <c>LevelLoader</c>. Only the fields bpsim actually consumes are retained.
	/// </summary>
	internal sealed class LevelByteReader
	{
		private readonly byte[] m_buffer;

		private int m_offset;

		public LevelByteReader(byte[] buffer)
		{
			m_buffer = buffer;
		}

		public int Offset => m_offset;

		public int Length => m_buffer.Length;

		public bool AtEnd => m_offset >= m_buffer.Length;

		public byte ReadByte()
		{
			return m_buffer[m_offset++];
		}

		public short ReadInt16()
		{
			short value = (short)(m_buffer[m_offset] | (m_buffer[m_offset + 1] << 8));
			m_offset += 2;
			return value;
		}

		public int ReadInt32()
		{
			int value = m_buffer[m_offset] | (m_buffer[m_offset + 1] << 8) | (m_buffer[m_offset + 2] << 16) | (m_buffer[m_offset + 3] << 24);
			m_offset += 4;
			return value;
		}

		public uint ReadUInt32()
		{
			return (uint)ReadInt32();
		}

		public float ReadSingle()
		{
			float value = BitConverter.ToSingle(m_buffer, m_offset);
			m_offset += 4;
			return value;
		}

		public bool ReadBoolean()
		{
			return ReadByte() != 0;
		}

		public Vector2 ReadVector2()
		{
			return new Vector2(ReadSingle(), ReadSingle());
		}

		public Vector3 ReadVector3()
		{
			return new Vector3(ReadSingle(), ReadSingle(), ReadSingle());
		}

		public Color ReadColor()
		{
			uint value = ReadUInt32();
			return new Color((float)((value >> 24) & 0xFF) / 255f, (float)((value >> 16) & 0xFF) / 255f, (float)((value >> 8) & 0xFF) / 255f, (float)(value & 0xFF) / 255f);
		}

		public byte[] ReadBytes(int count)
		{
			byte[] result = new byte[count];
			Buffer.BlockCopy(m_buffer, m_offset, result, 0, count);
			m_offset += count;
			return result;
		}

		public void Skip(int count)
		{
			m_offset += count;
		}

		/// <summary>BinaryReader.ReadString semantics: 7-bit encoded byte length, then UTF-8.</summary>
		public string ReadString()
		{
			int length = 0;
			int shift = 0;
			while (true)
			{
				byte current = m_buffer[m_offset++];
				length |= (current & 0x7F) << shift;
				if ((current & 0x80) == 0)
				{
					break;
				}
				shift += 7;
			}
			string value = Encoding.UTF8.GetString(m_buffer, m_offset, length);
			m_offset += length;
			return value;
		}
	}

	/// <summary>
	/// Parses a BPLE <c>Level_XX_data.bytes</c> payload into an object tree.
	/// </summary>
	internal static class LevelDocumentParser
	{
		/// <summary>
		/// The world-units-per-level-unit factor the game applies in
		/// <c>LevelLoader.ReadData</c> (<c>INFeature.TerrainScale</c> ×
		/// <c>LevelSceneSettings.TerrainScale</c> = 8 × 1). bpsim parts are one world unit
		/// per grid cell in the same units, so level geometry must be scaled by the same
		/// amount to sit at the right size next to a contraption.
		/// </summary>
		public const float TerrainScale = 8f;

		private const byte DataTypeNone = 0;

		private const byte DataTypeTerrain = 1;

		private const byte DataTypePrefabOverrides = 2;

		public static List<LevelObject> Parse(byte[] bytes)
		{
			if (bytes == null || bytes.Length < 4)
			{
				throw new InvalidOperationException("level payload is empty");
			}
			LevelByteReader reader = new LevelByteReader(bytes);
			int count = reader.ReadInt32();
			if (count < 0 || count > 100000)
			{
				throw new InvalidOperationException($"implausible root object count {count}");
			}
			List<LevelObject> objects = new List<LevelObject>(count);
			for (int i = 0; i < count; i++)
			{
				objects.Add(ReadObject(reader));
			}
			return objects;
		}

		private static LevelObject ReadObject(LevelByteReader reader)
		{
			short childCount = reader.ReadInt16();
			if (childCount == 0)
			{
				return ReadPrefabInstance(reader);
			}
			if (childCount < 0)
			{
				throw new InvalidOperationException($"negative child count {childCount}");
			}
			return ReadGroup(reader, childCount);
		}

		private static LevelObject ReadPrefabInstance(LevelByteReader reader)
		{
			LevelObject obj = new LevelObject
			{
				Name = reader.ReadString(),
				PrefabIndex = reader.ReadInt16(),
				Position = reader.ReadVector3(),
				Euler = reader.ReadVector3(),
				Scale = reader.ReadVector3()
			};
			ReadData(reader, obj);
			// LevelLoader.ReadData rescales x/y (not z) after the payload has been read.
			obj.Position = new Vector3(obj.Position.x * TerrainScale, obj.Position.y * TerrainScale, obj.Position.z);
			obj.Scale = new Vector3(obj.Scale.x * TerrainScale, obj.Scale.y * TerrainScale, obj.Scale.z);
			return obj;
		}

		private static LevelObject ReadGroup(LevelByteReader reader, int childCount)
		{
			LevelObject obj = new LevelObject
			{
				Name = reader.ReadString(),
				Position = reader.ReadVector3()
			};
			// LevelLoader.ReadParentObject never runs ReadData, so a group's own position
			// is deliberately left unscaled. Only its children are scaled.
			for (int i = 0; i < childCount; i++)
			{
				obj.Children.Add(ReadObject(reader));
			}
			return obj;
		}

		private static void ReadData(LevelByteReader reader, LevelObject obj)
		{
			switch (reader.ReadByte())
			{
			case DataTypeTerrain:
				obj.Terrain = ReadTerrain(reader);
				break;
			case DataTypePrefabOverrides:
				// Object-deserializer overrides only tweak gameplay components (camera
				// bounds, challenge timers…). bpsim has no counterpart for them yet.
				reader.Skip(reader.ReadInt32());
				break;
			case DataTypeNone:
				break;
			default:
				throw new InvalidOperationException($"unknown level data type for '{obj.Name}'");
			}
		}

		private static LevelTerrainData ReadTerrain(LevelByteReader reader)
		{
			LevelTerrainData terrain = new LevelTerrainData
			{
				FillTextureTileOffsetX = reader.ReadSingle(),
				FillTextureTileOffsetY = reader.ReadSingle()
			};
			terrain.Fill = ReadFillMesh(reader);
			terrain.Color = reader.ReadColor();
			terrain.FillTextureIndex = reader.ReadInt32();
			terrain.Curve = ReadCurveMesh(reader);
			int curveCount = reader.ReadInt32();
			for (int i = 0; i < curveCount; i++)
			{
				LevelCurveTexture texture = new LevelCurveTexture
				{
					TextureIndex = reader.ReadInt32(),
					Size = reader.ReadVector2(),
					FixedAngle = reader.ReadBoolean(),
					FadeThreshold = reader.ReadSingle()
				};
				terrain.CurveTextures.Add(texture);
			}
			if (reader.ReadInt32() > 0)
			{
				terrain.ControlTexturePng = reader.ReadBytes(reader.ReadInt32());
			}
			terrain.HasCollider = reader.ReadBoolean();
			return terrain;
		}

		private static LevelMeshData ReadFillMesh(LevelByteReader reader)
		{
			int vertexCount = reader.ReadInt32();
			Vector3[] vertices = new Vector3[vertexCount];
			for (int i = 0; i < vertexCount; i++)
			{
				Vector2 point = reader.ReadVector2();
				vertices[i] = new Vector3(point.x, point.y, 0f);
			}
			// The fill UVs need the prefab's FillTextureTileWidth/Height, which is not part
			// of the level payload; LevelImporter derives them once the prefab is known.
			return new LevelMeshData
			{
				Vertices = vertices,
				Triangles = ReadTriangles(reader)
			};
		}

		private static LevelMeshData ReadCurveMesh(LevelByteReader reader)
		{
			int vertexCount = reader.ReadInt32();
			Vector3[] vertices = new Vector3[vertexCount];
			Color[] colors = new Color[vertexCount];
			for (int i = 0; i < vertexCount; i++)
			{
				Vector2 point = reader.ReadVector2();
				vertices[i] = new Vector3(point.x, point.y, -0.01f);
				// Curve points get r = 1, the stripe twin r = 0; the curve material
				// blends splat 0 and splat 1 with that ramp.
				colors[i] = new Color((i + 1) % 2, 0f, 0f, 0f);
			}
			Vector2[] uv = new Vector2[vertexCount];
			int pairCount = vertexCount / 2;
			float distance = 0f;
			for (int i = 1; i < pairCount; i++)
			{
				int vertex = 2 * i;
				distance += (vertices[vertex] - vertices[vertex - 2]).magnitude;
				Vector2 coordinate = new Vector2(distance, i);
				uv[vertex] = coordinate;
				uv[vertex + 1] = coordinate;
			}
			return new LevelMeshData
			{
				Vertices = vertices,
				Colors = colors,
				UV = uv,
				Triangles = ReadTriangles(reader)
			};
		}

		private static int[] ReadTriangles(LevelByteReader reader)
		{
			int indexCount = reader.ReadInt32();
			int[] triangles = new int[indexCount];
			for (int i = 0; i < indexCount; i++)
			{
				triangles[i] = reader.ReadInt16();
			}
			return triangles;
		}
	}
}
