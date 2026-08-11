# Explode-On-Destroy / Mini Bomb Entity Shape

**Cluster Charge status: reverted, unchanged from before this doc existed.** Pixie's Cluster Charge
(`ClusterBombUpgrade`/`ClusterBombSkillAction`) was briefly rebuilt on a new stationary "Mini Bomb"
entity shape, then reverted back to its original, already-working `Projectile`-based bomblet
implementation at the user's explicit request - it had already been tuned and tested, and converting
it wasn't wanted. `AreaHitData.TrySpawnClusterBomblets` once again spawns real `Projectile` bomblets
via `ProjectileSpawner`, exactly as it did originally. **Do not redirect Cluster Charge onto Mini
Bomb/`ExplodeOnDestroy` again without being asked.**

What *did* stay, because a different, separately-authored feature already depends on it and works:

## `ExplodeOnDestroy` - a generic "detonate when destroyed" primitive

A stationary delayed-effect entity - no movement/velocity/collision/trajectory of its own - for
anything that's just "spawn here, wait out a fuse (or take a killing hit), explode." Not its own
component beyond one small addition; built on components that already existed:

- **The fuse is `DestroyAfterTime`**, not a bespoke timer - `DestroyAfterTimeSystem` already ticks a
  lifetime down and destroys on expiry for every spawned area/orb/sentry in the game.
- **Ownership is `AreaOwner`**, not a second Owner/Source/Element trio - the same component `Vortex`
  already reuses. `SpawnedEntitySpawner.ConfigureOwnerAndArea` (renamed from `ConfigureArea`) stamps
  it unconditionally on every spawn rather than gating it behind AreaDamage/Vortex/ExplodeOnDestroy
  specifically - that allowlist would only ever keep growing as more things want to read "who owns
  this," so it was removed. Every existing caller (`SpawnEntitySkillAction`, `SpawnEntityEffectData`)
  is unaffected since they never read an absent `AreaOwner` anyway.
- **The new component is `ExplodeOnDestroy`** (`Damage`/`SpawnDepth`/`AssetRef<AreaHitData> Explosion`)
  - checked from **two** independent trigger points, both a plain optional `TryGetPointer` rather than
  a per-feature branch bolted onto either system:
  - `DestroyAfterTimeSystem`, when a timed lifetime runs out.
  - `DamageUtility.ApplyDamage`'s non-Enemy death branch, when `Health` reaches 0 from taking damage.
    This is what makes a **decoy-trap** variant possible: author the prototype with `Health` seeded
    to 1, a `Decoy` tag (see `EnemyMovementUtility.TryFindNearestDecoy` - both an Idle enemy's initial
    target pick and an already-chasing enemy get pulled onto the nearest Decoy, pre-existing
    mechanic, zero new code needed), and a real `PhysicsCollider3D` **on the same physics layer as a
    real player** (see `Decoy.qtn`'s own comment - enemy attack hit-connect checks re-query "nearest
    thing on the Player layer" independently of targeting, so a collider on the wrong layer means
    enemies path to it but their attacks don't land) so attacks can actually connect. Enemies aggro
    onto it like a normal decoy, and the instant one lands the killing hit, it detonates.

  Both trigger points call the same shared `ExplodeOnDestroyUtility.TryDetonate` rather than two
  copies of the same logic - it mirrors `AreaHitData.Detonate`'s own explosion call
  (`HitEffectUtility.ApplyInRadius` + `AreaDetonated`) without needing a `Projectile*`, which nothing
  carrying `ExplodeOnDestroy` ever has, including the same Unstable Mixture/Skill Area radius scaling
  a directly-thrown bomb gets - so the view layer (`EffectsManager`) needs zero new code either way.
  `AreaHitData.Detonate`'s public overload also takes an optional `radiusMultiplier` parameter
  (default 1) and now returns the final resolved radius - `TryDetonate` is the only caller that ever
  passes a real `radiusMultiplier`, for Birthday Cake's own rank 2 blast-radius bonus on her landed
  bomb specifically, and reads the returned radius back to run its own `ForceMarkOnDetonate` sweep for
  Backblast rank 3 (see `docs/pixie-ascensions.md`); every other `ExplodeOnDestroy` user (Mini Bomb) is
  unaffected either way.

**Current users:** Pixie's **Backblast** Ascension (reworked 2026-08-09 from an instant blast to a
dropped, fused bomb - `docs/pixie-ascensions.md`) drops a bomb at the dash's start/end via
`SpawnedEntitySpawner.Spawn` + `f.AddOrGet<ExplodeOnDestroy>{TriggersSpawnUpgrades: true}`, pointed at
`BombPrototype`/`Explosion` fields still needing Editor authoring - `DashBomb.prefab`
(`Resources/Skills/Pixie/Pixie_HeroSkillUpgrades/`), a stationary prototype with a real collider kept
as a working reference `ExplodeOnDestroy` prototype since before Backblast existed in this shape, is an
existing candidate to point those fields straight at. Pixie's **Pocket Bombs** Ascension
(`docs/pixie-ascensions.md`) is the other user - any qualifying Pixie explosion has a chance to spawn a
stationary Mini Bomb the same way, `MiniBombPrototype`/`Explosion` similarly still needing Editor
authoring (see that doc's own "Current status"). Any future "spawn something that explodes when it's
destroyed" feature reuses this same component/hook for free.

## Files

- `Assets/_QuantumUser/Simulation/QTN/ExplodeOnDestroy.qtn` - the component itself.
- `Assets/_QuantumUser/Simulation/Systems/ExplodeOnDestroyUtility.cs` - the shared `TryDetonate`
  logic both trigger points call into.
- `Assets/_QuantumUser/Simulation/Systems/DestroyAfterTimeSystem.cs` - trigger point #1 (timed
  expiry); unchanged for every existing `DestroyAfterTime` user otherwise.
- `Assets/_QuantumUser/Simulation/Systems/DamageUtility.cs`'s `ApplyDamage`, non-Enemy death branch -
  trigger point #2 (killed by damage), right alongside the existing `TrySentryOverload` call.
- `Assets/_QuantumUser/Simulation/Systems/SpawnedEntitySpawner.cs` - `ConfigureOwnerAndArea` change
  described above.

## Known simplifications / current status

- **No recursive cascade.** `ExplodeOnDestroy`'s own explosion never triggers another spawn of
  anything - `SpawnDepth` is carried but unread, so wiring an opt-in recursive mode later doesn't
  need another data-plumbing pass.
- **`DashBomb.prefab` needs its component re-added in the Editor.** It was hand-authored with
  `ExplodeOnExpire` (this component's original name) before the rename to `ExplodeOnDestroy` - once
  Quantum's codegen regenerates, the old `QPrototypeExplodeOnExpire` reference goes "missing script."
  Re-add the component (now `QPrototypeExplodeOnDestroy`) and re-enter `Damage = 10` / `Explosion` =
  the same `AreaHitData` asset it already had. `Health`/`Decoy` (for the decoy-trap variant) aren't on
  it yet either.
- **Cluster Charge is untouched/original** - see the note at the top of this doc. Its own `.asset`
  instance (inside `PixieBaseSkill.asset`) still points at its original `Projectile` (the
  `MiniBombProjectileDataAsset` sub-asset, `Movement`+`Hit=ClusterBombDetonate` already configured) -
  nothing to author here, it already worked before any of this and still does.
