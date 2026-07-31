// SpriteColor variant that protects black outlines and other dark details.
// SpriteRenderer.color.rgb is the replacement color and its alpha is strength.
Shader "Sprites/SpriteColor Preserve Black"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _BlackThreshold ("Black Protection Threshold", Range(0, 1)) = 0.18
        _ThresholdSoftness ("Threshold Softness", Range(0.001, 0.25)) = 0.04
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
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half _BlackThreshold;
                half _ThresholdSoftness;
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

            half4 Frag(Varyings input) : SV_Target
            {
                half4 source = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                // Perceptual luminance distinguishes black strokes from colored
                // artwork. Softness preserves antialiased stroke edges.
                half luminance = dot(source.rgb, half3(0.299h, 0.587h, 0.114h));
                half protectBlack = smoothstep(
                    _BlackThreshold - _ThresholdSoftness,
                    _BlackThreshold + _ThresholdSoftness,
                    luminance
                );

                half colorStrength = saturate(input.color.a) * protectBlack;
                half3 outputColor = lerp(source.rgb, input.color.rgb, colorStrength);
                return half4(outputColor, source.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
