// Additive variant of SpriteGrayscaleColorizeHDR - same "black -> SpriteRenderer.color, white ->
// white" ramp, but Blend One One instead of alpha blending, for a glow/energy sprite meant to brighten
// what's behind it rather than composite over it. Alpha is premultiplied into the output color here
// (Blend One One never multiplies by src alpha itself, unlike SrcAlpha blending) so a low-alpha pixel
// still adds proportionally less light instead of its full brightness. See SpriteGrayscaleColorizeHDR
// for the HDR-via-vertex-color caveat - same applies here.
Shader "Sprites/SpriteGrayscaleColorizeHDRAdditive"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
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
        Blend One One

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

                half luminance = dot(source.rgb, half3(0.299h, 0.587h, 0.114h));
                half t = smoothstep(0.0h, 1.0h, luminance);

                half3 outputColor = lerp(input.color.rgb, half3(1.0h, 1.0h, 1.0h), t);
                half alpha = source.a * input.color.a;

                // Premultiplied: Blend One One has no dst-side alpha multiply of its own.
                return half4(outputColor * alpha, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
