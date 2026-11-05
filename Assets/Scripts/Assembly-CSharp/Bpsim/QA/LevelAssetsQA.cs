#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using Bpsim.Levels;
using UnityEngine;
using UnityEngine.UI;
using CompressionLevel = System.IO.Compression.CompressionLevel;

namespace Bpsim.QA
{
	public static class LevelAssetsQA
	{
		/// <summary>Isolated APK-path checks; does not enter Play Mode or change scenes.</summary>
		public static string Run()
		{
			string scratch = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp",
				"level-assets-qa-" + Guid.NewGuid().ToString("N")));
			Directory.CreateDirectory(scratch);
			List<string> checks = new List<string>();
			GameObject root = null;
			try
			{
				void Check(bool passed, string name)
				{
					if (!passed) throw new Exception("Level assets QA failed: " + name);
					checks.Add(name);
				}

				Check(LevelCatalog.IsAvailable && LevelCatalog.Levels.Count > 0, "desktop catalog available");
				LevelEntryJson first = LevelCatalog.Levels[0];
				string texturePath = Directory.GetFiles(Path.Combine(LevelCatalog.Root, "textures"), "*.png")[0];
				string texture = "textures/" + Path.GetFileName(texturePath);
				List<string> paths = new List<string> { "index.json", "prefabs.json", first.data, texture };
				if (!string.IsNullOrEmpty(first.contraption))
				{
					paths.Add("contraptions/" + first.contraption);
				}
				LevelAssetReader loose = new LevelAssetReader(LevelCatalog.Root);
				byte[] marker = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("level \u5173\u5361")).ToArray();
				foreach (CompressionLevel compression in new[] { CompressionLevel.NoCompression, CompressionLevel.Optimal })
				{
					string apk = Path.Combine(scratch, "base with spaces-" + compression + ".apk");
					using (FileStream file = File.Create(apk))
					using (ZipArchive archive = new ZipArchive(file, ZipArchiveMode.Create))
					{
						foreach (string path in paths)
						{
							using Stream entry = archive.CreateEntry("assets/BpsimLevels/" + path, compression).Open();
							byte[] bytes = loose.ReadAllBytes(path);
							entry.Write(bytes, 0, bytes.Length);
						}
						using Stream bom = archive.CreateEntry("assets/BpsimLevels/bom.txt", compression).Open();
						bom.Write(marker, 0, marker.Length);
					}
					string jarRoot = "jar:" + new Uri(apk).AbsoluteUri + "!/assets/BpsimLevels";
					LevelAssetReader packed = new LevelAssetReader(jarRoot);
					Check(!File.Exists(Path.Combine(jarRoot, "index.json")), compression + ": reproduces old Android gate failure");
					Check(packed.Exists("index.json"), compression + ": packaged index exists");
					Check(!packed.Exists("missing.bytes") && packed.ReadAllBytes("missing.bytes") == null,
						compression + ": missing asset stays missing");
					foreach (string path in paths)
					{
						Check(loose.ReadAllBytes(path).SequenceEqual(packed.ReadAllBytes(path)), compression + ": " + path);
					}
					Check(packed.ReadAllBytes(first.data.Replace('/', '\\')).SequenceEqual(loose.ReadAllBytes(first.data)),
						compression + ": mixed separators");
					Check(packed.ReadAllText("bom.txt") == "level \u5173\u5361", compression + ": UTF-8 BOM and Chinese text");
					Check(JsonUtility.FromJson<LevelIndexJson>(packed.ReadAllText("index.json")).levels.Length == LevelCatalog.Levels.Count,
						compression + ": index deserialization");
					Check(JsonUtility.FromJson<LevelPrefabTableJson>(packed.ReadAllText("prefabs.json")).items.Length > 0,
						compression + ": prefab deserialization");
					Check(LevelDocumentParser.Parse(packed.ReadAllBytes(first.data)).Count > 0,
						compression + ": binary level parsing");

					// Exercise the production availability/read path with an APK-shaped root.
					FieldInfo assets = typeof(LevelCatalog).GetField("s_assets", BindingFlags.Static | BindingFlags.NonPublic);
					object previous = assets.GetValue(null);
					try
					{
						assets.SetValue(null, packed);
						Check(LevelCatalog.IsAvailable, compression + ": bootstrap availability gate");
						Check(LevelCatalog.ReadLevelBytes(first).SequenceEqual(loose.ReadAllBytes(first.data)),
							compression + ": production payload reader");
						if (!string.IsNullOrEmpty(first.contraption))
						{
							Check(LevelCatalog.ReadContraption(first) == loose.ReadAllText("contraptions/" + first.contraption),
								compression + ": production contraption reader");
						}
						root = new GameObject("LevelAssetsQA");
						LevelMenu menu = LevelMenu.Create(root.transform);
						Button toggle = menu.transform.Find("LevelMenuToggle").GetComponent<Button>();
						Transform panel = menu.transform.Find("LevelMenuPanel");
						Check(toggle.gameObject.activeInHierarchy && !panel.gameObject.activeSelf,
							compression + ": visible level button");
						toggle.onClick.Invoke();
						Check(panel.gameObject.activeSelf, compression + ": button opens picker");
						Check(panel.Find("Levels/Viewport/Content").childCount > 0,
							compression + ": picker populated");
					}
					finally
					{
						assets.SetValue(null, previous);
						if (root != null) UnityEngine.Object.DestroyImmediate(root);
						root = null;
					}
				}
				LevelAssetReader missing = new LevelAssetReader("jar:" + new Uri(Path.Combine(scratch, "missing.apk")).AbsoluteUri
					+ "!/assets/BpsimLevels");
				Check(!missing.Exists("index.json") && missing.ReadAllText("index.json") == null,
					"missing archive stays unavailable");
				return "PASS: " + checks.Count + " checks\n" + string.Join("\n", checks);
			}
			finally
			{
				if (root != null) UnityEngine.Object.DestroyImmediate(root);
				Directory.Delete(scratch, recursive: true);
			}
		}
	}
}
#endif
