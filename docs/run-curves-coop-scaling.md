# Run Curves & Co-op Scaling

Time-based difficulty ramp ("run curves") and player-count-based scaling ("co-op scaling"), with
three consumers: combined with the pre-existing per-`EnemyTier` HP baseline
(`EnemyTierStatsConfig.MaxHealth`) into a single HP/damage-multiplier snapshot taken once per
spawned enemy (`EnemyBalanceUtility.ResolveEnemyStats`) - the damage half of which is then actually
applied to every hit an enemy lands, via `HitEffectUtility.ScaleByEnemyDamageMultiplier`; applied
every Director pulse to scale `DirectorBudget` accumulation with the run and with live player count
(`CombatDirectorUtility.ResolveBudgetMultiplier`); and applied to the per-level XP requirement to
scale it with live player count (`ExperienceUtility.ResolveXpRequirementMultiplier`). Lives entirely
in `Assets/_QuantumUser/Simulation/Balance/`.

## Design

A **run curve** is a 7-anchor lookup (minutes 0/2/4/6/8/10/12), linearly interpolated between
anchors and clamped flat outside that range - so difficulty keeps ramping smoothly through a run
and caps rather than extrapolating past 12 minutes. All 4 channels are stored as one array of
`RunCurveAnchor` rows (`BalanceConfig.Curves`) - one row per anchor minute, every channel's value
at that point in the run sitting together - rather than 4 separate parallel `FP[7]` arrays, which
made "what's `EnemyDmg` at 6 minutes" a matter of counting index positions across unrelated fields.
A **co-op scaling row** is a flat 4-value lookup (`P1`..`P4`) keyed by live player count, clamped to
`[1,4]`.

Both are authored on one consolidated `BalanceConfig : AssetObject` asset (a single `AssetRef` on
`RuntimeConfig`, deliberately not one-ScriptableObject-per-table) rather than split across several
small config assets - see "Consolidation" below.

Everything is `FP`, never `float`/`double`/`AnimationCurve`. Fractional constants use
`FP.FromString("...")` (`FP` has no implicit conversion from a C# decimal literal, by design, to
avoid non-deterministic float literals in simulation code) - whole numbers use plain int literals
via `FP`'s implicit int conversion.

### Curve channels (`CurveChannel`)

| Channel | Anchors (0/2/4/6/8/10/12 min) | Consumer today |
|---|---|---|
| `EnemyHp` | 1.0 / 1.6 / 2.5 / 3.6 / 5.0 / 6.5 / 6.5 | `EnemyBalanceUtility.ResolveEnemyStats` |
| `EnemyDmg` | 1.0 / 1.1 / 1.2 / 1.35 / 1.5 / 1.6 / 1.6 | `EnemyBalanceUtility.ResolveEnemyStats` |
| `DirectorBudget` | 1.0 / 1.8 / 2.8 / 4.0 / 5.5 / 7.0 / 7.0 | `CombatDirectorUtility.ResolveBudgetMultiplier` |
| `ExpectedPlayerDps` | 1.0 / 2.0 / 3.5 / 5.5 / 9.0 / 13.5 / 13.5 | **none** - reserved, no consumer yet |

### Co-op global multipliers (`CoopGlobalKey`, `BalanceConfig.CoopGlobal`)

| Key | P1 / P2 / P3 / P4 | Consumer today |
|---|---|---|
| `EnemyDamage` | 1.00 / 1.00 / 1.05 / 1.10 | `EnemyBalanceUtility.ResolveEnemyStats` |
| `DirectorBudget` | 1.00 / 1.70 / 2.40 / 3.00 | `CombatDirectorUtility.ResolveBudgetMultiplier` |
| `EliteFrequency` | 1.00 / 1.60 / 2.20 / 2.80 | **none** - reserved, no consumer yet |
| `XpRequirement` | 1.00 / 1.60 / 2.20 / 2.80 | `ExperienceUtility.ResolveXpRequirementMultiplier` |

`docs/survival-director.md` documents "Milestone 7 (Co-op Scaling)" as "living player count →
budget/target-pressure/cap multipliers." Only the **budget** half is implemented here - the
`DirectorBudget` curve and co-op row are multiplied together in
`CombatDirectorUtility.ResolveBudgetMultiplier` and applied to `phase.BudgetPerPulse` before it
accumulates into the `DirectorBudget` global (`CombatDirectorUtility.TryPulse`). Target-pressure and
alive-cap scaling (`SurvivalPhase.TargetPressure`/`MaxAliveEnemies`) are **not** implemented - there's
no `CoopGlobalKey` for either yet, and no consumer.

`XpRequirement` has no paired run curve (unlike `EnemyHp`/`EnemyDmg`/`DirectorBudget`) - the
per-level XP curve already lives on `ExperienceConfig.RequiredExperience`
(`FPAnimationCurve`, X = display level), so this only needs the player-count multiplier, applied on
top of that curve's result in `ExperienceUtility.Grant`. See docs/experience-drops.md for the
leveling mechanic itself.

`ExpectedPlayerDps`/`EliteFrequency` remain pure reserved lookup data - neither maps onto an
existing mechanism in this codebase (`EliteFrequency` has no "elite spawn frequency" knob anywhere
in the Director's group-selection logic to hook into yet).

### Enemy HP baseline + co-op HP scaling (`CoopHpRow`)

The per-`EnemyTier` HP baseline is **not** duplicated in `BalanceConfig` - `EnemyBalanceUtility.
ResolveEnemyStats` reads it straight from the pre-existing `EnemyTierStatsConfig.MaxHealth`
(`EnemyTierStatsConfig.Resolve(f, tier)`), the same baseline already read everywhere else in the
codebase (`EnemyGroupConfig.ComputeCost`, `ExperienceUtility.TrySpawnDrop`, etc). An earlier version
of this feature added a second, parallel `EnemyRow.BaseHp` table that shadowed
`EnemyTierStatsConfig.MaxHealth` for spawned enemies only - that duplication was removed once it
was clear nothing justified two separate places to author the same number; single source of truth.

`BalanceConfig` still carries its own per-tier `CoopHpRow` multiplier table (`P1`..`P4` per tier,
array + linear match by `Tier` field, never `(int)tier` indexing) - this one genuinely is new data,
not a duplicate of anything:

| Tier | P1 / P2 / P3 / P4 |
|---|---|
| Filler / Normal / Specialist | 1.00 / 1.15 / 1.25 / 1.35 |
| Heavy | 1.00 / 1.35 / 1.60 / 1.85 |
| Elite | 1.00 / 1.50 / 1.90 / 2.30 |
| Boss | 1.00 / 1.70 / 2.20 / 2.60 |

### Consolidation

A single `BalanceConfig` asset holds the run curves and both co-op multiplier tables together,
rather than one `AssetObject` per table - keeps `RuntimeConfig` down to one `AssetRef` for this
whole feature instead of accumulating a new one per lookup table.

## `ResolveEnemyStats`

```csharp
EnemyRuntimeStats ResolveEnemyStats(Frame f, EnemyTier tier)
{
    BalanceConfig balance = f.FindAsset(f.RuntimeConfig.BalanceConfig);

    FP elapsedSeconds = f.Global->SurvivalTime;   // NOT Time.time / f.Number*f.DeltaTime
    int playerCount = f.PlayerCount;

    FP baseHp  = EnemyTierStatsConfig.Resolve(f, tier).MaxHealth;
    FP curveHp = balance.Evaluate(CurveChannel.EnemyHp, elapsedSeconds);
    FP coopHp  = balance.GetCoopHp(tier, playerCount);

    FP curveDmg = balance.Evaluate(CurveChannel.EnemyDmg, elapsedSeconds);
    FP coopDmg  = balance.GetCoopGlobal(CoopGlobalKey.EnemyDamage, playerCount);

    return new EnemyRuntimeStats
    {
        MaxHp = FPMath.RoundToInt(baseHp * curveHp * coopHp),
        DamageMultiplier = curveDmg * coopDmg,
    };
}
```

Called exactly once per spawn, from `EnemySystem.SeedFromEnemyData`, and the result fans out to two
seed methods - `SeedHealth` (writes `Health.MaxHealth`/`CurrentHealth`, unchanged downstream logic
otherwise) and the new `SeedCombatModifiers` (writes `EnemyCombatModifiers.DamageMultiplier`).
Neither is ever re-evaluated later - no re-scaling on phase advance or player join/leave, since a
healthbar or damage number changing retroactively on a living enemy would look broken.

### Worked example (acceptance check)

Filler (`EnemyTierStatsConfig.Filler.MaxHealth = 20`) at `SurvivalTime = 360` (6 min, lands exactly
on the `EnemyHp` curve's own 360s anchor - no interpolation drift), `PlayerCount = 4`:
`MaxHp = RoundToInt(20 × 3.6 × 1.35) = RoundToInt(97.2) = 97`.

Same Filler at `SurvivalTime = 0`, `PlayerCount = 1`: `MaxHp = RoundToInt(20 × 1.0 × 1.0) = 20` -
unscaled baseline, exactly.

### How `DamageMultiplier` actually reaches a hit

`EnemyCombatModifiers.DamageMultiplier` is snapshotted at spawn (above) but not applied there -
enemy attack damage isn't stored on the enemy entity itself, it's read fresh off `EnemyActionData.
Damage` by whichever delivery type fires (melee/area/beam/projectile - see `Assets/Enemy/Actions/
Delivery/`). Rather than scale `action.Damage` at each of those ~10+ call sites individually,
`HitEffectUtility.ApplyToTarget` (both overloads) - the single funnel every one of them ultimately
calls, `context.Owner` still the attacking enemy, whether the hit came from a manually-built
`HitEffectContext` (melee/area/beam deliveries) or a spawned `Projectile` resolving its own hit
later (`ProjectileHitData.ApplyEffects`, which preserves `projectile->Owner` through to the same
call) - scales `context.Damage` once, right at the top, before any `HitEffectData` (e.g.
`DamageEffectData`) reads it:

```csharp
FP ScaleByEnemyDamageMultiplier(Frame f, EntityRef owner, FP damage)
{
    if (f.Unsafe.TryGetPointer<EnemyCombatModifiers>(owner, out var modifiers) == false)
        return damage;   // no-op for a player-owned hit, or an enemy prototype missing the component

    return damage * modifiers->DamageMultiplier;
}
```

`TryGetPointer` finding nothing is the expected case for every player-dealt hit (players never
carry `EnemyCombatModifiers`) and for an enemy prototype that hasn't had the component added yet -
both correctly no-op rather than error.

## `ResolveBudgetMultiplier`

```csharp
FP ResolveBudgetMultiplier(Frame f)
{
    BalanceConfig balance = f.FindAsset(f.RuntimeConfig.BalanceConfig);
    if (balance == null) return FP._1;   // graceful no-op, logs an error

    FP curveMultiplier = balance.Evaluate(CurveChannel.DirectorBudget, f.Global->SurvivalTime);
    FP coopMultiplier = balance.GetCoopGlobal(CoopGlobalKey.DirectorBudget, f.PlayerCount);

    return curveMultiplier * coopMultiplier;
}
```

Called every pulse from `CombatDirectorUtility.TryPulse`, right where `phase.BudgetPerPulse`
already accumulates into the `DirectorBudget` global - `f.Global->DirectorBudget +=
phase.BudgetPerPulse * ResolveBudgetMultiplier(f);`. Unlike `ResolveEnemyStats`, this isn't a
one-time spawn snapshot - it's recomputed every pulse (every `phase.PulseInterval` seconds), which
is correct here since `DirectorBudget` is a continuously-accumulating global, not a per-entity
value baked once. A missing `BalanceConfig` degrades to `1x` (Director keeps running on
`BudgetPerPulse` alone) rather than halting the whole Director, unlike `SurvivalConfig`/
`DirectorConfig`/`LifecycleConfig`, which `CombatDirectorSystem` still hard-requires.

## `ResolveXpRequirementMultiplier`

```csharp
FP ResolveXpRequirementMultiplier(Frame f)
{
    BalanceConfig balance = f.FindAsset(f.RuntimeConfig.BalanceConfig);
    if (balance == null) return FP._1;   // graceful no-op, logs an error

    return balance.GetCoopGlobal(CoopGlobalKey.XpRequirement, f.PlayerCount);
}
```

Called from `ExperienceUtility.Grant`, multiplied into `config.RequiredExperience.Evaluate(...)`
right where the level-up while-loop compares it against `f.Global->TotalExperience`:

```csharp
FP xpRequirementMultiplier = ResolveXpRequirementMultiplier(f);

while (f.Global->Level + 1 < config.MaxLevel
       && f.Global->TotalExperience >= config.RequiredExperience.Evaluate(f.Global->Level + 2) * xpRequirementMultiplier)
{
    f.Global->Level++;
}
```

Resolved once per `Grant` call (not per while-loop iteration - player count can't change mid-call).
Since `TotalExperience` is one shared co-op run total (not per-player, see `Experience.qtn`), more
players killing in parallel fills it faster - this multiplier raises the threshold to compensate,
same rationale as `EnemyDamage`/`DirectorBudget` scaling up with player count. Same graceful-`1x`
missing-`BalanceConfig` fallback as the other two consumers.

## Files

- `Assets/_QuantumUser/Simulation/Balance/BalanceConfig.cs` - `CurveChannel`/`CoopGlobalKey` enums,
  `RunCurveAnchor`/`CoopGlobalRow`/`CoopHpRow` classes, `BalanceConfig : AssetObject` (curves +
  both co-op tables + `Evaluate`/`GetCoopGlobal`/`GetCoopHp`).
- `Assets/_QuantumUser/Simulation/Balance/EnemyBalanceUtility.cs` - `EnemyRuntimeStats` struct,
  static `ResolveEnemyStats` (reads `EnemyTierStatsConfig.MaxHealth` as the HP baseline).
- `Assets/_QuantumUser/Simulation/QTN/Enemy/EnemyCombatModifiers.qtn` - new component,
  `FP DamageMultiplier`, hand-authored on prototypes (not dynamically added).
- `Assets/_QuantumUser/Simulation/Systems/Enemy/EnemySystem.cs` - `SeedFromEnemyData` resolves
  stats once; `SeedHealth` takes the resolved `stats` instead of reading `EnemyTierStatsConfig`
  directly; new `SeedCombatModifiers`.
- `Assets/_QuantumUser/Simulation/Default/RuntimeConfig.User.cs` - new
  `AssetRef<BalanceConfig> BalanceConfig`.
- `Assets/_QuantumUser/Simulation/Systems/Director/CombatDirectorUtility.cs` - `TryPulse` applies
  `ResolveBudgetMultiplier(f)` to `phase.BudgetPerPulse` before it accumulates into
  `f.Global->DirectorBudget`.
- `Assets/_QuantumUser/Simulation/Systems/ExperienceUtility.cs` - `Grant` applies
  `ResolveXpRequirementMultiplier(f)` to `config.RequiredExperience.Evaluate(...)` in the level-up
  while-loop condition.
- `Assets/_QuantumUser/Simulation/Systems/HitEffectUtility.cs` - both `ApplyToTarget` overloads
  scale `context.Damage` via `ScaleByEnemyDamageMultiplier` before any `HitEffectData` reads it -
  the single funnel every enemy delivery type's damage passes through.

## Current status / Editor authoring needed

**Update (2026-08-07):** both authoring gaps below are resolved - verified against the actual
project files, not just this doc's own prior claims.

- ~~No `BalanceConfig.asset` instance exists yet~~ - resolved. `BalanceConfig.asset` exists at
  `Assets/_QuantumUser/Resources/Configs/BalanceConfig.asset` and is assigned to
  `RuntimeConfig.BalanceConfig` in both `MenuScene.unity` and `QuantumGameScene.unity`. All three
  consumers (`ResolveEnemyStats`, `ResolveBudgetMultiplier`, `ResolveXpRequirementMultiplier`) now
  resolve real curve/co-op data instead of degrading to `1x`.
- ~~`EnemyCombatModifiers` has not been added to any `EntityPrototype` yet~~ - resolved. It's on
  `Assets/_QuantumUser/Entities/Enemies/GenericEnemyPrefab.prefab` (the shared generic enemy
  prototype, referenced by `DirectorConfig.EnemyPrototype` - this prefab was renamed from
  `BasicEnemy` since this doc was first written) as `QPrototypeEnemyCombatModifiers`, authored
  `DamageMultiplier = 1` (a correct baseline - `SeedCombatModifiers` overwrites it with the real
  resolved value at spawn). `HitEffectUtility.ScaleByEnemyDamageMultiplier` now finds the component
  and actually scales enemy damage per the `EnemyDmg` curve/`EnemyDamage` co-op row.
- `ExpectedPlayerDps`/`EliteFrequency` channels/rows remain unconsumed - see "Curve
  channels"/"Co-op global multipliers" above for why each is still reserved.
