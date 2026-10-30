Shader "Hidden/OcclusionDebugComposite"
{
    // Dead asset: nothing references this shader - see Tools/BPSim/BPSIM_EDITOR_MIGRATION.md.
    // The properties (_Depth, _Overlay) come from both the decompiler stub and the shipping player
    // build's parsed shader form, which also shows _OnlyDepth / _OnlyOverlay / _YFlip uniforms.
    // It is a fullscreen composite of a depth view and an overlay view; the exact blend is not
    // recoverable, so this is the self-consistent version those uniforms imply.
    Properties
    {
        _Depth ("Depth", 2D) = "white" {}
        _Overlay ("Overlay", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "OcclusionDebugComposite"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            // bpsim-main: this shader used UNITY_VERTEX_INPUT_INSTANCE_ID / UNITY_VERTEX_OUTPUT_STEREO /
            // TEXTURE2D / SAMPLE_TEXTURE2D / TransformObjectToHClip without including the SRP shader
            // library, so a d3d11 player build failed with "unrecognized identifier". Core.hlsl provides
            // all of them.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Depth);
            SAMPLER(sampler_Depth);
            TEXTURE2D(_Overlay);
            SAMPLER(sampler_Overlay);
            float _OnlyDepth;
            float _OnlyOverlay;
            float _YFlip;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                if (_YFlip != 0.0)
                {
                    uv.y = 1.0 - uv.y;
                }

                half4 depth   = SAMPLE_TEXTURE2D(_Depth, sampler_Depth, uv);
                half4 overlay = SAMPLE_TEXTURE2D(_Overlay, sampler_Overlay, uv);

                if (_OnlyDepth != 0.0)   return depth;
                if (_OnlyOverlay != 0.0) return overlay;
                return half4(depth.rgb + overlay.rgb, max(depth.a, overlay.a));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
