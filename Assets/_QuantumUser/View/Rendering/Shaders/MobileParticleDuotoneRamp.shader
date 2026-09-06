// Duotone particle shader: the _MainTex is read as a grayscale ramp (black -> white). Black pixels
// are replaced by the particle color (vertex color from the Particle System's Start/Color-over-
// Lifetime modules, times the material's own _TintColor), white pixels stay pure white, and anything
// in between linearly ramps between the two - so recoloring a particle system only ever tints its
// shadows/dark detail, never its highlights. _RampStart/_RampEnd remap the 0-1 luminance range before
// the ramp (Photoshop-levels style) in case the source texture's blacks/whites aren't pure 0/1.
Shader "Project/Mobile Particle Duotone Ramp"
{
    Properties
    {
        [MainTexture] _MainTex ("Grayscale Ramp Texture", 2D) = "white" {}
        [MainColor] _TintColor ("Tint Color", Color) = (1, 1, 1, 1)

        [Header(Ramp)]
        _RampStart ("Ramp Start (Black -> Color)", Range(0, 1)) = 0
        _RampEnd ("Ramp End (White -> White)", Range(0, 1)) = 1
        [Toggle] _InvertRamp ("Invert Ramp (White -> Color instead)", Float) = 0

        [Header(Blending)]
        [Toggle] _Additive ("Additive", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest LEqual
            // Fixed premultiplied-alpha blend state - the _Additive toggle switches modes at the
            // shader level instead (zeroing the output alpha), so no second pass/variant is needed.
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _TintColor;
                half _RampStart;
                half _RampEnd;
                half _InvertRamp;
                half _Additive;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : TEXCOORD1;
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
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half luminance = dot(tex.rgb, half3(0.299h, 0.587h, 0.114h));
                half ramp = saturate((luminance - _RampStart) / max(_RampEnd - _RampStart, 0.0001h));
                ramp = lerp(ramp, 1.0h - ramp, _InvertRamp);

                half3 particleColor = input.color.rgb * _TintColor.rgb;
                half3 outputColor = lerp(particleColor, half3(1, 1, 1), ramp);
                half coverage = tex.a * input.color.a;

                // Premultiplied alpha blend (Blend One OneMinusSrcAlpha) reproduces both a normal
                // alpha blend AND additive from the same fixed blend state: RGB is always
                // premultiplied by coverage, and zeroing the output alpha for Additive stops the
                // destination from being knocked back at all, so color only ever adds.
                half3 premultiplied = outputColor * coverage;
                half outAlpha = lerp(coverage, 0.0h, _Additive);

                return half4(premultiplied, outAlpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
