// Photoshop-style "Select Color Range" as a glow mask: pick _TargetColor (e.g. a crystal's
// pink), and every source texel whose hue falls within _HueRange of it (soft-edged over
// _HueFeather, and gated by _MinSaturation so grays/near-black never match) gets _GlowColor
// added on top. One texture sample, one RGB->HSL conversion per pixel - cheaper than the
// multi-tap edge glow in Sprite Inner Glow / Sprite Status Colorise Flash, since this doesn't
// need to find the silhouette edge. _TargetColor's HSL is computed once per vertex (it's a
// per-material constant, so interpolating it across the quad is exact) instead of every pixel.
//
// SpriteRenderer.color contract, same as Sprites/SpriteColor:
// RGB   = mix color
// Alpha = mix strength (0 = untouched sprite, 1 = fully replaced by RGB)
// Output is premultiplied (Blend One OneMinusSrcAlpha) for the same reason as SpriteColor: a
// straight-alpha blend also applies SrcAlpha to the alpha channel, so a transparent-background
// RenderTexture composited elsewhere (e.g. through a RawImage) gets destination alpha a*a
// instead of a - a bright halo on every antialiased edge texel.
Shader "Sprites/Sprite Selective Color Glow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}

        [Header(Target Color Range)]
        _TargetColor ("Target Color (color to select, e.g. the crystal's pink)", Color) = (1, 0.3, 0.85, 1)
        _HueRange ("Hue Range", Range(0, 0.5)) = 0.08
        _HueFeather ("Hue Feather", Range(0.001, 0.5)) = 0.05
        _MinSaturation ("Min Saturation (excludes grays/blacks)", Range(0, 1)) = 0.25
        _SaturationFeather ("Saturation Feather", Range(0.001, 1)) = 0.15

        [Header(Glow)]
        [HDR] _GlowColor ("Glow Color", Color) = (1, 0.3, 0.85, 1)
        _GlowIntensity ("Glow Intensity", Range(0, 8)) = 1
        _PulseSpeed ("Pulse Speed (0 = static)", Float) = 0
        _PulseAmount ("Pulse Amount", Range(0, 1)) = 0
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
        Blend One OneMinusSrcAlpha

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
                half4 _TargetColor;
                half _HueRange;
                half _HueFeather;
                half _MinSaturation;
                half _SaturationFeather;
                half4 _GlowColor;
                half _GlowIntensity;
                half _PulseSpeed;
                half _PulseAmount;
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
                half2 targetHueSat : TEXCOORD1;
            };

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

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color;

                // _TargetColor is constant for the whole draw call - converting it here instead
                // of per-pixel means every fragment only pays for one RgbToHsl (the sampled
                // texel's), not two.
                half3 targetHsl = RgbToHsl(_TargetColor.rgb);
                output.targetHueSat = targetHsl.xy;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half3 sourceHsl = RgbToHsl(texColor.rgb);

                // Hue is circular: 0.98 and 0.02 are only 0.04 apart, not 0.96.
                half hueDelta = abs(sourceHsl.x - input.targetHueSat.x);
                hueDelta = min(hueDelta, 1.0h - hueDelta);
                half hueMask = 1.0h - smoothstep(_HueRange, _HueRange + _HueFeather, hueDelta);

                // Grays and near-black have meaningless/noisy hue - never let them light up.
                half satMask = smoothstep(_MinSaturation - _SaturationFeather, _MinSaturation, sourceHsl.y);

                half mask = hueMask * satMask * texColor.a;

                half pulse = 1.0h + sin(_Time.y * _PulseSpeed) * _PulseAmount;
                half3 glow = _GlowColor.rgb * (mask * _GlowIntensity * pulse);

                // i.color.rgb is the mix color, i.color.a is the mix strength (see header).
                half3 tinted = lerp(texColor.rgb, input.color.rgb, input.color.a);

                // Sprite shape/opacity always comes from the texture, never from i.color.a;
                // premultiply here to match the Blend One OneMinusSrcAlpha above.
                half3 finalRgb = (tinted + glow) * texColor.a;
                return half4(finalRgb, texColor.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
