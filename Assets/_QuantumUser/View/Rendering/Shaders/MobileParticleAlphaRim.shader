// Cheap URP mesh-particle shader: one texture sample and a vertex-evaluated Fresnel rim.
// Intended for Particle System Render Mode = Mesh. The mesh must have useful normals.
Shader "Project/Mobile Particle Alpha Rim"
{
    Properties
    {
        [MainTexture] _MainTex ("Texture", 2D) = "white" {}
        [MainColor] _TintColor ("Tint Color", Color) = (1, 1, 1, 1)

        [Header(HDR Rim Light)]
        [HDR] _RimColor ("Rim Color", Color) = (1, 1, 1, 1)
        _RimPower ("Rim Falloff", Range(0.25, 8)) = 3
        _RimThreshold ("Rim Width", Range(0, 1)) = 0
        _RimSoftness ("Rim Softness", Range(0.001, 1)) = 1
        _RimIntensity ("Rim Intensity", Range(0, 10)) = 1
        _RimOpacity ("Rim Opacity", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _TintColor;
                half4 _RimColor;
                half _RimPower;
                half _RimThreshold;
                half _RimSoftness;
                half _RimIntensity;
                half _RimOpacity;
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
                half rim : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);

                // Do the view/normal work per vertex rather than for every covered pixel.
                half3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                half3 viewDirWS = GetWorldSpaceNormalizeViewDir(positionInputs.positionWS);
                half fresnel = 1.0h - saturate(dot(normalWS, viewDirWS));
                output.rim = pow(fresnel, _RimPower);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 albedo = tex * _TintColor * input.color;

                // Texture alpha shapes the mesh; vertex alpha supports Color over Lifetime.
                half rimMask = tex.a * input.color.a;
                half rimBand = smoothstep(_RimThreshold, _RimThreshold + _RimSoftness, input.rim);
                // The rim band replaces both the tinted RGB and its alpha. Rim color alpha
                // controls transparency only, so a transparent rim can still keep its exact hue.
                half rimAlpha = _RimColor.a * rimMask * _RimOpacity;
                half3 rimColor = _RimColor.rgb * _RimIntensity;
                half3 color = lerp(albedo.rgb, rimColor, rimBand);
                half alpha = lerp(albedo.a, rimAlpha, rimBand);

                color = MixFog(color, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
