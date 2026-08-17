# Environment Details (ground/wall cosmetic detail slots)

A View-only companion to the hand-authored "cube generator" (`CubeVisualBuilder`) - the artist
hand-places small cosmetic prop GameObjects (rocks, foliage, debris, cracks, pipes...) directly in
each chunk prefab, positioned/rotated exactly as intended in the Editor; at runtime, a script only
ever decides *whether* a given placed slot shows anything at all and, if so, *which* themed sprite,
both deterministically. Deliberately simulation-free: it never touches Quantum state or adds a `.qtn`
component, since none of this affects gameplay - but the sprite pick still needs to look the same
for every client and every split-screen instance in a match, and not reshuffle on a chunk rebuild,
so it's seeded from `RuntimeConfig.Seed` (shared/replicated for the whole match) combined with each
chunk's own stable grid coordinates rather than plain `UnityEngine.Random`.

**This replaces an earlier, fully-procedural version** of this feature (computed positions/rotations
from `ChunkWallCube` bounds and per-cell density rolls, with camera-angle scale-compensation knobs).
That approach surfaced real friction while testing - `ChunkWallCube` boxes turned out to be
room-spanning rather than thin wall strips, floor height had to be derived from a box's `max.y` not
`min.y`, and correct-looking flat/wall orientation needed non-uniform scale hacks to counteract the
camera's tilt. Hand-placing lets the artist get position/rotation/orientation exactly right by eye
once, per prop, with zero geometry-inference risk - the runtime script's job shrinks to just "pick a
sprite," which is what this doc now describes. The old design/bugs are preserved in git history if
ever needed again.

## What this adds

- **`GroundDetailSlot`/`WallTopDetailSlot`/`WallMidDetailSlot`** (`Assets/_QuantumUser/View/World/`)
  - three small marker `MonoBehaviour`s (global namespace, matching `ChunkWallCube`'s own
  convention), each `[RequireComponent(typeof(SpriteRenderer))]`. The artist places one per intended
  prop directly in a chunk prefab, with a placeholder `Sprite` assigned to the `SpriteRenderer` for
  Editor preview, and authors its `Transform` (position/rotation/scale) exactly as wanted. Each also
  has its own `WorldSize` field - the intended world-unit size across whichever sprite ends up
  assigned, so swapping the sprite at runtime (see below) never changes how big the slot reads in the
  scene. Wall is split into Top/Mid (not one generic wall type) since a prop suited to the upper
  portion of a wall (vents, cracks) usually doesn't suit its middle/base (moss, pipes, scuffs) and
  vice versa. Three distinct types overall (not one generic type with a Ground/WallTop/WallMid enum)
  so each unambiguously draws only from its own `WorldTheme.Details` pool, and so both wall types
  alone get `EnvironmentManager.DetailSpriteMaterial` assigned without an extra per-slot flag.
- **`WorldDetailTheme`/`WorldTheme.Details`** (`Assets/_QuantumUser/View/World/WorldTheme.cs`) - a
  `List<Sprite>` + a `[Range(0,1)] *DetailChance` pair per slot type -
  `GroundDetails`/`GroundDetailChance`, `WallTopDetails`/`WallTopDetailChance`,
  `WallMidDetails`/`WallMidDetailChance`. The list is picked from with equal probability (no
  per-sprite weight, no scale-variance range); the chance is the probability a given placed slot of
  that type shows anything at all (`0` hides every slot of that kind, `1` always shows one) - a slot
  with an empty matching pool stays disabled regardless of chance. Everything else (how many props
  exist, their size, position, rotation) is an Editor-authoring concern on the slots themselves, not
  theme data.
- **`ChunkDetailScatter`** (`Assets/_QuantumUser/View/World/ChunkDetailScatter.cs`) - a
  `CustomQuantumEntityViewComponent` (same one-shot Frame/EntityRef-bound-once base class as before,
  `Initialize`/`QUpdate` retry-until-ready shape `ColliderVisualScaleView` also uses - retries until
  `Chunk` is readable **and** a `WorldTheme` is active, so a theme assigned after this chunk already
  initialized still gets picked up on a later frame). Sits on the same chunk View prefab root that
  already carries `QuantumEntityView` (`LevelChunk.prefab`/`EnemyChunk.prefab`/`WallChunk.prefab`).
  Only reads `Chunk.OriginCellX/Z` (for the seed) - no `Transform3D`, no `ChunkWallCube` bounds, no
  footprint/floor-height math of any kind anymore.

  **Per-slot resolution** (`ResolveSlot`): finds every `GroundDetailSlot`/`WallTopDetailSlot`/
  `WallMidDetailSlot` under this chunk (`GetComponentsInChildren`, once per type) and, per slot,
  rolls `rng.NextDouble() < chance` - if it fails (or the matching pool is empty), disables that
  slot's `SpriteRenderer` entirely (`enabled = false`) and stops; if it passes, picks a sprite (equal
  probability), assigns it, and sets `localScale = Vector3.one * (worldSize * ResolveUnitScale(sprite))`
  - `ResolveUnitScale` normalizes away the picked sprite's own pixel size/`Sprite.bounds`/PPU so
  `worldSize` always means true world units regardless of that sprite's import settings. **Never
  touches position or rotation** - those stay exactly as the artist authored them; a wall decal
  facing the wrong way or a ground prop floating above the floor is now purely an authoring fix in
  the Editor, not a bug in this script.

  **Determinism**: one `System.Random` per chunk, seeded from `frame.RuntimeConfig.Seed` combined
  with `Chunk.OriginCellX`/`OriginCellZ` via a manual integer hash - deliberately **not** .NET's
  `HashCode.Combine`, which mixes in a random per-process seed by design (hash-flood mitigation) and
  would break cross-client consistency. Every slot draws from the *same* sequence in
  `GetComponentsInChildren`'s stable hierarchy order, so different slots naturally get different
  (but still deterministic and reproducible) outcomes without needing a per-slot seed.

  **Testing** (`[Button]`, NaughtyAttributes): `ChunkDetailScatter.Regenerate` (public - visible on
  the component in the Inspector during Play Mode) re-rolls every slot on this one chunk instance
  immediately (bypasses the "already generated" guard, logs `verbose` reasoning so a manual click
  always says exactly why it did or didn't run). `WorldTheme` itself also has a **`Regenerate All
  Chunk Details (Debug)`** button (`FindObjectsByType<ChunkDetailScatter>` + call `Regenerate` on
  each) to re-roll every chunk in the scene at once while tuning a theme's `Details` pool live.
  Runtime-only overall - there's no Editor-time (non-Play) preview, since the seed needs a live
  `Frame`; the artist's placeholder sprite is what previews in Edit Mode instead.

- **`CubeVisualBuilder` detail avoidance** (`Assets/_QuantumUser/View/World/CubeVisualBuilder.cs`) -
  optional, opt-in coordination so a wall's own baked edge/corner texture doesn't clash with an
  *actually-shown* wall detail nearby. One new bool, `avoidNearWallDetails` - no separate prefab to
  assign, it just forces `edgePrefabs[0]`/`outerCornerPrefabs[0]` (element 0 of whichever list is
  already authored) instead of the usual `PickVariant(edgePrefabs)`/`PickVariant(outerCornerPrefabs)`
  random pick, for any cell within `detailAvoidRadius` (`IsNearShownDetail`, **XZ-only** - Y is
  deliberately ignored, since `worldPosition` here is always at this cube's own local Y origin (its
  bottom pivot) while a hand-placed detail sits wherever the artist actually put it on the wall
  surface, typically much higher; comparing full 3D distance silently failed the check almost
  everywhere. For an edge run specifically, the check runs against the run's actual *center* -  where
  it's really drawn - not its starting cell, which for a multi-cell-wide run could sit up to 1 world
  unit away and silently push the check outside a modest radius even though single-cell corners, with
  no such offset, still worked) of a *shown* detail. "Shown" is the key
  word - a `WallTopDetailSlot`/`WallMidDetailSlot` GameObject existing is **not** enough on its own,
  since whether it actually displays a sprite is a runtime, seeded roll only `ChunkDetailScatter`
  resolves, and `CubeVisualBuilder` has no reliable lifecycle ordering against that (`Start()` here vs.
  Quantum's `OnEntityInstantiated` timing there) to infer it safely. So instead of guessing:
  `CubeVisualBuilder.Start()` skips its own auto-`Generate()` entirely whenever
  `HasDetailAvoidance` (`avoidNearWallDetails`) is true, and waits - `ChunkDetailScatter`,
  right after resolving every wall slot in the chunk, collects the world positions of only the ones
  that actually passed their chance roll, sets that list on every avoidance-enabled
  `CubeVisualBuilder`'s `ShownDetailPositions`, and calls `Generate()` explicitly, once. If nothing
  ended up shown that chunk (chance missed everything, or nothing was placed), the list is empty and
  every avoidance cube just generates normally, unrestricted anywhere - matching the actual request
  ("if there's no wall detail in that chunk, we don't restrict; if there is, restrict only in that
  area"). **Known gap**: if an avoidance-enabled cube is also merged with a *non*-avoidance neighbor,
  that neighbor's own ungated `Start()` draws this cube's cells too (`DrawMergingNeighbors`) before
  `ChunkDetailScatter` ever sets `ShownDetailPositions`, so the first pass sees an empty list and the
  later explicit `Generate()` call redraws the whole cluster again - not handled, since this game's
  actual usage is one room-spanning, non-merged box per room; avoid combining detail avoidance with a
  merged cube elsewhere.

- **`Project/Detail Sprite Height Fog`**
  (`Assets/_QuantumUser/View/Rendering/Shaders/DetailSpriteHeightFog.shader`) - a small, dedicated,
  `SpriteRenderer`-compatible shader (alpha-blended, vertex-color-tinted) that reimplements just the
  Height Fog block from `Project/Mobile Toon Modular Level`'s own fragment shader (same math, same
  `_HeightFogColor`/`_HeightFogTopY`/`_HeightFogFalloff`/`_HeightFogStrength` properties) - that
  level shader itself is opaque and mesh-oriented (custom vertex-color-encoded wall/surface role, no
  alpha blending) and would render broken garbage if assigned directly to a `SpriteRenderer`.
  `EnvironmentManager` gained a `detailSpriteMaterial` field (a `Material` using this shader)
  alongside its existing `levelMaterial`, kept in sync automatically every `Load()`
  (`ApplyDetailSpriteHeightFog`) - Color from the same `environment.Sky` the level material gets, and
  TopY/Falloff/Strength copied straight from `levelMaterial`'s own current values, so there's nothing
  to keep in sync by hand across the two separate Material assets. Applied to wall slots only, via
  `renderer.sharedMaterial` (never `.material`, which would silently instantiate a per-renderer copy
  and defeat the whole "one Material, tinted once" point) - ground slots keep whatever material the
  artist assigned by hand when placing them.
- **`EnvironmentManager.Instance`/`CurrentTheme`/`DetailSpriteMaterial`**
  (`Assets/_QuantumUser/View/World/EnvironmentManager.cs`) - small additions so there's one place to
  ask "which `WorldTheme` is currently active" and "what Material do wall detail slots use." Nothing
  else about `EnvironmentManager` changed; it still only tints the shared level Material/camera
  background, and nothing yet decides "current level's theme" for real (`initialTheme` is still a
  debug-preview-only field) - that broader theme-selection problem is explicitly out of scope here.

## No Simulation/`.qtn` changes

Confirmed intentionally View-only/cosmetic - no new component, no codegen step, so this can be
iterated on purely by pressing Play (no "Quantum codegen gotcha" applies here, see CLAUDE.md).

## Current status / what's still needed

The code compiles as-is (no codegen dependency). Nothing shows yet, though:

1. Hand-place `GroundDetailSlot`/`WallTopDetailSlot`/`WallMidDetailSlot` GameObjects in each chunk
   prefab under `Assets/_QuantumUser/Entities/LevelChunk/` (`LevelChunk.prefab`, `EnemyChunk.prefab`,
   `WallChunk.prefab`, and any others authored later) - each needs a placeholder `Sprite` on its
   `SpriteRenderer` and an authored `WorldSize`/position/rotation. None exist yet.
2. Add a `ChunkDetailScatter` component to each of those same chunk prefabs' root (same root that
   already has `QuantumEntityView`) - it finds the slots as children, wherever in the hierarchy
   they're placed.
3. Author `WorldTheme.Details` (`GroundDetails`/`WallTopDetails`/`WallMidDetails` sprite lists, and
   the matching `GroundDetailChance`/`WallTopDetailChance`/`WallMidDetailChance` - all default to
   `0`) on whichever `WorldTheme` asset ends up loaded for a level - until then every slot's chance
   check fails (or finds an empty pool) and stays hidden.
4. Something needs to actually call `EnvironmentManager.Load(theme)` for the level actually being
   played, not just `initialTheme`'s Awake-time debug preview - same pre-existing "no real 'current
   world' source yet" gap `EnvironmentManager`'s own header comment already flagged. Until then, use
   `WorldTheme`'s own `Apply To Scene (Debug)` button (or `EnvironmentManager.initialTheme`) to set a
   `CurrentTheme` during Play Mode, then `ChunkDetailScatter`'s `Regenerate (Test)` button (or
   `WorldTheme`'s `Regenerate All Chunk Details (Debug)`) to see it.
5. No `Material` asset exists yet for `Project/Detail Sprite Height Fog` - create one in the Editor
   (`Create > Material`, assign the shader), assign it to `EnvironmentManager.detailSpriteMaterial`.
   Not hand-authored as a `.mat` file here since it needs the shader's own Unity-generated GUID,
   which doesn't exist until Unity imports the `.shader` file at least once. Wall slots render fine
   without it (fall back to whatever material the artist put on the placeholder), just without
   height fog.
6. Detail avoidance (`avoidNearWallDetails`) is entirely optional and `false` by default - a
   `CubeVisualBuilder` with it off behaves exactly as before this feature (auto-generates at its own
   `Start()`, zero coupling to `ChunkDetailScatter`). To use it, tick the bool on whichever cubes
   should react to nearby wall details (element 0 of `edgePrefabs`/`outerCornerPrefabs` should be
   authored as the plain/neutral variant) and tune `detailAvoidRadius`.

Not yet manually verified end-to-end in-Editor under this hand-placed-slot design (the earlier
procedural version was verified working before being replaced - see this doc's own history).

## Explicit non-goals for this pass

- No per-`ChunkType` theming (Enemy vs Boss vs Merchant) - one `WorldTheme.Details` pool applies
  uniformly across every chunk type in a level, matching how `WorldObstacleTheme`/`Environment`
  already work.
- No per-sprite weight in `GroundDetails`/`WallTopDetails`/`WallMidDetails` - equal-probability pick only, kept
  deliberately simple.
- No pooling/despawn-and-respawn, no dynamic slot creation - slots are permanent, hand-placed parts
  of the chunk prefab; `ChunkDetailScatter` only ever toggles `SpriteRenderer.enabled`/swaps
  `.sprite`/rescales, never creates or destroys GameObjects.
