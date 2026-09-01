// Ground-projected shadow for a 9-sliced SpriteRenderer (the round character blobs from
// GroundBlobManager and the rectangular building footprints from BuildingShadowManager both run
// through this one material): the falloff shape (soft rounded-rect edge) has to live in a texture
// so Unity's sprite slicing can keep that edge a fixed pixel width while the middle stretches to
// fit any block size. _MainTex only carries the shadow's SHAPE - the actual tint comes from
// _ShadowColor so instances can share one texture.
//
// Multiply-blended like Custom/BlobShadowMultiply, but stencil-masked so overlapping shadows do
// NOT compound into a darker intersection - see the blend/stencil state below for how, and what
// it costs.
//
// WHICH channel carries that shape is a slider (_ShapeSource), because the two readings of a
// typical soft-glow PNG are both useful and look nothing alike:
//   RGB   - a glow texture leaves RGB flat white and puts the gradient only in alpha, so R
//           returns ~1 across the whole opaque footprint. That yields a hard, geometric blob
//           with no gradient, which suits a flat graphic art direction.
//   Alpha - the painted falloff itself, so a soft glow. Alpha is also never sRGB-decoded, so
//           this ramp stays exactly as authored.
// _ShapeCutout can then harden whichever source is chosen.
//
// The SpriteRenderer's own vertex colour is honoured: PlayerShadow drives colour.a every frame
// for its height falloff, so ignoring it would leave a jumping character's shadow at full
// strength all the way up.
//
// Optional comic cross-hatching inside the shadow, matching Project/Mobile Toon Modular Level's
// own shadow hatch. Both project the pattern in WORLD XZ off the same _HatchScale, so a blob
// passing over hatched terrain lines up with it instead of reading as a second, unrelated ink
// layer sliding underneath.
Shader "Custom/BlobShadowSpriteMultiply"
{
    Properties
    {
        _MainTex ("Shape Texture (see _ShapeSource)", 2D) = "white" {}
        _ShadowColor ("Shadow Color", Color) = (0.35, 0.35, 0.4, 1)
        _MinShadowAmount ("Min Shadow Amount (below this, a pixel is left unclaimed)", Range(0, 0.5)) = 0.03
        _Strength ("Strength", Range(0, 1)) = 1

        [Header(Shape)]
        _ShapeSource ("Shape Source (0 = RGB hard, 1 = Alpha soft)", Range(0, 1)) = 0
        _ShapeCutout ("Cutout Amount (0 = as sampled, 1 = hard)", Range(0, 1)) = 0
        _ShapeThreshold ("Cutout Threshold", Range(0, 1)) = 0.5

        [Header(Shadow Hatching)]
        [NoScaleOffset] _HatchMap ("Hatch Texture", 2D) = "white" {}
        _HatchScale ("Hatch Tiling (per world unit)", Float) = 0.5
        _HatchStrength ("Hatch Strength", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            // Multiply, so a shadow darkens whatever is under it PROPORTIONALLY and therefore
            // works over any ground brightness. On its own that compounds - two shadows at 0.5
            // leave 0.25 in the intersection instead of 0.5 - so the stencil below limits each
            // screen pixel to being shaded exactly once per frame.
            //
            // (A min()/darken blend is the other way to make overlaps idempotent, and it keeps
            // fades intact, but min() compares against an ABSOLUTE colour instead of scaling the
            // ground. Blending runs in LINEAR space here, where the lit toon ground sits around
            // 0.2-0.4, so the threshold lands right on top of the ground's own brightness and the
            // shadow flickers between "far too strong" and "invisible" depending on how each patch
            // of floor happens to be shaded. Reading the true ground colour would fix it, but that
            // needs _CameraOpaqueTexture, and this project deliberately keeps the opaque copy off
            // on mobile - see LakeShader.shader's own header.)
            Blend DstColor Zero

            // First writer claims the pixel: draw only where no shadow has drawn yet this frame,
            // then mark it. URP clears stencil along with depth at the start of each camera, so
            // the claim lasts exactly one frame. Bit 0x40 is used rather than the low bits, which
            // URP reserves for its own material/motion-vector masks.
            //
            // The cost is that overlaps are order-dependent: whichever shadow rasterizes first
            // wins the shared pixels, soft rim included, so one shadow's fade can show as a
            // slightly lighter notch across another's solid interior. _MinShadowAmount below keeps
            // the faintest part of a rim from claiming pixels at all, which is most of the fix;
            // hardening the shape (_ShapeCutout = 1) removes the rest, and is usually what the
            // rectangular building footprints want anyway.
            Stencil
            {
                Ref 64
                ReadMask 64
                WriteMask 64
                Comp NotEqual
                Pass Replace
            }

            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // SpriteRenderer.color is NOT reliably vertex colour. Unity bakes it into the vertices
            // only when it DYNAMICALLY BATCHES the sprite; drawn any other way - instanced, which is
            // exactly what a pool of identical blobs sharing one sprite and one material gets - the
            // vertices stay white and the colour arrives as a per-draw constant instead
            // (unity_SpriteColor, already declared in URP's UnityPerDraw block via Core.hlsl, or the
            // PerDrawSprite instancing buffer below). Reading only input.color therefore drops
            // GroundBlobManager's per-frame height fade AND its tint the moment Unity picks the other
            // path, leaving a jumping character's shadow at full strength all the way up - and it
            // switches silently depending on how many blobs happen to be on screen. URP's own
            // Sprite-Unlit-Default composes both sources the same way (input.color * unity_SpriteColor).
            #ifdef UNITY_INSTANCING_ENABLED
                UNITY_INSTANCING_BUFFER_START(PerDrawSprite)
                    UNITY_DEFINE_INSTANCED_PROP(float4, unity_SpriteRendererColorArray)
                UNITY_INSTANCING_BUFFER_END(PerDrawSprite)
                #define BLOB_SPRITE_COLOR UNITY_ACCESS_INSTANCED_PROP(PerDrawSprite, unity_SpriteRendererColorArray)
            #else
                #define BLOB_SPRITE_COLOR unity_SpriteColor
            #endif

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_HatchMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _ShadowColor;
                half _MinShadowAmount;
                half _Strength;
                half _ShapeSource;
                half _ShapeCutout;
                half _ShapeThreshold;
                half _HatchStrength;
                float _HatchScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 groundUv : TEXCOORD1;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // The blob lies flat on the ground plane, so world XZ is the whole projection.
                // Resolved per vertex - it is linear across the quad, so interpolating costs one
                // varying instead of a matrix multiply per fragment. Correct whether or not Unity
                // dynamically batches the sprite: batching pre-transforms vertices to world space
                // and leaves object-to-world as identity, so this yields the same answer either way.
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.groundUv = positionWS.xz * _HatchScale;
                output.uv = input.uv;
                // Resolved here, not in the fragment: it is uniform across the quad, so the
                // instanced-property access happens once per vertex instead of once per pixel.
                output.color = input.color * (half4)BLOB_SPRITE_COLOR;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // One fetch either way - a sample returns the whole texel, so choosing between
                // .r and .a costs a lerp, not a second read.
                half4 shapeTexel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half shape = lerp(shapeTexel.r, shapeTexel.a, _ShapeSource);

                // Plain lerps rather than smoothsteps, so each slider's endpoints are EXACT: 0 is
                // precisely the sampled channel and 1 is precisely the step, no remapping sneaking
                // in. That makes both sliders usable as live A/B comparisons.
                half hardShape = step(_ShapeThreshold, shape);
                half amount = lerp(shape, hardShape, _ShapeCutout);

                // The height fade (GroundBlobManager/PlayerShadow drive vertex alpha as the owner
                // rises off the ground) folded together with the material's own master strength.
                amount *= input.color.a * _Strength;

                // Discarded rather than shaded, so this fragment never reaches the stencil write
                // either. That matters more than the fillrate it saves: a barely-visible rim that
                // claimed a pixel would lock out the solid interior of a shadow drawn later, and
                // the notch that leaves is far more visible than the sliver of fade given up here.
                //
                // Deliberately tested against the FADED contribution, not against the raw shape. It
                // was briefly moved above the fade so a height-faded shadow would keep its whole
                // soft gradient instead of collapsing to a hard core - but that also let the faint
                // outer rim of a high, nearly-invisible blob claim stencil pixels, which is exactly
                // the notch this threshold exists to prevent: on a rig carrying several overlapping
                // blobs (Lux's sentry - one on the chassis plus one per hand) a hovering hand's rim
                // stamped a hard-edged disc straight through the chassis's own shadow. How much
                // gradient survives the fade is _MinShadowAmount's job to tune (it wants to be small
                // - 0.01 or below), not the ordering's.
                clip(amount - _MinShadowAmount);

                half3 tint = lerp(half3(1.0h, 1.0h, 1.0h), _ShadowColor.rgb * input.color.rgb, amount);

                // Scaled by amount so the hatch fades out with the blob's own falloff and its
                // height fade, rather than sitting at full strength inside a nearly-invisible
                // shadow. Uniform condition, so the branch is coherent and free when disabled.
                [branch] if (_HatchStrength > 0.001h)
                {
                    half hatch = SAMPLE_TEXTURE2D(_HatchMap, sampler_LinearRepeat, input.groundUv).r;
                    tint *= lerp(1.0h, hatch, amount * _HatchStrength);
                }

                return half4(tint, 1.0h);
            }
            ENDHLSL
        }
    }
}
