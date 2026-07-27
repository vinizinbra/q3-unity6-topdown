// Same multiply-blend idea as Custom/BlobShadowMultiply, but for a 9-sliced SpriteRenderer
// instead of a procedural quad: the falloff shape (soft rounded-rect edge) has to live in a
// texture so Unity's sprite slicing can keep that edge a fixed pixel width while the middle
// stretches to fit any block size. _MainTex only carries the shadow's shape (R channel = 0..1
// shadow amount) - the actual tint comes from _ShadowColor so instances can share one texture.
Shader "Custom/BlobShadowSpriteMultiply"
{
    Properties
    {
        _MainTex ("Falloff Texture (R = shadow amount)", 2D) = "black" {}
        _ShadowColor ("Shadow Color", Color) = (0.35, 0.35, 0.4, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Blend DstColor Zero
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _ShadowColor;
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half amount = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).r;
                half3 tint = lerp(half3(1.0h, 1.0h, 1.0h), _ShadowColor.rgb, amount);
                return half4(tint, 1.0h);
            }
            ENDHLSL
        }
    }
}
