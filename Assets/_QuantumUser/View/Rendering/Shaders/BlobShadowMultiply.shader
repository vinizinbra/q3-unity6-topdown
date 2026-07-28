// Cheapest possible blob shadow: a small flat quad (built procedurally by PlayerShadow, no
// authored sprite/texture) that darkens whatever is under it via multiplicative blending.
// `Blend DstColor Zero` multiplies the framebuffer by our output color instead of compositing
// with an alpha channel, so there's no transparency sorting to get right between overlapping
// shadow blobs or other transparent effects - draw order simply doesn't matter for the result.
Shader "Custom/BlobShadowMultiply"
{
    Properties
    {
        _ShadowColor ("Shadow Color", Color) = (0.35, 0.35, 0.4, 1)
        _Softness ("Edge Softness", Range(0.01, 1)) = 0.4
        _Strength ("Strength", Range(0, 1)) = 1
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

            CBUFFER_START(UnityPerMaterial)
                half4 _ShadowColor;
                half _Softness;
                half _Strength;
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

            // Soft circle inscribed in the quad's UV space, faded from full darkness at the
            // center to none at the edge - no texture sample needed.
            half4 Frag(Varyings input) : SV_Target
            {
                half dist = length(input.uv - 0.5h) * 2.0h;
                half falloff = 1.0h - smoothstep(1.0h - _Softness, 1.0h, dist);
                half darkness = falloff * _Strength;
                half3 tint = lerp(half3(1.0h, 1.0h, 1.0h), _ShadowColor.rgb, darkness);
                return half4(tint, 1.0h);
            }
            ENDHLSL
        }
    }
}
