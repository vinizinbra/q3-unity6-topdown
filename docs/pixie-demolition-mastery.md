# Pixie — Demolition Mastery Hero Traits

A 4-trait Hero Trait pool for Pixie (Direct Hit / Concussive Force / Volatile Payload / Mini
Ordnance), mirroring Max's "Fire Mastery" pool (`docs/max-vendetta-fire-mastery.md`). All 4 react to
**any** of Pixie's explosions - her Bunny Bomb Hero Skill, Dash Ascension bombs, explosive weapon
perks - not one specific mechanic, by hooking the two shared choke points every explosion in the game
funnels through: `HitEffectUtility.ApplyInRadius` (bomb-type blasts) and
`HitEffectUtility.ApplyDamageInRadius` (weapon-perk-type blasts, reached via `ApplyExplosion`), both
ending at `DamageUtility.ApplyDamage`.

**Explicitly separate from, and never touches:** `ClusterBombUpgrade`/`ClusterBombSkillAction`/
`AreaHitData.TrySpawnClusterBomblets` - Pixie's pre-existing Hero Skill Upgrade (deterministic
Projectile-based bomblet count from her Bunny Bomb specifically). Mini Ordnance below is a
*different* mechanism (a chance-based Mini Bomb drop from any explosion) and was given a
deliberately distinct component/class name (`MiniOrdnanceUpgrade`, not anything containing
"Cluster") after a naming mix-up between the two happened once already this session.

## The 4 traits

- **Direct Hit** (`DirectHitUpgrade`) - a binary inner zone: within `InnerRadiusFraction` (default
  0.35) of an explosion's own radius, damage is multiplied by `1 + DamageMultiplierBonus`.
- **Concussive Force** (`ConcussiveForceUpgrade`) - an arcade-style falloff, not a strict physical
  one: full `Force` out to `InnerRadiusFraction` (default 0.5 - a generous sweet spot, not a
  pinpoint), then a linear taper to 0 at the blast edge. `EliteMultiplier` (<1) further reduces the
  result against Elite-tier enemies. **Bosses need no dedicated handling at all** - confirmed via
  this session's research that `BossRuntimeState.StaggerMeter` (`BossSystem.TickStagger`, ticks off
  raw Health-diff damage taken, any source) and `TierStats.CanBeInterruptedByKnockback`/
  `KnockbackMultiplier` (per-tier physical resistance) already give bosses exactly "resist
  displacement, build stagger toward a forced break" for free, regardless of what dealt the damage.
- **Volatile Payload** (`VolatilePayloadUpgrade`) - a critical hit that is *also* an explosion
  applies Burn to whatever it crit. `BurnIntensity` is a flat damage-per-tick value (same convention
  `StatusSpreadOnDeath.BurnIntensity` already uses), not a percent-of-hit scale.
- **Mini Ordnance** (`MiniOrdnanceUpgrade`, DisplayName "Cluster Charges") - any qualifying
  explosion rolls `Chance` to drop a stationary Mini Bomb (`ExplodeOnDestroy`/`AreaOwner`/
  `DestroyAfterTime` - see `docs/explode-on-destroy.md`) at the blast center. `Damage`/`Explosion`
  are flat/authored values, not multipliers of the parent blast, matching `ExplodeOnDestroy.Damage`'s
  own convention - the `Explosion` `AreaHitData` asset's own (smaller) `BlastRadius` is what makes
  this weaker than the parent, no runtime radius math needed.

## Architecture

- **Direct Hit / Concussive Force** are resolved inline, per target, inside the two shared radius-hit
  loops - a new shared helper, `DemolitionMasteryUtility.ApplyProximityEffects` (`Assets/
  _QuantumUser/Simulation/Systems/Heroes/Pixie/DemolitionMasteryUtility.cs`), computes the
  distance-from-center fraction once and resolves both from it. Called from `HitEffectUtility.
  ApplyInRadius` (mutates `context.Damage` before the Effects list ever reads it) and `Apply
  DamageInRadius` (mutates a local copy before calling `ApplyDamage`, with one extra
  `isExplosion`-gated `Transform3D` lookup added there since that method didn't previously need
  target position at all). Strictly opt-in (`TryGetPointer` on the owner's own component) - zero
  behavior change for every other hero/mechanic that reaches these same two widely-shared methods
  (Vortex, Sentries, every other hero's weapon perks).
- **Volatile Payload / Mini Ordnance** react to two new signals in `Combat.qtn`:
  - `OnExplosionCriticalHit(EntityRef target, EntityRef owner, FP damage, DamageSource source)` -
    fired from `DamageUtility.ApplyDamage` right alongside the existing `OnCriticalHit`, additionally
    gated on `isExplosion == true`. A dedicated sibling signal rather than an extra parameter on
    `OnCriticalHit` itself, so every existing subscriber (Flashpoint, weapon perks, Rift Mutations) is
    unaffected.
  - `OnAreaExplosionDetonated(EntityRef owner, FPVector3 center, FP radius, DamageSource source)` -
    fired once per genuine radius blast, only from the two *original* explosion sources
    (`AreaHitData.Detonate` and `HitEffectUtility.ApplyExplosion`). **Deliberately never fired from
    `ExplodeOnDestroyUtility.TryDetonate`** (a Mini Bomb's own detonation) - this alone is what makes
    "Mini Bombs cannot generate additional Cluster Charges" true, no depth-tracking/gating needed.
    (Direct Hit/Concussive Force still apply to a Mini Bomb's own blast "for free," since they hook
    `ApplyInRadius` itself, which `TryDetonate` already calls - only Mini Ordnance's own recursive
    spawn is excluded.)
  - Both consumed by one new system, `PixieDemolitionMasterySystem` (`Assets/_QuantumUser/
    Simulation/Systems/Heroes/Pixie/PixieDemolitionMasterySystem.cs`), mirroring
    `MaxFireMasteryReactionSystem`'s shape (a hero's whole trait pool in one file). Mini Ordnance's
    spawn is a 4-line composition of `SpawnedEntitySpawner.Spawn` (already stamps `AreaOwner`/
    `DestroyAfterTime` unconditionally) + `f.AddOrGet<ExplodeOnDestroy>` - no new spawner class.

## Current status

- Code compiles once Quantum's DSL codegen picks up `Combat.qtn`/`DemolitionMastery.qtn` (open the
  Editor, or see CLAUDE.md's "Quantum `.qtn` codegen gotcha" for the headless path).
- `Tools/RiftRaiders/Pixie/Generate Demolition Mastery Assets` (also chained into `Generate All
  Assets`) authors all 4 `.asset` instances and appends them to `PixieCharacterData.PassiveUpgrades`
  - **append-only**, unlike `PixieChainReactionAssetGenerator`'s own `WireCharacterData`, which fully
  replaces that list; running both leaves all 8 entries (4 Chain Reaction + 4 Demolition Mastery)
  intact regardless of order.
- `MiniOrdnance.asset`'s `MiniBombPrototype`/`Explosion` fields are left unassigned by the generator -
  same authoring gap every other feature in this codebase has. Needs a minimal stationary
  `EntityPrototype` (`Transform3D` only, no `PhysicsCollider3D`/movement data) and an `AreaHitData`
  asset with a small `BlastRadius`, neither of which a generator can author (see
  `docs/explode-on-destroy.md`'s own note on this same gap for `DashBomb.prefab`).
