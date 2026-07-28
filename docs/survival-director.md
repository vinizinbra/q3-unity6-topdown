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
6. **Milestone 7 (Co-op Scaling)** - not started. Living player count -> budget/target-pressure/cap multipliers.
7. Explicitly out of scope for this system (per the original design): events, boss integration via the Director (bosses stay hand-placed via `BossSystem`), player-cluster/split-party-aware spawning, meta progression/rewards, difficulty presets (should mean "point `RuntimeConfig` at a different `SurvivalConfig`," not new code), procedural map generation itself.

## Deliberate simplifications (don't "fix" these without a real reason)

- **`EnemySpawnProfile` only has the fields Milestone 1-2 consume.** Ground-probe tuning (start height/distance - `TryFindGroundHeight`'s existing hardcoded constant is reused instead), ground-continuity sampling, and line-of-sight/jump-down fields are real future additions (Milestone 4/5), deliberately left off rather than authored-but-inert - an unused field here would be indistinguishable from a bug, unlike e.g. `EnemyDataAsset.Traits`, which is inert by an explicit, already-commented design choice.
- **`HighGroundRanged`/`Boss` spawn categories are placeholders.** They exist so a designer can mark intent now, but `GroupSpawnerUtility` treats both exactly like `Flying` (no height restriction) until Milestone 5. Don't author real `HighGroundRanged` content yet.
- **No ground-accessibility/path heuristic yet** (Milestone 4) - a `GroundMelee` member only needs *a* floor at the right height, not a walkable route from there to the fight.
- **`AllowsPartialSpawn` is authored but unused.** `GroupSpawnerUtility` is always strictly all-or-nothing today.
- **`TrySelectGroup` doesn't pre-validate member asset references** - see "Group validation algorithm"'s own "Known gap" note. Safe (no budget spent) but not caught a step earlier.
- **No manual debug spawn command yet** - test via a fast-pulsing `SurvivalConfig` phase instead (see roadmap step 2).

## What's implemented vs. what's still needed (authoring checklist)

The code compiles and is wired in, but **the Director does nothing yet at runtime** until real content exists:

1. Author at least one `EnemySpawnProfile` (e.g. a `GroundMelee` profile with sensible `ClearanceRadius`/`Height` and a small negative `MinimumHeightDifference`).
2. Assign that profile to every `EnemyDataAsset` you want the Director to be able to spawn (`EnemyDataAsset.SpawnProfile`).
3. Assign `DirectorConfig.EnemyPrototype` to the project's shared generic enemy prototype (`BasicEnemy`'s baked `EntityPrototype` asset) - **this is new and required**; the existing `DirectorConfig.asset` predates this field and needs it set before anything can spawn.
4. Author one or more `EnemyGroupConfig` assets: pick `Members` (`EnemyData` + `Quantity`), a `SpawnPattern`, and a `FormationRadius`. `Weight`/`MinimumSurvivalTime`/`MaximumSurvivalTime` are optional (default to "always eligible, equal weight").
5. Author a `SurvivalConfig` with at least one `SurvivalPhase` (set `BudgetPerPulse`, `PulseInterval`, `TargetPressure`, `MaxAliveEnemies` to something non-zero, and populate `AllowedGroups`) - the existing `SurvivalConfig.asset` still has an empty `Phases` array.
6. **Important:** keep `LifecycleConfig.RelevantRange >= DirectorConfig.SpawnRingRadiusMax` (see the `ValidateOnce` guardrail above).
7. **Note on the existing `DirectorConfig.asset`**: its old `MaxSpawnPlacementAttempts: 8` YAML key is now orphaned (Unity drops unrecognized keys silently on next save) - the renamed `MaxGroupSpawnAttempts` field defaults to the same value (8) in code, so nothing changes functionally, but the Editor should re-save this asset once opened to clean up the stale key.

If `RuntimeConfig` is missing any of the three Domain config refs, or `SurvivalConfig.Phases` is empty, `CombatDirectorSystem` logs an error once and stays idle rather than throwing.
