# Training Zone (movement + aim tutorial level)

A hand-built, offline-only course that teaches movement and aiming before a first run: walk, climb
ledges, hop small gaps, dash wide gaps, walk a raised path, hop across stepping stones, then shoot
harmless target dummies. Text is painted on the ground at each station. No run ever starts there,
so the Director never spawns enemies.

## Design

- **Its own scene and map.** `Assets/_Project/Scenes/TrainingZoneScene.unity` is a copy of
  GrasslandOutpostGameScene (camera, HUD, managers, water) without the Boss Arena and test entities.
  It has its own Map, `Resources/Maps/TrainingZoneMap` (WorldSize 512, so the 122-unit course fits).
- **Fixed biome.** TrainingZoneScene's `EnvironmentManager` has `initialTheme = GrasslandOutpostTheme`
  and `sceneThemeOnly` on, so the Training Zone always uses the Grassland Outpost look. The world picked
  in the menu (`RuntimeConfig.WorldDefinition`) is ignored for the look. A dedicated TrainingFacility
  biome was tried and removed on 2026-10-12.
- **No procedural generation.** `Resources/LevelConfig/TrainingZoneLevel` has an empty `ChunkPool`.
  `LevelGenerationSystem.SeedFromExistingChunks` picks up the hand-placed station chunks, and the
  player spawns at the centre of the LobbyStart station.
- **The match stays in Lobby.** With `LevelConfig.IsTrainingZone`, `LobbyBoundarySystem` never sets
  `GameState.Survival`. As a result, CombatDirectorSystem stays idle and the solo tutorial popups
  (`InMatchTutorialManager`, triggered on Lobby→Survival) never open.
- **Stations are separate chunks.** Each station is its own `Chunk` map entity with its own baked
  `RespawnPoint`. `FallRespawnUtility` respawns a fall at the nearest chunk's point, so falling in the
  Dash gap puts the player back at the start of the Dash station, not at the beginning of the course.
- **No manual Jump needed.** Jump is keyboard-only (Space, see `docs/input.md`), so every height
  change is a 1-unit step (auto-mantle, `MovementDataAsset.MaxLedgeHeight`). Every gap is either
  small enough to auto-hop or meant to be dashed.
- **Aim lesson.** Weapons fire automatically at `Aim.Target` once in range; SwitchTarget (Tab / R1)
  cycles targets. The range has 3 dummies at different distances behind two low cover blocks. The Skill station after it has 5 dummies packed together for the hero skill.

## Spike Trap (accessory lesson, reusable hazard)

`SpikeTrap` is generic and meant to be reused in levels, Traversal Challenges and trap rooms later.

**Simulation** (`QTN/SpikeTrap.qtn`, `Systems/Combat/SpikeTrapSystem.cs`)
- The plate is a box: the entity is the centre, ±`HalfExtents` on X/Z.
- It cycles **Armed → Warning → Extended → Cooldown**:
  - In **Warning** the spikes peek out (telegraph).
  - While **Extended**, everything valid on the plate is hit every `HitInterval`, starting the instant
    the spikes come up.
- **Trigger** is `Step` (a valid target walks onto it) or `Timer` (`TimerArmedTime`, staggered with
  `TimerOffset`).
- **Targets** follow `DamageTargetMask` (Both / Players / Enemies).
  - Hits go through `DamageUtility.ApplyDamage`, so the Accessory Guard, shields and Invulnerable
    (downed, burrowed) all behave normally.
  - The system itself skips airborne players (a jump clears it), burrowed and non-grounded enemies,
    and `EnemyTrait.GroundHazardImmunity`.
- **`AccessoryDropDirection` / `AccessoryDropSpread`**: when non-zero, a player the trap hits gets a
  transient `AccessoryDropHint` (`QTN/Accessory/AccessoryGuard.qtn`) for that hit only. The blocked
  accessory then lands in that cone instead of a random direction, with the same distance band and
  the same solid-ground check, falling back to the normal ring. The Training Zone uses (1, 0) ±20°:
  forward, past the trap.
- **`TeachingHit`** (the Training Zone): only a player whose accessory can block (Equipped,
  durability above 1) counts, both for triggering and for being hit. The trap always pops the
  accessory off, and never costs Health or breaks it.

**View** (`View/Entities/Hazards/SpikeTrapView.cs`)
- It builds its own visual at runtime from `HalfExtents`: a grid of **sockets** (`socketSpacing`
  1.0), one low bevelled stone base per spike, edge to edge (`socketSize` 1), and one tall slim
  charcoal pyramid spike each, with a lit steel face toward the camera.
- The spikes are hidden in their sockets (only a dark hole shows) while Armed, peek out during Warning, snap up when Extended and
  sink back during Cooldown.
- **Tuning buttons** on `SpikeTrapView`:
  - **Regenerate Spikes** rebuilds the visual with the current Inspector values. In Play mode it does
    it right away and keeps the spike state. In edit mode it builds a preview with the spikes up,
    sized from the object's `QPrototypeSpikeTrap` and flagged DontSave, so it never gets saved into
    the scene.
  - **Toggle Preview Up/Down** and **Clear Preview** control the edit-mode preview.
- An optional red telegraph pulses during Warning. There's also an optional `extendSound` and a
  `FollowCamera.ShakeAtPosition` when the spikes extend.
- The meshes are vertex-coloured with alpha 1 (the ToonTerrain "prop" convention), so they render
  with the active tileset's material. `fallbackMaterial` is used otherwise.

**Training Zone use** (Accessory station)
- One 2 × 12 trap (exactly 2 socket columns) across the whole station, so there's no way around it.
- Settings: Step trigger, Players only, TeachingHit, `WarningTime` 0 (it can't be outrun or dashed
  past), Extended 0.8 s, `HitInterval` 0.2, Cooldown 1.2, Damage 10.
- Yellow/black hazard-stripe borders, and one view-only warning sign on the approach side (the
  Rift POI sign board, `RR_TrainingFinishSign`, with the `PictoIcon_Caution` symbol tinted alert red on its face, and the TraversalActivator's warm
  "Available" glow particles behind it).

## Target dummies

- `TrainingDummySpawner` (`QTN/TrainingZone.qtn`) is a map entity per dummy.
  `TrainingDummySpawnerSystem` (inside GameplaySystemGroup, after EnemyLifecycleSystem) creates the
  dummy once `LevelGenerated` is set, then re-creates it `RespawnDelay` (2 s) after it dies. A lingering
  corpse already counts as dead.
- The dummy is created the same way as a Director spawn (`GroupSpawnerUtility.SpawnMember`): the
  generic `DirectorConfig.EnemyPrototype`, then `Enemy.EnemyData` is set and
  `EnemySystem.SeedFromEnemyData` is called. The one difference is that it gets **no
  `EnemyLifecycle`**. Otherwise EnemyLifecycleSystem retires it as "irrelevant" while the player is
  far away, which destroys and respawns it every few seconds.
- `Resources/Enemy/Training/TrainingDummy.asset` is a copy of W1-Turret. Its `AI.Targeting` is
  cleared and `DetectionRange` set to 0, so it never leaves Idle and never attacks. `Stats.Movement`
  is set to ScarabNestStationaryMovement, so it gets `Anchored` and knockback can't move it. Swap its
  view or HP (60 now) there.
- The `TrainingDummy` tag makes `DamageUtility.ResolveDeath` skip every on-kill drop (XP, Scrap,
  Rift Shard, Coin, chest), so shooting dummies can't level the player up or fill a wallet.

## Ground text and guide

- The titles are Rift Pink (#FD3971) and the hints are white, all in Inter Medium Bold.
- **Breathing room:** every station after Start begins with a 4-unit strip of plain floor
  (`BreathingRoom`, part of the same chunk) before its content. It gives a pause between lessons and
  lets the text pop in ahead of the player.
- `TrainingZoneGuideWidget` (View only, on the `TrainingZone` root, added by the builder) hides every
  station's text at start. When the local player gets within 3 units (`revealLookahead`) of a
  station, it pops that station's labels in one after another (scale 0 → 1.15 → 1, unscaled time).
- **Finish POI:** the course ends on a real interaction, which is the last lesson.
  - `TrainingFinishPoi.prefab` is a copy of TraversalActivator: same sign and Available particles,
    without the TraversalChallenge component or view.
  - It carries `Interactable{Kind = TrainingFinish, Radius 3}`, `TrainingFinish{LeaveDelay 1}` and a
    `TrainingFinishPoiView`, which shows the interaction prompt "FINISH TRAINING" / "BACK TO MENU".
  - `ContextInteractionSystem` always resolves it as Available.
  - In `SkillSystem`, a Base Skill press on it fires `EventTrainingFinished`. The guide widget handles
    that event for a local player: it waits `LeaveDelay`, then calls `MatchMakingConfig.LeaveMatch()`.
- There are no toasts: encouraging toasts were tried and removed.

## Building / editing the course

The layout is code: `Assets/_Project/Scripts/TrainingZone/Editor/TrainingZoneBuilder.cs`. To change
it, open TrainingZoneScene (additively is fine), edit the station methods, then run **Tools ▸
RiftRaiders ▸ Level ▸ Build Training Zone**. The tool:

1. Tears down and regenerates the `TrainingZone` root.
2. Clones the TraversalChunk prefab's VisualCubes, so tileset visuals build at runtime like any chunk.
3. Rebuilds each station's compound collider and bakes its respawn point.
4. Re-bakes the Quantum map.

Then save the scene.

| # | Station | Teaches | Layout |
|---|---------|---------|--------|
| 0 | Start (LobbyStart, 12) | Move | Flat pad, "TRAINING ZONE" / "MOVE" |
| 1 | Climb (16) | Auto-mantle | One 1-unit step in the middle |
| 2 | Too High (22) | The 1-unit limit | Text on open floor to the left; a 2-unit wall (not climbable, not even with Jump), with a 1-unit side step on the camera side and a second 2-unit wall right of the step, so the only way on is floor → step → wall top |
| 3 | Gaps (16) | Auto-hop | Two 2-unit gaps |
| 4 | Dash (16) | Dash | One 4-unit gap |
| 5 | High Ground (18) | Heights | Narrow stair over water: +1, +2, then a +3 lookout, each separated by a 1-unit gap; drop down |
| 6 | Platforms (20) | Chained hops | 4 stepping stones at +0 / +1 |
| 7 | Accessory (20) | Accessory Guard | A `SpikeTrap` across the whole station hits the player once: the guard blocks it and the accessory pops off. Past the strip: "PICK IT BACK UP / Walk over your accessory" |
| 8 | Range (24) | Auto-aim | 3 respawning dummies, 2 cover blocks |
| 9 | Skill (18) | Hero skill (E / X) | 5 dummies packed together, since most hero skills hit an area; generic text (the zone doesn't know the hero) |
| 10 | Finish (12) | Interact | The Finish POI (`TrainingFinishPoi` prefab, a TraversalActivator look-alike placeholder) + "INTERACT / E / X next to the sign"; pressing interact returns to the menu |

## Entry point

- `MainMenuWindow.trainingButton` (optional), wired to `Training()`.
- `GameManager.PlayTrainingZone()`, then `MatchMakingConfig.StartTrainingZoneRunner()`. This runs the
  same local session as Practice, with `TrainingZoneMap` / `TrainingZoneLevel` (Inspector fields on
  MatchMakingConfig) swapped onto the cloned RuntimeConfig.
- **After every Intro** (Splash → Intro → Training Zone → Menu): `IntroSequence` calls
  `GameManager.RequestTrainingZoneAfterIntro()`. It then loads MenuScene under `SceneLoader.Cover`
  instead of `SceneLoader.Load`. MenuScene has to load first because it owns MatchMakingConfig, which
  starts the session. `GameManager.Start` starts the Training Zone one frame later. The cover's
  `holdUntil` (`GameManager.IsInMatchWindowShown`) keeps it up until the Training Zone's InMatchWindow
  is showing, so the menu is never seen in between. There is no "seen" save for now.
- **Skip:** a "SKIP TRAINING" button at the top centre of TrainingZoneScene's HUD Canvas (a clone of
  the Breathing Skip button). `TrainingZoneSkipWidget` calls `MatchMakingConfig.LeaveMatch()`, the same
  path as the settings Disconnect, which returns to the menu.
- `RestartOfflineMatch` keeps the session type. Players leave via the in-match settings Disconnect.
- Pressing Play directly in TrainingZoneScene also works: its QuantumDebugRunner RuntimeConfig points
  at the training Map and LevelConfig.

## Current status / known simplifications

- Checked in Play mode (2026-10-12) through `GameManager.PlayTrainingZone`: the scene loads, all 7
  stations are present with respawn points, and the player spawns on the Start pad. Tileset visuals
  and ground text render. The 3 dummies spawn Idle and `Anchored` and stay alive.
- Not checked yet: the dummy kill → 2 s respawn cycle, and the no-drop gate.

- Gap and ledge sizes come from the movement numbers (jump reach about 2.9 units, Dash 5 units) and
  have not been playtested yet. Tune them in the builder if a gap turns out too easy or impossible.
- Key prompts are fixed text (keyboard / gamepad). They are not device-aware and have no touch
  wording.
- There is no completion tracking or reward, and no "first launch → Training Zone" prompt.
- The dummies use the Turret view as a placeholder.
