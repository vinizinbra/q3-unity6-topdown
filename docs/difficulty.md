# Difficulty Tiers (Easy / Medium / Hard / Nightmare N)

Selectable run difficulty built as **multipliers layered on top of the existing balance stack**,
not a parallel pipeline. Medium is the pre-difficulty tuning (every multiplier 1), so choosing
Medium, or having no `DifficultyConfig` assigned, is an exact no-op.

## Design

- **Selection**: `RuntimeConfig.Difficulty` (`DifficultyTier` enum: Easy, Medium, Hard,
  Nightmare) plus `RuntimeConfig.NightmareLevel` (int, clamped to ≥ 1, only read for Nightmare).
  It is set in the Inspector for now. Nothing picks it at runtime yet (see "Later").
- **Config**: `DifficultyConfig : AssetObject`. It has authored `Easy`/`Medium`/`Hard` rows and a
  per-channel `NightmareGrowth[]` (`PerLevel`, `Cap`).
  - **Nightmare N = Hard × PerLevel^N, clamped to Cap.** Nightmare always stacks on top of Hard,
    so NM1 is strictly harder than Hard.
  - HP and damage are uncapped; they are the endless axis.
  - Density, elite weight, windup, recovery, projectile speed, lead and move speed plateau at their
    `Cap`. This is for fairness and mobile perf (density drives MaxAlive).
  - `MaxMultiplier` (10000) bounds every channel so boss `MaxHealth` can't overflow Int32/FP at
    absurd levels.
  - The power is computed as an iterative FP multiply loop (deterministic, no float `pow`).
- **Snapshot**: `DifficultySystem.OnInit` resolves the tier once into
  `Global.Difficulty` (`DifficultySnapshot`, `Difficulty.qtn`). Everything reads that snapshot
  through `DifficultyUtility`, so there is no per-hit asset lookup. An all-zero snapshot reads as
  1, so a missing config means Medium.

### Channels

| Channel | Easy | Medium | Hard | NM ×/level | Cap | Consumer |
|---|---|---|---|---|---|---|
| EnemyHpLight (Filler/Normal) | 0.7 | 1 | 1.35 | 1.20 | – | `EncounterModifierUtility.ResolveEnemyHealthMultiplier` → `EnemySystem.SeedHealth` |
| EnemyHpHeavy (Specialist+, incl. Boss) | 0.7 | 1 | 1.45 | 1.25 | – | same |
| EnemyDamage | 0.6 | 1 | 1.3 | 1.15 | – | `ResolveEnemyDamageMultiplier` → `HitEffectUtility` |
| SpawnDensity | 0.8 | 1 | 1.15 | 1.06 | 1.6 | `ResolveSpawnDensityMultiplier` → budget accrual, MaxAlive, TargetPressure |
| EliteWeight | 0.7 | 1 | 1.3 | 1.15 | 3.0 | `ResolveWeightMultiplier` (Elite+ group/enemy rolls) |
| AnticipationSpeed | 0.8 | 1 | 1.15 | 1.05 | 1.5 | `EnemySystem.UpdatePreparation` (Preparation + Telegraph) |
| RecoverySpeed | 0.85 | 1 | 1.1 | 1.05 | 1.4 | `EnemySystem.UpdateRecovery` |
| EnemyProjectileSpeed | 0.85 | 1 | 1.15 | 1.05 | 1.5 | `ProjectileSpawner.Spawn` (Enemy owners only) |
| EnemyProjectileLead | 0.5 | 1 | 1.2 | 1.05 | 1.6 | Projectile/Fan/MortarBarrage delivery `LeadFactor`/`MaxLeadDistance` |
| EnemyMoveSpeed | 0.95 | 1 | 1.05 | 1.03 | 1.25 | `EnemySystem` move speed, `ProjectileAimUtility.ResolveLeadVelocity` cap |

Resolved values: NM1 HpHeavy ×1.81, NM5 ×4.43, NM10 ×13.5. All the capped channels are at their
cap by NM10. At these placeholder rates HP hits `MaxMultiplier` around NM45, so tune `PerLevel`
down before deep Nightmare matters.

The speed channels are **tick-rate multipliers** (>1 = shorter windup/recovery). They are consumed
exactly like `BossPhaseUtility`'s per-phase multipliers, at the same lines, composed with them.

**Deliberately not channels:** XP requirement and in-run coins. A harder tier shouldn't also slow
the build or starve the Store, because the difficulty should come from the enemies.

### Composition order

`EnemyTierStatsConfig` base × `BalanceConfig` run curve × co-op row × Rift Mutations × **difficulty**.

Difficulty multiplies in as its **own factor** inside `EncounterModifierUtility`. It is never
written into the additive `*Bonus` globals (`RunMutations.qtn`), because mutations add but
Nightmare compounds. One consequence: the "negative HP bonus is ignored for bosses" rule stays
scoped to mutations, so Easy does soften bosses.

### Projectile lead

`DifficultyUtility.ScaleEnemyLead` multiplies a delivery's authored `LeadFactor` and
`MaxLeadDistance` by `EnemyProjectileLead`.
- The factor is clamped to ≤ 1, since leading past the true intercept only makes a shot worse.
- `LeadFactor = 0` stays 0, so the multiplier never turns a non-leading shooter into a leading one.

The lead's flight-time estimate now uses `DifficultyUtility.ResolveEnemyShotSpeed` (delivery ×
boss phase × difficulty), which is the speed `ProjectileSpawner` really launches at. Before this,
it used only the delivery's own multiplier, so boss-phase projectile speed also over- or
under-led; that is fixed as a side effect.

### View

The windup visuals mirror the sim's windup rate, so they fold in `AnticipationSpeed` too:
- `TelegraphGrow.ResolveAnticipationMultiplier` (ground decal growth; `TelegraphFade` hands its
  duration to it);
- `EnemyBlobAnimationView`'s windup `dt` scale.

## Files

- `Assets/_QuantumUser/Simulation/QTN/Difficulty.qtn`: `DifficultyTier` enum, `DifficultySnapshot`,
  `Global.Difficulty`.
- `Assets/_QuantumUser/Simulation/Balance/DifficultyConfig.cs`: `DifficultyChannel`,
  `DifficultyRow`, `NightmareGrowthRow`, `DifficultyConfig` (`Resolve`, `BuildSnapshot`,
  `GetDisplayName`).
- `Assets/_QuantumUser/Simulation/Balance/DifficultyUtility.cs`: `Get`, `GetEnemyHp`,
  `GetEnemyProjectileSpeed`, `ScaleEnemyLead`, `ResolveEnemyShotSpeed`.
- `Assets/_QuantumUser/Simulation/Balance/DifficultySystem.cs`: one-shot `OnInit` snapshot,
  registered first in `SystemSetup.User.cs`.
- `Assets/_QuantumUser/Simulation/Default/RuntimeConfig.User.cs`: `Difficulty`, `NightmareLevel`,
  `DifficultyConfig`.
- Hooks:
  - `EncounterModifierUtility.cs`
  - `EnemySystem.cs` (move / windup / recovery)
  - `ProjectileSpawner.cs`
  - `ProjectileAimUtility.cs`
  - `ProjectileDeliveryData.cs`, `FanProjectileDeliveryData.cs`, `MortarBarrageDeliveryData.cs`
  - `TelegraphGrow.cs`, `EnemyBlobAnimationView.cs`
- Asset: `Assets/_QuantumUser/Resources/Configs/DifficultyConfig.asset`. It is assigned on
  `MatchMakingConfig.RuntimeConfig` (MenuScene) and `QuantumRunnerLocalDebug.RuntimeConfig`
  (GrasslandOutpostGameScene).

## Current status

- Code-complete and compiling. Numbers are placeholders, not yet play-tuned.
- Not yet verified in play at non-Medium tiers.
- Known gap: `EnemyBlobAnimationView`'s windup `dt` still ignores
  `BossPhaseUtility.ResolveAnticipationMultiplier`. This is pre-existing and untouched here.

## Later (explicitly out of scope for this pass)

- **Lobby selector + network transport.** The leader picks the tier, and it travels like the seed
  (the `SyncMatchRoom` payload plus a room property), written into `RuntimeConfig.Difficulty`/
  `NightmareLevel` before `CloneConfig` in `MatchMakingConfig.StartRunner`.
- **Unlock ladder / save.** Beating tier N unlocks N+1 (PlayerPrefs, next to the Talent keys).
- **Meta reward multiplier per tier** (there is no persistent meta wallet yet).
- **Nightmare rule modifiers** (e.g. every Kth level adds an elite affix; see the Elite affixes idea).
- Balance Simulator support (apply the same channels in `BalanceSimDirector`/`BalanceSimRunner`).
- Loading-screen / result-screen tier badge (`DifficultyConfig.GetDisplayName`). The in-match HUD
  label is done: `GameplayUiController.difficultyLabel`, set once in `QStart` from `RuntimeConfig`.
