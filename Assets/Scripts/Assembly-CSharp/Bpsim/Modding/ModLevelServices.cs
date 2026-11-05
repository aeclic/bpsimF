using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Bpsim.Levels;
using Bpsim.Mods;
using Bpsim.Parts;
using Bpsim.UI;
using UnityEngine;

namespace Bpsim.Modding
{
	/// <summary>Host-owned resources are released even if a Mod's OnUnload throws.</summary>
	public static class ModHostResources
	{
		private static readonly Dictionary<Guid, List<IDisposable>> s_resources = new Dictionary<Guid, List<IDisposable>>();

		public static T Own<T>(ModEntryPoint owner, T resource) where T : IDisposable
		{
			CheckOwner(owner);
			if (resource == null) throw new ArgumentNullException(nameof(resource));
			if (!s_resources.TryGetValue(owner.Id, out List<IDisposable> resources))
			{
				resources = new List<IDisposable>();
				s_resources.Add(owner.Id, resources);
			}
			resources.Add(resource);
			return resource;
		}

		internal static void CheckOwner(ModEntryPoint owner)
		{
			if (owner == null || owner.Id == Guid.Empty || ModRegistry.GetModById(owner.Id)?.EntryPoint != owner)
			{
				throw new InvalidOperationException("This service requires a registered, loading or enabled Mod entry point.");
			}
		}

		internal static void Release(Guid owner)
		{
			if (!s_resources.TryGetValue(owner, out List<IDisposable> resources)) return;
			s_resources.Remove(owner);
			for (int i = resources.Count - 1; i >= 0; i--)
			{
				try { resources[i].Dispose(); }
				catch (Exception ex) { Debug.LogWarning("[Mods] Resource cleanup failed for " + owner + ": " + ex.Message); }
			}
		}
	}

	internal sealed class ModDisposableAction : IDisposable
	{
		private Action m_action;
		public ModDisposableAction(Action action) { m_action = action; }
		public void Dispose()
		{
			Action action = m_action;
			m_action = null;
			action?.Invoke();
		}
	}

	/// <summary>Modal input capture is opt-in and does not pause or replace the physics world.</summary>
	public static class ModInputCapture
	{
		private static int s_count;
		private static Behaviour s_operator;
		private static Behaviour s_camera;
		private static bool s_operatorEnabled;
		private static bool s_cameraEnabled;

		public static IDisposable Acquire(ModEntryPoint owner)
		{
			ModHostResources.CheckOwner(owner);
			if (s_count++ == 0)
			{
				s_operator = UserInterface.Instance != null ? UserInterface.Instance.PartOperator : null;
				s_camera = SceneCamera.Instance;
				s_operatorEnabled = s_operator != null && s_operator.enabled;
				s_cameraEnabled = s_camera != null && s_camera.enabled;
				if (s_operator != null) s_operator.enabled = false;
				if (s_camera != null) s_camera.enabled = false;
			}
			return ModHostResources.Own(owner, new ModDisposableAction(() =>
			{
				if (--s_count != 0) return;
				if (s_operator != null) s_operator.enabled = s_operatorEnabled;
				if (s_camera != null) s_camera.enabled = s_cameraEnabled;
				s_operator = null;
				s_camera = null;
			}));
		}
	}

	/// <summary>No Mod registrations means no additional buttons in the level picker.</summary>
	public static class ModLevelMenu
	{
		internal sealed class MenuAction
		{
			public Guid Owner;
			public string Label;
			public Action Click;
		}

		private static readonly List<MenuAction> s_actions = new List<MenuAction>();
		internal static event Action Changed;
		internal static IReadOnlyList<MenuAction> Actions => s_actions;

		public static IDisposable AddButton(ModEntryPoint owner, string label, Action onClick)
		{
			ModHostResources.CheckOwner(owner);
			if (string.IsNullOrWhiteSpace(label) || label.Length > 40 || onClick == null)
				throw new ArgumentException("A short label and callback are required.");
			if (s_actions.Exists(action => action.Owner == owner.Id))
				throw new InvalidOperationException("A Mod may register only one level-menu entry.");
			MenuAction entry = new MenuAction { Owner = owner.Id, Label = label, Click = onClick };
			s_actions.Add(entry);
			Changed?.Invoke();
			return ModHostResources.Own(owner, new ModDisposableAction(() =>
			{
				s_actions.Remove(entry);
				Changed?.Invoke();
			}));
		}
	}

	/// <summary>A Mod-provided level. ExtensionData is opaque to the base game.</summary>
	public sealed class ModLevelDefinition
	{
		public string Key;
		public string BaseScene;
		public string Title;
		public string[] Prefabs = Array.Empty<string>();
		public string[] References = Array.Empty<string>();
		public byte[] LevelBytes;
		public string Starter;
		public SchematicsFormat StarterFormat = SchematicsFormat.Xml;
		public string ExtensionData;
	}

	public sealed class ModLevelLoaded
	{
		public Guid Owner;
		public ModLevelDefinition Definition;
		public GameObject Root;
		public Vector3 Anchor;
		public float TerrainScale;
		public float VisualDepthOffset;
	}

	/// <summary>Generic level access; editor logic and custom files remain in the optional Mod.</summary>
	public static class ModLevels
	{
		private sealed class Catalog
		{
			public Guid Owner;
			public Func<IEnumerable<ModLevelDefinition>> Read;
		}

		private static readonly List<Catalog> s_catalogs = new List<Catalog>();
		private static event Action<ModLevelLoaded> Loaded;
		internal static event Action CatalogChanged;
		public const int MaximumLevelBytes = 16 * 1024 * 1024;

		public static ModLevelDefinition[] BuiltInLevels()
		{
			List<ModLevelDefinition> levels = new List<ModLevelDefinition>();
			foreach (LevelEntryJson entry in LevelCatalog.Levels)
			{
				if (entry.modOwner != Guid.Empty) continue;
				levels.Add(FromEntry(entry, false));
			}
			return levels.ToArray();
		}

		public static ModLevelDefinition ReadBuiltIn(string scene)
		{
			LevelEntryJson entry = LevelCatalog.FindLevel(scene);
			if (entry == null || entry.modOwner != Guid.Empty) throw new ArgumentException("Unknown built-in level: " + scene);
			return FromEntry(entry, true);
		}

		public static ModLevelDefinition CaptureCurrent()
		{
			LevelManager manager = LevelManager.Instance;
			if (manager == null || !manager.HasLevel) return null;
			ModLevelDefinition definition = FromEntry(manager.Entry, true);
			if (manager.Scene != null && PartManager.Instance != null && !PartManager.Instance.IsSimulating)
			{
				using MemoryStream stream = new MemoryStream();
				PartManager.Instance.SaveScene(stream, manager.Scene.SceneID, SchematicsFormat.Csv);
				definition.Starter = Encoding.UTF8.GetString(stream.ToArray());
				definition.StarterFormat = SchematicsFormat.Csv;
			}
			return definition;
		}

		private static ModLevelDefinition FromEntry(LevelEntryJson entry, bool includeData)
		{
			return new ModLevelDefinition
			{
				Key = entry.modDefinition != null ? entry.modDefinition.Key : entry.scene,
				BaseScene = entry.modDefinition != null ? entry.modDefinition.BaseScene : entry.scene,
				Title = entry.title,
				Prefabs = (string[])(entry.prefabs ?? Array.Empty<string>()).Clone(),
				References = (string[])(entry.references ?? Array.Empty<string>()).Clone(),
				LevelBytes = includeData ? LevelCatalog.ReadLevelBytes(entry) : null,
				Starter = includeData ? LevelCatalog.ReadContraption(entry) : null,
				StarterFormat = entry.modDefinition != null ? entry.modDefinition.StarterFormat : SchematicsFormat.Xml,
				ExtensionData = entry.modDefinition?.ExtensionData
			};
		}

		public static IDisposable RegisterCatalog(ModEntryPoint owner, Func<IEnumerable<ModLevelDefinition>> read)
		{
			ModHostResources.CheckOwner(owner);
			if (read == null) throw new ArgumentNullException(nameof(read));
			if (s_catalogs.Exists(catalog => catalog.Owner == owner.Id)) throw new InvalidOperationException("Catalog already registered.");
			Catalog registration = new Catalog { Owner = owner.Id, Read = read };
			s_catalogs.Add(registration);
			Refresh();
			return ModHostResources.Own(owner, new ModDisposableAction(() =>
			{
				s_catalogs.Remove(registration);
				if (LevelManager.Instance != null && LevelManager.Instance.Entry?.modOwner == registration.Owner)
					LevelManager.Instance.Unload();
				Refresh();
			}));
		}

		public static IDisposable SubscribeLoaded(ModEntryPoint owner, Action<ModLevelLoaded> handler)
		{
			ModHostResources.CheckOwner(owner);
			if (handler == null) throw new ArgumentNullException(nameof(handler));
			Action<ModLevelLoaded> filtered = loaded => { if (loaded.Owner == owner.Id) handler(loaded); };
			Loaded += filtered;
			return ModHostResources.Own(owner, new ModDisposableAction(() => Loaded -= filtered));
		}

		public static void Refresh()
		{
			LevelCatalog.InvalidateIndex();
			CatalogChanged?.Invoke();
		}

		internal static void AppendEntries(List<LevelEntryJson> entries)
		{
			foreach (Catalog catalog in s_catalogs)
			{
				try
				{
					int count = 0;
					HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
					IEnumerable<ModLevelDefinition> definitions = catalog.Read();
					if (definitions == null) continue;
					foreach (ModLevelDefinition definition in definitions)
					{
						if (++count > 512) throw new InvalidOperationException("A Mod catalog is limited to 512 levels.");
						if (definition == null || string.IsNullOrWhiteSpace(definition.Key) || !keys.Add(definition.Key)) continue;
						entries.Add(ToEntry(catalog.Owner, definition));
					}
				}
				catch (Exception ex) { Debug.LogWarning("[Mods] Level catalog " + catalog.Owner + ": " + ex.Message); }
			}
		}

		private static LevelEntryJson ToEntry(Guid owner, ModLevelDefinition definition)
		{
			if (definition.Key == null || definition.Key.Length > 80 || definition.Key.IndexOfAny(new[] { '/', '\\', '\0' }) >= 0)
				throw new ArgumentException("Invalid Mod level key.");
			return new LevelEntryJson
			{
				episode = "mod_" + owner.ToString("N"),
				scene = "mod:" + owner.ToString("N") + ":" + definition.Key,
				title = string.IsNullOrWhiteSpace(definition.Title) ? definition.Key : definition.Title,
				prefabs = definition.Prefabs ?? Array.Empty<string>(),
				references = definition.References ?? Array.Empty<string>(),
				modOwner = owner,
				modDefinition = definition
			};
		}

		public static bool Load(ModEntryPoint owner, ModLevelDefinition definition, out string error)
		{
			error = null;
			try
			{
				ModHostResources.CheckOwner(owner);
				if (!s_catalogs.Exists(catalog => catalog.Owner == owner.Id)) throw new InvalidOperationException("Register a level catalog before importing.");
				Validate(definition);
				if (LevelManager.Instance == null) throw new InvalidOperationException("LevelManager is not ready.");
				return LevelManager.Instance.Load(ToEntry(owner.Id, definition), out error);
			}
			catch (Exception ex) { error = ex.Message; return false; }
		}

		internal static void NotifyLoaded(LevelEntryJson entry, LevelImportResult result)
		{
			if (entry.modOwner == Guid.Empty || Loaded == null) return;
			ModLevelLoaded loaded = new ModLevelLoaded
			{
				Owner = entry.modOwner, Definition = entry.modDefinition, Root = result.Root,
				Anchor = result.Anchor, TerrainScale = LevelDocumentParser.TerrainScale,
				VisualDepthOffset = LevelImporter.DepthOffset
			};
			foreach (Action<ModLevelLoaded> handler in Loaded.GetInvocationList())
			{
				try { handler(loaded); }
				catch (Exception ex) { Debug.LogException(ex); }
			}
		}

		public static void Validate(ModLevelDefinition definition)
		{
			if (definition == null || definition.LevelBytes == null || definition.LevelBytes.Length < 4 || definition.LevelBytes.Length > MaximumLevelBytes)
				throw new FormatException("Level data is missing or exceeds 16 MiB.");
			if (definition.Prefabs == null || definition.References == null || definition.Prefabs.Length > 32767 || definition.References.Length > 65535)
				throw new FormatException("Invalid resource reference tables.");
			using BinaryReader reader = new BinaryReader(new MemoryStream(definition.LevelBytes, false), Encoding.UTF8);
			int remainingObjects = 100000;
			int roots = Count(reader, 100000);
			for (int i = 0; i < roots; i++) ValidateObject(reader, definition, 0, ref remainingObjects);
			if (reader.BaseStream.Position != reader.BaseStream.Length) throw new FormatException("Unexpected data after the level object tree.");
			if (!string.IsNullOrEmpty(definition.Starter))
			{
				byte[] starter = Encoding.UTF8.GetBytes(definition.Starter);
				if (starter.Length > MaximumLevelBytes) throw new FormatException("Starter contraption is too large.");
				using MemoryStream stream = new MemoryStream(starter, false);
				Schematics.CreateLoader(definition.StarterFormat).Read(stream);
			}
		}

		private static void ValidateObject(BinaryReader reader, ModLevelDefinition definition, int depth, ref int remaining)
		{
			if (depth > 128 || --remaining < 0) throw new FormatException("Level hierarchy limit exceeded.");
			short children = reader.ReadInt16();
			if (children < 0) throw new FormatException("Negative child count.");
			SkipString(reader);
			if (children > 0)
			{
				Floats(reader, 3);
				for (int i = 0; i < children; i++) ValidateObject(reader, definition, depth + 1, ref remaining);
				return;
			}
			int prefab = reader.ReadInt16();
			if (prefab < 0 || prefab >= definition.Prefabs.Length) throw new FormatException("Unresolved prefab index: " + prefab);
			Floats(reader, 9);
			switch (reader.ReadByte())
			{
			case 0: return;
			case 2: Skip(reader, Count(reader, 4 * 1024 * 1024)); return;
			case 1:
				Floats(reader, 2);
				ValidateMesh(reader);
				reader.ReadUInt32();
				Reference(reader, definition.References.Length);
				ValidateMesh(reader);
				int textures = Count(reader, 1024);
				for (int i = 0; i < textures; i++)
				{
					Reference(reader, definition.References.Length);
					Floats(reader, 2); reader.ReadBoolean(); Floats(reader, 1);
				}
				int control = Count(reader, 65535);
				if (control > 0) Skip(reader, Count(reader, 4 * 1024 * 1024));
				reader.ReadBoolean();
				return;
			default: throw new FormatException("Unknown level object data type.");
			}
		}

		private static void ValidateMesh(BinaryReader reader)
		{
			int vertices = Count(reader, 65535);
			Floats(reader, vertices * 2);
			int indices = Count(reader, 1000000);
			if (indices % 3 != 0) throw new FormatException("Terrain triangle index count is not divisible by three.");
			for (int i = 0; i < indices; i++)
			{
				short index = reader.ReadInt16();
				if (index < 0 || index >= vertices) throw new FormatException("Terrain index outside the vertex array.");
			}
		}

		private static void Floats(BinaryReader reader, int count)
		{
			for (int i = 0; i < count; i++)
			{
				float value = reader.ReadSingle();
				if (float.IsNaN(value) || float.IsInfinity(value) || Math.Abs(value) > 1000000f) throw new FormatException("Invalid level coordinate.");
			}
		}

		private static void Reference(BinaryReader reader, int length)
		{
			int reference = reader.ReadInt32();
			if (reference < 0 || reference >= length) throw new FormatException("Unresolved texture reference: " + reference);
		}

		private static int Count(BinaryReader reader, int maximum)
		{
			int value = reader.ReadInt32();
			if (value < 0 || value > maximum) throw new FormatException("Invalid level array length: " + value);
			return value;
		}

		private static void SkipString(BinaryReader reader)
		{
			uint length = 0;
			for (int shift = 0; shift < 35; shift += 7)
			{
				byte value = reader.ReadByte();
				if (shift == 28 && value > 7) throw new FormatException("Invalid string length.");
				length |= (uint)(value & 127) << shift;
				if (value < 128)
				{
					if (length > 65535) throw new FormatException("Level object name is too long.");
					Skip(reader, (int)length);
					return;
				}
			}
			throw new FormatException("Invalid string length.");
		}

		private static void Skip(BinaryReader reader, int count)
		{
			if (count > reader.BaseStream.Length - reader.BaseStream.Position) throw new EndOfStreamException("Truncated level data.");
			reader.BaseStream.Position += count;
		}
	}

	/// <summary>Flat, bounded, owner-scoped storage. Uninstalling a Mod does not erase its levels.</summary>
	public sealed class ModDataStore
	{
		private readonly string m_root;
		private ModDataStore(string root) { m_root = root; }
		public string Root => m_root;

		public static ModDataStore Open(ModEntryPoint owner, string name)
		{
			ModHostResources.CheckOwner(owner);
			CheckName(name);
			string root = Path.Combine(Application.persistentDataPath, "ModData", owner.Id.ToString("N"), name);
			Directory.CreateDirectory(root);
			return new ModDataStore(root);
		}

		public string[] Files(string extension)
		{
			if (extension == null || extension.Length < 2 || extension[0] != '.' || extension.IndexOfAny(new[] { '*', '?', '/', '\\' }) >= 0)
				throw new ArgumentException("Expected a file extension.");
			string[] paths = Directory.GetFiles(m_root, "*" + extension, SearchOption.TopDirectoryOnly);
			for (int i = 0; i < paths.Length; i++) paths[i] = Path.GetFileName(paths[i]);
			Array.Sort(paths, StringComparer.Ordinal);
			return paths;
		}

		public string ReadText(string name)
		{
			string path = Resolve(name);
			if (!File.Exists(path)) return null;
			if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new IOException("Mod document exceeds 32 MiB.");
			return File.ReadAllText(path, Encoding.UTF8);
		}

		public void WriteText(string name, string text)
		{
			string path = Resolve(name);
			byte[] bytes = new UTF8Encoding(false).GetBytes(text ?? string.Empty);
			if (bytes.Length > 32 * 1024 * 1024) throw new IOException("Mod document exceeds 32 MiB.");
			string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
			try
			{
				using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
					stream.Write(bytes, 0, bytes.Length);
					stream.Flush(true);
				}
				if (File.Exists(path)) File.Replace(temporary, path, null);
				else File.Move(temporary, path);
			}
			finally { if (File.Exists(temporary)) File.Delete(temporary); }
		}

		public void Delete(string name) { File.Delete(Resolve(name)); }

		private string Resolve(string name)
		{
			CheckName(name);
			string path = Path.Combine(m_root, name);
			if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
				throw new IOException("Mod storage does not follow links.");
			return path;
		}

		private static void CheckName(string name)
		{
			if (string.IsNullOrWhiteSpace(name) || name.Length > 120 || name == "." || name == ".." ||
				name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0)
				throw new ArgumentException("Expected a simple file or directory name.");
		}
	}
}
