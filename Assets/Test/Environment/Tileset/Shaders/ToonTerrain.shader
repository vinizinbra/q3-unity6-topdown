// Cartoon terrain shader for the tileset pieces (URP). One material for every tile:
//  - Surface (grass) texture projected top-down in WORLD space, wall texture world-space triplanar,
//    so nothing stretches when the builder scales tiles to the cube height or stretches Centers.
//  - Mesh data baked by build_grasscliff_autotiles.py:
//      UV0.x = strata coordinate: integer values sit on the wall's crease rows (1-4 rock ledges,
//              5 = cliff top edge, 6 = grass border) -> outline lines drawn here, width in pixels;
//              each group (ledges / cliff top / grass border) has its own width + strength.
//      UV0.y = surface weight: 0 on the wall / at the grass border, 1 once inside the grass
//              -> one-directional fade: the surface starts at the grass outline in _FadeColor and
//              fades into the surface texture going inward (_EdgeFadeStart/End), broken up by noise.
//      COLOR.rgb = wall tint per row (terrain) or albedo (props), COLOR.a = 1 for props.
//  - Toon lighting (stepped main light + tinted shadow, received shadows) and hand-drawn style
//    hatching in the shadows, applied as a MULTIPLY: in _HatchTex black = ink, white = nothing.
//    R = single lines (mid shadow), G = cross lines (deep shadow); a grayscale texture works too
//    (both layers then use the same lines, the deep layer just darkens them further).
Shader "RiftRaiders/Test/ToonTerrain"
{
    Properties
    {
        [Header(Surface)]
        _SurfaceTex ("Surface Texture (RGB, A = edge noise)", 2D) = "white" {}
        _SurfaceTint ("Surface Tint", Color) = (1, 1, 1, 1)
        _SurfaceScale ("Surface World Size (m per tile)", Float) = 4

        [Header(Wall)]
        _WallTex ("Wall Texture", 2D) = "white" {}
        _WallTint ("Wall Tint", Color) = (1, 1, 1, 1)
        _WallScale ("Wall World Size (m per tile)", Float) = 3
        _TriplanarSharpness ("Triplanar Blend Sharpness", Range(1, 16)) = 6

        [Header(Wall To Surface Fade)]
        _EdgeFadeStart ("Fade Start (0 = at the outline)", Range(0, 1)) = 0
        _EdgeFadeEnd ("Fade End (how far inward it fades)", Range(0, 1)) = 0.4
        _EdgeFadeNoise ("Fade Noise (uses Surface A)", Range(0, 1)) = 0.2
        _FadeColor ("Fade Color at the outline (A = strength)", Color) = (0.55, 0.42, 0.25, 1)

        [Header(Texture Outlines)]
        _OutlineColor ("Outline Color (A = opacity)", Color) = (0.17, 0.11, 0.08, 1)
        _StrataOutlineWidth ("Rock Ledge Lines Width (px)", Range(0, 8)) = 1.8
        _StrataOutlineStrength ("Rock Ledge Lines Strength", Range(0, 1)) = 0.85
        _RimOutlineWidth ("Cliff Top Edge Width (px)", Range(0, 10)) = 1.8
        _RimOutlineStrength ("Cliff Top Edge Strength", Range(0, 1)) = 0.6
        _BorderOutlineWidth ("Grass Border Width (px)", Range(0, 10)) = 1.5
        _BorderOutlineStrength ("Grass Border Strength", Range(0, 1)) = 0

        [Header(Toon Lighting)]
        _ShadowTint ("Shadow Tint", Color) = (0.55, 0.52, 0.72, 1)
        _ShadowThreshold ("Light/Shadow Threshold", Range(0, 1)) = 0.5
        _ShadowSoftness ("Light/Shadow Softness", Range(0.001, 0.5)) = 0.04
        _CastShadowStrength ("Received Shadow Strength", Range(0, 1)) = 1
        _AmbientStrength ("Ambient Strength", Range(0, 2)) = 0.35

        [Header(Hatching)]
        [NoScaleOffset] _HatchTex ("Hatch Texture (multiply: black = ink, white = none; R single, G cross)", 2D) = "white" {}
        _HatchColor ("Hatch Ink Color (multiplied, A = opacity)", Color) = (0.12, 0.08, 0.16, 1)
        _HatchStrength ("Hatch Strength (walls)", Range(0, 1)) = 0.85
        _SurfaceHatchScale ("Hatch On Surface (x wall strength)", Range(0, 1)) = 0.35
        _Hatch1Threshold ("Single Lines From Darkness", Range(0, 1)) = 0.45
        _Hatch2Threshold ("Cross Lines From Darkness", Range(0, 1)) = 0.75
        _HatchSoftness ("Hatch Fade In", Range(0.001, 0.5)) = 0.08
        _HatchScale ("Hatch World Size (m per tile)", Float) = 2
        [Toggle(_HATCH_SCREENSPACE)] _HatchScreenSpace ("Hatch In Screen Space", Float) = 0
        _HatchScreenScale ("Hatch Screen Tiling", Float) = 6
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _SurfaceTex_ST;
            float4 _WallTex_ST;
            half4 _SurfaceTint;
            float _SurfaceScale;
            half4 _WallTint;
            float _WallScale;
            float _TriplanarSharpness;
            float _EdgeFadeStart;
            float _EdgeFadeEnd;
            float _EdgeFadeNoise;
            half4 _FadeColor;
            half4 _OutlineColor;
            float _StrataOutlineWidth;
            float _StrataOutlineStrength;
            float _RimOutlineWidth;
            float _RimOutlineStrength;
            float _BorderOutlineWidth;
            float _BorderOutlineStrength;
            float _SurfaceHatchScale;
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma shader_feature_local _HATCH_SCREENSPACE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_SurfaceTex); SAMPLER(sampler_SurfaceTex);
            TEXTURE2D(_WallTex);    SAMPLER(sampler_WallTex);
            TEXTURE2D(_HatchTex);   SAMPLER(sampler_HatchTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 data : TEXCOORD2;
                half4 color : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.data = input.uv;
                o.color = input.color;
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return o;
            }

            half3 Triplanar(TEXTURE2D_PARAM(tex, samp), float3 p, float3 w)
            {
                return SAMPLE_TEXTURE2D(tex, samp, p.zy).rgb * w.x
                     + SAMPLE_TEXTURE2D(tex, samp, p.xz).rgb * w.y
                     + SAMPLE_TEXTURE2D(tex, samp, p.xy).rgb * w.z;
            }

            // Anti-aliased line on every integer of t, `widthPx` wide on screen.
            half IntegerLine(float t, float widthPx)
            {
                float px = max(fwidth(t), 1e-5);
                float d = abs(frac(t + 0.5) - 0.5) / px;
                return saturate(widthPx * 0.5 - d + 0.5);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 P = input.positionWS;
                float3 N = normalize(input.normalWS);
                half isProp = input.color.a;

                // --- albedo: wall (triplanar) fading into surface (top-down), both in world space
                float3 tw = pow(abs(N), _TriplanarSharpness);
                tw /= max(dot(tw, 1.0), 1e-4);
                half3 wall = Triplanar(TEXTURE2D_ARGS(_WallTex, sampler_WallTex), P / _WallScale, tw) * _WallTint.rgb * input.color.rgb;
                half4 surfSample = SAMPLE_TEXTURE2D(_SurfaceTex, sampler_SurfaceTex, P.xz / _SurfaceScale);
                half3 surface = surfSample.rgb * _SurfaceTint.rgb;

                // One-directional fade, outside -> inside: everything outside the grass outline (cliff
                // face, rock rim) is plain wall; the surface starts AT the outline (strata coordinate 6)
                // in full fade colour and fades into the surface texture going inward over
                // _EdgeFadeStart.._EdgeFadeEnd (UV0.y = distance into the grass). Nothing before the outline.
                half w = step(5.98, input.data.x);                       // 1 = inside the grass outline
                float fade = input.data.y + (surfSample.a - 0.5) * _EdgeFadeNoise * saturate(input.data.y * 4.0);
                half inward = smoothstep(_EdgeFadeStart, max(_EdgeFadeEnd, _EdgeFadeStart + 1e-3), fade);
                half3 top = lerp(surface, lerp(_FadeColor.rgb, surface, inward), _FadeColor.a);
                half3 terrain = lerp(wall, top, w);
                half3 albedo = lerp(terrain, input.color.rgb, isProp);

                // --- toon main light (+ received shadows)
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(P));
                #else
                    float4 shadowCoord = TransformWorldToShadowCoord(P);
                #endif
                Light mainLight = GetMainLight(shadowCoord);
                float ndl = dot(N, mainLight.direction);
                float shadowAtten = lerp(1.0, mainLight.shadowAttenuation, _CastShadowStrength);
                float lit = smoothstep(_ShadowThreshold - _ShadowSoftness, _ShadowThreshold + _ShadowSoftness, ndl * 0.5 + 0.5);
                lit *= smoothstep(0.5 - _ShadowSoftness, 0.5 + _ShadowSoftness, shadowAtten);

                half3 color = albedo * lerp(_ShadowTint.rgb, 1.0, lit) * mainLight.color;
                color += albedo * SampleSH(N) * _AmbientStrength;

                // --- hatching: single lines in mid shadow, cross lines in deep shadow
                float darkness = 1.0 - saturate(ndl) * shadowAtten;
                #if defined(_HATCH_SCREENSPACE)
                    float2 suv = GetNormalizedScreenSpaceUV(input.positionCS) * float2(_ScreenParams.x / _ScreenParams.y, 1.0) * _HatchScreenScale;
                    half2 hatch = SAMPLE_TEXTURE2D(_HatchTex, sampler_HatchTex, suv).rg;
                #else
                    half2 hatch = Triplanar(TEXTURE2D_ARGS(_HatchTex, sampler_HatchTex), P / _HatchScale, tw).rg;
                #endif
                half2 ink = 1.0 - hatch;   // black = ink, white = paper (nothing)
                half h1 = ink.x * smoothstep(_Hatch1Threshold, _Hatch1Threshold + _HatchSoftness, darkness);
                half h2 = ink.y * smoothstep(_Hatch2Threshold, _Hatch2Threshold + _HatchSoftness, darkness);
                half hatchAmount = _HatchStrength * lerp(_SurfaceHatchScale, 1.0, 1.0 - w);   // strong on walls, light on grass
                // multiply shadow: full ink multiplies by the ink colour, paper leaves the colour untouched
                color *= lerp(1.0, _HatchColor.rgb, saturate(max(h1, h2)) * hatchAmount * _HatchColor.a);

                // --- texture-level outlines on the terrain (rock ledges, cliff top, grass border)
                float t = input.data.x;
                float n = round(t);
                half isTop = step(4.5, n);      // 5 = cliff top edge
                half isBorder = step(5.5, n);   // 6 = grass border
                half width = lerp(lerp(_StrataOutlineWidth, _RimOutlineWidth, isTop), _BorderOutlineWidth, isBorder);
                half strength = lerp(lerp(_StrataOutlineStrength, _RimOutlineStrength, isTop), _BorderOutlineStrength, isBorder) * step(0.5, n);
                half outline = IntegerLine(t, width) * strength * (1.0 - isProp) * _OutlineColor.a;
                color = lerp(color, _OutlineColor.rgb, outline);

                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 ShadowVert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return positionCS;
            }

            half4 ShadowFrag() : SV_Target { return 0; }
            ENDHLSL
        }

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

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 DepthVert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return TransformObjectToHClip(input.positionOS.xyz);
            }

            half DepthFrag(float4 positionCS : SV_POSITION) : SV_Target { return positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            Varyings DepthNormalsVert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 DepthNormalsFrag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = PackNormalOctQuadEncode(normalWS);
                    return half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)), 0.0);
                #else
                    return half4(normalWS, 0.0);
                #endif
            }
            ENDHLSL
        }
    }
}
