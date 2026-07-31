Shader "Sprites/SpriteColorise"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
                half4 color : COLOR;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                half4 color : COLOR;
            };

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = TransformObjectToHClip(v.vertex.xyz);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color;
                return o;
            }

            half3 RgbToHsl(half3 color)
            {
                half maxChannel = max(color.r, max(color.g, color.b));
                half minChannel = min(color.r, min(color.g, color.b));
                half chroma = maxChannel - minChannel;
                half lightness = (maxChannel + minChannel) * 0.5h;
                half saturation = chroma / max(1.0h - abs(2.0h * lightness - 1.0h), 0.0001h);
                half hue = 0.0h;

                if (chroma > 0.0001h)
                {
                    if (maxChannel == color.r)
                        hue = frac((color.g - color.b) / chroma / 6.0h);
                    else if (maxChannel == color.g)
                        hue = ((color.b - color.r) / chroma + 2.0h) / 6.0h;
                    else
                        hue = ((color.r - color.g) / chroma + 4.0h) / 6.0h;
                }

                return half3(hue, saturation, lightness);
            }

            half HueToRgb(half p, half q, half t)
            {
                t = frac(t);
                if (t < 1.0h / 6.0h) return p + (q - p) * 6.0h * t;
                if (t < 1.0h / 2.0h) return q;
                if (t < 2.0h / 3.0h) return p + (q - p) * (2.0h / 3.0h - t) * 6.0h;
                return p;
            }

            half3 HslToRgb(half3 hsl)
            {
                if (hsl.y < 0.0001h)
                    return hsl.zzz;

                half q = hsl.z < 0.5h
                    ? hsl.z * (1.0h + hsl.y)
                    : hsl.z + hsl.y - hsl.z * hsl.y;
                half p = 2.0h * hsl.z - q;

                return half3(
                    HueToRgb(p, q, hsl.x + 1.0h / 3.0h),
                    HueToRgb(p, q, hsl.x),
                    HueToRgb(p, q, hsl.x - 1.0h / 3.0h)
                );
            }

            half4 frag(v2f i) : SV_Target
            {
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);

                // Preserve the source sprite's tonal detail, regardless of its hue.
                half sourceLightness = dot(texColor.rgb, half3(0.299h, 0.587h, 0.114h));

                // SpriteRenderer.color.rgb supplies the Colorize hue and saturation.
                half3 coloriseHsl = RgbToHsl(i.color.rgb);
                coloriseHsl.z = sourceLightness;
                half3 colorised = HslToRgb(coloriseHsl);

                // SpriteRenderer.color.a moves through four intensity levels:
                // 0 = original, 1/3 = tonal colorize, 2/3 = solid-color overlay, 1 = white.
                half strength = saturate(i.color.a);
                half3 outputColor;

                if (strength <= 1.0h / 3.0h)
                {
                    outputColor = lerp(texColor.rgb, colorised, strength * 3.0h);
                }
                else if (strength <= 2.0h / 3.0h)
                {
                    outputColor = lerp(colorised, i.color.rgb, (strength - 1.0h / 3.0h) * 3.0h);
                }
                else
                {
                    outputColor = lerp(i.color.rgb, half3(1.0h, 1.0h, 1.0h), (strength - 2.0h / 3.0h) * 3.0h);
                }

                return half4(outputColor, texColor.a);
            }
            ENDHLSL
        }
    }
}
