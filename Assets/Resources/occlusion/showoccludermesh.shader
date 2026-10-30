Shader "Shader Graphs/ShowOccluderMesh"
{
    // Dead asset, but the one occlusion shader a material exists for
    // (Assets/Resources/occlusion/Material_ShowOccluderMesh.mat).  Nothing loads that material -
    // see Tools/BPSim/BPSIM_EDITOR_MIGRATION.md.
    //
    // IMPORTANT: this file was a Shader Graph asset, but it must NOT be regenerated as a
    // .shadergraph - that would be a different asset GUID and would break the material's m_Shader
    // reference.  Hand-written HLSL keeps the GUID.
    //
    // Deliberately minimal: a plain built-in CG unlit pass.  No URP includes and no
    // Shader-Graph-only property types (2DArray), because those are what stopped the first
    // attempt from importing - the asset came in with an empty shader name.  The graph body is
    // not recoverable anyway; what matters is that the asset resolves so the material's shader
    // reference does not fall back to the error shader.
    Properties
    {
        [HideInInspector] _QueueOffset ("_QueueOffset", Float) = 0
        [HideInInspector] _QueueControl ("_QueueControl", Float) = -1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "QUEUE" = "Geometry"
        }

        Cull Back
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
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
            };

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.vertex = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return fixed4(1.0, 1.0, 1.0, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
