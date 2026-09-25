// Mobile-first, texture-free, UNLIT stylized water for URP.
// No camera depth texture, no shadow sampling, no per-pixel lighting - the flat look comes entirely
// from a couple of quantized world-space wave bands (the "water pattern") plus optional stepped,
// pixel-art style shoreline foam (flat bands with ~1px anti-aliased edges, stepped frame rate). Foam
// distance comes from WaterShoreBaker's baked global _ShoreField (see WaterShoreBaker.cs), so there
// is no DepthNormals prepass anywhere in this shader.
//
// Perf: the water is one screen-covering quad, so almost everything is paid per PIXEL. Every value
// that is linear in world position or time (pattern phases, shore UV, surge phase, dash grid, ring
// time offset) is therefore computed per VERTEX and interpolated - exact, since linear interpolation
// of a linear function is the function itself - leaving the fragment with a non-dependent texture
// read, triangle waves instead of sin(), and half-precision foam math with no branches.
Shader "Project/LakeShader"
{
    Properties
    {
        [Header(Color)]
        _ShallowColor ("Shallow Color", Color) = (0.12, 0.62, 0.72, 1)
        _DeepColor ("Deep Color", Color) = (0.025, 0.25, 0.42, 1)
        _WaterOpacity ("Water Opacity", Range(0, 1)) = 0.62

        [Header(Water Pattern)]
        _FacetSteps ("Color Steps", Range(1, 8)) = 3
        _PatternScale ("Pattern Scale", Range(0.02, 2)) = 0.22
        _PatternSpeed ("Pattern Speed", Range(0, 3)) = 0.5
        _PatternContrast ("Pattern Contrast", Range(0, 1)) = 0.6

        [Header(Glimmer)]
        _HighlightColor ("Glimmer Color", Color) = (0.65, 0.95, 1, 1)
        _HighlightStrength ("Glimmer Strength", Range(0, 1)) = 0.15

        [Header(Low Poly Waves)]
        _WaveHeight ("Wave Height", Range(0, 0.5)) = 0.05
        _WaveScale ("Wave Scale", Range(0.1, 8)) = 1.3
        _WaveSpeed ("Wave Speed", Range(0, 4)) = 0.6

        [Header(Shore Foam)]
        // Foam distance-to-coast comes from WaterShoreBaker's global _ShoreField (no depth buffer).
        [Toggle(_SHOREFIELD_FOAM)] _ShoreFieldFoam ("Enable Shore Field Foam", Float) = 0
        _FoamColor ("Foam Color", Color) = (0.82, 1, 1, 1)
        _FoamDistance ("Foam Distance (world)", Range(0.01, 6)) = 1.5
        _FoamNoiseScale ("Foam Ring Count", Range(1, 8)) = 3
        _FoamSpeed ("Foam Ring Speed (+ = rolls in to shore)", Range(-3, 3)) = 0.4
        _FoamRingWidth ("Foam Ring Width", Range(0.05, 0.6)) = 0.25
        _FoamLineWidth ("Contact Line Width", Range(0, 1)) = 0.15
        _FoamSurge ("Contact Line Surge", Range(0, 0.5)) = 0.1
        _FoamSurgeSpeed ("Contact Line Surge Speed", Range(0, 4)) = 1
        _FoamBreakup ("Ring Breakup (outer rings dash out)", Range(0, 1.5)) = 0.8
        _FoamDashSize ("Ring Dash Size (world)", Range(0.05, 2)) = 0.375
        _ShoreSteps ("Shore Tint Steps (0 = off)", Range(0, 6)) = 3
        _ShoreTintStrength ("Shore Tint Strength", Range(0, 1)) = 0.35

        [Header(Pixel Art Look)]
        _FoamEdgeSoftness ("Foam Edge Softness (0 = hard pixel edges, 1 = ~1px anti-aliased)", Range(0, 2)) = 1
        _FoamFrameRate ("Foam Frame Rate (0 = smooth)", Range(0, 30)) = 10
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent-50"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            // Both stages: the vertex shader writes the shore UV / foam interpolants the fragment reads.
            // A _fragment-only keyword compiles a vertex variant without them -> Metal pipeline error.
            #pragma shader_feature_local _SHOREFIELD_FOAM
            #pragma multi_compile_instancing

            // Only Core (transforms + _Time + texture macros). No Lighting.hlsl, no shadows, no depth.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

#if defined(_SHOREFIELD_FOAM)
            // Published once per level by WaterShoreBaker via Shader.SetGlobal*. R = distance to the
            // nearest land, 0 at the coast .. 1 at maxShoreDistanceWorld out. Params: xy = world
            // center XZ, z = world size (2*worldExtent), w = maxShoreDistanceWorld.
            TEXTURE2D(_ShoreField);
            SAMPLER(sampler_ShoreField);
            float4 _ShoreFieldParams;
#endif

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half4 _HighlightColor;
                half4 _FoamColor;
                half _WaterOpacity;
                half _FacetSteps;
                half _PatternScale;
                half _PatternSpeed;
                half _PatternContrast;
                half _HighlightStrength;
                half _WaveHeight;
                half _WaveScale;
                half _WaveSpeed;
                half _FoamDistance;
                half _FoamNoiseScale;
                half _FoamSpeed;
                half _FoamRingWidth;
                half _FoamLineWidth;
                half _FoamSurge;
                half _FoamSurgeSpeed;
                half _FoamBreakup;
                half _FoamDashSize;
                half _ShoreSteps;
                half _ShoreTintStrength;
                half _FoamFrameRate;
                half _FoamEdgeSoftness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                // xyz = the three pattern wave phases, w = contact-line surge phase - all in CYCLES
                // (frac() in the fragment turns them into triangle waves).
                float4 wavePhases : TEXCOORD0;
#if defined(_SHOREFIELD_FOAM)
                // xy = _ShoreField UV, zw = ring-dash grid coords (world XZ / dash size).
                float4 shoreUVDash : TEXCOORD1;
                // x = ring time offset (already frac'd), y = shore field value -> normalized foam gap.
                float2 foamConst : TEXCOORD2;
#endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float TriangleWave(float value)
            {
                return abs(frac(value) * 2.0 - 1.0) * 2.0 - 1.0;
            }

            // 0..1 triangle wave of a phase in cycles - the cheap stand-in for sin() * 0.5 + 0.5.
            half Tri01(float cycles)
            {
                return (half)abs(frac(cycles) * 2.0 - 1.0);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                // Cheap per-vertex silhouette movement - flat, so it needs a triangulated mesh to show.
                float time = _Time.y * _WaveSpeed;
                float waveA = TriangleWave((positionWS.x + positionWS.z * 0.63) * _WaveScale * 0.16 + time * 0.18);
                float waveB = TriangleWave((positionWS.z - positionWS.x * 0.41) * _WaveScale * 0.11 - time * 0.13);
                positionWS.y += (waveA + waveB * 0.55) * _WaveHeight;

                // Pattern: the same three directional waves as the old sin() pattern, as phases in
                // cycles (radians / 2pi) so the fragment only needs frac/abs.
                float2 p = positionWS.xz * _PatternScale;
                float t = _Time.y * _PatternSpeed;
                output.wavePhases.x = (p.x + t) * INV_TWO_PI;
                output.wavePhases.y = (p.y * 1.3 - t * 0.85) * INV_TWO_PI;
                output.wavePhases.z = ((p.x + p.y) * 0.7 + t * 0.5) * INV_TWO_PI;

#if defined(_SHOREFIELD_FOAM)
                // Stepped foam clock (pixel-art frame rate). Per vertex, so it costs nothing per pixel.
                float foamTime = _Time.y;
                if (_FoamFrameRate > 0.0)
                    foamTime = floor(foamTime * _FoamFrameRate) / _FoamFrameRate;

                // Surge phase drifts along the coast so the whole shoreline doesn't breathe in unison.
                output.wavePhases.w = (positionWS.x * 0.31 + positionWS.z * 0.23 + foamTime * _FoamSurgeSpeed) * INV_TWO_PI;

                output.shoreUVDash.xy = (positionWS.xz - _ShoreFieldParams.xy) / _ShoreFieldParams.z + 0.5;
                output.shoreUVDash.zw = positionWS.xz / max(_FoamDashSize, 0.01);

                // frac here keeps the fragment's half-precision ring math small as _Time grows.
                output.foamConst.x = frac(foamTime * _FoamSpeed);
                output.foamConst.y = _ShoreFieldParams.w / max(_FoamDistance, 0.001);
#endif

                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // Texture-free stylized water: three triangle waves -> 0..1, quantized into
                // _FacetSteps flat bands. _PatternContrast fades the whole thing toward a flat mid
                // tone (0 = perfectly flat single color, 1 = full shallow<->deep band range).
                half n = (Tri01(input.wavePhases.x) + Tri01(input.wavePhases.y) + Tri01(input.wavePhases.z)) * 0.3333h;

                half band = floor(n * _FacetSteps) / max(_FacetSteps - 1.0h, 1.0h);
                band = lerp(0.5h, band, _PatternContrast);
                half3 waterColor = lerp(_DeepColor.rgb, _ShallowColor.rgb, band);

                // Fake specular: a bright crest on the pattern peaks, no light/normal needed.
                half glimmer = smoothstep(0.86h, 0.98h, n) * _HighlightStrength;
                waterColor = lerp(waterColor, _HighlightColor.rgb, glimmer);

                half foamMask = 0.0h;

#if defined(_SHOREFIELD_FOAM)
                // Stepped foam bands. Every edge is a saturate() ramp over ~1 screen pixel (fwidth *
                // _FoamEdgeSoftness), never step()/if - branch-free, and FXC's min-precision path
                // miscompiles step/compare on halves for mobile targets.
                half gap = saturate(SAMPLE_TEXTURE2D(_ShoreField, sampler_ShoreField, input.shoreUVDash.xy).r * (half)input.foamConst.y);
                half gapWidth = max(fwidth(gap) * _FoamEdgeSoftness, 0.001h);
                half invGapWidth = 1.0h / gapWidth;
                half inFoamBand = saturate((0.999h - gap) * 1000.0h);

                // 1. Shore tint: flat colour steps getting lighter toward the coast.
                half tintX = (1.0h - gap) * _ShoreSteps;
                half tintWidth = max(gapWidth * _ShoreSteps, 0.001h);
                half tintStepped = floor(tintX) + saturate((frac(tintX) - 1.0h + tintWidth) / tintWidth);
                half shoreTint = tintStepped / max(_ShoreSteps, 1.0h);
                waterColor = lerp(waterColor, _FoamColor.rgb, shoreTint * _ShoreTintStrength);

                // 2. Contact line hugging the coast, surging in and out.
                half surge = Tri01(input.wavePhases.w) * _FoamSurge;
                half contactLine = saturate((_FoamLineWidth + surge - gap) * invGapWidth + 0.5h);

                // 3. Rings travelling toward the shore, dashing out the farther they are. Distance to
                //    the ring's centre is wrapped so both edges get the same soft ramp.
                half ringPhase = frac(gap * _FoamNoiseScale + (half)input.foamConst.x);
                half ringHalf = _FoamRingWidth * 0.5h;
                half ringDist = abs(ringPhase - ringHalf);
                ringDist = min(ringDist, 1.0h - ringDist);
                half ring = saturate((ringHalf - ringDist) * invGapWidth / _FoamNoiseScale + 0.5h) * inFoamBand;

                // Dashes: R2 low-discrepancy hash per dash cell - no sin().
                float2 dashCell = floor(input.shoreUVDash.zw);
                half dashNoise = (half)frac(dashCell.x * 0.7548776 + dashCell.y * 0.5698403);
                ring *= saturate((dashNoise - gap * _FoamBreakup) * 32.0h);

                foamMask = max(ring, contactLine);
                waterColor = lerp(waterColor, _FoamColor.rgb, foamMask);
#endif

                half opacity = saturate(max(_WaterOpacity, max(foamMask, glimmer)));
                return half4(waterColor, opacity);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
