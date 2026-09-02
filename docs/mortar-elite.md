# Mortar Elite / Random-Scatter Barrage

A new `EnemyDeliveryData` that fires several real, arc-lobbed projectiles at once - some landing
exactly on the target's locked position (forcing them to actually move), the rest scattered randomly
around that same point (so standing anywhere nearby is never fully safe either) - each preceded by a
visible ground telegraph the player can read and dodge before it lands. The ground telegraph itself
ended up a small, fully generic primitive, reusable by the pre-existing single-shot Mortar (and any
future delayed-ground-impact attack) rather than a Mortar-Elite-only mechanism.

## Why this was mostly already possible

Almost every piece already existed, just not combined the way this attack needs:

- `ProjectileDeliveryData.UseArc` (+ `LaunchAngle`/`Gravity`, using `ProjectileSpawner.SolveArcLaunch`)
  already lobs a single shot at the locked target - its own comment says it was built specifically to
  absorb the "Mortar" roster concept (see `Assets/_QuantumUser/Docs/enemies.md`'s Bomber row). It only
  ever fires one shot, though, and its `WaitForImpact` option tracks the in-flight shot via the single
  `Enemy.SkillProjectile` field - it can't wait on more than one at a time.
- `ScatterDeliveryData` already resolves N independent random points around an anchor (via the shared
  `EnemyDeliveryData.RandomizeAroundAnchor`/`MinRandomOffset`/`MaxRandomOffset` - a ring, so a point is
  never exactly on the anchor), but applies instant `HitEffectData` there rather than spawning
  something that flies to it.
- `FanProjectileDeliveryData` already spawns several real projectiles in one `Begin()` - but always
  resolves instantly with no telegraph, precisely because (per its own comment) `Enemy.SkillProjectile`
  can't track more than one in-flight shot, so it never even tries.
- Impact damage needed no new code at all - a projectile's `Hit` is already resolved generically
  through `ProjectileHitData`, and an `AreaHitData` instance (the same "reused purely as data" pattern
  already established for Explode-On-Destroy/Pixie's bomblets) gives blast-radius damage on landing
  for free - and, as of the pass described below, also supplies the ground-warning circle's own
  radius, rather than a second authored number.
- **The ground telegraph itself needed no new simulation-side entity/component either** - the View-
  side `TelegraphManager` pool (`Assets/_QuantumUser/View/Managers/TelegraphManager.cs`) already
  supports any number of simultaneous independent `Get()`/`Release()` instances on its own - the "only
  one telegraph at a time" limitation lives entirely in `EnemyAttackVisualsView`'s own bookkeeping
  (`_currentTelegraph`, a single slot per enemy), not in the pool itself. A plain fired event carrying
  just Position/Duration/Radius, with a small new listener pulling straight from that pool, does the
  exact same job with zero simulation-side state.

## What this change adds

- **`MortarBarrageDeliveryData`**
  (`Assets/_QuantumUser/Simulation/Assets/Enemy/Actions/Delivery/MortarBarrageDeliveryData.cs`) - the
  new delivery. Always resolves instantly (`Begin()` returns `true`, same as `ScatterDeliveryData`/
  `FanProjectileDeliveryData` - the enemy is immediately free to act again, sidestepping
  `Enemy.SkillProjectile`'s single-slot limit entirely rather than fighting it). For `ShellCount`
  shots, the first `AimedShellCount` land exactly on the locked anchor (`action.Origin` -
  `Enemy.SkillTargetPosition` for the common `TargetAnchor` case - same aim `ProjectileDeliveryData`
  already uses); the rest go through the inherited `RandomizeAroundAnchor`, same ring
  `ScatterDeliveryData` already uses. `AimedShellCount` is authored per asset, not hardcoded, so a
  Normal-tier mortar (`AimedShellCount = 0`, pure area denial) and an Elite one
  (`AimedShellCount > 0`, forces real movement) can share this exact class. The arc itself is solved
  via `movement.GetLaunchToTarget(f, origin, point, EntityRef.None)` on whichever
  `ProjectileMovementData` the shell's own `ProjectileDataAsset.Movement` points at (typically a
  `BallisticProjectileMovementData`) - not a direct `ProjectileSpawner.SolveArcLaunch` call with the
  delivery's own `LaunchAngle`/`Gravity` (an earlier version did exactly that, and is why those
  fields don't exist on this class - see "Bugs found" below).
- **`EnemyDeliveryData.FireLandingWarning`/`ResolveWarningRadius`**
  (`Assets/_QuantumUser/Simulation/Assets/Enemy/Actions/Delivery/EnemyDeliveryData.cs`) - two small
  shared static helpers on the base class, not specific to Mortar. `FireLandingWarning(f, origin,
  point, velocity, radius)` derives real flight time from the solved launch's own horizontal speed
  and fires `ProjectileLandingWarning` (no-ops if radius or flight time is 0). `ResolveWarningRadius(f,
  hit)` reads a projectile's own `Hit` back and returns `AreaHitData.BlastRadius` if it has one, `0`
  otherwise - so the warning circle's size is never a second authored field that can drift out of
  sync with the real blast radius. `MortarBarrageDeliveryData` calls both per shell.
- **`ProjectileDeliveryData.ShowLandingWarning`** (new opt-in bool, default `false` - every existing
  shot keeps its exact current behavior) - calls the same two helpers for a single-shot lob, so the
  pre-existing single-shot `MortarEnemy` can opt into the exact same ground telegraph
  `MortarBarrageDeliveryData` uses instead of (or alongside) its old caster-anchored windup Circle.
- **`ProjectileLandingWarning`** (`Assets/_QuantumUser/Simulation/QTN/Events.qtn`) - a new, generic
  event carrying only `Position`/`Duration`/`Radius` (deliberately no `EntityRef` owner - several fire
  independently at once, and nothing downstream needs to know which enemy sent them). Named after the
  mechanism, not the enemy - any future delivery can fire it directly
  (`f.Events.ProjectileLandingWarning(point, authoredDuration, radius)`) for a delayed ground impact
  that isn't a projectile at all (e.g. a boss dropping a volley of telegraphed spikes), skipping
  `FireLandingWarning`'s flight-time derivation entirely when the duration is just an authored fuse.
- **`GroundWarningTelegraphManager`**
  (`Assets/_QuantumUser/View/Managers/GroundWarningTelegraphManager.cs`) - a small new scene-level
  listener (sibling to `EffectsManager`/`TelegraphManager` in the same folder) that subscribes to
  `ProjectileLandingWarning` and, per event, pulls an instance from `TelegraphManager`'s pool, sizes it
  to `Radius`, snaps it onto the real Unity ground (`Physics.Raycast` against the `Ground` layer, same
  fix `EnemyAttackVisualsView.SnapToGround` already uses - see "Bugs found"), and calls the exact same
  `TelegraphFade.Initialize(...)` (fade in, optional child `TelegraphGrow` fill animation) that
  `EnemyAttackVisualsView` already uses for a caster's own windup telegraph - then schedules
  `FadeOutAndRelease()` after `Duration` via a coroutine. No simulation-side entity/component at all.

## Current status / what's still needed

The code compiles once Quantum codegen picks up the new `ProjectileLandingWarning` event in
`Events.qtn` - but **no enemy actually uses this yet**, same situation as every other system
documented in the project `CLAUDE.md`. To make a real Mortar Elite:

1. Author a `MortarShell` `ProjectileDataAsset` - `Movement` = a `BallisticProjectileMovementData`
   instance with its own `LaunchAngle`/`Gravity` authored there (this is the ONLY place those two
   values live), `Hit` = an `AreaHitData` instance (blast damage + radius on impact - this radius is
   also what sizes the ground-warning circle, via `ResolveWarningRadius`).
2. Author a ground-warning `TelegraphPrefab` - same shape any other `TelegraphManager` prefab uses: a
   root with `TelegraphFade` (wired to a `SpriteRenderer`), optionally a child sprite with
   `TelegraphGrow` for a fill-in animation. Assign it to a `GroundWarningTelegraphManager` instance
   placed once in the gameplay scene (`warningTelegraphPrefab` field) - reusing the project's existing
   `CircleTelegraph.prefab` (`Assets/_QuantumUser/Resources/QuantumViewAssets/Telegraph/`) works as a
   starting point.
3. Author a `MortarBarrage_Elite` `MortarBarrageDeliveryData` asset - `ShellCount = 3`,
   `AimedShellCount = 1`, `MinRandomOffset`/`MaxRandomOffset` tuned (e.g. 2 / 6 - both must be
   non-zero or the "random" shells land on top of the aimed one), `ProjectileData` = step 1.
   Optionally also author `MortarBarrage_Normal` (same class, `AimedShellCount = 0` and/or fewer
   shells/lower damage) for a non-Elite mortar-type enemy.
4. Author an `EnemyActionData` asset per delivery asset above - `Delivery` = step 3, `Damage` (per
   shell), `EngageRange` long (backline/ranged behavior), `Origin = TargetAnchor`,
   `Trigger.Type = Cooldown`, `CooldownTime`/`AnticipationTime`/`TelegraphStartPercent` tuned for a
   visible caster-side "charging up" windup that stacks with the new per-shell ground warnings rather
   than replacing them (or set `TelegraphStartPercent = 1` to skip the caster-side windup telegraph
   entirely and rely purely on the per-shell ground warnings - if authoring one, use `Shape = Circle`;
   several other `TelegraphShape` values, including the tempting-sounding `LandingMarker`, are declared
   but have no rendering implementation in `EnemyAttackVisualsView.ComputeTelegraphPose`).
5. Author a `MortarElite` `EnemyDataAsset` - `Tier = EnemyTier.Elite`, `EnemyName`, `ViewPrefab`,
   `Actions.SkillActions = [MortarBarrage_Elite]` (plus a basic melee/fallback action for close range).
   If a Normal-tier variant is wanted too, author a second `EnemyDataAsset` with
   `Tier = EnemyTier.Normal` and `Actions.SkillActions = [MortarBarrage_Normal]`.
6. Wire `MortarElite` (and any Normal variant) into a `SurvivalPhase.AllowedEnemies` entry or a new
   `EnemyGroupConfig` - Elite groups automatically get the chunk-connectivity-gated placement already
   built for Elite+ tiers, no extra work needed there.
7. To give the pre-existing single-shot `MortarEnemy` the same ground telegraph, just tick
   `ShowLandingWarning` on its own `GrenadierProjectileDelivery` (`ProjectileDeliveryData`) asset - no
   other change needed, since its `GrenadierProjectileDataAsset.Hit` is already an `AreaHitData`.

## Bugs found (live in-Editor testing against `EliteMortarEnemy.asset`)

- **Shells spawned at world origin and detonated instantly.** `ProjectileSpawner.SolveArcLaunch` (a
  bare math helper) only fills `Velocity`/`IsValid` - it never sets `ProjectileLaunch.SpawnPosition`
  (that's normally done by `ProjectileMovementData.GetLaunchToTarget` afterward). An early version of
  this delivery called `SolveArcLaunch` directly, so `SpawnPosition` stayed at its default `(0,0,0)`,
  and `ProjectileSpawner.Spawn`'s `transform->Position = launch.SpawnPosition` spawned every shell at
  the world origin instead of at the enemy - which then instantly detonated against whatever geometry
  happened to be there (`AreaHitData.DetonateOnLevelGeometry`), reading as "the projectile never
  appears / disappears instantly". The same latent bug was found (and fixed) in
  `ProjectileDeliveryData.cs`'s own `UseArc == true` branch and both `UseArc` branches of
  `FanProjectileDeliveryData.cs` - neither had ever been exercised in production before (every existing
  arc-shot enemy uses `UseArc = false` + a `BallisticProjectileMovementData` instead, which doesn't hit
  this path), so the bug had sat there latent.
- **Landing point could drift from where the shell actually lands.** The same early version also had
  its own `LaunchAngle`/`Gravity` fields, duplicating the exact same tuning
  `BallisticProjectileMovementData` already owns. The launch was solved against the delivery's own
  `Gravity`, but the projectile's actual in-flight curve (`BallisticProjectileMovementData.
  UpdateVelocity`, applied every tick) used whatever `Gravity` was authored on the *movement asset*
  instead - two independently-authored copies of the same number, only correct if kept in sync by
  hand. Fixed by removing those fields entirely and routing the launch through the assigned
  `ProjectileData.Movement` itself - `LaunchAngle`/`Gravity` now live in exactly one place.
- **The ground-warning decal could sit a few pixels off the real landing point.**
  `GroundWarningTelegraphManager` originally positioned the marker straight off the simulation's own
  `Position` - but (same as `EnemyAttackVisualsView.SnapToGround` already documents for the enemy's
  own windup telegraph) the simulation's deterministic idea of ground height doesn't necessarily match
  the Unity-rendered ground mesh exactly, and under this game's tilted top-down camera even a small Y
  mismatch projects onto screen as a visible XZ offset. Fixed by adding the identical real-
  `Physics.Raycast`-against-the-`Ground`-layer snap before positioning the marker.

## Known simplification

`AimedShellCount` shells land exactly on `Enemy.SkillTargetPosition` as captured whenever
`AimLock`/`OnAnticipating` last updated it during the windup (`LocksAtTelegraphEnd`, the default,
tracks all the way up to the instant `Begin()` fires - the latest this framework's aim-lock timing
options allow). It does not predict/lead the target's future position - by the time the shell's own
(real, arc-derived) flight time elapses, a target that kept moving normally will usually no longer be
standing there, which is the intended fairness contract, not a bug to fix.

`GroundWarningTelegraphManager`'s countdown (the coroutine timing `FadeOutAndRelease`) runs on plain
real time (`WaitForSeconds`), not tied to the enemy's own anticipation-slow multiplier the way
`TelegraphGrow`'s growth rate is when driven from `EnemyAttackVisualsView` (there, `EntityRef.None` is
passed deliberately - this marker has no owning enemy to read a freeze/slow status from). If the whole
`GameplaySystemGroup` is ever paused while a shell is mid-flight (e.g. a Level-Up screen opening at
exactly the wrong instant), the real projectile's simulation-tick flight freezes along with it but this
View-side timer keeps counting in real time regardless, so the warning could visually resolve slightly
out of sync with the actual (paused) impact in that specific edge case. Not fixed here - the same
tolerance level most of this codebase's other View-side cosmetic timers already accept.
