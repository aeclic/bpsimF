Shader "Hidden/BpsimLevelEditor/TerrainMask"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 Vert(float3 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS); }
            half4 Frag() : SV_Target { return half4(1,1,1,1); }
            ENDHLSL
        }
    }
}
