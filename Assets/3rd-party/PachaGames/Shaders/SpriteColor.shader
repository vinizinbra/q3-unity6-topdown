Shader "Sprites/SpriteColor"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
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

        Cull Off
        ZWrite Off
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
            CBUFFER_END

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
            };

            v2f vert (appdata_t v)
            {
                v2f o;
                o.vertex = TransformObjectToHClip(v.vertex.xyz);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color;
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
                return texColor;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
