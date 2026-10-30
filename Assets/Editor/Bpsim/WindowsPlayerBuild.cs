using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Bpsim.EditorTools
{
    /// <summary>
    /// Windows 播放器打包入口。既可以走命令行
    ///   Unity.exe -batchmode -quit -executeMethod Bpsim.EditorTools.WindowsPlayerBuild.BuildDefault
    /// 也可以在编辑器里经 DshBridge 调
    ///   invoke_static type=Bpsim.EditorTools.WindowsPlayerBuild method=Build
    /// 输出目录默认 Builds/HavokTest（不覆盖既有产物）。
    /// </summary>
    public static class WindowsPlayerBuild
    {
        private const string DefaultOutDir = "Builds/HavokTest";

        public static void BuildDefault()
        {
            string dir = DefaultOutDir;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-outDir")
                {
                    dir = args[i + 1];
                }
            }
            Debug.Log("[Build] " + Build(dir));
        }

        public static string Build(string outDir)
        {
            StringBuilder sb = new StringBuilder();
            if (string.IsNullOrEmpty(outDir))
            {
                outDir = DefaultOutDir;
            }
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string fullOut = Path.IsPathRooted(outDir) ? outDir : Path.Combine(projectRoot, outDir);
            Directory.CreateDirectory(fullOut);
            string exe = Path.Combine(fullOut, "bpsim.exe");

            List<string> scenes = new List<string>();
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
            {
                if (s.enabled && !string.IsNullOrEmpty(s.path))
                {
                    scenes.Add(s.path);
                }
            }
            if (scenes.Count == 0)
            {
                return "no enabled scenes in EditorBuildSettings";
            }
            sb.Append("scenes=").Append(string.Join(",", scenes)).Append('\n');

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = exe,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None
            };

            // WinPlayerPostProcessor.PrepareForBuild cleans the directory recorded in
            // EditorUserBuildSettings.GetBuildLocation() (the last build path, persisted in
            // Library/EditorUserBuildSettings.asset).  If that stale path no longer exists the
            // whole build dies with "DirectoryNotFoundException: The path ... does not exist."
            // before doing any work, so always point it at the directory we are about to build.
            EditorUserBuildSettings.SetBuildLocation(BuildTarget.StandaloneWindows64, exe);

            string progress = Path.Combine(projectRoot, "Logs", "player-build.log");
            File.WriteAllText(progress, DateTime.Now.ToString("HH:mm:ss") + " START " + exe + "\n");
            DateTime started = DateTime.Now;
            BuildReport report = BuildPipeline.BuildPlayer(options);
            TimeSpan elapsed = DateTime.Now - started;
            BuildSummary summary = report.summary;
            sb.Append("result=").Append(summary.result)
              .Append(" errors=").Append(summary.totalErrors)
              .Append(" warnings=").Append(summary.totalWarnings)
              .Append(" sizeMB=").Append((summary.totalSize / (1024f * 1024f)).ToString("0.0"))
              .Append(" elapsed=").Append(elapsed.ToString(@"mm\:ss"))
              .Append('\n');
            sb.Append("exe=").Append(exe).Append('\n');
            if (summary.result != BuildResult.Succeeded)
            {
                foreach (BuildStep step in report.steps)
                {
                    foreach (BuildStepMessage m in step.messages)
                    {
                        if (m.type == LogType.Error || m.type == LogType.Exception)
                        {
                            sb.Append("  ").Append(step.name).Append(": ").Append(m.content).Append('\n');
                        }
                    }
                }
            }
            try
            {
                File.AppendAllText(progress, DateTime.Now.ToString("HH:mm:ss") + " " + sb.ToString());
            }
            catch (Exception)
            {
            }
            return sb.ToString();
        }
    }
}
