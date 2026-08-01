// SpriteRenderer.color contract:
// RGB white     = original sprite
// RGB non-white = colorize using that hue/saturation
// Alpha         = full-white hit flash strength
Shader "Sprites/Sprite Status Colorise Flash"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _WhiteTolerance ("White Detection Tolerance", Range(0.0001, 0.2)) = 0.01

        [Header(Material Colorize)]
        _ColorizeColor ("Colorize Color (baked per-material, unlike SpriteRenderer.color's per-instance status colorize above)", Color) = (1, 1, 1, 1)
        _ColorizeIntensity ("Colorize Intensity", Float) = 0

        [Header(Inner Glow)]
        [HDR] _GlowColor ("Glow Color (alpha = extra strength multiplier)", Color) = (1, 0.3, 0.85, 1)
        _GlowIntensity ("Glow Intensity", Float) = 0
        _GlowWidth ("Glow Width (texels)", Float) = 2
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
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _MainTex_TexelSize;
                half _WhiteTolerance;
                half4 _ColorizeColor;
                half _ColorizeIntensity;
                half4 _GlowColor;
                half _GlowIntensity;
                half _GlowWidth;
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

                // Preserve the source sprite's light/dark detail while replacing
                // hue and saturation with SpriteRenderer.color.rgb.
                half3 statusHsl = RgbToHsl(input.color.rgb);
                statusHsl.z = sourceLightness;
                half3 colorised = HslToRgb(statusHsl);

                // Treat near-white as the explicit "no status" value.
                half distanceFromWhite = max(
                    1.0h - input.color.r,
                    max(1.0h - input.color.g, 1.0h - input.color.b)
                );
                half statusEnabled = step(_WhiteTolerance, distanceFromWhite);
                half3 statusResult = lerp(source.rgb, colorised, statusEnabled);

                // Material colorize: same preserve-lightness hue replacement as the per-instance
                // status colorize above, but sourced from a material property instead of
                // SpriteRenderer.color - that channel is already spoken for by HitFeedback's hit-
                // flash (see the file header contract), so a persistent baked-in tint (e.g.
                // Freeze's icy colorize) needs its own knob that flashes can't stomp.
                half3 materialHsl = RgbToHsl(_ColorizeColor.rgb);
                materialHsl.z = sourceLightness;
                half3 materialColorised = HslToRgb(materialHsl);
                statusResult = lerp(statusResult, materialColorised, saturate(_ColorizeIntensity));

                // Inner glow: brightest just inside the silhouette edge, softly fading toward
                // the interior over _GlowWidth. Sprites have no usable normals for a fresnel rim
                // (see MobileParticleAlphaRim's header comment on why that shader needs mesh
                // curvature), so this detects the edge cheaply instead - sampling outward in
                // GLOW_STEPS increments per cardinal direction and taking the closest hit's
                // weight, so a pixel right at the edge reads near-full strength and one
                // _GlowWidth texels in reads ~0, instead of a single fixed-distance tap (which
                // reads as a hard ring rather than a soft gradient). Zero at _GlowIntensity's
                // default so existing materials on this shader render unchanged.
                #define GLOW_STEPS 4
                half glowMask = 0;
                [unroll]
                for (int i = 1; i <= GLOW_STEPS; i++)
                {
                    half2 offset = _MainTex_TexelSize.xy * _GlowWidth * (i / (half) GLOW_STEPS);
                    half weight = saturate(1.0h - (half) i / GLOW_STEPS);

                    half hit = 0;
                    hit += saturate(source.a - SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + half2(offset.x, 0)).a);
                    hit += saturate(source.a - SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv - half2(offset.x, 0)).a);
                    hit += saturate(source.a - SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + half2(0, offset.y)).a);
                    hit += saturate(source.a - SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv - half2(0, offset.y)).a);

                    glowMask = max(glowMask, saturate(hit) * weight);
                }
                glowMask *= source.a;
                half3 glowResult = lerp(statusResult, _GlowColor.rgb, saturate(glowMask * _GlowIntensity * _GlowColor.a));

                // Hit feedback always wins and can reach completely white.
                half3 finalColor = lerp(glowResult, half3(1, 1, 1), saturate(input.color.a));
                return half4(finalColor, source.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
