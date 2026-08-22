// Scrolling energy beam / tracer for a LineRenderer - the hitscan weapon styles under
// View/Entities/Weapon/Hitscan (LineRendererHitscanView, ContinuousHitscanView).
//
// The scroll is driven entirely by _Time in the fragment shader, so it costs NOTHING on the CPU:
// nothing per frame writes a material property, no MaterialPropertyBlock is involved, and every
// tracer in flight keeps sharing the one material asset. That is the whole reason this exists
// rather than an Update() nudging mainTextureOffset - that approach instances the material per
// LineRenderer copy (one more material to bind per tracer, and the pool means several are live at
// once) and does per-frame managed work for something the GPU already gets for free.
//
// Pair it with LineTextureMode.Tile on the line itself (see HitscanViewBase.ApplyTextureTiling):
// in the default Stretch mode the texture maps exactly once over the whole line, so a 3m and a 30m
// shot would scroll at wildly different visible rates and a tiled pattern would never repeat.
//
// Vertex color is honored, so the LineRenderer's own start/end colors, its width/color gradient and
// LineRendererHitscanView's alpha fade-out tween all still drive this exactly as they did.
Shader "Project/Scrolling Beam"
{
    Properties
    {
        [MainTexture] _MainTex ("Beam Texture", 2D) = "white" {}
        [HDR] _Color ("Tint (HDR)", Color) = (1, 1, 1, 1)

        [Header(Scroll)]
        _ScrollSpeed ("Scroll Speed (UV per second)", Vector) = (-2, 0, 0, 0)

        [Header(Second Layer)]
        [Toggle(_SECONDLAYER)] _UseSecondLayer ("Enable Second Layer", Float) = 0
        _ScrollSpeed2 ("Layer 2 Scroll Speed", Vector) = (-3.5, 0, 0, 0)
        _Layer2Tiling ("Layer 2 Tiling Multiplier", Range(0.1, 8)) = 2.3
        _Layer2Blend ("Layer 2 Blend", Range(0, 1)) = 0.5

        [Header(Shape)]
        _EdgeSoftness ("Cross Beam Edge Softness", Range(0, 0.5)) = 0.15

        [Header(Blending)]
        // Defaults to SrcAlpha One - additive, but still driven by alpha, so the fade tween can
        // fade a tracer out. Set Dst to OneMinusSrcAlpha for a normal alpha-blended beam.
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 1
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
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            // A keyword rather than a lerp against a blend value: a beam that doesn't want the
            // second layer doesn't pay for its texture sample at all.
            #pragma shader_feature_local_fragment _SECONDLAYER

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                float4 _ScrollSpeed;
                float4 _ScrollSpeed2;
                half _Layer2Tiling;
                half _Layer2Blend;
                half _EdgeSoftness;
                half _SrcBlend;
                half _DstBlend;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                // Tiling/offset stays applied here; the scroll is added in the fragment stage so a
                // long line's interpolated UVs can't shear the animation across its segments.
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 scrolledUV = input.uv + _Time.y * _ScrollSpeed.xy;
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, scrolledUV);

                #if defined(_SECONDLAYER)
                    // Same texture at a different rate/scale - the cheapest way to stop a single
                    // scrolling band from reading as an obvious repeating loop.
                    float2 secondUV = input.uv * float2(_Layer2Tiling, 1.0) + _Time.y * _ScrollSpeed2.xy;
                    half4 tex2 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, secondUV);
                    tex = lerp(tex, tex * tex2 * 2.0h, _Layer2Blend);
                #endif

                half4 color = tex * _Color * input.color;

                // V runs across the beam's width, so this feathers its long edges - enough to make
                // even a plain white texture read as a beam rather than a hard-edged ribbon. At 0 the
                // divide saturates to 1 everywhere, so it costs the same and does nothing.
                half edgeDistance = min(input.uv.y, 1.0h - input.uv.y);
                color.a *= saturate(edgeDistance / max(_EdgeSoftness, 0.0001h));

                return color;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
