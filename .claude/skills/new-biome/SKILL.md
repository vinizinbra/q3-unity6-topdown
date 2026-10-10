---
name: new-biome
description: |
  How to create or rework a world biome / tileset (tiles, wall props, continuous wall runs, graffiti, world
  theme) with the Blender tileset generator + TilesetPlatformBuilder, and verify it at the gameplay camera.
  Use when the user asks for a new biome/world/tileset, new or changed wall props, cables/pipes/wire/
  clothesline-style runs, street poles, graffiti/pixo/spray paint, prop outlines (ink lines), or "add X to the
  <biome> theme". Complements docs/tileset-builder.md (WHAT exists) with HOW to do it and the traps.
---

# New Biome / Tileset Workflow

Read `docs/tileset-builder.md` first - it is the source of truth for what every tileset, run and field does.
This skill is the procedure + pitfalls. Everything a world shows goes through its `WorldTheme` asset
(`Assets/_Project/Data/WorldTheme/*Theme.asset`) -> `Tileset` (TilesetDefinition) -> models/props/runs.
There is no separate detail-sprite system any more (Environment Details was removed on purpose).

## Pipeline

1. **Textures** (grayscale, coloured by the material's Light/Dark pairs):
   `Blender -b --factory-startup -P "Source~/make_space_textures.py" -- <biome> <Set>/Textures`
   (add an `elif BIOME == "<biome>"` recipe + a seed in the `rng` dict). Brick/plank walls: use
   `np.quantile` for coverage so patches never swamp the base pattern.
2. **Models**: `BIOME=<biome> Blender -b --factory-startup -P "Source~/build_grasscliff_autotiles.py" -- "$PWD/<Set>/Models"`.
   **Always pass BIOME** (without it GrassCliff_* files land in the wrong folder). Add: a flag, `PREFIX`
   entry, `FLAT` for built walls, optional `profile()` block (cross-section rows), palette block
   (props bake vertex colour from `PALETTE`, so `PALETTE += [...]` slots past 15 are fine), plain-wall
   early return in `edge()`, a `<biome>_props()` + `<biome>_runs()` and hook them into `pieces = (...)`.
3. **Unity import** (live Editor via `unity command eval`):
   - Check `EditorApplication.isPlaying` before anything that recompiles; wait for the user to stop.
   - Texture importer: copy settings from an existing biome's texture (`ReadTextureSettings`).
   - Material: copy an existing `<Set>_Toon.mat`, swap `_SurfaceTex`/`_WallTex` + colour pairs.
     Never overwrite user-tuned values on an existing material without asking.
   - **Remap every new FBX's `<Prefix>Tiles` material to the Toon material** (else it renders WHITE).
   - Tileset asset: copy an existing `*Tileset.asset`, set `modelPrefix`/`material`, run `AutoFillFromFolder`
     via reflection, fill `wallScatter` / `wallRuns` through `SerializedObject`.
   - WorldTheme: copy one, set `worldName` (add to `WorldThemeName`), `tileset.Tileset`, sky, water.
4. **Verify** (see Verification) and update `docs/tileset-builder.md` (table row + sections).

## Wall props

- Authored facing -Z, pivot on the wall face at the prop's lowest point; embedded ~3 cm proud, foot 5 cm out.
- `P(name)` sets `pc.feature_lines = name not in NO_LINES` -> baked ink lines (creases > `FEATURE_ANGLE` 50 deg).
  Skip lines on glowing / flat smears / very thin things. EVERY `<biome>_props()` must do this - Arctic / Moon were built
  without it and had no outlines until 2026-09-28.
- Real size; check scale reads at the gameplay camera (Neon props are 1.5-1.7x).
- Built (straight) walls: set tileset `wallPropSlope` 0 (default 0.12 follows undercut cliffs and buries
  flat props on straight walls). Keep-out band (`wallPropAvoidBand`) for caps/trims; props shrink to fit
  (MinFitScale 0.55) instead of vanishing on short walls. `FreeStanding` for poles/cones/tents.

## Wall runs (continuous decorations)

`TilesetDefinition.wallRuns` / `WallRunSet` (see doc). Modules are 1 cell along X, facing -Z:
- **Seamless joints**: every module starts/ends at the same border heights (sagging cables: fixed heights at
  x = +-0.5; helix wire: phase 0 at each border, integer turns per cell).
- **End modules** are the +X end (builder mirrors). `EndDown` + stretched `Drop` + `DropFoot` for ends that go
  to the street. Corners are authored in the corner TILE frame (`Piece.clamp = False`, stubs a hair thicker).
- **Ink on tubes**: smooth tubes have no crease -> use `Piece.tube(..., view=V)` (silhouette edges inked) and
  generate `_Side` (view `(1,0,-1)`) + corner `_XFront` variants, assigned to `SideModel` / `*Side` / `*XFront`.
  Tubes must have >= 8 sides (6 sides = 60 deg > 50 -> EVERY facet inked -> patchy black).
  Pass `soft_caps=True` (or add to `Piece.soft_edges`) for module ends, else every cell joint shows an ink ring.
  Sheets / coatings (slime): extrude a cross-section along the plan (`goo()` in alien2_runs), open soft joints.
  Baked edge flags on long flat strips can lose one face in the FBX import (the line then only shows on the hidden
  side) - for a sheet's silhouette draw the ink as geometry: a thin near-black tube along the edge (`INKLINE`).
  Organic round things: `lathe()` (alien2_props). A "band" hanging under the lip reads as a CABLE - coatings must
  sit on / wrap the lip (BelowTop 0).
- Foot runs (`Foot = true`): street items every N cells = empty Straight + item in Decorated (chance 1,
  `DecoratedSpacing`); continuous foot runs (trench) use `FootReserveHeight`.
- Coexistence is automatic: runs claim cells in list order (later wall runs avoid them), foot items skip
  claimed / other foot cells. `FrontOnly` for flat things (clotheslines). `BandClearance` keeps runs under caps.
- Keep them sparse: the user finds overlapping or dense runs noisy (typical Chance 0.12-0.3).

## Graffiti / paint (textures, not fonts)

`Source~/make_graffiti.py <out.png> [neon|survivor]` writes a 1024 atlas of 8 tiles (2 cols x 4 rows, 512x256);
`graffiti_quads(z)` makes `<Prefix>Prop_Graffiti_01..08` (own `<Prefix>Graffiti` slot -> remap to a URP Unlit
material: alpha CLIP for paint, alpha BLEND + no ZWrite for neon glow). Lettering must look hand-made:
`wildstyle()` (jittered bubbled letters, gradient, 3D extrude, streaks, stars, drips), `pixo()` (stroke
alphabet `PIXO`), `hand_text()` / `hand_symbol()` (shaky spray `HAND` alphabet), neon tube letters via
`jitter_mask`. Which style a biome uses is a taste call: the user rejected plain-font SURVIVOR spray paint
(Outpost -> `hand_text`) but asked to keep the Favela's first-pass bubble letters (`bubble()` + `pixo_font()` + `tag()`)
over the wildstyle rework - show both and let them pick. Quads sit 3.5 cm off built walls, 7 cm off natural rock.

## Verification

- Scripts in `.claude/skills/new-biome/scripts/`:
  `render_theme_edit.cs` (placeholders `__THEME__ __TX__ __TZ__ __D__ __OUT__`) - 4 test chunks with a WorldTheme's tileset,
  sky and water / cloud surface, prop counts in the result (the scratchpad can be wiped between sessions - keep scripts here);
  `render.sh <tileset> "<sky C# color>" <tx> <tz> <out.png>` - 4 test chunks at the gameplay camera + prop
  counts; `render_lineup_edit.cs` (placeholders `__SET__ __PREFIX__ __NAMES__ __FOOT__ __D__ __OUT__`) - props
  side by side against a wall for close checks. Zoom: replace `(0, 36, -36)` with `(0, 12, -12)`.
- Preview scene `Assets/Test/Environment/Tileset/TilesetPreview.unity` (real camera/light/water/shore field):
  pick a theme on `TilesetPreview` -> Apply Theme. Clear generated tiles before saving the scene.
- Judge at the gameplay camera first, then close-ups; count spawns per type to check frequencies.
- Show the user: copy each round's renders to `Recordings/TilesetRenders/` (gitignored) and `open` them (Preview) -
  chat image / file links don't open for them.

## Editor traps

- `unity command eval` main-thread timeout is 5 s: remap/reimport many FBXs in time-boxed batches
  (stop at ~3 s, loop until `left=0`); a big `AssetDatabase.Refresh()` can time out - wait, then retry.
- Edit mode: `EditorSceneManager.NewScene(... Additive)` + `CloseScene`; play mode: `SceneManager.CreateScene`
  + `UnloadSceneAsync` (the other API throws).
- New materials may render black/unclipped on the first render: set
  `ShaderUtil.allowAsyncCompilation = false` around the render, restore after.
- Blender: create all UV layers before fetching them; a double-sided face needs its own back vertices
  (`faces.new` "face already exists"); lone flat faces may flip - build closed thin prisms.
- Game scene's Directional Light is inactive in edit mode; the preview scene carries an active copy.
