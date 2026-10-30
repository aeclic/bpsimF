using System.Text;
using UnityEditor;
using UnityEngine;

namespace Bpsim.EditorTools
{
    /// <summary>
    /// Shader QA probe: after the 2026-10-26 shader fixes, prove that the six shaders that failed the
    /// Windows/d3d11 player build now compile, including the variants the build compiler expands.
    ///
    ///   * Hidden/Universal Render Pipeline/Bloom + UberPost  - the failing variant was
    ///     `_FOVEATED_RENDERING_NON_UNIFORM_RASTER` ON (d3d11 has no PS5 RemapFoveatedRendering*).
    ///     Fixed by identity fallbacks in Packages/com.unity.render-pipelines.core/ShaderLibrary/
    ///     FoveatedRendering.hlsl.  This probe forces the global keyword on and compiles every pass.
    ///   * Hidden/OcclusionDebugOccluders / OcclusionDebugComposite / OccludeeScreenSpaceAABB
    ///     - no SRP shader library include, so UNITY_VERTEX_INPUT_INSTANCE_ID / TEXTURE2D were
    ///     unrecognised.  Now they include URP Core.hlsl.
    ///   * Bpsim/URP/Unlit_Texture_Instancing - the non-instanced branch redeclared _Color.._LayerTint
    ///     on top of UnityPerMaterial ("redefinition of '_Color'").
    ///
    /// Run it through the DSH bridge:
    ///   py br.py invoke_static '{"type":"Bpsim.EditorTools.ShaderVariantProbe","method":"Run"}'
    /// and read the "Shader error in" lines from the Console afterwards.
    /// </summary>
    public static class ShaderVariantProbe
    {
        static readonly string[] AllTargets =
        {
            "Hidden/Universal Render Pipeline/Bloom",
            "Hidden/Universal Render Pipeline/UberPost",
            "Hidden/Universal Render Pipeline/FinalPost",
            "Hidden/OcclusionDebugOccluders",
            "Hidden/OcclusionDebugComposite",
            "Hidden/OccludeeScreenSpaceAABB",
            "Bpsim/URP/Unlit_Texture_Instancing",
        };

        static readonly string[] FoveatedTargets =
        {
            "Hidden/Universal Render Pipeline/Bloom",
            "Hidden/Universal Render Pipeline/UberPost",
        };

        public static string Run()
        {
            var sb = new StringBuilder();
            var captured = new System.Collections.Generic.List<string>();
            Application.LogCallback handler = (condition, stackTrace, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Warning)
                    captured.Add(type + ": " + condition.Replace("\n", " "));
            };
            Application.logMessageReceived += handler;
            string report;
            try
            {
                report = RunCore(sb);
            }
            finally
            {
                Application.logMessageReceived -= handler;
            }

            sb.AppendLine("---- captured log messages (" + captured.Count + ") ----");
            foreach (string line in captured) sb.AppendLine("  " + line);
            sb.AppendLine("PROBE DONE");
            return sb.ToString();
        }

        static string RunCore(StringBuilder sb)
        {

            foreach (string name in AllTargets)
            {
                Shader shader = Shader.Find(name);
                if (shader == null)
                {
                    sb.AppendLine("MISSING shader: " + name);
                    continue;
                }

                string assetPath = AssetDatabase.GetAssetPath(shader);
                ShaderMessage[] importMessages = ShaderUtil.GetShaderMessages(shader);
                sb.AppendLine("== " + name);
                sb.AppendLine("   asset: " + assetPath + "  passes: " + shader.passCount
                              + "  importMessages: " + importMessages.Length);
                foreach (ShaderMessage m in importMessages)
                {
                    sb.AppendLine("   import " + m.severity + ": " + m.message + " (line " + m.line + ")");
                }
            }

            // Force-compile the keyword-ON foveated variants with a plain material.
            Shader.EnableKeyword("_FOVEATED_RENDERING_NON_UNIFORM_RASTER");
            try
            {
                foreach (string name in FoveatedTargets)
                {
                    Shader shader = Shader.Find(name);
                    if (shader == null) continue;

                    Material mat = new Material(shader);
                    mat.hideFlags = HideFlags.DontSave;
                    int passes = shader.passCount;
                    for (int p = 0; p < passes; p++)
                    {
                        ShaderUtil.CompilePass(mat, p, true);
                        ShaderUtil.CompilePass(mat, p, false);
                        sb.AppendLine("FOVEATED " + name + " pass " + p + " compiled");
                    }
                    Object.DestroyImmediate(mat);
                }
            }
            finally
            {
                Shader.DisableKeyword("_FOVEATED_RENDERING_NON_UNIFORM_RASTER");
            }

            // Occlusion debug shaders + the instancing shader: compile every pass of a fresh material
            // (keyword set is empty, i.e. the plain / non-instanced variant that used to fail).
            foreach (string name in new[]
                     {
                         "Hidden/OcclusionDebugOccluders",
                         "Hidden/OcclusionDebugComposite",
                         "Hidden/OccludeeScreenSpaceAABB",
                         "Bpsim/URP/Unlit_Texture_Instancing",
                     })
            {
                Shader shader = Shader.Find(name);
                if (shader == null) continue;

                Material mat = new Material(shader);
                mat.hideFlags = HideFlags.DontSave;
                int passes = shader.passCount;
                for (int p = 0; p < passes; p++)
                {
                    ShaderUtil.CompilePass(mat, p, true);
                    ShaderUtil.CompilePass(mat, p, false);
                    sb.AppendLine("PLAIN " + name + " pass " + p + " compiled");
                }
                Object.DestroyImmediate(mat);
            }

            sb.AppendLine("CORE DONE");
            return sb.ToString();
        }

        // ---- diagnostics for the compute shaders the player build reports as "was not imported correctly" ----

        static readonly string[] ComputeNames =
        {
            "BlendShapeComputeShader",
            "InstantiateDeformationData",
            "SkinningComputeShader",
            "SparseUploader",
        };

        /// <summary>
        /// The player build logs "Compute shader X was not imported correctly." for the four DOTS
        /// deformation kernels of com.unity.entities.graphics.  This dumps what the editor itself
        /// thinks of those assets (import messages, isSupported, kernels) so a build-only failure can
        /// be told apart from a broken asset.  ComputeShader is not a Shader subclass, so the
        /// Shader-based helpers above do not apply here.
        /// </summary>
        public static string ComputeReport()
        {
            var sb = new StringBuilder();
            foreach (string name in ComputeNames)
            {
                string[] guids = AssetDatabase.FindAssets(name + " t:ComputeShader");
                sb.AppendLine("== " + name + "  (ComputeShader assets: " + guids.Length + ")");
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    ComputeShader cs = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                    sb.AppendLine("   path: " + path);
                    if (cs == null)
                    {
                        sb.AppendLine("   LOAD FAILED");
                        continue;
                    }

                    sb.AppendLine("   kind=" + (path.EndsWith(".compute") ? "source" : "native-stub"));
                    try
                    {
                        int kernel = cs.FindKernel("SkinningDense1ComputeKernel");
                        sb.AppendLine("   FindKernel(SkinningDense1ComputeKernel) = " + kernel);
                    }
                    catch (System.Exception e)
                    {
                        sb.AppendLine("   FindKernel threw " + e.GetType().Name + ": " + e.Message);
                    }
                }
            }

            // What the renderer actually reports for the build target.
            sb.AppendLine("activeBuildTarget=" + EditorUserBuildSettings.activeBuildTarget
                          + "  selectedGroup=" + BuildPipeline.GetBuildTargetGroup(
                              EditorUserBuildSettings.activeBuildTarget));
            sb.AppendLine("COMPUTE REPORT DONE");
            return sb.ToString();
        }

        // ---- optional end-to-end check: a real Windows player build into a scratch directory ----

        static string _buildState = "idle";
        static string _buildReport = "";

        /// <summary>
        /// Kicks off a StandaloneWindows64 build of the enabled scenes into
        /// &lt;project&gt;/../_shaderfix_build (outside Assets).  Returns immediately; poll BuildStatus().
        /// This is the exact pipeline that failed with "Error building Player: 10 errors" before the
        /// shader fixes, so a Succeeded result proves the console is clean for the player too.
        /// </summary>
        public static string StartBuild()
        {
            if (_buildState == "running") return "already running";

            var scenes = new System.Collections.Generic.List<string>();
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
                if (s.enabled) scenes.Add(s.path);

            string outDir = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(Application.dataPath, "..", "..", "_shaderfix_build"));
            string outExe = System.IO.Path.Combine(outDir, "bpsim.exe");

            var opts = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = outExe,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };

            _buildState = "running";
            _buildReport = "scenes=" + scenes.Count + " out=" + outExe;
            EditorApplication.delayCall += () =>
            {
                try
                {
                    UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(opts);
                    _buildReport = "result=" + report.summary.result
                                   + " errors=" + report.summary.totalErrors
                                   + " warnings=" + report.summary.totalWarnings
                                   + " time=" + report.summary.totalTime
                                   + " out=" + outExe;
                }
                catch (System.Exception e)
                {
                    _buildReport = "exception: " + e.Message;
                }
                finally
                {
                    _buildState = "done";
                }
            };
            return "started: " + _buildReport;
        }

        public static string BuildStatus()
        {
            return _buildState + " | " + _buildReport;
        }
    }
}
