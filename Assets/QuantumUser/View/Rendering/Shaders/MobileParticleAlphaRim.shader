// Alpha-blended mobile particle shader (texture * tint * vertex color) plus an HDR fresnel rim light
// and an optional subtle vertex wobble for "living energy" blobs.
// Needs real per-vertex normals with curvature (Mesh particle render mode, e.g. orbs/rings) - on a
// camera-facing billboard quad the normal always points straight at the viewer, so dot(N,V) is always
// ~1 and the rim never shows.
Shader "Project/Mobile Particle Alpha Rim"
{
    Properties
    {
        [MainTexture] _MainTex ("Texture", 2D) = "white" {}
        [MainColor] _TintColor ("Tint Color", Color) = (1, 1, 1, 1)

        [Header(HDR Rim Light)]
        [HDR] _RimColor ("Rim Color", Color) = (1, 1, 1, 1)
        _RimPower ("Rim Falloff", Range(0.25, 32)) = 3
        _RimThreshold ("Rim Threshold", Range(0, 1)) = 0
        _RimSoftness ("Rim Softness", Range(0.001, 1)) = 1
        _RimIntensity ("Rim Intensity", Range(0, 10)) = 1
        _RimOpacity ("Rim Opacity", Range(0, 1)) = 1

        [Header(Local Energy Animation)]
        _BlobAmplitude ("Blob Amplitude", Range(0, 0.5)) = 0
        _BlobFrequency ("Blob Frequency", Range(0.1, 10)) = 2
        _BlobSpeed ("Blob Speed", Range(0, 10)) = 1
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
                half _BlobAmplitude;
                half _BlobFrequency;
                half _BlobSpeed;
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
                float3 normalWS : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // Cheap "living blob" wobble: sum of 3 sines over object-space position, no noise
                // texture sample needed. Amplitude defaults to 0 so it's a no-op on other materials.
                float t = _Time.y * _BlobSpeed;
                float3 p = input.positionOS.xyz * _BlobFrequency;
                half wobble = (sin(p.x + t) + sin(p.y * 1.3 - t * 0.7) + sin(p.z * 0.8 + t * 1.2)) * 0.3333h;
                float3 positionOS = input.positionOS.xyz + input.normalOS * wobble * _BlobAmplitude;

                VertexPositionInputs positionInputs = GetVertexPositionInputs(positionOS);
                output.positionWS = positionInputs.positionWS;
                output.positionCS = positionInputs.positionCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 albedo = tex * _TintColor * input.color;

                // Rim fades with the sprite's shape mask and the particle's own lifetime alpha,
                // but not _TintColor.a - that's a separate authoring knob for base opacity only.
                half rimMask = tex.a * input.color.a;

                float3 normalWS = normalize(input.normalWS);
                half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half fresnel = pow(1.0h - saturate(dot(normalWS, viewDirWS)), _RimPower);
                half rimBand = smoothstep(_RimThreshold, _RimThreshold + _RimSoftness, fresnel);
                half3 rim = _RimColor.rgb * rimBand * _RimIntensity * rimMask;

                // The rim band also pushes alpha toward opaque, so the edge reads as a solid
                // outline even where the body itself (albedo.a) is mostly see-through.
                half alpha = max(albedo.a, rimBand * _RimOpacity * rimMask);

                half3 color = albedo.rgb + rim;
                color = MixFog(color, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
