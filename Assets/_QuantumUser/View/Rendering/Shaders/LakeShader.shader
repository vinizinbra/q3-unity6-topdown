// Mobile-first, texture-free, UNLIT stylized water for URP.
// No camera depth texture, no shadow sampling, no per-pixel lighting - the flat look comes entirely
// from a couple of quantized world-space sine bands (the "water pattern") plus optional shoreline
// foam. Foam distance comes from WaterShoreBaker's baked global _ShoreField (see WaterShoreBaker.cs),
// so there is no DepthNormals prepass anywhere in this shader.
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
        _FoamDistance ("Foam Distance", Range(0.01, 2)) = 0.35
        _FoamNoiseScale ("Foam Ring Count", Range(1, 8)) = 3
        _FoamSpeed ("Foam Ring Speed", Range(0, 3)) = 0.4
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
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local_fragment _SHOREFIELD_FOAM
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
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float TriangleWave(float value)
            {
                return abs(frac(value) * 2.0 - 1.0) * 2.0 - 1.0;
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

                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // Texture-free stylized water: three cheap world-space sines -> 0..1, quantized into
                // _FacetSteps flat bands. _PatternContrast fades the whole thing toward a flat mid
                // tone (0 = perfectly flat single color, 1 = full shallow<->deep band range).
                float2 p = input.positionWS.xz * _PatternScale;
                float t = _Time.y * _PatternSpeed;
                half n = sin(p.x + t) + sin(p.y * 1.3h - t * 0.85h) + sin((p.x + p.y) * 0.7h + t * 0.5h);
                n = saturate(n * 0.1667h + 0.5h);

                half band = floor(n * _FacetSteps) / max(_FacetSteps - 1.0h, 1.0h);
                band = lerp(0.5h, band, _PatternContrast);
                half3 waterColor = lerp(_DeepColor.rgb, _ShallowColor.rgb, band);

                // Fake specular: a bright crest on the pattern peaks, no light/normal needed.
                half glimmer = smoothstep(0.86h, 0.98h, n) * _HighlightStrength;
                waterColor = lerp(waterColor, _HighlightColor.rgb, glimmer);

                half foamMask = 0.0h;

#if defined(_SHOREFIELD_FOAM)
                // depthGap = world-space distance to shore, sampled from the baked field by world XZ.
                float2 shoreUV = (input.positionWS.xz - _ShoreFieldParams.xy) / _ShoreFieldParams.z + 0.5;
                half depthGap = SAMPLE_TEXTURE2D(_ShoreField, sampler_ShoreField, shoreUV).r * _ShoreFieldParams.w;

                half foamArea = saturate(1.0h - depthGap / max(_FoamDistance, 0.001h));

                // Animated contour rings + a solid contact line at the waterline.
                half normalizedGap = saturate(depthGap / max(_FoamDistance, 0.001h));
                half ringPhase = frac(normalizedGap * _FoamNoiseScale - _Time.y * _FoamSpeed);
                half ring = smoothstep(0.72h, 0.9h, 1.0h - abs(ringPhase * 2.0h - 1.0h));
                half contactLine = 1.0h - smoothstep(0.0h, 0.10h, normalizedGap);

                foamMask = saturate(max(ring * foamArea, contactLine));
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
