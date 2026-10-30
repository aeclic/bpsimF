using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;

namespace Bpsim.EditorTools
{
	public static class FrameShapesBuildPostprocessor
	{
		[PostProcessBuild(1000)]
		public static void Deploy(BuildTarget target, string path)
		{
			if (target != BuildTarget.StandaloneWindows64) return;
			string project = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
			string script = Path.Combine(project, "Mods", "FrameShapes", "deploy.ps1");
			var info = new ProcessStartInfo("powershell.exe")
			{
				Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" -GameDir \"" + Path.GetDirectoryName(path) + "\" -UnityEditor \"" + Path.GetDirectoryName(EditorApplication.applicationPath) + "\"",
				UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
			};
			using var process = Process.Start(info);
			var output = process.StandardOutput.ReadToEndAsync();
			var error = process.StandardError.ReadToEndAsync();
			process.WaitForExit();
			if (process.ExitCode != 0) throw new Exception("BepInEx 变形框部署失败：" + output.Result + error.Result);
			UnityEngine.Debug.Log(output.Result);
		}
	}
}
