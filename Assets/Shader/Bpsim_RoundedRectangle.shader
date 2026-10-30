Shader "Bpsim/RoundedRectangle"
{
    // This shader was MISSING from the decompilation entirely: Bpsim/UI/RoundedRectangle.cs:109
    // and :117 do `new Material(Shader.Find("Bpsim/RoundedRectangle"))` and set _Width / _Height /
    // _Radius (:75-77, :122-124), but no shader asset by that name existed, so rounded UI panels
    // fell back to Unity's error shader.
    //
    // Written as a uGUI surface shader: it keeps the standard UI/Default stencil, colour-mask and
    // RectMask2D properties so it still behaves correctly inside masks, and adds a signed
    // distance field for a rounded rectangle keyed off the local vertex position.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _Width ("Width", Float) = 100
        _Height ("Height", Float) = 100
        _Radius ("Corner Radius", Range(0, 512)) = 8

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "RoundedRectangle"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float2 localPosition : TEXCOORD1;
                float4 worldPosition : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float4 _ClipRect;
            float _Width;
            float _Height;
            float _Radius;

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex        = UnityObjectToClipPos(v.vertex);
                o.texcoord      = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.localPosition = v.vertex.xy;
                o.color         = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 color = tex2D(_MainTex, i.texcoord) * i.color;

                // Signed distance to a rounded rectangle centred on the rect's pivot.
                float2 halfSize = 0.5 * float2(_Width, _Height);
                float  radius   = min(_Radius, min(halfSize.x, halfSize.y));
                float2 d        = abs(i.localPosition) - (halfSize - radius);
                float  sd       = length(max(d, 0.0)) + min(max(d.x, d.y), 0.0) - radius;
                color.a *= saturate(0.5 - sd);

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
