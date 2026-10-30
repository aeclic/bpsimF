Shader "Bpsim/Unlit_Texture"
{
    // Reconstructed. Evidence:
    //  * Name/file/GUID from the decompiler stub; the two properties and their display names
    //    come from the decompiler stub and are confirmed by the shipping player build's parsed
    //    shader form (_MainTex "Base (RGB) Trans (A)", _Color "Main Color").
    //  * No LightMode tag in the shipped tags ("IGNOREPROJECTOR"/"QUEUE"/"RenderType" only), so
    //    this is drawn as SRPDefaultUnlit - which the project's URP 2D renderer accepts.
    //  * Used for the pre-rendered part atlas: Part_Rendered_SpriteMap_0.txt's header is
    //    "Part_Rendered_Texture_0, 4096, 2048, 0.4" (material, atlas w, h, scale) and the texture
    //    is fetched as ResolveMaterial(name).mainTexture.  Part_Rendered_Texture_0.mat is the only
    //    material using it.
    //  * Body is the trivial unlit sprite fetch - tex * _Color - which is what the stub's
    //    fragment already emitted.
    Properties
    {
        _MainTex ("Base (RGB) Trans (A)", 2D) = "white" {}
        _Color ("Main Color", Vector) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "QUEUE" = "Transparent"
            "RenderType" = "Transparent"
            "IGNOREPROJECTOR" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
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
            #pragma multi_compile_instancing

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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv         = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                output.color      = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                return tex * (half4)input.color * (half4)_Color;
            }
            ENDHLSL
        }
    }

    // Legacy CG fallback for pipelines that do not understand the URP tags above, mirroring the
    // "+INSTANCING_ON" keyword set the shipped shader carried.
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
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.vertex   = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = TRANSFORM_TEX(IN.texcoord, _MainTex);
                OUT.color    = IN.color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                return tex2D(_MainTex, IN.texcoord) * IN.color * _Color;
            }
            ENDCG
        }
    }

    Fallback Off
}
