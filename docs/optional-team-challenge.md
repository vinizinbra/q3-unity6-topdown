# Optional Team Challenge

A world-shared, one-shot POI the whole co-op team can voluntarily interrupt Survival to attempt: a
short combat challenge (Kill Rush / Flawless Hunt / Cursed Survival) for a Rift Mutation reward.
Mirrors `docs/traversal-challenge.md`'s own precedent (bespoke world-shared state machine, own
standalone Director-pause counter checked at the same call sites Traversal Challenge's own is,
no `PoiUsagePolicy`/`PoiUsage`, no `PoiInteractionLockUtility` entry) rather than the per-player
Healing Shrine/Cursed Rift/Store/Blacksmith shape, since re-triggerability here is governed by one
shared attempt for the whole team, not a per-player usage count.

Every currently required Raider (connected, not Downed/KO) must press Interact inside the small
Interaction Area to Ready up, then stay inside the larger Ready/Cancel Area
(`TeamChallengeConfig.ReadyCancelRadius`) - leaving it cancels that Raider's own Ready with no
timeout. Survival runs completely normally while waiting. Once every required Raider is
simultaneously Ready, activation locks in (no more cancelling), an optional
`TeamChallengeConfig.CountdownDuration` counts down, then `ChallengeActive` begins: Survival's
timer/spawning pause via `Global.ActiveTeamChallengeCount` (a dedicated counter, same shape as
`Global.ActiveTraversalChallengeCount`, never shared with it), the rolled `ChallengeDefinition`'s
own enemies spawn through the exact same `GroupSpawnerUtility`/`EnemyBalanceUtility`/`BalanceConfig`
co-op scaling pipeline every normal Survival enemy uses, and players keep fighting normally
(`GameplaySystemGroup` is never disabled). On success the POI becomes `RewardAvailable` - Survival
keeps running, any connected Raider may return and Interact to atomically claim the one shared
reward, which reuses `LevelUpUtility.BeginChestScreen(..., LevelUpCategory.RiftMutation)` verbatim
(the exact mechanism a Chest already uses) to pause the party and roll every connected player their
own independent 3-Rift-Mutation choice. On failure the POI becomes permanently `Completed` - one
attempt only, no retries, no reward.

Readying up (and re-checked every tick while `WaitingForTeam`, in case one spawns after everyone
already readied) is blocked while any `EnemyTier.Elite` enemy is alive anywhere in the map - the
encounter is meant to read as its own clean, isolated fight, not one that starts on top of an
ongoing Elite fight. The instant `ChallengeActive` begins, every enemy still alive in the world
(normal Survival spawns, not this challenge's own) is wiped - same death VFX a normal kill plays
(`DamageUtility.FireEnemyExploded`/the Dead-phase corpse linger), but with the entire on-kill
pipeline skipped (no `EntityDied`/`OnEntityKilled`, no XP/Scrap/RiftShard/Coin/Chest drops, no
`MonstersKilled` credit) - this is a map reset the challenge triggers, not a kill a player earned.
The same no-reward-but-still-plays-a-death-effect treatment applies to the challenge's OWN
leftover spawned enemies when the attempt ends (`TeamChallengeUtility.KillWithoutRewards`, called
from both `BeginChallengeActive`'s wipe and `DestroyChallengeSpawns`) - they used to be
`f.Destroy`'d outright with no visual at all.

## Why this design, not something else

- **No `PoiUsagePolicy`/`PoiUsage`, no `PoiActivationSystem`/`PoiViewState`.** Same reasoning
  Traversal Challenge's own doc gives: this is a world-shared, one-shot attempt, not a per-player
  repeatable interaction - `TeamChallengeState` alone (not per-player usage counts) governs
  re-triggerability, and `TeamChallengeView` reads that enum directly instead of `PoiActivation`.
  Since this POI never carries a `PoiActivation` component at all, `TeamChallengeView` extends
  `InteractionPromptPoiView` directly rather than the generic `PoiView` (which owns the
  `PoiActivation`-driven Inactive/Active/Expired visuals - dead weight here) - see
  `InteractionPromptPoiView`'s own header and the "reward preview" bullet below.
- **A dedicated `Global.ActiveTeamChallengeCount`, not a reuse of `ActiveTraversalChallengeCount`.**
  The two pauses are conceptually unrelated triggers - ending one must never affect the other's own
  freeze. Checked at the exact same call sites `ActiveTraversalChallengeCount` already is
  (`SurvivalProgressionUtility.Tick`'s two clock advances, `RunPhaseUtility.TickBreathingGraceHold`,
  `CombatDirectorSystem.Update`'s spawn gate), incremented only once `ChallengeActive` truly begins
  (not at `Starting`) - Survival keeps running normally through the countdown, per spec.
- **No co-op scaling of its own.** `ChallengeDefinition.SpawnGroups` references the existing
  `EnemyGroupConfig` asset type directly and spawns via `GroupSpawnerUtility.TrySpawnGroup` - the
  exact same call every normal Survival encounter uses, which already resolves final enemy stats
  through `EnemyBalanceUtility.ResolveEnemyStats`/`BalanceConfig`. Zero direct `BalanceConfig` calls
  anywhere in this feature.
- **`TeamChallengeReady` (presence-based, per-player) vs. `TeamChallengeParticipant` (locked-in
  snapshot of who actually started the attempt).** Two separate components rather than one, because
  Ready state must remain cancellable up until the unanimous vote locks in, while Flawless Hunt's
  real-HP-loss check and Cursed Survival's curse both need a STABLE "who is actually in this
  attempt" set that survives a later Ready/Cancel Area departure (which no longer applies once
  `ChallengeActive` begins - nobody's input is ever locked).
- **Kill Rush/Flawless Hunt/Cursed Survival share ONE runtime** (`TeamChallengeSystem` +
  `ChallengeObjectiveUtility` + `TeamChallengeReactionSystem`), not three separate systems - only
  the success/failure hook differs per `ChallengeType` (timer vs. signal-driven), everything else
  (state machine, spawn call, cleanup, reward hookup) is identical.
- **Cursed Survival's 1 HP pin/Accessory-disable is a NEW, small, reversible snapshot
  (`CursedSurvivalOverride`)**, not `AccessoryGuardUtility.Disable()` (a one-way, destructive call
  that also zeroes durability and sets `Broken`) - the curse only ever flips
  `AccessoryGuard.Disabled` (which `TryBlock` already hard-gates on), restored from the immutable
  snapshot (never the live pinned value) on every terminal exit, so a mid-curse heal or Accessory
  event can never leave a player better off than before the curse began. The 1 HP ceiling itself is
  enforced structurally in `HealUtility.ApplyFlatHeal` (the single funnel every heal source goes
  through), not by each heal source knowing about the curse.
- **The reward claim reuses `LevelUpUtility.BeginChestScreen` verbatim.** A Chest already does
  exactly "one interaction triggers an independent, per-player-rolled 3-card `ChooseWindow` for
  every connected player, pausing the whole party" - zero new roll/UI code needed. Atomicity (first
  claim wins) is just a state guard (`if State != RewardAvailable return; State = Completed;`) -
  Quantum ticks single-threaded/deterministically and every interaction utility re-resolves state
  fresh rather than trusting a same-tick cached value, so a same-tick double-claim attempt can only
  ever land once.
- **The "X / Y READY" per-player readout lives on `InteractionPromptWidget`'s own new "challenge
  area"** (a fixed pool of `ChallengeReadyIconWidget` slots, shown/hidden by the live connected-
  player count, each toggled Ready/idle independently) rather than a bespoke world-space counter -
  that widget already exists per-POI (spawned by `TeamChallengeView`'s own inherited
  `InteractionPromptPoiView.Initialize` off the sibling `Interactable`), so this is additive to
  infrastructure every POI already has. The physical Ready/Cancel Area **boundary ring** stays on
  `TeamChallengeView` instead (a genuinely different concern - a place to stand, not a readout).
- **`TeamChallengeView` also pushes a per-`TeamChallengeState` description override onto its own
  `InteractionPromptWidget`** (`SetDescriptionOverride`, e.g. "STARTING...", "CHALLENGE IN
  PROGRESS", "CLAIM YOUR REWARD", "CHALLENGE COMPLETE", "CHALLENGE FAILED") - needed because
  `TeamChallengeUtility.ResolveInteractionState` collapses Starting/ChallengeActive/Completed/
  Failed all into the single generic `AlreadyUsed` `ContextInteractionState` bucket (and
  `RewardAvailable` into `Available`), so the plain per-state `promptAlreadyUsedDescription`/
  `promptActiveDescription` text alone can't tell them apart. The override takes priority over
  that generic text whenever non-empty (same "second source can override the plain per-state text"
  idiom `InteractionPromptWidget` already uses for Revive's live bleed-out countdown), and is
  cleared (empty string) for Available/WaitingForTeam, which the rolled `Description`/rules/ready
  area already cover well.
- **The per-type rules breakdown (icon+text rows) lives on `ChallengeDefinition` itself, resolved
  dynamically off the live `TeamChallenge.SelectedChallenge` roll** - not a static field on the POI
  prefab - because the actual objective varies per attempt (Kill Rush/Flawless Hunt/Cursed Survival
  are randomly rolled, see `TeamChallengeUtility.TryReadyUp`), the same reason
  `InteractionPromptWidget.ResolveActiveDescription` already reads `ChallengeDefinition.Description`
  live instead of a fixed `PoiView` string. A NEW `InteractionPromptWidget` "rules area" (a fixed
  pool of `IconTextRowWidget` slots, same shape as the Ready-icon pool above) shows one row per
  `ChallengeDefinition.Rules` entry, Available/WaitingForTeam only.
- **The reward preview (one icon+text row, e.g. "RIFT MUTATION") is a plain, constant
  `InteractionPromptPoiView` field (`promptRewardIcon`/`promptRewardText`)**, threaded through
  `Setup` exactly like `promptTitle` already is - not pulled from `TeamChallengeConfig` - because
  it's authored once per POI *instance* in the Inspector, identically to every other prompt field
  that shared base already owns, and (unlike the rules above) never varies by which
  `ChallengeDefinition` gets rolled: every Optional Team Challenge type shares the exact same claim
  mechanism/reward (`LevelUpUtility.BeginChestScreen(..., LevelUpCategory.RiftMutation)`). The same
  two fields (generic on `InteractionPromptPoiView`, inherited by both `PoiView` and
  `TeamChallengeView`/`TraversalChallengeView`) are reused verbatim by Traversal Challenge's own
  activator for its own reward preview - see `docs/traversal-challenge.md`.

## File map

- `Assets/_QuantumUser/Simulation/QTN/Poi/TeamChallenge.qtn` - `ChallengeType`,
  `TeamChallengeState`, `TeamChallenge` component, `Global.ActiveTeamChallengeCount`/
  `TeamChallengeTimeRemaining`, `TeamChallengeReady`/`TeamChallengeParticipant`/
  `CursedSurvivalOverride`/`TeamChallengeSpawn` per-entity components.
- `Assets/_QuantumUser/Simulation/QTN/Poi/ContextInteraction.qtn` - new `InteractableKind.
  TeamChallenge` value.
- `Assets/_QuantumUser/Simulation/QTN/GameState.qtn` - originally a new `HudBannerKind.TeamChallenge`
  value; folded into `GameState.TeamChallenge` directly as part of the later Announcer/event-driven-
  HUD pass (see `docs/game-state.md`).
- `Assets/_QuantumUser/Simulation/QTN/Events.qtn` - `TeamChallengeActivated`/`Started`/`Completed`/
  `Failed`.
- `Assets/_QuantumUser/Simulation/Default/RuntimeConfig.User.cs` - new
  `Bots.DisableAutoTeamChallengeReady` opt-out.
- `Assets/_QuantumUser/Simulation/Assets/Poi/ChallengeDefinition.cs` - new asset: `Type`,
  `DisplayName`, `Duration`, `KillTarget`, `SpawnGroups[]` (references `EnemyGroupConfig`).
- `Assets/_QuantumUser/Simulation/Assets/Poi/ChallengeDefinition.View.cs` - new `partial` split:
  `Rules[]` (`ChallengeRuleEntry{Sprite Icon, string Text}`), the per-type rules breakdown shown in
  `InteractionPromptWidget`'s new rules area (same `.View.cs` convention `CharacterData`/
  `PassiveData`/`SkillData` already use for their own Sprite-carrying additions).
- `Assets/_QuantumUser/Simulation/Assets/Poi/TeamChallengeConfig.cs` - new asset:
  `ChallengePool[]`, `ReadyCancelRadius`, `CountdownDuration`.
- `Assets/_QuantumUser/Simulation/Systems/Poi/TeamChallengeUtility.cs` - interaction/state-machine
  logic (ready-up, unanimity, start, spawn, end, atomic reward claim, bot auto-ready, live HUD
  recount helpers).
- `Assets/_QuantumUser/Simulation/Systems/Poi/ChallengeObjectiveUtility.cs` - generic timeout rule
  + Cursed Survival's own timer/incapacitation poll.
- `Assets/_QuantumUser/Simulation/Systems/Poi/CursedSurvivalUtility.cs` - the reversible 1 HP/
  Accessory-disable snapshot and restore.
- `Assets/_QuantumUser/Simulation/Systems/Poi/TeamChallengeSystem.cs` - per-tick state ticking
  (WaitingForTeam/Starting/ChallengeActive), lives inside `GameplaySystemGroup`.
- `Assets/_QuantumUser/Simulation/Systems/Poi/TeamChallengeReactionSystem.cs` - Kill Rush/Flawless
  Hunt's kill counter and Flawless Hunt's real-HP-loss failure (`ISignalOnEntityKilled`/
  `ISignalOnHealthDamageApplied`).
- `Assets/_QuantumUser/Simulation/Systems/Poi/ContextInteractionSystem.cs` - new
  `InteractableKind.TeamChallenge` dispatch case.
- `Assets/_QuantumUser/Simulation/Systems/Player/SkillSystem.cs` - new `TeamChallengeUtility.
  TryInteract` dispatch case.
- `Assets/_QuantumUser/Simulation/Systems/Director/SurvivalProgressionUtility.cs` - `SurvivalTime`/
  `PhaseTimer` advances also gated on `ActiveTeamChallengeCount <= 0`.
- `Assets/_QuantumUser/Simulation/Systems/Director/RunPhaseUtility.cs` - `TickBreathingGraceHold`
  also holds while `ActiveTeamChallengeCount > 0`.
- `Assets/_QuantumUser/Simulation/Systems/Director/CombatDirectorSystem.cs` - `Update`'s spawn gate
  + `ApplyEffectiveState`'s (was `ApplyHudBanner`) `TeamChallenge` precedence (via
  `TeamChallengeUtility.AnyBannerActive`, since the countdown itself precedes the pause counter
  incrementing).
- `Assets/_QuantumUser/Simulation/Systems/Combat/HealUtility.cs` - `ApplyFlatHeal`'s 1 HP clamp for
  a `CursedSurvivalOverride` holder.
- `Assets/_QuantumUser/Simulation/Default/SystemSetup.User.cs` - registers
  `TeamChallengeReactionSystem`/`TeamChallengeSystem`.
- `Assets/_QuantumUser/View/Entities/Poi/TeamChallengeView.cs` - state-driven visuals + Ready/Cancel
  Area boundary ring; extends `InteractionPromptPoiView` directly (no sibling `PoiView` needed on
  this prefab - see that class's own header) and pushes a per-`TeamChallengeState` description
  override onto its own `InteractionPromptWidget` via `SetDescriptionOverride`.
- `Assets/_QuantumUser/View/Entities/Poi/InteractionPromptPoiView.cs` - shared base extracted out of
  `PoiView`, owning the world-space Base-Skill prompt (title/per-state description/reward preview,
  spawn/despawn off the sibling `Interactable`) with no dependency on `PoiActivation` - `PoiView`
  now extends this and adds only its own `PoiActivation`-driven Inactive/Active/Expired visuals;
  `TeamChallengeView`/`TraversalChallengeView` extend it directly instead, since neither ever
  carries a `PoiActivation` component and inheriting `PoiView` itself would mean dragging in those
  dead fields (previously a real, confusing duplicate: both `PoiView` and `TeamChallengeView`
  separately declared their own "Active Visual" field on the same prefab).
- `Assets/_Project/Scripts/UI/InGame/Hud/InteractionPromptWidget.cs` - new "challenge area" (fixed
  `ChallengeReadyIconWidget[]` pool), shown only for a `TeamChallenge` POI mid-`WaitingForTeam`; a
  second new "rules area" (fixed `IconTextRowWidget[]` pool, `RefreshRulesArea`) shown Available/
  WaitingForTeam off the live `ChallengeDefinition.Rules`; a new constant reward row
  (`rewardArea`/`rewardRow`, `ApplyReward`), set once in `Setup` from `InteractionPromptPoiView`'s
  own `promptRewardIcon`/`promptRewardText`; and `SetDescriptionOverride`, letting an owning View
  push a live, state-specific description that outranks the generic per-`ContextInteractionState`
  text.
- `Assets/_Project/Scripts/UI/InGame/Hud/ChallengeReadyIconWidget.cs` - one Ready/idle indicator
  slot.
- `Assets/_Project/Scripts/UI/InGame/Hud/IconTextRowWidget.cs` - new generic (Sprite, string) row,
  backing both the rules area's pooled rows and the single reward row above.
- `Assets/_QuantumUser/View/Entities/Poi/PoiView.cs` - `promptRewardIcon`/`promptRewardText` fields
  (now on the shared `InteractionPromptPoiView` base it extends), threaded through
  `InteractionPromptWidgetManager.SpawnWidget`/`InteractionPromptWidget.Setup` alongside the
  existing `promptTitle`/description fields.
- `Assets/_Project/Scripts/UI/InGame/Hud/TeamChallengeWidget.cs` - global HUD banner (objective
  readout during Starting/ChallengeActive), the activation toasts, and the 3 big animated
  "CHALLENGE STARTED/COMPLETE/FAILED" announcements (via `AnnouncerManager`, see
  `docs/announcer.md` - originally a dedicated `AnnouncementBannerWidget` extracted from
  `BreathingWidget`'s own "AREA SECURED" tween shape, later generalized into the shared
  `AnnouncerManager` once "AREA SECURED"/"SURVIVAL MODE STARTED" turned out to be the exact same
  copy-pasted shape too).
- `Assets/_Project/Scripts/UI/InGame/RunResultManager.cs` - `ResolvePlayerLabel` promoted to
  `internal` so `TeamChallengeWidget` can reuse the same hero-name resolution for its own toast.
- `Assets/_QuantumUser/Editor/TeamChallengeAssetGenerator.cs` - new Editor tool
  (`Tools/RiftRaiders/Poi/Generate Team Challenge Assets`) that scaffolds the 3
  `ChallengeDefinition` assets + 1 `TeamChallengeConfig` asset with decisive placeholder values
  (`SpawnGroups` deliberately left empty - assign by hand). Mirrors
  `RiftMutationAssetGenerator.cs`'s create-or-update shape.

## Current status

Code-complete; nothing is authored yet, so nothing spawns at runtime until the following is done in
the Editor (same "code compiles once Quantum's `.qtn` codegen runs" caveat every other feature in
this index carries - see the root `CLAUDE.md`'s codegen gotcha):

1. Run `Tools/RiftRaiders/Poi/Generate Team Challenge Assets` to scaffold one `TeamChallengeConfig`
   asset and the 3 `ChallengeDefinition` assets under `Assets/_QuantumUser/Resources/Poi/
   TeamChallenge/` with decisive placeholder values, then assign each `ChallengeDefinition.
   SpawnGroups[]` by hand (left empty by the generator) and re-tune `TeamChallengeConfig.
   ReadyCancelRadius`/`CountdownDuration` once the POI's own `Interactable.Radius` is authored.
   Each `ChallengeDefinition.Rules[]` row also has placeholder Text already seeded - assign each
   row's `Icon` sprite by hand (left null by the generator, same reasoning as `SpawnGroups`).
2. Build a POI prefab carrying `Interactable{Kind=TeamChallenge, Radius=<small>}`,
   `QPrototypeTeamChallenge{Availability, Config, ...}`, and `TeamChallengeView` alone - **no
   sibling `PoiView` component**, `TeamChallengeView` now extends `InteractionPromptPoiView`
   directly and supplies the Base-Skill prompt itself (see the file map above). Assign
   `availableVisual`/`waitingVisual`/`activeVisual`/`rewardVisual`/`completedVisual`/`readyRing`,
   the inherited `promptTitle`/description fields, `promptRewardIcon`/`promptRewardText` (e.g. a
   Rift Mutation icon + "RIFT MUTATION") for the reward preview row, and optionally the 5 per-state
   `...PromptDescription` overrides (defaults are already sensible). If an existing prefab still has
   a leftover sibling `PoiView`, remove it - it no longer does anything useful here and its own
   dead "Active Visual" field only reads as a confusing duplicate of `TeamChallengeView`'s real one.
3. Extend the shared `InteractionPromptWidget` prefab (`InteractionPromptWidgetManager`'s pooled
   widget):
   - a `challengeArea` GameObject holding N `ChallengeReadyIconWidget` slots (N = the game's max
     party size), each with its own `readyVisual`/`idleVisual` children, assigned to both fields.
   - a `rulesArea` GameObject holding M `IconTextRowWidget` slots (M = the most rows any one
     `ChallengeDefinition.Rules` authors), assigned to `ruleRows`.
   - a `rewardArea` GameObject holding one `IconTextRowWidget`, assigned to `rewardRow`.
4. Wire a `TeamChallengeWidget` scene instance under the HUD (`GameplayWindow`, alongside
   `TraversalChallengeWidget`/`BreathingWidget`) with `root`/`titleText`/`objectiveText`
   assigned, plus an `AnnouncementBannerWidget` instance (`root`/`canvasGroup`/`text` assigned,
   same rect/canvas-group shape `BreathingWidget`'s own "AREA SECURED" banner uses) wired
   into its `announcementBanner` field.
5. Place the POI instance in a level chunk and verify end-to-end in-Editor, solo and co-op (see the
   verification checklist in the implementation plan / PR description).

Not yet manually verified end-to-end in-Editor.

## Encounter density tuning

The challenge encounter is paced by `CombatDirectorUtility.TryPulse`, so refill speed is set by
three `ChallengeDefinition` fields, not by any challenge-specific code:

- `PulseInterval` - the worst-case wait before the next purchase after a kill. It was 3-4s, which
  read as "kill everything, then stand around". Now **0.5s** on all three; `BudgetPerPulse` was
  scaled down to match (~12-16 budget/s), so supply still comfortably exceeds the kill rate.
- `MaxAliveEnemies` vs. group size - `SwarmRush` is 8 enemies, so a cap of 10-15 only ever fit ONE
  group at a time and it emptied completely before the next could be bought. Caps are now 32 (Kill
  Rush) / 22 (Flawless Hunt) / 18 (Cursed Survival).
- `AllowedEnemies` - each definition now also lists a lone `Swarm` (cost 1), so the Director can
  top up one enemy at a time instead of waiting to afford a whole group.
- **Enemy mix** - every definition now mixes swarm and ranged: groups `SwarmRush`,
  `RangedSkirmish` (3 Gunner + Sniper) and `C1DoubleGunner`, plus a Turret+Swarm pack
  (`I2-R4B-SwarmTurretPack` for Kill Rush/Cursed, `TurretSwarmPack` for Flawless Hunt), and lone
  `Swarm` + `Gunner` in `AllowedEnemies`. Kill Rush keeps `C1MeleePair`; Cursed Survival keeps its
  original `C1Gunline`/`C1MeleePair`/`FullAssault`. Group weights live on the group assets, so
  shifting the ratio per challenge means listing a group twice (or editing the `AllowedEnemies` weight).

Kill targets were raised (Kill Rush 20 -> 40 in 30s, Flawless Hunt 15 -> 30). Numbers are decisive
placeholders - re-tune after a played run.

Follow-up: the enemy-mix pass above (more ranged/swarm groups + lone Swarm/Gunner top-ups) pushed
this too far the other way - overpopulated. Pulled `TargetPressure`/`MaxAliveEnemies`/
`BudgetPerPulse` back down without giving up the fast `PulseInterval` (still ~0.6s, up slightly
from 0.5s to space purchases out a little): Kill Rush 36->24 pressure / 32->22 cap, Flawless Hunt
22->16 pressure / 22->16 cap, Cursed Survival 36->24 pressure / 18->13 cap. Kill targets/timers
unchanged. Re-tune again after a played run - this is still a placeholder guess, not measured. Rule rows with `ScaleTextWithKillTarget` are a
`string.Format` template and must use `{0}` (they previously held a hard-coded number, so the
prompt never reflected the real co-op-scaled target).

## Known simplifications

- All of a `ChallengeDefinition`'s `SpawnGroups` spawn together, all at once, at `ChallengeActive`
  start - not staggered into timed waves. Multiple short waves (mentioned as a nice-to-have for
  Kill Rush) can be approximated today by authoring several `EnemyGroupConfig` entries, but there is
  no built-in stagger/interval mechanism.
- The Ready/Cancel unanimity check and Cursed Survival's own incapacitation check both use
  `PlayerLifeStateUtility.IsIncapacitated` polling every tick - there is no "just entered Downed"
  signal in this codebase, so a poll (same idiom `SurvivalProgressionUtility`/
  `TraversalChallengeSystem` already use for their own live checks) was used instead of adding one.
- The `GameState.TeamChallenge` overlay (originally `HudBannerKind.TeamChallenge`) is resolved via a
  live `f.Filter<TeamChallenge>()` scan every tick (`TeamChallengeUtility.AnyBannerActive`) rather
  than a cheap Global bool, since the banner needs to show during `Starting` even though
  `ActiveTeamChallengeCount` itself only increments at `ChallengeActive` - acceptable given at most a
  handful of these POIs are expected to ever exist in a level.
- `ChallengeDefinition.Rules[]` rows have no per-row layout beyond a plain vertical list (`ruleRows`
  pool, filled top-down) - no reordering/animation between challenge rolls, and a `Rules` array
  longer than the authored `ruleRows` pool simply truncates (extra rows never show, nothing warns
  about it). Size the pool to the longest authored `Rules[]` with headroom.
- The reward preview (`InteractionPromptPoiView.promptRewardIcon`/`promptRewardText`) is a single static row with no
  live state of its own - it doesn't change across `TeamChallengeState`, so it stays visible
  (e.g. "RIFT MUTATION") even after `RewardAvailable`/`Completed`, when it's arguably no longer
  useful information. Acceptable for now since it's purely informational and never wrong, just
  redundant once the reward's actually been claimed.
- If more than one Optional Team Challenge POI is ever `Starting`/`ChallengeActive` at once,
  `TeamChallengeWidget`'s single HUD banner only reflects whichever one the scan finds first - same
  accepted simplification `TraversalChallengeWidget`'s own multi-instance countdown documents.
