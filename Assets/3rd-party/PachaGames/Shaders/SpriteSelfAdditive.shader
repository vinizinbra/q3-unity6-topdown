// Reproduces a normal sprite plus an identical additive sprite in one draw.
// SpriteRenderer.color.a controls the virtual additive copy's intensity.
Shader "Sprites/Sprite Self Additive"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
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
        // Premultiplied output lets one pass reproduce:
        // normal base sprite + an additive copy of that same sprite.
        Blend One OneMinusSrcAlpha

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
                float2 spriteUV : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.spriteUV = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 baseSprite = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.spriteUV);
                baseSprite.rgb *= input.color.rgb;

                // Alpha 0 = ordinary premultiplied sprite.
                // Alpha 1 = ordinary sprite plus one identical additive copy.
                half additiveStrength = saturate(input.color.a);
                half3 premultipliedBase = baseSprite.rgb * baseSprite.a;
                half3 outputColor = premultipliedBase * (1.0h + additiveStrength);
                return half4(outputColor, baseSprite.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
