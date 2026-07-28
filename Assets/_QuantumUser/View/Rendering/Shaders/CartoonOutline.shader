// Screen-space depth+normal edge detection outline.
//
// Because edges are found from depth/normal discontinuities in the final image rather than
// from individual mesh silhouettes, two coplanar objects that sit flush against each other
// (e.g. two 1x1x1 cubes placed side by side) read as one continuous surface: there is no
// depth or normal jump at the seam, so no outline is drawn there. Only real screen-space
// edges (silhouettes and creases) produce a line, which is what makes adjacent cubes read
// as a single 1x2 block instead of two individually outlined cubes.
Shader "Hidden/Custom/CartoonOutline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (0, 0, 0, 1)
        _DepthThreshold ("Depth Threshold", Range(0.0001, 0.05)) = 0.003
        _NormalThreshold ("Normal Threshold", Range(0, 2)) = 0.4
        _EdgeThickness ("Edge Thickness (px)", Range(0.5, 4)) = 1.0
        _EdgeSoftness ("Edge Softness (Rounding)", Range(0, 2)) = 0.3
        _GrazingAngleBias ("Grazing Angle Bias", Range(0, 1)) = 0.2
        _GrazingAngleBiasScale ("Grazing Angle Bias Scale", Range(1, 10)) = 5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off

        Pass
        {
            Name "CartoonOutlineEdgeDetect"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            half4 _OutlineColor;
            float _DepthThreshold;
            float _NormalThreshold;
            float _EdgeThickness;
            float _EdgeSoftness;
            float _GrazingAngleBias;
            float _GrazingAngleBiasScale;
            // xy = 1/width, 1/height ; zw = width, height. Set from C# instead of relying on
            // an auto-generated _BlitTexture_TexelSize so the effect is robust to how the
            // source handle is bound.
            float4 _SourceTexelSize;

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 texel = _SourceTexelSize.xy * _EdgeThickness;

                float2 uvTL = uv + float2(-texel.x, texel.y);
                float2 uvTR = uv + float2(texel.x, texel.y);
                float2 uvBL = uv + float2(-texel.x, -texel.y);
                float2 uvBR = uv + float2(texel.x, -texel.y);

                float rawDepthC = SampleSceneDepth(uv);
                float rawDepthTL = SampleSceneDepth(uvTL);
                float rawDepthTR = SampleSceneDepth(uvTR);
                float rawDepthBL = SampleSceneDepth(uvBL);
                float rawDepthBR = SampleSceneDepth(uvBR);

                float eyeDepthC = LinearEyeDepth(rawDepthC, _ZBufferParams);
                float eyeDepthTL = LinearEyeDepth(rawDepthTL, _ZBufferParams);
                float eyeDepthTR = LinearEyeDepth(rawDepthTR, _ZBufferParams);
                float eyeDepthBL = LinearEyeDepth(rawDepthBL, _ZBufferParams);
                float eyeDepthBR = LinearEyeDepth(rawDepthBR, _ZBufferParams);

                float depthDiagA = eyeDepthTR - eyeDepthBL;
                float depthDiagB = eyeDepthTL - eyeDepthBR;
                // Normalise by distance so the threshold reads the same in screen-space
                // regardless of how far the surface is from the camera.
                float depthEdge = sqrt(depthDiagA * depthDiagA + depthDiagB * depthDiagB) / max(eyeDepthC, 0.0001);

                float3 normalC = SampleSceneNormals(uv);
                float3 normalTL = SampleSceneNormals(uvTL);
                float3 normalTR = SampleSceneNormals(uvTR);
                float3 normalBL = SampleSceneNormals(uvBL);
                float3 normalBR = SampleSceneNormals(uvBR);

                float3 normalDiagA = normalTR - normalBL;
                float3 normalDiagB = normalTL - normalBR;
                float normalEdge = sqrt(dot(normalDiagA, normalDiagA) + dot(normalDiagB, normalDiagB));

                // Relax the depth threshold on surfaces seen edge-on (e.g. a floor stretching
                // toward the horizon) so their natural depth gradient isn't drawn as an outline.
                float3 worldPos = ComputeWorldSpacePosition(uv, rawDepthC, UNITY_MATRIX_I_VP);
                float3 viewDir = normalize(GetCameraPositionWS() - worldPos);
                float nDotV = saturate(dot(normalC, viewDir));
                float grazing = saturate((_GrazingAngleBias - nDotV) * _GrazingAngleBiasScale);
                float depthThreshold = _DepthThreshold * (1.0 + grazing * 8.0);

                // smoothstep instead of a hard step() so the line's antialiased edge softens
                // pixel-stair-stepping and rounds off what would otherwise be razor-sharp
                // corners at silhouette direction changes.
                float depthSoftWidth = max(depthThreshold, 1e-5) * _EdgeSoftness;
                float normalSoftWidth = max(_NormalThreshold, 1e-5) * _EdgeSoftness;
                float isDepthEdge = smoothstep(depthThreshold, depthThreshold + depthSoftWidth, depthEdge);
                float isNormalEdge = smoothstep(_NormalThreshold, _NormalThreshold + normalSoftWidth, normalEdge);
                float edge = max(isDepthEdge, isNormalEdge);

                // Point sampling: source and destination are the same resolution, so there is
                // nothing to filter and this avoids paying for bilinear taps on every pixel.
                half4 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);
                return lerp(sceneColor, half4(_OutlineColor.rgb, sceneColor.a), edge * _OutlineColor.a);
            }
            ENDHLSL
        }
    }
}
