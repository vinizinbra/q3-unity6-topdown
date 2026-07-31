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

## Known simplification

A burrowed/traveling enemy stays a solid `PhysicsBody3D` - a player can still physically bump into it
mid-relocation even though it's invisible and can't be hit/targeted. Fixing this would mean adding a
new physics layer (e.g. `BurrowedEnemy`) plus a Unity collision-matrix entry, the same trick
`EnemySystem.OnEnemyDied` already uses for the `DeadEnemy` layer - that's pure Unity Editor
project-settings work (Project Settings -> Tags and Layers / Physics), not code, and wasn't done here.
See `EnemySystem.cs:245-258` for the precedent if this ever needs fixing.
