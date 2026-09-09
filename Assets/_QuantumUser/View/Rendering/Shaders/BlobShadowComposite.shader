Shader "Hidden/BlobShadowComposite"
{
    Properties
    {
        _ShadowColor ("Shared Shadow Multiplier", Color) = (0.35, 0.35, 0.4, 1)
        [NoScaleOffset] _HatchMap ("Hatch Texture", 2D) = "white" {}
        _HatchScale ("Hatch Tiling", Float) = 0.5
        _HatchStrength ("Hatch Strength", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "MultiplyShadowMask"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend DstColor Zero
            ColorMask RGB

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_HatchMap);
            CBUFFER_START(UnityPerMaterial)
                half4 _ShadowColor;
                half _HatchStrength;
                float _HatchScale;
            CBUFFER_END

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half amount = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord).r;
                half3 multiplier = lerp(half3(1, 1, 1), _ShadowColor.rgb, amount);
                // Reconstruct the receiving surface only where hatching is actually visible.
                // Disabled hatching costs no depth/hatch samples or world-position reconstruction.
                [branch] if (_HatchStrength > 0.001h && amount > 0)
                {
                    float depth = SampleSceneDepth(input.texcoord);
                    #if !UNITY_REVERSED_Z
                        depth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, depth);
                    #endif
                    float3 positionWS = ComputeWorldSpacePosition(input.texcoord, depth, UNITY_MATRIX_I_VP);
                    half hatch = SAMPLE_TEXTURE2D(_HatchMap, sampler_LinearRepeat, positionWS.xz * _HatchScale).r;
                    multiplier *= lerp(1.0h, hatch, amount * _HatchStrength);
                }
                return half4(multiplier, 1);
            }
            ENDHLSL
        }
    }
}
