# Boss Encounter (Phase Trigger + Boss Window)

The boss encounter is the `SurvivalConfig` phase-timeline entry with `Kind = Boss` (see
`docs/survival-director.md` for the timeline itself, and `docs/game-state.md` for the `GameState.Boss`
state). This doc covers the trigger/plumbing that seals the arena, teleports players, spawns the
boss, and hard-pauses over a full-screen reveal card + camera cutaway. A boss's own in-combat
behavior (phases, stagger, combos) is the pre-existing, separate `BossDataAsset`/`BossSystem`/
`BossRuntimeState` framework - see the Scrapjaw boss-combat plan (`.claude/plans/clever-herding-metcalfe.md`)
for that framework's own design and the one boss built on it so far.

## Boss Phase Trigger

The moment `SurvivalConfig`'s phase timeline reaches a `Kind = Boss` entry,
`RunPhaseUtility.BeginBossEncounter` fires once.

### Arena markers (`BossArena` component)

Both the teleport destination(s) and the boss spawn position(s) come from two hand-authored marker
fields on a new `BossArena` component (`BossEncounter.qtn`, both `[4]`-capped arrays) rather than a
single computed center - deliberately its own component, not fields on `Chunk` itself (every chunk in
the level carries `Chunk`, dozens of them from procedural generation, so these arrays would otherwise
sit wasted on every non-Boss chunk).

A level designer places real marker GameObjects in the Boss Arena
(`BossTeleportPointMarker`/`BossSpawnPointMarker`, `Assets/_QuantumUser/View/World/`) and a new
`BossArenaMarkerBaker` `[Button]` (mirrors `ChunkRespawnPointBaker`, requiring both `QPrototypeChunk`
and the new `QPrototypeBossArena` on the same prototype) bakes them in. Unauthored (or no `BossArena`
component at all) falls back to a single point at the chunk's own plain geometric center, so nothing
breaks if a level never places either marker. Whatever position is resolved gets re-grounded via
`EnemyMovementUtility.TryFindGroundHeight`/`GetGroundLayerMask` (same top-down ground raycast every
normal Director spawn already uses), so nothing lands inside floor/prop geometry even off a slightly-off
marker.

### `BeginBossEncounter`

Then it:

- **Teleports every connected player**, one marker per player slot (wraps around if fewer points than
  players) so they land spread out instead of stacked (`KCC.Teleport`, same idiom
  `DamageUtility.RespawnPlayer` already uses).
- **Enables every hand-placed `BossArenaGate`-tagged collider entity** (an empty marker tag,
  `BossEncounter.qtn` - the level designer places the actual sealing colliders in the Editor; a new
  signal-only `BossArenaGateSystem` forces each one's `PhysicsCollider3D` disabled the instant it's
  created, regardless of what `IsEnabled` was authored on the prototype, so there's no "forgot to
  uncheck it" footgun; this whole mechanism does no adjacency computation of its own).
- **Spawns `SurvivalPhase.BossPrototype`** (a new `AssetRef<EntityPrototype>` field, read only for a
  Boss entry) once per resolved `BossSpawnPoints` entry via `EnemySystem.SeedFromEnemyData` - so 2+
  authored spawn points spawn that many copies of the same boss (e.g. twin bosses), not different
  kinds - deliberately without `EnemyLifecycle` (same "shouldn't auto-retire, doesn't need Director
  pressure accounting" reasoning already established for the Scrapjaw boss-combat plan's own
  `SpawnPackDeliveryData` pack adds).

`CombatDirectorSystem`'s own gate already stops normal Director spawning entirely once `GameState`
becomes `Boss` - confirmed with the user, only the boss itself (and whatever its own abilities spawn)
should be active during the fight.

### Hard pause (`PauseDuration` / `BossPauseSystem`)

Right after spawning, if `SurvivalPhase.PauseDuration > 0` (a new `FP` field, 5s authored by the MVP
generator - only takes effect once the generator is re-run, since editing the generator's own source
doesn't retroactively update an already-authored `SurvivalConfig_MVP.asset`), `BeginBossEncounter`
also `f.SystemDisable<GameplaySystemGroup>()`s - the exact same mechanism
`LevelUpUtility.OpenUpgradeScreen` uses to pause a Level-Up screen, just auto-timed via a new always-on
`BossPauseSystem` (counts `Global.BossPauseTimer` down, re-enables the group at 0) instead of
player-choice-driven. Confirmed with the user: a genuine hard freeze (player movement/weapons/skills,
KCC, `EnemySystem`/`BossSystem` AI including the boss itself, the fall systems - everything inside the
group), not just a visual overlay, so nothing can act while the Boss Window reveal plays.

### `EnemyView` rig (opt-in boss visual)

`EnemyView` (`Assets/_QuantumUser/View/Entities/Enemy/`) gained a second, opt-in way to author a
spawned enemy's visual - unblocks a boss's own one-off `EntityPrototype` specifically, since the normal
path (`EnemyDataAsset.ViewPrefab`, pooled and fit-scaled to the collider radius at runtime) exists only
because the SHARED generic Director prototype has to visually represent many different `EnemyData`.
`SpawnSprite` now checks for an `EnemyViewRig` already baked as a real child of `spriteRoot` first; if
found, it skips `ViewPrefab`/`ViewPrefabPool` entirely (nothing to instantiate, the GameObject already
exists) but still applies the exact same `ResolveFitScale` sprite-bounds math, `Vector3.down * radius`
bottom-pivot positioning, and `HasShadow` radius-based auto-scale to it - confirmed with the user, a
boss's rig should sit at its collider's bottom center and dynamically track its own radius exactly like
a normal enemy's pooled sprite does. Only rotation stays manual. A new `Resolve Scale` `[Button]` on
`EnemyView` re-runs the whole resolve pass on a live entity in Play Mode for quick iteration (tweak
`viewRadiusPadding`/`Stats.Radius`/the sprite, re-click, no respawn needed). No other enemy is affected.

### `EnemyFallSystem` (Boss/Elite fall recovery)

A new `EnemyFallSystem` (`Assets/_QuantumUser/Simulation/Systems/Enemy/`) gives Boss/Elite-tier enemies
the same "fall off the level → take fall damage → respawn to safety" treatment `PlayerFallSystem`
already gives players - confirmed with the user, so a Boss or Elite pushed off a ledge
(physics/knockback) can't end up lost/stuck instead of just dying normally like every other tier still
does. The shared nearest-chunk/inset-into-bounds respawn math was extracted out of `PlayerFallSystem`
into a new `FallRespawnUtility` so both systems use the exact same logic; Elite reuses it directly off
its own current position (it has no tracked "last grounded" position the way `PlayerMovement` does),
while Boss respawns specifically at its own sealed Boss Arena's `BossSpawnPoints[0]`
(`LevelGenerationSystem.ResolveBossSpawnPositions`, ground-corrected the same way
`RunPhaseUtility.BeginBossEncounter` already is) rather than the generic nearest-chunk fallback, since
respawning it into some nearby chunk would strand it outside its own `BossArenaGate`-sealed boundary
mid-fight.

### No XP drops / level-up screens during the fight

Confirmed with the user: a boss's own spawned adds still deal damage and die normally, but they must
never be able to interrupt the fight with a level-up screen. Two independent guards, both keyed off
`Global.CurrentState == GameState.Boss`:

- `ExperienceUtility.TrySpawnDrop` refuses to spawn an `ExpOrb` at all while Boss is active - an add's
  XP is simply forfeited, not banked/deferred for after the encounter.
- `DebugCheatSystem.TryOpenNextPendingLevelUp` (the shared per-level upgrade-screen drain - see
  `docs/level-up-upgrades.md`'s own "Multiple levels from one `Grant` call" entry) also refuses to open
  a new screen while Boss is active, for whatever might already be sitting in
  `Global.DebugPendingLevelUps` from XP collected right before the encounter began - it just holds the
  drain (doesn't discard it) until `GameState` leaves `Boss`, then resumes normally.

Both are XP/level-up-specific; a Chest opened by a talent, or any other Choice Window, is unaffected by
either guard.

### Boss HUD (2026-08-17)

View-side, the boss gets its own dedicated HUD instead of sharing the normal enemy UI:

- `EnemyView.RefreshSprite` now skips `EnemyUiWidgetManager.SpawnWidget` entirely for `EnemyTier.Boss`
  (a new `EnemyView.IsBoss` gate) - no floating `CharacterUiWidget` above the boss at all.
- A new single-instance `BossWidget` (`Assets/_Project/Scripts/UI/InGame/Hud/BossWidget.cs`) shows a
  top-screen name + HP bar + shield bar for whichever entity `frame.Filter<BossRuntimeState, Health>()`
  finds. Shield turned out to already be enemy-agnostic (`EnemySystem.SeedShield` seeds it off
  `EnemyDataAsset.Stats.ShieldMultiplier` for any enemy, boss included - `GrasslandOutpostBoss.asset`
  already has `ShieldMultiplier = 1` authored), so `BossWidget`'s shield bar needed no new
  simulation-side work.

**Shared HUD banner (2026-08-18, updated for the Announcer/event-driven-HUD pass).**
`BossWidget`/`SurvivalWidget`/`TeamChallengeWidget`/`TraversalChallengeWidget`/
`BreathingWidget` (see `docs/traversal-challenge.md`/`docs/optional-team-challenge.md`) all
read the single `Global.CurrentState` (`GameState`, `GameState.qtn`) instead of each independently
re-deriving "am I the one that should show" - `TeamChallenge`/`TraversalChallenge` are now real
`GameState` values (originally a separate `HudBannerKind`/`Global.HudBanner` field), resolved once a
tick by `CombatDirectorSystem.ApplyEffectiveState` (Boss beats TeamChallenge beats TraversalChallenge
beats the real underlying phase, `Global.CombatPhaseState`), so all five always stay mutually
exclusive on-screen even though a Team/Traversal Challenge deliberately never changes the *real*
phase. All five now extend the shared `GameStateGatedWidget` base
(`Assets/_Project/Scripts/UI/InGame/Hud/GameStateGatedWidget.cs`) and react to the real
`GameStateChanged` event instead of polling `Global` every `QUpdate` - see `docs/announcer.md` and
`docs/game-state.md`.

**`BossWarningWidget`** (`Assets/_Project/Scripts/UI/InGame/Hud/BossWarningWidget.cs`, View-only) shows
a "BOSS APPROACHING" HUD banner + countdown - but only during the LAST `Breathing` phase before `Boss`
(peeked via `SurvivalConfig.Phases[CurrentPhaseIndex + 1].Kind == Boss`, same idiom
`SurvivalWidget`'s own marker-skip logic already uses) and only once
`Global.BreathingTimeRemaining` drops to its own `warningThreshold` (10s default) - confirmed with the
user, the boss encounter itself stays fully automatic (SurvivalConfig-driven), this is purely a
heads-up layered on the pre-existing countdown, not a new pause/trigger stage.

`BossWidget` also triggers the `BossWindow` reveal (below) the instant it finds the boss entity for the
first time each encounter (edge-detected via a new `_wasBoss` field) - reuses `BossWidget`'s own
already-running "find the boss, resolve its `EnemyDataAsset`" lookup rather than a separate trigger
component, and casts to `BossDataAsset` to pull `Title`/`Subtitle`/`UiSprite` for the window if it
resolves.

## Boss Window

A full-screen reveal card (`Assets/_Project/Scripts/UI/InGame/BossWindow.cs`, `UiWindow` subclass) shown
once per encounter, right as the boss spawns (triggered from `BossWidget`) - similar in spirit to
`ChooseWindow`'s own intro animation (same `ShakeGrowImpactAnimation` reveal building block,
`useUnscaledTime: true` throughout) but with its own content and sequence: icon background → boss icon
→ title background → title text → subtitle text, each staggered via its own `ShakeGrowImpactAnimation`,
then a hold, then the whole thing fades away via a `disappearCanvasGroup` (`Tween.Alpha`, 0.3s default).

Not wired into `WindowManager` - called directly (`.Show()`), so it doesn't hide the HUD underneath,
same "bypass WindowManager to keep the HUD visible" choice Cursed Rift/Store/Blacksmith already made.
`Title`/`Subtitle`/`UiSprite` are three new fields on `BossDataAsset` (`BossDataAsset.View.cs`, a new
partial file mirroring `EnemyDataAsset.View.cs`'s own simulation/view split) - deliberately separate
from the base `EnemyDataAsset.EnemyName` already used by `BossWidget`'s in-combat HUD name, since the
reveal card's own text doesn't need to match that 1:1. A `[Button] TestIntroAnimation()` (with an
optional `testBossData` field to preview real content) lets it be tuned standalone in Play Mode without
a live boss encounter.

### Camera-focus cutaway

The reveal is bracketed by a one-way camera-focus cutaway (confirmed with the user), also driven from
`BossWidget`:

- A new `ScreenFadeWidget` (`Assets/_Project/Scripts/UI/InGame/Hud/ScreenFadeWidget.cs`, single shared
  instance, `Tween.Alpha` on a full-screen `CanvasGroup`) fades to black.
- `FollowCamera` (`Assets/_QuantumUser/View/Camera/FollowCamera.cs`) gets a new
  `SetFocusOverride(Transform, snap: true)`/`ClearFocusOverride(snap: true)` pair that locks its framing
  onto a single transform instead of averaging its normal multi-player `_targets` list (the `snap`
  default instantly repositions `_smoothedPosition` rather than easing, since the whole point is to
  already be exactly on target the instant the fade reveals it - no visible pan).
- `ScreenFadeWidget` fades back in showing the boss in focus while `BossWindow` plays over it. The
  boss's own Unity `Transform` is resolved via `QuantumEntityViewUpdater.GetView(bossEntity)` (found
  once via `FindFirstObjectByType` in `BossWidget.Awake`) - same idiom `SentryView` already uses for
  resolving another entity's view from outside its own `CustomQuantumEntityViewComponent`.

Returning to normal framing once `Global.BossPauseTimer` counts down to 0 (edge-detected each
`QUpdate`, same shape as the `_wasBoss` edge-detect) is deliberately NOT a mirrored fade-out/fade-in -
confirmed with the user, just `ClearFocusOverride(snap: false)` directly, letting `FollowCamera`'s own
existing `Update()` lerp ease it back to the players naturally (nothing jarring to hide - the camera's
already framing the arena, players and boss both right there). The enter cut degrades gracefully to a
plain instant camera snap (no fade) if `ScreenFadeWidget.Instance` isn't found in the scene.

## Current status / Editor authoring needed

The code compiles once codegen picks up `BossEncounter.qtn`'s new `BossArena` component
(`TeleportPoints`/`SpawnPoints`, moved off `Chunk` itself for the performance reason above). Nothing is
authored yet:

- No `BossArenaGate`-tagged colliders exist in `QuantumGameScene.unity` around the Boss Arena's own
  corridor(s).
- The Boss chunk's own prototype doesn't have a `QPrototypeBossArena` added yet, and no
  `BossTeleportPointMarker`/`BossSpawnPointMarker`s have been placed under it either (both fall back to
  the chunk's plain geometric center until baked via `BossArenaMarkerBaker`'s `[Button]`, so this
  doesn't block testing, just leaves it unrefined).
- `SurvivalConfig_MVP.asset`'s `Boss` phase's `BossPrototype` is unassigned (no real boss
  `EntityPrototype` exists yet).
- `BossWidget` needs Editor wiring before it shows anything: a scene panel (name text/HP slider/shield
  slider) doesn't exist yet, and `SurvivalWidget`'s new `visualRoot` field needs its existing
  slider/text/marker children wrapped under one new child container and assigned to it (its script
  currently sits directly on `DirectorTimelineWidget`, so `visualRoot` can't just be that same
  GameObject - see the field's own tooltip).
- `BossWarningWidget`/`BossWindow` are both entirely unauthored in-scene (no prefab/hierarchy built,
  `BossWidget.bossWindow` unassigned, no `Title`/`Subtitle`/`UiSprite` authored on
  `GrasslandOutpostBoss.asset`).
- `ScreenFadeWidget` has no scene instance yet (a full-screen black `Image`/`CanvasGroup` under the HUD
  Canvas) - until one exists, the camera-focus cutaway silently degrades to an instant, unhidden camera
  snap (no fade) rather than failing outright.

Not yet manually verified end-to-end in-Editor.

## Stagger / Kneel threshold is % of boss max HP (2026-10-20)

`StaggerProfileData.Threshold`/`RegenRate` (flat damage) became `ThresholdPercents` (list of fractions
of `Health.MaxHealth`, Nth break uses entry N, last repeats), `RegenRatePercent` (fraction of MaxHealth
per second) and `MinHealthPercent` (at/below this remaining life the boss never kneels again and the
meter is cleared). Being fractions of the party/difficulty-scaled MaxHealth, the Kneel cadence scales
with party size. `BossRuntimeState.StaggerBreakCount` tracks N. Empty list = no mechanic. Both bosses
(W1 World1Boss, W2 Dune Leviathan): one Kneel per 20% of max HP in damage, regen 1%/s, no Kneel at or
below 20% life. Needs `.qtn` codegen (`StaggerBreakCount`).

## Boss defeat sequence (2026-10-20)

The killing blow on the **last living boss** (a twin dying first just lingers as before) no longer
lets it vanish after `DeathLingerTime`; `DamageUtility.ResolveDeath` -> `BossDefeatUtility.TryBegin`
adds `BossDefeatSequence` (`QTN/Enemy/BossDefeatSequence.qtn`), **disables `GameplaySystemGroup`**
(same hard pause as the intro - nobody can act, take damage or go down) and raises synced
`BossDefeated(Boss, Killer, Position, Duration, BurstTime)`. The always-on `BossDefeatSystem`
(registered right before `VictorySystem`) then runs the timeline:

- **first tick** - `ClearArena`: every other enemy pops (`EnemyExploded`, no drops/credit), every
  `Projectile` and every enemy-owned (or orphaned) `AreaOwner` hazard is destroyed.
- **`BossDataAsset.DefeatBurstTime`** (2.5s) - `RiftShardUtility.GrantAll(DefeatRiftShardReward)`
  (20, real shards to every player, each scaled by their own gain multiplier) + synced
  `BossDefeatBurst(Boss, Position, RiftShards)`.
- **`DefeatSequenceDuration`** (4.5s) - destroys the boss, re-enables the group; `VictorySystem` sees it
  gone the same tick (last phase) -> `RunResultManager` popup.

Boss start/defeat have no voice-over triggers (removed). Haptics' `bossKilled` still fires on `EntityDied` = killing blow.

**View** - `Assets/_Project/Scripts/Boss/BossDefeatSequenceDirector.cs`, self-installed (like
`HapticsDirector`, nothing to place in a scene), all tuning in its serialized fields. Off `BossDefeated`:
hit-stop (boss rig `Animator.speed` 0 + particles paused, 0.15s real time), white full-screen flash (its
own runtime overlay canvas), heavy `FollowCamera.Shake`, `FollowCamera.SetFocusOverride(boss, snap:
false)` (pans + zooms in, spread 0 = minZoom). Throes: Pixie's "Unstable" look
(`StatusEffectsManager.ExplodeMarkParticlePrefab`, own instance parented to the rig) swelling with the
rig jitter, red flicker and rumble all speeding up toward the burst. Off `BossDefeatBurst`: Pixie's bomb
blast (`EffectsManager.PlayBossDefeatExplosion` = `defaultAreaBlastEffect`, x8 + x4), flash, shake,
`bossKilled` haptic, rig renderers hidden, shard shower (copies of the real Rift Shard pickup view -
`RiftShardPrototype` -> `ViewPrototype` -> `EntityView.Prefab`, Quantum components stripped - pop out
then home onto every hero; pink-sphere fallback if the view isn't loaded), `AnnouncerManager` "BOSS DEFEATED" + the VO. Camera eases back 0.6s before
gameplay resumes; the rig is restored afterwards (pooled views). Synced events arrive ~RTT late online;
fine since gameplay is paused anyway.

**Still placeholder / not done:** no impact sting / victory jingle / music
cut, no hero victory emoji/pose (emojis are a sim command).

**Dying sprite instead of a death animation:** `EnemyViewRig` has an optional Dying section - `Dying
Sprite` (swapped in at the killing blow, held until the burst hides the rig), `Dying Sprite Target`
(empty = Reference Sprite) and `Dying Hides Other Sprites` (on = the pose is the whole body on one
sprite; off = only the target changes, e.g. a new face). Empty = no swap. Not authored on either boss
yet - no art exists. The throes flicker multiplies over each body sprite's authored color (restored
after) and skips `GroundAnchoredOverlay` props like the Leviathan's sand.

**Rift Pink VFX + no tip-over (2026-10-20):** the throes mark and every blast (burst, hero pops) now use
`Resources/BossDefeat/BossDefeatUnstable` / `BossDefeatExplosion` - copies of `ExplosiveMark` /
`SimpleExplosion` with every warm particle color (start color, color over lifetime/by speed, trails)
remapped onto Rift Pink #FD3971 (orange -> exactly #FD3971, yellow -> #FD3971 toward white, red -> darker #FD3971, all x original brightness;
whites, alpha, brightness kept). Originals untouched; missing copies fall back to them. Materials/textures
weren't changed - an orange-baked texture would still show orange. `EnemyBlobAnimationView` skips its
generic `TriggerDie` tip-over-and-fall for an entity carrying `BossDefeatSequence`.

**Bigger payout (2026-10-20):** `DefeatRiftShardReward` 20 -> **100** (upgrades cost 150+), and the burst
also hoovers every uncollected Rift Shard pickup on the map into the same grant
(`BossDefeatUtility.CollectLooseRiftShards` - the run is ending, they'd be lost otherwise);
`BossDefeatBurst.RiftShards` is that total. `DefeatSequenceDuration` 4.5 -> 6.5s so the payout finishes
before the result popup. View: up to 40 real-shard pieces released over 1s in a rolling wave; each landing
plays the real Rift Shard pickup sound + pickup flash via `FlyingCurrencyManager.PlayRiftShardShowerArrival`
with the pitch climbing a semitone per shard (cap 12); a big Rift Pink "+N RIFT SHARDS" tally (announcer's
font) counts up per landing, punches each time, and slams (x1.5 + flash + shake + haptic) on the last;
it fades out with the camera return, before the result popup. Neither field is serialized on the boss
assets yet, so the code defaults apply to both bosses.

**Loot fountain (2026-10-20):** the shower also flies visual-only Coins (20) and XP orbs (24) - real pickup
looks via the same prototype -> ViewPrototype -> EntityView.Prefab copy as the shards
(`ResolvePickupTemplate`), random heroes, spread over the same release window, pickup flash on arrival, no
sound/tally/grant. The burst also plays `Resources/BossDefeat/BossDefeatCrystalSparks`: ~105 tumbling 3D
Rift Pink crystals (`RiftCrystalSpark` mesh - flat-shaded hexagonal bipyramid - with a URP Particles/Simple
Lit material, Rift Pink emission) sprayed up/out of a hemisphere, arcing under gravity and shrinking out;
unscaled time. Built once by an Editor script; tune the prefab/material directly.

**Punchier intro (2026-10-20):** `BossIntroSequenceDirector` (`Scripts/Boss/`, self-installed) adds impact to
the existing reveal - `BossWidget` calls `BossIntroSequenceDirector.Play(boss, pauseRemaining)` at the cut
(under the black fade). Cut wide (zoom 1.9) -> 0.35s -> zoom slams to 0.7 (ease-in, small overshoot) -> on
impact: white flash, Rift Pink blast + crystal spray at the boss's feet, heavy shake, haptic, the boss's view
root punches x1.2 and wobbles back -> slow push-in to 0.62 with a low rumble while the card is up -> zoom
override released 0.7s before `BossPauseTimer` ends, then BossWidget's existing ease-back. It reuses the
defeat director's FX (`BossDefeatSequenceDirector.Instance.FlashScreen/PlayPinkExplosion/PlayCrystalSparks`)
so intro and defeat read as bookends. New `FollowCamera.SetZoomOverride(float?)`: exact zoom, rigid follow
(no `speed` smoothing) while set; null = normal eased zoom resumes.

**Heroes idle during hard pauses:** `BlobAnimationView` reads velocity as zero while `GameplaySystemGroup`
is disabled (boss intro/defeat, level-up screens) - the KCC keeps the last tick's velocity frozen, which
left heroes running in place.

**Boss Window vs. the slam (2026-10-20):** the card's full-screen dim went from 78% (`#000000C8`) to 35%
black (`#00000059`, on `BossWindow.prefab`; Canvas.prefab doesn't override it), and `BossWidget` now shows
the card from `BossIntroSequenceDirector.Play`'s `onImpact` callback instead of at the cut - the slam plays
clear, then the card bangs in with the impact (its 4s `totalDuration` still ends with the ~5s pause). The
card is centred on screen exactly where the camera centres the boss, so the impact/explosion happens just
before it covers that spot.

## Forced actions, phase entry beats, debug prototypes (2026-10-29, Riff Riot)

Opt-in additions, defaults keep W1/W2 exactly as before - see `docs/riff-riot-boss.md`:
`BossPhaseData.EntryAction` (forced on phase entry), `BossSystem.TryForceAction` +
`BossRuntimeState.PendingForcedSlot` (forces a SkillAction, waiting out an Active delivery such as a leap instead of
yanking the boss out mid-air), `BossDataAsset.DeferForcedActionsWhileActive` (routes the stagger break through it),
`BossDataAsset.DebugPrototype` (the boss's own prototype for Spawn Near Local Player / Jump To This Boss) and
`RuntimeConfig`/`Global.DebugBossPrototypeOverride` (any world's Boss phase spawns that prototype instead).
