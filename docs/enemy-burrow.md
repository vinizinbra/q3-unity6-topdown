# Enemy Burrow / Invulnerable Relocation

A reusable `EnemyDeliveryData` that lets an enemy dive underground, become fully invulnerable and
untargetable, travel invisibly to a new point near its target, and resurface there - the "burrow or
invulnerable relocation" enemy-design pattern: prevents an enemy from dying/being pinned in place,
creates anticipation, and hands it back off into the normal (telegraphed, avoidable) attack cycle
once it resurfaces.

## Why this was mostly already possible

Before this change, almost every piece already existed in the codebase, just unused or scattered:

- `Invulnerable` (`Assets/_QuantumUser/Simulation/QTN/Invulnerable.qtn`) was already an empty tag
  component that `DamageUtility.ApplyDamage` already no-ops every hit against - nothing added it.
- The `EnemyActionPhase`/`EnemyDeliveryData` framework (`Preparation` -> `Telegraph` -> `Active` ->
  `Recovery`, see `Assets/_QuantumUser/Simulation/QTN/Enemy/Enemy.qtn`) already models exactly this
  "windup, commit, multi-tick execution, cooldown" shape, with `ChargeDeliveryData`/`LeapDeliveryData`
  as kinematic multi-tick precedents and `TeleportBlinkDeliveryData` as a pure-reposition (no damage)
  precedent.
- `EnemyMovementUtility` already had `RandomPositionInRing`, `TryFindGroundHeight`, and
  `MoveKinematicTowards` - everything needed to pick a relocation destination and move there.

The one real gap was that player-side targeting (`AimSystem`, `VortexSystem`,
`EnemyMovementUtility.TryFindNearestEnemy`) never checked `Invulnerable` at all - an "untargetable"
enemy would still get auto-aimed/homed-at even though hits on it were already silently ignored.

## What this change adds

- **`Burrowed`** (`Assets/_QuantumUser/Simulation/QTN/Enemy/Burrowed.qtn`) - a new empty tag
  component, kept separate from `Invulnerable` so a future unrelated invulnerability source (e.g. a
  shield mechanic) doesn't also hide the enemy's sprite. Added/removed together with `Invulnerable`
  by `BurrowDeliveryData`, never independently.
- **`BurrowDeliveryData`**
  (`Assets/_QuantumUser/Simulation/Assets/Enemy/Actions/Delivery/BurrowDeliveryData.cs`) - the new
  delivery. `Begin()` picks a destination (`RandomizeAroundAnchor` around the target, ground-corrected
  via `TryFindGroundHeight`, same trick `LeapDeliveryData` uses for its landing spot), goes kinematic,
  and adds `Invulnerable` + `Burrowed`. `Tick()` runs three lerp sub-phases off one shared
  `StateTimer` countdown - Dive (sink in place) -> Travel (move underground at `-DiveDepth`) ->
  Resurface (rise back to real ground height at the destination) - then removes both tags and
  finishes. No damage `Effects` - purely repositioning. The "exit attack is avoidable" part of the
  design pattern falls out for free from the existing Recovery -> Chasing -> Preparation/Telegraph
  cycle that follows once it resurfaces; this delivery doesn't need its own bolted-on "burst on
  emerge."
- **Targeting patched to skip `Invulnerable`**, next to each site's existing dead-enemy check:
  `AimSystem.IsAliveTarget`, `EnemyMovementUtility.TryFindNearestEnemy`, `VortexSystem.TryFindNearestEnemy`
  (this last one didn't even exclude dead enemies before this change either).
- **View**: `EnemyBlobAnimationView` gained a `Burrow` state, watched off `Burrowed` the same
  edge-triggered way it already watches `Enemy.Phase` for `Dead` - shrinks/sinks the rig away on the
  dive (reusing the same squash-and-shrink math `Die` uses, but reversibly via a new `_burrowT`
  instead of `Die`'s one-way `_dieShrinkT`), holds hidden while still `Burrowed`, and grows back on
  resurface.

## Current status / what's still needed

The code compiles and every piece above is wired, but **no enemy actually uses it yet** - same
situation as every other system documented in the project `CLAUDE.md`. To make a real enemy burrow:

1. In the Editor, create an `EnemyActionData` asset and assign `BurrowDeliveryData` (also authored as
   its own asset) as its `Delivery`.
2. Tune `EnemyActionData.EngageRange` large (`TrySelectAction`'s range gate always applies regardless
   of `Trigger` - see `EnemyDecisionUtility.cs` - so a small `EngageRange` would prevent this action
   from ever being selected except at melee range).
3. Tune `EnemyActionData.CooldownTime` long, so it can't burrow back-to-back ("cannot remain
   invulnerable repeatedly").
4. Optionally set `EnemyActionData.Trigger.Type = OnHealthThreshold` (with a `HealthPercent` like
   `0.25`-`0.3`) so it reads as a genuine escape - "prevents immediate deletion" - rather than a
   random reposition; leave it `Cooldown` (the default) for a periodic reposition instead.
5. Add the `EnemyActionData` into that enemy's `EnemyDataAsset.SkillActions`.

## Collision while burrowed (fixed 2026-10-01)

`Begin()` parks the enemy's collider on the **DeadEnemy** layer (collides with Ground/Obstacle only)
and stores the original in `Burrowed.PreviousLayer`; the resurface restores it. So a burrowing enemy
no longer shoves other enemies or players along its travel path, and Enemy|Boss-mask queries skip it.
No project-settings change needed - the layer already existed with the right matrix row.

## Sandworm Larva (World 2, 2026-10-01) - first real user

Normal-tier (HP 60) Wildlife melee that gap-closes by burrowing. Not in any `SurvivalConfig`/group yet -
spawn it with `EnemyDataAsset`'s "Spawn Near Local Player" Inspector button.

- `Enemy/World2/Wildlife/SandwormLarva/W2-SandwormLarva.asset` - cloned NormalMelee chain (own guids:
  `SandwormLarvaBiteAction`/`BiteDelivery`) + cloned elite burrow chain (`SandwormLarvaBurrowAction`/
  `BurrowDelivery`, which carries the existing `DustTrailWithRocks` OnGoingStep FX and
  `HitDustExplosion` impact), `Actions.SkillActions = [Burrow]`. MoveSpeed 4 (NormalMelee 4.5).
- Bite: SelectionWeight 4. Burrow: SelectionWeight 1, EngageRange 6, Cooldown 7s, DownTime 0.5s (punish
  window), cooldown-triggered. Score math (`EnemyDecisionUtility.TrySelectAction`): weight + up to 2 for
  range - so in bite range the bite always wins, and out of it only the burrow qualifies.
- Burrow action uses homing (`UpdateTargetDirectionWhileActive`, set by the user), MaxTravelDuration 2s, ArriveDistance 0.5.
- Burrow delivery: TowardTarget, resurfaces 1.2-2.2 from the player when not homing, Dive/Resurface 0.25s,
  BurrowMoveSpeed 5, `RetargetAtPercent` 0.5 (re-aims mid-travel while hidden), `AttackOnResurface` off - the bite
  after emerging is its own normal telegraphed attack.
- View: `_Project/Prefabs/View/EnemyView/World2/Wildlife/SandwormLarva.prefab` - Prefab Variant of
  `ScavengerHunt-Melee` (placeholder art).

**Bug found here - skill cooldowns never applied to Director spawns.** Skill cooldowns live on the
optional `EnemyActionSlots` component, which was only on the boss prefab; every Director spawn uses
one generic prototype without it, so `SetCooldownRemaining` silently no-oped and a skill stayed
permanently off cooldown - it kept winning the selection (repetition penalty aside) and the enemy
never attacked. `EnemySystem.SeedActionSlots` now adds it at spawn for any enemy with SkillActions.
No other Director-spawned enemy had SkillActions yet (the World 1 boss carries the component on its own prefab), so the Larva was the first to hit it.

**Travel is speed-based (2026-10-01).** Travel used to be a timed `Lerp(start, destination, t)`, so
any destination change mid-way snapped the enemy (and its dust trail). It's now real movement: each
tick it steps toward the sunk destination at **`BurrowMoveSpeed`** (u/s, formerly `TravelSpeed` -
`FormerlySerializedAs` kept existing values: Larva 5, elite 10) and Travel ends on arrival. Dive and
Resurface stay timed (`Enemy.StateTimer`). Per-burrow state lives on `Burrowed` (Stage, MoveSpeed,
TravelElapsed, RetargetAt, Retargeted, PreviousLayer). `TravelDuration` is only a fallback when
`BurrowMoveSpeed <= 0` (speed = distance / TravelDuration, resolved once at Begin).
`RetargetAtPercent` = fraction of the first leg's estimated travel time; fires once, underground,
re-scattering around the target's LIVE position (it used to read `SkillTargetPosition`, which Begin()
overwrites with the destination - so it scattered around the old landing spot) and just steers there.
**Homing burrow (`DirectionTracking = UpdateTargetDirectionWhileActive`)** is supported: EnemySystem
re-points `SkillTargetPosition` at the live target every tick, so the worm steers after the player
underground (no scatter - it surfaces under them). `MaxTravelDuration` (default 3s, Larva 2s, <= 0 =
no cap) stops a chase it can't win: on timeout it resurfaces where it is if there's ground, else keeps
going until there is. The landing point is locked at the start of Resurface (stored in
`SkillStartPosition`) and snapped to the real ground under it. `ArriveDistance` (default 0, Larva 0.5)
ends Travel that far (flat) short of the destination on the approach side, so a homing worm surfaces
beside the target instead of under it. Travel moves flat; height only matters at the landing snap, so the rise doesn't slide after the
target or end below ground. `RetargetAtPercent` is redundant with homing (the next tick's tracking
overwrites it).

Not done yet: a ground warning at the resurface point.
