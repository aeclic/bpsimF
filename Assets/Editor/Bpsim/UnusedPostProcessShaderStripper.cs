using System.Collections.Generic;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bpsim.EditorTools
{
    /// <summary>
    /// Strips URP post-processing shader variants out of the build.
    ///
    /// The project renders through a custom BatchRendererGroup pipeline and both cameras ship
    /// with post-processing disabled (Camera m_RenderPostProcessing = 0), so none of the
    /// post-processing shaders are ever sampled at runtime. They are nevertheless pulled into
    /// the build through the URP renderer's PostProcessData, and - with URP's own "Strip Unused
    /// Post Processing Variants" not taking effect for this (shader-only, overridden) URP package -
    /// each pass expands to its full variant space. UberPost alone produced 46080 variants on a
    /// single graphics API (~15 min of shader compilation); removing them takes the shader stage
    /// of an Android build down to a few seconds.
    ///
    /// If post-processing is ever turned back on, remove the corresponding names from
    /// <see cref="Unused"/> (and re-enable the camera flag).
    /// </summary>
    public sealed class UnusedPostProcessShaderStripper : IPreprocessShaders
    {
        static readonly HashSet<string> Unused = new HashSet<string>
        {
            "Hidden/Universal Render Pipeline/UberPost",
            "Hidden/Universal Render Pipeline/FinalPost",
            "Hidden/Universal Render Pipeline/Bloom",
            "Hidden/Universal Render Pipeline/LutBuilderLdr",
            "Hidden/Universal Render Pipeline/LutBuilderHdr",
            "Hidden/Universal Render Pipeline/Scaling Setup",
            "Hidden/Universal Render Pipeline/GaussianDepthOfField",
            "Hidden/Universal Render Pipeline/BokehDepthOfField",
            "Hidden/Universal Render Pipeline/MotionBlur",
            "Hidden/Universal Render Pipeline/PaniniProjection",
            "Hidden/Universal Render Pipeline/TemporalAA",
            "Hidden/Universal Render Pipeline/CameraMotionBlur",
        };

        public int callbackOrder => 0;

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (shader != null && Unused.Contains(shader.name))
                data.Clear();
        }
    }
}
