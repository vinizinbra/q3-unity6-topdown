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
  each orb subject to the `OrbPickupEfficiency` curve (orbs expire after `ExperienceConfig.OrbLifetime`,
  60 s); kill coin orbs roll separately at that share x `CoinPickupFactor` (coins expire after 30 s);
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
- **Weapon DPS** - `WeaponSystem.ResolveLiveFireCooldown` + `StatUtility.GetFireCooldown` +
  `DamageUtility.ResolveOutgoingDamage`: sustained fire incl. reloads, pellets, crit, `Weapon.DamageMultiplier`
  (perks + `WeaponSystem.AddLevel` compounding), the player-level bonus
  (`ExperienceConfig.DamageBonusPerLevel` x displayed Level, weapon and skill alike), Hero Mastery
  (Weapon Weight / Element lines, while the equipped weapon matches), x `HitEfficiency`.
  - **Crit** is `CharacterStats.CriticalDamageMultiplier x max(1, Weapon.CriticalDamageBonus)`, multiplicative
    (`ResolveCriticalTerms`). Heroes author 1.0, so the weapon's bonus IS the multiplier.
  - **Magazine cycle** (`SimWeapon.MagazineCycleSeconds`): the reload starts on the last shot and overlaps
    its cooldown. Every wait (fire cooldown, burst delay, reload) is rounded up to whole Quantum ticks
    (SessionConfig `UpdateFPS`, 20 = 0.05 s), because `WeaponSystem` sets each timer fresh (no carried
    remainder) and counts it down once per tick. Fast weapons land well under their `FireRate` (12/s ->
    10/s) and a small Fire Rate bonus can be worth nothing until it crosses a tick boundary. The scenario's
    `QuantizeWeaponTimers` switches this off for A/B.
- **Skill DPS** - damage per cast / cycle x `DamageMultiplier x SkillDamageMultiplier` x character crit
  x level bonus x `SkillUseEfficiency`.
  - **Cycle** = cooldown / `SkillCooldownMultiplier`, plus the channel for `JuggernautSkillData`/
    `BerserkSkillData`: the slot stays Active for `Duration` and `SkillSystem.FinishSkill` only arms the
    cooldown when it ends. Projectile skills finish at release, so their spawned entity lives during
    the cooldown.
  - **Skill Duration** only stretches Skill-source spawned entities (`SpawnedEntitySpawner`: Kai's
    vortex, Zara's totem, Lux's sentry) - it scales their tick/pulse/barrel damage, never the cycle, and
    doesn't touch a channel.
  - Skill-mounted weapons (Lux's sentries) get neither crit nor the level bonus: a barrel has no
    `CharacterStats`, only the baked `StatUtility.GetSkillDamageMultiplier`. Their DPS is recomputed
    live through `SimWeapon.BaseDps` (tick rounding included) with Overclock's fire rate, extra
    lifetime and Redline.
  - Dedicated models: `BerserkSkillData` (Max) = weapon fire-rate/reload buff at channel / (channel +
    cooldown) uptime, read live; `JuggernautSkillData` (Brute) = `Damage` per discharge x `AreaTargets`
    x `Duration/DischargeCooldownPerEnemy` x `ChannelContactUptime`; `ProjectileSkillData` = `Damage` x
    (area? `AreaTargets`) - only when the hit carries a `DamageEffectData` (or is a vortex) - plus
    `SpawnAlternatingAreaEffectData` beats (Zara: Damage/Support alternate, Support deals half, so 0.75
    of `DamageAmount` per beat). All **Activated** actions on the skill (only when
    `SkillData.CheckActions`) add any mounted `AssetRef<WeaponDataAsset>` (Lux sentry) and any
    `Damage`/`DamageAmount` field x ticks x `AreaTargets`. The table's "Skill model" line says which
    path was used.
- **Skill Ascension ranks** - `BalanceSimSkillModel.EvaluateUpgrade` values each rank of a hero-skill
  action from its rank-indexed fields against the skill's basis (`ProjectileSkillData.Damage`,
  `JuggernautSkillData.Damage`, `SpawnSentrySkillAction.SkillDamage`): Kai Compression/Void Shards/
  Collapse (percent x pulses x targets; Compression's rank-3 Implosion counts the vortex's *pull*
  pulses - prefab `Vortex.TickInterval` 0.05 s -> every 2 game ticks - not the damage pulse), Pixie
  Cluster Bomb / Direct Hit (x `InnerRadiusFraction`^2 - only the blast's inner area qualifies) /
  Birthday Cake (bonus from rank 3 only), Brute Bone Breaker / Aftershock (stacks = every unit hit this
  cast, Earthquake = the stacked blast x its percent) / Concussive Impact (per discharge hit), Max Full
  Throttle (weapon damage while Overdrive, assumes max Rage), Zara Amplifier/Double Time/Main Stage,
  Lux Weapon Systems (extra barrels) / Overclock (fire rate, lifetime, Redline) / Overload Core; unknown
  actions use a generic `DamagePercent` rule. A modelled action whose rank adds nothing stays at 0;
  only unmodelled ranks (Singularity, Momentum, Last Stand...) fall back to the flat
  `SkillUpgradeDpsValue`. Kai's baseline vortex therefore shows ~0
  skill DPS until Compression/Collapse/Shards are picked - that is the authored design, not a bug.
- **Level-ups** - category from `LevelUpConfig.LevelSequence[(level-1) % count]` where `level` is the new
  displayed level (`Global.Level + 1` after the increment, so the first level-up reads index 1),
  `ChoiceCount` cards
  drawn weighted by rarity (`LevelUpConfig.GetWeight`) from the same pools `LevelUpUtility` collects
  (weapon perks not equipped + fire-type match, Global Upgrades under `MaxPicks`, hero
  skill/dash/passive ranks), Choose Weapon = best of 3 pool weapons at the `WeaponOfferCurve`
  level/perk count (keep current if none is better). Weight-0 perks are never drawn; Choose Weapon
  offers drop perks conflicting with one already rolled (Store offers don't, same as the game).
- **Elite chests** - an enemy with `ChestDrop` (6 Elites, 100%) gives every player one extra pick in the
  chest's forced category (`Chest.Kind` read off the prototype prefab), same roll as a level-up. Rift
  Mutation chests are logged, not simulated.
- **Breathing Break shopping** - the designed per-Break loop, in order, each only if affordable
  (`CoinReserve` kept): **1 weapon** - one Store offer (fresh account: `ShopWeaponOfferCount` 0),
  rolled like `StoreUtility.RollWeaponOffers` (`WeaponOfferCurve` level, Bernoulli perk slots, talent
  rarity tuning), price `WeaponOfferBasePrice + WeaponOfferPricePerPerk x perks` (asset: 1000 + 250),
  bought when predicted DPS >= current x `WeaponBuyThreshold` (1 = never downgrade); **1 perk** -
  Blacksmith `PerkChoiceCount` offers by `BreakTuning[BreathingIndex]`, best DPS-per-coin (asset
  prices 500/1000/1750/3000); **1 accessory repair** (2 points × `StoreConfig.AccessoryRepairCostPerPoint`,
  only if `OfferAccessoryService`); **1 food** (weighted roll from `StoreConfig.FoodPool`, its
  `Price`, no DPS effect). Counts are the `*PerBreak` knobs; "Increase Weapon Level" only if
  `StoreConfig.OfferWeaponLevelUp`. Skipped offers are written to the pick log with the reason.

## Knobs that are *not* in the game data (calibrate these against a real run)

| Scenario field | Meaning | Default |
|---|---|---|
| `HitEfficiency` | share of theoretical weapon DPS that lands | 0.7 |
| `SkillUseEfficiency` | share of theoretical skill DPS that lands | 0.85 |
| `AreaTargets` | enemies an AoE hits when enough are alive | 3 |
| `RetargetSeconds` | seconds of one player's DPS lost per kill (overkill + retarget) - fixed per kill, so independent of `TickSeconds` | 0.15 s |
| `CoinPickupFactor` | coin orbs expire sooner (30 s vs 60 s): coin pickup share = XP share x this | 0.9 |
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

- Cohesive party only: no cluster-split fronts, no encounter modifiers, no rift mutations (so Rift
  Mutation chests and the mixed-fallback roll's mutation cards are skipped). Co-op Store stock is rolled
  per player (the game shares one inventory per Break).
- `IsEligible` overrides (Flashpoint needs a Burn source, Dash Charge's cap) need a `Frame`, so they
  aren't mirrored.
- Juggernaut discharges are `AreaTargets` x channel / per-enemy cooldown x `ChannelContactUptime`; the
  real limit is the charge meter (10 m of travel per discharge) and Momentum, which isn't modelled.
- Player survivability is not simulated (no deaths/revives); enemy damage output is reported only
  through the `EnemyDmg` curve implicitly via HP/TTK, not as incoming DPS.
- Global Upgrades that don't touch DPS/economy (move speed, regen, pickup radius, dash) are picked
  only when they win the roll, and have no simulated effect. `HeroSkillCharge`/`DashCharge` ignored.
- Perk procs, elemental reactions, hero passives, Ascension effects and Accessory Guard are not
  modelled beyond the flat knobs above. Hero Mastery's flat damage bonus is modelled; its rank-3
  specials and Fire Mastery's Neutral-weapon-under-Ignition exception are not.
- Ramp perks (Relentless Fire/Suppressive Cycle/Overcharge Cycle): a per-HIT ramp is credited at half
  its max stacks (placeholder - hit rate isn't modelled). A per-SHOT ramp (`AdvancePerShot`, Auto
  Shotgun) is deterministic and averaged exactly over one magazine fired from 0 stacks via
  `WeaponDataAsset.PerShotRampFactors`, shared with the Inspector DPS preview.
- Focus-fire: party damage always goes to the lowest-HP enemy first.
- Rows are keyed on `SurvivalTime`, so a Breathing Break's shopping shows up as the jump between two
  rows. After `DurationMinutes` the run still plays out a trailing Breathing Break so a Boss phase
  right after it is reached; the Boss phase ends the run and the last row reports `BossHp`/`BossTtk`
  (0 = never reached). The fight itself is not simulated.

## Comparing against a real run

1. Add `BalanceRunRecorder` to any GameObject in the game scene (`AutoStart` on) - or call
   `StartRecording` from its `[Button]`. It writes `Library/BalanceSim/actual_<date>.csv` every
   minute of `SurvivalTime` (Breathing Breaks, Lobby and level-up screens don't advance it, matching
   the simulator's rows). What it measures:
   - kills: only `Enemy` targets killed by a player (tier cached while alive - most tiers are destroyed
     before the synced `EntityDied` arrives);
   - `TotalDps`: `CharacterStats.DamageDealt` (enemy-only, overkill removed) plus skill-spawned owners'
     `EntityDamaged`, over seconds outside Breathing;
   - `WeaponDps`: every-shot-lands ceiling from the game's own `ResolveBaselineDamageMultiplier`/
     `ResolveBaselineCritical`, tick-rounded - the window multiplies it by `HitEfficiency` to pair it;
   - coins: per-frame wallet change, so Break shopping doesn't cancel income;
   - `PressureFill` uses the co-op pressure row; `Players` = party size, and a loaded CSV only attaches
     to the matching Players tab.
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
- Kai's baseline vortex has no damage beyond its 20 impact (by design); Brute's Juggernaut is a
  contact channel (`ChannelContactUptime`); `LuxSentryLaser.asset` has `Damage 0` (unauthored
  placeholder), so Weapon Systems R3 is currently a no-op in game and in the sim.

Not yet calibrated: every knob in the table above still carries its default - the next recorded run
per hero is the input for that.
