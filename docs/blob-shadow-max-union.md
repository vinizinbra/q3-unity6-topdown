# Mobile blob shadow MAX union

`Custom/BlobShadowSpriteMultiply` uses one scalar MAX mask and one shared-color composite. Characters, limbs, special objects, and 9-sliced building sprites remain independent. GroundBlobManager, PlayerShadow, BuildingShadowManager, pooling, and height-fade calculations are unchanged.

## Rendering

The original shader multiplied scene color per sprite and used stencil bit 64 to reject subsequent sprites. A weak feather could exclude a stronger interior. Removing stencil alone would accumulate darkness.

The mobile version runs two RenderGraph passes before ordinary transparents:

1. **MAX amount:** Draw each shadow once into a mask cleared to zero, using `Blend One One`, `BlendOp Max`, and `ColorMask R`. Keep the existing shape-source/cutout controls, SpriteRenderer alpha, and `_Strength`. RGB and hatching never participate in the reduction.
2. **Multiply once:** A fullscreen triangle samples that mask and outputs `lerp(1, sharedShadowColor, amount)`. Optional world-XZ hatching modulates this multiplier once, proportional to the resolved amount. `Blend DstColor Zero` multiplies scene RGB while preserving camera alpha.

There is no winner-color pass or RGBA winner texture. There is no ordinary transparent pass on the blob shader, so sprites cannot apply the effect again. There is no first-writer stencil logic or CameraOpaqueTexture requirement.

## Shared style

All three renderer features reference `Assets/_Project/Art/Material/ShadowHatch.mat` as their **Shadow Style**. Edit its `_ShadowColor`, `_HatchMap`, `_HatchScale`, and `_HatchStrength` to adjust every blob. The feature copies these settings into its runtime composite material, so edits also work in Play mode.

SpriteRenderer **RGB tint is intentionally ignored**, including on buildings. SpriteRenderer **alpha still works** through vertex colors, per-draw sprite color, and instancing, preserving individual strength and jump fading. Other materials may still have different shape, cutout, threshold, and strength settings, but the renderer feature's style controls the final color and hatch globally.

World-space hatch coordinates now come from the receiving surface reconstructed from scene depth. When hatch strength is zero, the composite skips the depth/hatch samples and world-position reconstruction. `_MinShadowAmount` defaults to zero; the shared material's previous 0.008 cutoff was migrated to zero. Soft gradients are not hardened to fix overlap.

## Mask precision and resolution

The target prefers **R8_UNorm**, with render/sample/blend capability checks. Fallbacks are R16_SFloat and RGBA8_UNorm. Unsupported devices report an error instead of using accumulating blending. The MAX is exact at the target's precision; ordinary R8 quantization can differ from unquantized input by approximately 0.002.

All installed features default to **Full** resolution relative to the camera's render resolution, including pipeline render scale. This preserves MAX at each rendered pixel. **Half** and **Quarter** remain optional feature settings; they reduce rasterization and storage but spatially approximate the final field and may bleed at occluder boundaries. Changing resolution does not change MAX into accumulating blending.

## Mobile cost

| Implementation | Shadow geometry draws | Fullscreen composite | Extra shadow color targets |
| --- | --- | --- | --- |
| Original stencil shader | Once per sprite | None | None |
| Previous per-blob RGB winner version | Twice per sprite | One | R8 + RGBA8 |
| Current shared-style version | Once per sprite | One | R8 |

Compared with the RGB winner version, this removes one geometry pass and **80% of shadow-target storage** at the same resolution with R8 available. It is not an 80% reduction in total GPU time.

At 1280×720, the R8 target occupies approximately **0.88 MiB**, versus **4.4 MiB** for the previous pair. At 1920×1080 it occupies approximately **1.98 MiB**, versus **9.9 MiB**. Half resolution quarters these numbers; quarter resolution divides them by sixteen. Figures exclude URP scene/depth targets and allocation overhead.

This still costs more infrastructure than the original stencil shader: a fullscreen pass, mask traffic, and sampled scene depth. URP can add a depth copy/prepass when depth was previously disabled. An intermediate camera target is requested to keep screen orientation consistent; the current mobile pipeline already renders below display resolution. RenderGraph manages the mask; there are no per-object render textures or scene-color copies.

Actual GPU milliseconds have not been measured on a phone. Profile representative gameplay on target devices before choosing a reduced resolution or declaring the impact negligible.

## Automatic setup

One active `BlobShadowRendererFeature` is installed in each of:

- `Assets/Settings/Mobile_Renderer.asset`
- `Assets/Settings/PC_Renderer.asset`
- `Assets/URP/New Universal Render Pipeline Asset_Renderer.asset`

Each has explicit references to `BlobShadowComposite.shader` and the shared `ShadowHatch.mat`. Existing prefab/material references remain valid. BlobShadowPrefab and BuildingShadowPrefab use the existing shader GUID, so their instances automatically use the new path. The legacy MainChar PlayerShadow material was migrated from Sprites/Default to the shared shadow material; its sprite and fade settings are unchanged. No limb shadows were removed.

For a new renderer, add the same feature and assign its Composite Shader and Shadow Style. For a new shadow sprite, use a material with `Custom/BlobShadowSpriteMultiply`. Lights retain their separate light material.

## Verification and limits

Run **Tools → Rendering → Validate Blob Shadow MAX** outside Play mode. `BlobShadowGpuChecks.cs` renders the production shaders and checks all four requested MAX cases in both orders, sampled shape × sprite alpha, shared color independent of sprite RGB, GPU instancing, shape controls, pixel-by-pixel soft union and composite, sliced geometry, shared hatching, and clearing.

An isolated Unity 6000.3.6f1 / URP 17.3 project also exercises the actual RenderGraph feature, the installed mobile/PC/alternate renderer assets, dynamic batching, opaque occlusion, a 4× MSAA camera target, and reduced resolutions. GPU validation is on macOS Metal (Apple M3 Max). Windows APIs, Switch, Android Vulkan/GLES, and iOS still require device validation/profiling. Gameplay jump animations still need an in-game check; their existing placement/fade code was not changed.

RenderGraph must remain enabled; there is no compatibility-mode implementation. The field affects opaque scene color before transparent sprites/effects render. Overlay/UI cameras are skipped to prevent applying the field twice. Shadows visible exclusively to overlay cameras do not join the base-camera field. Resolved MSAA depth can differ from per-sample silhouette coverage.

## Files

- `Assets/_QuantumUser/View/Rendering/Shaders/BlobShadowSpriteMultiply.shader`
- `Assets/_QuantumUser/View/Rendering/BlobShadowRendererFeature.cs` and `.meta`
- `Assets/_QuantumUser/View/Rendering/Shaders/BlobShadowComposite.shader` and `.meta`
- The three renderer assets listed above
- `Assets/_Project/Art/Material/ShadowHatch.mat` (cutoff migration; existing user color edits preserved)
- `Assets/_QuantumUser/Entities/Characters/MainChar.prefab` (legacy shadow material migration)
- `Assets/_Project/Editor/BlobShadowGpuChecks.cs` and `.meta`
- This document
