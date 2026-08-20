# Survival Director

Continuous-spawn combat pacing system for the Quantum simulation. Replaces "no enemy spawner exists yet" - the project's per-enemy AI (`Enemy`/`EnemySystem`/`BossSystem`) already existed before this; this system decides *when*, *where*, and *what group* to spawn, and when a spawned enemy should disappear again.

Design follows four deliberately small, composable domains rather than one large "smart" director. Each domain has exactly one job and never reaches into the next domain's decisions:

1. **Survival Progression** - owns the survival timer, the current phase, and that phase's budget/pressure/cap/allowed-groups. Never spawns anything itself.
2. **Combat Director** - every pulse: read the phase, add budget, measure current relevant pressure, stop if already at target, otherwise pick one authored `EnemyGroupConfig` (deterministic weighted roll among unlocked/affordable/uncapped candidates) and ask the Group Spawner to place it near a predicted "moving combat bubble," repeat until the purchase limit. Decides *when*, *what*, and *whether affordable* - never *where*.
3. **Group Spawner** - a stateless helper (`GroupSpawnerUtility`), not its own System. Decides *where* a Director-selected group can spawn safely: finds a ring anchor with ground under it, generates a deterministic formation around that anchor, validates every member (ground + per-profile height rule + clearance), and only creates entities once the *entire* formation is confirmed valid. Never chooses which group, when, or whether it's affordable.
4. **Enemy Lifecycle** - a purchased enemy is `Active` while relevant (close/attacking/recently hit/elite/persistent), goes `Irrelevant` once none of those hold, and `Retired` (destroyed, partial refund) after sitting `Irrelevant` past `RetireDelay`. Persistent enemies never leave `Active`.

## Runtime flow

```
SurvivalProgressionUtility.Tick
  -> advances SurvivalTime/PhaseTimer, returns the SurvivalPhase in effect this tick

CombatDirectorUtility.TryPulse (Domain 2)
  -> add phase.BudgetPerPulse to DirectorBudget
  -> compute predicted combat center (team center + average velocity * PredictionTime)
  -> loop (bounded by DirectorConfig.MaxPurchasesPerPulse):
       measure relevant pressure -> stop if >= phase.TargetPressure
       TrySelectGroup            -> stop if no valid/affordable/unlocked group
       GroupSpawnerUtility.TrySpawnGroup (Domain 3) -> stop if it can't find a safe formation
       only on spawner success: DirectorBudget -= group.ComputeCost(f)

GroupSpawnerUtility.TrySpawnGroup (Domain 3)
  -> for each of MaxGroupSpawnAttempts ring anchors:
       find ground under the anchor, else try another anchor
       generate every member's offset via GroupFormationUtility (SpawnPattern)
       validate every member (ground, height rule, clearance) - one failure discards the whole anchor
       on full success: create every entity, add EnemyLifecycle, return success

EnemyLifecycleSystem (Domain 4, every tick, every entity with EnemyLifecycle)
  -> relevance check -> Active/Irrelevant/Retired state advance
  -> on Retired: refund LifecycleConfig.RefundFraction * EnemyTierStatsConfig.Get(data.Tier).Cost into DirectorBudget, destroy
```

## Files

**Data (`Assets/_QuantumUser/Simulation/Assets/Director/`)**
- `SurvivalConfig.cs` - `SurvivalPhase[] Phases`. Each phase: `Duration`, `BudgetPerPulse`, `PulseInterval`, `TargetPressure`, `MaxAliveEnemies`, `AllowedGroups`. Last phase never expires (loops forever once reached).
- `EnemyGroupConfig.cs` - `Members[]` (`EnemyData` + `Quantity`, no authored offsets), `Weight`, `MinimumSurvivalTime`/`MaximumSurvivalTime`, `MaxConcurrent`, `SpawnPattern`, `FormationRadius`, `AllowsPartialSpawn` (reserved, unused). `Cost` is **not** a field - see `ComputeCost(f)`.
- `EnemySpawnProfile.cs` - Domain 3 per-enemy-type placement rules: `SpawnCategory`, `MinimumHeightDifference`/`MaximumHeightDifference`, `ClearanceRadius`/`ClearanceHeight`. Referenced by `EnemyDataAsset.SpawnProfile`.
- `DirectorConfig.cs` - `EnemyPrototype` (the one shared generic prototype every Director purchase is created from), `PredictionTime`, `SpawnRingRadiusMin/Max`, `MaxPurchasesPerPulse`, `MaxGroupSpawnAttempts`.
- `LifecycleConfig.cs` - `RelevantRange`, `RecentCombatWindow`, `RetireDelay`, `RefundFraction`.

**Simulation (`Assets/_QuantumUser/Simulation/QTN/Director/SurvivalDirector.qtn`, `.../QTN/Enemy/EnemyLifecycle.qtn`)** - unchanged by this pass, no `.qtn` edits were needed (see "Why no codegen was needed" below).
- Global fields: `SurvivalTime`, `CurrentPhaseIndex`, `PhaseTimer`, `DirectorBudget`, `DirectorPulseTimer`.
- `EnemyLifecycle` component (opt-in, added dynamically at spawn - never hand-authored on a prototype): `State` (`Active`/`Irrelevant`/`Retired`), `IrrelevantTimer`, `RecentCombatTimer`, `LastObservedHealth`, `SourceGroup`.

**Systems (`Assets/_QuantumUser/Simulation/Systems/Director/`)**
- `CombatDirectorSystem` - runs right after `LevelGenerationSystem`, before everything else. Calls `SurvivalProgressionUtility.Tick` then `CombatDirectorUtility.TryPulse` each frame.
- `SurvivalProgressionUtility` - advances the phase clock.
- `CombatDirectorUtility` - the pulse/purchase loop, predicted-combat-center math, deterministic weighted group selection. Delegates all placement to `GroupSpawnerUtility`.
- `GroupSpawnerUtility` **(new)** - Domain 3: ring anchor search, per-member formation validation (ground/height/clearance), transactional entity creation. See "Physics-based group spawning algorithm" below.
- `GroupFormationUtility` **(new)** - turns `(SpawnPattern, index, count, FormationRadius)` into a deterministic local offset. Implements Cluster/Arc/Line/Scatter/Circle.
- `EnemyLifecycleSystem` - runs right before `DestroyAfterTimeSystem`, after every hit-resolving system. Relevance check, state advance, retirement + refund.

**Edited existing files:**
- `EnemyDataAsset.cs` - `Persistent` (bool, never auto-retires), and **new**: `SpawnProfile` (`AssetRef<EnemySpawnProfile>`). `Cost` is no longer a field here - it's tier-driven, see `EnemyTierStatsConfig` below.
- `EnemySystem.cs` - **new**: public `SeedFromEnemyData(f, entity, data)`, factored out of `OnEntityPrototypeMaterialized` so `GroupSpawnerUtility` can re-run the same Health/Shield/Radius seeding after it sets `Enemy->EnemyData` post-`Create` (see "Why enemies are spawned off one generic prototype" below).
- `EnemyMovementUtility.cs` - **new**: `GetObstacleLayerMask`, used by `GroupSpawnerUtility`'s clearance check.
- `Default/RuntimeConfig.User.cs` - `AssetRef<SurvivalConfig>`, `AssetRef<DirectorConfig>`, `AssetRef<LifecycleConfig>`.
- `Default/SystemSetup.User.cs` - registered `CombatDirectorSystem` and `EnemyLifecycleSystem` at the positions described above.

## Why enemies are spawned off one generic prototype

Every hand-authored enemy prefab in this project (`Enemy.prefab`, `BasicEnemy.prefab`) is the **same** generic `EntityPrototype` shell - `Enemy`, `Health`, `Shield`, `PhysicsCollider3D` with no size/stats of its own. A specific enemy *type* only exists once `Enemy.EnemyData` points at a real `EnemyDataAsset`; `EnemySystem.OnEntityPrototypeMaterialized` then seeds Health/Shield/collider radius from that asset. Hand-placed enemies bake their `EnemyData` directly onto the prototype instance in the Inspector.

The Director can't do that per member and still get automatic group cost: if every `GroupMemberEntry` carried its own baked `AssetRef<EntityPrototype>`, there would be no cheap way to read that prototype's `Cost` back out without materializing an entity (Quantum's `EntityPrototype` doesn't expose a plain "read this component's default value" API worth relying on). So `GroupMemberEntry` references `EnemyDataAsset` directly - the same single source of truth pressure/refund already use - and `DirectorConfig.EnemyPrototype` holds one shared blank prototype (assign it to `BasicEnemy`'s baked `EntityPrototype` asset in the Editor).

The one wrinkle: `OnEntityPrototypeMaterialized` fires synchronously inside `f.Create`, before `GroupSpawnerUtility.SpawnMember` gets a chance to set `Enemy->EnemyData`. It runs against an empty ref and does nothing (this is expected, not an error - see the comment on that signal). `SpawnMember` calls the new `EnemySystem.SeedFromEnemyData` immediately after setting `EnemyData`, which re-runs the same seeding manually. The end result is identical to an entity that had `EnemyData` baked in from the start.

## Why no codegen was needed

This pass only added/changed plain C# `AssetObject` fields (`EnemyGroupConfig`, `EnemySpawnProfile`, `EnemyDataAsset`, `DirectorConfig`) and static utility code. None of it touches a `.qtn`-declared component or global, so Quantum's DSL codegen (see the project `CLAUDE.md`'s "Quantum codegen gotcha" section) never needed to run for this change. `EnemyLifecycle.SourceGroup` is `AssetRef<EnemyGroupConfig>` - a generic reference type independent of `EnemyGroupConfig`'s own fields - so it didn't need regenerating either.

## AssetObject definitions

**`SurvivalConfig`** (Domain 1) - `SurvivalPhase[] Phases`, each `{ Duration, BudgetPerPulse, PulseInterval, TargetPressure, MaxAliveEnemies, AllowedGroups }`. Unchanged this pass.

**`DirectorConfig`** (Domain 2/3 shared) - `EnemyPrototype`, `PredictionTime`, `SpawnRingRadiusMin/Max`, `MaxPurchasesPerPulse`, `MaxGroupSpawnAttempts` (renamed from `MaxSpawnPlacementAttempts` - same role, now explicitly "attempts at picking a **group** anchor", since a single anchor now validates a whole formation, not one enemy).

**`LifecycleConfig`** (Domain 4) - `RelevantRange`, `RecentCombatWindow`, `RetireDelay`, `RefundFraction`. Unchanged this pass.

**`EnemyGroupConfig`** (Domain 2/3 shared - encounter composition) - `Members[]` (`{ EnemyData, Quantity }`), `Weight`, `MinimumSurvivalTime`/`MaximumSurvivalTime`, `MaxConcurrent`, `SpawnPattern`, `FormationRadius`, `AllowsPartialSpawn` (reserved). `ComputeCost(f)` sums `EnemyTierStatsConfig.Get(member's Tier).Cost * Quantity` across members - **no authored `Cost` field** on either the group or `EnemyDataAsset`, so a balance change to one tier's `Cost` in `EnemyTierStatsConfig` instantly updates every group/enemy using that tier, satisfying "one source of truth" without needing to instantiate anything to check affordability (summing authored FP values is cheap; the earlier design's "don't auto-derive, it'd mean instantiating every candidate" concern doesn't apply once members reference data assets directly instead of baked prototypes).

**`EnemySpawnProfile`** (Domain 3, new) - `SpawnCategory` (`GroundMelee`/`GroundRanged`/`HighGroundRanged`/`Flying`/`Boss`), `MinimumHeightDifference`/`MaximumHeightDifference` (only enforced for the two Ground categories), `ClearanceRadius`/`ClearanceHeight`. Deliberately minimal - see "Deliberate simplifications" for what's intentionally not on it yet.

**`EnemyDataAsset`** (existing, extended) - added `SpawnProfile` (`AssetRef<EnemySpawnProfile>`). Every `EnemyDataAsset` referenced by a `GroupMemberEntry` must have one, or `GroupSpawnerUtility` rejects the whole group with a clear error and spends no budget.

## ECS component definitions

Unchanged this pass - no `.qtn` edits (see "Why no codegen was needed").

- Globals (`SurvivalDirector.qtn`): `SurvivalTime`, `CurrentPhaseIndex`, `PhaseTimer`, `DirectorBudget`, `DirectorPulseTimer`.
- `EnemyLifecycle` component (`EnemyLifecycle.qtn`): `State`, `IrrelevantTimer`, `RecentCombatTimer`, `LastObservedHealth`, `SourceGroup`. Opt-in, added by `GroupSpawnerUtility.SpawnMember` at spawn time - never hand-authored on a prototype.

## System responsibilities and execution order

1. **`LevelGenerationSystem`** (unrelated to the Director, runs first for the same "world setup first" reason).
2. **`CombatDirectorSystem`** (Domains 1+2, merged into one System - see its own comment for why two Systems would add nothing). `SurvivalProgressionUtility.Tick` then `CombatDirectorUtility.TryPulse`, which internally calls into `GroupSpawnerUtility` (Domain 3, a static helper, not its own System - the design doc's own guidance: "Choose the simplest architecture compatible with Photon Quantum," and Domain 3 has no per-tick state of its own to justify a System).
3. *(every other gameplay System - `EnemySystem`, `AimSystem`, damage-resolving Systems, etc.)*
4. **`EnemyLifecycleSystem`** (Domain 4) - placed right before `DestroyAfterTimeSystem`, after every hit-resolving System, so a same-tick combat death is correctly excluded from retirement bookkeeping (not one-tick-stale), and `DestroyAfterTimeSystem`'s "must run last" invariant still holds since this System also calls `f.Destroy`.

## Director pulse algorithm (`CombatDirectorUtility.TryPulse`)

1. Tick `DirectorPulseTimer` down by `DeltaTime`; return early if it hasn't reached `phase.PulseInterval` yet.
2. Reset the timer, add `phase.BudgetPerPulse` to `DirectorBudget`.
3. Compute the predicted combat center (team center + average velocity * `PredictionTime`); skip the whole pulse if no players exist yet.
4. Loop up to `MaxPurchasesPerPulse` times:
   a. Measure relevant pressure (sum of `Cost` over every `EnemyLifecycle.State == Active` entity) - stop if `>= phase.TargetPressure`.
   b. `TrySelectGroup` - stop if nothing valid (see "Group validation algorithm").
   c. `GroupSpawnerUtility.TrySpawnGroup` - stop if it can't find a safe formation near the predicted center.
   d. Only on spawner success: `DirectorBudget -= group.ComputeCost(f)`.
5. Each stop condition just ends the loop for *this* pulse - the next pulse (a few seconds later, per `PulseInterval`) tries again with fresh state. No unbounded retry anywhere.

Note: the alive-enemy cap (`phase.MaxAliveEnemies`) isn't a separate early-exit step - it's one of `TrySelectGroup`'s per-candidate filters (`aliveCount + candidate.ComputeMemberCount() > phase.MaxAliveEnemies`), so a pulse that's at cap for big groups can still legitimately select and spawn a small one.

## Group validation algorithm (`CombatDirectorUtility.TrySelectGroup`)

For each `AssetRef<EnemyGroupConfig>` in `phase.AllowedGroups`, in authored order:

1. `Weight > 0` (a `Weight <= 0` group is a soft-disable, excluded from the roll entirely).
2. `SurvivalTime >= MinimumSurvivalTime` and (`MaximumSurvivalTime <= 0` or `SurvivalTime <= MaximumSurvivalTime`) - the group's own unlock window, on top of simply being listed in `AllowedGroups`.
3. `ComputeCost(f) <= DirectorBudget`.
4. `aliveCount + ComputeMemberCount() <= phase.MaxAliveEnemies`.
5. `MaxConcurrent <= 0`, or live copies (recounted from `EnemyLifecycle.SourceGroup`, not a maintained counter) `< MaxConcurrent`.

Every group that survives all five becomes a candidate in a **deterministic weighted roll**: one `f.RNG->Next(FP._0, totalWeight)` draw walked against each candidate's own `Weight` in authored order - not one roll per candidate, not a tactical score. A dev debugging "why this group" only ever needs the candidate list plus that single roll value.

**Known gap:** this step does not pre-check that every member's `EnemyData`/`SpawnProfile` assets actually exist - a group with a broken reference still gets selected here and only fails inside `GroupSpawnerUtility` (with a clear `Log.Error` and no budget spent, so it's safe, just not caught a step earlier). Acceptable for now since a broken reference is an authoring mistake surfaced immediately and loudly in the Console, not a runtime edge case.

## Physics-based group spawning algorithm (`GroupSpawnerUtility.TrySpawnGroup`)

1. Validate `DirectorConfig.EnemyPrototype` is assigned and the group has at least one member/quantity - log and fail otherwise (no attempts spent).
2. Up to `MaxGroupSpawnAttempts` times:
   a. Pick a candidate anchor via `EnemyMovementUtility.RandomPositionInRing` (deterministic angle + distance between `SpawnRingRadiusMin/Max`) around the predicted combat center.
   b. `TryFindGroundHeight` under it - no ground means try another anchor, no partial credit.
   c. Roll one shared facing rotation for the whole formation (same idiom as the previous design - the ring picks *where*, this picks the formation's *orientation*).
   d. Flatten every `GroupMemberEntry.Quantity` into individual formation slots (slot 0..N-1, continuous across members) and, for each slot:
      - `GroupFormationUtility.ComputeLocalOffset` (pattern/index/count/`FormationRadius`) -> rotate by the shared facing -> candidate horizontal position.
      - `TryFindGroundHeight` at that position - fail the whole attempt if missing.
      - Height rule (`ValidateHeightRule`) - only for `GroundMelee`/`GroundRanged`, compares `groundY - anchorGroundY` against the profile's `Minimum/MaximumHeightDifference`.
      - Clearance (`HasClearance`) - a vertical capsule overlap query (`ClearanceRadius`/`ClearanceHeight`) against Player | Enemy | Obstacle layers; any hit rejects the position.
   e. Any single member failing discards the **entire** attempt - nothing is created, the next attempt starts from a fresh anchor.
   f. All members valid -> create every entity now (never before this point - see "Budget Transaction" in the failure-cases section), return success.
3. All attempts exhausted -> return failure, nothing created, no budget spent (`CombatDirectorUtility` only subtracts cost after a `true` return).

## Ground accessibility heuristic

**Not implemented yet** - this is Milestone 4 (see roadmap). Today a `GroundMelee`/`GroundRanged` member only needs *a* floor under it within the height-difference band; nothing walks the path between the anchor and the predicted player position to reject an isolated platform, cliff-top ledge, or gap the enemy can't actually cross on foot. `EnemyMovementUtility.CanCrossLedge`/`HasGroundAhead` already implement the per-step version of this same probe for normal movement (see that file) - Milestone 4's job is reusing that shape as a multi-sample pre-spawn check (`GroundContinuitySamples`, `MaximumStepHeight` on `EnemySpawnProfile`), not inventing a new one.

## High-ground shooter validation

**Not implemented yet** - Milestone 5. `EnemySpawnCategory.HighGroundRanged` and `.Boss` exist as authored values today, but `GroupSpawnerUtility.ValidateHeightRule` treats both exactly like `Flying` (no height restriction at all) until line-of-sight and jump-down-landing search are built. Spawning a `HighGroundRanged` enemy today can strand it somewhere with no valid escape - don't author real `HighGroundRanged` content until this milestone lands.

## Enemy lifecycle and refund flow

Unchanged this pass - `EnemyLifecycleSystem` already implements the full `Active -> Irrelevant -> Retired` flow described in the original design:

- **Active**: contributes pressure, normal AI. **Relevant** = `Persistent || Tier == Elite || RecentCombatTimer > 0 || IsAttacking(Phase) || IsCloseToAnyPlayer(RelevantRange)` - a flat OR of five named conditions, not a score.
- **Irrelevant**: none of the above hold; `IrrelevantTimer` accumulates.
- **Retired**: `IrrelevantTimer >= RetireDelay` -> refund `Cost * RefundFraction` into `DirectorBudget`, `f.Destroy`. No XP/loot/kill-credit path is touched (retirement is a completely separate code path from `EnemySystem`'s own death handling - see `EnemyLifecycleSystem.Update`'s early-out on `Phase == Dead`, which prevents ever double-managing or refunding an enemy the players actually killed).

## Deterministic random-state strategy

Every random decision in the Director/Spawner goes through `f.RNG` (Quantum's deterministic stream), never `UnityEngine.Random`:

- Predicted-center ring anchor: `EnemyMovementUtility.RandomPositionInRing` (angle + distance).
- Formation facing roll: one `f.RNG->Next(0, 360)` per spawn attempt, shared by the whole formation.
- `GroupSpawnPattern.Scatter` offsets: one angle + distance roll per member (the only pattern that draws RNG at all - Cluster/Circle/Arc/Line are pure `index/count` formulas, reproducible from the member count alone with zero RNG draws).
- Weighted group selection: one `f.RNG->Next(FP._0, totalWeight)` cumulative-weight roll (see "Group validation algorithm").

Candidate ordering is always the authored array order (`phase.AllowedGroups`, `EnemyGroupConfig.Members`) - never a set/dictionary iteration - so the same seed and game state reproduce the same selected group and the same spawn attempt sequence, satisfying the original design's determinism requirement without any extra bookkeeping.

## Failure cases and safeguards

- **Budget Transaction**: `GroupSpawnerUtility` only ever creates entities after `TryValidateFormation` confirms every member's position - a partially-valid formation creates nothing, and `CombatDirectorUtility` only subtracts `ComputeCost(f)` after `TrySpawnGroup` returns `true`. A failed purchase costs nothing and stops that pulse's loop, not the whole system.
- **Bounded loops everywhere**: `MaxPurchasesPerPulse` (purchases/pulse), `MaxGroupSpawnAttempts` (anchors/purchase). No loop in this system is unbounded.
- **Missing config**: `CombatDirectorSystem` logs one `Log.Error` and stays idle if `RuntimeConfig` is missing `SurvivalConfig`/`DirectorConfig`/`LifecycleConfig`, or `SurvivalConfig.Phases` is empty - it doesn't throw.
- **Missing `DirectorConfig.EnemyPrototype`**: `GroupSpawnerUtility.TrySpawnGroup` logs and returns false immediately, no attempts spent.
- **Broken group/member references**: missing `EnemyData`/`SpawnProfile` on a member logs a clear error naming the group and rejects only that spawn attempt (see "Group validation algorithm"'s known gap).
- **Authoring guardrail**: `CombatDirectorSystem.ValidateOnce` logs an error (not a hard failure) if `LifecycleConfig.RelevantRange < DirectorConfig.SpawnRingRadiusMax` - a freshly spawned enemy could otherwise land already `Irrelevant` and retire without ever engaging.

## Minimal implementation roadmap

1. ~~Director skeleton + one hardcoded group / budget economy + purchase loop / lifecycle~~ - implemented (previous pass).
2. **Milestone 1 (Manual Group Spawn) + Milestone 2 (Basic Physics Spawn) - implemented this pass**: `EnemySpawnProfile`, auto-derived `ComputeCost`, deterministic formation patterns (`GroupFormationUtility`), ring + ground-raycast + clearance validation, same-level height rule for `GroundMelee`/`GroundRanged` (`GroupSpawnerUtility`). **Not included**: a manual debug spawn command (Milestone 1's own suggestion) - for now, test by authoring a `SurvivalConfig` phase with a short `PulseInterval` and a fast `BudgetPerPulse`.
3. **Milestone 4 (Ground Accessibility)** - not started. Add `GroundContinuitySamples`/`MaximumStepHeight` to `EnemySpawnProfile`; reuse `EnemyMovementUtility`'s existing ledge/step probes as a multi-sample pre-spawn check between anchor and predicted player position.
4. **Milestone 5 (High-Ground Shooter)** - not started. Add line-of-sight validation and jump-down landing search; wire `EnemySpawnCategory.HighGroundRanged` to something other than "treated like Flying."
5. **Milestone 3 refinement (Spawn Attempt Strategy)** - not started. Today a failed anchor is discarded outright; the original design's fuller escalation (`AngleAttemptStep`, `DistanceRelaxationStep`, `FormationRadiusRelaxation`, per-member retry) is deferred until the simple whole-anchor retry proves insufficient in practice.
6. **Milestone 7 (Co-op Scaling)** - budget half implemented, target-pressure/cap not started. `CombatDirectorUtility.ResolveBudgetMultiplier` scales `phase.BudgetPerPulse` by a run curve (`CurveChannel.DirectorBudget`) and a co-op multiplier (`CoopGlobalKey.DirectorBudget`), both authored on the new `BalanceConfig` asset (see `docs/run-curves-coop-scaling.md`) - missing `BalanceConfig` degrades to a `1x` no-op rather than halting the Director. `SurvivalPhase.TargetPressure`/`MaxAliveEnemies` are still static per-phase values, not player-count-scaled - no `CoopGlobalKey` exists for either yet.
7. Explicitly out of scope for this system (per the original design): events, boss integration via the Director (bosses stay hand-placed via `BossSystem`), player-cluster/split-party-aware spawning, meta progression/rewards, difficulty presets (should mean "point `RuntimeConfig` at a different `SurvivalConfig`," not new code), procedural map generation itself.

## Deliberate simplifications (don't "fix" these without a real reason)

- **`EnemySpawnProfile` only has the fields Milestone 1-2 consume.** Ground-probe tuning (start height/distance - `TryFindGroundHeight`'s existing hardcoded constant is reused instead), ground-continuity sampling, and line-of-sight/jump-down fields are real future additions (Milestone 4/5), deliberately left off rather than authored-but-inert - an unused field here would be indistinguishable from a bug, unlike e.g. `EnemyDataAsset.Traits`, which is inert by an explicit, already-commented design choice.
- **`HighGroundRanged`/`Boss` spawn categories are placeholders.** They exist so a designer can mark intent now, but `GroupSpawnerUtility` treats both exactly like `Flying` (no height restriction) until Milestone 5. Don't author real `HighGroundRanged` content yet.
- **No ground-accessibility/path heuristic yet** (Milestone 4) - a `GroundMelee` member only needs *a* floor at the right height, not a walkable route from there to the fight.
- **`AllowsPartialSpawn` is authored but unused.** `GroupSpawnerUtility` is always strictly all-or-nothing today.
- **`TrySelectGroup` doesn't pre-validate member asset references** - see "Group validation algorithm"'s own "Known gap" note. Safe (no budget spent) but not caught a step earlier.
- **No manual debug spawn command yet** - test via a fast-pulsing `SurvivalConfig` phase instead (see roadmap step 2).

## What's implemented vs. what's still needed (authoring checklist)

**Update (2026-08-07): all of the below is now authored and wired.** The code compiles, real content exists, and the Director should actually spawn at runtime - verify in-Editor if you haven't yet. Kept here as the up-to-date record of what's in place, not as a to-do list.

1. ~~Author at least one `EnemySpawnProfile`~~ - done: `GroundEnemy.asset` (`_QuantumUser/Resources/Enemy/_SpawnProfile/`), `SpawnCategory = GroundMelee`, `ClearanceRadius = 1`, `ClearanceHeight = 2`, `MinimumHeightDifference = -2`, `MaximumHeightDifference = 0`.
2. ~~Assign that profile to every `EnemyDataAsset`~~ - done: all 11 `BaseEnemies/*.asset` (Shielder, NormalRanged, HeavySlammer, NormalMelee, Charger, LeaperEnemy, Grenadier, Flanker, Suicider, Sniper, Swarm) have `Economy.SpawnProfile -> GroundEnemy` assigned. Note the field moved under a nested `Economy` struct in a later reorg (`EnemyDataAsset.Economy.SpawnProfile`, not the old flat `SpawnProfile` - the flat field is now a `[HideInInspector]` migration-bridge leftover, see that file's own "MIGRATION BRIDGE" comment).
3. ~~Assign `DirectorConfig.EnemyPrototype`~~ - done: `DirectorConfig.asset` has a non-empty `EnemyPrototype` reference (the shared generic prototype now lives at `Assets/_QuantumUser/Entities/Enemies/GenericEnemyPrefab.prefab`, not `BasicEnemy` - the prefab was renamed since this doc was first written).
4. ~~Author one or more `EnemyGroupConfig` assets~~ - done: `EnemyGroupConfig.asset` (`_QuantumUser/Resources/Director/EnemyGroups/`), one member (Sniper x1), `Weight = 1`, `MaxConcurrent = 3`, `SpawnPattern = Cluster`, `FormationRadius = 3`.
5. ~~Author a `SurvivalConfig` with at least one `SurvivalPhase`~~ - done: `SurvivalConfig.asset` has one phase (`BudgetPerPulse = 100`, `PulseInterval = 1`, `TargetPressure = 10`, `MaxAliveEnemies = 5`, `AllowedGroups = [EnemyGroupConfig]`).
6. ~~`LifecycleConfig.RelevantRange >= DirectorConfig.SpawnRingRadiusMax`~~ - fixed 2026-08-07: `RelevantRange` was 12 (less than `SpawnRingRadiusMax`'s 16, the exact inverted case the `ValidateOnce` guardrail warns about), raised to 20 for a margin above 16 rather than the bare minimum.
7. ~~Stale `MaxSpawnPlacementAttempts` YAML key~~ - resolved; the current `DirectorConfig.asset` only has `MaxGroupSpawnAttempts: 8`, no orphaned key.
8. ~~Optional: assign `RuntimeConfig.BalanceConfig`~~ - done, see `docs/run-curves-coop-scaling.md`'s own now-updated status.

## First playable content pass (2026-08-07)

**Important - which `SurvivalConfig` is actually live**: `RuntimeConfig.SurvivalConfig` in `QuantumGameScene.unity` is currently assigned to **`SurvivalConfig_MVP.asset`** (Guid `516156630782283822`), not the `SurvivalConfig.asset` this generator writes to (Guid `322533958745177076`). `SurvivalConfig_MVP.asset` is authored by a separate script, `Assets/_QuantumUser/Editor/MvpSurvivalContentGenerator.cs` (`Tools > RiftRaiders > Generate MVP Survival Content`) - a reduced 7-enemy-roster timeline (Filler, Swarm, NormalMelee, NormalRanged, Charger, Grenadier, HeavySlammer only) for balance-testing, with its own 5-phase `PhaseSpecs` (including a 30s solo-Filler "Phase 0" this generator's own timeline doesn't have) and 4 new groups (`FillerSolo`, `FillerCreepMvp`, `MeleeOnly`, `RangedOnly`) alongside 4 reused groups from this generator's own roster (`SwarmRush`, `ChargerDuo`, `SlammerPincer`, `GrenadierBarrage`). **Any balance change meant to affect actual gameplay right now needs to land in `MvpSurvivalContentGenerator.cs`, not (only) here** - the two are independent timelines that happen to share some `EnemyGroupConfig` assets (so a `Weight`/`MaxConcurrent` edit to e.g. `SwarmRush` itself affects both, but each generator's own `PhaseSpecs` numbers don't). The "Overall population raised" bullet below was mirrored into `MvpSurvivalContentGenerator.cs`'s own phases at the same ratio; the "Chaff tuning" bullet was not (verify whether `FillerCreepMvp`'s own already-elevated `Weight`/`MaxConcurrent` still reads as filler-heavy enough before touching it further).

`Assets/_QuantumUser/Editor/SurvivalDirectorContentGenerator.cs` (`Tools > RiftRaiders > Generate Survival Director Content`, same menu group/pattern as every other `*AssetGenerator.cs` in this project) authors:

- **11 `EnemyGroupConfig` assets** under `_QuantumUser/Resources/Director/EnemyGroups/` (`FillerCreep`, `SwarmRush`, `SuicideSquad`, `MeleeSkirmish`, `RangedSkirmish`, `ChargerDuo`, `ShieldWall`, `SlammerPincer`, `GrenadierBarrage`, `LeaperAmbush`, `FullAssault`) - each composed from 1+ of the `BaseEnemies` (now 12, since `Filler.asset` - a deliberate slower Swarm reskin - was added) in a formation pattern that matches its archetype (e.g. `Line` for a backline skirmish, `Circle` for a two-sided Slammer pincer, `Scatter` for Suiciders/Leapers that shouldn't clump). See the generator's own per-spec comments for the design intent behind each.
- **A full 6-phase `SurvivalConfig.Phases` timeline** (overwrites the prior single-phase placeholder wholesale), ramping from a 2-enemy-type warm-up at minute 0 to the full group roster by minute 8-10, then holding as an endless last phase - budget/pressure/cap only ramp modestly phase-to-phase by design, since `CombatDirectorUtility.ResolveBudgetMultiplier`'s own run curve already does most of the time-based scaling (see `docs/run-curves-coop-scaling.md`) - this timeline's job is unlocking new `AllowedGroups` and raising the ceiling that curve-scaled budget spends against, not re-deriving the ramp a second time.
- **Chaff tuning (2026-08-07, in response to playtesting feedback that fillers felt scarce)**: `FillerCreep`/`SwarmRush` both got `Weight` raised to `1.5` (every other group sits at `0.6`-`1.0`) and are now in **every** phase's `AllowedGroups`, not just bookending the run - previously `FillerCreep` was Phase-1-only and `SwarmRush` dropped out for two phases in the middle of the ramp. Without a weight edge, chaff's *share* of the weighted roll silently shrinks every time a phase adds more competing groups to the roster, even though its own absolute `Weight` never changed - reads as "fewer fillers" the deeper into a run you get, purely from dilution.
- **Overall population raised (2026-08-07, "more enemies in quantity")**: `MaxAliveEnemies`/`TargetPressure` both raised roughly 60-70% across all 6 phases (`MaxAliveEnemies`: 6/10/14/18/22/24 -> 10/16/22/28/34/40; `TargetPressure`: 8/14/20/28/36/44 -> 14/22/32/44/56/68), `BudgetPerPulse` raised to match so the Director can actually afford to buy up to the new ceilings rather than being budget-starved under them, and `DirectorConfig.MaxPurchasesPerPulse` raised `3` -> `5` so a single pulse isn't attempt-capped before it reaches the new headroom either. All three levers had to move together - raising just the caps without the budget/purchase-count to fill them would've been a no-op.

**Not yet run** - Unity was already open with this project when this was authored, so the generator wasn't invoked headlessly (see the project `CLAUDE.md`'s own warning about a second headless instance against an already-open Editor). Run `Tools > RiftRaiders > Generate Survival Director Content` once in the open Editor to materialize it. The prior single-group `EnemyGroupConfig.asset` stub in the same folder is superseded and left unreferenced - safe to delete by hand once the new content is verified.

Alongside this, all 11 `BaseEnemies`' action `Damage` values were rebalanced by hand (see `docs/hero-balance-pass.md`-adjacent context in `CLAUDE.md` for the player-side target baseline: 100 HP/30 Shield live on Kai) - `HeavySlammer` was doing literally 0 damage (dead action), `Sniper` was hitting for 40 (nearly a third of a player's effective HP) off a cheap Normal-tier enemy, and `Swarm`'s near-zero attack cycle worked out to ~25 DPS in melee contact. All 11 are now edited directly in their `.asset` files (no codegen needed, same as any other plain data field) to a coherent tier-shaped curve - no per-enemy `.asset` needs regenerating for this part, only the group/phase content above needs the Editor step.

If `RuntimeConfig` is missing any of the three Domain config refs, or `SurvivalConfig.Phases` is empty, `CombatDirectorSystem` logs an error once and stays idle rather than throwing - no longer the actual state, but still the correct fallback behavior to know about.

---

## Player clusters (co-op split spawning)

When co-op players wander apart, spawning everything at the party centroid drops enemies in the
empty gap between them, where they instantly fall outside `LifecycleConfig.RelevantRange` of anyone
and retire without engaging - the reported "survival mode does nothing when players are far apart".
Fixed by treating each proximity cluster as its own combat front.

**Files:** `PlayerClusterDirectorUtility.cs` (new) owns clustering + all the split math; wired from
`CombatDirectorSystem.Update` (`UpdateRuntimeScalars` each combat tick) and
`CombatDirectorUtility.TryPulse` (anchor plan + per-front distribution). New `DirectorConfig` fields
and `SurvivalDirector.qtn` globals hold the tunables/runtime state. Reward scaling injected in
`CurrencyOrbSystem.Grant`.

**Threat model (reuses the existing per-player-count curve as `GetThreatBudget(n)` =
`BalanceConfig.GetCoopGlobal(DirectorBudget, n)`):**
- `BasePartyBudget = GetThreatBudget(liveClusteredPlayerCount)`.
- `RequestedSplit = Σ GetThreatBudget(clusterSize)` over the live clusters.
- `FinalTotal = min(RequestedSplit, BasePartyBudget * MaxSplitThreatMultiplier)` (cap, 1.40 default;
  over-cap cluster budgets scale down proportionally).
- `SplitThreatMultiplier = FinalTotal / BasePartyBudget` (>=1, exactly 1 when cohesive). Multiplies
  `DirectorBudget` accrual, per-front `TargetPressure`, and `MaxAliveEnemies`. Nothing is hardcoded
  to 4 players - every term reads a live cluster size.

**Rewards (kept separate from threat so splitting isn't an XP/Coin farm):**
`PerEnemyXpScale = (1 + (SplitThreatMultiplier-1)*SplitXpRewardFactor) / SplitThreatMultiplier`
(coin analogue with `SplitCoinRewardFactor`). With factor 0 it's exact break-even (total reward
unchanged despite more enemies); the shipped defaults 0.25/0.10 give a small risk bonus (+10% XP /
+4% coins at the 1.40 cap). Applied per orb in `CurrencyOrbSystem.Grant`; Rift Shards unscaled.

**Cluster detection:** deterministic union-find over `PlayerLink` entities by flat XZ
`ClusterDistance` (30). Anti-flicker is a hysteresis on the cohesive<->split *mode* only
(`DirectorSplitActive`/`DirectorSplitTimer`, `ClusterSplitDelay` 2s / `ClusterMergeDelay` 1s), so
wandering near the boundary doesn't thrash the scalars; within split mode the anchors follow players
live. Single-front (solo/cohesive) uses the exact pre-cluster global pressure gate, so non-split
play is unchanged.

**Distribution:** each pulse round-robins purchases to the front furthest below its own
`TargetPressure` share (local pressure = active enemy cost within `ClusterPressureRadius`); a front
that can't place a group is marked exhausted so it doesn't starve the others. **Major enemies
(`Tier >= Elite`) always anchor at the global centroid, never duplicated per front** - Elite/midboss
stay a global event; the Boss uses its own separate `RunPhaseUtility.BeginBossEncounter` path.
During Breathing/Boss the scalars reset to 1 (no split threat/reward while the party scatters to
shops). No existing enemies are ever moved/retired by a cluster change - it only affects future
spawns.

**Editor:** `SurvivalDirector.qtn`'s new globals need codegen (automatic in the open Editor). The new
`DirectorConfig` fields default in code, so the existing `DirectorConfig.asset` picks them up on
reimport - tune `ClusterDistance`/`MaxSplitThreatMultiplier`/reward factors/delays there. Not yet
verified end-to-end in co-op.
