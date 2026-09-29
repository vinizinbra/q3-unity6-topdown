// Mobile version of RiftRaiders/Test/ToonTerrain - same look, same Properties (a ToonTerrain material can
// switch to this shader and keep every value), built for low-end GPUs where the full shader covering
// almost every pixel was the frame-rate limiter (see docs/performance.md).
//
// What makes it cheap - everything that is constant per face or linear in position moves to the vertex
// stage, and the fragment is left with texture reads + the pixel-width lines:
//  - Tiles are flat-shaded facets (one normal per face), so the toon main light, ambient, received-shadow
//    (none on mobile) and the hatch weights are EXACT per vertex. Lighting is a single multiplier:
//    color = albedo * light, with light = lerp(ShadowTint, 1, lit) * mainLightColor + SH * Ambient.
//  - Surface / wall / hatch UVs, the water-gradient ramp, the water-line distance and the raised tint are
//    linear in position (or constant per face) -> interpolated, not recomputed.
//  - One albedo read (surface OR wall, per pixel), one hatch read only on faces that can get ink, no shadow
//    keywords/variants at all.
// Deliberate simplifications vs ToonTerrain:
//  - no received shadows (the Mobile URP asset has shadows off anyway);
//  - on SMOOTH-normal props the toon light step and the hatch mapping are resolved per vertex (softer
//    terminator); tiles and flat props are identical;
//  - emissive props below Emission Min Y blend to the Off colour before shading instead of after.
Shader "RiftRaiders/Mobile/ToonTerrainMobile"
{
    Properties
    {
        [Header(Surface)]
        _SurfaceTex ("Surface Texture (GRAYSCALE: white = Light, black = Dark)", 2D) = "white" {}
        _SurfaceLightColor ("Surface Light Color", Color) = (0.72, 0.76, 0.29, 1)
        _SurfaceDarkColor ("Surface Dark Color", Color) = (0.6, 0.66, 0.22, 1)
        _SurfaceScale ("Surface World Size (m per tile)", Float) = 4

        [Header(Raised Surface (height tint))]
        _RaisedLightColor ("Raised Surface Light Color", Color) = (0.2, 0.55, 0.5, 1)
        _RaisedDarkColor ("Raised Surface Dark Color", Color) = (0.12, 0.35, 0.36, 1)
        _RaisedFromY ("Raised From World Y", Float) = 1.5
        _RaisedStrength ("Raised Tint Strength", Range(0, 1)) = 0

        [Header(Wall)]
        _WallTex ("Wall Texture (GRAYSCALE: white = Light, black = Dark)", 2D) = "white" {}
        _WallLightColor ("Wall Light Color", Color) = (0.39, 0.28, 0.49, 1)
        _WallDarkColor ("Wall Dark Color", Color) = (0.3, 0.21, 0.4, 1)
        _WallScale ("Wall World Size (m per tile)", Float) = 3

        [Header(Wall To Surface Fade)]
        _EdgeFadeStart ("Fade Start (0 = at the outline)", Range(0, 1)) = 0
        _EdgeFadeEnd ("Fade End (how far inward it fades)", Range(0, 1)) = 0.4
        _FadeColor ("Fade Color at the outline (A = strength)", Color) = (0.55, 0.42, 0.25, 1)

        [Header(Texture Outlines)]
        _OutlineColor ("Outline Color (A = opacity)", Color) = (0.17, 0.11, 0.08, 1)
        _PropLineWidth ("Prop Edge Lines Width (px)", Range(0, 6)) = 1.6
        _PropLineStrength ("Prop Edge Lines Strength", Range(0, 1)) = 1
        _StrataOutlineWidth ("Rock Ledge Lines Width (px)", Range(0, 8)) = 1.8
        _StrataOutlineStrength ("Rock Ledge Lines Strength", Range(0, 1)) = 0.85
        _RimOutlineWidth ("Cliff Top Edge Width (px)", Range(0, 10)) = 1.8
        _RimOutlineStrength ("Cliff Top Edge Strength", Range(0, 1)) = 0.6
        _BorderOutlineWidth ("Grass Border Width (px)", Range(0, 10)) = 1.5
        _BorderOutlineStrength ("Grass Border Strength", Range(0, 1)) = 0

        [Header(Water Depth Height Gradient)]
        _GradientBottomColor ("Bottom Color (unlit, at Start Y and below)", Color) = (0.45, 0.83, 0.99, 1)
        _GradientTopColor ("Top Tint (at Start Y + Distance)", Color) = (1, 1, 1, 1)
        _GradientStartY ("Start Y (fully Bottom Color)", Float) = -3
        _GradientDistance ("Distance (back to normal shading)", Float) = 3
        _GradientStrength ("Gradient Strength", Range(0, 1)) = 0

        [Header(Water Line On Walls)]
        _WallLineColor ("Line Color", Color) = (0.025, 0.02, 0.03, 1)
        _WallLineY ("World Y", Float) = 0
        _WallLineThickness ("Thickness", Float) = 0.1
        _WallLineStrength ("Strength", Range(0, 1)) = 0

        [Header(Toon Lighting)]
        _ShadowTint ("Shadow Tint", Color) = (0.55, 0.52, 0.72, 1)
        _ShadowThreshold ("Light/Shadow Threshold", Range(0, 1)) = 0.5
        _ShadowSoftness ("Light/Shadow Softness", Range(0.001, 0.5)) = 0.04
        _CastShadowStrength ("Received Shadow Strength (unused on mobile)", Range(0, 1)) = 1
        _AmbientStrength ("Ambient Strength", Range(0, 2)) = 0.35

        [Header(Emission)]
        _EmissionStrength ("Neon Emission Strength (props with UV0.x = -1)", Range(0, 10)) = 3
        _EmissionMinY ("Emission Min World Y (below = no glow, e.g. under water)", Float) = -1000
        _EmissionOffColor ("Emissive Below Min Y Color (A = strength, 0 = shaded like a prop)", Color) = (0, 0, 0, 1)

        [Header(Hatching)]
        [NoScaleOffset] _HatchTex ("Hatch Texture (multiply: black = ink, white = none; R single, G cross)", 2D) = "white" {}
        _HatchColor ("Hatch Ink Color (multiplied, A = opacity)", Color) = (0.12, 0.08, 0.16, 1)
        _HatchStrength ("Hatch Strength (walls)", Range(0, 1)) = 0.85
        _SurfaceHatchScale ("Hatch On Surface (x wall strength)", Range(0, 1)) = 0.35
        _Hatch1Threshold ("Single Lines From Darkness", Range(0, 1)) = 0.45
        _Hatch2Threshold ("Cross Lines From Darkness", Range(0, 1)) = 0.75
        _HatchSoftness ("Hatch Fade In", Range(0.001, 0.5)) = 0.08
        _HatchScale ("Hatch World Size (m per tile)", Float) = 6
        [Toggle(_HATCH_SCREENSPACE)] _HatchScreenSpace ("Hatch In Screen Space", Float) = 0
        _HatchScreenScale ("Hatch Screen Tiling", Float) = 6
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // Same layout as ToonTerrain's UnityPerMaterial (SRP Batcher compatible, and a material can swap
        // between the two shaders without losing anything).
        CBUFFER_START(UnityPerMaterial)
            float4 _SurfaceTex_ST;
            float4 _WallTex_ST;
            half4 _SurfaceLightColor;
            half4 _SurfaceDarkColor;
            half4 _RaisedLightColor;
            half4 _RaisedDarkColor;
            float _RaisedFromY;
            half _RaisedStrength;
            float _SurfaceScale;
            half4 _GradientBottomColor;
            half4 _GradientTopColor;
            float _GradientStartY;
            float _GradientDistance;
            float _GradientStrength;
            half4 _WallLineColor;
            float _WallLineY;
            float _WallLineThickness;
            float _WallLineStrength;
            half4 _WallLightColor;
            half4 _WallDarkColor;
            float _WallScale;
            float _EdgeFadeStart;
            float _EdgeFadeEnd;
            half4 _FadeColor;
            half4 _OutlineColor;
            float _StrataOutlineWidth;
            float _StrataOutlineStrength;
            float _RimOutlineWidth;
            float _RimOutlineStrength;
            float _BorderOutlineWidth;
            float _BorderOutlineStrength;
            float _PropLineWidth;
            float _PropLineStrength;
            float _SurfaceHatchScale;
            float _EmissionStrength;
            float _EmissionMinY;
            half4 _EmissionOffColor;
            half4 _ShadowTint;
            float _ShadowThreshold;
            float _ShadowSoftness;
            float _CastShadowStrength;
            float _AmbientStrength;
            half4 _HatchColor;
            float _HatchStrength;
            float _Hatch1Threshold;
            float _Hatch2Threshold;
            float _HatchSoftness;
            float _HatchScale;
            float _HatchScreenScale;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma shader_feature_local _HATCH_SCREENSPACE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // Device profiling switches (globals, set by CheatMenu -> Rendering -> "Terrain Cost"). Uniform
            // branches, so ~free while 0; 1 = skip that feature to measure what it costs on the GPU.
            float _TTDebugNoAlbedo;     // flat Light colours instead of the surface/wall texture + fade
            float _TTDebugNoHatch;
            float _TTDebugNoOutlines;   // terrain strata / cliff / border lines
            float _TTDebugNoPropLines;
            float _TTDebugNoWater;      // water depth ramp + water line

            TEXTURE2D(_SurfaceTex); SAMPLER(sampler_SurfaceTex);
            TEXTURE2D(_WallTex);    SAMPLER(sampler_WallTex);
            TEXTURE2D(_HatchTex);   SAMPLER(sampler_HatchTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;    // x = strata coordinate (-1 = emissive prop), y = distance into the grass
                float2 uv1 : TEXCOORD1;   // props: feature-edge barycentrics b0, b1
                float2 uv2 : TEXCOORD2;   // props: b2, y = 1 when the mesh has edge data
                half4 color : COLOR;      // rgb = wall row tint / prop albedo, a = 1 for props
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uv : TEXCOORD0;      // xy = surface UV (top-down), zw = wall planar UV (hatch = wall UV x WallScale / HatchScale)
                float4 data : TEXCOORD1;    // x = strata t, y = distance into the grass, z = water ramp (unclamped), w = P.y - WallLineY
                half4 albedo : TEXCOORD2;   // rgb = vertex colour, a = isProp
                half4 light : TEXCOORD3;    // rgb = lighting multiplier, a = raised tint (grass tops are flat -> exact per vertex)
                half2 hatch : TEXCOORD4;    // single / cross line weights, already x strength x surface scale x ink alpha
                float4 edge : TEXCOORD5;    // props: xyz = feature-edge barycentrics, w = has edge data
                half fogFactor : TEXCOORD6;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Face's dominant-axis projection (same as ToonTerrain) - constant across a flat facet.
            float2 PlanarUV(float3 p, float3 n)
            {
                float3 a = abs(n);
                return a.x > a.z ? (a.x > a.y ? p.zy : p.xz) : (a.z > a.y ? p.xy : p.xz);
            }

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                float3 P = pos.positionWS;
                float3 N = normalize(TransformObjectToWorldNormal(input.normalOS));
                o.positionCS = pos.positionCS;

                o.uv = float4(P.xz / _SurfaceScale, PlanarUV(P, N) / _WallScale);
                o.data = float4(input.uv.x, input.uv.y,
                                (P.y - _GradientStartY) / max(abs(_GradientDistance), 1e-4),
                                P.y - _WallLineY);

                // --- toon main light + ambient as one multiplier (albedo factors out of both terms).
                Light mainLight = GetMainLight();
                half ndl = dot(N, mainLight.direction);
                half lit = smoothstep(_ShadowThreshold - _ShadowSoftness, _ShadowThreshold + _ShadowSoftness, ndl * 0.5h + 0.5h);
                half3 light = lerp(_ShadowTint.rgb, 1.0h, lit) * mainLight.color + SampleSH(N) * _AmbientStrength;

                // --- hatch weights (darkness is per face on flat facets)
                half darkness = 1.0h - saturate(ndl);
                half grass = step(5.98h, input.uv.x);
                half amount = _HatchStrength * lerp(_SurfaceHatchScale, 1.0h, 1.0h - grass) * _HatchColor.a;
                half2 hatch = half2(smoothstep(_Hatch1Threshold, _Hatch1Threshold + _HatchSoftness, darkness),
                                    smoothstep(_Hatch2Threshold, _Hatch2Threshold + _HatchSoftness, darkness)) * amount;

                half3 albedo = input.color.rgb;
                half isProp = input.color.a;

                // --- emissive props: lit neon = albedo x Emission, unshaded and unhatched (ToonTerrain overrode
                // the shaded colour the same way); below Emission Min Y they blend to the Off colour.
                if (isProp > 0.0h && input.uv.x <= -0.5)
                {
                    half above = step(_EmissionMinY, P.y);
                    light = lerp(light, (half3)_EmissionStrength, above * isProp);
                    hatch *= 1.0h - above * isProp;
                    albedo = lerp(albedo, _EmissionOffColor.rgb, (1.0h - above) * isProp * _EmissionOffColor.a);
                }

                o.albedo = half4(albedo, isProp);
                o.light = half4(light, saturate((P.y - _RaisedFromY) * 8.0) * _RaisedStrength);
                o.hatch = hatch;
                o.edge = float4(input.uv1, input.uv2);
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return o;
            }

            // Rule: fwidth / ddx / implicit-mip sampling only outside branches, or in branches whose
            // condition is constant per mesh or per material (quad-uniform).
            half4 Frag(Varyings input) : SV_Target
            {
                half isProp = input.albedo.a;
                half grass = step(5.98h, (half)input.data.x);   // 1 = inside the grass outline

                // --- albedo: ONE read - surface (inside the grass outline) OR wall, never both. Props keep
                // their vertex colour and skip it entirely (isProp is constant per mesh).
                half3 albedo = input.albedo.rgb;
                [branch] if (_TTDebugNoAlbedo > 0.5)
                {
                    albedo = lerp(grass > 0.5h ? _SurfaceLightColor.rgb : _WallLightColor.rgb * input.albedo.rgb, input.albedo.rgb, isProp);
                }
                else [branch] if (isProp < 1.0h)
                {
                    half3 terrain;
                    [branch] if (grass > 0.5h)
                    {
                        half surfaceGray = SAMPLE_TEXTURE2D(_SurfaceTex, sampler_SurfaceTex, input.uv.xy).r;
                        half3 surfaceDark = _SurfaceDarkColor.rgb;
                        half3 surfaceLight = _SurfaceLightColor.rgb;
                        [branch] if (_RaisedStrength > 0.0h)
                        {
                            half raised = input.light.a;
                            surfaceDark = lerp(surfaceDark, _RaisedDarkColor.rgb, raised);
                            surfaceLight = lerp(surfaceLight, _RaisedLightColor.rgb, raised);
                        }
                        half3 surface = lerp(surfaceDark, surfaceLight, surfaceGray);
                        half inward = smoothstep(_EdgeFadeStart, max(_EdgeFadeEnd, _EdgeFadeStart + 1e-3h), (half)input.data.y);
                        terrain = lerp(surface, lerp(_FadeColor.rgb, surface, inward), _FadeColor.a);
                    }
                    else
                    {
                        half wallGray = SAMPLE_TEXTURE2D(_WallTex, sampler_WallTex, input.uv.zw).r;
                        terrain = lerp(_WallDarkColor.rgb, _WallLightColor.rgb, wallGray) * input.albedo.rgb;
                    }
                    albedo = lerp(terrain, input.albedo.rgb, isProp);
                }

                half3 color = albedo * input.light.rgb;

                // --- hatching: read the ink only on faces whose weights allow any (lit faces skip it).
                #if defined(_HATCH_SCREENSPACE)
                    float2 hatchUV = GetNormalizedScreenSpaceUV(input.positionCS) * float2(_ScreenParams.x / _ScreenParams.y, 1.0) * _HatchScreenScale;
                #else
                    float2 hatchUV = input.uv.zw * (_WallScale / _HatchScale);
                #endif
                [branch] if (_HatchStrength * _HatchColor.a > 0.0 && _TTDebugNoHatch < 0.5)
                {
                    float2 hatchDdx = ddx(hatchUV);
                    float2 hatchDdy = ddy(hatchUV);
                    [branch] if (max(input.hatch.x, input.hatch.y) > 0.0h)
                    {
                        half2 ink = 1.0h - SAMPLE_TEXTURE2D_GRAD(_HatchTex, sampler_HatchTex, hatchUV, hatchDdx, hatchDdy).rg;
                        color *= lerp(1.0h, _HatchColor.rgb, saturate(max(ink.x * input.hatch.x, ink.y * input.hatch.y)));
                    }
                }

                // --- texture-level outlines on the terrain (rock ledges, cliff top, grass border)
                // Pixel distance to the nearest line first (the derivative stays outside the per-pixel
                // branch); the width/strength selection only runs within reach of a line - everywhere else
                // saturate(width / 2 - d + 0.5) is exactly 0 for every line type.
                [branch] if (_TTDebugNoOutlines < 0.5 && _OutlineColor.a > 0.0)
                {
                    float t = input.data.x;
                    float px = max(fwidth(t), 1e-5);
                    half d = abs(frac(t + 0.5) - 0.5) / px;
                    half reach = max(max(_StrataOutlineWidth, _RimOutlineWidth), _BorderOutlineWidth) * 0.5h + 0.5h;
                    [branch] if (d < reach && isProp < 1.0h)
                    {
                        half n = round((half)t);
                        half isTop = step(4.5h, n);      // 5 = cliff top edge
                        half isBorder = step(5.5h, n);   // 6 = grass border
                        half width = lerp(lerp(_StrataOutlineWidth, _RimOutlineWidth, isTop), _BorderOutlineWidth, isBorder);
                        half strength = lerp(lerp(_StrataOutlineStrength, _RimOutlineStrength, isTop), _BorderOutlineStrength, isBorder) * step(0.5h, n);
                        half outline = saturate(width * 0.5h - d + 0.5h) * strength * (1.0h - isProp) * _OutlineColor.a;
                        color = lerp(color, _OutlineColor.rgb, outline);
                    }
                }

                // --- prop edge lines (edge.w is constant per mesh - 0 on every tile)
                [branch] if (input.edge.w > 0.0 && _TTDebugNoPropLines < 0.5)
                {
                    float3 eb = input.edge.xyz;
                    float3 epx = eb / max(fwidth(eb), 1e-5);
                    half emin = min(epx.x, min(epx.y, epx.z));
                    half propLine = saturate(_PropLineWidth * 0.5h - emin + 0.5h) * saturate((half)input.edge.w) * _PropLineStrength * _OutlineColor.a;
                    color = lerp(color, _OutlineColor.rgb, propLine);
                }

                // --- water depth ramp (unlit Bottom colour below Start Y, back to shaded by Start Y + Distance)
                [branch] if (_GradientStrength > 0.0 && _TTDebugNoWater < 0.5)
                {
                    half gt = saturate((half)input.data.z);
                    [branch] if (gt < 1.0h)
                    {
                        half3 depthColor = lerp(_GradientBottomColor.rgb, color * _GradientTopColor.rgb, gt);
                        color = lerp(color, depthColor, _GradientStrength);
                    }
                    else
                    {
                        // above the band (most of the screen): lerp(c, c * Top, S) == c * lerp(1, Top, S)
                        color *= lerp(1.0h, _GradientTopColor.rgb, _GradientStrength);
                    }
                }

                // --- water line on walls, pixel-stable width
                [branch] if (_WallLineStrength > 0.0 && _TTDebugNoWater < 0.5)
                {
                    float lineAA = max(fwidth(input.data.w), 1e-4);
                    half lineDistance = abs((half)input.data.w);
                    half lineHalf = max(_WallLineThickness * 0.5h, 0.0h);
                    // beyond lineHalf + AA the smoothstep is exactly 1 -> no line; grass never gets one
                    [branch] if (grass < 0.5h && lineDistance < lineHalf + (half)lineAA)
                    {
                        half wallLine = 1.0h - smoothstep(lineHalf, lineHalf + (half)lineAA, lineDistance);
                        color = lerp(color, _WallLineColor.rgb, wallLine * saturate(_WallLineStrength) * _WallLineColor.a);
                    }
                }

                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        // Depth for URP features that need it (depth texture / priming). No ShadowCaster: mobile renders
        // without shadows - use ToonTerrain where casting matters.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 DepthVert(DepthAttributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return TransformObjectToHClip(input.positionOS.xyz);
            }

            half DepthFrag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
