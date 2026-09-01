// Minimal URP mesh-particle shader: one texture sample tinted by vertex color, lit by a cheap
// per-VERTEX main-light + ambient term (no per-pixel lighting, no additional lights, no shadows).
// The main-light term is quantized into a hard lit/shadow band (same _LightThreshold/_BandSoftness
// idiom as Project/Mobile Toon Modular Level) for a toon look instead of a smooth gradient.
Shader "Project/Mobile Particle Vertex Color"
{
    Properties
    {
        [MainTexture] _MainTex ("Texture", 2D) = "white" {}
        [MainColor] _TintColor ("Tint Color", Color) = (1, 1, 1, 1)

        [Header(Toon Shading)]
        _LightThreshold ("Light Threshold", Range(0, 1)) = 0.5
        _BandSoftness ("Band Softness", Range(0.001, 0.5)) = 0.05
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 1

        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5 // SrcAlpha
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10 // OneMinusSrcAlpha
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend [_SrcBlend] [_DstBlend]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _TintColor;
                half _LightThreshold;
                half _BandSoftness;
                half _ShadowStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : TEXCOORD1;
                half3 lighting : TEXCOORD2;
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

                // Cheap per-vertex toon lighting: main light N.L (no shadows/extra lights) quantized
                // into a hard lit/shadow band, plus SH ambient.
                half3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                Light mainLight = GetMainLight();
                half NdotL = saturate(dot(normalWS, mainLight.direction));
                half lit = smoothstep(_LightThreshold - _BandSoftness, _LightThreshold + _BandSoftness, NdotL);
                half3 ambient = SampleSH(normalWS);
                // _ShadowStrength dials the dark band from full main-light (0, shadow invisible)
                // down to ambient-only (1, darkest) - the lit band always gets the full main light.
                half3 litTone = ambient + mainLight.color;
                half3 shadowTone = ambient + mainLight.color * (1 - _ShadowStrength);
                output.lighting = lerp(shadowTone, litTone, lit);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 albedo = tex * _TintColor * input.color;
                return half4(albedo.rgb * input.lighting, albedo.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
