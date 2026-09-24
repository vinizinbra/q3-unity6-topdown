# Tileset Platform Builder (cube layouts → autotiled cartoon platforms)

Edit-time (and runtime-capable) tool that replaces the visuals of grid-aligned level cubes with
autotiled tileset pieces. Lives entirely in `Assets/Test/Environment/Tileset/` (test area, not yet
wired into chunk generation). Blender generators live in that folder's `Source~/` (ignored by Unity).

## How it works

- **`TilesetPlatformBuilder`** (MonoBehaviour, NaughtyAttributes buttons **Generate Visual Tileset** /
  **Reset**). Reads every `MeshRenderer` under Source Root as a set of top-down cells (from mesh
  bounds, so a disabled renderer still works and the cube pivot does not matter), groups cubes with the
  same bottom + top height, splits each group into 4-connected platforms, solves the tiles, spawns
  them under a generated container, scales Y to the platform height and hides the cube renderers
  (colliders are untouched). Tiles copy the source cube's layer + static flags. Generating again
  replaces the previous result; `variationSeed` reshuffles variants deterministically.
- **`TilesetAutotiler`** (pure static solver), two modes chosen per `TilesetDefinition`:
  - **DualGrid** (current): tiles sit on grid *vertices* (half a cell off the cube grid) and cover the
    4 quarter cells around them → only 4 shapes: `Center` (4 solid), `Edge` (2 adjacent),
    `Corner` (1), `InnerCorner` (3); 2 diagonal quarters = two Corners. No fallbacks.
  - **PerCell** (legacy, used by the Outpost set): 15 per-cell configurations + a 2x2 InnerCorner.
  - Plain Center tiles are merged into greedy rectangles and one Center is stretched over each.
- **`TilesetDefinition`** (ScriptableObject): mode, model prefix, merge-centers flag and
  `{Key, Model, Weight}` entries. Several entries per key = variants (`<prefix><key>_V<n>`), picked per
  tile by a stable hash, weighted. **Auto Fill From Folder** finds them (keeps existing weights).

Rotation convention: rotating a piece +90° about Y (clockwise from above) maps its N side to E;
masks rotate with `TilesetAutotiler.RotateMask`. Models are authored in Unity plan coordinates and
converted to Blender with (bx, by) = (-x, -z) so FBX imports unrotated (verified in the Editor).

## Tilesets

| Set | Folder | Mode | Notes |
|---|---|---|---|
| Grassland Outpost | `Tileset/Models` | PerCell | first prototype, palette material |
| Grassland Cliffs | `Tileset/GrassCliff` | DualGrid | current look, `ToonTerrain` material |

Generators: `Source~/build_outpost_autotiles.py`, `build_grasscliff_autotiles.py`, textures
`make_toon_textures.py`. Run with
`Blender -b -P <script> -- <models_out_dir>`; each prints a `[fit]` report of how the walkable top edge
matches the cube collider.

## ToonTerrain shader (`Tileset/Shaders/ToonTerrain.shader`)

One material per tileset, shared by every tile. World-space surface texture (top-down) and triplanar
wall texture, so nothing stretches when tiles are scaled/stretched. Mesh data baked by the generators:

- `UV0.x` strata coordinate: integers are outline lines (1–4 ledges, 5 cliff top, 6 surface
  outline), each group with its own width/strength.
- `UV0.y` distance into the surface (0 at the outline) → one-directional fade: the surface starts at
  the outline in `Fade Color` and fades into the surface texture inward. Nothing fades outside it.
- `COLOR.rgb` wall tint (terrain) or albedo (props); `COLOR.a = 1` = prop/foliage. Rock-type details
  (boulders, shards, holes, cracks) are shaded as terrain so they follow the wall colour.
- Toon main light + received shadows; hatching is a **multiply**: in the hatch texture black = ink,
  white = nothing (R single lines, G cross lines). Strong on walls, `Hatch On Surface` scales grass.
- Main directional light only (additional lights not toon-shaded yet).

## Design constraints (agreed with the user)

- The **walkable top edge must match the cube collider**: edges within ~1–6 cm, lip may overhang
  ≤ 3 cm; inner corners ≤ ~9 cm past the collider. Diagonal (chamfer) corners cut ~14 cm by design.
- Side profile is an undercut (`___ \`): the lip is the widest point, walls tuck in below it.
- Walls: one continuous face (no horizontal strata bands), smooth waves only (no per-sample noise),
  geometric chamfer corners with hard vertical creases, details (holes, pebbles, shards, cracks that
  can cross the lip into the surface) sit on the deformed wall surface.
- Deformation is zero at tile borders so any variant meets any other.

## Current status

Code-complete and verified in the Editor on `TilesetTest.unity` (additive test scene with Outpost and
GrassCliff copies of the same layout). Not yet used by the real level/chunk pipeline. Known gaps: no
toon additional lights; outer silhouette outline would need a screen-space edge pass.
