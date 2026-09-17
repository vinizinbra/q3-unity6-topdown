// Recolors a GRAYSCALE sprite (black -> white ramp, no hue of its own) from SpriteRenderer.color,
// read straight off the per-vertex COLOR stream - no material _TintColor, so every instance can carry
// its own color/intensity (e.g. ProjectileDataVisualsView.ApplySprite's `sprite.color = weaponData.
// ProjectileColor`) without a material swap or MaterialPropertyBlock.
//
// White pixels in the source texture stay pure white regardless of SpriteRenderer.color - only the
// darker tones pick up the tint, ramping via smoothstep toward black = SpriteRenderer.color.rgb
// exactly, white = (1,1,1) exactly, same "tint shadows, protect highlights" idea
// MobileParticleDuotoneRamp.shader uses for particles, adapted to the sprite-shader boilerplate
// SpriteColor Preserve Black already establishes in this file family.
//
// HDR CAVEAT: SpriteRenderer bakes .color into the sprite mesh's vertex COLOR stream, which Unity
// typically stores as Color32 (8-bit per channel) - a channel above 1.0 likely gets clamped to 1.0
// before it ever reaches this shader, regardless of what float value C# assigned. Verify in-Editor
// (Frame Debugger / a visibly-different HDR value) that intensity actually survives on this project's
// URP/2D Renderer setup. If it clamps, the usual fix is a MaterialPropertyBlock-driven [HDR] material
// property instead of vertex color - a different (per-renderer, not per-vertex) delivery mechanism.
Shader "Sprites/SpriteGrayscaleColorizeHDR"
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

                // Source is expected grayscale (R=G=B), but luminance-weighted in case it isn't
                // perfectly so - same weights the rest of this shader family already uses.
                half luminance = dot(source.rgb, half3(0.299h, 0.587h, 0.114h));
                half t = smoothstep(0.0h, 1.0h, luminance);

                // t=0 (black) -> input.color.rgb exactly; t=1 (white) -> (1,1,1) exactly.
                half3 outputColor = lerp(input.color.rgb, half3(1.0h, 1.0h, 1.0h), t);

                // Standard SpriteRenderer semantics: color.a is an opacity multiplier, not
                // repurposed as colorize strength the way SpriteColor Preserve Black's alpha is.
                return half4(outputColor, source.a * input.color.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
