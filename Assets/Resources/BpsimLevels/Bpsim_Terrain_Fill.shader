Shader "Bpsim/Terrain_Fill"
{
    // Opaque body of an imported BPLE terrain piece.
    //
    // The main project draws this with the fill mesh of e2dTerrain: fill UVs are
    // (vertex - tileOffset) / tileSize, so they run well outside [0,1] and rely on the
    // texture's Repeat wrap mode. The tint is the per-terrain Color stored in the level
    // binary (e2dTerrain renders with renderer.sharedMaterial.color = that colour).
    //
    // Kept separate from Bpsim/Unlit_Texture because that one is a transparent sprite
    // shader (ZWrite Off); terrain has to write depth or every prop would fight it.
    Properties
    {
        _MainTex ("Fill (RGB) Trans (A)", 2D) = "white" {}
        _Color ("Main Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "QUEUE" = "Geometry"
            "RenderType" = "Opaque"
            "IGNOREPROJECTOR" = "True"
            "PreviewType" = "Plane"
        }

        Cull Off
        Lighting Off
        ZWrite On
        ZTest LEqual

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
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
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv         = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                output.color      = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                return half4(tex.rgb * input.color.rgb * _Color.rgb, 1);
            }
            ENDHLSL
        }
    }

    SubShader
    {
        Tags { "QUEUE" = "Geometry" "RenderType" = "Opaque" "IGNOREPROJECTOR" = "True" }

        Cull Off
        Lighting Off
        ZWrite On

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
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex   = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = TRANSFORM_TEX(IN.texcoord, _MainTex);
                OUT.color    = IN.color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, IN.texcoord);
                return fixed4(tex.rgb * IN.color.rgb * _Color.rgb, 1);
            }
            ENDCG
        }
    }

    Fallback Off
}
