---
name: mobile-juicy-vfx
description: |
  How to build juicy, cheap, mobile-safe particle effects for RiftRaiders skills, enemy and boss attacks:
  ground rings, radius explosions, cone hits (boss punch / swipe / breath / sound wave), slams and shockwaves,
  impacts and muzzle pops. Covers the radius-1 authoring contract (root scale = gameplay radius), Shuriken
  mesh particles with vertex-colour shapes, the project's mobile particle shaders, simple procedural meshes
  (ring, disc, wedge, dome, low-poly sphere, half cone / crown for slams), the juice layer recipe, mobile
  budgets, code builders (SlamBlastFxBuilder: per-hero slam vs blast styles, ToonSlam smoke swirl) and
  gameplay-camera verification incl. old-vs-new comparison GIFs. Use whenever the user asks for a new or reworked
  particle / VFX / efeito / partícula / explosão / anel / cone / slam / shockwave / impact for a skill or
  attack, asks for a GIF/recording of an effect, or asks why an effect does not match its skill radius.
  Not VFX Graph: never use it here.
---

# Mobile Juicy VFX

Shuriken (`ParticleSystem`) only. **Do not use VFX Graph**: it needs compute shaders, costs a lot on low-end
Android/GLES, doesn't go through `EffectsManager`'s ParticleSystem pool, and gives us nothing at our particle
counts (tens, not millions). Juice comes from layering and timing, not from particle count.

Read first: `Assets/_Project/Editor/Boss/MeshParticleFxKit.cs` (shared mesh-particle plumbing + preview),
`Assets/_Project/Editor/Boss/RiffRiotPowerChordFxBuilder.cs` (a full cone effect built in code), and
`EffectsManager.PlayEffect` (`Assets/_QuantumUser/View/Managers/EffectsManager.cs`).

## 1. The radius-1 contract (non-negotiable)

Every area effect is authored at **gameplay radius 1** and sized only by its root transform:
`EffectsManager.Instance.PlayEffect(prefab, position, rotation, Vector3.one * radius)`.
This is already the house rule: `OnShockwaveReleased`, `OnGroundbreakerSlammed`, `OnAreaDetonated`,
`SkillFxStep.ScaleByRadius` (`Vector3.one * action.EffectRadius * step.Scale`) all do exactly this.

| Shape | Pivot | 1 unit means | Caller passes |
|---|---|---|---|
| **Ground ring / area** | centre, on the floor | outer edge of the damage circle | `Vector3.one * radius`, `Quaternion.identity` |
| **Radius explosion** | centre, on the floor | blast radius (dome/sphere reaches 1 sideways) | `Vector3.one * radius` |
| **Cone** (punch, swipe, breath, sound wave) | **apex** (the attacker), on the floor | cone **range**; local **+Z = forward**, cone symmetric around +Z | `Vector3.one * range`, `Quaternion.LookRotation(flatDir)` |
| **Slam / shockwave** | impact point, on the floor | how far the wave travels | `Vector3.one * radius` |
| **Line** (eruption line, lane) | start point | line length along +Z | `new Vector3(width, 1, length)` only if the effect is built for it |
| **Point impact** (hit, muzzle) | contact point | not radius-scaled: authored at real size | prefab's own scale |

Rules that make the contract actually work:
- **Every** `ParticleSystem` in the prefab: `main.scalingMode = Hierarchy`. Under `Local` a child ignores the
  root scale and silently stays radius 1 (existing offenders: `SimpleExplosion`, `BruteAfterShockExplosion`;
  `Tools ▸ RiftRaiders ▸ Test ▸ Particles - Set Children To Hierarchy Scaling` converts one and keeps its size).
  `Shape` mode scales only the emitter shape, not particle size: don't use it.
- `simulationSpace = Local` so speeds, travel distance and gravity drops scale with the root. A World-space
  system (trails, lingering smoke) does not scale its motion: give it a speed in radius units on purpose, or keep
  it Local.
- Root transform stays at scale (1,1,1) in the prefab; `EffectsManager` resets pooled instances to the scale you
  pass. No baked root scale, no "author at 3 and divide".
- Lift ground layers 0.02–0.05 above y=0 (z-fight), and sort ground layers under everything else
  (`renderQueue` / `sortingFudge`), so a ring never draws over the heroes.
- **Cone angle is not scalable** (uniform scale keeps the angle). Gameplay angles differ
  (`BreathConeDeliveryData.ConeAngleDegrees` = 60, Power Chord 90). Build one prefab per angle as a builder
  parameter and suffix it: `…_Cone60`, `…_Cone90`, `…_Cone120`. Never squash X to fake a narrower cone (arcs turn
  into ellipses).
- Thickness grows with radius (a radius-6 ring is 6× wider than radius 1). Fine for most effects; if a big ring
  reads fat, make the ring band thinner for that prefab (author for the radius it is mostly used at) rather than
  adding non-uniform scale at the call site.

## 2. Look: shapes as meshes, not big textured quads

On mobile the cost of an effect is **overdraw** (transparent pixels drawn many times), not particle count. A
camera-facing quad with a ring texture pays for every transparent pixel of the whole square; a ring **mesh** only
pays where the ring is. So the default is one mesh particle per layer, shape carried by **vertex colour/alpha**,
the way `MeshParticleFxKit` does it:
- `renderMode = Mesh`, `alignment = Local`, `startSpeed 0`, `startSize 1`, one burst of 1, size/colour over
  lifetime drive the animation. `MeshParticleFxKit.ApplyMeshRenderer` sets the renderer up (no shadows/probes).
- Vertex colours **must** be `Color32` (`BuildMesh` does this; Float32 colours are misread by Unity 6).
- UVs at 0.5 sample a plain soft texture; the vertex alpha profile does the feathering.
- Save meshes with `MeshParticleFxKit.SaveMesh` so guids survive rebuilds.

### Mesh library (all radius 1, on XZ, +Z forward)

Already there:
- `MeshParticleFxKit.Arc(...)` — feathered band at the cone rim (wavefront), currently fixed 90°.
- `MeshParticleFxKit.SoftCone(...)` — whole 90° cone fill, apex alpha → far alpha, rim fade.
- `MeshParticleFxKit.Ring(name, innerRadius, across[], segments = 48)` — full 360° annulus, outer edge at 1, alpha
  profile across the band. Ground ring, shockwave front, telegraph edge.
- `MeshParticleFxKit.Disc(name, centreAlpha, fadeStart, segments = 32)` — filled circle, soft rim. Ground flash,
  scorch, area tint.
- `MeshParticleFxKit.Dome(name, height, rimAlpha, topAlpha, segments = 32, rings = 5)` — hemisphere radius 1,
  `height` tall, alpha horizon → top (cheap vertex Fresnel). `height .5` = squat slam body, `.85` = round explosion
  shell, `1` with `rimAlpha .35 / topAlpha 1` = glowing fireball core.
- `MeshParticleFxKit.Crown(name, height, bottomRadius, wall[], segments = 32)` — **open truncated cone** (half-cone
  wall), `bottomRadius` at the floor flaring to 1 at `height`. Scaled up over life it is the rising wall of a slam;
  this is what makes a slam read as a slam.
- `MeshParticleFxKit.Shard(name, width = .35f, tipAlpha = .15f)` — flat diamond, long axis +Z. Debris, sparks.
- `MeshParticleFxKit.RenderCirclePreview(...)` — circle sibling of `RenderConePreview` (radius outline, hero capsule,
  indexed frames for GIFs, fixed seeds on pack prefabs so frame sequences don't flicker).
- `Assets/_Project/Art/Primitives/` — `PrototypeLowPolySphere.obj` (explosion core/bubble), `PrototypeMidPolySphere`,
  `PrototypeTorus`, `PrototypeCone`, `PrototypeCylinder`, `PrototypeCylinderWall`, `PrototypePyramid`,
  `PrototypeDiamond`, `HexPrism.asset`. Check their size before use (a sphere of radius 0.5 needs startSize 2).

Add to the kit (generic, same style, parameters with defaults that reproduce the current output) when first needed:
- `Wedge(name, angleDegrees, ...)` / add an `angleDegrees = 90` parameter to `Arc` and `SoftCone` — cone variants.
- `Crown(..., angleDegrees = 360)` — a partial crown is a cone punch's leading wall.

Segments: rings/domes that grow to gameplay radius 4–5 fill a big part of the screen, so 32 segments (16 showed
visible facets on the dome rim at radius 4); arcs/cones need 64–96 because the rim is what players read.

### Proven pack layer: ToonSlam smoke swirl (user favourite)

`RR_ToonBodySlam`'s **VFX3D_Grounded_03** — Wallcoeur's animated DriftSmoke sheet on its half-sphere — is the
"rotating smoke" the user likes; reuse it instead of inventing smoke. It already fits the contract:
`03_MATERIALS/03_Model/DemiSphere.fbx` is radius 1, Y-up (dips 0.2 below its pivot, so lift the child 0.2), and
`03_MATERIALS/Impact/M_Fp_DriftSmoke05ADD.mat` is `Mobile/Particles/Additive`. Recipe (`SlamBlastFxBuilder.SmokeSwirl`):
mesh particle, 4×4 Grid sheet, WholeSheet, `frameOverTime` linear 0→1 (multiplier .9999), 1 cycle; life .6,
delay .03; random start Y rotation + `rotationOverLifetime.y = π` rad/s; size XZ `.2 → .75@.15 → 1`, Y the same
×0.6; tint `Lerp(Core, Main, .35)` fading to the hero hue. Hand-made alpha-blended "smoke balls" (Dome puffs) read
as lumpy flat blobs; don't.

## 3. Shaders (mobile only)

| Shader | Use for |
|---|---|
| `Mobile/Particles/Additive` (`MeshParticleFxKit.AdditiveShader`) | glow, white-hot core, flashes, energy. Glow = stacked additive layers (halo wide+faint, core thin+bright). **No Bloom.** |
| `Mobile/Particles/Alpha Blended` (`AlphaBlendedShader`) | solid colour bodies, dust, smoke, the "colour" layer under the additive core |
| `Project/Mobile Particle Tint HDR` | single-texture mesh/billboard with HDR tint |
| `Project/Mobile Particle Alpha Rim` | spheres/domes: cheap vertex Fresnel rim = shield bubbles, explosion shells |
| `Project/Mobile Particle Duotone Ramp` | recolouring Epic Toon FX / Cartoon FX grayscale textures to our palette (shadows tinted, highlights stay white) |
| `Project/Mobile Particle Vertex Color` | lit toon debris chunks (rocks, scrap) with hard light band |

Banned on mobile: soft particles (needs depth texture), distortion/heat-haze (needs opaque texture), Lit/Simple
Lit particle shaders, per-particle real-time lights, Shader Graph particle shaders with noise in the fragment,
VFX Graph. Textures: reuse Epic Toon FX / Cartoon FX / Wallcoeur textures before making new ones (see memory
"VFX from existing packs"); keep them ≤ 256² and in the Gameplay atlas when they are sprites.

## 4. Juice recipe (layer timeline)

Most of our attacks read best as 4–6 layers in < 0.6 s:

1. **Anticipation** (optional, only for enemy/boss attacks that aren't telegraphed elsewhere): a gathering pulse at
   the source. The red telegraph decal (`EnemyAttackVisualsView`) is the warning; don't duplicate it.
2. **Flash** 0–0.08 s: additive white core disc/soft cone, full size at birth, alpha 1 → 0. Sells the hit
   frame. Pair with the camera shake / haptics the gameplay code already fires.
3. **Main shape** 0–0.35 s: the ring / wavefront / dome. Size over lifetime **ease-out with overshoot**
   (0 → 1.08 at 35% → 1.0), so the edge lands on the real radius and "punches" past it. Colour: white → hue → dark
   edge, alpha holds then drops fast (`1, .95@.35, .5@.7, 0@1` as in the Power Chord builder).
4. **Secondary motion** 0.02–0.5 s: 6–16 shards/sparks/rocks with speed in radius units and some gravity, random
   rotation, size shrinking to 0. Dust puffs (alpha blended, 3–6, slow) for ground slams.
5. **Ground mark** 0.1–1.0 s: thin dark/coloured ring or scorch disc on the floor fading out, so the area stays
   readable after the flash.
6. **Echo** (optional): a second thinner ring 0.05–0.08 s later; the Power Chord uses three nested wavefronts.

Shape notes per type:
- **Ground ring AoE**: Disc flash + Ring expanding 0.6 → 1 + inner faint Disc fill + edge sparks emitted on a
  circle (Shape = Circle, radius 1, `radiusThickness 0`).
- **Explosion / blast** (`SlamBlastFxBuilder` Blast style, Yoru Vortex): Disc flash + **hot core fireball**
  (additive Dome height 1, white → hue, .12 → .6 in .22 s) + round shell (alpha-blended Dome height .85) + one fast
  floor Ring + **debris thrown UP in 3D** (Hemisphere shape rotated (-90,0,0), `alignToDirection`, gravity ~1.4,
  extra +Y velocity) + smoke swirl. No Crown wall and no flat floor dust: those two are what make it read as a
  ground slam instead of a blast (user feedback).
- **Cone punch / swipe**: SoftCone flash at full range (instant, the whole struck area), Arc wavefront(s) travelling
  apex → rim, Crown wall slice at the leading edge for big boss punches, sparks emitted from a sector mesh
  (`shape.mesh = Sector`, as in the Power Chord).
- **Slam / shockwave** (`SlamBlastFxBuilder` Slam style, Brute Aftershock): Disc flash + squat alpha-blended Dome
  (height .5) + white Crown wall rising and expanding .15 → 1.05 → 1 while its height collapses + Ring at the floor
  with an echo burst .1 s later + flat splinters (Circle shape on XZ) + smoke swirl; the Crown is what makes it
  read as a *wave* from the top-down camera.
- **Reference builder**: `Assets/_Project/Editor/Vfx/SlamBlastFxBuilder.cs` — one `Palette` per hero (Core / Main /
  Deep / WallTint + `Blast` flag), one prefab each (`Prefabs/Effects/<Hero>/<Name>Fx`). A new hero's blast or slam
  is a new Palette + menu item, not a new builder.

Colour: hero effects use the hero's palette, enemy attacks the enemy's, Rift accent `#FD3971` only where the
design asks for it. Contrast is judged on the biome the effect lives on (memory "Test bosses on their biome").

## 5. Mobile budget (per effect instance)

- ≤ 6 drawn particle systems (a silent root clock doesn't count), ≤ 2 own materials (one additive, one alpha
  blended) — each system+material is a draw call. A shared pack material that is already mobile (the DriftSmoke
  swirl) may be the third; swap a layer out rather than going to 7 systems.
- ≤ 40 particles alive total; mesh layers are 1 particle each.
- Total lifetime ≤ 1 s (ground mark may linger to ~1.5 s). Non-looping, `playOnAwake`, `stopAction None`:
  `EffectsManager` pools prefabs and must see `IsAlive == false` to release them. Use
  `RenderConePreview(..., mustBeDeadAt: x)` to prove it.
- `cullingMode = AlwaysSimulate` (pooled one-shots must finish even off-screen), `maxParticles` set to the real
  count, `useAutoRandomSeed = false` + fixed `randomSeed` for repeatable previews.
- No collision module, no sub-emitters chains, no trails module on more than one system, no noise module.

## 6. Build in code, not by hand

New effects are built by an Editor builder so they can be regenerated and tuned in one place:
- File: `Assets/_Project/Editor/Vfx/<Name>FxBuilder.cs` (boss-specific ones stay next to their boss builder),
  namespace `RiftRaiders.Editor`, using `MeshParticleFxKit`. Generic meshes go into the kit, not the builder.
- Menu: `Tools/RiftRaiders/VFX/<Name>/Build` (category submenu, never flat at top level).
- Output: `Assets/_Project/Prefabs/Effects/<Group>/<Name>Fx.prefab` + its meshes/materials in the same folder.
- Builders overwrite assets in place (`SaveMesh`, `EnsureMaterial`), so guids and prefab references survive.
- "Edit the builder, not the assets": say so in the builder's summary and in the owning doc.
- Hook it up through an existing seam: an `EffectsManager` slot (`[SerializeField]` prefab + `Vector3.one * e.Radius`),
  `SkillFxStep` with `ScaleByRadius = true`, or the boss/enemy delivery's own effect field. Use the event's real
  `Radius` / the action's `EffectRadius` — never a hand-typed size.

## 7. Verify (every round)

1. Render at the gameplay camera with `MeshParticleFxKit.RenderConePreview` (or a ring/circle sibling of it added
   to the kit): FOV 14, Y 36, rot (45,0,0), no post, the target biome's floor colour, the real gameplay outline
   drawn (cone at its real angle, circle at radius 1) and the 1 m grid.
2. Render at **two radii** (1 and the largest real one, e.g. 4.5 for Groundbreaker rank 3) by setting the root
   scale: the edge must sit on the outline both times. If it only fits at 1, a system is not on Hierarchy/Local.
3. Frames at ~0.03 / 0.08 / 0.2 / 0.4 s, and `mustBeDeadAt`.
4. Copy the PNGs to `Recordings/` and `open` them (memory "Show renders"), every round, unprompted.
5. **GIF for the user to evaluate** (what they ask for): `RenderCirclePreview(path, radius, floor, times, (i, t) =>
   ".../frames/<key>/{i:000}.png", width: 800, height: 600)` with `times` = `i / 30f` for ~1.1 s (real speed) and
   `i * .01f` for 0.3× slow-mo, run through `unity cmd eval_file`; then PIL (no ffmpeg on this machine): crop around
   the circle, label each panel, paste panels side by side, `convert("P", palette=ADAPTIVE)`, `save_all`, 33 ms per
   frame, hold the last frame ~12 frames. Always put **old | reference | new** side by side (e.g. old Aftershock |
   Groundbreaker ToonSlam | new). Save to `Recordings/<name>.gif` and `open` it.
6. **Debug a layer**: `PrefabUtility.LoadPrefabContents`, disable every renderer but one, `SaveAsPrefabAsset` to a temp
   path, render, delete the temp. Finds artifacts (z-fight holes, dark petals) in one shot.
7. In Play: trigger via the cheat menu / Jump To This Boss; check overdraw with Scene view ▸ Overdraw when a
   layer covers a big area.

## Pitfalls

- `Local` scaling mode on any child → effect stuck at radius 1 (most common bug with pack prefabs).
- Pack prefabs (Epic Toon FX / Cartoon FX) are authored at arbitrary sizes and Local mode: copy, set Hierarchy,
  re-size to radius 1, and swap their materials to the mobile shaders before using them in the radius contract.
- Cone prefab pivot at the centre of the cone instead of the apex → it hits behind the attacker.
- Cone authored along +Y or +X (the telegraph sprite convention) instead of +Z → wrong direction with
  `LookRotation`.
- Billboard (camera-facing) particles for ground shapes → they tilt with the camera; ground shapes are Local
  mesh/HorizontalBillboard.
- A looping system in a pooled prefab never releases its pool slot.
- Float32 mesh colours → black/invisible mesh particles in Unity 6.
- Root at y=0 is ON the floor: a visible floor layer on the root z-fights (radial holes). Make the root a silent
  clock (emission off, renderer off) and lift every layer as a child (`SlamBlastFxBuilder`).
- `Mathf.SmoothStep(from, to, t)` is a smoothed *lerp*, not GLSL `smoothstep(edge0, edge1, x)`: write the
  `t*t*(3-2t)` yourself for alpha falloffs.
- Additive tints wash out to pastel/mint on bright floors (cyan on grass): put the hue on an alpha-blended layer
  (dome / ring) and keep additive for the white flash, hot core, sparks and the smoke swirl.
- A late alpha-blended dome lingering at mid alpha turns the inside murky grey-blue; drop its alpha to ~.12 by 70%
  of life, and keep dust/puffs out at the wall, not over the centre.
- Pack prefabs placed at the event position (old `BruteAfterShockExplosion`, `VortexRandomExplosion`,
  `RR_ToonBodySlam`) render half under the floor: half their layers sit at or below the pivot.
- Built-in `Texture2D.whiteTexture` on a saved material is lost on serialization: use a real texture asset
  (the kit uses Epic Toon FX `glow1.png`).
