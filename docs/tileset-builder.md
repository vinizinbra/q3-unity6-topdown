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

## Surface scatter (props on top of platforms)

`TilesetDefinition` holds three weighted prop lists (`ScatterEntry {Model, Weight, ScaleRange, AnyYaw,
Cells}`): **Ground** (small walk-through decor for walkable floors - rocks, tufts, cables; density per
interior cell), **Rooftop** (big props for raised blocks players can't walk on - containers, machines,
antennas; `Cells = 2` needs a free interior neighbour) and **Rooftop edge** props (fence segments placed
on Edge/Edge2 tiles, facing like the tile, pushed 0.3 inward, stretched to the tile's length).
`TilesetPlatformBuilder.surfaceDecor` picks the mode per cube (host's value): **Auto** = Ground when the
platform's bottom is below `groundBelowY` (-0.5: floors rising out of the pits, incl. Traversal
platforms), else Rooftop; or force None / Ground / Rooftop. Props only go on interior cells (all 8
neighbours solid, so never on the lip/fade), at most one per cell, positions/yaw/scale hashed from
WORLD cell coords + `variationSeed` (same layout = same props whichever cube hosts). Props are separate
objects at real size (tiles stretch with platform height, props don't), no colliders (visual only).

**Wall props** (`TilesetDefinition.wallScatter`, `wallDensity`): one chance per CELL of wall length (a merged/stretched Edge2 gets 2-3 slots along it), spawned as
separate real-size objects (not stretched with the wall). Embedded props sit in the band below the lip
(upper ~2 units of the wall, never below `wallPropMinY` = above the water line), pushed a little into the
face at that height's inset; `Foot` props stand at the tile's bottom, only where that ground is visible
(bottom >= `groundBelowY`, i.e. raised blocks - not floors dropping into the pits). They also need real ground in front of the wall: some cube's top at the foot height under both ends of the prop's footprint (`HasGroundAt`, from all cubes' boxes - no physics), so nothing floats under bridges / overhangs or hangs over pit edges. Chance is weighted by
facing vs `cameraViewDirection` (gameplay camera looks +Z): full on walls facing the camera, half on side
walls, none on walls facing away (never seen). Authoring: face -Z, pivot at the mount point, lowest
point at height 0. `wallPropAvoidBand` (fraction of wall height, + `wallPropAvoidMargin`) is a keep-out band props never overlap, e.g. NeonCityV4's baked cyan trim (0.73-0.83): embedded props go fully below or fully above it (real height = mesh bounds x scale), foot props shrink to fit under it (skipped below 70%). The gameplay camera is far (FOV 14, Y 36) - props need ~0.5-1 unit to read.

## Per-world tileset (EnvironmentManager)

`WorldTheme.Tileset` picks the tileset per world: `EnvironmentManager.Load` calls
`TilesetPlatformBuilder.SetTilesetOverride(theme tileset, rebuild)`, which replaces every cube's own
`tileset` (the prefab value stays the fallback when a theme leaves it empty / no manager exists) and
regenerates clusters already built. Since each biome has its own tileset + ToonTerrain material,
the level LOOK (surface/wall colours, hatch, water depth colour, water line) is authored on that
material - the theme no longer writes into any level material (2026-09-24 cleanup). The theme only
keeps shared/non-material things: Sky (camera), water colours + opacity (shared lake material),
blood colour, obstacle/detail sprite pools. Keep each material's water depth colour matching its
theme's Sky. Mapping: GrasslandOutpost -> GrassOutpostV2 (GrassCliff kept), NeonFloodDistrict -> NeonCityV4 (V1-V3 kept),
DesertOilFields -> DesertOilV2 (DesertCliff kept), AlienPlanet -> AlienV2 (AlienCliff kept), Moon -> MoonV2 (MoonCliff kept), ArcticFields -> ArcticV2 (ArcticCliff kept), Haunted -> HauntedCliff, Zaun -> ZaunCliff, FeudalJapan -> JapanCliff, Inferno -> InfernoCliff. The override
is global, so applying a theme in the Editor also re-skins other loaded scenes' cubes (e.g. the
test scene) until another theme / `SetTilesetOverride(null, true)` rebuilds them.

## Tilesets

| Set | Folder | Mode | Notes |
|---|---|---|---|
| Grassland Outpost | `Tileset/Models` | PerCell | first prototype, palette material |
| Grassland Cliffs | `Tileset/GrassCliff` | DualGrid | current look, `ToonTerrain` material; **angular** style (2026-09-24: flat planar wall facets, flat shading, straight grass border; `GRASS_STYLE=organic` regenerates the old smooth-wave look); Edge V1-V10 + Edge2 V1-V10 (same variants 2 long, 1.7x wave length, 1.5x details, lip chips kept 1x wide for collider fit) |
| Alien V2 | `Tileset/AlienV2` | DualGrid | (2026-09-25, from 2 overgrown-alien-ruins refs) `BIOME=alien2`: natural grassland-style angular cliffs (not FLAT) with fractured-rock wall texture, dark indigo walls vs light lavender floor for contrast, calm floor (few faint rune rings), 12 wall props: embedded pink rune line / rune circle, lime slime drips, glowing purple crystals; foot pink mushroom cluster + big mushroom, crystal cluster, cyan glow tufts, eye stalks, stone rubble, rune pillar, slime puddle (no barrels); uses the new Raised Surface tint (floor = slate, raised blocks from y 1.5 = teal moss) + lime rim; AlienPlanet theme uses it (AlienCliff kept; water left as the user tuned it) |
| Moon V2 | `Tileset/MoonV2` | DualGrid | (2026-09-25) `BIOME=moon2` = MoonCliff tiles with plain walls + 13 wall props: embedded meteorite (glowing ember cracks), satellite debris, bent antenna with red beacon, cyan glow crystals, metal hatch; foot lander leg, crashed probe, generic mission flag, astronaut helmet, rover wheel, supply crates, moon rocks x2 (no barrels); MoonV2_Toon walls fade into Space Color (gradient -2.5 -> +0.8, wall line off); Moon theme uses it + the Starfield floor (MoonCliff kept) |
| Arctic V2 | `Tileset/ArcticV2` | DualGrid | (2026-09-25) `BIOME=arctic2` = ArcticCliff tiles with plain walls (baked icicles/ice chunks removed, V3 boulder shrunk) + 17 wall props: embedded ice shards + big shard cluster, icicle cluster, frozen pipe, mammoth tusk, bones, frozen skull; foot upward ice spike clusters x2, snow drift, ice blocks, snow-capped crates, red sled, snowman, signpost, icy rocks x2 (no barrels; ice props weighted 3 so it stays spiky like V1). Ground is a MID-TONE frozen blue-grey (white hid all the white/pale props) with a white snow rim at the outline (fade colour, 0.18); props are bright snow / pale cyan ice; wallDensity 0.5; ArticFields theme uses it (ArcticCliff kept, water untouched) |
| Desert Oil V2 | `Tileset/DesertOilV2` | DualGrid | (2026-09-25) `BIOME=desert2` = DesertCliff tiles with plain walls (baked cacti removed, V3 boulder shrunk) + 18 wall props: embedded rusty pipe, pipe + red valve wheel, oil leak, cow skull, bones, ammonite fossil, rusty sheet; foot pumpjack, jerry cans (no barrels), sandbags, lying pipe, tyre stack, crates, flammable sign, sandstone rocks x2, dead bush, oil puddle; wallDensity 0.4, scale ~0.9-1.3; material copied from the user's DesertCliff_Toon; DesertOilFields theme uses it (keeps its user-tuned dark oil water) |
| Desert Cliffs | `Tileset/DesertCliff` | DualGrid | same generator as GrassCliff with `BIOME=desert` (angular/geometric, big chamfers, Edge + Edge2 variants, dry roots, hexagonal cacti at the wall foot); `make_desert_textures.py` sand/sandstone; `DesertCliff_Toon` tinted from the DesertOilFields theme's Surface/Walls |
| Alien Cliffs | `Tileset/AlienCliff` | DualGrid | `BIOME=alien`: same geometric shape + EMISSIVE crystal clusters (cyan/magenta/acid, off below y -1) on Edge V2/V3/V6, purple/teal tendrils; `make_space_textures.py alien` (hex plates / crystal-fracture rock) |
| Moon Cliffs | `Tileset/MoonCliff` | DualGrid | `BIOME=moon`: same geometric shape, no plants (V4 roots -> loose rocks); `make_space_textures.py moon` (craters / grey rock) |
| Arctic Cliffs | `Tileset/ArcticCliff` | DualGrid | `BIOME=arctic`: same geometric shape, hexagonal icicles under the lip (V4) and pale ice-chunk clusters (lit, not emissive) on V2/V3/V6; `make_space_textures.py arctic` (snow drifts / faceted ice) |
| Haunted (Dracula castle) | `Tileset/HauntedCliff` | DualGrid | `BIOME=haunted`, Castlevania style tuned for the far gameplay camera: flat masonry walls (WALL_N 0.03, low wobble), big props - red/gold banners (V1/V4), torches with emissive flames (V2/V8), emissive gothic windows (V3/V5/V10), stone buttress on Corner V2; ruin variants V6/V7/V9 kept; `make_space_textures.py haunted` (flagstones / running-bond cut stone); light flagstone vs dark wall contrast, hatch 0.5, emission 1.5 (3 clips windows to white without post), blood-red water line + wine moat |
| Zaun (Arcane undercity) | `Tileset/ZaunCliff` | DualGrid | `BIOME=zaun`: flat built walls (like the castle), big props - copper pipe runs (V1/V8 + valve), rusty drain pipe + hanging teal lamps (V2/V4), glowing chem tank at the foot (V3), pink / teal neon signs (V5/V10), copper pipe on Corner V2; `make_space_textures.py zaun` (riveted deck plates + grates / rusted wall panels with streaks); chem-green fade + water line, toxic green water, smog sky; emission 1.8 |
| Feudal Japan | `Tileset/JapanCliff` | DualGrid | `BIOME=japan`: flat built walls, big props - red paper lanterns (V1/V8, emissive), stone toro with glowing fire box (V2 + Corner V2), sakura tree (V3), bamboo grove (V4), wooden gate panel (V5), shimenawa rope + shide papers (V10); `make_space_textures.py japan` (seamless Voronoi stepping stones + moss gaps / ishigaki fitted stones); sunset sky, koi-pond teal water, white water line, emission 1.6 |
| Inferno (hellish volcano) | `Tileset/InfernoCliff` | DualGrid | `BIOME=inferno`: angular basalt walls, EMISSIVE lava - glowing cracks (V1/V8, V10 crosses the lip into the ground), lava drips / wide cascade from the lip + magma pools at the foot (V2/V4), brimstone vent (V5); obsidian spikes jutting out (V3/V5/V8 + Corner V2); `make_space_textures.py inferno` (Voronoi cooled-lava crust / basalt column bands); molten orange fade rim, lava 'water' (opacity 1), crimson sky; Emission Min Y OFF (-1000) so lava glows all the way down |
| Grassland Outpost V2 | `Tileset/GrassOutpostV2` | DualGrid | concept pass (2026-09-25), `BIOME=grass2`: GrassCliff angular tiles + WALL props only (user rejected ground/rooftop scatter): embedded in the face - tyre, half tyre, skull, bones, rusty pipe, metal sheet, rebar; leaning at the foot - tyre stack, lean tyre, planks, ladder, crates (no barrels - the map already has them), warning board, shovel, purple rock clusters x2; wall density 0.75 (camera-facing walls), props at their original ~0.9-1.3x scale (user: bigger read worse), no ladder; material copied from the user's GrassCliff_Toon; GrasslandOutpost theme uses it (GrassCliff kept) |
| Neon City V4 | `Tileset/NeonCityV4` | DualGrid | (2026-09-25) `BIOME=neon4` = V3 tiles (curb border, cyan fade, trim, pink corner strips) with PLAIN walls; street life moved to 15 wall props (real size, not stretched): wall-mounted pink/cyan posters, AC, neon sign, vent; at the foot shopfront, door+lamp, pipe, crates, cardboard boxes, trash bags, vending machine, cone, market tent, street lamp (no barrels); wallDensity 0.55, scale 1.5-1.7; NeonFloodDistrict theme uses V4 (V1-V3 kept) |
| Neon City V3 | `Tileset/NeonCityV3` | DualGrid | variation (2026-09-25), `BIOME=neon3` = V2 (trim, corner strips, shops/props, textures) + V1's border: vertical metal curb exactly on the collider edge rounding onto a flat 7 cm strip (profile rows 6-9), and V1's soft cyan surface fade (fade 0.16/0.94/1 at 16.5%, over the whole fade range); NeonFloodDistrict theme currently uses V3 (V1 + V2 kept) |
| Neon City V2 | `Tileset/NeonCityV2` | DualGrid | concept pass (2026-09-25), `BIOME=neon2`: same chamfered block architecture, flat dark navy panel walls with a CONTINUOUS cyan neon trim baked into the wall profile (emissive rows between z .73-.83, so it wraps every edge/corner/inner corner), pink neon strip on every corner chamfer; props - lit storefront + magenta awning + counter/stools (V1), pink neon poster + pipe (V2), door with lamp + crates/yellow barrels (V3), street lamp + barrels (V4), big pink billboard (V5), AC unit + pipe (V8), pink market tent (V10); `make_space_textures.py neon2` (street plates, grates, hazard chevrons, manhole / riveted wall panels); pale rim at the top outline (short light fade), purple pits; NeonFloodDistrict theme now uses it (Sky purple) - V1 kept |
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
- **Raised Surface tint** (`_RaisedLightColor/_RaisedDarkColor`, `_RaisedFromY`, `_RaisedStrength`, default 0 = off): surfaces above a world Y use their own Light/Dark pair so height reads at a glance (AlienV2: floor slate, raised moss). No extra texture read.
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
