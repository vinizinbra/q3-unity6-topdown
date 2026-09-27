// Debug-only "cheapest possible" stand-in for ToonTerrain, swapped in at runtime by CheatMenu ->
// Rendering -> "Simple Terrain Shader" to measure how much of an area's GPU cost is that shader's
// per-pixel work (see docs/performance.md). Everything is computed per VERTEX (wrapped Lambert from
// the main light + flat ambient); the fragment only returns the interpolated colour. Colour = the
// material's _BaseColor, or the vertex colour where alpha = 1 (ToonTerrain's "isProp" convention).
// Loaded through Resources/Debug/DebugSimpleVertexLit.mat so it ships in builds.
Shader "RiftRaiders/Debug/SimpleVertexLit"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.6, 0.6, 0.6, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 color : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);

                half3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                Light mainLight = GetMainLight();
                half wrapped = saturate(dot(normalWS, mainLight.direction)) * 0.6h + 0.4h;
                half3 albedo = lerp(_BaseColor.rgb, input.color.rgb, input.color.a);
                o.color = albedo * wrapped * mainLight.color;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return half4(input.color, 1.0h);
            }
            ENDHLSL
        }
    }
}
