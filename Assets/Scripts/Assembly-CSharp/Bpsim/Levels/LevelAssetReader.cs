using System;
using System.IO;
using System.IO.Compression;

namespace Bpsim.Levels
{
	/// <summary>Reads loose StreamingAssets or entries inside an Android APK/OBB.</summary>
	internal sealed class LevelAssetReader
	{
		private readonly string m_root;

		private readonly string m_archivePath;

		private readonly string m_entryRoot;

		public LevelAssetReader(string root)
		{
			m_root = root;
			if (!root.StartsWith("jar:", StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
			// Android StreamingAssets is a jar:file URI, not a filesystem directory.
			int separator = root.IndexOf("!/", StringComparison.Ordinal);
			if (separator < 0)
			{
				throw new ArgumentException("Invalid StreamingAssets archive URI: " + root, nameof(root));
			}
			Uri archiveUri = new Uri(root.Substring(4, separator - 4), UriKind.Absolute);
			if (!archiveUri.IsFile)
			{
				throw new ArgumentException("StreamingAssets archive must be a local file: " + root, nameof(root));
			}
			m_archivePath = archiveUri.LocalPath;
			m_entryRoot = root.Substring(separator + 2).Replace('\\', '/').TrimEnd('/') + "/";
		}

		public bool Exists(string relativePath)
		{
			if (m_archivePath == null)
			{
				return File.Exists(Path.Combine(m_root, relativePath));
			}
			if (!File.Exists(m_archivePath))
			{
				return false;
			}
			using FileStream file = File.OpenRead(m_archivePath);
			using ZipArchive archive = new ZipArchive(file, ZipArchiveMode.Read);
			return archive.GetEntry(EntryPath(relativePath)) != null;
		}

		public byte[] ReadAllBytes(string relativePath)
		{
			if (m_archivePath == null)
			{
				string path = Path.Combine(m_root, relativePath);
				return File.Exists(path) ? File.ReadAllBytes(path) : null;
			}
			if (!File.Exists(m_archivePath))
			{
				return null;
			}
			using FileStream file = File.OpenRead(m_archivePath);
			using ZipArchive archive = new ZipArchive(file, ZipArchiveMode.Read);
			ZipArchiveEntry entry = archive.GetEntry(EntryPath(relativePath));
			if (entry == null)
			{
				return null;
			}
			using Stream stream = entry.Open();
			using MemoryStream bytes = new MemoryStream();
			stream.CopyTo(bytes);
			return bytes.ToArray();
		}

		public string ReadAllText(string relativePath)
		{
			byte[] bytes = ReadAllBytes(relativePath);
			if (bytes == null)
			{
				return null;
			}
			using StreamReader reader = new StreamReader(new MemoryStream(bytes, writable: false));
			return reader.ReadToEnd();
		}

		private string EntryPath(string relativePath)
		{
			return m_entryRoot + relativePath.Replace('\\', '/');
		}
	}
}
