// Deep-space floor - a drop-in replacement for the LakeShader water quad in worlds floating in space
// (Moon), via WorldWaterTheme.SurfaceMaterial / EnvironmentManager, same slot as Project/CloudFog.
//
// Mobile-friendly like CloudFog (see its header):
//  - two star layers from ONE tileable texture (R = star, G = twinkle phase) with different scale and
//    PARALLAX: each layer's UV follows the camera by a fraction (0 = glued to the world, 1 = glued to
//    the camera like a skybox), so stars drift slower than the level and read as infinitely far below;
//    all UVs are linear in world XZ / camera / time -> computed per vertex (exact on the 4-vertex quad);
//  - a slow nebula glow from a grayscale cloud texture (e.g. CloudNoise_Difference);
//  - twinkle from a triangle wave (no sin), all half, no step / if;
//  - alpha-blended with the same shore-field EDGE FADE as CloudFog: space thins out close to the
//    buildings so their walls (faded into Space Color by the tileset's water-depth gradient) show
//    through going down.
Shader "Project/Starfield"
{
    Properties
    {
        [Header(Space)]
        _SpaceColor ("Space Color", Color) = (0.03, 0.04, 0.1, 1)
        _Opacity ("Opacity (away from the buildings)", Range(0, 1)) = 1
        _HeightOffset ("Height Offset (moves the floor down/up, m)", Float) = 0

        [Header(Stars)]
        [NoScaleOffset] _StarTex ("Star Texture (R = star, G = twinkle phase, tileable)", 2D) = "black" {}
        _StarColor ("Star Color", Color) = (0.9, 0.95, 1, 1)
        _StarBrightness ("Star Brightness", Range(0, 4)) = 1.6
        _FarScale ("Far Layer World Size (m per tile)", Float) = 18
        _NearScale ("Near Layer World Size (m per tile)", Float) = 30
        _NearWeight ("Near Layer Weight", Range(0, 1)) = 0.7
        _FarParallax ("Far Layer Parallax (0 = world, 1 = camera)", Range(0, 1)) = 0.85
        _NearParallax ("Near Layer Parallax", Range(0, 1)) = 0.6
        _TwinkleSpeed ("Twinkle Speed", Range(0, 4)) = 0.6
        _TwinkleAmount ("Twinkle Amount", Range(0, 1)) = 0.6

        [Header(Nebula)]
        [NoScaleOffset] _NebulaTex ("Nebula Texture (grayscale, tileable)", 2D) = "black" {}
        _NebulaColor ("Nebula Color", Color) = (0.35, 0.18, 0.6, 1)
        _NebulaColor2 ("Nebula Color 2 (brightest)", Color) = (0.2, 0.55, 0.75, 1)
        _NebulaStrength ("Nebula Strength", Range(0, 1)) = 0.45
        _NebulaScale ("Nebula World Size (m per tile)", Float) = 60
        _NebulaParallax ("Nebula Parallax", Range(0, 1)) = 0.9
        _NebulaDrift ("Nebula Drift (xy m/s)", Vector) = (0.05, 0.02, 0, 0)

        [Header(Near Buildings (Shore Field))]
        // WaterShoreBaker's baked distance-to-land field (runtime only).
        [Toggle(_SHOREFIELD_FADE)] _ShoreFade ("Use Shore Field (edge fade)", Float) = 0
        _EdgeFadeDistance ("Edge Fade Distance (m)", Range(0.1, 12)) = 2.5
        _EdgeFadeOpacity ("Opacity Right At The Buildings", Range(0, 1)) = 0.1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-50" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            // _local (not _local_fragment): the vertex stage writes the shore UV interpolant.
            #pragma shader_feature_local _SHOREFIELD_FADE
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_StarTex); SAMPLER(sampler_StarTex);
            TEXTURE2D(_NebulaTex); SAMPLER(sampler_NebulaTex);

#if defined(_SHOREFIELD_FADE)
            TEXTURE2D(_ShoreField); SAMPLER(sampler_ShoreField);
            float4 _ShoreFieldParams;
#endif

            CBUFFER_START(UnityPerMaterial)
                half4 _SpaceColor;
                half _Opacity;
                float _HeightOffset;
                half4 _StarColor;
                half _StarBrightness;
                float _FarScale;
                float _NearScale;
                half _NearWeight;
                float _FarParallax;
                float _NearParallax;
                float _TwinkleSpeed;
                half _TwinkleAmount;
                half4 _NebulaColor;
                half4 _NebulaColor2;
                half _NebulaStrength;
                float _NebulaScale;
                float _NebulaParallax;
                float4 _NebulaDrift;
                half _EdgeFadeDistance;
                half _EdgeFadeOpacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 starUV : TEXCOORD0;    // xy = far layer, zw = near layer
                float3 nebula : TEXCOORD1;    // xy = nebula UV, z = twinkle time
#if defined(_SHOREFIELD_FADE)
                float3 shore : TEXCOORD2;     // xy = shore field UV, z = field -> 0..1 over fade distance
#endif
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS.y += _HeightOffset;
                o.positionCS = TransformWorldToHClip(positionWS);

                float2 cam = _WorldSpaceCameraPos.xz;
                o.starUV.xy = (positionWS.xz - cam * _FarParallax) / max(_FarScale, 0.01);
                // rotated near layer so the two tilings never line up
                float2 p = positionWS.xz - cam * _NearParallax;
                o.starUV.zw = float2(p.x * 0.8 - p.y * 0.6, p.x * 0.6 + p.y * 0.8) / max(_NearScale, 0.01) + 0.37;
                o.nebula.xy = (positionWS.xz - cam * _NebulaParallax + _NebulaDrift.xy * _Time.y) / max(_NebulaScale, 0.01);
                o.nebula.z = _Time.y * _TwinkleSpeed;

#if defined(_SHOREFIELD_FADE)
                o.shore.xy = (positionWS.xz - _ShoreFieldParams.xy) / _ShoreFieldParams.z + 0.5;
                o.shore.z = _ShoreFieldParams.w / max(_EdgeFadeDistance, 0.01);
#endif
                return o;
            }

            half Twinkle(half2 star, half t)
            {
                half tri = abs(frac(t + star.y) * 2.0h - 1.0h);          // 0..1 triangle wave, per-star phase
                return star.x * (1.0h - _TwinkleAmount + _TwinkleAmount * tri);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half t = (half)frac(input.nebula.z);
                half2 far = SAMPLE_TEXTURE2D(_StarTex, sampler_StarTex, input.starUV.xy).rg;
                half2 near = SAMPLE_TEXTURE2D(_StarTex, sampler_StarTex, input.starUV.zw).rg;
                half stars = Twinkle(far, t) + Twinkle(near, t + 0.5h) * _NearWeight;

                half neb = SAMPLE_TEXTURE2D(_NebulaTex, sampler_NebulaTex, input.nebula.xy).r;
                half3 nebula = lerp(_NebulaColor.rgb, _NebulaColor2.rgb, saturate(neb * 2.0h - 1.0h));
                half3 color = lerp(_SpaceColor.rgb, nebula, neb * _NebulaStrength);
                color += _StarColor.rgb * stars * _StarBrightness;

#if defined(_SHOREFIELD_FADE)
                half field = SAMPLE_TEXTURE2D(_ShoreField, sampler_ShoreField, input.shore.xy).r;
                half edgeFade = lerp(_EdgeFadeOpacity, 1.0h, saturate(field * (half)input.shore.z));
#else
                half edgeFade = 1.0h;
#endif
                return half4(color, _Opacity * edgeFade);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
