# Run Phase (Combat / Breathing)

A repeating **Breathing Break** between segments of continuous combat - enemy spawning stops, but
players stay fully controllable and can explore/use Breathing-only world POIs (Healing Shrine,
Cursed Rift - see `docs/breathing-poi.md`). Read this doc first if you haven't;
`docs/breathing-poi.md` only covers what's specific to the two POIs.

## Design choices

- **`Breathing` was added to the existing `GameState` enum**, not a parallel "RunPhase" enum. The
  architecture (`GameState` + `GameStateUtility.SetState`, see `docs/game-state.md`) already *is*
  the Run Phase concept - a second, overlapping state machine would need to stay in sync with the
  first for no benefit. `Breathing` behaves like `Lobby` (does **not** disable
  `GameplaySystemGroup` - players must stay controllable), not like `Upgrade`.
- **Breathing is literally an entry in `SurvivalConfig.Phases[]`**, not a separately-tracked timer
  or a second config asset - confirmed with the user. `SurvivalPhase` gained a `SurvivalPhaseKind
  Kind` field (originally a plain `Boolean IsBreathing`, promoted to an enum once Elite/Boss needed
  room too - see "Elite / Boss phases" below); author a Breathing Break by adding a `Phases[]`
  entry with `Kind = Breathing` and a `Duration` (its length), interleaved with the Director's own
  combat-phase entries
  (e.g. Combat 180s, Breathing 30s, Combat 180s, Breathing 30s, ...). `SurvivalConfig` already
  *is* the run's own pacing timeline - a separate `RunPhaseConfig` asset (an earlier iteration of
  this design) or a flat `BreathingBreakStartTimes` list (a later one) were both considered and
  rejected once Breathing became a first-class phase - one timeline, not two kept in sync by hand.
  Only `Duration` is read for a Breathing entry; `BudgetPerPulse`/`PulseInterval`/`TargetPressure`/
  `MaxAliveEnemies`/`AllowedGroups` are ignored.
- **`CombatDirectorSystem` (not a separate system) detects the Breathing transition.** Since
  Breathing is just another `SurvivalPhase`, and `CombatDirectorSystem` already owns walking that
  same `Phases[]` timeline (`SurvivalProgressionUtility.Tick`), extending it to also sync
  `GameState` off the current phase's `Kind` value is a natural continuation of what it
  already does - not a third system watching the same state from outside.
- **`SurvivalTime` and `PhaseTimer` are independent clocks** (confirmed with the user 2026-08-14,
  fixing a real regression the phase-based Breathing rework had introduced - see "Independent
  timers" below). `PhaseTimer` tracks "how long has the CURRENT phase been running" and has to keep
  advancing through Breathing too (it's what actually ends the Break); `SurvivalTime` tracks
  cumulative COMBAT time only and freezes entirely during Breathing, so a phase authored with
  `Duration=120` always hands its successor a `SurvivalTime` of exactly `120`, regardless of how
  long (or whether) a Breathing Break ran in between.
- **Progression now runs through BOTH `Survival` and `Breathing`**, not `Survival` only -
  `CombatDirectorSystem`'s own gate changed from `CurrentState != Survival` to `CurrentState !=
  Survival && CurrentState != Breathing`. This is required for `PhaseTimer` to keep advancing
  *through* a Breathing phase (otherwise the Break would never end) - `Lobby`/`Upgrade` remain
  excluded exactly as before. The Director's own spawn/pulse call
  (`CombatDirectorUtility.TryPulse`) is separately skipped whenever the current phase `Kind ==
  Breathing`, so enemies never spawn during a Break even though progression itself keeps ticking.
- **Entering Breathing does NOT clear enemies.** An earlier iteration of this feature
  force-retired every non-`Economy.Persistent` Director-purchased enemy the instant Breathing
  began (`RunPhaseUtility.ClearCombatEnemies`, since deleted) - removed once the encounter-clear
  hold below existed, since a force-clear made that hold meaningless (there was almost never
  anything left to hold on) AND instantly emptied the screen the moment a Break started, which
  reads as a bug, not a feature. Whatever's alive when Breathing begins just stays alive - killed
  by players, or naturally `Retired` via the pre-existing `EnemyLifecycle` Irrelevant timeout
  (`docs/survival-director.md`), completely unchanged and still running during Breathing. Leaving
  Breathing DOES still sweep every uncommitted `CursedRiftInteraction`/open `StoreInteraction`/
  `BlacksmithInteraction` (see `docs/breathing-poi.md`/`docs/store-blacksmith.md`) - a one-shot side
  effect living in `RunPhaseUtility`, called by `CombatDirectorSystem` only on the exact tick the
  current phase's `Kind` value actually changes away from `Breathing` - never re-run while already
  in that state.
- **The area isn't "secured" just because the phase boundary was crossed.** Spawning stopping
  (`TryPulse` skipped) and `SurvivalTime` freezing both happen the INSTANT `CurrentState` becomes
  `Breathing`, same as always - but `PhaseTimer` (and therefore `BreathingTimeRemaining`/the Break
  actually ending) only starts advancing once every currently-alive enemy is gone, killed or
  naturally expired (`Global.BreathingAreaSecured`, see "Elite / Boss phases" below - Breathing now
  reuses that exact same `IsEncounterCleared` hold, just checking ANY enemy instead of a specific
  tier). Since nothing force-clears on entry anymore (see above), this hold is what actually does
  the work of keeping the Break's own countdown from starting while enemies are still around,
  `Economy.Persistent` or not.
  `BreathingCountdownWidget`'s "AREA SECURED" banner/countdown/skip-vote UI all wait for this too,
  so the HUD never claims the area is secured while something hostile is still alive.

## `GameState.qtn`

```
enum GameState : Byte
{
    Lobby, Survival, Upgrade, Event, Boss, Breathing
}

global
{
    GameState CurrentState;
    GameState PreUpgradeState;
    FP BreathingTimeRemaining;
    Int32 BreathingIndex;
    Boolean BreathingAreaSecured;
}

// Per-player, lazily added (never pre-seeded) - see "Skip vote" below.
component BreathingSkipVote
{
    Int32 VotedAtBreathingIndex;
}
```

`BreathingTimeRemaining` is maintained by `CombatDirectorSystem` every tick as the current
`SurvivalPhase.Duration` minus `Global.PhaseTimer` (purely a cheap client-facing convenience value
- `BreathingCountdownWidget` reads it directly with no asset lookup of its own). `BreathingIndex`
is 0-based, incremented once each Breathing→Survival transition completes - the reset key
`PoiUsagePolicy.OncePerPlayerPerBreak` (and now `BreathingSkipVote`'s own `VotedAtBreathingIndex`
too) compares against (see `docs/breathing-poi.md`'s "Generic POI availability/usage
infrastructure"). `BreathingAreaSecured` is maintained by `SurvivalProgressionUtility.Tick` off the
same `IsEncounterCleared` check that holds `PhaseTimer` open (see "Independent timers" and "Elite /
Boss phases" below) - true only while `CurrentState == Breathing` AND every currently-alive enemy
is gone.

## Independent timers (`SurvivalTime` vs. `PhaseTimer`)

Confirmed with the user 2026-08-14: `SurvivalTime` and `PhaseTimer` are deliberately **independent
clocks**, not the same value read two ways - fixing a real regression the phase-based Breathing
rework (above) had silently introduced. Before this fix, `SurvivalProgressionUtility.Tick`
advanced `Global.SurvivalTime` unconditionally every tick, including through a Breathing phase -
so a `Duration=120` combat phase followed by a `Duration=30` Breathing Break handed the NEXT
combat phase a `SurvivalTime` of `150`, silently stretching the designer's authored pacing by
however long every Break in the run happened to last (worse once Skip votes exist - a skipped
Break would inconsistently stretch pacing LESS than a full one).

Now:
- **`PhaseTimer`** - "how long has the CURRENT phase (combat OR Breathing) been running." Resets
  to `0` on every phase transition, advances every tick regardless of `Kind`, EXCEPT while the
  current phase's own encounter isn't cleared yet - Elite/Boss hold on their own matching
  `EnemyDataAsset.Tier`, Breathing holds on ANY currently-alive enemy (see "Elite / Boss phases"
  below, which now covers Breathing too) - and is the ONLY thing that actually drives a transition
  (`Tick`'s own `>= Duration` check) - it has to keep advancing through Breathing (once unblocked)
  or the Break would never end.
- **`SurvivalTime`** - "how much COMBAT time has this run accumulated," consumed by
  `BalanceConfig`'s run curves/co-op scaling (see CLAUDE.md's "Run Curves & Co-op Scaling") and any
  future HUD run-timer. Advances ONLY while the current phase's `Kind != Breathing` - frozen
  entirely during a Breathing phase (Elite/Boss phases do NOT freeze it - only their own
  `PhaseTimer` advancement is held, see below). Unlike `PhaseTimer`, this freeze is NOT conditional
  on enemies being cleared - it freezes the instant `Kind == Breathing`, full stop, same tick
  spawning also stops.

Concrete example (the one that motivated this): Phase 1 authored `Duration=120` (combat), Phase 2
a Breathing entry `Duration=30`, Phase 3 the next combat phase. `SurvivalTime` reaches `120` the
instant Phase 1 ends (matches `PhaseTimer` there, since Phase 1 isn't Breathing). Through all 30s
of Phase 2, `PhaseTimer` counts `0→30` (ending the Break on schedule) while `SurvivalTime` stays
frozen at `120`. Phase 3 begins with `SurvivalTime` still exactly `120` - not `150` - regardless of
whether that Break ran its full 30s or was skipped early (see below).

## Skip vote

Any connected player can vote to end the CURRENT Breathing phase early by sending a zero-payload
`SkipBreathingCommand` - once EVERY connected player has voted for that same Break, it ends
immediately instead of waiting out its authored `Duration`. `SurvivalTime` is completely unaffected
either way (see above) - Phase 3 always begins at `SurvivalTime==120` whether the Break ran the
full 30s or was skipped after 5.

- **`BreathingSkipVote`** (per-player, lazily `f.AddOrGet`'d the first time that player votes -
  never pre-seeded at spawn) stores `VotedAtBreathingIndex`. Presence, not just field value, is
  what "has this player voted" means (a never-voted player has no component at all) - avoids a
  false-positive match against `BreathingIndex`'s own default-`0` on the run's very first Break.
  Compared against `Global.BreathingIndex` - same self-cleaning "which iteration did this happen"
  convention `PoiUsageEntry.UsedAtBreathingIndex` already uses (`Poi.qtn`), so a stale vote from an
  earlier Break never silently counts toward the next one; no explicit clear-all-votes step exists
  or is needed.
- **`RunPhaseUtility.TryForceSkipBreathing(f, survivalConfig)`**, called from
  `CombatDirectorSystem.Update` immediately before `SurvivalProgressionUtility.Tick` (same-tick
  dependency: a vote cast this tick must be visible to this same tick's `Tick` call). Processes any
  `SkipBreathingCommand` sent this tick (idempotent re-vote - resending just rewrites the same
  index), then, only while the current phase `Kind == Breathing` and every connected player has
  now voted, sets `Global.PhaseTimer = currentPhase.Duration` directly - `Tick`'s own existing
  `>= Duration` check then ends the phase exactly as if it had run out naturally, no separate
  transition path to keep in sync. A no-op entirely outside Breathing (voting during Combat just
  pre-registers for whichever Breathing phase comes next - harmless).
- Requires **at least one connected player** to fire (an empty lobby trivially "voting
  unanimously" would be a meaningless, confusing force-skip).
- **Interaction with the encounter-clear hold (see "Elite / Boss phases" below):** forcing
  `PhaseTimer` to `currentPhase.Duration` does NOT by itself end the phase if the area still isn't
  secured - `Tick`'s own advance condition requires `encounterCleared == true` too. A unanimous
  skip vote cast while enemies are still alive effectively pre-arms the transition: nothing happens
  until `BreathingAreaSecured` flips true, at which point the phase ends immediately (no remaining
  countdown to wait out) instead of running the rest of its `Duration`.

## `SurvivalConfig.cs`

```csharp
public enum SurvivalPhaseKind
{
    Combat,
    Breathing,
    Boss,
    Elite
}

[Serializable]
public struct SurvivalPhase
{
    public String Name;           // Editor/log-readability only, never read by simulation logic
    public SurvivalPhaseKind Kind;
    public FP Duration;
    public FP BudgetPerPulse;
    public FP PulseInterval;
    public FP TargetPressure;
    public Int32 MaxAliveEnemies;
    public List<AssetRef<EnemyGroupConfig>> AllowedGroups;
}

public class SurvivalConfig : AssetObject
{
    public SurvivalPhase[] Phases;
}
```

`Breathing` only reads `Duration` (see above), and additionally holds `PhaseTimer` from advancing
until every currently-alive enemy (any tier) is dead or expired. `Boss`/`Elite` behave like `Combat`
for spawning and `SurvivalTime` purposes - they still pulse-spawn from their own `AllowedGroups`
exactly like a normal combat phase - but hold `PhaseTimer` the same way, just scoped to their own
matching `EnemyDataAsset.Tier` (`EnemyTier.Boss`/`EnemyTier.Elite`) instead of every enemy. See
"Elite / Boss phases" below (now covers Breathing's own hold too).

## `CombatDirectorSystem.cs`

Still one merged system (Domains 1+2 of the Survival Director design - see
`docs/survival-director.md`), now also owning the Combat↔Breathing transition as a natural
extension of Domain 1 (Survival Progression):

```
Update:
    if CurrentState is neither Survival nor Breathing -> return (Lobby/Upgrade/Event/Boss)
    RunPhaseUtility.TryForceSkipBreathing(f, survivalConfig)   // processes Skip votes
    currentPhase = SurvivalProgressionUtility.Tick(f, survivalConfig)   // SurvivalTime/PhaseTimer independent; PhaseTimer also holds for an uncleared Elite/Boss/Breathing encounter, and maintains Global.BreathingAreaSecured - see below
    ApplyPhaseGameState(f, currentPhase)
    if currentPhase.Kind == Breathing -> return   // no Director spawning during a Break
    CombatDirectorUtility.TryPulse(...)      // unchanged - also runs for Boss/Elite phases
```

`ApplyPhaseGameState` computes `desiredState = currentPhase.Kind == Breathing ? Breathing :
Survival` (Boss/Elite both map to `Survival` - they're vocabulary/pacing distinctions for the
Director's own timeline, not a `GameState` transition of their own); if it differs from
`Global.CurrentState`, runs the one-shot transition side effect leaving Breathing
(`RunPhaseUtility.CancelUncommittedCursedRiftInteractions` + `CloseStoreInteractionsOnBreathingEnd`
+ `CloseBlacksmithInteractionsOnBreathingEnd` + `BreathingIndex++` - entering Breathing has NO side
effect here, see above) and calls `GameStateUtility.SetState` - then always refreshes
`BreathingTimeRemaining`.

## Elite / Boss phases (and Breathing's own encounter-clear hold)

Confirmed with the user: an `Elite` or `Boss` phase entry holds `PhaseTimer` from advancing (see
"Independent timers" above) until every currently-alive enemy of the matching `EnemyDataAsset.Tier`
(`EnemyTier.Elite`/`EnemyTier.Boss`) is dead, REGARDLESS of `Duration` - an encounter can spawn more
than one qualifying enemy, and the phase only ends once all of them are gone.
`SurvivalProgressionUtility.IsEncounterCleared` checks this live via a plain `f.Filter<Enemy>()`
scan every tick (same "read live, never maintain a separate counter that could desync" idiom
`PoiActivationUtility.AnyConnectedPlayerCanUse` already uses) rather than tracking spawn/death
counts - `Combat` phases always read as cleared (no gate at all). Enemies are still introduced the
normal way, via `CombatDirectorUtility.TryPulse` pulling from the phase's own `AllowedGroups`
(author a group containing only `EnemyTier.Elite`/`Boss` enemies) - this mechanic only gates the
PHASE TRANSITION, nothing about spawning itself changes.

`Breathing` reuses the exact same `IsEncounterCleared` hold, just with no tier filter - ANY
currently-alive enemy (not `EnemyActionPhase.Dead`) holds it open, mirrored into
`Global.BreathingAreaSecured` for the View to read (see `GameState.qtn`). Spawning stopping and
`SurvivalTime` freezing both still happen the instant the phase boundary is crossed regardless of
this hold (see "Independent timers" above) - only `PhaseTimer`'s own advancement, and therefore
when the Break actually ends, waits for the area to be clear. Since Breathing no longer
force-clears anything on entry (see above), this hold is genuinely load-bearing - whatever's alive
when the phase boundary is crossed, `Economy.Persistent` or not, has to be killed by a player or
fall `Irrelevant` long enough to naturally `Retire` (`EnemyLifecycleSystem`, unaffected by
`GameState`) before the Break's own countdown starts.

`Global.BreathingAreaSecured` also gates every Breathing-only POI (Healing Shrine, Cursed Rift,
Store, Blacksmith) - `PoiAvailabilityUtility.IsAvailable`'s `Breathing` case now checks
`AvailableInBreathing && BreathingAreaSecured`, so a POI stays dormant/unusable for the same window
the HUD countdown stays hidden, not just from the phase boundary onward. See
`docs/breathing-poi.md`.

`DirectorTimelineUiWidget` (the HUD progress bar, `Assets/_Project/Scripts/UI/InGame/Hud/`) shows a
phase-specific icon at the boundary marker where a non-Combat phase begins - resolved via
`SpriteManager.GetSprite(SurvivalPhaseKind.ToString())` (the same name-keyed sprite-library lookup
`CurrencyUiWidget`/`PurchasableCardUi` already use). No dedicated `SpriteConfigSO` subclass needed -
`SpriteManager` searches every registered config by name regardless of which subclass authored it,
so entries named `"Breathing"`/`"Boss"`/`"Elite"` just need to live in any config already registered
on the scene's `SpriteManager` (e.g. the existing `SpriteConfigCurrency` asset). `Combat` never gets
an icon, hard-coded (not just "leave that entry unauthored"). The marker prefab itself needs a
`DirectorPhaseMarkerWidget` component with its own `Icon` (`Image`) reference explicitly assigned -
an explicit ref rather than a `GetComponentInChildren<Image>` guess, since the marker's own tick/line
visual is often an `Image` too.

## Boss phase trigger

Entering a `Boss`-`Kind` phase is the one `SurvivalPhaseKind` that gets its own dedicated
`GameState` value (`GameState.Boss`, not `Survival`) and a real one-shot side effect -
`CombatDirectorSystem.ApplyPhaseGameState` resolves `desiredState` via a proper switch now
(`Breathing -> Breathing`, `Boss -> Boss`, everything else -> `Survival`, unchanged for
`Combat`/`Elite`) and calls `RunPhaseUtility.BeginBossEncounter` on the exact tick the state is
about to become `Boss`, mirroring the same one-shot-per-edge shape already used for the Breathing
sweep in that same method. `CombatDirectorSystem`'s own outer gate (`CurrentState != Survival &&
!= Breathing`) already excludes any other state from `CombatDirectorUtility.TryPulse` - so wiring
`Boss` in here is what stops normal Director spawning entirely once the fight begins, confirmed
with the user (only the boss itself, and whatever its own abilities spawn, should be active during
the encounter). `GameState.Boss` does **not** pause `GameplaySystemGroup`, same as
`Survival`/`Breathing` - the whole point is an active, playable fight, not a menu-style pause.

`BeginBossEncounter` does three things, all gated on a Boss Arena chunk actually existing
(`LevelGenerationSystem.TryFindBossArenaChunk`, mirroring `TryGetLobbyStartBounds`'s exact shape).
Both the teleport destination(s) and the boss spawn position(s) are resolved off that same chunk
via two new hand-authored marker fields on a new `BossArena` component (`BossEncounter.qtn`)
rather than a single computed center - deliberately its own component, not fields on `Chunk`
itself (every chunk in the level carries `Chunk`, dozens of them from procedural generation, so
these `[4]`-capped arrays would otherwise sit wasted on every non-Boss chunk). A level designer
places real marker GameObjects in the Boss Arena (`BossTeleportPointMarker`/`BossSpawnPointMarker`,
`Assets/_QuantumUser/View/World/`) and a new `BossArenaMarkerBaker` `[Button]` (mirrors
`ChunkRespawnPointBaker`'s exact shape, requiring both `QPrototypeChunk` and the new
`QPrototypeBossArena` on the same prototype) bakes each into `BossArena.TeleportPoints`/
`SpawnPoints` (chunk-local, same convention `Chunk.Waypoints`/`RespawnPoint` already use).
Unauthored (the pre-marker default, or simply no `BossArena` component present at all) falls back
to a single point at the chunk's own plain geometric footprint center, so nothing breaks if a level
never places either marker. Whatever position ends up resolved - baked or fallback - gets
ground-corrected via `EnemyMovementUtility.TryFindGroundHeight`/`GetGroundLayerMask` (the same
top-down ground raycast
`GroupSpawnerUtility.TrySpawnGroup` already uses for every normal Director spawn), so neither the
teleported players nor the boss land inside floor/prop geometry even if a marker was placed
slightly off the real floor:

1. **Teleport** - one marker per player slot (up to 4), so connected players land spread out
   instead of stacked on the same spot; every connected player (`LevelUpUtility.GetConnectedPlayers`,
   widened from `private` to `internal` for this reuse) is assigned
   `teleportPositions[playerIndex % teleportPositions.Count]` (wraps around if fewer points than
   players are authored - always at least 1, the geometric-center fallback) and gets `KCC.Teleport`
   there plus zeroed kinematic/dynamic/external velocity, the exact idiom `DamageUtility.RespawnPlayer`
   already uses for the death/respawn teleport - no new teleport pattern introduced.
2. **Seal the arena** - a new empty marker component, `BossArenaGate` (`BossEncounter.qtn`), tags
   whichever collider entities the level designer hand-places around the Boss Arena's own
   corridor(s). A new signal-only system, `BossArenaGateSystem` (`ISignalOnComponentAdded<BossArenaGate>`,
   same shape as `MapGroundSettleSystem`), forces each one's `PhysicsCollider3D.Enabled` to `false`
   the instant it's created - regardless of what `IsEnabled` was authored as on the prototype, so
   there's no "forgot to uncheck it on this one gate" footgun to rely on Editor discipline for.
   This mechanism deliberately does no adjacency computation of its own - confirmed with the user,
   who places the actual gate colliders by hand (the Boss Arena chunk's `AllowedConnectionSides` is
   unrestricted, so a given run can end up with 1-4 real corridors depending on how that run's
   level generated) - `BeginBossEncounter` just flips `Enabled = true` back on every tagged entity.
3. **Spawn the boss(es)** - a new `SurvivalPhase.BossPrototype` field (`AssetRef<EntityPrototype>`,
   read only when `Kind == Boss` - `Duration`/`BudgetPerPulse`/`PulseInterval`/`TargetPressure`/
   `MaxAliveEnemies`/`AllowedGroups` are all ignored for a Boss entry too, same treatment a
   Breathing entry's own irrelevant fields already get, since Director spawning has already
   stopped) is spawned once per resolved `BossSpawnPoints` entry (or once at the geometric-center
   fallback if none are authored) - so placing 2+ `BossSpawnPointMarker`s spawns that many copies
   of the same boss (e.g. twin bosses), not different kinds. Each spawn does
   `f.Create(phase.BossPrototype)`, positions it, then `EnemySystem.SeedFromEnemyData` off whatever
   `EnemyData` the prototype already has baked in - mirrors `GroupSpawnerUtility.SpawnMember`'s own
   `f.Create` -> position -> `SeedFromEnemyData` sequence, deliberately **without** adding
   `EnemyLifecycle` (the same choice already made and confirmed safe for the Scrapjaw boss-combat
   plan's own `SpawnPackDeliveryData` pack adds - only `EnemyLifecycleSystem`/`CombatDirectorUtility`
   read that component and both already ignore entities without it - a boss should never auto-retire
   via the Irrelevant timeout). The boss's own dedicated `EntityPrototype` (not authored by this doc
   - see the Scrapjaw boss-combat plan, `.claude/plans/clever-herding-metcalfe.md`) is expected to
   already carry its own `EnemyData`/`BossRuntimeState`/`EnemySequenceState` baked in, same as any
   other self-contained one-off prototype in this codebase (Chests, POIs) - nothing about this
   trigger is boss-specific.
4. **Hard-pause for the reveal** - right after spawning, if `phase.PauseDuration > 0`,
   `f.SystemDisable<GameplaySystemGroup>()` (the exact same mechanism
   `LevelUpUtility.OpenUpgradeScreen` already uses to pause a Level-Up screen, just auto-timed
   instead of player-choice-driven) and `Global.BossPauseTimer` is seeded with it. A new always-on
   system, `BossPauseSystem` (registered outside `GameplaySystemGroup`, same "can't live inside the
   group it's responsible for re-enabling" reasoning `LevelUpSystem`/`ChestSystem`/
   `DebugCheatSystem`/`BossArenaGateSystem` already document for themselves), counts
   `BossPauseTimer` down every tick and calls `f.SystemEnable<GameplaySystemGroup>()` once it hits
   0. Everything inside the group freezes for that window - player movement/weapons/skills, KCC,
   `EnemySystem`/`BossSystem` AI (the boss itself included, already spawned by this point), the
   fall systems - confirmed with the user: this is deliberately a genuine hard freeze, not just a
   visual overlay, so nothing can act while the Boss Window reveal (below) plays.

`EnemyFallSystem` (further below) reuses this same `ResolveBossSpawnPositions` call - a fallen Boss
respawns at `BossSpawnPoints[0]` specifically (the first authored point, or the geometric-center
fallback), not wherever it happened to fall from.

`EnemyView` (View, `Assets/_QuantumUser/View/Entities/Enemy/`) also gained a second way to author a
spawned enemy's visual, specifically to unblock a boss's own one-off `EntityPrototype`: normally
`EnemyView.SpawnSprite` instantiates `EnemyDataAsset.ViewPrefab` fresh at runtime (pooled via
`ViewPrefabPool`) and fits it to the entity's own collider radius - necessary because the SHARED
generic entity prototype (`DirectorConfig.EnemyPrototype`) has to visually represent many different
`EnemyData` at runtime. A boss's own dedicated prototype doesn't have that problem, so `SpawnSprite`
now checks for an `EnemyViewRig` already baked as a real child of `spriteRoot` FIRST - if found, it
skips the `ViewPrefab` resolve/pool step entirely (there's nothing to instantiate or pool, the
GameObject already exists), but the exact same `ResolveFitScale` sprite-bounds math, the same
`Vector3.down * radius` bottom-pivot positioning, and `HasShadow`'s own radius-based auto-scale all
still apply to it - confirmed with the user: a boss's rig should sit at its collider's bottom center
and dynamically track its own radius exactly like a normal enemy's pooled sprite already does, not
stay at a fixed hand-authored scale/position. Only rotation is left untouched (the artist still
controls the rig's own tilt) - a `Resolve Scale` `[Button]` on `EnemyView` re-runs the whole resolve
pass on a live entity in Play Mode for quick iteration after tweaking `viewRadiusPadding`/a
sprite/`Stats.Radius`, without needing to respawn. Requires the baked `EnemyViewRig` to be a DIRECT
child of `spriteRoot` - position/scale are applied to its own transform, not some intermediate
wrapper. `ConnectRig` (handing the rig to
`EnemyBlobAnimationView`/`EnemyArmAimView`/`EnemyAttackVisualsView`/`HitFeedback`) still runs either
way. No other enemy is affected - `spriteRoot` stays empty until `SpawnSprite` populates it for every
prototype that doesn't bake its own rig in.

A new `EnemyFallSystem` (`Assets/_QuantumUser/Simulation/Systems/Enemy/`) gives Boss/Elite-tier
enemies the same "fall off the level → fall damage → respawn to safety" treatment `PlayerFallSystem`
already gives players - confirmed with the user. Every other tier is deliberately excluded (a
disposable Filler/Normal/Specialist/Heavy lost to a fall is a non-issue; a stuck Boss or an Elite
the encounter-hold is still waiting on would actually break things). The nearest-chunk/inset-into-
bounds respawn math was extracted out of `PlayerFallSystem` into a new `FallRespawnUtility` so both
systems share it - Elite calls it directly off its own current (mid-fall) position, since enemies
don't track a "last grounded" position the way `PlayerMovement` does; Boss instead respawns at its
own sealed Boss Arena's `BossSpawnPoints[0]` (`LevelGenerationSystem.ResolveBossSpawnPositions`,
same call `BeginBossEncounter` above uses, ground-corrected the same way too), since the generic
nearest-chunk fallback could strand it outside its own `BossArenaGate`-sealed boundary mid-fight.

## Editor authoring needed

1. **`SurvivalConfig.asset`'s `Phases[]`** - interleave a few entries with `Kind = Breathing`
   (e.g. `Duration = 30`) among the Director's own combat-phase entries
   (`Tools/RiftRaiders/Generate Survival Director Content` authors the combat ones; Breathing
   entries are a manual Inspector step deliberately NOT automated - see
   `BreathingPoiContentGenerator`'s own comment for why: that generator fully replaces `Phases[]`
   each run, so anything inserted by a second script would get silently wiped out next time it
   runs).
2. **`BreathingCountdownWidget`** (View, `Assets/_Project/Scripts/UI/InGame/Hud/`) - "AREA
   SECURED" + "NEXT ASSAULT 00:30" HUD element, needs wiring on the scene HUD prefab. Always
   visible (part of the normal HUD, never hidden by the Cursed Rift Choice Window - see
   `docs/choice-window-refactor.md`), though its `areaSecuredRoot`/`countdownRoot` children stay
   hidden until `Global.BreathingAreaSecured` flips true (see above) - there's currently no
   "clearing area" visual for the window between the phase boundary and that point, the widget
   just shows nothing. As of 2026-08-14 also owns the Skip Vote UI: `skipButton`
   (sends `SkipBreathingCommand` for every one of this client's own local slots at once - shown
   until all of them have voted this Break) and `waitingRoot`/`waitingText` ("WAITING FOR OTHER
   PLAYERS...", shown in the button's place once this client HAS voted but the Break hasn't ended
   yet) - neither has a real GameObject built/assigned in the scene yet. No live "X/Y ready"
   vote-count readout - only a binary voted/not-voted for this client's own local player(s), since
   nothing currently exposes a cross-player count to the View (would need a small addition, e.g. a
   `Global` counter, if that's wanted later).
3. **No `Elite` `SurvivalConfig.Phases[]` entry authored yet** - a designer needs to add one,
   pointing `AllowedGroups` at a group containing only `EnemyTier.Elite` enemies (no such group
   exists yet either), before the encounter-hold mechanic has anything to actually hold on.
   `SurvivalConfig_MVP.asset` does already end on a `Boss` entry (see below).
4. **`DirectorTimelineUiWidget`'s phase icons** - no sprites named `"Breathing"`/`"Boss"`/`"Elite"`
   exist yet in any `SpriteConfigSO` registered on the scene's `SpriteManager` (reuse the existing
   `SpriteConfigCurrency` asset - no new subclass needed), and `markerPrefab` needs a child `Image`
   added and assigned to a `DirectorPhaseMarkerWidget` component's `Icon` field.
5. **Boss phase trigger has nothing authored yet** (see "Boss phase trigger" above): no
   `BossArenaGate`-tagged collider entities exist in `QuantumGameScene.unity` around the Boss
   Arena's own corridor(s); the Boss chunk's own prototype doesn't have a `QPrototypeBossArena`
   added yet, and no `BossTeleportPointMarker`/`BossSpawnPointMarker`s have been placed under it
   either (both fall back to the chunk's plain geometric center until they are, so this isn't
   blocking, just unrefined) - add the component, place the markers, then run
   `BossArenaMarkerBaker.BakeBossArenaMarkers` (`[Button]`, must run on the Scene instance, not an
   isolated prefab - same caveat `ChunkWaypointBaker`/`ChunkRespawnPointBaker` already document);
   and `SurvivalConfig_MVP.asset`'s `Boss` phase's `BossPrototype` is unassigned (no real boss
   `EntityPrototype` exists yet - see the Scrapjaw boss-combat plan) - so no boss actually spawns
   until that's authored by hand either.
6. **Manual end-to-end test not yet run**: confirm Breathing triggers on schedule, enemies clear,
   the Director stops spawning, the countdown UI shows/counts down/returns cleanly to Survival,
   a Level-Up/Chest screen opening mid-Breathing correctly resumes back into `Breathing` (not
   hardcoded to `Survival` - `PreUpgradeState` capture already handles this for free, same as it
   already does for `Lobby`), `SurvivalTime` genuinely holds steady through a Breathing phase (the
   concrete 120/150 example above), a full-Skip-vote actually ends a Break early without disturbing
   `SurvivalTime`, and a Boss/Elite phase genuinely holds `PhaseTimer` until every spawned
   Boss/Elite-tier enemy is dead (including with more than one alive at once). Also new
   (2026-08-17, after user-reported feedback that entering Breathing was still instantly wiping
   every enemy on screen - traced to `RunPhaseUtility.ClearCombatEnemies`, since deleted): confirm
   enemies alive when Breathing begins actually STAY alive and fightable, that killing/outlasting
   them (via the normal `EnemyLifecycle` Irrelevant→Retired timeout) is what flips
   `BreathingAreaSecured`/unfreezes `PhaseTimer`/reveals the HUD banner and POIs, and that a
   Breathing phase beginning with nothing left alive secures on the very next tick (no perceptible
   stall).
