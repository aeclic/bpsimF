#ifndef UNITY_FOVEATED_RENDERING_INCLUDED
#define UNITY_FOVEATED_RENDERING_INCLUDED

#if defined(SHADER_API_PS5) && defined(_FOVEATED_RENDERING_NON_UNIFORM_RASTER)
#include "Packages/com.unity.render-pipelines.ps5/ShaderLibrary/API/FoveatedRendering_PSSL.hlsl"
#else
// bpsim-main: the shipped URP 14.0.7 shaders (Bloom, UberPost, SSAO, XRMirrorView, ...) declare
// `#pragma multi_compile_fragment _ _FOVEATED_RENDERING_NON_UNIFORM_RASTER`, so a Windows/d3d11
// player build compiles the keyword-ON variant of every one of them and calls the Remap* helpers
// below.  Upstream only defines those helpers for PS5 (FoveatedRendering_PSSL.hlsl), which makes
// that variant fail with "undeclared identifier 'RemapFoveatedRenderingDistort/Resolve/Density'"
// (seen in bpsim-main's build: Bloom line 60, UberPost line 184).  Identity fallbacks keep the
// keyword-ON variant compilable everywhere; it is dead code on desktop (URP only enables that
// keyword for PS5 / variable-rate-shading XR), so identity is the correct behaviour here.
float2 RemapFoveatedRenderingDistort(float2 uv) { return uv; }
float2 RemapFoveatedRenderingResolve(float2 uv) { return uv; }
float2 RemapFoveatedRenderingNaive(float2 uv)   { return uv; }
float2 RemapFoveatedRenderingDensity(float2 uv) { return float2(1.0f, 1.0f); }
#endif

#endif // UNITY_FOVEATED_RENDERING_INCLUDED
