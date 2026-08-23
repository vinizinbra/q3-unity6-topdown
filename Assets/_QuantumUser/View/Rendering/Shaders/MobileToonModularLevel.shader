Shader "Project/Mobile Toon Modular Level"
{
    Properties
    {
        [Header(Textures)]
        [MainTexture] _WallMap ("Wall Texture", 2D) = "white" {}
        _SurfaceMap ("Surface Texture", 2D) = "white" {}
        [Toggle] _SurfaceUseWorldUV ("Surface World UV", Float) = 0
        [NoScaleOffset] _StyleMask ("Outline and Surface Fade Atlas", 2D) = "white" {}

        [Header(Surface World Noise)]
        [NoScaleOffset] _GlobalNoiseMap ("Surface Noise Texture", 2D) = "gray" {}
        _GlobalNoiseScale ("Noise Scale", Float) = 0.2
        _GlobalNoiseStrength ("Noise Strength", Range(0,1)) = 0.15
        _GlobalNoiseDarkColor ("Noise Dark Color", Color) = (0.72,0.75,0.78,1)
        _GlobalNoiseLightColor ("Noise Light Color", Color) = (1,1,1,1)
        _GlobalNoiseOffset ("Noise World Offset", Vector) = (0,0,0,0)

        [Header(Wall)]
        _WallColor ("Wall Color", Color) = (1,1,1,1)
        _InkColor ("Outline Color", Color) = (0.025,0.02,0.03,1)
        _WallOuterGlowStrength ("Outline Fade", Range(0,1)) = 0.3

        [Header(World Height Wall Line)]
        _WallLineColor ("Line Color", Color) = (0.025,0.02,0.03,1)
        _WallLineY ("World Y", Float) = 0
        _WallLineThickness ("Thickness", Float) = 0.1
        _WallLineStrength ("Strength", Range(0,1)) = 0

        [Header(Surface)]
        _SurfaceColor ("Surface Color", Color) = (1,1,1,1)
        _SurfaceEdgeColor ("Surface Fade Color", Color) = (0.22,0.16,0.1,0.55)
        _SurfaceFadeSteps ("Fade Steps", Range(1,6)) = 3
        _SurfaceFadeQuantize ("Fade Hardness", Range(0,1)) = 1
        _SurfaceLineColor ("Edge Line Color", Color) = (0,0,0,1)
        _SurfaceLineWidth ("Edge Line Width", Range(0,0.5)) = 0.15

        [Header(Shadow Hatching)]
        [NoScaleOffset] _HatchMap ("Hatch Texture", 2D) = "white" {}
        _HatchScale ("Hatch Tiling (per world unit)", Float) = 0.5
        _HatchStrength ("Hatch Strength", Range(0,1)) = 0

        [Header(Height Fog)]
        _HeightFogColor ("Height Fog Color", Color) = (0.55,0.62,0.7,1)
        _HeightFogTopY ("Height Fog Top Y", Float) = 0
        _HeightFogFalloff ("Height Fog Falloff", Float) = 4
        _HeightFogStrength ("Height Fog Strength", Range(0,1)) = 0

        [Header(Base Height Gradient)]
        _GradientBottomColor ("Gradient Bottom", Color) = (0.65,0.72,0.68,1)
        _GradientTopColor ("Gradient Top", Color) = (1,1,1,1)
        _GradientStartY ("Gradient Start Y", Float) = 0
        _GradientDistance ("Gradient Distance", Float) = 4
        _GradientStrength ("Gradient Strength", Range(0,1)) = 1

        // Stable implementation settings.
        [HideInInspector] _BaseColor ("Global Base Tint", Color) = (1,1,1,1)
        [HideInInspector] _AOColor ("Vertex AO Color", Color) = (0.25,0.28,0.35,1)
        [HideInInspector] _WallAOStrength ("Wall Vertex AO Strength", Range(0,1)) = 0.45
        [HideInInspector] _SurfaceAOStrength ("Surface Vertex AO Strength", Range(0,1)) = 0
        [HideInInspector] _AOContrast ("Vertex AO Contrast", Range(0.25,4)) = 1
        [HideInInspector] _ShadowTint ("Shadow Tint", Color) = (0.38,0.42,0.55,1)
        [HideInInspector] _LightThreshold ("Light Threshold", Range(0,1)) = 0.45
        [HideInInspector] _BandSoftness ("Band Softness", Range(0.001,0.25)) = 0.04
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit" Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_WallMap); SAMPLER(sampler_WallMap);
            TEXTURE2D(_SurfaceMap); SAMPLER(sampler_SurfaceMap);
            TEXTURE2D(_StyleMask); SAMPLER(sampler_StyleMask);
            TEXTURE2D(_GlobalNoiseMap); SAMPLER(sampler_GlobalNoiseMap);
            TEXTURE2D(_HatchMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _WallMap_ST; float4 _SurfaceMap_ST;
            half4 _BaseColor, _WallColor, _SurfaceColor, _AOColor, _HeightFogColor, _InkColor, _SurfaceEdgeColor, _GradientBottomColor, _GradientTopColor, _ShadowTint, _GlobalNoiseDarkColor, _GlobalNoiseLightColor, _WallLineColor, _SurfaceLineColor;
            half _WallAOStrength, _SurfaceAOStrength, _SurfaceUseWorldUV, _AOContrast, _HeightFogStrength, _WallOuterGlowStrength, _GradientStrength, _LightThreshold, _BandSoftness, _GlobalNoiseStrength, _WallLineStrength, _HatchStrength, _SurfaceFadeSteps, _SurfaceFadeQuantize, _SurfaceLineWidth;
            float _GradientStartY, _GradientDistance, _HeightFogTopY, _HeightFogFalloff, _GlobalNoiseScale, _WallLineY, _WallLineThickness, _HatchScale;
            float4 _GlobalNoiseOffset;
            CBUFFER_END
            half SampleGlobalNoiseFBM(float2 baseUv)
            {
                half n = SAMPLE_TEXTURE2D(_GlobalNoiseMap, sampler_LinearRepeat, baseUv).r;
                n += SAMPLE_TEXTURE2D(_GlobalNoiseMap, sampler_LinearRepeat, baseUv * 2.63 + 0.37).r * 0.5;
                return n / 1.5;
            }
            struct A { float4 positionOS:POSITION; half3 normalOS:NORMAL; float2 uv:TEXCOORD0; float2 uv2:TEXCOORD1; float2 uv3:TEXCOORD2; half4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; float2 uv:TEXCOORD2; float2 uv3:TEXCOORD3; half2 roleAo:TEXCOORD4; DECLARE_LIGHTMAP_OR_SH(lightmapUV,vertexSH,5); half fog:TEXCOORD6; };
            V Vert(A i) { V o=(V)0; UNITY_SETUP_INSTANCE_ID(i); o.normalWS=TransformObjectToWorldNormal(i.normalOS); o.positionWS=TransformObjectToWorld(i.positionOS.xyz); o.positionCS=TransformWorldToHClip(o.positionWS); o.uv=i.uv; o.uv3=i.uv3; o.roleAo=i.color.rg; OUTPUT_LIGHTMAP_UV(i.uv2,unity_LightmapST,o.lightmapUV); OUTPUT_SH(o.normalWS,o.vertexSH); o.fog=ComputeFogFactor(o.positionCS.z); return o; }
            half4 Frag(V i):SV_Target
            {
                half surface=step(0.5h,i.roleAo.x);
                half3 wall=SAMPLE_TEXTURE2D(_WallMap,sampler_WallMap,TRANSFORM_TEX(i.uv,_WallMap)).rgb;
                // World mode makes the surface texture continuous across every modular piece.
                // Its normal material Tiling and Offset fields control repeats per world unit
                // and global placement respectively; local UV mode remains the default.
                float2 surfaceLocalUv=TRANSFORM_TEX(i.uv,_SurfaceMap);
                float2 surfaceWorldUv=i.positionWS.xz*_SurfaceMap_ST.xy+_SurfaceMap_ST.zw;
                float2 surfaceUv=lerp(surfaceLocalUv,surfaceWorldUv,saturate(_SurfaceUseWorldUV));
                half3 top=SAMPLE_TEXTURE2D(_SurfaceMap,sampler_SurfaceMap,surfaceUv).rgb;
                half3 materialColor=lerp(_WallColor.rgb,_SurfaceColor.rgb,surface);
                half3 baseColor=lerp(wall,top,surface)*materialColor*_BaseColor.rgb;

                // One continuous XZ noise sample is shared by every modular surface piece.
                // surface is a hard 0/1 (step()), so walls never see this tint - skip the
                // sampling there entirely instead of paying for it and discarding the result.
                [branch] if (surface > 0.5h)
                {
                    float3 noisePosition=i.positionWS+_GlobalNoiseOffset.xyz;
                    float2 noiseUv=noisePosition.xz*_GlobalNoiseScale;
                    half globalNoise=SampleGlobalNoiseFBM(noiseUv);
                    half3 noiseTint=lerp(_GlobalNoiseDarkColor.rgb,_GlobalNoiseLightColor.rgb,globalNoise);
                    baseColor*=lerp(half3(1,1,1),noiseTint,_GlobalNoiseStrength);
                }

                half vertexAO=pow(saturate(i.roleAo.y),max(_AOContrast,0.01h));
                half3 aoTint=lerp(_AOColor.rgb,half3(1,1,1),vertexAO);
                half aoStrength=lerp(_WallAOStrength,_SurfaceAOStrength,surface);
                baseColor*=lerp(half3(1,1,1),aoTint,aoStrength);
                float t=saturate((i.positionWS.y-_GradientStartY)/max(abs(_GradientDistance),0.0001));
                baseColor*=lerp(half3(1,1,1),lerp(_GradientBottomColor.rgb,_GradientTopColor.rgb,t),_GradientStrength);

                // Draw one continuous, pixel-stable band across wall-role geometry only.
                float lineDistance=abs(i.positionWS.y-_WallLineY);
                float lineHalfThickness=max(_WallLineThickness*0.5,0.0);
                float lineAntiAlias=max(fwidth(i.positionWS.y),0.0001);
                half wallLineMask=(1-surface)*(1-smoothstep(lineHalfThickness,lineHalfThickness+lineAntiAlias,lineDistance));
                wallLineMask*=saturate(_WallLineStrength)*_WallLineColor.a;
                baseColor=lerp(baseColor,_WallLineColor.rgb,wallLineMask);

                half2 styleMask=SAMPLE_TEXTURE2D(_StyleMask,sampler_StyleMask,i.uv3).rg;
                half rawMask=1-styleMask.r;
                half amount=saturate(rawMask);
                half roleAlpha=lerp(_InkColor.a,_SurfaceEdgeColor.a,surface);
                // Wall tiles pack their baked outer fade in green. Old grayscale atlases remain
                // compatible because red and green are equal and therefore produce no halo.
                half glowEnvelope=saturate(1-styleMask.g);
                half wallOuterGlow=saturate(glowEnvelope-saturate(rawMask))*(1-surface);
                baseColor*=1-saturate(wallOuterGlow*_WallOuterGlowStrength);
                // Terrace the surface fade into flat bands. The baked mask is a smoothstep ramp
                // (Uv2InkMaskBakerWindow.DrawFadeLine), which is what makes it read as a soft
                // gradient; rounding it to _SurfaceFadeSteps levels turns it into stacked flat
                // shapes instead. Walls deliberately keep the raw ramp - they use this same mask
                // channel for crease ink, which has to stay a continuous stroke.
                half fadeSteps=max(_SurfaceFadeSteps,1);
                half bandedAmount=floor(amount*fadeSteps+0.5h)/fadeSteps;
                half shapedAmount=lerp(amount,bandedAmount,saturate(_SurfaceFadeQuantize));
                half tintAmount=lerp(amount,shapedAmount,surface);
                baseColor=lerp(baseColor,lerp(_InkColor.rgb,_SurfaceEdgeColor.rgb,surface),tintAmount*roleAlpha);

                // A hard line sitting at the shared edge, drawn over the banded fade. amount is
                // exactly 1 at that edge and falls inward, so 1-amount is a direct distance from
                // it. fwidth keeps the line one screen-pixel soft at any camera distance instead
                // of stair-stepping, without needing its own softness property.
                half edgeDistance=1-amount;
                half edgeAA=max(fwidth(edgeDistance),0.0001h);
                half surfaceLineMask=(1-smoothstep(_SurfaceLineWidth,_SurfaceLineWidth+edgeAA,edgeDistance))*surface*_SurfaceLineColor.a;
                baseColor=lerp(baseColor,_SurfaceLineColor.rgb,surfaceLineMask);
                half3 n=normalize(i.normalWS); Light light=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half lit=smoothstep(_LightThreshold-_BandSoftness,_LightThreshold+_BandSoftness,saturate(dot(n,light.direction))*light.shadowAttenuation);
                half3 gi=SAMPLE_GI(i.lightmapUV,i.vertexSH,n);
                half3 color=baseColor*(lerp(_ShadowTint.rgb,light.color,lit)+gi);

                // Comic cross-hatching, confined to the shadow side. 'lit' is the existing toon
                // band, so this needs no threshold of its own - it simply fades in wherever that
                // band already went dark. Note the Mobile RP asset ships with main-light shadows
                // off, so lit is pure N.L there and the hatch tracks facing, not cast shadows.
                //
                // Projected in WORLD space, matching the surface noise above. Sampling it in
                // screen space pins the pattern to the display and the level slides underneath it
                // as the camera pans. One sample, no triplanar: the surface role is flat so it
                // takes a plain XZ projection, and walls pick whichever vertical plane their
                // normal faces most - exact for axis-aligned modular pieces, shearing only on
                // geometry rotated off the world axes.
                //
                // Uniform condition, so the branch is fully coherent and costs nothing when the
                // effect is off - which is the default, leaving every existing material unchanged.
                [branch] if (_HatchStrength > 0.001h)
                {
                    float2 wallUv=abs(n.x)>abs(n.z) ? i.positionWS.zy : i.positionWS.xy;
                    float2 hatchUv=lerp(wallUv,i.positionWS.xz,surface)*_HatchScale;
                    half hatch=SAMPLE_TEXTURE2D(_HatchMap,sampler_LinearRepeat,hatchUv).r;
                    // The hatch multiplies the composited colour, so without this it lands ON TOP
                    // of the baked crease ink and the world-height wall line and scratches through
                    // them. Both are line work that has to stay solid and read above the shading,
                    // so hold the hatch out of wherever either one is already drawn - the lines
                    // then sit over a hatched field instead of being broken up by it.
                    half lineMask=saturate(max(max(amount*roleAlpha,wallLineMask),surfaceLineMask));
                    color*=lerp(1,hatch,saturate(1-lit)*_HatchStrength*(1-lineMask));
                }

                half heightFogDepth=max(_HeightFogTopY-i.positionWS.y,0);
                half heightFogFalloffSq=max(_HeightFogFalloff*_HeightFogFalloff,0.0001);
                half heightFog=1-exp(-(heightFogDepth*heightFogDepth)/heightFogFalloffSq);
                heightFog*=saturate(_HeightFogStrength*_HeightFogColor.a);
                color=lerp(color,_HeightFogColor.rgb,heightFog);
                return half4(MixFog(color,i.fog),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _WallMap_ST; float4 _SurfaceMap_ST;
            half4 _BaseColor, _WallColor, _SurfaceColor, _AOColor, _HeightFogColor, _InkColor, _SurfaceEdgeColor, _GradientBottomColor, _GradientTopColor, _ShadowTint, _GlobalNoiseDarkColor, _GlobalNoiseLightColor, _WallLineColor, _SurfaceLineColor;
            half _WallAOStrength, _SurfaceAOStrength, _SurfaceUseWorldUV, _AOContrast, _HeightFogStrength, _WallOuterGlowStrength, _GradientStrength, _LightThreshold, _BandSoftness, _GlobalNoiseStrength, _WallLineStrength, _HatchStrength, _SurfaceFadeSteps, _SurfaceFadeQuantize, _SurfaceLineWidth;
            float _GradientStartY, _GradientDistance, _HeightFogTopY, _HeightFogFalloff, _GlobalNoiseScale, _WallLineY, _WallLineThickness, _HatchScale;
            float4 _GlobalNoiseOffset;
            CBUFFER_END
            float3 _LightDirection;
            struct ShadowAttributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct ShadowVaryings { float4 positionCS:SV_POSITION; };
            ShadowVaryings ShadowVertex(ShadowAttributes i)
            {
                UNITY_SETUP_INSTANCE_ID(i);
                ShadowVaryings o;
                float3 positionWS=TransformObjectToWorld(i.positionOS.xyz);
                float3 normalWS=TransformObjectToWorldNormal(i.normalOS);
                o.positionCS=TransformWorldToHClip(ApplyShadowBias(positionWS,normalWS,_LightDirection));
                #if UNITY_REVERSED_Z
                    o.positionCS.z=min(o.positionCS.z,o.positionCS.w*UNITY_NEAR_CLIP_VALUE);
                #else
                    o.positionCS.z=max(o.positionCS.z,o.positionCS.w*UNITY_NEAR_CLIP_VALUE);
                #endif
                return o;
            }
            half4 ShadowFragment(ShadowVaryings i):SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _WallMap_ST; float4 _SurfaceMap_ST;
            half4 _BaseColor, _WallColor, _SurfaceColor, _AOColor, _HeightFogColor, _InkColor, _SurfaceEdgeColor, _GradientBottomColor, _GradientTopColor, _ShadowTint, _GlobalNoiseDarkColor, _GlobalNoiseLightColor, _WallLineColor, _SurfaceLineColor;
            half _WallAOStrength, _SurfaceAOStrength, _SurfaceUseWorldUV, _AOContrast, _HeightFogStrength, _WallOuterGlowStrength, _GradientStrength, _LightThreshold, _BandSoftness, _GlobalNoiseStrength, _WallLineStrength, _HatchStrength, _SurfaceFadeSteps, _SurfaceFadeQuantize, _SurfaceLineWidth;
            float _GradientStartY, _GradientDistance, _HeightFogTopY, _HeightFogFalloff, _GlobalNoiseScale, _WallLineY, _WallLineThickness, _HatchScale;
            float4 _GlobalNoiseOffset;
            CBUFFER_END
            struct DepthAttributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct DepthVaryings { float4 positionCS:SV_POSITION; };
            DepthVaryings DepthVertex(DepthAttributes i) { UNITY_SETUP_INSTANCE_ID(i); DepthVaryings o; float3 positionWS=TransformObjectToWorld(i.positionOS.xyz); o.positionCS=TransformWorldToHClip(positionWS); return o; }
            half4 DepthFragment(DepthVaryings i):SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On ZTest LEqual Cull Back
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _WallMap_ST; float4 _SurfaceMap_ST;
            half4 _BaseColor, _WallColor, _SurfaceColor, _AOColor, _HeightFogColor, _InkColor, _SurfaceEdgeColor, _GradientBottomColor, _GradientTopColor, _ShadowTint, _GlobalNoiseDarkColor, _GlobalNoiseLightColor, _WallLineColor, _SurfaceLineColor;
            half _WallAOStrength, _SurfaceAOStrength, _SurfaceUseWorldUV, _AOContrast, _HeightFogStrength, _WallOuterGlowStrength, _GradientStrength, _LightThreshold, _BandSoftness, _GlobalNoiseStrength, _WallLineStrength, _HatchStrength, _SurfaceFadeSteps, _SurfaceFadeQuantize, _SurfaceLineWidth;
            float _GradientStartY, _GradientDistance, _HeightFogTopY, _HeightFogFalloff, _GlobalNoiseScale, _WallLineY, _WallLineThickness, _HatchScale;
            float4 _GlobalNoiseOffset;
            CBUFFER_END
            struct NormalAttributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct NormalVaryings { float4 positionCS:SV_POSITION; half3 normalWS:TEXCOORD0; };
            NormalVaryings DepthNormalsVertex(NormalAttributes i) { UNITY_SETUP_INSTANCE_ID(i); NormalVaryings o; float3 positionWS=TransformObjectToWorld(i.positionOS.xyz); o.normalWS=TransformObjectToWorldNormal(i.normalOS); o.positionCS=TransformWorldToHClip(positionWS); return o; }
            half4 DepthNormalsFragment(NormalVaryings i):SV_Target { return half4(normalize(i.normalWS)*0.5h+0.5h,0); }
            ENDHLSL
        }
    }
}
