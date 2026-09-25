// Stylized cloud / height-fog floor - a drop-in replacement for the LakeShader water quad in worlds
// where the void below the platforms should read as a sea of clouds instead of water (see
// WorldWaterTheme.SurfaceMaterial / EnvironmentManager).
//
// Built for mobile, where the full-screen water quad is pixel-bound (see LakeShader notes):
//  - alpha-blended like the lake quad (same cost class), so the building walls below the floor show
//    through the thin parts - Gaps / Mid / Tops opacity per band;
//  - 2 reads of ONE small grayscale cloud texture at different scale / speed / direction, +1 optional
//    read of WaterShoreBaker's _ShoreField; every UV is linear in world XZ + time, so it's computed per
//    vertex (exact on the 4-vertex quad) and the reads are non-dependent;
//  - toon bands (Deep / Mid / Top) from saturate() ramps, all half, no sin / step / if.
// Pair it with the tileset material's water-depth gradient ending in Deep Color, so cliff walls sink
// into the clouds.
Shader "Project/CloudFog"
{
    Properties
    {
        [Header(Colors)]
        _DeepColor ("Deep Color (gaps between clouds)", Color) = (0.22, 0.08, 0.32, 1)
        _MidColor ("Mid Color", Color) = (0.48, 0.2, 0.6, 1)
        _TopColor ("Top Color (cloud tops)", Color) = (0.86, 0.55, 0.92, 1)

        [Header(Transparency)]
        // What's below the cloud floor (building walls going down) shows through the thin parts.
        _DeepAlpha ("Gaps Opacity", Range(0, 1)) = 0.35
        _MidAlpha ("Mid Opacity", Range(0, 1)) = 0.75
        _TopAlpha ("Cloud Tops Opacity", Range(0, 1)) = 0.95

        [Header(Placement)]
        _HeightOffset ("Height Offset (moves the cloud floor down/up, m)", Float) = 0

        [Header(Cloud Pattern)]
        [NoScaleOffset] _CloudTex ("Cloud Texture (grayscale, tileable)", 2D) = "gray" {}
        _CloudScale ("Large Layer World Size (m per tile)", Float) = 26
        _DetailScale ("Detail Layer World Size (m per tile)", Float) = 11
        _DetailWeight ("Detail Layer Weight", Range(0, 1)) = 0.4
        _WindA ("Large Layer Drift (xy m/s)", Vector) = (0.35, 0.12, 0, 0)
        _WindB ("Detail Layer Drift (xy m/s)", Vector) = (-0.2, 0.3, 0, 0)

        [Header(Toon Bands)]
        _MidThreshold ("Mid From", Range(0, 1)) = 0.42
        _TopThreshold ("Top From", Range(0, 1)) = 0.66
        _BandSharpness ("Band Sharpness", Range(1, 40)) = 14

        [Header(Near Buildings (Shore Field))]
        // Uses WaterShoreBaker's baked distance-to-land field (runtime only - baked once the level exists).
        [Toggle(_SHOREFIELD_BILLOW)] _ShoreBillow ("Use Shore Field (edge fade + billows)", Float) = 0
        _EdgeFadeDistance ("Edge Fade Distance (m) - clouds thin out this close to the buildings", Range(0.1, 12)) = 3
        _EdgeFadeOpacity ("Opacity Right At The Buildings (x band opacity)", Range(0, 1)) = 0
        _BillowDistance ("Billow Distance (m)", Range(0.1, 6)) = 1.6
        _BillowStrength ("Billow Strength (brighter clouds piled at the cliffs)", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-50" }

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
            // _local (not _local_fragment): the vertex stage writes the shore UV interpolant.
            #pragma shader_feature_local _SHOREFIELD_BILLOW
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_CloudTex); SAMPLER(sampler_CloudTex);

#if defined(_SHOREFIELD_BILLOW)
            // Published by WaterShoreBaker: R = distance to nearest land (0 at the coast .. 1 at max),
            // params xy = world centre XZ, z = world size, w = max distance in world units.
            TEXTURE2D(_ShoreField); SAMPLER(sampler_ShoreField);
            float4 _ShoreFieldParams;
#endif

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor;
                half4 _MidColor;
                half4 _TopColor;
                float _CloudScale;
                float _DetailScale;
                half _DetailWeight;
                float4 _WindA;
                float4 _WindB;
                half _MidThreshold;
                half _TopThreshold;
                half _BandSharpness;
                half _BillowDistance;
                half _BillowStrength;
                float _HeightOffset;
                half _DeepAlpha;
                half _MidAlpha;
                half _TopAlpha;
                half _EdgeFadeDistance;
                half _EdgeFadeOpacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 cloudUV : TEXCOORD0;   // xy = large layer, zw = detail layer
#if defined(_SHOREFIELD_BILLOW)
                float4 shore : TEXCOORD1;     // xy = shore field UV, z = field -> 0..1 over billow distance, w = field -> 0..1 over fade distance
#endif
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS.y += _HeightOffset;
                o.positionCS = TransformWorldToHClip(positionWS);

                float t = _Time.y;
                o.cloudUV.xy = (positionWS.xz + _WindA.xy * t) / max(_CloudScale, 0.01);
                // rotate the detail layer ~37 degrees so the two tilings never line up
                float2 r = float2(positionWS.x * 0.8 - positionWS.z * 0.6, positionWS.x * 0.6 + positionWS.z * 0.8);
                o.cloudUV.zw = (r + _WindB.xy * t) / max(_DetailScale, 0.01);

#if defined(_SHOREFIELD_BILLOW)
                o.shore.xy = (positionWS.xz - _ShoreFieldParams.xy) / _ShoreFieldParams.z + 0.5;
                o.shore.z = _ShoreFieldParams.w / max(_BillowDistance, 0.01);
                o.shore.w = _ShoreFieldParams.w / max(_EdgeFadeDistance, 0.01);
#endif
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, input.cloudUV.xy).r;
                half b = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, input.cloudUV.zw).r;
                half n = lerp(a, b, _DetailWeight);

#if defined(_SHOREFIELD_BILLOW)
                half field = SAMPLE_TEXTURE2D(_ShoreField, sampler_ShoreField, input.shore.xy).r;
                n += saturate(1.0h - field * (half)input.shore.z) * _BillowStrength;
                half edgeFade = lerp(_EdgeFadeOpacity, 1.0h, saturate(field * (half)input.shore.w));
#else
                half edgeFade = 1.0h;
#endif

                half mid = saturate((n - _MidThreshold) * _BandSharpness);
                half top = saturate((n - _TopThreshold) * _BandSharpness);
                half3 color = lerp(lerp(_DeepColor.rgb, _MidColor.rgb, mid), _TopColor.rgb, top);
                half alpha = lerp(lerp(_DeepAlpha, _MidAlpha, mid), _TopAlpha, top) * edgeFade;
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
