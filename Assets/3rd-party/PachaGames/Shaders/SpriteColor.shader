Shader "Sprites/SpriteColor"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}

        // uGUI masking support (Mask = stencil, RectMask2D = _ClipRect). Driven by the
        // CanvasRenderer; defaults are no-ops so SpriteRenderer usage is unaffected.
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        ZWrite Off
        ColorMask [_ColorMask]
        // Premultiplied blending, exactly like Sprites/Default. NOT "SrcAlpha OneMinusSrcAlpha"
        // with a straight-alpha output: that factor also applies to the ALPHA channel, so the
        // destination alpha ends up as a*a instead of a. On an opaque backbuffer nobody notices,
        // but anything that composites this render by its alpha - a transparent-background
        // RenderTexture shown through a RawImage, e.g. CharacterPreviewWidget - gets far too much
        // background bleeding through every antialiased edge texel, which reads as a bright halo
        // eating into the sprite's own dark outline.
        Blend One OneMinusSrcAlpha

        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
            CBUFFER_END

            // Set by RectMask2D through the CanvasRenderer (same declarations as UI/Default).
            float4 _ClipRect;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
                half4 color : COLOR; // SpriteRenderer.color
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                half4 color : COLOR;
            #ifdef UNITY_UI_CLIP_RECT
                float4 mask : TEXCOORD1;
            #endif
            };

            v2f vert (appdata_t v)
            {
                v2f o;
                o.vertex = TransformObjectToHClip(v.vertex.xyz);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color;
            #ifdef UNITY_UI_CLIP_RECT
                // Same soft-clip setup as UI/Default: UI vertices are in canvas space, which is
                // the space _ClipRect is expressed in.
                float2 pixelSize = o.vertex.w;
                pixelSize /= float2(_ScreenParams.x, _ScreenParams.y) * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
                o.mask = float4(v.vertex.xy * 2 - clampedRect.xy - clampedRect.zw,
                                0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));
            #endif
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);

                // i.color.rgb is the mix color, i.color.a is the mix strength:
                // 0 = untouched sprite, 1 = fully replaced by i.color.rgb.
                texColor.rgb = lerp(texColor.rgb, i.color.rgb, i.color.a);

                // Sprite shape/opacity always comes from the texture, never from i.color.a.
                texColor.rgb *= texColor.a;

            #ifdef UNITY_UI_CLIP_RECT
                // Output is premultiplied, so the clip factor scales all four channels.
                half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(i.mask.xy)) * i.mask.zw);
                texColor *= m.x * m.y;
            #endif
                return texColor;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
