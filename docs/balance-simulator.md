# Balance Simulator

Editor tool that predicts, **per minute and per hero**, what a run on a given config looks like -
player level, kills, weapon/skill DPS, weapon level and perks, coin income vs. spending, enemy HP vs.
time-to-kill, Director pressure - and diffs that prediction against a recorded real run.

Open it via `Tools > RiftRaiders > Balance > Balance Simulator`.

## Why an analytical simulator

There is no headless Quantum harness in the project (no test asmdef, no offline `SessionRunner`
bootstrap), and the bots pick level-up cards uniformly at random and never use the Blacksmith - so a
real-simulation bot run would not model a human's economy decisions anyway. The tool instead reads the
authored assets straight from the Editor (`AssetDatabase` + `QuantumUnityDB.GetGlobalAssetEditorInstance`)
and steps the run analytically. Nothing in `Assets/_QuantumUser/Simulation` changes.

## File map

`Assets/_QuantumUser/Editor/BalanceSimulator/`

| File | Role |
|---|---|
| `BalanceSimScenario.cs` | ScriptableObject "run config to test": which configs, party, duration, seeds, player-skill and decision-policy knobs. Create via `Create > RiftRaiders > Balance > Simulator Scenario` or the window's **New** button. |
| `BalanceSimAssets.cs` | Loads the scenario's configs (or the `Resources` defaults) and resolves `AssetRef`s. |
| `BalanceSimModel.cs` | `SimStats` (CharacterStats mirror), `SimWeapon` (weapon DPS math + perk/level application), `SimPlayer`, `SimEnemy`. |
| `BalanceSimSkillModel.cs` | Expected skill damage per activation from the authored `SkillData`/actions/effects. |
| `BalanceSimDirector.cs` | Mirror of `SurvivalProgressionUtility.Tick` + `CombatDirectorUtility.TryPulse/TrySelectSpawn` for a cohesive party. |
| `BalanceSimPolicy.cs` | Level-up rolls/picks and Breathing Break shopping (Store weapons, Blacksmith perks). |
| `BalanceSimRunner.cs` | Ticks a run, applies party damage, credits kills -> XP/coins, snapshots per minute; Monte-Carlo over seeds. |
| `BalanceSimReport.cs` | Column layout (`Col`), averaging, CSV/Markdown export, recorder CSV loading. |
| `BalanceSimulatorWindow.cs` | The EditorWindow. |

`Assets/_Project/Scripts/Balance/BalanceRunRecorder.cs` - runtime MonoBehaviour that samples the live
`Frame` once per minute into `Library/BalanceSim/actual_<date>.csv` with the same columns.

## What the model mirrors (with source)

- **Phase timeline** - `SurvivalConfig.Phases[]` exactly as `SurvivalProgressionUtility.Tick`: Combat
  advances `SurvivalTime`; Breathing freezes it and waits for alive == 0 before its `Duration` counts;
  Elite freezes it until the Elite dies; Boss ends the simulation (last row shows Boss HP/TTK); the
  last phase holds forever.
- **Director** - every `PulseInterval`: `Budget += BudgetPerPulse x BalanceConfig.DirectorBudget curve x
  CoopGlobal(DirectorBudget)`; up to `DirectorConfig.MaxPurchasesPerPulse x CoopGlobal(DirectorPressure)`
  purchases while alive cost < `TargetPressure x CoopGlobal(DirectorPressure)`; candidates filtered by
  Weight, Min/MaxSurvivalTime, cost <= budget, `MaxAliveEnemies x CoopGlobal(DirectorPressure)`,
  `MaxConcurrent`; one weighted roll over groups + direct enemies; phase-start
  `GuaranteedGroup`/`GuaranteedEnemyData`.
- **Enemy HP** - `RoundToInt(TierStats.MaxHealth x EnemyHp curve (Light for Filler/Normal, Heavy for Specialist+) x CoopHp(tier)) x Stats.HealthMultiplier`
  + tier shield x `Stats.ShieldMultiplier` (`EnemyBalanceUtility.ResolveEnemyStats` +
  `EnemySystem.SeedHealth/SeedShield`; every authored enemy has `ShieldMultiplier` 0 today).
- **Engagement** - a spawn lands on the `DirectorConfig.SpawnRingRadiusMin/Max` ring and walks in at
  its own `Stats.MoveSpeed`; until then it counts toward Director pressure but can't be damaged.
  This is what keeps the Director from re-buying every pulse the way instant kills would.
- **XP** - `TierStats.ExpValue` per kill x collector's `ExperienceGainMultiplier` into one shared total,
  each orb subject to the `OrbPickupEfficiency` curve (orbs expire after `ExperienceConfig.OrbLifetime`);
  level thresholds `ExperienceConfig.RequiredExperience.Evaluate(level) x DifficultyMultiplier x
  CoopGlobal(XpRequirement)` (`ExperienceUtility.GetRequiredExperience`).
- **Coins** - `TierStats.CoinValue` with `CoinDropChance` per kill; a collected coin orb credits
  EVERY player's own wallet (`CoinUtility.GrantAll`, each x their own `CoinGainMultiplier`) - wallets
  are independent, income is not - kill drops x `CoopGlobal(CoinGain)`; plus **barrels** (not
  discounted by the co-op row): `LevelConfig.ChunkPool[Enemy].Count` chunks x the pool-weighted
  expected `Breakable` spawns per chunk prototype, read from each chunk prefab's
  `Chunk.SpawnConfig` -> `ChunkSpawnConfig.Spawns` exactly as `TalentGateSystem.ResolveSpawn` would
  (talent-gated entries skipped, `Chance` 0 = always, one prototype drawn by weight), each worth the
  barrel's `BreakLootData` drop (`BreakableUtility.TrySpawnLoot`; first drop with >= 50% chance), x
  `BarrelBreakFraction`, spread evenly over the survival minutes. `BarrelsPerEnemyChunk` > 0
  overrides the per-chunk count. The window status line shows the resolved figures per chunk.
- **Weapon DPS** - `WeaponSystem.ResolveFireCooldown` + `StatUtility.GetFireCooldown` +
  `DamageUtility.ResolveOutgoingDamage`: sustained fire incl. reloads, pellets, weapon + character
  crit, `Weapon.DamageMultiplier` (perks + `WeaponSystem.AddLevel` compounding), x `HitEfficiency`.
- **Skill DPS** - expected damage per activation / max(cooldown / `SkillCooldownMultiplier`, active
  duration) x `DamageMultiplier x SkillDamageMultiplier` x character crit x `SkillUseEfficiency`.
  Dedicated models: `BerserkSkillData` (Max) = weapon fire-rate/reload buff with
  `Duration/Cooldown` uptime; `JuggernautSkillData` (Brute) = `Damage` per discharge x `AreaTargets`
  x `Duration/DischargeCooldownPerEnemy` x `ChannelContactUptime`; `ProjectileSkillData` = `Damage` x (area? `AreaTargets`)
  plus `SpawnAlternatingAreaEffectData` ticks (Zara) and spawned-entity durations (Kai vortex). All
  **Activated** actions on the skill add: any mounted `AssetRef<WeaponDataAsset>` (Lux sentry) as
  weapon DPS x duration, and any `Damage`/`DamageAmount` field x ticks x `AreaTargets`. The table's
  "Skill model" line says which path was used.
- **Skill Ascension ranks** - `BalanceSimSkillModel.EvaluateUpgrade` values each rank of a hero-skill
  action from its rank-indexed fields against the skill's basis (`ProjectileSkillData.Damage`,
  `JuggernautSkillData.Damage`, `SpawnSentrySkillAction.SkillDamage`): Kai Compression/Void Shards/
  Collapse (percent x pulses x targets), Pixie Cluster Bomb/Direct Hit/Birthday Cake, Brute Bone
  Breaker/Aftershock/Concussive Impact, Max Full Throttle (weapon damage x Berserk uptime), Zara
  Amplifier/Double Time/Main Stage, Lux Weapon Systems/Overclock/Overload Core; unknown actions use a
  generic `DamagePercent` rule. A rank with no quantifiable effect (Singularity, Momentum, Last
  Stand...) falls back to the flat `SkillUpgradeDpsValue`. Kai's baseline vortex therefore shows ~0
  skill DPS until Compression/Collapse/Shards are picked - that is the authored design, not a bug.
- **Level-ups** - category from `LevelUpConfig.LevelSequence[(level-1) % count]`, `ChoiceCount` cards
  drawn weighted by rarity (`LevelUpConfig.GetWeight`) from the same pools `LevelUpUtility` collects
  (weapon perks not equipped + fire-type match, Global Upgrades under `MaxPicks`, hero
  skill/dash/passive ranks), Choose Weapon = best of 3 pool weapons at the `WeaponOfferCurve`
  level/perk count (keep current if none is better).
- **Breathing Break shopping** - the designed per-Break loop, in order, each only if affordable
  (`CoinReserve` kept): **1 weapon** - one Store offer (fresh account: `ShopWeaponOfferCount` 0),
  rolled like `StoreUtility.RollWeaponOffers` (`WeaponOfferCurve` level, Bernoulli perk slots, talent
  rarity tuning), price `WeaponOfferBasePrice + WeaponOfferPricePerPerk x perks` (asset: 1000 + 250),
  bought when predicted DPS >= current x `WeaponBuyThreshold` (1 = never downgrade); **1 perk** -
  Blacksmith `PerkChoiceCount` offers by `BreakTuning[BreathingIndex]`, best DPS-per-coin (asset
  prices 500/1000/1750/3000); **1 accessory repair** (`StoreConfig.ResolveAccessoryRepairCost(2)`,
  only if `OfferAccessoryService`); **1 food** (weighted roll from `StoreConfig.FoodPool`, its
  `Price`, no DPS effect). Counts are the `*PerBreak` knobs; "Increase Weapon Level" only if
  `StoreConfig.OfferWeaponLevelUp`. Skipped offers are written to the pick log with the reason.

## Knobs that are *not* in the game data (calibrate these against a real run)

| Scenario field | Meaning | Default |
|---|---|---|
| `HitEfficiency` | share of theoretical weapon DPS that lands | 0.7 |
| `SkillUseEfficiency` | share of theoretical skill DPS that lands | 0.85 |
| `AreaTargets` | enemies an AoE hits when enough are alive | 3 |
| `OverkillWaste` | tick damage lost per kill (overkill/retarget) | 0.15 |
| `OrbPickupEfficiency` (curve by survival minute), `CoopPickupBonus` | share of XP/kill-coin orbs collected before `OrbLifetime` expires (solo); each extra player shrinks the loss by the bonus | 0.95 → 0.9 (6 min) → 0.8 (12 min); 0.35 |
| `ChannelContactUptime` | share of a contact channel (Juggernaut) with `AreaTargets` enemies actually in contact | 0.5 |
| `UnquantifiedPerkDpsValue` | DPS worth of a perk the sim can't quantify (procs, pierce...) | +4% |
| `SkillUpgradeDpsValue` | skill DPS worth of one passive/dash rank, or a skill rank the sim can't quantify | +10% |
| `EnemyLeakFraction` | spawns retired/refunded instead of killed | 5% |
| `WeaponBuysPerBreak` / `BlacksmithBuysPerBreak` / `AccessoryRepairsPerBreak` / `FoodBuysPerBreak` | the per-Break shopping loop | 1 / 1 / 1 / 1 |
| `BarrelBreakFraction` (+ `BarrelsPerEnemyChunk` override, `BarrelLoot`, `CoinsPerBarrelOverride`) | share of the data-counted barrels actually broken | 0.75 |
| `SpawnFailureChance` | Director purchases that find no anchor (rest of the pulse forfeited) | 10% |
| `EngageDistanceFraction` / `EngageReactionSeconds` / `StationaryEngageSeconds` | walk-in delay before a spawn can be shot: ring x fraction / enemy `MoveSpeed` + reaction; fixed for turrets | 0.7 / 0.75 s / 4 s |

## Known simplifications

- Cohesive party only: no cluster-split fronts, no encounter modifiers, no rift mutations.
- Player survivability is not simulated (no deaths/revives); enemy damage output is reported only
  through the `EnemyDmg` curve implicitly via HP/TTK, not as incoming DPS.
- Global Upgrades that don't touch DPS/economy (move speed, regen, pickup radius, dash) are picked
  only when they win the roll, and have no simulated effect. `HeroSkillCharge`/`DashCharge` ignored.
- Perk procs, elemental reactions, hero passives, Ascension effects and Accessory Guard are not
  modelled beyond the flat knobs above.
- Focus-fire: party damage always goes to the lowest-HP enemy first.
- Rows are keyed on `SurvivalTime`, so a Breathing Break's shopping shows up as the jump between two
  rows. After `DurationMinutes` the run still plays out a trailing Breathing Break so a Boss phase
  right after it is reached; the Boss phase ends the run and the last row reports `BossHp`/`BossTtk`
  (0 = never reached). The fight itself is not simulated.

## Comparing against a real run

1. Add `BalanceRunRecorder` to any GameObject in the game scene (`AutoStart` on) - or call
   `StartRecording` from its `[Button]`. It writes `Library/BalanceSim/actual_<date>.csv` every
   minute of `SurvivalTime` (Breathing Breaks, Lobby and level-up screens don't advance it, matching
   the simulator's rows).
2. In the window, run the same scenario, then **Load Actual CSV**. Compare columns show
   `predicted / actual / delta`, coloured green (<15%), yellow (<35%), red.
3. Tune `HitEfficiency`/`SkillUseEfficiency` until minute 1-3 DPS matches; the remaining deltas are
   balance signals (XP curve, Director budget, prices, weapon curve...).

## Reading the table

- With `SimulateAllPlayerCounts` on (default) a Run covers 1P/2P/3P/4P; the **Players** toolbar above
  the hero tabs switches between them (CSV export carries a `Players` column). Use it to check the
  `CoinGain`/`XpRequirement`/`DirectorPressure` co-op rows: `BreakAfford`, `Level` and `PartyKills`
  should read about the same on every tab.

- `DpsRatio` = TotalDps / (`ExpectedPlayerDps` curve x `ExpectedDpsBaseline`) - the design
  expectation; >1 the player is ahead of the curve.
- `Budget` climbing every minute = spawns are capped by `MaxAliveEnemies`/`TargetPressure`, not budget.
- `PressureFill` < 1 with `Alive` small = the player clears faster than the Director refills.
- `IdlePct` = share of the survival minute with nothing alive to shoot (Breathing excluded).
- `TtkNormal/Heavy/Elite` = seconds for the whole party to kill one fresh spawn of that tier.

## Current status (2026-09-12)

Runs in-Editor; all three assemblies (simulation, editor, recorder) compile clean. There is also a
project skill, `.claude/skills/balance-simulator/SKILL.md`, with the operational knowledge (how to
calibrate, extend, compile-check, and the gotchas) - keep both in step when the model changes.

Verified/found so far by comparing against play:
- A 3-minute Max run reached level ~7.5 / 139 kills vs. a predicted 8.7 / 190 before the walk-in
  engagement delay and `SpawnFailureChance` existed - re-check with the recorder attached; the
  remaining gap should come out of `HitEfficiency` (pistol) and the engage knobs.
- Co-op levelled slower than solo because only the Director *budget* scaled with player count while
  its pressure/alive caps didn't - fixed with `CoopGlobalKey.DirectorPressure`
  (`docs/run-curves-coop-scaling.md`); `XpRequirement` now wants re-tuning downward.
- Kai's baseline vortex has no damage beyond its 12 impact (by design); Brute's Juggernaut is a
  contact channel (`ChannelContactUptime`); `LuxSentryLaser.asset` has `Damage 0` (unauthored
  placeholder), so Weapon Systems R3 is currently a no-op in game and in the sim.

Not yet calibrated: every knob in the table above still carries its default - the next recorded run
per hero is the input for that.
