// "Encased in ice" look for ANY sprite - procedural, no extra textures or per-enemy sprites.
// Swapped onto every SpriteRenderer of an entity by HitFeedback (hardFreezeMaterial /
// freezeMaterial) while the status is active.
//
// SpriteRenderer.color contract (same as Sprites/SpriteColor, the enemies' base material, so
// HitFeedback's flashes look identical frozen or not):
// RGB   = mix colour (white for a damage flash, orange for Burn, ...)
// Alpha = mix strength - 0 = the ice look untouched, 1 = fully replaced by RGB.
// HitFeedback suppresses its rest tints (frozenTint/elemental, authored at a=1) while this material
// is showing, so at rest alpha is 0.
//
// Layers, cheapest first:
//  1. Ice ramp      - source luminance remapped onto Deep -> Mid -> Light, so the enemy's own
//                     shading/detail survives while its colours turn to ice.
//  2. Facets        - two overlapping triangle lattices in TEXEL space (sticks to the sprite through
//                     flips/squash/batching, uniform size across atlas sprites); per-triangle hash
//                     shades each facet like a cut crystal. No loops, no neighbour search.
//  3. Cracks        - thin bright lines on the big lattice's triangle edges.
//  4. Frost rim     - 8-tap inner-edge detect (half the taps of the old inner glow), broken up by the
//                     facet hash so it reads as crusted frost rather than a clean outline.
//  5. Glint sweep   - narrow band in WORLD space, so it travels coherently across every part of a
//                     multi-sprite rig instead of each part sweeping on its own.
//  6. Sparkles      - a few facet centroids twinkle (triangle-wave, no per-pixel sin).
// All hashes are sin-free (Hoskins hash12).
Shader "Sprites/Sprite Frozen"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}

        [Header(Ice Ramp)]
        _IceDeep ("Deep (shadows / ink)", Color) = (0.02, 0.05, 0.17, 1)
        _IceMid ("Mid", Color) = (0.3, 0.66, 0.98, 1)
        _IceLight ("Light (highlights)", Color) = (0.88, 0.97, 1, 1)
        _IceAmount ("Ice Amount (0 = original colours)", Range(0, 1)) = 0.88
        _InkThreshold ("Ink Threshold (luminance kept as outline)", Range(0, 0.5)) = 0.07
        _LumaContrast ("Luminance Contrast", Range(0.5, 2)) = 1.15
        _BodyLift ("Body Lift (dark bodies still read as ice)", Range(0, 0.8)) = 0.38

        [Header(Facets)]
        _FacetSize ("Facet Size (texels)", Float) = 170
        _FacetContrast ("Facet Contrast", Range(0, 1)) = 0.35

        [Header(Cracks)]
        [HDR] _CrackColor ("Crack Color", Color) = (0.85, 0.97, 1, 1)
        _CrackWidth ("Crack Width (lattice units)", Range(0.001, 0.1)) = 0.018
        _CrackAmount ("Crack Amount", Range(0, 1)) = 0.45

        [Header(Frost Rim)]
        [HDR] _RimColor ("Rim Color", Color) = (0.92, 0.99, 1, 1)
        _RimWidth ("Rim Width (texels)", Float) = 22
        _RimIntensity ("Rim Intensity", Range(0, 2)) = 0.85

        [Header(Glint Sweep)]
        [HDR] _GlintColor ("Glint Color", Color) = (1.4, 1.5, 1.6, 1)
        _GlintPeriod ("Glint Period (world units)", Float) = 7
        _GlintSpeed ("Glint Speed (sweeps/sec)", Float) = 0.45
        _GlintWidth ("Glint Width (0-1 of period)", Range(0.01, 0.5)) = 0.07

        [Header(Sparkles)]
        [HDR] _SparkleColor ("Sparkle Color", Color) = (2, 2, 2, 1)
        _SparkleDensity ("Sparkle Density", Range(0, 1)) = 0.12
        _SparkleSize ("Sparkle Size", Range(0.05, 0.6)) = 0.22
        _SparkleSpeed ("Sparkle Speed", Float) = 0.6
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
                half4 _IceDeep;
                half4 _IceMid;
                half4 _IceLight;
                half _IceAmount;
                half _InkThreshold;
                half _LumaContrast;
                half _BodyLift;
                float _FacetSize;
                half _FacetContrast;
                half4 _CrackColor;
                half _CrackWidth;
                half _CrackAmount;
                half4 _RimColor;
                half _RimWidth;
                half _RimIntensity;
                half4 _GlintColor;
                float _GlintPeriod;
                float _GlintSpeed;
                half _GlintWidth;
                half4 _SparkleColor;
                half _SparkleDensity;
                half _SparkleSize;
                float _SparkleSpeed;
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
                // xy = big lattice coords, zw = small (rotated) lattice coords - linear in uv, so
                // computed per vertex instead of per pixel.
                float4 lattice : TEXCOORD1;
                // x = glint phase (linear in world position + time, so also per vertex), y = sparkle time
                float2 timing : TEXCOORD2;
                half4 color : COLOR;
            };

            // Skews a 2D point onto an equilateral triangle lattice (cell = 2 triangles).
            float2 ToLattice(float2 p)
            {
                return float2(p.x - p.y * 0.57735, p.y * 1.1547);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color;

                float2 texel = output.uv * _MainTex_TexelSize.zw / max(_FacetSize, 1.0);
                // Second lattice ~2.1x smaller, rotated ~37deg and offset, so the two grids never
                // line up and the result reads as irregular crystal instead of a tiled pattern.
                float2 rotated = float2(texel.x * 0.8 - texel.y * 0.6, texel.x * 0.6 + texel.y * 0.8) * 2.13 + 17.3;
                output.lattice = float4(ToLattice(texel), ToLattice(rotated));

                // Diagonal across the screen for the 45deg gameplay camera (x + y + z covers both
                // upright billboards and flat-on-ground sprites).
                float sweep = (positionWS.x + positionWS.y * 0.7 + positionWS.z * 0.7) / max(_GlintPeriod, 0.01);
                output.timing = float2(sweep - _Time.y * _GlintSpeed, _Time.y * _SparkleSpeed);
                return output;
            }

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            // Returns: x = per-triangle hash, y = distance to the nearest triangle edge,
            // zw = offset from the triangle's centroid (lattice units).
            float4 Triangle(float2 q)
            {
                float2 cell = floor(q);
                float2 f = q - cell;
                float upper = step(f.x, f.y);
                float diagonal = abs(f.x - f.y);
                float edge = upper > 0.5
                    ? min(min(f.x, 1.0 - f.y), diagonal)
                    : min(min(f.y, 1.0 - f.x), diagonal);
                float2 center = upper > 0.5 ? float2(0.3333, 0.6667) : float2(0.6667, 0.3333);
                return float4(Hash12(cell * 2.0 + upper), edge, f - center);
            }

            half3 IceRamp(half t)
            {
                half3 low = lerp(_IceDeep.rgb, _IceMid.rgb, saturate(t * 2.0h));
                return lerp(low, _IceLight.rgb, saturate(t * 2.0h - 1.0h));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 source = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half luma = dot(source.rgb, half3(0.299h, 0.587h, 0.114h));
                // The black ink outline is what keeps the silhouette readable - every additive
                // layer below is masked off it, and the ramp maps it to _IceDeep (near-black navy).
                half body = smoothstep(_InkThreshold * 0.5h, _InkThreshold, luma);
                luma = saturate((luma - 0.5h) * _LumaContrast + 0.5h);
                // Lift non-ink pixels into the mid/light part of the ramp - otherwise a dark-bodied
                // enemy just turns navy and reads as "darkened", not frozen.
                luma = lerp(luma * 0.5h, _BodyLift + luma * (1.0h - _BodyLift), body);

                float4 big = Triangle(input.lattice.xy);
                float4 small = Triangle(input.lattice.zw);

                // 2. Facets - each crystal face pushed lighter/darker before the ramp, so the
                // faceting lives in the ice colours instead of greying them out.
                half facet = (half)(big.x * 0.65 + small.x * 0.35) - 0.5h;
                half3 ice = IceRamp(saturate(luma + facet * _FacetContrast));
                half3 color = lerp(source.rgb, ice, _IceAmount);

                // 3. Cracks - only on roughly half the big edges (hash-gated) so it doesn't read as
                // a grid.
                half crack = 1.0h - smoothstep(0.0h, _CrackWidth, (half)big.y);
                crack *= step(0.45h, (half)small.x) * _CrackAmount * body;
                color = lerp(color, _CrackColor.rgb, crack);

                // 4. Frost rim - inner edge, two distances (cardinal full, diagonal ~half) for a
                // soft-ish falloff with only 8 taps.
                float2 o = _MainTex_TexelSize.xy * _RimWidth;
                float2 d = o * 0.5;
                half rimHit = 0;
                rimHit += 1.0h - SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + float2(o.x, 0)).a;
                rimHit += 1.0h - SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv - float2(o.x, 0)).a;
                rimHit += 1.0h - SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + float2(0, o.y)).a;
                rimHit += 1.0h - SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv - float2(0, o.y)).a;
                rimHit += 2.0h * (1.0h - SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + d).a);
                rimHit += 2.0h * (1.0h - SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv - d).a);
                rimHit += 2.0h * (1.0h - SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + float2(d.x, -d.y)).a);
                rimHit += 2.0h * (1.0h - SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + float2(-d.x, d.y)).a);
                half rim = saturate(rimHit * 0.25h) * (0.55h + 0.9h * (half)small.x) * body;
                color = lerp(color, _RimColor.rgb, saturate(rim * _RimIntensity));

                // 5. Glint sweep - brighter on "upward-facing" facets so the band breaks into
                // crystal flashes instead of a flat stripe.
                half sweep = (half)abs(frac(input.timing.x) - 0.5) * 2.0h;
                half glint = saturate(1.0h - (1.0h - sweep) / _GlintWidth);
                glint *= glint * (0.4h + 1.2h * (half)big.x) * body;
                color += _GlintColor.rgb * glint;

                // 6. Sparkles - a hash-picked subset of small facets pulse at their centroid.
                half sparkleCell = step(1.0h - _SparkleDensity, (half)Hash12(small.xx * 91.7 + 3.1));
                half pulse = (half)abs(frac(input.timing.y + small.x * 7.0) * 2.0 - 1.0);
                pulse = pow(pulse, 6.0h);
                half star = saturate(1.0h - (half)length(small.zw) / _SparkleSize);
                // Cross-shaped falloff - axis-aligned arms make it read as a twinkle, not a dot.
                half arms = saturate(1.0h - (half)min(abs(small.z), abs(small.w)) * 18.0h) * star;
                half sparkle = (star * star * star + arms * 0.6h) * pulse * sparkleCell * body;
                color += _SparkleColor.rgb * sparkle;

                // Hit feedback always wins - same mix rule as Sprites/SpriteColor.
                color = lerp(color, input.color.rgb, saturate(input.color.a));
                return half4(color, source.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
