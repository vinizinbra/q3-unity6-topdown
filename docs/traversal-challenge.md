# Traversal Challenge

An interactable world prop, placed in a `ChunkType.Traversal` chunk, that turns a gap-crossing into
a timed co-op puzzle instead of a passive backdrop. `TraversalChunk.asset`'s `ChunkSpawnConfig`
already baked a `GlobalUpgradeChestEntity` at a fixed offset near the far side of the chunk before
this feature existed - an unfinished scaffold this feature completes: the chest was always going to
be the reward, it just had no way to actually become reachable.

Press the Base Skill button on the activator (same generic redirect every other POI already uses -
see `docs/breathing-poi.md`) and a set of temporary platforms spawn, bridging the gap toward the
chest. A `Duration`-second countdown starts (45s authored as a decisive starting point within the
30-60s range this was scoped to). While it's running, `Global.SurvivalTime` freezes and
`CombatDirectorSystem` stops spawning new enemies **globally** - but `GameplaySystemGroup` stays
fully enabled, so nobody's movement/weapon/skill input is locked. The intended co-op shape: one
player keeps fighting whatever's already spawned (they just stop seeing *new* spawns) while a
teammate crosses; once crossed, the fighter can walk over and grab the chest later with zero time
pressure, since only the crossing itself was ever timed.

**Any connected player can be the one to activate it, and any connected player can be the one to
complete it** - it is not scoped to whoever pressed the button first. Reaching a checkpoint near the
far side (proximity, not sequential platform-by-platform tracking) completes it: the platforms stay
solid **permanently**, `SurvivalTime`/spawning resume, and the chest is a normal proximity pickup
from then on (`ChestSystem`, untouched). If nobody reaches the checkpoint before the timer expires,
every spawned platform is destroyed and the challenge settles on `Failed` - **permanently, the same
one-attempt-per-run contract as `Completed`** (confirmed with the user) - `SurvivalTime`/spawning
resume the same way. One attempt per activator per run, whichever way it goes.

`TraversalChallengeActivated`/`Completed`/`Failed` each also fire a `ToastManager` popup
("TRAVERSAL CHALLENGE STARTED"/"COMPLETE"/"FAILED") on **every connected client**, not just
whoever triggered it - wired in `TraversalChallengeWidget.Awake`, unfiltered unlike
`InteractionPromptWidget`'s own per-local-player toast, since this is whole-team awareness (same
reasoning the countdown banner itself already documents), not personal feedback.

## Why this design, not something else

- **No `PoiUsagePolicy`/`PoiUsage`.** Every other POI (Healing Shrine, Cursed Rift, Store,
  Blacksmith) is per-player gated - a player who already used it can't again. This one is world-
  shared instead: the `TraversalChallengeState` machine alone decides who can activate/complete it
  (anyone, while `Idle`/`Active` respectively), since the whole point is co-op division of labor,
  not an individual reward loop.
- **No `PoiInteractionLockUtility` entry.** Cursed Rift/Store/Blacksmith all lock the interacting
  player's movement/weapon/skill input while their Choice Window is open. Traversal Challenge locks
  nobody - a player has to be free to actually walk the platforms, and the fighting teammate
  explicitly needs to keep fighting normally.
- **Platforms are spawned/destroyed at runtime (`f.Create`/`f.Destroy`)**, not pre-placed and
  collider-toggled like `BossArenaGate`. Quantum's own entity view spawn/destroy lifecycle handles
  visibility for free this way - no custom View-side bool-mirroring class needed, since platforms
  have no reason to exist before activation in the first place.
- **The global pause is a standalone counter** (`Global.ActiveTraversalChallengeCount`), not a
  detour through `SurvivalPhaseKind.Breathing`. Forcing `GameState` to `Breathing` would also fire
  `RunPhaseUtility`'s real Breathing-transition side effects (`BreathingIndex++`, resetting every
  `OncePerPlayerPerBreak` POI usage, cancelling uncommitted Cursed Rift interactions) - all
  unwanted collateral for an ad-hoc 30-60s trigger. The counter is checked at the exact THREE places
  `SurvivalPhaseKind.Breathing` already is: `SurvivalProgressionUtility.Tick`'s `SurvivalTime`
  advance, that same method's `PhaseTimer` advance (added after an initial pass only froze
  `SurvivalTime` - a challenge activated mid-Breathing would otherwise let `PhaseTimer` keep
  counting down `Global.BreathingTimeRemaining` and quietly end the Break, resuming Director
  spawning, out from under a still-in-progress crossing), and `CombatDirectorSystem.Update`'s
  `TryPulse` gate. Incremented on activate and decremented (clamped at 0) on complete-or-fail, so
  multiple concurrent Traversal Challenges across different chunks stay correct independently
  without ever touching `GameState`.
- **`CheckpointPosition`/`PlatformPositions` are CHUNK-relative offsets**, not activator-relative and
  not absolute literals - resolved as `chunkPosition + chunkRotation * offset`, the exact same
  rotation-aware math `Chunk.RespawnPoint` already uses (chunks can be placed rotated 90/270°, not
  just translated). Chunk-relative rather than activator-relative so authoring lines up with the
  same frame of reference `TraversalChunk.asset`'s own `ChunkSpawnConfig.Spawns[].Offset` already
  uses for the chest - both are eyeballed in the same coordinate space. The owning chunk is resolved
  once, on activation, via `FallRespawnUtility.TryFindNearestChunk` (nearest chunk, not strict
  containment - the same "Chunk seam gap pattern" `Chunk.RespawnPoint`'s own resolution already
  relies on, since a hand-placed prop can sit right at a chunk boundary seam) and cached on
  `TraversalChallenge.Chunk`, so `TraversalChallengeSystem`'s per-tick checkpoint check doesn't
  re-scan every `Chunk` entity - it just reads that cached ref's `Transform3D` each tick.
- **A player who falls through a vanished platform needs no new code.** `PlayerFallSystem`/
  `FallRespawnUtility` already catch anyone dropping below `LevelConfig.FallDeathHeight` (default
  -10) unconditionally, every tick, and respawn them at the chunk's own baked `Chunk.RespawnPoint` -
  `TraversalChunk.prefab` already has one authored. This only actually triggers once the real gap
  geometry is deep enough, which needs verifying in-Editor (see "Editor authoring needed" below).
- **`PoiView` (the current, generic POI view) is reused as-is on the activator**, purely for its
  free Base-Skill prompt-widget wiring off the sibling `Interactable` - it silently no-ops on its
  own Inactive/Active/Expired visuals since this entity deliberately carries no `PoiActivation`
  component (that state shape assumes per-player usage tracking, which this POI has none of). A
  small dedicated `TraversalChallengeView` handles the real Idle/Active/Completed/Failed 3D visual
  swap, reading `TraversalChallenge.State` directly.
- **The countdown itself is a single, always-present, whole-team HUD banner
  (`TraversalChallengeWidget`)**, not a per-entity world-following widget. This was tried first as a
  manager-pooled widget anchored to the activator's own world Transform (the
  `CharacterUiWidget`/`InteractionPromptWidget` pattern) and corrected: the pause/no-new-spawns
  effect is global for the whole team, so a floating marker only visible to whoever's actually
  looking at that spot in the level is wrong - every player needs to see the countdown regardless of
  where they are (e.g. the teammate still fighting elsewhere). Instead it's one shared instance
  under the HUD, same idiom `BreathingCountdownWidget` already uses for "NEXT ASSAULT 00:30" -
  polls `Global.TraversalChallengeTimeRemaining` (a cheap client-facing convenience value written by
  `TraversalChallengeSystem` every tick a challenge is Active, same role `Global.BreathingTimeRemaining`
  already plays) every `QUpdate`, no per-entity following needed.
- **Shown only while `Global.HudBanner == HudBannerKind.TraversalChallenge`** - a shared, single
  "which top banner owns the screen right now" value (`HudBannerKind`, `GameState.qtn`), resolved
  once a tick by `CombatDirectorSystem.ApplyHudBanner` and also read by `BossWidget`/
  `DirectorTimelineUiWidget`/`BreathingCountdownWidget`, so all four stay mutually exclusive without
  each independently re-deriving its own condition off `GameState`/`ActiveTraversalChallengeCount`.
  Resolution order: Boss beats TraversalChallenge beats the DirectorTimeline default - the more
  specific/urgent thing always wins over the passive ambient one, not an arbitrary fixed ranking.
  Deliberately a NEW field, not a new value on `GameState` itself - `GameState` gates real
  simulation behavior (`PoiAvailabilityUtility`, `CombatDirectorSystem`'s own spawn gate,
  `GameplaySystemGroup` disables), and a Traversal Challenge being Active must never change what
  `GameState` the match is actually in (other players keep fighting normally elsewhere). This is
  also exactly why `BreathingCountdownWidget` needs its own explicit `HudBanner` check rather than
  just trusting `GameState` - a Traversal Challenge can be activated mid-Breathing-Break
  (`AvailableInBreathing=true`) without `CurrentState` ever leaving `Breathing`, so without it both
  banners would show stacked on screen at once.

## File map

- `Assets/_QuantumUser/Simulation/QTN/Poi/TraversalChallenge.qtn` - `TraversalChallengeState` enum,
  `TraversalChallenge` component, `Global.ActiveTraversalChallengeCount`.
- `Assets/_QuantumUser/Simulation/QTN/Poi/ContextInteraction.qtn` - `InteractableKind` gained
  `TraversalChallenge` (appended last, index 4 - never inserted, the existing 0-3 values are already
  baked into shipped prefab YAML).
- `Assets/_QuantumUser/Simulation/Systems/Poi/TraversalChallengeUtility.cs` -
  `ResolveInteractionState`/`TryActivate`/`Complete`/`Fail`, same shape as `HealingShrineUtility`,
  plus `ResolveChunkAnchor`/`ResolveCachedAnchor` (chunk-relative offset resolution, reusing
  `FallRespawnUtility.TryFindNearestChunk`).
- `Assets/_QuantumUser/Simulation/Systems/Poi/TraversalChallengeSystem.cs` - ticks `RemainingTime`
  and checks checkpoint proximity (`EnemyMovementUtility.FindPlayersInRadiusForPickup`, the same
  proximity idiom `ChestSystem` already uses) for every `Active` instance.
- `Assets/_QuantumUser/Simulation/Systems/Poi/ContextInteractionSystem.cs` /
  `Assets/_QuantumUser/Simulation/Systems/Player/SkillSystem.cs` - one more per-kind dispatch case
  each, same shape as every other `InteractableKind`.
- `Assets/_QuantumUser/Simulation/Systems/Director/SurvivalProgressionUtility.cs` /
  `Assets/_QuantumUser/Simulation/Systems/Director/CombatDirectorSystem.cs` - the two global-pause
  guard points, widened to also check `Global.ActiveTraversalChallengeCount`.
- `Assets/_QuantumUser/Simulation/QTN/Events.qtn` - `TraversalChallengeActivated`/
  `TraversalChallengeCompleted`/`TraversalChallengeFailed`.
- `Assets/_QuantumUser/Simulation/Default/SystemSetup.User.cs` - `TraversalChallengeSystem`
  registered inside `GameplaySystemGroup`, right after `PoiActivationSystem`.
- `Assets/_QuantumUser/View/Entities/Poi/TraversalChallengeView.cs` - new, small View companion
  (Idle/Active/Completed/Failed 3D visuals only), placed alongside the existing `PoiView` on the
  activator prefab.
- `Assets/_QuantumUser/View/Entities/Poi/TraversalPlatformView.cs` - View companion for the platform
  entity itself (`TraversalPlatform.prefab`, spawned/destroyed via `SpawnedPlatforms[]` above), not
  the activator. On spawn: reads its `visualCollider`'s world bounds center (before
  `cubeVisualBuilder.Generate()` runs and replaces that collider), wraps the generated visual mesh
  in a runtime pivot transform centered there - correcting for `CubeVisualBuilder`'s own documented
  bottom-min-corner pivot convention - moves the generated visual off the entity root and onto that
  pivot instead (the pivot itself is never parented under the entity to begin with), then tweens it
  up from `riseDistance` below into its resolved position. On destroy: since the
  visual is already detached, it survives the entity's own teardown to play a shake (now genuinely
  centered on the pivot) then sink-and-destroy sequence instead of vanishing instantly with the
  entity - a platform has no ChestView-style "~1 frame grace window" before Quantum destroys its
  View GameObject (`Fail()` destroys every `SpawnedPlatforms[]` entry the same tick it fails), so
  unlike `ChestView`/`SentryView`'s own tweened-but-still-parented children, this one has to be off
  the entity's hierarchy for its whole lifetime, not just at the moment of destruction.
- `Assets/_Project/Scripts/UI/InGame/Hud/TraversalChallengeWidget.cs` - single always-present HUD
  countdown banner, same idiom `BreathingCountdownWidget` already uses - not per-entity, reads
  `Global.HudBanner`/`TraversalChallengeTimeRemaining` directly.
- `Assets/_QuantumUser/Simulation/QTN/GameState.qtn` - new `HudBannerKind` enum + `Global.HudBanner`,
  shared by `TraversalChallengeWidget`/`BossWidget`/`DirectorTimelineUiWidget`/
  `BreathingCountdownWidget`.
- `Assets/_QuantumUser/Simulation/Systems/Director/CombatDirectorSystem.cs` - new
  `ApplyHudBanner`, called from `ApplyPhaseGameState` every tick, resolves `Global.HudBanner`.

## Current status

The code compiles once Quantum's `.qtn` codegen runs and is registered in `SystemSetup.User.cs`.
Nothing is authored yet, so nothing spawns at runtime until the following is done in the Editor:

1. Create `TraversalChallengeActivator.prefab` (`Assets/_QuantumUser/Entities/LevelProps/`) -
   mirror `HealingShrine.prefab`'s structure: solid non-trigger `QuantumEntityPrototype.
   PhysicsCollider`, `QPrototypeInteractable{Kind=TraversalChallenge, Radius≈3}`,
   `QPrototypeTraversalChallenge{...}`, plus both `PoiView` and `TraversalChallengeView`.
2. `TraversalPlatform.prefab` `EntityPrototype` exists (solid ground-layer collider, no gameplay
   component) - verify its physics layer matches the level's real ground/floor layer. Still needs,
   by hand in the Editor: attach the new `TraversalPlatformView` to the root and assign
   `cubeVisualBuilder` (the `VisualCube` child's own `CubeVisualBuilder`) and `visualCollider` (that
   same child's `BoxCollider`). Leave `VisualCube`'s own `transform.localScale` authored at `(1,1,1)`
   as-is - don't hand-author it to `(4,4,4)` to compensate for the root's own `4,4,4` scale.
   `TraversalPlatformView.Initialize` reparents `cubeVisualBuilder`'s whole GameObject onto a runtime
   pivot BEFORE calling `Generate()`, and `SetParent(..., worldPositionStays: true)` recomputes its
   local scale to preserve world scale in the process - it already comes out as `(4,4,4)` (what
   `Generate()`'s own grid-size math needs) with no separate authoring step. Pre-scaling it by hand
   as well would double up and generate a wrong `16,16,16`-cell grid instead.
3. Place a `TraversalChallengeActivator` instance as a second child under `TraversalChunk.prefab`'s
   existing `Entities` GameObject, alongside the already-baked chest instance.
4. Author `Duration`, `CheckpointRadius`, `CheckpointPosition`, `PlatformPositions[]`,
   `PlatformCount`, `PlatformPrototype`, and `Availability`
   (`AvailableInCombat=true, AvailableInBreathing=true` recommended - the whole point is pausing
   things *during* Survival, Breathing already has both paused for free) directly on the instance's
   `QPrototypeTraversalChallenge` - all positions authored as offsets relative to the CHUNK's own
   placement (same frame of reference as the chest's own `ChunkSpawnConfig` offset), not the
   activator prop's own placement within it.
5. Re-run `ChunkSpawnBaker`'s Bake Spawns on `TraversalChunk.prefab` (fully replaces
   `TraversalChunk.asset`'s `Spawns[]`, harmlessly re-baking the existing chest entry too).
6. Verify in-Editor that the real gap between the activator and the checkpoint drops a player below
   `LevelConfig.FallDeathHeight` if a mid-bridge platform is destroyed while they're standing on it.
7. Wire a `TraversalChallengeWidget` scene instance under the HUD (`GameplayWindow`, alongside
   `BreathingCountdownWidget`) with its `root`/`countdownText` assigned - doesn't exist in-scene
   yet, so no countdown banner shows anywhere until this is built.

Not yet manually verified end-to-end in-Editor, solo or co-op.

## Known simplifications

- Completion is a single proximity checkpoint near the far side, not sequential per-platform
  tracking - "reach the end" rather than "cross each platform in order."
- One shared `PlatformPrototype` reused at every authored position - no per-platform visual variety
  or independently-timed reveal (all platforms appear together on activation).
- A failed attempt is not retryable at all (`State` settles on `Failed`, permanently - see "Why this
  design" above) - a level designer who wants a more forgiving puzzle should author a longer
  `Duration`, not rely on a retry.
- If the activator entity were ever destroyed while `State == Active` (nothing in this codebase
  destroys a POI prop today, so not currently reachable), `SpawnedPlatforms[]` would leak and
  `ActiveTraversalChallengeCount` would leak incremented, permanently freezing `SurvivalTime`/
  spawning for the rest of the run. Not guarded against - would need
  `ISignalOnComponentRemoved<TraversalChallenge>` if this ever becomes reachable.
