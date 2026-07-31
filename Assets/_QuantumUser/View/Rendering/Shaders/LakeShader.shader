// Mobile-first, texture-free stylized water for URP.
// INTERSECTION_FOAM requires the URP camera depth texture. Leave it disabled on
// low-end mobile when LakeVisualBuilder's separate foam-ring mesh is sufficient.
Shader "Project/LakeShader"
{
    Properties
    {
        [Header(Color)]
        _ShallowColor ("Shallow Color", Color) = (0.12, 0.62, 0.72, 1)
        _DeepColor ("Deep Color", Color) = (0.025, 0.25, 0.42, 1)
        _ShadowColor ("Shadow Tint", Color) = (0.45, 0.60, 0.72, 1)
        _WaterOpacity ("Water Opacity", Range(0.25, 1)) = 0.82

        [Header(Low Poly Waves)]
        _WaveHeight ("Wave Height", Range(0, 0.5)) = 0.08
        _WaveScale ("Wave Scale", Range(0.1, 8)) = 1.3
        _WaveSpeed ("Wave Speed", Range(0, 4)) = 0.65
        _FacetSteps ("Color Facet Steps", Range(2, 8)) = 4

        [Header(Sun Sparkle)]
        _HighlightColor ("Highlight Color", Color) = (0.65, 0.95, 1, 1)
        _HighlightSize ("Highlight Size", Range(8, 128)) = 48
        _HighlightStrength ("Highlight Strength", Range(0, 1)) = 0.35

        [Header(Depth Intersection Foam)]
        [Toggle(_INTERSECTION_FOAM)] _IntersectionFoam ("Enable Depth Intersection Foam", Float) = 0
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
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local_fragment _INTERSECTION_FOAM
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#if defined(_INTERSECTION_FOAM)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#endif

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half4 _ShadowColor;
                half4 _HighlightColor;
                half4 _FoamColor;
                half _WaterOpacity;
                half _WaveHeight;
                half _WaveScale;
                half _WaveSpeed;
                half _FacetSteps;
                half _HighlightSize;
                half _HighlightStrength;
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
                float4 screenPos : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Two cheap directional triangle waves. Displacement happens per vertex:
            // use a reasonably triangulated, flat water mesh for the low-poly silhouette.
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
                float time = _Time.y * _WaveSpeed;
                float waveA = TriangleWave((positionWS.x + positionWS.z * 0.63) * _WaveScale * 0.16 + time * 0.18);
                float waveB = TriangleWave((positionWS.z - positionWS.x * 0.41) * _WaveScale * 0.11 - time * 0.13);
                positionWS.y += (waveA + waveB * 0.55) * _WaveHeight;

                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.screenPos = ComputeScreenPos(output.positionCS);
                return output;
            }

            half4 Frag(Varyings input, half facing : VFACE) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // A constant normal per rasterized triangle creates genuine faceting,
                // without a normal map or texture samples.
                float3 normalWS = normalize(cross(ddy(input.positionWS), ddx(input.positionWS)));
                normalWS *= facing >= 0.0h ? 1.0h : -1.0h;

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half toonLight = step(0.42h, ndotl) * mainLight.shadowAttenuation;

                // Quantized world-space bands add broad low-poly color variation.
                half band = dot(normalWS, normalize(half3(0.35h, 0.8h, 0.48h))) * 0.5h + 0.5h;
                band = floor(band * _FacetSteps) / max(_FacetSteps - 1.0h, 1.0h);
                half3 waterColor = lerp(_DeepColor.rgb, _ShallowColor.rgb, band);
                waterColor *= lerp(_ShadowColor.rgb, half3(1, 1, 1), toonLight);

                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float3 halfDir = SafeNormalize(mainLight.direction + viewDirWS);
                half sparkle = pow(saturate(dot(normalWS, halfDir)), _HighlightSize);
                sparkle = step(0.5h, sparkle) * _HighlightStrength * mainLight.shadowAttenuation;
                waterColor = lerp(waterColor, _HighlightColor.rgb, sparkle);
                half foamMask = 0.0h;

#if defined(_INTERSECTION_FOAM)
                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                float rawSceneDepth = SampleSceneDepth(screenUV);
                float sceneEyeDepth = LinearEyeDepth(rawSceneDepth, _ZBufferParams);
                float waterEyeDepth = -TransformWorldToView(input.positionWS).z;
                half depthGap = max(0.0, sceneEyeDepth - waterEyeDepth);

                half foamArea = saturate(1.0h - depthGap / max(_FoamDistance, 0.001h));

                // Animated contour rings: depthGap is the distance away from the
                // intersection, so every stripe follows the shoreline/object shape.
                half normalizedGap = saturate(depthGap / max(_FoamDistance, 0.001h));
                half ringPhase = frac(normalizedGap * _FoamNoiseScale
                                      - _Time.y * _FoamSpeed);
                half ring = 1.0h - abs(ringPhase * 2.0h - 1.0h);
                ring = smoothstep(0.72h, 0.9h, ring);

                // A solid narrow contact line keeps the object connection readable.
                half contactLine = 1.0h - smoothstep(0.0h, 0.10h, normalizedGap);
                half foam = saturate(max(ring * foamArea, contactLine));
                foamMask = foam;
                waterColor = lerp(waterColor, _FoamColor.rgb, foam);
#endif

                // Keep graphic details crisp while allowing the lake bed to show.
                half opacity = saturate(max(_WaterOpacity, max(foamMask, sparkle)));
                return half4(waterColor, opacity);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
