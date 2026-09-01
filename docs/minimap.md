# Minimap

A node-based minimap - the static layout (one filled square per placed `Chunk` entity, at that
chunk's true relative grid position/size) plus a level outline are baked into a single
procedurally-painted `Texture2D`/`RawImage`, so adjacent chunks' real footprints tile together
into a connected, pixel-art-style blueprint with no adjacency/edge data needed. This is the first,
deliberately decoupled slice of a much larger future "Run Pacing + Exploration System" idea
(assault/breathing rhythm, local aggro/leashing, POIs, world events) - only the minimap itself was
built now, since it stands on its own and needs almost nothing that doesn't already exist in the
simulation.

## What this adds

- **`Chunk.Discovered`** (`Assets/_QuantumUser/Simulation/QTN/Chunk.qtn`) - a new `bool` field,
  defaults `false`. Shared/co-op, not per-player (same convention Experience/Talents already use).
- **`ChunkDiscoverySystem`**
  (`Assets/_QuantumUser/Simulation/Systems/Level/ChunkDiscoverySystem.cs`) - each tick, for every
  chunk not yet `Discovered`, computes its world AABB from `Transform3D.Position` +
  `LevelGenerationSystem.MinCornerOffsetWorld`/`SwapsAxes` (accounts for rotation - see "Known
  simplification" below for why the offset is needed, not just the axis swap) and
  `ChunkSizeWidth`/`ChunkSizeDepth` (already world-space units, not grid cells), then checks every
  player for containment - same X/Z-only footprint-containment idea `LobbyBoundarySystem` already
  uses for the LobbyStart chunk, duplicated locally rather than shared (that check is only 3
  lines). Registered in the always-on-per-tick `GameplaySystemGroup` block of
  `Assets/_QuantumUser/Simulation/Default/SystemSetup.User.cs`, no ordering dependency on anything
  else.
- **`MinimapWidget`** (`Assets/_Project/Scripts/UI/InGame/Hud/Minimap/MinimapWidget.cs`) - a
  `QuantumGlobalMonoBehaviour` that reads `game.Frames.Predicted` every `QUpdate` (never
  `Update`/`LateUpdate`, per house convention). World bounds are fixed/authored (`worldExtent`/
  `worldCenter`), not scanned from placed chunks - the known playable world is assumed to fit
  inside `+-worldExtent`, so the texture size and world-to-texel scale (`worldUnitsPerTexel` - not
  1:1, e.g. a 20-unit chunk paints as a 2x2 block at the default 10) are derived once in `QStart`
  with zero dependency on chunk data.

  **Per-tick chunk painting** (`UpdateChunks`): caches each newly-seen chunk's texel rect
  (`ComputeTexelRect`/`GetWorldBounds`) and paints its initial fill; on a `Discovered` flip,
  repaints only that one chunk's own sub-rect (`undiscoveredColor`/`discoveredColor`) - never the
  whole texture. The current chunk (see below) paints `currentColor` instead, reverting the
  previous one back to `discoveredColor`. Multiple paints in the same tick batch into one
  `Texture2D.Apply()` call. Steady state (nothing changed) costs just a handful of comparisons,
  not per-frame pixel work.

  **Level outline** (`ComputeLevelOutline`): computed exactly once, gated on `Global.LevelGenerated`
  (not "we just saw a new chunk" - `game.Frames.Predicted` could observe the level mid-populate,
  and computing from a partial chunk set would permanently lock in a wrong, single-chunk-looking
  outline, since this only ever runs once). Standard edge-detection-on-a-pixel-grid: rasterizes
  every chunk's rect into an occupancy mask first, then marks an occupied texel as outline if any
  texel within `outlineTexels` of it (or the texture's own edge) is unoccupied - deliberately NOT
  a per-chunk-pair "does this whole edge touch another chunk" check (an earlier version), since
  that treats each of a chunk's 4 sides as one monolithic decision and gets a partially-covered
  side wrong. The outline mask's own shape never changes once computed (still derived from every
  chunk regardless of `Discovered`, so it doesn't shift as more gets explored), but
  `RepaintSingleChunk` only stamps `outlineColor` on top of a chunk that's actually `Discovered` -
  when the outline first computes, every already-painted chunk gets force-repainted once so it
  actually receives the stamp, even if its own state didn't change that tick.

  **Icon overlays**: one small `Image` per chunk whose `ChunkType` has a sprite assigned in
  `chunkTypeSprites[]` (positioned from `ComputeTexelRect`'s own rounded rect center via
  `TexelRectCenterToMapPosition`, NOT the chunk's raw continuous-space center - independent
  per-corner rounding at this coarse a scale can shift the painted square's actual center by up to
  half a texel, so deriving the icon from the same rect keeps the two aligned) - visibility toggles
  with `Discovered`. Left unassigned for `Enemy`/`Traversal` in the current authoring plan, so only
  Boss/Merchant/LobbyStart get an icon; the texture alone represents every other chunk.

  **Player markers**: one pooled `RectTransform` per **match player** (`PlayerLink` filter - local
  and remote alike, not `MyLocalPlayer.Slots`, so teammates show up too), repositioned every frame.

  **Elite markers** (`UpdateEliteMarkers`): one pooled `RectTransform` per currently-alive
  `EnemyDataAsset.Tier == Elite` enemy (`Enemy` filter, skipping `EnemyActionPhase.Dead`),
  repositioned every frame - same seen/stale-sweep pooling shape as player markers, just keyed by
  the enemy's own `EntityRef` and gated on Tier instead of `PlayerLink`. One generic marker
  (`eliteMarkerPrefab`) for every Elite regardless of which `EnemyDataAsset` it is, no per-enemy-type
  sprite - Elites already get special always-relevant/never-retiring treatment from
  `EnemyLifecycleSystem` (see CLAUDE.md's own "Boss Phase Trigger" section), so surfacing them on
  the map follows the same reasoning. Shares the identical `OverlayPair`/full-map-panel machinery
  every other overlay here does, so it shows on both map surfaces for free. Shows whatever sprite
  `eliteMarkerPrefab`'s own `Image` is authored with as-is - no per-entity-type sprite override.
  Leave `eliteMarkerPrefab` unassigned to disable Elite markers entirely.

  **Special markers** (`UpdateSpecialMarkers`): identical shape to Elite markers, one pooled
  `RectTransform` per currently-alive enemy with `EnemyDataAsset.Economy.Persistent == true` AND
  `Tier != Elite` - a persistent enemy that isn't actually an Elite (e.g. a boss's own persistent
  summon), which still gets the same always-relevant/never-retiring treatment from
  `EnemyLifecycleSystem`. Deliberately its own `specialMarkerPrefab` rather than reusing the Elite
  marker, since a Special isn't an Elite and shouldn't read as one on the map. Leave
  `specialMarkerPrefab` unassigned to disable Special markers entirely.

  **Clear-Enemy markers** (`UpdateClearEnemyMarkers`): one pooled marker per every currently-alive
  ORDINARY enemy - excluding `Tier == Elite`/`Economy.Persistent == true`, which already get their
  own Elite/Special marker above, so a single enemy never shows two markers at once - but ONLY while
  `GameState.CurrentState == Breathing` AND `Global.BreathingAreaSecured == false` - the same "CLEAR
  ALL ENEMIES..." window
  `BreathingCountdownWidget`'s `notSecuredRoot` shows (see `docs/run-phase.md`'s "Elite / Boss
  phases" section - Breathing holds `PhaseTimer` open until every alive enemy is gone, mirrored into
  `BreathingAreaSecured`). Outside that window every existing marker is torn down immediately
  (not left to the stale sweep) - covers both "the area just secured" and "`GameState` left
  Breathing some other way while enemies were still up." Ordinary enemies aren't Persistent, so
  unlike Elite/Special they can expire via `EnemyLifecycleSystem`'s own `Irrelevant -> Retired`
  timeout (`f.Destroy`, see `CombatDirectorUtility.RetireEnemy`) with no signal to react to - the
  same seen/stale-sweep pooling shape every marker pass here uses is what tears its marker down the
  instant `frame.Filter<Enemy, Transform3D>()` stops returning it, identical to a normal kill. Leave
  `clearEnemyMarkerPrefab` unassigned to disable Clear-Enemy markers entirely.

  **Current chunk** (`ResolveCurrentChunk`): whichever chunk contains *this instance's own* bound
  local player, via `MyLocalPlayer.Slots[localSlotIndex]` - the one place this class needs
  local-player awareness for chunk state.

  **Centering on the player** (`CenterOnLocalPlayer`): every frame, `mapRect`'s own
  `anchoredPosition` is set to `-WorldToMapPosition(localPlayerWorldPos)`, so this instance's local
  player always lands at `mapRect`'s parent's origin - the texture itself is never re-baked for
  this. Expects `mapRect` to be nested inside a separately-authored masked container (fixed
  position/size, clips whatever overflows) that defines the actual visible viewport - the standard
  "content pans, mask stays put" technique.

  **Toggle** (`toggleButton`/`fullMapPanel`, optional): clicking `toggleButton` flips `fullMapPanel`
  (a `JuicyGameobject` -
  `Assets/3rd-party/PachaGames/Scripts/Runtime/Util/JuicyGameobject.cs` - or one found on
  `fullMapImage`'s own `GameObject` if `fullMapPanel` is unassigned) active/inactive via its own
  `SetActive`, which scales from zero and activates on `Show`/`SetActive(true)` and scales to zero
  THEN deactivates on `Hide`/`SetActive(false)`, instead of an instant on/off snap - lets the corner
  minimap itself act as the button that opens/closes the big map, no input-system binding needed.
  Reads the panel's own live `gameObject.activeSelf` rather than tracking a separate open/closed
  bool, so its authored starting state (normally inactive) and anything else that shows/hides it
  later stay the source of truth. `toggleButton` is wired in `Awake` (not `QStart`) since it's plain
  Unity UI, not simulation-driven. Leave `toggleButton` unassigned to disable click-to-toggle
  entirely - the panel can still be opened by whatever else drives its active state.

  **Full-map panel** (`fullMapImage`, optional): a second surface showing the WHOLE level at once,
  unpanned/unmasked (e.g. a Tab-key panel). The *texture* is literally shared - both `RawImage`s
  point at the same `Texture2D`, so every repaint updates both for free - but icons and player
  markers are real UI objects, not texture content, so each surface gets its own clone of each,
  held together in an `OverlayPair` (`Mini` under `mapRect`, `Full` under `_fullOverlayRoot`) and
  driven in lockstep from the same data. The only per-surface differences are the rect positions
  are computed against (`WorldToMapPosition`/`TexelRectCenterToMapPosition` both take a root rect,
  since the two surfaces draw the same texture at different UI sizes) and `fullMapOverlayScale`, a
  uniform scale for the big map's own clones since it's usually drawn much larger. `_fullOverlayRoot`
  is `fullMapRect` if assigned, else `fullMapImage`'s own `RectTransform` - which is correct as
  long as that's square and center-pivoted. With `fullMapImage` unassigned, every `Full` is simply
  `null` and nothing changes.

  Chunk icons are positioned once at spawn (a chunk never moves), which would strand them if a
  surface is laid out *later* - the full-map panel is typically inactive, and possibly zero-sized,
  until first opened. `RefreshIconPositionsIfResized` (polled every `QUpdate`, a no-op unless a
  surface's own `rect.width` actually changed) re-places them from each pair's cached `TexelRect`.
  Player markers need none of this - they're repositioned every frame anyway.

  **Prefab templates**: `iconPrefab`/`playerMarkerPrefab` are expected to be scene child objects
  under this same widget (not Project-window prefab assets) - `QStart` disables both once so the
  template itself doesn't render at its own design-time position; every spawned clone explicitly
  sets its own active state right after instantiating, so this is safe regardless of the template's
  disabled state.

  Deliberately **not** wired through `GameplayUiController` the way `upgradeWindows[]` is - every
  instance runs the identical frame query regardless of which split-screen slot it lives under
  (`localSlotIndex` is the only per-instance knob), so "one per local player" is purely a
  scene-hierarchy placement concern (drop one instance under each player's own HUD Canvas).

## Current status / what's still needed

The code compiles once the Editor regenerates Quantum codegen for the new `Chunk.Discovered`
field (see CLAUDE.md's own "Quantum `.qtn` codegen gotcha" section), and `ChunkDiscoverySystem` is
registered - chunks will actually start flipping `Discovered` at runtime. Nothing renders yet,
though:

1. Create a `MinimapWidget` scene instance under each split-screen HUD Canvas (one per local
   player slot, same count as `GameplayUiController.upgradeWindows[]`), setting `localSlotIndex`
   to `0`/`1` respectively.
2. Build a masked container (e.g. `Image` + `Mask`/`RectMask2D`) defining the actual visible
   viewport, and nest `mapRect` (square, centered pivot) inside it - `mapRect` pans every frame
   (`CenterOnLocalPlayer`), so it should be sized to cover the whole map, not just the viewport.
3. Assign a `RawImage` (`mapImage`) sized to fill `mapRect`.
4. Author an icon template, a player-marker template, an Elite-marker template, a Special-marker
   template, and a Clear-Enemy-marker template (plain `Image`s are enough) as child objects under
   the `MinimapWidget` GameObject, assign to
   `iconPrefab`/`playerMarkerPrefab`/`eliteMarkerPrefab`/`specialMarkerPrefab`/
   `clearEnemyMarkerPrefab` respectively - a distinct color/shape from the player marker (and from
   each other) is worth authoring so an Elite reads as a threat, a Special reads as its own thing
   (neither a teammate nor an Elite), and a Clear-Enemy marker reads as an ordinary, temporary
   threat rather than either.
5. Assign `chunkTypeSprites` (a `List<ChunkTypeSpriteEntry>` - each entry an explicit `ChunkType` +
   `Sprite` pair, not a positional array, so entries can be added/reordered freely; add one entry
   per `ChunkType` that should show an icon - `Boss`, `Merchant`, `LobbyStart`, `HealingShrine`,
   `CursedRift` (added 2026-08-14 for the two Breathing POI chunks, see `docs/breathing-poi.md`),
   `Blacksmith` (see `docs/store-blacksmith.md`) - on each instance; leave `Enemy`/`Traversal` out
   of the list (or their `Sprite` unassigned) so they show no icon.
6. Set `worldExtent`/`worldCenter` to match the actual authored playable world size.
7. Set `outlineTexels` > 0 (and consider a lower `worldUnitsPerTexel`, e.g. 5, for more texel
   headroom) to enable the level outline; leave at 0 to disable it entirely.
8. For the full-map panel: assign its `RawImage` to `fullMapImage`. If that `RawImage` is square
   and center-pivoted, leave `fullMapRect` empty; otherwise nest a square, center-pivoted content
   layer of the same size over it and assign that instead. Tune `fullMapOverlayScale` so the
   shared icon/marker templates read at the right size on the bigger surface (they're clones of
   the same prefabs the minimap uses, so their authored size is minimap-sized).
9. To let the corner minimap itself open/close the full-map panel: add a `Button` (e.g. covering
   the minimap's own masked viewport rect, `Image` + `Raycast Target` on) and assign it to
   `toggleButton`; add a `JuicyGameobject` component to the panel's root (backdrop +
   `fullMapImage`/`fullMapRect`, whatever should show/hide together) and assign IT to
   `fullMapPanel`, leaving that root **inactive** in the scene by default so the big map starts
   closed. An `EventSystem` must exist in the scene for the click to register, same as any other
   Unity UI `Button`.

Not yet manually verified end-to-end in-Editor.

## Explicit non-goals for this pass

- No POI-specific icons beyond `ChunkType` itself (chest/challenge) - `HealingShrine`/`CursedRift`
  now have their own `ChunkType` values (see above) and so CAN get a minimap icon like
  Boss/Merchant, but no sprite is authored for either yet.
- No secret-room hiding - no "secret" concept exists on `Chunk`.
- ~~No toggle/zoom/full-map view~~ - superseded: an optional second full-map surface
  (`fullMapImage`) now shares the texture, icons, and player markers with the panned corner map
  (see "Full-map panel" above). Zoom itself is still out of scope.
- No breathing-window pulse/highlight behavior - belongs to the larger, deferred pacing system.

## Known simplification (resolved 2026-08-13)

`LevelGenerationSystem`'s `RotationYaw` used to have a `return 0` short-circuit that made a
procedurally-rotated chunk's `Transform3D.Position`/`Rotation` ignore its real `Chunk.Rotation`
entirely (chunks always rendered/collided unrotated even though `Chunk.Rotation` and the grid
footprint were correctly randomized) - removed. Fixing it meant `Transform3D.Position` is no
longer simply a rotated chunk's AABB min corner (rotating in place around a chunk's own
min-corner-pivoted local origin swings the footprint into a different world region) - both
`ChunkDiscoverySystem` and `MinimapWidget.GetWorldBounds` now add
`LevelGenerationSystem.MinCornerOffsetWorld`'s offset (reimplemented locally in `MinimapWidget`
since it's `internal` to the Simulation assembly) on top of the pre-existing `SwapsAxes` width/
depth swap (already handled correctly before this fix - it was the actual cause of an incomplete/
wrong outline early in this feature's build, 3-in-4 chunks land rotated) to recover the true min
corner. `CubeVisualBuilder`/`QuantumEntityView`/hand-placed detail slots needed no changes - they
already followed `Transform3D.Rotation` via normal Unity transform-hierarchy composition, they just
never received a non-identity value before now.

Separately: even with correct rotation handling, independently rounding each chunk's texel rect
corners (`Mathf.RoundToInt` per corner, not coordinated across neighbors) can still misalign
adjacent chunks by up to 1 texel - highly visible at the default coarse `worldUnitsPerTexel` (a
chunk is only 2-4 texels wide). Two ways to actually fix this if it becomes a problem again: lower
`worldUnitsPerTexel` (shrinks the error's relative size, doesn't eliminate it), or snap every
chunk's rect to a shared, precomputed table of unique world-space boundary values instead of
rounding each chunk's corners independently (fully eliminates it, more code). Neither is
implemented.
