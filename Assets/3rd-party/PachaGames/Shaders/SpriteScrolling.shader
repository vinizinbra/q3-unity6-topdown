Shader "UI/ScrollingImage"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _ScrollSpeedX ("Scroll Speed X", Range(0, 10)) = 1
        _ScrollSpeedY ("Scroll Speed Y", Range(0, 10)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
        }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        Lighting Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ PIXELSNAP_ON

            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float2 uv       : TEXCOORD0;
                fixed4 color    : COLOR;

            };

            struct v2f
            {
                float2 uv       : TEXCOORD0;
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
            };

            sampler2D _MainTex;
            float _ScrollSpeedX;
            float _ScrollSpeedY;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.uv;
                uv.x -= _Time.y * _ScrollSpeedX;  // Scroll horizontally based on time
                uv.y -= _Time.y * _ScrollSpeedY;  // Scroll horizontally based on time

                fixed4 col = tex2D(_MainTex, uv);
                col *= IN.color;
                return col;
            }
            ENDCG
        }
    }
}
