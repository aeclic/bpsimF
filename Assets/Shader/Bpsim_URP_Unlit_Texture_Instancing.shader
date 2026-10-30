Shader "Bpsim/URP/Unlit_Texture_Instancing"
{
    // Reconstructed. Evidence:
    //  * Name/file/GUID from the decompiler stub; property names, display names and pass tags
    //    from the shipping player build's parsed shader form
    //    (bpsim_2023.0/bpsim_2023.0.2_windows_x64/bpsim_Data/sharedassets0.assets).
    //  * Pass tag must be LightMode=Universal2D: the project renders through URP 14.0.7's 2D
    //    renderer (URP_2D_Asset.asset -> URP_2D_Asset_Renderer.asset) and Render2DLightingPass
    //    draws "Universal2D" plus the legacy "SRPDefaultUnlit".
    //  * Instancing is mandatory: Part_Texture_0.mat has m_EnableInstancingVariants 1, and both
    //    call sites feed per-instance data - SceneRenderSystem through a BatchRendererGroup and
    //    MaterialSprite through a MaterialPropertyBlock.
    //  * SceneRenderSystem.InitializeProperties() declares exactly six per-instance properties:
    //    unity_ObjectToWorld / unity_WorldToObject at UnsafeUtility.SizeOf<float3x4>() = 48 bytes
    //    and _Color / _Rect / _UVRect / _BlendFactor at 16 bytes, published as
    //    MetadataValue{ Value = (uint)(int.MinValue | byteOffset) } and bound with
    //    AddBatch(metadata, bufferHandle).  That is Unity's DOTS instancing metadata layout -
    //    UnityInstancing.hlsl's UNITY_DOTS_MATRIX_M loads exactly `float3x4 unity_ObjectToWorld`.
    //  * Geometry: SpriteUtility.PopulateMesh puts the corners at
    //    (VertexX +- VertexW/2, VertexY +- VertexH/2) with UV (U,V)..(U+W,V+H), and every caller
    //    uses the built-in "Quad.fbx" (-0.5..0.5), so _Rect.xy is the local centre, _Rect.zw the
    //    full size, and _UVRect.xy/zw the atlas origin/extent.
    //  * _BlendFactor = (textureWeight, colourWeight, frameShape, glassFill).
    //    Shapes reuse the existing DOTS instance buffer and draw batch; ordinary parts use z=0.
    //  * No fog handling: MainScene's RenderSettings has m_Fog 0.
    Properties
    {
        _MainTex ("Main Texture", 2D) = "white" {}
        _Color ("Main Color", Vector) = (1,1,1,1)
        _Rect ("Rect", Vector) = (0,0,1,1)
        _UVRect ("UV Rect", Vector) = (0,0,1,1)
        _BlendFactor ("Blend Factor", Vector) = (1,0,0,0)
        _LayerTint ("Layer Tint", Vector) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "QUEUE" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);

        // These four MUST stay inside UnityPerMaterial: a BatchRendererGroup refuses to draw a pass
        // that is not SRP Batcher compatible ("A BatchDrawCommand is using a pass ... that is not
        // SRP Batcher compatible. Reason: Material property is found in another cbuffer than
        // UnityPerMaterial"), and they are also the defaults used when a batch does not override
        // the property.  See the metadata block below for how the per-instance values arrive.
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float4 _Color;
            float4 _Rect;
            float4 _UVRect;
            float4 _BlendFactor;
            float4 _LayerTint;
        CBUFFER_END

        // The BatchRendererGroup metadata path that SceneRenderSystem publishes.  Two hard rules:
        //
        // 1) The block name has to be "MaterialPropertyMetadata".  Unity's BRG only fills the
        //    metadata cbuffer with that exact name; a custom name (this block used to be called
        //    "BpsimInstanceMetadata") silently receives zeros, so every property fell back to its
        //    UnityPerMaterial default - which made each part draw the whole 4096x2048 atlas inside
        //    a 1x1 quad while unity_ObjectToWorld (URP's builtin block) still worked.  This is the
        //    same pattern URP uses in Shaders/UnlitInput.hlsl:13-23.
        // 2) Under a non-instanced variant the DOTS block does not exist: UnityInstancing.hlsl
        //    only declares UNITY_DOTS_INSTANCING_START / UNITY_ACCESS_DOTS_INSTANCED_PROP_* inside
        //    "#if defined(UNITY_INSTANCING_ENABLED) || defined(UNITY_DOTS_INSTANCING_ENABLED)"
        //    (UnityInstancing.hlsl:239), so an unguarded block makes the plain variant fail with
        //    "unrecognized identifier 'UNITY_DOTS_INSTANCING_START'".  The plain variant therefore
        //    reads the UnityPerMaterial members declared above - MaterialSprite drives the palette
        //    icons through a MaterialPropertyBlock, which writes exactly those cbuffer slots.
        //    Do NOT redeclare _Color/_Rect/_UVRect/_BlendFactor/_LayerTint in the #else branch:
        //    they are already inside UnityPerMaterial, and the duplicate made the d3d11 player build
        //    fail with "redefinition of '_Color'".
        //
        // Do NOT declare unity_ObjectToWorld / unity_WorldToObject here: URP's
        // ShaderLibrary/UniversalDOTSInstancing.hlsl (pulled in by Core.hlsl -> Input.hlsl)
        // already declares them in its own universal UNITY_DOTS_INSTANCING_START(
        // BuiltinPropertyMetadata) block, and repeating them aborts the DOTS_INSTANCING_ON
        // variant with "redefinition of 'unity_DOTSInstancingF48_Metadataunity_ObjectToWorld'"
        // (line 72).  The transform is consumed through TransformObjectToHClip ->
        // UNITY_MATRIX_M -> UNITY_DOTS_MATRIX_M, i.e. the builtin declaration;
        // SceneRenderSystem's metadata still supplies it under the same "unity_ObjectToWorld"
        // property id.
        #if defined(UNITY_INSTANCING_ENABLED) || defined(UNITY_DOTS_INSTANCING_ENABLED)
        UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
            UNITY_DOTS_INSTANCED_PROP(float4, _Color)
            UNITY_DOTS_INSTANCED_PROP(float4, _Rect)
            UNITY_DOTS_INSTANCED_PROP(float4, _UVRect)
            UNITY_DOTS_INSTANCED_PROP(float4, _BlendFactor)
            UNITY_DOTS_INSTANCED_PROP(float4, _LayerTint)
        UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
        #define BPSIM_INSTANCED_PROP(type, name, def) UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(type, name)
        #else
        #define BPSIM_INSTANCED_PROP(type, name, def) name
        #endif

        struct BpsimAttributes
        {
            float3 positionOS : POSITION;
            float2 uv         : TEXCOORD0;
            float4 color      : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct BpsimVaryings
        {
            float4 positionCS : SV_POSITION;
            float2 uv         : TEXCOORD0;
            float4 color      : COLOR;
            float4 tint       : TEXCOORD1;
            float4 blend      : TEXCOORD2;
            float4 layerTint  : TEXCOORD3;
            float2 shapePoint : TEXCOORD4;
            float4 atlasRect  : TEXCOORD5;
        };

        BpsimVaryings BpsimUnlitVertex(BpsimAttributes input)
        {
            BpsimVaryings output = (BpsimVaryings)0;
            UNITY_SETUP_INSTANCE_ID(input);

            float4 rect   = BPSIM_INSTANCED_PROP(float4, _Rect, float4(0, 0, 1, 1));
            float4 uvRect = BPSIM_INSTANCED_PROP(float4, _UVRect, float4(0, 0, 1, 1));

            // Quad.fbx spans -0.5..0.5; _Rect.xy is the centre and _Rect.zw the full size.
            float3 positionOS = float3(input.positionOS.xy * rect.zw + rect.xy, input.positionOS.z);

            output.positionCS = TransformObjectToHClip(positionOS);
            output.uv         = input.uv * uvRect.zw + uvRect.xy;
            output.shapePoint = input.positionOS.xy + 0.5;
            output.atlasRect  = uvRect;
            output.color      = input.color;
            output.tint       = BPSIM_INSTANCED_PROP(float4, _Color, float4(1, 1, 1, 1));
            output.blend      = BPSIM_INSTANCED_PROP(float4, _BlendFactor, float4(1, 0, 0, 0));
            output.layerTint  = BPSIM_INSTANCED_PROP(float4, _LayerTint, float4(1, 1, 1, 1));

            return output;
        }

        half4 BpsimUnlitFragment(BpsimVaryings input) : SV_Target
        {

            float2 sampleUV = input.uv;
            if (input.blend.z > 0.5)
            {
                float2 p = input.shapePoint;
                float edge;
                if (input.blend.z < 1.5)
                    edge = min(min(p.x, 1-p.y), (p.y-p.x)*0.70710678);
                else if (input.blend.z < 2.5)
                    edge = min(p.y, 0.5-length(float2(p.x-0.5,p.y)));
                else
                    edge = 0.5-length(p-0.5);
                clip(edge);
                if (input.blend.w < 0.5)
                {
                    clip(0.13-edge);
                    // Sample the original frame's bottom edge, as BPIR's generated edge strips do.
                    sampleUV = input.atlasRect.xy + float2(p.x, edge)*input.atlasRect.zw;
                }
            }
            half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, sampleUV);

            // _BlendFactor.x weights the sprite's own texture, .y weights _Color.  (1,0,0,0) is
            // the identity for ordinary parts; (0,0.75,0,0) is used for a tinted frame.
            half3 rgb = tex.rgb * (half)input.blend.x + (half3)input.tint.rgb * (half)input.blend.y;
            rgb *= (half3)input.layerTint.rgb;
            return half4(rgb, tex.a) * (half4)input.color * (half)input.tint.a;
        }
        ENDHLSL

        Pass
        {
            Name "BpsimUnlit2D"
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma vertex BpsimUnlitVertex
            #pragma fragment BpsimUnlitFragment
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma instancing_options assumeuniformscaling
            ENDHLSL
        }

        // Drawn by the 2D renderer's legacy pass list, and by a forward renderer.
        Pass
        {
            Name "BpsimUnlit"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex BpsimUnlitVertex
            #pragma fragment BpsimUnlitFragment
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma instancing_options assumeuniformscaling
            ENDHLSL
        }
    }

    Fallback Off
}
