// Lightweight visible cloud layer for a horizontal plane above the level.
// Uses two scrolling samples from one grayscale texture; no depth texture,
// lighting, particle system, or camera setup is required.
Shader "Project/Cloud Scrolling"
{
    Properties
    {
        [MainTexture] _CloudMap ("Cloud Texture", 2D) = "gray" {}
        [MainColor] _CloudColor ("Cloud Color A", Color) = (0.9, 0.95, 1, 0.32)
        _ShadowColor ("Cloud Color B", Color) = (0.48, 0.58, 0.7, 0.2)

        [Header(Shape)]
        _CloudScale ("Cloud Size", Range(0.1, 8)) = 1
        _Coverage ("Cloud Coverage", Range(0.05, 0.95)) = 0.5
        _Softness ("Edge Softness", Range(0.01, 0.4)) = 0.12
        _LayerBlend ("Second Layer Blend", Range(0, 1)) = 0.45

        [Header(Animation)]
        _DirectionA ("Primary Direction", Vector) = (0.018, 0.008, 0, 0)
        _DirectionB ("Secondary Direction", Vector) = (-0.009, 0.014, 0, 0)

        [Header(Plane Edge Fade)]
        _EdgeFade ("Edge Fade", Range(0.001, 0.5)) = 0.12
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+20"
        }

        Pass
        {
            Name "CloudLayer"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_CloudMap);
            SAMPLER(sampler_CloudMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _CloudMap_ST;
                half4 _CloudColor;
                half4 _ShadowColor;
                half _CloudScale;
                half _Coverage;
                half _Softness;
                half _LayerBlend;
                half _EdgeFade;
                float4 _DirectionA;
                float4 _DirectionB;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // World projection keeps motion coherent if the plane is moved.
                float2 baseUV = input.positionWS.xz * (0.02 * _CloudScale);
                half cloudA = SAMPLE_TEXTURE2D(_CloudMap, sampler_CloudMap,
                                               baseUV + _Time.y * _DirectionA.xy).r;
                half cloudB = SAMPLE_TEXTURE2D(_CloudMap, sampler_CloudMap,
                                               baseUV * 1.73 + _Time.y * _DirectionB.xy).r;
                half density = lerp(cloudA, cloudA * cloudB * 1.35h, _LayerBlend);
                half shape = smoothstep(_Coverage - _Softness,
                                        _Coverage + _Softness, density);

                // Prevent a visible rectangular border on a finite plane.
                half2 edgeDistance = min(input.uv, 1.0h - input.uv);
                half edgeMask = smoothstep(0.0h, max(_EdgeFade, 0.001h),
                                           min(edgeDistance.x, edgeDistance.y));
                shape *= edgeMask;

                half3 color = lerp(_ShadowColor.rgb, _CloudColor.rgb, density);
                half alpha = shape * lerp(_ShadowColor.a, _CloudColor.a, density);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
