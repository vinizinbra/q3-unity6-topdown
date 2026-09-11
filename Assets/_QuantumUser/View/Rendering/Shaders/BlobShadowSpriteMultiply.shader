// Cheap per-sprite blob shadow: one ordinary transparent multiply pass (Blend DstColor Zero),
// no renderer feature, no fullscreen composite, no depth-texture request. Occlusion behind
// opaque foreground geometry comes for free from the normal hardware depth test (ZTest LEqual
// against the camera's own depth attachment) instead of a manually sampled depth texture.
// Used by GroundBlobManager, BuildingShadowManager and PlayerShadow, including 9-sliced sprites.
// Overlapping shadows compound (multiply on top of multiply) rather than unioning to one shared
// darkness - the trade accepted for dropping the MAX-mask/composite renderer feature on mobile.
Shader "Custom/BlobShadowSpriteMultiply"
{
    Properties
    {
        _MainTex ("Shape Texture (see _ShapeSource)", 2D) = "white" {}
        _ShadowColor ("Shadow Color", Color) = (0.35, 0.35, 0.4, 1)
        _MinShadowAmount ("Optional Contribution Cutoff", Range(0, 0.5)) = 0
        _Strength ("Strength", Range(0, 1)) = 1

        [Header(Shape)]
        _ShapeSource ("Shape Source (0 = RGB hard, 1 = Alpha soft)", Range(0, 1)) = 0
        _ShapeCutout ("Cutout Amount (0 = as sampled, 1 = hard)", Range(0, 1)) = 0
        _ShapeThreshold ("Cutout Threshold", Range(0, 1)) = 0.5

        [Header(Shadow Hatching)]
        [NoScaleOffset] _HatchMap ("Hatch Texture", 2D) = "white" {}
        _HatchScale ("Hatch Tiling (per world unit)", Float) = 0.5
        _HatchStrength ("Hatch Strength", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off
        Cull Off

        Pass
        {
            Name "ForwardMultiply"
            Tags { "LightMode" = "UniversalForward" }
            Blend DstColor Zero
            ColorMask RGB

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_HatchMap);

            // Sprite alpha can arrive in batched vertices, unity_SpriteColor, or the
            // instancing buffer. Multiply both sources to preserve height fading in each path.
            #ifdef UNITY_INSTANCING_ENABLED
                UNITY_INSTANCING_BUFFER_START(PerDrawSprite)
                    UNITY_DEFINE_INSTANCED_PROP(float4, unity_SpriteRendererColorArray)
                UNITY_INSTANCING_BUFFER_END(PerDrawSprite)
                #define BLOB_SPRITE_COLOR UNITY_ACCESS_INSTANCED_PROP(PerDrawSprite, unity_SpriteRendererColorArray)
            #else
                #define BLOB_SPRITE_COLOR unity_SpriteColor
            #endif

            CBUFFER_START(UnityPerMaterial)
                half4 _ShadowColor;
                half _MinShadowAmount;
                half _Strength;
                half _ShapeSource;
                half _ShapeCutout;
                half _ShapeThreshold;
                half _HatchStrength;
                float _HatchScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 positionWSxz : TEXCOORD1;
                half alpha : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWSxz = positionWS.xz;
                output.uv = input.uv;
                // Resolved here, not in the fragment: it is uniform across the quad, so the
                // instanced-property access happens once per vertex instead of once per pixel.
                half4 spriteColor = (half4)BLOB_SPRITE_COLOR;
                output.alpha = input.color.a * spriteColor.a;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 shapeTexel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half shape = lerp(shapeTexel.r, shapeTexel.a, _ShapeSource);
                half hardShape = step(_ShapeThreshold, shape);
                half amount = saturate(lerp(shape, hardShape, _ShapeCutout) * input.alpha * _Strength);
                clip(amount - _MinShadowAmount);

                half3 multiplier = lerp(half3(1, 1, 1), _ShadowColor.rgb, amount);
                // World position comes from this sprite's own quad, already lying on the
                // ground plane - no depth sample/reconstruction needed like the fullscreen
                // composite version required.
                if (_HatchStrength > 0.001h)
                {
                    half hatch = SAMPLE_TEXTURE2D(_HatchMap, sampler_LinearRepeat, input.positionWSxz * _HatchScale).r;
                    multiplier *= lerp(1.0h, hatch, amount * _HatchStrength);
                }
                return half4(multiplier, 1);
            }
            ENDHLSL
        }
    }
}
