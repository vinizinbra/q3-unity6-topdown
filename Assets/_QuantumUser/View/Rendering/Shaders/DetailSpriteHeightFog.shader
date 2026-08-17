// Sprite-compatible companion to Project/Mobile Toon Modular Level - that shader is opaque,
// mesh-oriented (custom vertex-color-encoded wall/surface role, no alpha blending) and can't be
// assigned to a SpriteRenderer directly. This reimplements just its Height Fog block (same math,
// same _HeightFogColor/_HeightFogTopY/_HeightFogFalloff/_HeightFogStrength properties) on top of
// normal alpha-blended, vertex-color-tinted sprite rendering, so ChunkDetailScatter's procedural
// wall/ground sprites can fade into the same height fog the level geometry does. See
// EnvironmentManager.ApplyEnvironment, which keeps this shader's fog properties in sync with the
// level material's own.
Shader "Project/Detail Sprite Height Fog"
{
    Properties
    {
        [MainTexture] _MainTex ("Sprite Texture", 2D) = "white" {}

        [Header(Height Fog)]
        _HeightFogColor ("Height Fog Color", Color) = (0.55,0.62,0.7,1)
        _HeightFogTopY ("Height Fog Top Y", Float) = 0
        _HeightFogFalloff ("Height Fog Falloff", Float) = 4
        _HeightFogStrength ("Height Fog Strength", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _HeightFogColor;
            half _HeightFogStrength;
            float _HeightFogTopY, _HeightFogFalloff;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float2 uv:TEXCOORD1; half4 color:COLOR0; };
            V Vert(A i)
            {
                V o=(V)0;
                UNITY_SETUP_INSTANCE_ID(i);
                o.positionWS=TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS);
                o.uv=TRANSFORM_TEX(i.uv,_MainTex);
                o.color=i.color;
                return o;
            }
            half4 Frag(V i):SV_Target
            {
                half4 tex=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv);
                half4 color=tex*i.color;
                half heightFogDepth=max(_HeightFogTopY-i.positionWS.y,0);
                half heightFogFalloffSq=max(_HeightFogFalloff*_HeightFogFalloff,0.0001);
                half heightFog=1-exp(-(heightFogDepth*heightFogDepth)/heightFogFalloffSq);
                heightFog*=saturate(_HeightFogStrength*_HeightFogColor.a);
                color.rgb=lerp(color.rgb,_HeightFogColor.rgb,heightFog);
                return color;
            }
            ENDHLSL
        }
    }
}
