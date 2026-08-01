Shader "Sprites/Sprite Inner Glow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}

        [Header(Inner Glow)]
        [HDR] _GlowColor ("Glow Color", Color) = (0.2, 0.8, 1, 1)
        _GlowWidth ("Glow Width (Pixels)", Range(0, 16)) = 3
        _GlowSoftness ("Glow Softness", Range(0.25, 4)) = 1
        _GlowIntensity ("Glow Intensity", Range(0, 8)) = 1
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
                float4 _MainTex_TexelSize;
                half4 _GlowColor;
                half _GlowWidth;
                half _GlowSoftness;
                half _GlowIntensity;
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

            half SampleAlpha(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 sprite = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                // SpriteRenderer.color remains the normal base tint/opacity override.
                half4 baseColor = sprite * input.color;

                float2 radius = _MainTex_TexelSize.xy * _GlowWidth;
                float2 diagonalRadius = radius * 0.70710678;

                // The lowest surrounding alpha approximates proximity to an inside edge.
                half surroundingAlpha = 1.0h;
                surroundingAlpha = min(surroundingAlpha, SampleAlpha(input.uv + float2( radius.x, 0)));
                surroundingAlpha = min(surroundingAlpha, SampleAlpha(input.uv + float2(-radius.x, 0)));
                surroundingAlpha = min(surroundingAlpha, SampleAlpha(input.uv + float2(0,  radius.y)));
                surroundingAlpha = min(surroundingAlpha, SampleAlpha(input.uv + float2(0, -radius.y)));
                surroundingAlpha = min(surroundingAlpha, SampleAlpha(input.uv + float2( diagonalRadius.x,  diagonalRadius.y)));
                surroundingAlpha = min(surroundingAlpha, SampleAlpha(input.uv + float2(-diagonalRadius.x,  diagonalRadius.y)));
                surroundingAlpha = min(surroundingAlpha, SampleAlpha(input.uv + float2( diagonalRadius.x, -diagonalRadius.y)));
                surroundingAlpha = min(surroundingAlpha, SampleAlpha(input.uv + float2(-diagonalRadius.x, -diagonalRadius.y)));

                half innerEdge = sprite.a * (1.0h - surroundingAlpha) * step(0.001h, _GlowWidth);
                half glowMask = pow(saturate(innerEdge), _GlowSoftness);
                half glowAmount = glowMask * _GlowColor.a * _GlowIntensity;

                // Glow is additive inside the silhouette and follows SpriteRenderer opacity.
                half3 finalRgb = baseColor.rgb + _GlowColor.rgb * glowAmount;
                return half4(finalRgb, baseColor.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
