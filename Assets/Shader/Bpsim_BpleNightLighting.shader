Shader "Bpsim/URP/BpleNightLighting"
{
    Properties
    {
        _BpleNightVision ("Night vision", Float) = 0
        _BpleLightOrigin ("Light origin", Vector) = (0,0,0,0)
        _BpleLightInfo ("Radius / border / type / angle", Vector) = (1,.5,0,45)
        _BpleLightDirection ("Direction / cut", Vector) = (0,1,.25,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Overlay" }
        Cull Off ZWrite Off ZTest Always
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BpleSceneTexture);
        SAMPLER(sampler_BpleSceneTexture);
        CBUFFER_START(UnityPerMaterial)
            float _BpleNightVision;
            float4 _BpleLightOrigin;
            float4 _BpleLightInfo;
            float4 _BpleLightDirection;
        CBUFFER_END
        struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
        struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 world : TEXCOORD1; };
        Varyings FullscreenVertex(Attributes input)
        {
            Varyings output;
            output.positionCS = float4(input.positionOS.xy * 2, 0, 1);
            output.uv = input.uv;
            #if UNITY_UV_STARTS_AT_TOP
                output.uv.y = 1 - output.uv.y;
            #endif
            output.world = 0;
            return output;
        }
        Varyings LightVertex(Attributes input)
        {
            Varyings output;
            float3 world = TransformObjectToWorld(input.positionOS);
            output.positionCS = TransformWorldToHClip(world);
            output.uv = input.uv;
            output.world = world.xy;
            return output;
        }
        half3 NightColor(half3 color, float2 screenUV)
        {
            // BPLE NightVisionOverlay: SrcColor / OneMinusSrcAlpha, alpha=0.
            // It adds a vignetted green overlay; it does not turn the scene into grayscale.
            float v = saturate((length(screenUV - .5) - .6) / -.3);
            float vignette = .5 + .5 * v * v * (3 - 2 * v);
            half3 overlay = half3(.05,.4264706,0) * vignette;
            return saturate(color + overlay * overlay * _BpleNightVision);
        }
        half4 MaskFragment(Varyings input) : SV_Target
        {
            half4 color = SAMPLE_TEXTURE2D(_BpleSceneTexture, sampler_BpleSceneTexture, input.uv);
            half3 dark = half3(.04587758,.05882353,.04541522) * 2;
            half alpha = lerp(1,.5568628,_BpleNightVision);
            return half4(NightColor(lerp(color.rgb, dark, alpha), input.uv), color.a);
        }
        half4 LightFragment(Varyings input) : SV_Target
        {
            float2 delta = input.world - _BpleLightOrigin.xy;
            float radius = _BpleLightInfo.x;
            float border = max(.0001, _BpleLightInfo.y);
            float mask = 1 - smoothstep(radius, radius + border, length(delta));
            if (_BpleLightInfo.z > .5)
            {
                float forward = dot(delta, _BpleLightDirection.xy);
                float lateral = dot(delta, float2(_BpleLightDirection.y, -_BpleLightDirection.x));
                float width = max(0, forward) * tan(radians(_BpleLightInfo.w * .5));
                float cone = (1 - smoothstep(width, width + border, abs(lateral))) * step(_BpleLightDirection.z, forward);
                float baseLight = 1 - smoothstep(.5, 1, length(delta));
                mask = max(mask * cone, baseLight);
            }
            float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
            half4 color = SAMPLE_TEXTURE2D(_BpleSceneTexture, sampler_BpleSceneTexture, uv);
            return half4(NightColor(color.rgb, uv), mask);
        }
        ENDHLSL
        Pass
        {
            Blend One Zero
            HLSLPROGRAM
            #pragma vertex FullscreenVertex
            #pragma fragment MaskFragment
            ENDHLSL
        }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex LightVertex
            #pragma fragment LightFragment
            ENDHLSL
        }
    }
}
