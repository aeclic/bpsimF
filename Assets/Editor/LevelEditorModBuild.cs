#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;

namespace Bpsim.EditorTools
{
    public static class LevelEditorModBuild
    {
        public static void Refresh()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        public static string SourceStatus()
        {
            return string.Join("\n", CompilationPipeline.GetAssemblies().SelectMany(a => a.sourceFiles).Where(p => p.Contains("ModLevelServices") || p.Contains("ModWebService")));
        }

        public static string BuildMod()
        {
            string project = Path.GetDirectoryName(UnityEngine.Application.dataPath);
            string mod = Path.Combine(project, "Mods", "LevelEditor");
            string work = Path.Combine(project, "_codex_work", "level-editor-mod");
            Directory.CreateDirectory(work);
            UnityEditor.Compilation.Assembly host = CompilationPipeline.GetAssemblies(AssembliesType.Player)
                .First(assembly => assembly.name == "Assembly-CSharp");
            List<string> references = host.compiledAssemblyReferences.Concat(host.assemblyReferences.Select(assembly => assembly.outputPath))
                .Append(host.outputPath).Select(path => Path.IsPathRooted(path) ? path : Path.Combine(project, path))
                .Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            string output = Path.Combine(mod, "BpsimLevelEditor.dll");
            string response = Path.Combine(work, "mod-compile.rsp");
            Func<string, string> quote = path => "\"" + Path.GetFullPath(path).Replace('\\', '/') + "\"";
            StringBuilder options = new StringBuilder("-target:library\n-langversion:latest\n-nostdlib+\n-noconfig\n");
            options.AppendLine("-out:" + quote(output));
            foreach (string reference in references) options.AppendLine("-r:" + quote(reference));
            foreach (string source in Directory.GetFiles(Path.Combine(mod, "Code"), "*.cs")) options.AppendLine(quote(source));
            File.WriteAllText(response, options.ToString(), new UTF8Encoding(false));
            string runtime = Path.Combine(EditorApplication.applicationContentsPath, "MonoBleedingEdge");
            string mono = Path.Combine(runtime, "bin", "mono.exe");
            string compiler = Path.Combine(runtime, "lib", "mono", "4.5", "mcs.exe");
            if (!File.Exists(mono) || !File.Exists(compiler)) throw new FileNotFoundException("The Unity Mono Mod compiler is missing.");
            using (Process process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = mono, Arguments = quote(compiler) + " @" + quote(response),
                    WorkingDirectory = project, UseShellExecute = false, CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true
                };
                process.Start();
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(60000)) { process.Kill(); throw new TimeoutException("Mod compilation exceeded 60 seconds."); }
                string log = stdout.Result + stderr.Result;
                File.WriteAllText(Path.Combine(work, "mod-compile.log"), log, new UTF8Encoding(false));
                if (process.ExitCode != 0) throw new InvalidOperationException("Mod compilation failed:\n" + log);
                UnityEngine.Debug.Log("[LevelEditorModBuild] Built optional Mod DLL: " + output);
            }
            return output;
        }

        public static string BuildWaterAssets()
        {
            string project = Path.GetDirectoryName(UnityEngine.Application.dataPath);
            string mod = Path.Combine(project, "Mods", "LevelEditor");
            string staging = Path.Combine(project, "Assets", "Editor", "LevelEditorModAssets");
            string stagingTextures = Path.Combine(staging, "Textures");
            string output = Path.Combine(mod, "Assets");
            Directory.CreateDirectory(staging);
            Directory.CreateDirectory(stagingTextures);
            Directory.CreateDirectory(output);
            List<string> assetNames = new List<string>();
            foreach (string source in Directory.GetFiles(Path.Combine(mod, "Shaders"), "*.shader"))
            {
                File.Copy(source, Path.Combine(staging, Path.GetFileName(source)), true);
                assetNames.Add("Assets/Editor/LevelEditorModAssets/" + Path.GetFileName(source));
            }
            string textureSource = Path.Combine(mod, "Textures");
            if (Directory.Exists(textureSource))
            {
                foreach (string source in Directory.GetFiles(textureSource, "*.png", SearchOption.AllDirectories))
                {
                    string relative = source.Substring(textureSource.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (relative.StartsWith("UltimateWater" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                    string staged = Path.Combine(stagingTextures, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(staged));
                    File.Copy(source, staged, true);
                    assetNames.Add("Assets/Editor/LevelEditorModAssets/Textures/" + relative.Replace('\\', '/'));
                }
            }
            string ultimateWaterSource = Path.Combine(mod, "Web", "bpsim", "ultimate-water");
            if (!Directory.Exists(ultimateWaterSource))
                throw new DirectoryNotFoundException("Missing shared Ultimate Water textures: " + ultimateWaterSource);
            foreach (string source in Directory.GetFiles(ultimateWaterSource, "*.png", SearchOption.AllDirectories))
            {
                string relative = Path.Combine("UltimateWater", source.Substring(ultimateWaterSource.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                string staged = Path.Combine(stagingTextures, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(staged));
                File.Copy(source, staged, true);
                assetNames.Add("Assets/Editor/LevelEditorModAssets/Textures/" + relative.Replace('\\', '/'));
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (string assetName in assetNames)
                if (assetName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) ConfigureWaterTexture(assetName);
            AssetBundleBuild build = new AssetBundleBuild
            {
                assetBundleName = "water-windows.bundle",
                assetNames = assetNames.ToArray()
            };
            UnityEngine.AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(output, new[] { build },
                BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
            if (manifest == null) throw new InvalidOperationException("Water shader AssetBundle build failed.");
            return Path.Combine(output, build.assetBundleName);
        }

        /// <summary>Retain the demo's color space and bilinear mip filtering.</summary>
        private static void ConfigureWaterTexture(string path)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Missing texture importer for " + path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = !path.Contains("WaterNormals_01");
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.wrapMode = UnityEngine.TextureWrapMode.Repeat;
            importer.filterMode = UnityEngine.FilterMode.Bilinear;
            importer.anisoLevel = 1;
            importer.maxTextureSize = 1024;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        public static string BuildPackage()
        {
            BuildWaterAssets();
            BuildMod();
            string project = Path.GetDirectoryName(UnityEngine.Application.dataPath);
            string mod = Path.Combine(project, "Mods", "LevelEditor");
            if (!File.Exists(Path.Combine(mod, "Web", "index.html")))
                throw new FileNotFoundException("Run Tools/LevelEditor/sync_editor.py before packaging the offline editor.");
            string directory = Path.Combine(project, "Builds", "Mods");
            Directory.CreateDirectory(directory);
            string version = System.Xml.Linq.XDocument.Load(Path.Combine(mod, "Mod.xml")).Root.Element("Version").Value;
            string output = Path.Combine(directory, "BpsimLevelEditor-" + version + ".zip");
            using (FileStream file = new FileStream(output, FileMode.Create, FileAccess.Write))
            using (ZipArchive zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                foreach (string path in Directory.GetFiles(mod, "*", SearchOption.AllDirectories))
                {
                    if (path.EndsWith(".dshclaim", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                    string relative = path.Substring(mod.Length + 1).Replace('\\', '/');
                    ZipArchiveEntry entry = zip.CreateEntry(relative, CompressionLevel.Optimal);
                    using (Stream stream = entry.Open())
                    using (FileStream source = File.OpenRead(path)) source.CopyTo(stream);
                }
            }
            UnityEngine.Debug.Log("[LevelEditorModBuild] Packaged, not installed or enabled: " + output);
            return output;
        }
    }
}
#endif
