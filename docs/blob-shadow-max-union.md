# Blob shadows

`Custom/BlobShadowSpriteMultiply` is one ordinary transparent multiply pass per sprite. Characters,
limbs, special objects, and 9-sliced building sprites remain independent. GroundBlobManager,
PlayerShadow, BuildingShadowManager, pooling, and height-fade calculations are unchanged.

## Rendering

Each blob sprite draws once in the normal transparent queue with `Blend DstColor Zero`, `ColorMask
RGB` — a straight multiply of scene color by `lerp(1, _ShadowColor, amount)`, where `amount` comes
from the shape texture (`_MainTex`, with `_ShapeSource`/`_ShapeCutout`/`_ShapeThreshold` controls),
SpriteRenderer alpha, and `_Strength`. World-space hatching multiplies in on top, proportional to
resolved `amount`, sampled at the sprite's own world position (`positionWS.xz` from the vertex
shader) — no scene-depth reconstruction needed since the shadow quad already lies on the ground
plane.

Occlusion behind opaque foreground geometry (so a shadow doesn't paint over a wall in front of it)
comes from the ordinary hardware depth test (`ZTest LEqual`, `ZWrite Off`) against the camera's own
bound depth attachment — no `ConfigureInput(Depth)`, no explicit depth-texture request, no
renderer feature at all.

**Overlapping shadows compound, they don't union.** Two overlapping blobs multiply twice and read
darker than either one alone. There is no MAX-mask reduction pass, no fullscreen composite, no
intermediate camera texture, and no per-camera renderer feature. This is a deliberate reversion:
a prior mobile-specific version added a two-pass RenderGraph `BlobShadowRendererFeature` (MAX-mask
render target + fullscreen composite) specifically to make overlaps union instead of compound, but
its `requiresIntermediateTexture = true` forced the *entire frame* through an offscreen buffer +
blit instead of the backbuffer fast path, plus an added depth prepass/copy — a measured, too-costly
regression on mobile tile GPUs. That version, `BlobShadowComposite.shader`, and its
`BlobShadowGpuChecks.cs` validation tool were removed; see git history (`docs/blob-shadow-max-union.md`
predates this revert) if that technique needs to be revisited.

If overlap darkening becomes visually objectionable, mitigate with world/theme shadow colors that
aren't fully black (see below) rather than reintroducing the mask/composite machinery — a colored,
non-black `_ShadowColor` still reads fine under repeated multiplies since `lerp(1, color, amount)`
approaches `color`, not black, in the limit.

## Shared style

Every blob shadow renderer (GroundBlobManager, BuildingShadowManager, PlayerShadow) points at the
same material, `Assets/_Project/Art/Material/ShadowHatch.mat` — editing its `_ShadowColor`,
`_HatchMap`, `_HatchScale`, and `_HatchStrength` changes every blob directly (shared material
instance, not a copied runtime style). Per-world-theme shadow tint (e.g. dark green vs. dark
orange, rather than a neutral gray/black) is just a different `_ShadowColor` on this material (or a
per-theme variant swapped onto the renderers) — no shader change required, since the multiply math
works with any color.

SpriteRenderer **RGB tint is intentionally ignored** by the shadow shader — `_ShadowColor` is the
only source of tint, including on buildings. SpriteRenderer **alpha still works** through vertex
colors, per-draw sprite color, and instancing, preserving individual strength and jump fading.
Other materials may still have different shape, cutout, threshold, and strength settings, but
`_ShadowColor`/hatching apply per-material, so a themed variant means a themed material.

## Files

- `Assets/_QuantumUser/View/Rendering/Shaders/BlobShadowSpriteMultiply.shader`
- `Assets/_Project/Art/Material/ShadowHatch.mat`
- `Assets/_QuantumUser/View/Managers/GroundBlobManager.cs`, `Assets/_Project/Scripts/Util/BuildingShadowManager.cs`, `Assets/_QuantumUser/View/Entities/Player/PlayerShadow.cs`
