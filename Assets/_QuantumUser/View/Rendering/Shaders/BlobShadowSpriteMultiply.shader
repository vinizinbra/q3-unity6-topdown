// Used by GroundBlobManager and BuildingShadowManager, including 9-sliced sprites.
// BlobShadowRendererFeature reduces raw amounts with MAX, then multiplies the scene once
// using the shared material's color and world-space hatching. No ordinary transparent pass may also draw these sprites.
Shader "Custom/BlobShadowSpriteMultiply"
{
    Properties
    {
        _MainTex ("Shape Texture (see _ShapeSource)", 2D) = "white" {}
        _ShadowColor ("Shared Shadow Color (Renderer Feature style)", Color) = (0.35, 0.35, 0.4, 1)
        _MinShadowAmount ("Optional Contribution Cutoff", Range(0, 0.5)) = 0
        _Strength ("Strength", Range(0, 1)) = 1

        [Header(Shape)]
        _ShapeSource ("Shape Source (0 = RGB hard, 1 = Alpha soft)", Range(0, 1)) = 0
        _ShapeCutout ("Cutout Amount (0 = as sampled, 1 = hard)", Range(0, 1)) = 0
        _ShapeThreshold ("Cutout Threshold", Range(0, 1)) = 0.5

        [Header(Shared Shadow Hatching)]
        [NoScaleOffset] _HatchMap ("Hatch Texture", 2D) = "white" {}
        _HatchScale ("Hatch Tiling (per world unit)", Float) = 0.5
        _HatchStrength ("Hatch Strength", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off
        ZTest Always // Visibility uses scene depth, also with MSAA or a reduced-resolution mask.
        Cull Off

        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float4 _BlobShadowMaskSize;

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

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

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
                output.uv = input.uv;
                // Resolved here, not in the fragment: it is uniform across the quad, so the
                // instanced-property access happens once per vertex instead of once per pixel.
                half4 spriteColor = (half4)BLOB_SPRITE_COLOR;
                output.alpha = input.color.a * spriteColor.a;
                return output;
            }


            // Shape and height fade remain per sprite; color/hatching are shared at composite.
            float ShadowAmount(Varyings input)
            {
                float2 screenUv = input.positionCS.xy * _BlobShadowMaskSize.zw;
                float sceneDepth = SampleSceneDepth(screenUv);
                #if UNITY_REVERSED_Z
                    clip(input.positionCS.z - sceneDepth);
                #else
                    clip(sceneDepth - input.positionCS.z);
                #endif

                half4 shapeTexel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half shape = lerp(shapeTexel.r, shapeTexel.a, _ShapeSource);
                half hardShape = step(_ShapeThreshold, shape);
                float amount = saturate(lerp(shape, hardShape, _ShapeCutout) * input.alpha * _Strength);
                clip(amount - _MinShadowAmount);
                return amount; // The R8 target supplies normal UNorm quantization.
            }

            half4 FragMask(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float amount = ShadowAmount(input);
                return half4(amount, 0, 0, 0);
            }

        ENDHLSL

        Pass
        {
            Name "BlobShadowMask"
            Tags { "LightMode" = "BlobShadowMask" }
            Blend One One
            BlendOp Max
            ColorMask R
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragMask
            #pragma multi_compile_instancing
            ENDHLSL
        }

    }
}
