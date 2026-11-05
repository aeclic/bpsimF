Shader "BpsimLevelEditor/InteractiveWater2D"
{
    Properties
    {
        _WaterColor ("Ultimate Water Color", Color) = (0, 0.6634375, 0.9921568, 1)
        _BaseFarColor ("Base Far Color", Color) = (0, 0.8067973, 1, 1)
        _UnderwaterTopColor ("Underwater Top Color", Color) = (0, 1, 0.7019608, 1)
        _UnderwaterBottomColor ("Underwater Bottom Color", Color) = (0, 0.1927059, 1, 1)
        _SceneTex ("Scene Capture", 2D) = "black" {}
        _TerrainTex ("Terrain Mask", 2D) = "black" {}
        _NormalTexture ("WaterNormals_01", 2D) = "gray" {}
        _FoamTexture ("Foam_01", 2D) = "black" {}
        _CausticTexture ("Foam_02", 2D) = "black" {}
        _SurfaceFoamTexture ("Foam_03", 2D) = "black" {}
        _HasCapture ("Has Scene Capture", Float) = 0
        _Opacity ("Water Opacity", Range(0, 1)) = 1
        _NormalStrength ("Normal Strength", Float) = 0.251
        _Reflectivity ("Reflectivity", Float) = 0.663
        _GradientHeight ("Gradient Height", Float) = 9.44
        _GradientFade ("Gradient Fade", Float) = 0.16
        _WaterBounds ("Water Bounds", Vector) = (0, 0, 1, 1)
        _CaptureRect ("Capture World Rect", Vector) = (0, 0, 1, 1)
        _CaptureTexelSize ("Capture Texel Size", Vector) = (0.001, 0.001, 1024, 1024)
        _Effects ("Refraction Caustics Reflection Foam", Vector) = (0.4, 0.35, 0.663, 1)
        _Animation ("Time Flow Scale", Vector) = (0, 0, 0, 1)
        _NormalsTiling ("Normal Tiling", Vector) = (6.9, 32.27, 0, 0)
        _NormalTextureScale ("Original Normal Texture Scale", Vector) = (1, -0.98, 0, 0)
        _WaterSpeed ("Normal Scroll Speed", Vector) = (0.04, 0, 0, 0)
        _FoamTiling ("Foam Tiling", Vector) = (1.23, 2.96, 0, 0)
        _FoamScrollSpeed ("Foam Scroll Speed", Vector) = (0.04, 0, 0, 0)
        _CausticTiling ("Caustic Tiling", Vector) = (3, 2, 0, 0)
        _FoamStyle ("Foam Material", Vector) = (5.35, 1.83, 0.86, 0.598)
        _UnderwaterStyle ("Underwater Material", Vector) = (40.64, 0.13, 0, 0)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent+80" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Tags { "LightMode" = "Universal2D" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_SceneTex); SAMPLER(sampler_SceneTex);
            TEXTURE2D(_TerrainTex); SAMPLER(sampler_TerrainTex);
            TEXTURE2D(_NormalTexture); SAMPLER(sampler_NormalTexture);
            TEXTURE2D(_FoamTexture); SAMPLER(sampler_FoamTexture);
            TEXTURE2D(_CausticTexture); SAMPLER(sampler_CausticTexture);
            TEXTURE2D(_SurfaceFoamTexture); SAMPLER(sampler_SurfaceFoamTexture);

            CBUFFER_START(UnityPerMaterial)
                float4 _WaterColor, _BaseFarColor, _UnderwaterTopColor, _UnderwaterBottomColor;
                float4 _WaterBounds, _CaptureRect, _CaptureTexelSize, _Effects, _Animation;
                float4 _NormalsTiling, _NormalTextureScale, _WaterSpeed, _FoamTiling, _FoamScrollSpeed, _CausticTiling;
                float4 _FoamStyle, _UnderwaterStyle;
                float _HasCapture, _Opacity, _NormalStrength, _Reflectivity, _GradientHeight, _GradientFade;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 surface : TEXCOORD1;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 world : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float2 surface : TEXCOORD2;
                float4 color : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.world = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.world);
                output.uv = input.uv;
                output.surface = input.surface;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 captureUv = (input.world.xy - _CaptureRect.xy) / max(_CaptureRect.zw, 0.001);
                float terrain = _HasCapture > 0.5
                    ? SAMPLE_TEXTURE2D(_TerrainTex, sampler_TerrainTex, saturate(captureUv)).r : 0.0;
                clip(0.5 - terrain);

                float time = _Animation.x;
                float depth = max(0.0, input.surface.x - input.world.y);
                float gradient = max(_GradientHeight, 0.05);
                float depthFade = saturate(1.0 - exp(-depth / gradient));
                float deep = smoothstep(0.0, gradient * max(_GradientFade, 0.05), depth);

                float2 flow = _Animation.yz;
                float2 normalUv = input.uv * _NormalsTiling.xy * _NormalTextureScale.xy + time * _WaterSpeed.xy;
                normalUv += flow / max(_WaterBounds.zw, 0.001) * (time * 0.001);
                float2 normalA = SAMPLE_TEXTURE2D(_NormalTexture, sampler_NormalTexture, normalUv).ag * 2.0 - 1.0;
                float2 normalB = SAMPLE_TEXTURE2D(_NormalTexture, sampler_NormalTexture, normalUv + float2(0.5, 0.0)).ag * 2.0 - 1.0;
                float2 normal = (normalA - normalB) * 0.5 * _NormalStrength;
                float slope = input.color.r * 2.0 - 1.0;
                normal += float2(slope * 0.6, -abs(slope) * 0.18) * _NormalStrength;
                normal = clamp(normal, -0.8, 0.8);

                float3 scene = _HasCapture > 0.5
                    ? SAMPLE_TEXTURE2D(_SceneTex, sampler_SceneTex, saturate(captureUv)).rgb : _WaterColor.rgb;
                float2 refractUv = captureUv + normal * _Effects.x / max(_CaptureRect.zw, 0.001);
                float3 refracted = _HasCapture > 0.5
                    ? SAMPLE_TEXTURE2D(_SceneTex, sampler_SceneTex, saturate(refractUv)).rgb : scene;
                float3 transmitted = lerp(scene, refracted, saturate(_Effects.x * 1.5));
                float3 waterColor = lerp(_WaterColor.rgb, _BaseFarColor.rgb, deep);
                waterColor = lerp(waterColor, _UnderwaterBottomColor.rgb, saturate(depthFade * 0.45));
                float3 color = lerp(transmitted, waterColor, depthFade);

                float mirrorY = (2.0 * input.surface.x - input.world.y - _CaptureRect.y) / max(_CaptureRect.w, 0.001);
                float2 mirrorUv = float2(captureUv.x, mirrorY) + normal * _Effects.x * 0.5 / max(_CaptureRect.zw, 0.001);
                float mirrorOnscreen = step(0.0, mirrorUv.y) * step(mirrorUv.y, 1.0);
                float3 reflection = SAMPLE_TEXTURE2D(_SceneTex, sampler_SceneTex, saturate(mirrorUv)).rgb;
                float normalZ = sqrt(max(1.0 - dot(normal, normal), 0.05));
                float fresnel = _Reflectivity * _Effects.z * (0.3 + 0.7 * pow(1.0 - normalZ, 2.0));
                fresnel *= mirrorOnscreen * exp(-depth / max(gradient * 0.24, 0.05));
                color = lerp(color, reflection * float3(0.82, 0.96, 1.0), saturate(fresnel));

                float2 causticUv = input.uv * _CausticTiling.xy + float2(time * 0.035, -time * 0.027);
                causticUv += normal * 0.04;
                float causticA = SAMPLE_TEXTURE2D(_CausticTexture, sampler_CausticTexture, causticUv).r;
                float causticB = SAMPLE_TEXTURE2D(_CausticTexture, sampler_CausticTexture,
                    causticUv * 1.37 + float2(0.43, 0.19) - time * 0.018).r;
                float caustic = pow(saturate(causticA + causticB * 0.55), 2.2);
                caustic *= (1.0 - deep) * saturate(depth * 4.0);
                color += caustic * float3(0.55, 0.92, 1.0) * _Effects.y * 0.52;

                float2 foamUv = input.uv * _FoamTiling.xy + time * _FoamScrollSpeed.xy + normal * 0.04;
                float foamA = SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, foamUv).r;
                float foamB = SAMPLE_TEXTURE2D(_SurfaceFoamTexture, sampler_SurfaceFoamTexture,
                    foamUv * float2(0.73, 1.31) + float2(0.37, 0.61) - time * 0.012).r;
                float foamTexture = saturate(max(foamA, foamB * _FoamStyle.w));
                float2 texel = max(_CaptureTexelSize.xy, 0.00001);
                float shore = 0.0;
                if (_HasCapture > 0.5)
                {
                    shore = max(shore, SAMPLE_TEXTURE2D(_TerrainTex, sampler_TerrainTex, saturate(captureUv + float2(texel.x * 2.0, 0))).r);
                    shore = max(shore, SAMPLE_TEXTURE2D(_TerrainTex, sampler_TerrainTex, saturate(captureUv - float2(texel.x * 2.0, 0))).r);
                    shore = max(shore, SAMPLE_TEXTURE2D(_TerrainTex, sampler_TerrainTex, saturate(captureUv + float2(0, texel.y * 2.0))).r);
                    shore = max(shore, SAMPLE_TEXTURE2D(_TerrainTex, sampler_TerrainTex, saturate(captureUv - float2(0, texel.y * 2.0))).r);
                }
                float waterline = 1.0 - smoothstep(0.0, max(0.015, _Animation.w * 0.012), depth);
                float crest = saturate(abs(slope) * 1.8 + input.color.g);
                float foam = saturate((shore * 0.72 + waterline * foamTexture * 0.2 + crest * foamTexture * 0.28) * _Effects.w);
                foam = pow(foam, 1.0 / max(_FoamStyle.x, 1.0));
                color = lerp(color, float3(0.88, 0.98, 1.0), saturate(foam * _FoamStyle.y * 0.38));

                color = lerp(scene, color, saturate(_Opacity));
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
