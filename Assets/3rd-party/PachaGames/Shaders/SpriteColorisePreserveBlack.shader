// SpriteColorise variant that preserves black outlines and dark details.
Shader "Sprites/SpriteColorise Preserve Black"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _BlackThreshold ("Black Protection Threshold", Range(0, 1)) = 0.18
        _ThresholdSoftness ("Threshold Softness", Range(0.001, 0.25)) = 0.04
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
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half _BlackThreshold;
                half _ThresholdSoftness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color;
                return output;
            }

            half3 RgbToHsl(half3 color)
            {
                half maximum = max(color.r, max(color.g, color.b));
                half minimum = min(color.r, min(color.g, color.b));
                half chroma = maximum - minimum;
                half lightness = (maximum + minimum) * 0.5h;
                half saturation = chroma / max(1.0h - abs(2.0h * lightness - 1.0h), 0.0001h);
                half hue = 0.0h;

                if (chroma > 0.0001h)
                {
                    if (maximum == color.r)
                        hue = frac((color.g - color.b) / chroma / 6.0h);
                    else if (maximum == color.g)
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

            half4 Frag(Varyings input) : SV_Target
            {
                half4 source = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half sourceLightness = dot(source.rgb, half3(0.299h, 0.587h, 0.114h));

                half3 coloriseHsl = RgbToHsl(input.color.rgb);
                coloriseHsl.z = sourceLightness;
                half3 colorised = HslToRgb(coloriseHsl);

                // Preserve SpriteColorise's original alpha behavior:
                // 0 = original, 0.5 = colorized, 1 = fully lightened.
                half3 effectColor = input.color.a <= 0.5h
                    ? lerp(source.rgb, colorised, input.color.a * 2.0h)
                    : lerp(colorised, half3(1, 1, 1), (input.color.a - 0.5h) * 2.0h);

                // Dark strokes remain unchanged. Softness protects their
                // antialiased edge pixels from abrupt color transitions.
                half coloriseMask = smoothstep(
                    _BlackThreshold - _ThresholdSoftness,
                    _BlackThreshold + _ThresholdSoftness,
                    sourceLightness
                );
                half3 outputColor = lerp(source.rgb, effectColor, coloriseMask);
                return half4(outputColor, source.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
