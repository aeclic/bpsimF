using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bpsim.EditorTools
{
    /// <summary>
    /// Android build entry points for bpsim-main.
    ///
    /// Two project-level problems are addressed here, both verified on the LDPlayer emulator:
    ///
    /// 1. "App will not open on Android": the previous release shipped with the Android graphics
    ///    API list left at its default (Vulkan first, then GLES3). On Android the player brings up
    ///    Vulkan and dies while the graphics device thread creates a descriptor update template
    ///    (SIGSEGV in the Vulkan encoder) before the first frame ever reaches the game. Forcing
    ///    OpenGL ES 3.x only removes that path entirely.
    ///
    /// 2. "Android packaging is far too slow": with URP "Strip Unused Post Processing Variants"
    ///    disabled (UniversalRenderPipelineGlobalSettings.m_StripUnusedPostProcessingVariants = 0)
    ///    the URP post shaders keep their whole variant space, so a single UberPost pass tries to
    ///    compile 92160 variants (plus FinalPost 2560 x2). Enabling the strip removes them because
    ///    the project renders with post-processing turned off (camera m_RenderPostProcessing = 0).
    ///    Dropping Vulkan additionally halves the number of shader variants that must be compiled.
    /// </summary>
    public static class AndroidBuild
    {
        const string ApkDir  = "Builds/Android";
        const string ApkName = "bpsimF_android.apk";

        [MenuItem("Tools/BPSim/Android/Apply Android Settings")]
        public static void ApplyAndroidSettings()
        {
            // --- Graphics API: OpenGL ES 3.x only (no Vulkan) ---
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });

            // --- CPU architecture: 32-bit ARM (proven to run on the emulator via Houdini). ---
            // Flip to AndroidArchitecture.ARM64 for a modern 64-bit-only store build.
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7;

            // --- Keep Mono (fast packaging) and everything else stable ---
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.Mono2x);
            PlayerSettings.Android.minSdkVersion    = AndroidSdkVersions.AndroidApiLevel22;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.useAPKExpansionFiles = false;

            EditorUserBuildSettings.buildAppBundle = false; // plain .apk, not .aab
            EditorUserBuildSettings.development    = false;

            AssetDatabase.SaveAssets();
            UnityEngine.Debug.Log($"[AndroidBuild] gl {string.Join(",", PlayerSettings.GetGraphicsAPIs(BuildTarget.Android))} " +
                      $"arch {PlayerSettings.Android.targetArchitectures} backend {PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android)}");
        }

        [MenuItem("Tools/BPSim/Android/Build APK")]
        public static void BuildMenu() => Build();

        /// <summary>Batch/menu entry: apply settings then build the APK.</summary>
        public static void Build()
        {
            ApplyAndroidSettings();

            var scenes = new List<string>();
            foreach (var s in EditorBuildSettings.scenes)
                if (s.enabled) scenes.Add(s.path);
            if (scenes.Count == 0) scenes.Add("Assets/Scenes/MainScene.unity");

            Directory.CreateDirectory(ApkDir);
            string outPath = Path.Combine(ApkDir, ApkName).Replace('\\', '/');

            var sw = Stopwatch.StartNew();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes           = scenes.ToArray(),
                locationPathName = outPath,
                target           = BuildTarget.Android,
                targetGroup      = BuildTargetGroup.Android,
                options          = BuildOptions.None,
            });
            sw.Stop();

            var r = report.summary;
            UnityEngine.Debug.Log($"[AndroidBuild] result={r.result} size={r.totalSize} bytes " +
                      $"time={sw.Elapsed.TotalSeconds:F1}s steps={r.totalErrors} errors/{r.totalWarnings} warnings -> {outPath}");

            if (Application.isBatchMode)
                EditorApplication.Exit(r.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
