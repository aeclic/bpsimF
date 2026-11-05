Shader "Bpsim/EpoFactionTarget"
{
    // This is ONLY an invisible ECS-to-Renderer target. All outlining is done by EPO's asset shaders.
    Properties { _MainTex ("EPO alpha-cutout atlas", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="Universal2D" }
            ColorMask 0
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 Vert(float3 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS); }
            half4 Frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
