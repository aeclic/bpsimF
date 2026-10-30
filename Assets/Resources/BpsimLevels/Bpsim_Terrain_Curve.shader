Shader "Bpsim/Terrain_Curve"
{
    // Port of the main project's Assets/Resources/curve.shader ("e2d/Curve") to URP.
    //
    // An e2dTerrain curve mesh is a two-vertex-wide strip laid along the terrain
    // silhouette: even vertices sit on the curve, odd vertices are the outer edge of
    // the stripe. Its UVs are (arc length along the curve, segment index) and its
    // vertex colour r is 1 on the curve vertices, 0 on the stripe vertices, so the
    // stripe samples the splat texture from v = 0 (outer edge) to v = 1 (surface).
    //
    // _Control is a 1-pixel-tall strip from the level binary; its green channel
    // selects, per segment, between splat 0 (usually grass) and splat 1 (usually the
    // rock outline). Everything below mirrors e2dTerrainCurveMesh.RebuildMaterial:
    //   _InvControlSize     = 1 / controlTexture.width
    //   _InvControlSizeHalf = 0.5 / controlTexture.width
    //   _SplatParamsN       = (1 / size.x, size.y, fixedAngle ? 1 : 0, fadeThreshold)
    Properties
    {
        _ControlSize ("Control Size", Float) = 1
        _InvControlSize ("Inv Control Size", Float) = 1
        _InvControlSizeHalf ("Half of Inv Control Size", Float) = 0.5
        _Control ("Control (RGBA)", 2D) = "red" {}
        _Splat0 ("Layer 0 (R)", 2D) = "white" {}
        _SplatParams0 ("Splat Params 0", Vector) = (1,1,0,0)
        _Splat1 ("Layer 1 (G)", 2D) = "white" {}
        _SplatParams1 ("Splat Params 1", Vector) = (1,1,0,0)
        _Color ("Main Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "QUEUE" = "Transparent"
            "RenderType" = "Transparent"
            "IGNOREPROJECTOR" = "True"
            "PreviewType" = "Plane"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Control);
            SAMPLER(sampler_Control);
            TEXTURE2D(_Splat0);
            SAMPLER(sampler_Splat0);
            TEXTURE2D(_Splat1);
            SAMPLER(sampler_Splat1);

            CBUFFER_START(UnityPerMaterial)
                float _ControlSize;
                float _InvControlSize;
                float _InvControlSizeHalf;
                float4 _SplatParams0;
                float4 _SplatParams1;
                float4 _Color;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 controlUV  : TEXCOORD0;
                float2 splatUV    : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.controlUV  = float2(input.uv.y * _InvControlSize + _InvControlSizeHalf, 0.0);
                output.splatUV    = float2(input.uv.x * _SplatParams0.x, input.color.r);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 upper = SAMPLE_TEXTURE2D(_Splat1, sampler_Splat1, input.splatUV);
                half pick = floor(SAMPLE_TEXTURE2D(_Control, sampler_Control, input.controlUV).y);
                half4 lower = SAMPLE_TEXTURE2D(_Splat0, sampler_Splat0, input.splatUV);
                lower.rgb += (upper.rgb - lower.rgb) * pick;
                return lower * (half4)_Color;
            }
            ENDHLSL
        }
    }

    SubShader
    {
        Tags { "QUEUE" = "Transparent" "RenderType" = "Transparent" "IGNOREPROJECTOR" = "True" }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex    : SV_POSITION;
                float2 controlUV : TEXCOORD0;
                float2 splatUV   : TEXCOORD1;
            };

            float _InvControlSize;
            float _InvControlSizeHalf;
            float4 _SplatParams0;
            sampler2D _Control;
            sampler2D _Splat0;
            sampler2D _Splat1;
            fixed4 _Color;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex    = UnityObjectToClipPos(IN.vertex);
                OUT.controlUV = float2(IN.texcoord.y * _InvControlSize + _InvControlSizeHalf, 0.0);
                OUT.splatUV   = float2(IN.texcoord.x * _SplatParams0.x, IN.color.r);
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 upper = tex2D(_Splat1, IN.splatUV);
                half pick = floor(tex2D(_Control, IN.controlUV).y);
                fixed4 lower = tex2D(_Splat0, IN.splatUV);
                lower.rgb += (upper.rgb - lower.rgb) * pick;
                return lower * _Color;
            }
            ENDCG
        }
    }

    Fallback Off
}
