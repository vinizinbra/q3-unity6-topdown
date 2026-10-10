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
    (`TraversalPlatformView`, `SkipScaledParent` on WallChunk - it queues into `TilesetBuildQueue`
    after reparenting, so every gap filler spawned together costs one rebuild).
  - **WallChunk** (the `LevelGenerationSystem.FillInnerGaps` gap filler, 2026-09-26) is two cubes:
    `FloorCube` (y -3..1, same top/bottom as chunk floors, so it merges into the surrounding floor
    cluster - no lip/undercut seam showing water) and `VisualCube` = the wall block on top (y 1..3.5).
    Its Quantum collider is a Compound of the two boxes (`ChunkCompoundColliderBuilder`);
    `SpawnGapFillerRun` stretches every child box's X/Z over the merged run, and
    `ColliderVisualScaleView` sizes the wrapper from the compound's union (visualUnitSize Y 6.5 =
    that union's height, so Y scale stays 1).
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

**Wall runs** (`TilesetDefinition.wallRuns`, `WallRunSet`; 2026-09-26): continuous modular runs (pipelines
now; cables / fences / chains / ropes planned for other biomes) along the walls, laid in `ScatterWallRuns`
BEFORE the wall props. Edges are split into unit cells and grouped into lines (same facing, same wall-plane
coordinate, same bottom); consecutive cells form a stretch. Stretches are then chained through the Corner /
InnerCorner tiles between them (`BuildRunChains`: a convex corner at vertex v, rotation r joins the rot r+1 line
ending at v + R(0,1) to the rot r line starting at v + R(1,0); a concave one joins the rot r line ending at
v + R(-1,0) to the rot r+1 line starting at v + R(0,-1)) when the run has a `Corner` / `InnerCorner` module.
Walls facing away from the camera are never part of a chain. Each chain is a slot list along the walls' +X.
Per chain: roll `Chance` (x0.5 on side walls), lay a run of `Length` slots at `top - BelowTop` (skipped on walls
lower than `MinWallHeight`, below `wallPropMinY`), gap `MinGap`, repeat. Runs start and end on straight cells,
never on a corner. Middle cells pick weighted `Straight` modules, or `Decorated` ones (`DecoratedChance`, at least
`DecoratedSpacing` plain cells apart, never next to a corner); each end is `EndDown` + a `Drop` stretched to the
ground + `DropFoot` when there's ground in front (`HasGroundAt`, `DownEndChance`), else `EndWall`. Straight modules
are 1 cell along X, facing -Z, pivot on the wall face, centreline at `CenterHeight`; End modules are authored as
the +X end and mirrored (wrapper transform with scale.x = -1, which also carries the Drop's Y stretch). Corner
modules are authored in the corner TILE's frame (vertex at the origin) and placed with their pivot pushed in by
the run's inset along both walls; their straight stubs reach past the tile, a hair thicker, so the seam with the
neighbour is covered whatever the inset (generator `Piece.clamp = False`). The desert ones follow the chamfer:
straight, 45 deg bend, a diagonal parallel to the chamfer at the pipe offset, 45 deg bend, straight. No jitter /
random scale so joints line up; deterministic from the same hash as the scatter. Run cells reserve their height
band (`runReserved`) and wall props overlapping it skip (a drop reserves the whole cell below). Generator: the
`# ---- WALL RUN modules` block at the end of `desert2_props()` (pipe helpers reusable per biome).

Pipe ink (2026-09-26): smooth tubes have no crease for the feature-edge lines, so `Piece.tube(pts, r, pal, view)`
(one continuous tube - rings shared, no internal caps, so bends draw no joint rings) puts two facet edges exactly
on the silhouette for the FIXED camera (`cross(tangent, view)`) and adds them to `Piece.ink_edges`, which
`finish()` always inks. The silhouette depends on the wall's facing, so every pipe module is generated twice: front
walls (view `(0, 1, -1)` in the module frame) and `_Side` (view `(1, 0, -1)`); corners come as plain (-Z arm facing
the camera) and `_XFront` (-X arm facing it). The builder picks them: `ScatterEntry.SideModel` for Straight /
Decorated, `EndWallSide` / `EndDownSide` / `DropSide`, `CornerXFront` / `InnerCornerXFront` (`WallRunSet.Pick`,
empty = the base module). Pipes are charcoal (`4A4F5B`), not black, so the black ink reads on them. Runs were
de-noised: plain Straight (weight 5) with occasional Joint / Clamp, Decorated 0.22 chance, 3 cells apart; the
valve wheel is a bigger (0.19) bright red single torus with only its rim outlined.

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
| Desert Oil V2 | `Tileset/DesertOilV2` | DualGrid | (2026-09-25) `BIOME=desert2` = DesertCliff tiles with plain walls (baked cacti removed, V3 boulder shrunk) + 18 wall props: embedded rusty pipe, pipe + red valve wheel, oil leak, cow skull, bones, ammonite fossil, rusty sheet; foot pumpjack, jerry cans (no barrels), sandbags, lying pipe, tyre stack, crates, flammable sign, sandstone rocks x2, dead bush, oil puddle; wallDensity 0.4, scale ~0.9-1.3; material copied from the user's DesertCliff_Toon; DesertOilFields theme uses it (keeps its user-tuned dark oil water). Oil-field + fossil pass (2026-09-26, from a pipeline ref): + embedded ValveWheel, PressureGauge, PipeClamp (clamped pipeline run), ControlBox, Spine, DinoSkull; foot PipeElbow, Compressor, WellHead, GrateFence, Bollards, Ribcage, Tusk ; pipes restyled black with orange bands + steel flanges, crates red with an X; edge lines on all but OilLeak/OilPuddle/DeadBush. The one-cell PipeClamp prop was then dropped from the scatter (30 props) for the **Pipeline wall run** (`Run_Pipe_*`: Straight / Straight_Clamp / Straight_Bands, decorated Valve / Gauge / Box / Pump, EndWall, EndDown + Drop + DropFoot) |
| Desert Cliffs | `Tileset/DesertCliff` | DualGrid | same generator as GrassCliff with `BIOME=desert` (angular/geometric, big chamfers, Edge + Edge2 variants, dry roots, hexagonal cacti at the wall foot); `make_desert_textures.py` sand/sandstone; `DesertCliff_Toon` tinted from the DesertOilFields theme's Surface/Walls |
| Alien Cliffs | `Tileset/AlienCliff` | DualGrid | `BIOME=alien`: same geometric shape + EMISSIVE crystal clusters (cyan/magenta/acid, off below y -1) on Edge V2/V3/V6, purple/teal tendrils; `make_space_textures.py alien` (hex plates / crystal-fracture rock) |
| Moon Cliffs | `Tileset/MoonCliff` | DualGrid | `BIOME=moon`: same geometric shape, no plants (V4 roots -> loose rocks); `make_space_textures.py moon` (craters / grey rock) |
| Arctic Cliffs | `Tileset/ArcticCliff` | DualGrid | `BIOME=arctic`: same geometric shape, hexagonal icicles under the lip (V4) and pale ice-chunk clusters (lit, not emissive) on V2/V3/V6; `make_space_textures.py arctic` (snow drifts / faceted ice) |
| Haunted (Dracula castle) | `Tileset/HauntedCliff` | DualGrid | `BIOME=haunted`, Castlevania style tuned for the far gameplay camera: flat masonry walls (WALL_N 0.03, low wobble), big props - red/gold banners (V1/V4), torches with emissive flames (V2/V8), emissive gothic windows (V3/V5/V10), stone buttress on Corner V2; ruin variants V6/V7/V9 kept; `make_space_textures.py haunted` (flagstones / running-bond cut stone); light flagstone vs dark wall contrast, hatch 0.5, emission 1.5 (3 clips windows to white without post), blood-red water line + wine moat |
| Zaun (Arcane undercity) | `Tileset/ZaunCliff` | DualGrid | `BIOME=zaun`: flat built walls (like the castle), big props - copper pipe runs (V1/V8 + valve), rusty drain pipe + hanging teal lamps (V2/V4), glowing chem tank at the foot (V3), pink / teal neon signs (V5/V10), copper pipe on Corner V2; `make_space_textures.py zaun` (riveted deck plates + grates / rusted wall panels with streaks); chem-green fade + water line, toxic green water, smog sky; emission 1.8 |
| Feudal Japan | `Tileset/JapanCliff` | DualGrid | `BIOME=japan`: flat built walls, big props - red paper lanterns (V1/V8, emissive), stone toro with glowing fire box (V2 + Corner V2), sakura tree (V3), bamboo grove (V4), wooden gate panel (V5), shimenawa rope + shide papers (V10); `make_space_textures.py japan` (seamless Voronoi stepping stones + moss gaps / ishigaki fitted stones); sunset sky, koi-pond teal water, white water line, emission 1.6 |
| Inferno (hellish volcano) | `Tileset/InfernoCliff` | DualGrid | `BIOME=inferno`: angular basalt walls, EMISSIVE lava - glowing cracks (V1/V8, V10 crosses the lip into the ground), lava drips / wide cascade from the lip + magma pools at the foot (V2/V4), brimstone vent (V5); obsidian spikes jutting out (V3/V5/V8 + Corner V2); `make_space_textures.py inferno` (Voronoi cooled-lava crust / basalt column bands); molten orange fade rim, lava 'water' (opacity 1), crimson sky; Emission Min Y OFF (-1000) so lava glows all the way down |
| Grassland Outpost V2 | `Tileset/GrassOutpostV2` | DualGrid | concept pass (2026-09-25), `BIOME=grass2`: GrassCliff angular tiles + WALL props only (user rejected ground/rooftop scatter): embedded in the face - tyre, half tyre, skull, bones, rusty pipe, metal sheet, rebar; leaning at the foot - tyre stack, lean tyre, planks, ladder, crates (no barrels - the map already has them), warning board, shovel, purple rock clusters x2; wall density 0.75 (camera-facing walls), props at their original ~0.9-1.3x scale (user: bigger read worse), no ladder; material copied from the user's GrassCliff_Toon; GrasslandOutpost theme uses it (GrassCliff kept). Apocalypse pass (2026-09-26): + Overgrowth (vines over a rusty sheet) and CarDoor embedded, GrassTufts / Sandbags / Stakes (sharpened-log barricade) at the foot; TireStack/TireHalf weight 1; all props have edge lines except Rebar/Shovel. New FBXs need their `GrassOutpostV2_Tiles` material remapped to `GrassOutpostV2_Toon` (else they render white) |
| Neon City V5 | `Tileset/NeonCityV5` | DualGrid | (2026-09-26) `BIOME=neon5` = V4 (props, ink lines) with a real platform edge: 16 cm dark metal top border around every platform (wall-shaded, outlined) + side profile of a thick cap slab (flush with the collider) carrying the cyan trim, chamfered underside, body recessed 13 cm, dark plinth; no pink corner strip (it floated in front of the recessed body); tight corner chamfer CORNER_R 0.2 (the big 0.46 chamfer ate the whole corner cell with this thick profile, and a recessed profile only keeps the corner/edge seam aligned while R <= 0.5 - 0.41 x recess); broken-lip / collapse / landslide variants (Edge + Edge2 V6, V7, V9) removed from the V5 tileset (they notched the clean cap) - re-running Auto Fill would add them back. `wallPropAvoidBand` (0.72, 1) keeps props off the cap, `wallPropInset` 0.1 seats embedded props on the recessed body. Not assigned to a theme yet (NeonFloodDistrict still V4) |
| Neo-Favela | `Tileset/Favela` | DualGrid | (2026-09-30: SQUARE corners - CORNER_R 0.03, INNER_R 0.04, no corner top push-out - and an unfinished BRICK COPING baked into the profile: one brick course 20 cm wide x 2 cm tall (10 and 4 read too tall) standing ABOVE the walkable top along every edge / corner, wall (brick) textured; `TOP_MAX` 1.03 lets favela tiles exceed the old z<=1 clamp; the tileset's new `topRaise` (0.02) tells the builder about it - tiles are Y-scaled to the wall height, so OverBand runs (the Trepadeira) are lifted by topRaise x wall height and stay on the coping on tall base walls too. WallRunSet also got `RaisedOnly` (skip base walls dropping into the water; unused now)) (2026-09-26) `BIOME=favela`, textures `make_space_textures.py favela`: futuristic Brazilian favela. FLAT built walls: straight brick body set back 6 cm, dark tuck, concrete LAJE slab on top (`profile()` FAVELA block, tight 0.14 corners), plain walls (life = props/runs). Grayscale wall = running-bond brick + ~28% plaster (reboco) patches with a dark rim; floor = cracked concrete slabs. 23 wall props (scale 1.3-1.5, paint 1.4-1.8): embedded painted-plaster patches x4 (teal/pink/yellow/green), barred window, lit window, AC, parabolica, relogios de luz + spaghetti, neon graffiti tag (no ink), plant cans shelf, holo bar sign; foot caixa d'agua on bricks, botijoes, tijolo baiano stack, carrinho de mao + sand, white plastic chairs + isopor, paredao (LED speakers), mototaxi, iron gate, teal door, telha ondulada, trash bags (no barrels). Runs: `Gambiarra` (5 wobbling / crossing cables black/red/blue - straights A/B/C + Staple, decorated knot, relogios, pipa, chinelos, hanging loop; wall-box or drop ends onto a coil; corners), `Varal` (clothesline with clothes, no corners), `Postes` (Foot run: concrete pole + transformer + spaghetti ball + gatos to the wall, every 6th cell). Avoid band (0.84, 1) keeps props under the laje. `FavelaTheme` (WorldThemeName.NeoFavela = 14): sunset sky, murky canal water (valao). Graffiti (2026-09-26): `Source~/make_graffiti.py` (system python + PIL, macOS fonts) draws `Textures/Favela_Graffiti.png`, 8 toon tiles 512x256 in a 1024 atlas (bubble BAILE / RJ (Rift Jumpers crew mark) / PAZ / FAVELA with hard shadow + drips, a tall pixo, crown-heart spray stencil, X-eyed smiley, "Rift Jumpers" script tag); `graffiti_quads()` makes `Favela_Prop_Graffiti_01..08`, flat quads 3.5 cm off the wall UV-mapped to one tile each, own slot remapped to `Favela_Graffiti.mat` (URP Unlit, alpha clip, cull off - ToonTerrain props are vertex-coloured, not textured); weight 0.9 each, scale 1.2-1.5. New `wallPropSlope` (default 0.12 = the old hard-coded undercut depth) - Favela 0 + wallPropInset 0.025 so flat props sit ON the straight brick face instead of inside it |
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

**Mobile port: `Tileset/Shaders/ToonTerrainMobile.shader`** (`RiftRaiders/Mobile/ToonTerrainMobile`,
2026-09-27) - identical Properties + cbuffer layout, so a ToonTerrain material can switch shader and keep
every value. Toon light, ambient and hatch weights per VERTEX (exact on the flat-shaded tiles), UVs /
water ramp / water-line distance interpolated, one albedo read, hatch read only on faces that can get
ink, no shadow variants. Rendered side by side with the pre-optimization ToonTerrain at the gameplay
angle: 5 of 630k pixels differ by <=10/255. Gives up: received shadows (Mobile URP has them off), and on
smooth-normal props the toon step / hatch mapping resolve per vertex. ToonTerrain itself also had its
fragment trimmed the same day (identical output). See `docs/performance.md`.
**Default since 2026-09-28: all 20 biome `*_Toon.mat` materials use ToonTerrainMobile** (only the
shader reference changed; values untouched). Safe everywhere because the game scene's directional light
casts no shadows, so received shadows (the one thing it drops) never showed. It has no ShadowCaster
pass. New biomes copy an existing `*_Toon.mat`, so they inherit it. `ToonTerrain.shader` stays as the
reference / for scenes that need received shadows. The Edge Fade Noise feature was removed from both.

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
- **Prop edge lines** (2026-09-26, `_PropLineWidth` px / `_PropLineStrength`, colour = Outline Color): baked ink lines on props like the platform's texture outlines - no hull, no post. The generator (`Piece.feature_lines`, on for AlienV2, NeonCityV4, GrassOutpostV2 and DesertOilV2 props; per-set `NO_LINES` skips soft/glowing/tiny ones - Alien: runes, slime; Neon: neon sign; Outpost: rebar, shovel; Desert: oil leak/puddle, dead bush) fully triangulates and bakes per-corner barycentrics into UV1 (b0,b1) / UV2 (b2, has-data=1), set to 1 for edges that are NOT features (border or crease > `FEATURE_ANGLE` 50 deg); the shader draws where a coordinate hits 0 (pixel-wide via fwidth), after emission so glowing props keep their lines. Meshes without the data (tiles, older props) are unaffected. Creases only - a smooth silhouette (mushroom cap seen side-on) gets no line.
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

**Neon runs** (NeonCityV5, 2026-09-26, generator `neon_runs()` called from `neon4_props()` when NEON5): `Cables` -
3 cables (black / yellow / black, r 0.045 - chunky like the 1.5-1.7x Neon props) stacked up the wall, clips,
decorated junction box with LEDs / meter / small pink sign under the bundle, ends into a wall box or bending down
round one centre so the three legs stand side by side into a glowing ground box; `NeonPink` / `NeonCyan` - one
emissive tube on brackets (no ink), wall-socket ends, corners only. `WallRunSet.BandClearance`: with a tileset
avoid band (V5's cap slab) the run centreline stays that far under the band's lower edge, so runs sit on the
recessed wall body on any wall height (MinWallHeight 0.8 - most Neon walls are ~1 tall). Neon shop variants
(foot, all NEON4): `Shopfront` (magenta awning), `Shopfront_Noodle` (cyan window, red noren, lanterns),
`Shopfront_Shutter` (half-closed roller shutter, lit gap, pink sign, boxes), 1.5 each.
Short-wall fit (2026-09-26): with an avoid band, wall props that don't fit are SHRUNK to fit (foot: under the band;
embedded: into the space between the lowest slot and the band) down to `MinFitScale` 0.55 instead of being
skipped - before this, posters / doors / shops almost never spawned on the ~1-unit Neon walls.
`ScatterEntry.FreeStanding` foot props (Neon: Lamp, Cone, Tent) stand clear of the wall and ignore the band
(a lamp pole may be taller than the wall body). Neon also got `Window_Warm` (lit, half-drawn blinds) /
`Window_Cyan` (bars) wall props, a 4-sided pyramid traffic cone (rubber base, white band), cables black / hazard
yellow / black (`1E212B` / `E8B425`, not emissive - glow stays reserved for the real neon; user's pick), and lower run chances (cables 0.3, each tube 0.15) so runs leave wall for the props.
Neon concept pass 2 (2026-09-30, `neon5_edge_runs()` + NEON5 block in `neon4_props()`): runs `Fence` (yellow rails
r 0.045 on thick dark posts (0.12), some posts with a pink light; new WallRunSet `AbyssOnly` = only walls dropping below the
water line, i.e. the map rim; VISUAL ONLY, no collider; NOT Overlay - fence and hazard border never share a wall) and `Hazard` (reinforcement border: yellow plate mostly ON TOP of
the lip - the cap face below carries the trim glow - with 4 wide DARK-YELLOW (#B8861C) diagonal stripes per cell (black read as noise); chance 0.25, Fence 0.4; plain yellow corner
reinforcement; user asked for FEW, big details). Both: CenterHeight 0.5, BelowTop 0, OverBand, Inset -0.1 (cancels
wallPropInset). Wall props `Sign_Bunny` / `Sign_Zone` / `Sign_Maint` / `Sign_Arrow` (pink / cyan neon), `Sign_Danger` /
`Sign_Power` (yellow); foot `BarrierPost` (pink light), `HazardBarrier`, `CrateYellow`. New FACADE layer
(`TilesetDefinition.facadeScatter` + density / top Y / row step / rows, `TilesetPlatformBuilder.ScatterFacade`): walls
dropping below the water line get rows of real-size props below Facade Top Y, centred per cell (columns line up) -
NeonCityV5 uses BIG irregular pieces - `Facade_Patch1..6` (seeded jittered window grids, random on/off / missing
windows, mixed warm / cyan / pink), `Facade_Slit`, `Facade_Band`, `Facade_Billboard` (+`Cyan`); scale 1.4-2.2, ONE row
(density 0.5, top -0.15) + `facadeJitter` (0.3 cell, 0.35 m) - small / neat windows read 'certinhas', and a 2nd row of
big pieces overlapped the first (older Facade_Column/_Cluster/_Warm... models exist, unused) = a sky building under the platforms; NeonCityV5_Toon `_EmissionMinY` -100 so they glow below 0.
Neon concept pass 3 (2026-09-30): pink BUNNY motif (`bunny()` neon strokes) on `WallPlate_Bunny`, `TrashBin`, `CrateBunny`,
`PalletBoxes`; wall `WallLight_Pink`, `UtilityBox`, `SecurityCam`, `WarningLight` / `WarningLightYellow`, `Poster_Pink` /
`_Cyan` / `_Notice`; foot `CableSpool`, `ServerRack`, `Terminal` (wallDensity 0.95 - the ~1 m Neon walls under the trim
band skip / shrink many props). Rooftop scatter (raised blocks, density 0.06): `Roof_Antenna`, `Roof_Beacon`, `Roof_AC`,
`Roof_Vent`. Floor decals (models exist, REMOVED from groundScatter - user: no floor decals): `Decal_Zone` (A03), `Decal_Arrows`,
`Decal_Hazard`, `Decal_Dock` (text geometry is x-mirrored in the generator: U2B flips x). Tokyo facade signs:
`Facade_Tate1..6` (tall vertical boards on brackets, procedural kanji-like glyphs) and `Facade_Yoko1..3` (wide) mixed
with the window patches in facadeScatter (density 0.55).
Neon concept pass 4 (2026-09-30): glow prefab variants (`Prefabs/NeonCityV5_Prop_<X>_Glow`, additive `Environment`
sprite + Billboard, SideModel = the plain FBX so side walls get no floating glow) for `BarrierPost`, `Siren_Red` / `Siren_Amber` (foot sirens: truncated-pyramid base + caged beacon; replaced the wall WarningLights), `Vending`, `VendingBunny` (new, magenta
palette slot MAGENTA), `Terminal`, `SecurityCam` (SIMPLE grey box on a bracket tilted down, red tip + red glow - the detailed CCTV was worse; weight 2, scale 1.8-2.1). Structure props (yellow accents): foot `GeneratorYellow`, `VentStack`,
`ACFan`; wall `FanBox`, `CableBox`, `Conduit`, `BigPipe`. Bunny motif = SOLID flat silhouette (`bunny()`: splayed ears, octagon head, dark eyes; tube strokes looked awful). `Sign_Danger` simplified to one black triangle + '!' (text read
as noise). New ScatterEntry `MaxPerPlatform` / `PlatformChance` (surface scatter): `Roof_Antenna` max 1 per roof, only
35% of roofs.
Hazard border fixes: new WallRunSet `EndWallStart` (optional authored -X end; without it the builder MIRRORS EndWall with
x scale -1, which flipped the diagonal stripes) - NeonCityV5 `Run_Hazard_EndWallStart`; the bolted plain `Bolts` decorated
module was removed (read as a random yellow block); module joints and stripes carry no ink (`soft_edges`).
Corners: a FITTED cap (user's call - it needn't merge): solid dark-yellow, a bit taller / wider than the border, arms
running 10 cm over the neighbours, inked on every edge incl. the ends (`hz_path(ink_ends=True)`), 3 yellow bolts.
Small blocks (2026-10-09, `TilesetDefinition.smallBlocks` = {Size, Models}, `TilesetPlatformBuilder.TrySpawnSmallBlock`):
a small RAISED platform (bottom >= groundBelowY) that is a full rectangle of cells matching a block Size (or its 90-degree
rotation) is drawn as ONE model - no tiles, wall props, runs or surface scatter; collider unchanged. Model = real
footprint, pivot at the bottom centre, height 1, Y-scaled to the cube height; variant + 180-degree flip from the cell hash.
GrassOutpostV2: `Block_2x2` / `_3x3` / `_2x3` (`outpost_blocks()`: blue-grey stone body with chamfered corners set in
7 cm, cap flush with the collider, dark panel + 2 orange bars per side, grass tufts round the foot; `clamp = False`).
Detail comes from SHAPE (a stone-brick texture read as noise - rejected): dark plinth, body, lighter corner pillars over
the chamfers, bevelled cap flush with the collider + a raised top plate; all vertex-coloured (BLK_* palette).
Larger / non-rectangular raised platforms (4x3, 2x6...) keep the cliff tiles. The block must stand ALONE (no other cube
touching / intersecting its footprint within its height span - glued twin platforms keep the tiles), and each
SmallBlock has a `Chance` (hash per platform; Outpost 0.5) so only some small platforms become blocks.
Favela HOUSES were REVERTED (2026-10-09, user: didn't look good - smallBlocks emptied, House FBXs deleted, `favela_houses()` kept in the generator but not called). Was: (`favela_houses()`: `House_2x2/_3x3/_2x3` x V1/V2, chance 0.5): body is
terrain-shaded (STONE slot -> the favela brick wall texture), painted plaster patches + a barred window or iron door on
every side, a FLAT concrete laje flush with the collider (walkable), and the 'old roof' as low detail on it -
corrugated fibrocimento sheets (some rusty), a tyre holding them, a caixa d'agua, rebar stubs on V2.
Favela ground scatter (2026-10-09): flush `Prop_Manhole` (iron lid in a concrete ring) + `Prop_Drain` (boca-de-lobo
grate), favela palette, scale 2.2-2.6, density 0.006 (rare).
Favela surface (2026-10-09): `Textures/Favela_Surface.png` = the user's painted paving-slab sheet
(`Source~/favela_surface_reference.png`), converted to grayscale with the 1-99.5% range stretched to 0-1 and a 10 px
edge crossfade, 1024 px, Surface Scale 10 (5 read too small); coloured by Favela_Toon's Surface Light/Dark as before.
Favela ROOFTOP scatter (2026-10-09, `favela_roof_props()`): `Roof_Telha_A..D` (aligned 1-3 inked corrugated strips, coloured NEAR THE FLOOR / laje tone - FH_TELHA A19A8E, FH_TELHA_D 877F74, FH_RUST 8C7563, no red; saturated mixed colours read as noise, un-inked flat sheets read as nothing), `Roof_Tank` (scale 0.9-1.1 - 1.4-1.7 read too big), `Roof_Dish`, `Roof_Antenna` (max 1 per roof, 40% of roofs), `Roof_Planks`,
`Roof_AC`, `Roof_Crates` - each MaxPerPlatform 1-2, density 0.22. New tileset flag `rooftopEdgeCells`: rooftop scatter
may use edge cells too (narrow 2-wide roofs) and then keeps the 8 neighbour cells of each prop free (no heaps).
Those edge-cell props are pushed half a cell inward from each open side (a 2-wide roof -> its centre line) so they never
hang over the lip. Telha sheets are ALIGNED: laid edge to edge parallel to the walls (no model yaw, AnyYaw off -> only
90-degree turns).
Water scatter (2026-10-09, `TilesetDefinition.waterScatter` / `waterDensity` / `waterRing`, `TilesetPlatformBuilder.ScatterWater`):
props floating in OPEN WATER around the base platforms, baked with the level like every other piece. Empty cells (no
cube box over them) at Chebyshev distance waterRing.x..y cells from a cluster, one hash chance per cell; a cell near
several clusters belongs to the cluster owning the nearest cube box (ties: smallest min x, then z) so it spawns once;
placed at `waterLevelY` with random yaw + 0.4-cell jitter, under a `Water` child of the generated root. Visual only (no
collider, so no shore-field foam - each model carries its own flat foam ring). ArcticV2: `Iceberg_S/_M/_L/_Flat`
(`arctic2_props()`: faceted 8-sided ice, blue body / pale upper facets, separate snow cap + overhang, submerged skirt,
`clamp = False`), scale 2-3.2, density 0.018 (0.045 read too many), ring 3-7. Foam = a THIN irregular line hugging the waterline (1.03-1.12 R) - the first wide 1.28 R ring read as a solid plate under each berg. Shore field: the builder registers every water prop as a disc (`ShoreObstacles.Set`, radius = 0.8 x the model's plan
bounds x scale; removed in ClearResult); `WaterShoreBaker.FinishBake` and `TilesetPreviewScene.BakeShoreField` stamp
them into the land mask before the distance transform, so the water's edge fade / foam rings go round the icebergs
too. The baker keeps the chunk / collider land (`_landBase`) and re-finishes (distance + upload only) whenever
`ShoreObstacles.Version` changes - icebergs can be built after the load-time bake. Stale discs are impossible by construction: `RebuildAll`
(world switch) clears the whole registry first, every `Generate` removes its own entry before ScatterWater re-adds
it, `OnDestroy` removes it, and `Stamp` drops owners that were destroyed (switching Arctic -> Grassland used to leave
foam spots in empty water).
Neon trim GLOW (2026-09-30): ADDITIVE, baked in the NEON5 wall profile, opaque. Rows around the cyan trim carry strata
T = -3 + vertex alpha (strength: chamfer .6 -> .79 and cap .845 -> .92 fade 1 -> 0, `GLOW_A` env 0.9). ToonTerrainMobile
passes that alpha in `light.a` (only surface pixels read it - no extra interpolator) and ADDS `_WallGlowColor` (new;
alpha = strength, default 0 = off) x alpha x Emission Strength over the normally shaded wall. A lerp towards the glow
colour (first try) read grey / white; adding keeps it blue-cyan. NeonCityV5_Toon: Wall Glow Color (0, 0.65, 1, 1).
The desktop ToonTerrain shader has no glow term (NeonCityV5 uses the Mobile shader).
Neon fake lamp light + fog (2026-09-30): the `Postes` foot run's Decorated pole points at prefab variants
`NeonCityV5/Prefabs/NeonCityV5_Run_Lamp_Pole(_Side)_Glow` = the FBX + two child SpriteRenderers (`GlowCore` scale 2.2 a 0.7,
`GlowHalo` scale 8 a 0.4, warm #FFD27A) with the gameplay atlas's soft `Environment` glow sprite (512 ppu),
`NeonCityV5_LampGlow.mat` (Mobile/Particles/Additive) and `Billboard` - no real lights. Regenerating the FBX keeps them
(variants). NeonFloodDistrict's surface is now `CloudFog_NeonPuffy` (copy of CloudFog_Puffy: stacked puffy clouds,
transparent gaps, Height Offset -1, Deep = sky #6E1A5E, pink Mid 0.66/0.2/0.56, Top 1/0.55/0.85; NeonCityV5_Toon.mat Gradient Bottom Color = the same #6E1A5E);
`CloudFog_Neon` kept. Concept pass: `Run_Cable_DropFoot` (where the cable legs reach the street) is now a GENERATOR
(dark body, grey lid, yellow lightning panel, vents, cyan LED); greyer surface (NeonCityV5_Toon Surface Light
0.5/0.51/0.57, Dark 0.35/0.36/0.42); ground scatter = rare `Manhole` / `Drain` / `Manhole_Hex` / `Drain_Trench` / `Drain_Glow` (density 0.005; `Manhole_Steam` yellow-ring removed, scale 2.2-2.6; HazardMark and floor decals unused). Yellow
marks live IN the surface texture: new ToonTerrain(+Mobile) `_SurfaceAccentColor` (alpha = strength, default 0 = off,
so every other material is unchanged) paints where the surface texture's B channel is set - no extra sample, one lerp.
`NeonCityV5/Textures/NeonCityV5_Surface.png` = neon.png (R/G) + B mask of its own hazard stripes, made by
`Source~/make_neon_surface_accent.py` (each stripe redrawn as its convex-hull polygon = crisp geometric edges;
Accent alpha 0.6, texture CompressedHQ / ASTC 4x4 so the mask stays sharp; neon.png itself untouched - ModularLevel_Shared / older Neon sets use it). No runtime material copies anywhere (removed
WallFadeToSky / CloudsFromSky / MaterialSwaps 2026-09-30): sky colours are authored on the materials.
Neon graffiti (2026-09-26): `make_graffiti.py <png> neon` draws `NeonCityV5/Textures/NeonCityV5_Graffiti.png` -
8 glowing tiles (hollow tube letters NEON / RIFT / GLITCH (chromatic split) / 404, heart, bolt, X-eyed smiley,
RUN + arrow): coloured tube, white-hot core, soft halo that fades out before the tile border. `graffiti_quads()`
makes `NeonCityV5_Prop_Graffiti_01..08` (neon5 build), slot remapped to `NeonCityV5_Graffiti.mat` = URP Unlit
TRANSPARENT alpha blend (queue 3000, no ZWrite, cull off - clip would cut the halo off); weight 0.8 each, scale
1.3-1.6. V5 `wallPropSlope` = 0 (its recessed body is vertical), so flat props sit on the face.
**Desert pass 2** (2026-09-30): pipeline chunkier (R 0.125, centreline 0.26 = run CenterHeight, 0.15 off the face,
elbow radius 0.18, bigger flanges / pump / end box) + identity modules `Straight_Hazard` (yellow / black sleeve) and
`Straight_Label` (orange ID plate + flow arrow); scrapyard foot props `ScrapPile`, `CarWreck`, `PipeStack`; broken-lip /
bite edge variants V6 / V7 / V9 weight 2.5 for more edge variation. `DesertSkyTheme` = a copy of DesertOilFieldsTheme
with sky #B4B6EB + the `CloudFog_Puffy` surface (same tileset). NO runtime material copies (the user doesn't want
generated materials): the sky colour is simply authored on the materials - DesertOilV2_Toon's Gradient Bottom Color
= #B4B6EB (so DesertOilFields shares it) and CloudFog_Puffy Deep = sky, Mid = sky + 25% white, Top = + 85%.
`CloudFog_Puffy`: flat cartoon clouds from
`CloudPuffs.png` (R = premultiplied shade, G = coverage), made from the user's painted sheet
`Source~/cloud_reference.png` by `Source~/import_cloud_texture.py` (crops the empty top band so it tiles vertically,
fades the cut bottoms, square resample = ~1.35x vertical stretch against the 45-degree squash;
`make_cloud_puffs.py` is the older procedural version) and the CloudFog shader's `Stack Layers` mode
(large layer composited OVER the unrotated, dimmed detail layer; Mid 0 / Top 0.52 / Sharpness 2.2 = near-linear tones, Gaps Opacity 0 (Mid 0.4 / Top 0.55 - faded; texture blurred 3 px + Ignore Mipmap Limit, so low quality doesn't pixelate it) so the walls show
between the clouds and the junction follows the cloud silhouettes instead of a straight line; Height Offset -1, so clouds fade
into each other; off = the original blended noise, so CloudFog_Neon is unchanged). The EdgeTrim is chunky (plate 0.045) and never overlaps:
panels butt with a hairline gap, corners are ONE mitred `lip_path` whose arms stop at the run inset. Lip metalwork (reference sheet): `EdgeTrim` run (chance 0.5, length 2-5;
NOT `Overlay` - it was, but trim right above the Pipeline read messy, so a wall gets pipes OR trim; `Overlay` stays a
WallRunSet flag: shares walls with other runs, neither blocking nor blocked) = a CONTINUOUS steel angle over the lip
(every module spans the whole cell; seams + rivets mark panels, braces / hazard panel as decorated modules, a wide
riveted cap on convex corners); `Guardrail` run (0.2): posts clamped over the lip + a round rail. Industrial props:
`AlarmBox` (red lamp, hazard strip, conduit), `WallBracket`, `VentGrate` (wall), `WarningPost` (foot), and
ground scatter (density 0.012 - mid-map details must stay rare): desert flora `Cactus_Saguaro` / `Cactus_Small` / `Cactus_Dead` (dark green / dried red-brown,
smooth 12-sided + NO ink - small inked ribs read as a glitch; scale 0.5-0.7 - small ground detail, must not read as an obstacle; the only BIG cactus
is the `Cactus_Wall` foot prop (7-sided, every rib inked), scale 1.3-1.6, always against a wall), `RedRock`, `RedPebbles` (sand-toned ROCK_SAND #D98348 ~ the surface's dark tone, low / half buried, pebbles un-inked -
they blend with the ground), plus a rare `BrokenPlate` (weight 0.4). `RedShrub` (blobs + twigs) was dropped - it read as
a rock with antennae. `CarWreck` foot prop scale 1.5-1.7.

**Arctic runs** (2026-09-28, from a winter reference sheet; generator `arctic2_runs()`): `IcicleBand` (universal,
chance 0.4) - snow coat over the lip (`coat()`, same technique as the Alien slime: top layer with a wavy inner edge,
wrap, short face sheet, silhouette ink cords) + a band of chunky icicles (`crystal` pointing down, 1.8x / 1.9x);
`SnowCornice` (0.3) - thicker overhanging scalloped cornice, one icicle; `FrozenCable` (0.15, BelowTop 0.2) - orange
cable sagging between dark clamps, snow ridge on top, icicles under it, ends in a snow-capped junction box;
`IceFracture` (0.08, BelowTop 0.35) - jagged dark-blue crack with a pale core and small branches. All with corners
(convex on the real chamfer lip) and `_Side` / `_XFront` variants. The old `Icicles` prop is weight 0.4 now. The Icicle Band is ONE object (`iced_coat()`): the snow sheet's lower edge IS the icicles - a
zig-zag of 5 wide triangular teeth per cell (snow white above, ice blue in the teeth, tips leaning on the undercut
rock), outlined ONLY outside (the snow's floor edge + the zig-zag) with cords at the platform outline weight
(`INK_R` 0.0075); separate pyramid icicles are still used under the cable / cornice; the cable is chunky (r 0.05, bigger clamps / snow). ArcticV2 and MoonV2
props now bake edge lines too (they were built without `feature_lines`; Moon skips the glowing meteorite).

**Alien runs + mushroom v2** (2026-09-28, from a reference sheet; generator `alien2_runs()`): the biome language is
a pink slime band as the MAIN continuous detail, a cyan crystal seam + an organic root line as rare accents.
`SlimeBand` (chance 0.35) - goo COVERING the lip (not a hanging band - the first try read as cables):
`goo()` extrudes a cross-section along the plan line - a layer on the top edge with a wavy inner edge reaching onto
the floor, wrapping over the corner into a face sheet with fat lobes (equal at both cell borders), tapered
surface-of-revolution drips with a round drop, glossy flecks; run ends taper to nothing; corners follow the plan
path. Placed with `BelowTop` 0 / `CenterHeight` 0.5 (module lip = the platform top). Its outline is a thin
near-black cord (`INKLINE`) along the wavy floor edge and the lower edge - only the silhouette, no line through the
goo at the lip crease; module joints are open (no cap faces) and soft. Convex corner goo: the path is walked backwards (goo() takes the
outward normal on the path's right) and its diagonal sits at x + z = 0.36 - the tile's real chamfer lip is at
~0.43 and the builder shifts corner modules in by the run inset along BOTH walls (+0.07 in x + z); on CORNER_R the
goo was hidden under the chamfer top. `CrystalSeam` (0.1) - at the lip: dark
channel + ONE continuous glowing cyan vein (a dashed vein read as broken bits) with small crystal nubs, a crystal or two poking up per module (`Straight` / `Straight_B`), a big
5-crystal outcrop as the decorated module, ends at a crystal. `RootLine` (0.1) - organic alien vine: a thick vine wandering up/down/out and swelling at its knots
(`tube(..., radii=)` variable radius), a thin vine braided around it (integer turns per cell), drooping tendrils that
curl into spirals, some with glowing pink bulbs, small leaves; every module starts/ends at the same height, depth and
radius so they chain; ends dive tapering into the rock. All three at the
lip (`BelowTop` 0.06), with corners and `_Side` / `_XFront` ink variants. New `Piece.soft_edges` + `tube(...,
soft_caps=True)`: end-cap rings are never inked, so run modules join without a seam line. Mushroom v2 (`lathe()`
surfaces of revolution): tapered pale stem, dark gills, smooth domed cap with a darker rolled rim and flat light
spots on the dome top. The old lime `SlimeDrips` prop is now weight 0.5 (clashes with the pink band).

**Outpost runs + survivor paint** (2026-09-27, generator `outpost_runs()` + `graffiti_quads(z=-0.07)` in
`outpost_props()`): `BarbedWire` - a concertina helix (black, r 2.4 cm, 8-sided so only the silhouette is inked; phase 0 at every cell border, 5 turns/cell, loop radius 7.5 cm, so modules chain)
right under the cliff lip, rusty angle-iron stakes; decorated tin-can alarm / torn rag / red X warning plate; ends
at a stake; corners (helix around the chamfer path). `SandbagTrench` - continuous FOOT run: back row pressed against the wall foot, a front row stepped out, a
top row and a crown (bigger 0.34 x 0.2 x 0.15 bags; `FootReserveHeight` 0.5, props still spawn above it). Wire chance 0.18. `TelegraphPoles` - foot item,
leaning wooden pole, crossbar, insulators, wires back into the cliff, a cut wire, a nailed rag, every 6th cell.
Foot runs now also exclude each other (`footCellsTaken`: poles never stand on the trench). Graffiti lettering (make_graffiti.py; the Favela atlas uses the first-pass `bubble()` / `pixo_font()` / `tag()` set,
the user's pick over the rework below): `wildstyle()` - letters placed one by one (rotated / scaled / bounced,
`jitter_mask`), bubbled (`blobby`), 2-colour gradient fill, 3D extrusion, thick outline, highlight streaks, sparkle
stars, drips; `pixo()` draws Sao Paulo pixacao from a stroke alphabet (`PIXO`, angular tall letters), not a font;
neon letters use the same jitter as tube outlines. Survivor spray paint:
`make_graffiti.py <png> survivor` -> `GrassOutpostV2/Textures/GrassOutpostV2_Graffiti.png` (KEEP OUT, SAFE ->,
skull + crossbones stencil, day tally, NO FOOD, searched X-in-circle, HELP, GO ->; ragged spray edge, overspray,
drips; cream / red; letters / tally / X drawn stroke by stroke from a shaky hand alphabet `HAND` via `hand_text` / `hand_symbol`, not a font), `GrassOutpostV2_Graffiti.mat` (copy of the Favela clip material), weight 0.8 each, 7 cm off
the (undercut, natural) rock face.

**Ground features + water-edge props** (2026-09-28): `TilesetDefinition.groundFeatures` (`GroundFeature`: Model, Size in
cells, Margin, Chance) - one big floor piece per Ground platform, on the free Size rectangle (+ margin, both
orientations) closest to the platform centroid, never under a higher block (`CoveredAbove`); its cells are marked
used for the floor scatter. Favela `Campinho` = `Favela_Prop_Pitch` (7 x 4 dirt pitch, painted lines, low goals, a
ball; `Piece.clamp = False`). `ScatterEntry.WaterEdge` wall props only spawn on walls whose bottom is below
`waterLevelY` (base platforms dropping into the water, never raised blocks) and are placed with height 0 at the
water line - Favela `ValaoOutlet` (manilha pouring green sewage into the canal, foam). Favela greenery / street pass: `PlantPots`, `BananaPlant`, `WallPlant`, `BeerCrates` (engradados with bottle tops),
`Stall_Yellow` / `Stall_Red` (awning over the back half so the table + goods read from above, plastic chair) at 1.5-2x,
reboco-paint patches weight 0.6, corrugated 0.5, wallDensity 0.6; `Trepadeira` run (first in the list, chance 0.4):
big leafy clumps draped over the lip + strands with leaf clusters. The Campinho is painted straight on the floor
(no dirt base, worn off-white lines) so it doesn't grab attention. A concrete + brick big-zone
wall mix was tried for the Favela and rejected (map read more confusing) - the brick + small plaster version stays.

**Flat wall props** (2026-09-28): `ScatterEntry.FrontOnly` keeps flat decals (runes, drips, graffiti, paint, posters)
off side walls, where they read edge-on and look buried - set on those entries in AlienV2 / Favela / NeonCityV5 /
GrassOutpostV2. Embedded wall props never poke above the lip any more (shrink to fit, down to MinFitScale, else
skip). Alien runes / slime drips sit 7 cm proud on a dark stone backing (the natural rock bulges). Crystal seam
convex corner follows the real chamfer lip like the slime corner.

**Run coexistence** (2026-09-26): wall runs of one platform claim their cells (`runCellsTaken`, in the tileset's list
order) - a later non-foot run never lands on a wall stretch an earlier one owns (Favela: Varal avoids the Gambiarra;
Neon: tubes avoid Cables), and foot items (poles) skip claimed cells so they never hide a clothesline. A run's whole
module (centreline - `CenterHeight`, i.e. what hangs under it) must clear `wallPropMinY` (no clothes in the water).
`FrontOnly` keeps flat runs (clotheslines) off side walls, where they read edge-on. Favela Varal: straight metal
arms square to the wall at both ends (EndWall mirrored), rope 30 cm out sagging 8 cm per span, clothes 2x (two per
cell), right under the laje (BandClearance 0.05), Plain weight 0.3, Length 3-5.

**Foot runs** (`WallRunSet.Foot`, 2026-09-26): modules stand on the street in front of the wall (pivot at the wall
foot, inset 0.1, only where `HasGroundAt` finds ground; wall height / band ignored). With an EMPTY Straight model and
the item in Decorated (chance 1, `DecoratedSpacing` = cells between items), a run becomes evenly spaced items -
Neon `StreetLights`: `Run_Lamp_Pole` (+ `_Side` ink variant; 1.2 tall, mid-grey pole with baked silhouette ink,
warm glowing lamp block with a small dark cap - the camera sees lamp TOPS, not undersides - pink banner) every
4th cell, Length 3-16, Chance 0.95; the random scatter `Lamp` prop was removed. Only cells that actually get a
module reserve anything (a spawned foot item blocks its whole cell for the wall props; empty cells block nothing).

## Preview scene (`Tileset/TilesetPreview.unity`, 2026-09-26)

Look at a biome exactly as the game shows it, in Edit mode, without running a match. Copied from
GrasslandOutpostGameScene: Main Camera (same Camera + URP data, fixed at the FollowCamera offset (0, 35, -35),
rot 45, FOV 14 - move it on X/Z to look around), Directional Light, Water quad, EnvironmentManager, lighting
settings. Level = six chunk prefabs' cubes as plain `TilesetPlatformBuilder` cubes, laid out as islands with
water between them (EnemyChunk, TwinPlatforms, Crossroads, Bridge, Traversal, Start: straight walls, side walls,
inner corners, bridges, tall / short walls). `TilesetPreviewScene` (on the `TilesetPreview` object): pick a
Theme, press **Apply Theme** - loads it through the real EnvironmentManager (tileset override, sky, water
colours / surface material), builds every cube and bakes the shore field; it also re-applies on scene open /
script reload. **Rebuild Tiles** / **Bake Shore Field** separately. The shore field normally comes from
`WaterShoreBaker` (needs the Quantum frame), so the helper bakes the same field (same mapping, R8 encoding and
`_ShoreField` / `_ShoreFieldParams` globals) from the cubes' BoxColliders. Generated tiles are not meant to be
saved into the scene: clear them (`ResetVisualTileset`) before saving.


Chunk prefabs migrated from `CubeVisualBuilder` (2026-09-24); verified in the Editor by placing
EnemyChunk x2 + Bridge + rotated Crossroads side by side (floors merged into one cluster) and on
`TilesetTest.unity` (PrototypeCube layout copies for Outpost / GrassCliff / NeonCity). Not yet verified
in Play mode with the real level generator. `CubeVisualBuilder` stays for the scenes that still use it
(GrasslandOutpostGameScene, CubeVisualBuilderTTest).

Known gaps: no `avoidNearWallDetails` equivalent (no chunk used it); a cube destroyed at runtime
doesn't rebuild its cluster (chunks are persistent); a cluster's tiles live under the host chunk;
no toon additional lights; outer silhouette outline would need a screen-space edge pass.
