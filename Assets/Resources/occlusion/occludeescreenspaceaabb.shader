Shader "Hidden/OccludeeScreenSpaceAABB"
{
    // Dead asset: nothing in the decompiled Assembly-CSharp (or any plugin) references this shader
    // or a material using it - see Tools/BPSim/BPSIM_EDITOR_MIGRATION.md.  Reconstructed from the
    // shipping player build's parsed shader form, which shows no material properties, the
    // STEREO_* keyword set, a "ForwardOnly" pass tag, and _Transform (matrix) / _YFlip (float)
    // uniforms.  It projects occludee geometry so a screen-space AABB can be measured; the exact
    // render-target encoding is not recoverable, so the fragment emits the normalised clip-space
    // rectangle, which is self-consistent and never exercised by the game.
    Properties
    {
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 200

        Pass
        {
            Name "OccludeeAABB"
            Tags { "LightMode" = "ForwardOnly" }

            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            // bpsim-main: this shader used UNITY_VERTEX_INPUT_INSTANCE_ID / UNITY_VERTEX_OUTPUT_STEREO /
            // TEXTURE2D / SAMPLE_TEXTURE2D / TransformObjectToHClip without including the SRP shader
            // library, so a d3d11 player build failed with "unrecognized identifier". Core.hlsl provides
            // all of them.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4x4 _Transform;
            float _YFlip;

            struct Attributes
            {
                float3 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionCS = mul(_Transform, float4(input.positionOS, 1.0));
                if (_YFlip != 0.0)
                {
                    output.positionCS.y = -output.positionCS.y;
                }
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                // Normalised device coordinates of this occludee vertex.
                float2 ndc = input.positionCS.xy / max(abs(input.positionCS.w), 1e-5);
                return float4(ndc * 0.5 + 0.5, 0.0, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
