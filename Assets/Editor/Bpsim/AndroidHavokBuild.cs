using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Bpsim.EditorTools
{
    internal sealed class AndroidHavokBuild : IPostGenerateGradleAndroidProject, IPostprocessBuildWithReport
    {
        private const string LibraryName = "libHavokNative.so";

        public int callbackOrder => 0;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string[] libraries = FindLibraries(Path.Combine(path, "src", "main", "jniLibs")).ToArray();
            foreach (string library in libraries.Where(HasWrongCase))
            {
                string canonical = Path.Combine(Path.GetDirectoryName(library), LibraryName);
                if (libraries.Contains(canonical, StringComparer.Ordinal))
                    throw new BuildFailedException($"Duplicate Havok libraries in {Path.GetDirectoryName(library)}");

                string temporary = library + "." + Guid.NewGuid().ToString("N");
                File.Move(library, temporary);
                try
                {
                    File.Move(temporary, canonical);
                }
                catch
                {
                    File.Move(temporary, library);
                    throw;
                }
                Debug.Log($"[AndroidHavokBuild] Corrected generated library name: {canonical}");
            }

            foreach (string build in new[] { Path.Combine(path, "build"), Path.Combine(path, "..", "launcher", "build") })
                foreach (string cached in FindLibraries(build).Where(HasWrongCase).ToArray())
                    File.Delete(cached);
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            string output = report.summary.outputPath;
            if (report.summary.platform != BuildTarget.Android || !string.Equals(Path.GetExtension(output), ".apk", StringComparison.OrdinalIgnoreCase))
                return;

            using var stream = File.OpenRead(output);
            using var apk = new ZipArchive(stream, ZipArchiveMode.Read);
            const string armv7 = "lib/armeabi-v7a/";
            if (apk.Entries.Any(entry => entry.FullName.StartsWith("lib/", StringComparison.Ordinal) &&
                entry.Name.Equals(LibraryName, StringComparison.OrdinalIgnoreCase) && entry.Name != LibraryName) ||
                (apk.GetEntry(armv7 + "libunity.so") != null && apk.GetEntry(armv7 + LibraryName) == null))
                throw new BuildFailedException($"{output} is missing {LibraryName} or contains it with incorrect casing.");
        }

        private static bool HasWrongCase(string path) => Path.GetFileName(path) != LibraryName;

        private static IEnumerable<string> FindLibraries(string path) =>
            Directory.Exists(path)
                ? Directory.EnumerateFiles(path, "*.so", SearchOption.AllDirectories)
                    .Where(file => Path.GetFileName(file).Equals(LibraryName, StringComparison.OrdinalIgnoreCase))
                : Array.Empty<string>();
    }
}
