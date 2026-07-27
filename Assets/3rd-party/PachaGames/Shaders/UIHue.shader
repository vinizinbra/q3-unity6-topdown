Shader "UI/HueShiftMatrixByAlpha"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;

            struct appdata_t {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
            };

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color;
                return o;
            }

            float3 HueShift(float3 color, float angle)
            {
                float s = sin(angle * 6.2831853); // angle ∈ [0,1] mapped to radians
                float c = cos(angle * 6.2831853);

                float3x3 hueRotation = float3x3(
                    0.299 + 0.701 * c + 0.168 * s, 0.587 - 0.587 * c + 0.330 * s, 0.114 - 0.114 * c - 0.497 * s,
                    0.299 - 0.299 * c - 0.328 * s, 0.587 + 0.413 * c + 0.035 * s, 0.114 - 0.114 * c + 0.292 * s,
                    0.299 - 0.3   * c + 1.25  * s, 0.587 - 0.588 * c - 1.05  * s, 0.114 + 0.886 * c - 0.203 * s
                );

                return mul(hueRotation, color);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                
                fixed4 texColor = tex2D(_MainTex, i.uv);
                float a = texColor.a;
                texColor *= i.color;
                float hueShiftAmount = i.color.a;
                float3 shiftedColor = HueShift(texColor.rgb, hueShiftAmount);

                return fixed4(shiftedColor, a);
            }
            ENDCG
        }
    }
}
