Shader "UI/ScrollingCutout"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _ScrollSpeedX ("Scroll Speed X", Range(-5, 5)) = 0.5
        _ScrollSpeedY ("Scroll Speed Y", Range(-5, 5)) = 0.0
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="TransparentCutout" }
        Lighting Off
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _ScrollSpeedX;
            float _ScrollSpeedY;
            float _Cutoff;
            float _TimeY;

            v2f vert (appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 scrollUV = i.uv + float2(_ScrollSpeedX, _ScrollSpeedY) * _Time.y;
                fixed4 col = tex2D(_MainTex, scrollUV) * i.color;

                clip(col.a - _Cutoff); // cutout logic
                return col;
            }
            ENDCG
        }
    }
}
