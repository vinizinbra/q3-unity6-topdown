# Traversal Challenge

An interactable world prop, placed in a `ChunkType.Traversal` chunk, that turns a gap-crossing into
a timed co-op puzzle instead of a passive backdrop.

**The reward only exists because of the attempt.** It is ROLLED up front (one of `RewardPrototypes`
- Weapon Chest / Rift Mutation / Hero Chest / Weapon Perk Stone (prompt label "Perk Stone"; 4 = the array cap), uniform `f.RNG` roll on the challenge's first Idle
tick, `TraversalChallengeUtility.EnsureRewardRolled` -> `RolledRewardIndex`) so the POI prompt
previews exactly what will spawn: `TraversalChallengeView` reads the rolled prototype's
`Chest.Kind` and pushes `LevelUpCategoryUtility.GetChestRewardIcon/Name` into the prompt via
`InteractionPromptWidget.SetReward` (the activator's own `promptRewardIcon/Text` are left blank -
they'd only be a fallback). Activating spawns that same rolled prototype on the far side at the
chunk-relative `RewardPosition` (chunk pos + chunk rot * offset, then `GroundOffsetUtility.Apply` -
identical placement to the chunk spawner the old baked chest used); failing destroys it (if still
unopened); completing leaves it. It
used to be baked into `Traversal1ChunkSpawnConfig` and sat there from level load, which read as a
free chest next to an optional challenge - that entry and the chunk prefabs' baked chest preview
are gone, so a `ChunkSpawnBaker` re-bake won't bring it back.

**Return pad.** Once the challenge is `Completed` AND the reward is collected (`Chest.Opened`, or the
entity is gone - `TraversalChallengeUtility.IsRewardCollected`), `TraversalChallengeSystem` spawns
`ReturnTeleportPrototype` (`TraversalReturnTeleport.prefab`) once at `ReturnTeleportPosition`. A
non-incapacitated player must STAND inside its `Radius` (0.7) for `ChargeTime` (1.2s) - stepping
off resets their charge - and is then teleported to `ReturnDestinationPosition` (next to the
activator) by `TraversalReturnTeleportSystem`, with the same `KCC.Teleport` + zero-every-velocity
reset the boss-arena teleport and fall respawn use. Per-player charge lives on the pad
(`Charger[4]`/`Charge[4]` parallel keyed arrays, AreaAllyBudget-style). Both positions are
chunk-relative and ground-snapped at spawn. The pad is permanent for the rest of the run (a teammate
who was fighting elsewhere can use it later); it only appears after the reward is taken so nobody
warps away from an unopened chest.

Look: the pad itself is the `TeleportDock-Minimal` SPRITE (Art/Sprites/Environment, in
`GameplaySprites.spriteatlas`), set up like the other sprite POIs - `Sprite-Unlit-Default`, tilted 45deg
to face the 45deg gameplay camera, **sorting order -1** so heroes (sprites, order 0) and every
particle (order 1) draw over it. The importer pivot is the dock floor's centre; the sprite is lifted
so its bottom edge sits on the ground and pulled toward the camera by the same amount (at a 45deg
camera, ground offset == lift) so the drawn floor centre lands on the entity origin / trigger.
Shadow = the shared pooled blob (`HasShadow` on `Visual` -> `GroundBlobManager`, order -4):
`baseScale` 8.4 nudged 0.2m toward the camera - at the smaller size it sat exactly under the art and
was fully hidden. The whole pad (Visual) is at 0.49 = 70% of the first sprite pass (user call,
2026-10-21) - dock ~2.2m wide, trigger `Radius` 0.7, `DepartFx` 0.56; uniform scaling keeps the
floor centred on the trigger and the shadow (HasShadow follows lossyScale). Verified in renders on grass and sand with a real hero (QMax) standing on it.
Particles/FX keep the soft-geometric style (generated soft textures, additive): `SoftDot`,
`SoftHex*`, `HexGlow`, `SoftRay`; the exit marker (no sprite) still uses the soft hex decals.
- Idle: the dock sprite + a soft blue glow over its cyan centre ring, a few rising soft light rays
  and dots. Deliberately sparse.
- Charging (largest live `Charge / ChargeTime`): the charge emitters (pad rays + dots + an
  inward-swirling dot vortex, plus the exit's rays) start from zero and grow in rate/speed
  (Velocity-over-Lifetime speed modifier - their startSpeed is 0)/size (relative to authored),
  eased in so the last moments are the most intense; the centre glow swells; `PortalLoop` hums with pitch rising with the charge (faded on step-off).
- Exit marker (detached onto the destination): hidden at rest, grows in while anyone charges.
- Teleport (`EventTraversalReturnTeleported`): `DepartFx` on the pad and `ArrivalFx` on the exit -
  hex pop, soft-ray beam, hex shockwave ring, ray + dot burst - plus `HeroSpawnLand`; the exit then
  lingers ~0.6s and shrinks away. `DepartFx` also plays once when the pad first appears.

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

`TraversalChallengeActivated`/`Completed`/`Failed` each also fire a shared `AnnouncerManager` banner
("TRAVERSAL CHALLENGE STARTED"/"COMPLETE"/"FAILED", see `docs/announcer.md`) on **every connected
client**, not just whoever triggered it - wired in `TraversalChallengeWidget.Awake`, unfiltered
unlike `InteractionPromptWidget`'s own per-local-player toast, since this is whole-team awareness
(same reasoning the countdown banner itself already documents), not personal feedback. Originally a
`ToastManager` popup; moved to the same big-banner `AnnouncerManager` `TeamChallengeWidget`'s own
STARTED/COMPLETE/FAILED announcements already use, for the same dramatic weight.

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
- **`TraversalChallengeView` extends the shared `InteractionPromptPoiView` base directly**, purely
  for its free Base-Skill prompt-widget wiring off the sibling `Interactable` - NOT the generic
  `PoiView` (which also owns `PoiActivation`-driven Inactive/Active/Expired visuals, meaningless
  here since this entity deliberately carries no `PoiActivation` component - that state shape
  assumes per-player usage tracking, which this POI has none of). Originally this was `PoiView`
  placed alongside a sibling `TraversalChallengeView`; the two classes separately declaring their
  own "Active Visual" field read as one duplicate/conflicting Inspector slot (only
  `TraversalChallengeView`'s ever did anything - `PoiView`'s own QUpdate silently no-ops with no
  `PoiActivation` present), so `InteractionPromptPoiView` was split out of `PoiView` and
  `TraversalChallengeView`/`TeamChallengeView` (`docs/optional-team-challenge.md`) both extend it
  directly instead - no sibling `PoiView` needed on either prefab anymore. `TraversalChallengeView`
  reads `TraversalChallenge.State` directly for its own Idle/Active/Completed/Failed 3D visual
  swap.
- **The reward preview (the chest waiting on the far side) reuses `InteractionPromptPoiView`'s own
  generic `promptRewardIcon`/`promptRewardText` fields verbatim** (see
  `docs/optional-team-challenge.md`, which introduced them) rather than a Traversal-Challenge-
  specific field - this POI has no config asset of its own (every tuning value is authored directly
  on the instance's `QPrototypeTraversalChallenge`, see the file map), and the reward here never
  varies by anything live anyway (it's always the same chest), so the same constant, Setup-time,
  per-instance Inspector field the shared base already exposes for Optional Team Challenge covers
  it for free with zero new plumbing.
- **`TraversalChallengeView` also pushes a per-`TraversalChallengeState` description override**
  (`SetDescriptionOverride`, "CHALLENGE IN PROGRESS"/"CHALLENGE COMPLETE"/"CHALLENGE FAILED") onto
  its own `InteractionPromptWidget` for the same reason `TeamChallengeView` does (see
  `docs/optional-team-challenge.md`): `TraversalChallengeUtility.ResolveInteractionState` collapses
  Active/Completed/Failed all into the single generic `AlreadyUsed` `ContextInteractionState`
  bucket, so the plain `promptAlreadyUsedDescription` text alone can't tell them apart.
- **The countdown itself is a single, always-present, whole-team HUD banner
  (`TraversalChallengeWidget`)**, not a per-entity world-following widget. This was tried first as a
  manager-pooled widget anchored to the activator's own world Transform (the
  `CharacterUiWidget`/`InteractionPromptWidget` pattern) and corrected: the pause/no-new-spawns
  effect is global for the whole team, so a floating marker only visible to whoever's actually
  looking at that spot in the level is wrong - every player needs to see the countdown regardless of
  where they are (e.g. the teammate still fighting elsewhere). Instead it's one shared instance
  under the HUD, same idiom `BreathingWidget` already uses for "NEXT ASSAULT 00:30" -
  polls `Global.TraversalChallengeTimeRemaining` (a cheap client-facing convenience value written by
  `TraversalChallengeSystem` every tick a challenge is Active, same role `Global.BreathingTimeRemaining`
  already plays) every `QUpdate`, no per-entity following needed.
- **Shown only while `Global.CurrentState == GameState.TraversalChallenge`.** This used to be a
  separate `HudBannerKind`/`Global.HudBanner` field, deliberately kept out of `GameState` for
  exactly the collateral-damage reason above (`PoiAvailabilityUtility`/`CombatDirectorSystem`'s
  spawn gate/`GameplaySystemGroup` disables all key off `GameState`, and a Traversal Challenge must
  not disturb any of them elsewhere in the level). Folded into `GameState` anyway as part of the
  Announcer/event-driven-HUD pass (confirmed with the user), once `Global.CombatPhaseState` was
  introduced to take over `GameState`'s old "real phase" job: `CombatDirectorSystem.
  ApplyPhaseGameState` now writes the real Survival/Breathing/Boss phase to `CombatPhaseState`
  (its own Breathing-exit-side-effect compare is scoped to that field, immune to this overlay), and
  a new `ApplyEffectiveState` resolves `Global.CurrentState` every tick (Boss > TeamChallenge >
  TraversalChallenge > `CombatPhaseState`) - the exact same resolution order and multi-instance-safe
  live-scan approach `HudBannerKind` always used, just now landing in the one field every consumer
  (HUD widgets included) already reads, with a real `GameStateChanged` event to react to instead of
  polling. `PoiAvailabilityUtility`'s existing `default: return false` already covers
  `TeamChallenge`/`TraversalChallenge` for free (POIs go unavailable during a challenge, same as
  during `Boss`/`Upgrade` - confirmed acceptable with the user), and neither new value disables
  `GameplaySystemGroup`, so the rest of the team keeps playing normally exactly as before. This also
  means `BreathingWidget` no longer needs its own explicit exclusion check - `CurrentState`
  is simply never `Breathing` while this overlay is active, so a Traversal Challenge activated
  mid-Breathing-Break (`AvailableInBreathing=true`) can no longer show stacked with it. See
  `docs/game-state.md`/`docs/announcer.md`.

## File map

- `Assets/_QuantumUser/Simulation/QTN/Poi/TraversalChallenge.qtn` - `TraversalChallengeState` enum,
  `TraversalChallenge` component (incl. reward + return-pad fields), `TraversalReturnTeleport`
  component, `Global.ActiveTraversalChallengeCount`. `EventTraversalReturnTeleported` is in
  `Events.qtn`.
- `Assets/_QuantumUser/Simulation/Systems/Poi/TraversalReturnTeleportSystem.cs` - the pad's
  proximity teleport (registered right after `TraversalChallengeSystem`).
- `Assets/_QuantumUser/View/Entities/Poi/TraversalReturnTeleportView.cs` +
  `Assets/_QuantumUser/Entities/LevelChunk/TraversalReturnTeleport.prefab` - the pad (dark disc,
  pink additive rim/inner ring/glow as single-particle horizontal billboards - mesh quads can't be
  tinted with the vertex-colour-only `Mobile/Particles/Additive` materials - rising sparkles,
  light streaks, depart burst). Base material `TraversalReturnPadBase.mat` (URP Unlit).
- `Assets/_QuantumUser/Simulation/QTN/Poi/ContextInteraction.qtn` - `InteractableKind` gained
  `TraversalChallenge` (appended last, index 4 - never inserted, the existing 0-3 values are already
  baked into shipped prefab YAML).
- `Assets/_QuantumUser/Simulation/Systems/Poi/TraversalChallengeUtility.cs` -
  `ResolveInteractionState`/`TryActivate`/`Complete`/`Fail`, `SpawnReward`/`IsRewardCollected`/
  `SpawnReturnTeleport`, same shape as `HealingShrineUtility`,
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
- `Assets/_QuantumUser/View/Entities/Poi/TraversalChallengeView.cs` - Idle/Active/Completed/Failed
  3D visuals + per-`TraversalChallengeState` prompt description override; extends
  `InteractionPromptPoiView` directly (no sibling `PoiView` needed on this prefab).
- `Assets/_QuantumUser/View/Entities/Poi/InteractionPromptPoiView.cs` - shared base extracted out of
  `PoiView` (see `docs/optional-team-challenge.md`'s file map for the full rationale), owning the
  world-space Base-Skill prompt (title/per-state description/reward preview) with no dependency on
  `PoiActivation` - `PoiView` extends this and adds only its own `PoiActivation`-driven visuals;
  `TraversalChallengeView`/`TeamChallengeView` extend it directly instead.
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
  countdown banner, same idiom `BreathingWidget` already uses - not per-entity, extends
  `GameStateGatedWidget` (see `docs/announcer.md`), reads `Global.TraversalChallengeTimeRemaining`
  directly for its own countdown text. Timer presentation: the pill pops in when the banner appears and pulses on
  every second; at <= `urgentSeconds` (10) the text turns Rift Pink, the pill flashes dark and the
  clock icon shakes on each tick; at <= `finalCountdownSeconds` (5) its own
  `ChallengeCountdownWidget` instance (`TraversalFinalCountdownWidget`, HUD sibling - the same
  slam-in digit + screen-edge flash Team Challenge uses for its 3/2/1, see
  `docs/optional-team-challenge.md`) takes over, upper third of the screen, never the centre where
  the hero stands. All of it is a pure function of the sim's remaining time (late-join/rollback-safe).
- `Assets/_QuantumUser/Simulation/QTN/GameState.qtn` - `GameState` gained `TeamChallenge`/
  `TraversalChallenge` values plus `Global.CombatPhaseState`; the old `HudBannerKind` enum/
  `Global.HudBanner` field is gone. See `docs/game-state.md`.
- `Assets/_QuantumUser/Simulation/Systems/Director/CombatDirectorSystem.cs` - `ApplyEffectiveState`
  (was `ApplyHudBanner`), called from `ApplyPhaseGameState` every tick, resolves
  `Global.CurrentState`'s overlay.

## Current status

The code compiles once Quantum's `.qtn` codegen runs and is registered in `SystemSetup.User.cs`.
Nothing is authored yet, so nothing spawns at runtime until the following is done in the Editor:

1. Create `TraversalChallengeActivator.prefab` (`Assets/_QuantumUser/Entities/LevelProps/`) -
   mirror `HealingShrine.prefab`'s structure: solid non-trigger `QuantumEntityPrototype.
   PhysicsCollider`, `QPrototypeInteractable{Kind=TraversalChallenge, Radius≈3}`,
   `QPrototypeTraversalChallenge{...}`, and `TraversalChallengeView` alone - **no sibling `PoiView`
   component**, `TraversalChallengeView` extends `InteractionPromptPoiView` directly and supplies
   the Base-Skill prompt itself (see "Why this design" above). Assign the inherited
   `promptTitle`/description fields, `promptRewardIcon`/`promptRewardText` (e.g. a chest icon +
   "CHEST") for the reward preview row, and optionally the 3 per-state `...PromptDescription`
   overrides (defaults are already sensible).
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
7. Wire a `TraversalChallengeWidget` scene instance under the HUD (alongside
   `BreathingWidget`) with its `root`/`countdownText` assigned - doesn't exist in-scene
   yet, so no countdown banner shows anywhere until this is built.

Not yet manually verified end-to-end in-Editor, solo or co-op.

Reward-on-activate + return pad (2026-10-20): authored on `TraversalActivator.prefab` (RewardPosition =
the old baked chest offset (35.03, 2.68, 5.86); pad at (31.5, 1, 2.5) on the far block's free corner;
destination (1.6, 1, 9.8) on the start block ~2.7m from the activator). Compiles; pad look checked
in a gameplay-camera render; not yet played through.

## Layouts

All per-layout tuning (platforms, checkpoint, reward, return pad) lives on the ACTIVATOR prefab's
`QPrototypeTraversalChallenge`, so each chunk layout needs its own activator prefab + its own
`ChunkSpawnConfig` pointing at it. Both layouts share the one Traversal `ChunkPool` entry (weight 1
each), so a run gets one or the other.

- **`TraversalChunk`** (left -> right, `AllowedConnectionSides = Left`): `TraversalActivator` +
  `Traversal1ChunkSpawnConfig`. 1 spawned platform; the rest of the crossing is permanent stones with
  3.6-5.4m gaps (dash territory). `TraversalChunk 1.prefab` is an unused duplicate.
- **`TraversalChunkWest`** (rebuilt from scratch 2026-10-27, right -> left, `AllowedConnectionSides =
  Right`, 40x20): `TraversalActivatorWest` + `TraversalWestChunkSpawnConfig`, standard 4x4x4
  platforms. START (h0, x34..40) -> P1 (h0) -> P2 (h1) -> P3 (h2, up-screen) -> **jump + Dash 7.5m, +1**
  to REST (h3, x12..18 z8..14) -> hop 2m down-screen +1 to P4 (h4) -> GOAL (h4, x0..6 z2..8) at the
  far-left end, where the reward + return pad sit. An h4 pillar (x9..11 z3..5) sits exactly midway between GOAL (x6) and P4 (x14..18), 3m each side, centered on P4's line;
  at h4 it is a stepping stone (P4 -> pillar 3m hop, pillar -> GOAL 3m hop), so the final stretch
  needs no Dash, and REST -> pillar (~3.6m, +1) can skip P4. Labelled top view:
  `Recordings/TraversalChunkWest-top.png`. 4 spawned platforms, 40s.
- **`TraversalChunkNorth`** (reworked 2026-10-27, bottom -> top, `AllowedConnectionSides = Bottom`,
  20x40): `TraversalActivatorNorth` + `TraversalNorthChunkSpawnConfig`, standard 4x4x4 platforms.
  START (h0, x3..9 z0..5) -> BIG (permanent h0 island x6..18 z7..11 - one wide platform, since the
  activator can only spawn one platform prefab) with an h3 WALL (x6..18 z11..13) flush along its whole
  front edge -> jump off BIG's side out over the water and **jump + Dash forward around the wall end**
  (~6.5m) to P1 (h0, x16..20) -> P2 (h1) -> P3 (h2), climbing left -> **jump + Dash 7.5m, +1** to REST
  (h3, x2..8 z29..35; wait out the cooldown) -> **jump + Dash 7.5m, +1** right to P4 (h4) -> 2m hop to
  GOAL (h4, x14..20 z35..40, top edge) with the reward + return pad. 4 spawned platforms, 45s. The h3
  wall can't be dashed over (Dash height from h0 is ~2.6m, wall top is 4m). Going around the LEFT end
  can reach P2 (~6.4m, +1) and skip P1 - still a jump + Dash around the wall. Labelled top view:
  `Recordings/TraversalChunkNorth-top.png`.

**Reach (measured against a West playtest, 2026-10-27).** The hero capsule radius is 0.6
(`KCCSettings`): the edge auto-jump fires with the centre already past the edge and a landing counts
on capsule contact, so a normal jump clears ~4-5m flat (more with move-speed buffs), ~3.6m at +1.
Dash (`Dash.asset`) is a flat 5m burst that locks height, works mid-air, stops at walls, 4s cooldown /
1 charge - a plain Dash from the edge barely clears ~5.5m, so in practice gaps past jump range are
**jump + Dash** gaps: ~8-9m flat, ~8m at +1. Nothing reaches +2 (jump apex ~1.6m) - that is the only
hard gate, and both halves of each course must be separated by >= +2 or > ~9m without the spawned
platforms. Design: hops 2-3m, jump + Dash gaps 7-8m, walls (h2+) on the straight/diagonal line to force a
sidestep, a rest island between dashes for the cooldown. `PlatformPositions` are pad CENTERS (top =
y + 2). Waypoints are on the start island only.

**In-game check (2026-10-27, West, Play mode):** spawned platforms land exactly on the authored
chunk-relative offsets, with the prefab's baked collider size (root scale -> box extents), Ground
layer. The tileset view matches the collider except the prebuilt `Block_2x2` piece (used for a 2x2
pad), which overhangs it by ~0.2m per side.

**Cheats (CheatMenu > World > Map):** *Teleport To Traversal* (next to the first activator) and
*Activate Traversal* (TryActivate on every Idle challenge, as the sender) - `CheatActionKind.
TeleportToTraversal`/`ActivateTraversal`. Teleport first: activation is unavailable in the lobby.

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

## Level-ups during the challenge

No level-up screen opens while the challenge runs - XP is still collected, earned levels queue and
their screens open once it ends (`LevelUpUtility.IsLevelUpScreenHeld`, see
`docs/level-up-upgrades.md`).
