Shader "Hidden/OcclusionDebugOccluders"
{
    // Dead asset: nothing references this shader - see Tools/BPSim/BPSIM_EDITOR_MIGRATION.md.
    // The shipping player build's parsed form shows no properties and no uniforms, but vertex
    // inputs POSITION + COLOR and a fragment TEXCOORD carrying that colour, i.e. a vertex-coloured
    // debug draw of occluder geometry.  Debug-overlay render state.
    Properties
    {
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "OcclusionDebugOccluders"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            // bpsim-main: this shader used UNITY_VERTEX_INPUT_INSTANCE_ID / UNITY_VERTEX_OUTPUT_STEREO /
            // TEXTURE2D / SAMPLE_TEXTURE2D / TransformObjectToHClip without including the SRP shader
            // library, so a d3d11 player build failed with "unrecognized identifier". Core.hlsl provides
            // all of them.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return (half4)input.color;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
