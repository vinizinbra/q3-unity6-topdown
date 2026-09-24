# Tileset Platform Builder (cube layouts → autotiled cartoon platforms)

Runtime + edit-time replacement for `CubeVisualBuilder`: rebuilds the visuals of grid-aligned level
cubes (`PrototypeCube.obj`, pivot at the min corner) with autotiled tileset pieces. Code/assets live in
`Assets/Test/Environment/Tileset/`; Blender generators in that folder's `Source~/` (ignored by Unity).
All 14 prefabs in `_QuantumUser/Entities/LevelChunk/` use it (GrassCliff tileset).

## How it works

- **`TilesetPlatformBuilder`** sits on **each cube** (like `CubeVisualBuilder`; buttons **Generate
  Visual Tileset** / **Reset**). Merging is by collision, not hierarchy: every active builder in the
  same scene whose box (BoxCollider, else mesh bounds, computed from the transform) overlaps or touches
  this one at the same **top** height with the same `TilesetDefinition` joins its cluster, transitively
  and across chunks (neighbouring chunk floors become one platform). The cluster is rasterised to cells
  (grid anchored at the host's min corner), split into 4-connected platforms,
  solved as one shape per platform but each tile gets its OWN bottom = the highest bottom of the cells it covers (so a tile never hangs below the collider under it, e.g. the EnemyChunk-Bridge bridge merged with a deeper cube); Centers / edge runs only merge between tiles with the same bottom; spawned under `<host>_TilesetVisual`, parented to the host's nearest *unscaled* ancestor
  (chunk root / Traversal pivot). Only each cube's own MeshRenderer is hidden - children and colliders
  are untouched. The building cube becomes the cluster's host; members remember it, so rebuilding from
  any member (or a later chunk joining) tears the old result down first.
  - `autoGenerate`: **OnStart** (chunk cubes) queues into `TilesetBuildQueue`, which flushes once no
    cube has queued for 2 frames (max 60), so a level spawning over several frames builds each merged
    floor once; **OnEnable** for pooled objects; **Manual** = caller runs `Generate()`
    (`TraversalPlatformView`, `SkipScaledParent` on WallChunk).
  - `verticalOffset` (default -0.02): tiles sit 2 cm under the cube top (height unchanged) so their
    tops don't z-fight with anything authored exactly at the collider top.
  - `mergeWithNeighbours` off = tiled alone and never pulled into another cluster (TraversalPlatform).
- **`TilesetAutotiler`** (pure static solver), two modes chosen per `TilesetDefinition`:
  - **DualGrid** (current): tiles sit on grid *vertices* (half a cell off the cube grid) and cover the
    4 quarter cells around them → only 4 shapes: `Center` (4 solid), `Edge` (2 adjacent),
    `Corner` (1), `InnerCorner` (3); 2 diagonal quarters = two Corners. No fallbacks.
  - **PerCell** (legacy, used by the Outpost set): 15 per-cell configurations + a 2x2 InnerCorner.
  - Plain Center tiles are merged into greedy rectangles and one Center is stretched over each.
  - **Edge runs** (DualGrid): every straight run of same-facing Edge tiles is cut into segments of
    1..`maxEdgeLength` cells (stable hash of the run, longer segments weighted higher); each segment is
    ONE edge model stretched along the wall over those cells, pivot mid-segment. 2+ cells use `Edge2`
    (optional key, native 2 long) when the tileset has it, else `Edge`; a length is only allowed if
    its stretch <= `maxEdgeStretch` (defaults 3 cells / 1.5x -> 1 = Edge, 2 = Edge2, 3 = Edge2 x1.5).
    Variants flagged **No Stretch** (GrassCliff Edge/Edge2 V7 + V9: widened lip notches would open a
    walk-on-air gap) are only used unstretched. Keeps the 1-unit cube grid (38/61 chunk cubes are
    off a 2-unit grid); Corner/InnerCorner stay 1-unit.
- **`TilesetDefinition`** (ScriptableObject): mode, model prefix, merge-centers flag and
  `{Key, Model, Weight}` entries. Several entries per key = variants (`<prefix><key>_V<n>`), picked per
  tile by a stable hash, weighted. **Auto Fill From Folder** finds them (keeps existing weights).

Rotation convention: rotating a piece +90° about Y (clockwise from above) maps its N side to E;
masks rotate with `TilesetAutotiler.RotateMask`. Models are authored in Unity plan coordinates and
converted to Blender with (bx, by) = (-x, -z) so FBX imports unrotated (verified in the Editor).

## Per-world tileset (EnvironmentManager)

`WorldTheme.Tileset` picks the tileset per world: `EnvironmentManager.Load` calls
`TilesetPlatformBuilder.SetTilesetOverride(theme tileset, rebuild)`, which replaces every cube's own
`tileset` (the prefab value stays the fallback when a theme leaves it empty / no manager exists) and
regenerates clusters already built. Since each biome has its own tileset + ToonTerrain material,
the level LOOK (surface/wall colours, hatch, water depth colour, water line) is authored on that
material - the theme no longer writes into any level material (2026-09-24 cleanup). The theme only
keeps shared/non-material things: Sky (camera), water colours + opacity (shared lake material),
blood colour, obstacle/detail sprite pools. Keep each material's water depth colour matching its
theme's Sky. Mapping: GrasslandOutpost -> GrassCliff, NeonFloodDistrict -> NeonCity,
DesertOilFields -> DesertCliff, AlienPlanet -> AlienCliff, Moon -> MoonCliff, ArcticFields -> ArcticCliff, Haunted -> HauntedCliff. The override
is global, so applying a theme in the Editor also re-skins other loaded scenes' cubes (e.g. the
test scene) until another theme / `SetTilesetOverride(null, true)` rebuilds them.

## Tilesets

| Set | Folder | Mode | Notes |
|---|---|---|---|
| Grassland Outpost | `Tileset/Models` | PerCell | first prototype, palette material |
| Grassland Cliffs | `Tileset/GrassCliff` | DualGrid | current look, `ToonTerrain` material; **angular** style (2026-09-24: flat planar wall facets, flat shading, straight grass border; `GRASS_STYLE=organic` regenerates the old smooth-wave look); Edge V1-V10 + Edge2 V1-V10 (same variants 2 long, 1.7x wave length, 1.5x details, lip chips kept 1x wide for collider fit) |
| Desert Cliffs | `Tileset/DesertCliff` | DualGrid | same generator as GrassCliff with `BIOME=desert` (angular/geometric, big chamfers, Edge + Edge2 variants, dry roots, hexagonal cacti at the wall foot); `make_desert_textures.py` sand/sandstone; `DesertCliff_Toon` tinted from the DesertOilFields theme's Surface/Walls |
| Alien Cliffs | `Tileset/AlienCliff` | DualGrid | `BIOME=alien`: same geometric shape + EMISSIVE crystal clusters (cyan/magenta/acid, off below y -1) on Edge V2/V3/V6, purple/teal tendrils; `make_space_textures.py alien` (hex plates / crystal-fracture rock) |
| Moon Cliffs | `Tileset/MoonCliff` | DualGrid | `BIOME=moon`: same geometric shape, no plants (V4 roots -> loose rocks); `make_space_textures.py moon` (craters / grey rock) |
| Arctic Cliffs | `Tileset/ArcticCliff` | DualGrid | `BIOME=arctic`: same geometric shape, hexagonal icicles under the lip (V4) and pale ice-chunk clusters (lit, not emissive) on V2/V3/V6; `make_space_textures.py arctic` (snow drifts / faceted ice) |
| Haunted (Dracula castle) | `Tileset/HauntedCliff` | DualGrid | `BIOME=haunted`, Castlevania style tuned for the far gameplay camera: flat masonry walls (WALL_N 0.03, low wobble), big props - red/gold banners (V1/V4), torches with emissive flames (V2/V8), emissive gothic windows (V3/V5/V10), stone buttress on Corner V2; ruin variants V6/V7/V9 kept; `make_space_textures.py haunted` (flagstones / running-bond cut stone); light flagstone vs dark wall contrast, hatch 0.5, emission 1.5 (3 clips windows to white without post), blood-red water line + wine moat |
| Neon City | `Tileset/NeonCity` | DualGrid | straight panel walls + recessed plinth, baked emissive neon band that wraps corners, metal curb exactly on the collider, pavement top; 8 edge / 2 corner / 2 inner variants (windows, signs, vents, AC, cables, broken panel, neon tubes) |

Generators: `Source~/build_outpost_autotiles.py`, `build_grasscliff_autotiles.py`,
`build_neoncity_autotiles.py` (reuses the GrassCliff builder machinery with its own profile/props),
textures `make_toon_textures.py` / `make_neon_textures.py`. Run with
`Blender -b -P <script> -- <models_out_dir>`; each prints a `[fit]` report of how the walkable top edge
matches the cube collider (GrassCliff: raycast-based per 2.5 cm slice, robust to sparse angular geometry).

## ToonTerrain shader (`Tileset/Shaders/ToonTerrain.shader`)

One material per tileset, shared by every tile. World-space surface texture (top-down) and a single-read
dominant-axis wall texture, so nothing stretches when tiles are scaled/stretched. Mesh data baked by the generators:

- `UV0.x` strata coordinate: integers are outline lines (1–4 ledges, 5 cliff top, 6 surface
  outline), each group with its own width/strength; `-1` on a prop marks EMISSIVE geometry
  (neon, lit windows): unlit `COLOR.rgb * Neon Emission Strength` (HDR, bloom-ready), only above
  `Emission Min World Y` (NeonCity: -1, the water surface) - below it they turn a flat
  `Emissive Below Min Y Color` (black; alpha 0 = shaded like any prop).
- `UV0.y` distance into the surface (0 at the outline) → one-directional fade: the surface starts at
  the outline in `Fade Color` and fades into the surface texture inward. Nothing fades outside it.
- `COLOR.rgb` wall tint (terrain) or albedo (props); `COLOR.a = 1` = prop/foliage. Rock-type details
  (boulders, shards, holes, cracks) are shaded as terrain so they follow the wall colour.
- **Surface / Wall colours**: textures are GRAYSCALE and mapped `lerp(Dark, Light, tex)` with
  `Surface Light/Dark Color` and `Wall Light/Dark Color` (like the Modular Level material's noise
  pair); wall still x baked vertex tint. Texture generators write grayscale (`to_gray`, 2%/98%
  luminance -> black/white). Desert surface pair = theme Surface x Noise Light/Dark at 0.9 (the
  Modular Level maths). Hatch World Size defaults to 6.
- **Performance (2026-09-24)**: per pixel only ONE albedo read (surface OR wall, `[branch]` on the
  grass-outline step), wall + hatch use a dominant-axis planar UV (1 read each, not 3-read triplanar -
  seams only at flat-shaded facet creases), SH ambient per vertex -> 2 texture reads/pixel, vs 3-6 in Mobile Toon Modular Level and 7 before. Geometry is the heavier side now:
  tiles ~130-600 tris (old modular pieces ~21); a NeonCity level = ~4.7k tile renderers / 1.5M tris
  total, ~85k tris in the game camera per frame (culled).
- **Water depth gradient** (`_Gradient*`, world Y: blends the FINAL colour into a flat unlit Bottom
  Color below Start Y - no shading/shadow/hatch there, so walls melt into the water/background) and **water line** (`_WallLine*`,
  anti-aliased band at a world Y, walls only, drawn unlit on top of shadows/hatching) - same maths as the Modular Level shaders; authored per
  material (GrassCliff_Toon: bottom = GrasslandOutpost Sky, y -3 -> 0). Both default to strength 0.
- Toon main light + received shadows; hatching is a **multiply**: in the hatch texture black = ink,
  white = nothing (R single lines, G cross lines). Strong on walls, `Hatch On Surface` scales grass.
- Main directional light only (additional lights not toon-shaded yet).

## Design constraints (agreed with the user)

- The **walkable top edge must match the cube collider**: edges within ~1–6 cm, lip may overhang
  ≤ 3 cm. Corners are big diagonal chamfers set well back from the cube corner (user choice,
  2026-09-24): outer corners start ~22–23 cm in from the collider's square corner (chamfer R 0.40
  GrassCliff / 0.38 NeonCity), inner corners reach ~14–20 cm into the empty cell (R 0.30 / 0.28).
- Side profile is an undercut (`___ \`): the lip is the widest point, walls tuck in below it.
- Walls: one continuous face (no horizontal strata bands), smooth waves only (no per-sample noise),
  geometric chamfer corners with hard vertical creases, details (holes, pebbles, shards, cracks that
  can cross the lip into the surface) sit on the deformed wall surface.
- Deformation is zero at tile borders so any variant meets any other.

## Current status

Chunk prefabs migrated from `CubeVisualBuilder` (2026-09-24); verified in the Editor by placing
EnemyChunk x2 + Bridge + rotated Crossroads side by side (floors merged into one cluster) and on
`TilesetTest.unity` (PrototypeCube layout copies for Outpost / GrassCliff / NeonCity). Not yet verified
in Play mode with the real level generator. `CubeVisualBuilder` stays for the scenes that still use it
(GrasslandOutpostGameScene, CubeVisualBuilderTTest).

Known gaps: no `avoidNearWallDetails` equivalent (no chunk used it); a cube destroyed at runtime
doesn't rebuild its cluster (chunks are persistent); a cluster's tiles live under the host chunk;
no toon additional lights; outer silhouette outline would need a screen-space edge pass.
