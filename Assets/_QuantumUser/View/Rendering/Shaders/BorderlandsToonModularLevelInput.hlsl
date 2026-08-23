#ifndef BORDERLANDS_TOON_MODULAR_LEVEL_INPUT_INCLUDED
#define BORDERLANDS_TOON_MODULAR_LEVEL_INPUT_INCLUDED

// Single source of truth for this shader's UnityPerMaterial block.
//
// Every pass in a SubShader must declare a byte-for-byte identical CBUFFER or the SRP Batcher
// silently drops the material back to per-object binding. Project/Mobile Toon Modular Level
// hand-duplicates its block across all four passes; this one includes it instead so the four
// copies can never drift apart.

CBUFFER_START(UnityPerMaterial)
    float4 _WallMap_ST;
    float4 _SurfaceMap_ST;
    float4 _GlobalNoiseOffset;

    half4 _BaseColor, _WallColor, _SurfaceColor, _AOColor;
    half4 _InkColor, _SurfaceEdgeColor, _WallLineColor;
    half4 _GradientBottomColor, _GradientTopColor;
    half4 _GlobalNoiseDarkColor, _GlobalNoiseLightColor;
    half4 _HeightFogColor;
    half4 _CelShadowColor, _CelMidColor, _ToonSpecColor, _RimInkColor, _OutlineColor;

    half _WallAOStrength, _SurfaceAOStrength, _AOContrast, _SurfaceUseWorldUV;
    half _GlobalNoiseStrength, _WallOuterGlowStrength, _WallLineStrength;
    half _GradientStrength, _HeightFogStrength;
    half _CelSteps, _CelSharpness, _CelWrap, _UseCelRamp, _CelGiStrength;
    half _ToonSpecThreshold, _ToonSpecSoftness, _ToonSpecStrength;
    half _RimInkPower, _RimInkStrength;
    half _HatchStrength, _HatchShadowStart, _HatchShadowEnd;
    half _InkUnlit, _OutlineUseSmoothNormals;

    float _GradientStartY, _GradientDistance;
    float _HeightFogTopY, _HeightFogFalloff;
    float _GlobalNoiseScale, _WallLineY, _WallLineThickness;
    float _ToonSpecPower, _HatchScale;
    float _OutlineWidth, _OutlineMinPixels, _OutlineMaxPixels;
CBUFFER_END

// Quantises a 0..1 light term into _CelSteps hard bands. _CelSharpness widens or narrows the
// smoothstep between bands: 1 is a razor step (true cel), 0 collapses back to a linear ramp.
half CelQuantize(half light)
{
    half steps = max(_CelSteps, 1.0h);
    half scaled = saturate(light) * steps;
    half band = floor(scaled);
    half within = scaled - band;
    half halfWidth = lerp(0.5h, 0.015h, saturate(_CelSharpness));
    half blended = smoothstep(0.5h - halfWidth, 0.5h + halfWidth, within);
    return saturate((band + blended) / steps);
}

#endif
