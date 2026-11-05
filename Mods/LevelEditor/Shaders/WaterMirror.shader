Shader "BpsimLevelEditor/WaterMirror"
{
    Properties { _MainTex ("Scene", 2D) = "black" {} _Horizon ("Horizon", Float) = .5 }
    SubShader { Cull Off ZWrite Off ZTest Always
        Pass { CGPROGRAM
        #pragma vertex vert_img
        #pragma fragment frag
        #include "UnityCG.cginc"
        sampler2D _MainTex; float _Horizon;
        fixed4 frag(v2f_img i):SV_Target
        {
            float2 uv=float2(i.uv.x,2*_Horizon-i.uv.y);
            if(uv.y<0 || uv.y>1) return 0;
            return tex2D(_MainTex,uv);
        }
        ENDCG }
    }
}
