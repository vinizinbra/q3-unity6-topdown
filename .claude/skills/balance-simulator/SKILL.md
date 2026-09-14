---
name: balance-simulator
description: |
  How the RiftRaiders Balance Simulator works (Tools > RiftRaiders > Balance), what it models and
  deliberately doesn't, how to calibrate it against a recorded run, and how to extend it (new hero
  skill model, new knob, new column). Use when the user asks about predicted vs. played balance
  numbers, "why does hero X show Y DPS", tuning SurvivalConfig/BalanceConfig/XP/prices, or when
  changing any system the simulator mirrors (Director, weapons, skills, level-ups, Store/Blacksmith).
---

# Balance Simulator

Analytical Editor-window simulator that predicts a run **per minute of SurvivalTime, per hero**:
level, kills, weapon/skill DPS, weapon level/perks, coins, enemy HP vs. time-to-kill, Director
pressure. It reads the real config assets and steps the run with a player-decision policy; a runtime
recorder samples the same columns from a played match so the two can be diffed in the window.

Full design + formulas with source references: `docs/balance-simulator.md` (read it first when
touching the sim). This skill is the operational knowledge that doc doesn't carry.

## Files

`Assets/_QuantumUser/Editor/BalanceSimulator/` (compiles into `Quantum.Unity.Editor` via the folder
`.asmref`; can use every `Quantum.*` type, `AssetDatabase`, `QuantumUnityDB`):

| File | What lives there |
|---|---|
| `BalanceSimScenario.cs` | ScriptableObject "run config to test" + every calibration knob |
| `BalanceSimAssets.cs` | loads configs (scenario override or `Resources` default), `Resolve<T>(AssetRef<T>)`, `Warnings` |
| `BalanceSimModel.cs` | `SimStats` (CharacterStats mirror), `SimWeapon` (DPS + perk/level math), `SimPlayer`, `SimEnemy`, `SkillEstimate`, `UpgradeValue` |
| `BalanceSimSkillModel.cs` | `Evaluate` (base skill damage per activation) and `EvaluateUpgrade` (per-rank Ascension value) |
| `BalanceSimDirector.cs` | phase timeline + budget-pulse spawn loop + enemy HP/engage delay |
| `BalanceSimPolicy.cs` | level-up card rolls/picks, Choose Weapon, Store + Blacksmith at Breathing Breaks |
| `BalanceSimRunner.cs` | the tick loop, kills -> XP/coins, per-minute snapshots, Monte-Carlo over seeds |
| `BalanceSimReport.cs` | `Col` enum (column layout), averaging, CSV/Markdown, recorder-CSV loading |
| `BalanceSimulatorWindow.cs` | the window (scenario, seed, run, tabs, compare, pick log) |

`Assets/_Project/Scripts/Balance/BalanceRunRecorder.cs` (runtime, Assembly-CSharp): samples the
live `Frame` every 60 s of `Global.SurvivalTime` into `Library/BalanceSim/actual_<date>.csv`.
**Its `Columns[]` string array must stay in the same order as `BalanceSimReport.Col`.**

Scenario assets live in `Assets/_QuantumUser/Editor/BalanceSimulator/Scenarios/` (window **New**
button or `Create > RiftRaiders > Balance > Simulator Scenario`).

## Clock and axes

- Rows are keyed on **SurvivalTime**. Breathing Breaks and uncleared Elite phases freeze it (exactly
  like `SurvivalProgressionUtility.Tick`), so a Break's shopping appears as the jump between two rows
  and `IdlePct` excludes Breathing. The recorder samples on `Global.SurvivalTime` too.
- The run ends at `DurationMinutes` of SurvivalTime or when the Boss phase begins - the last row then
  carries `BossHp`/`BossTtk`; the boss fight itself is not simulated.
- `SimulateAllPlayerCounts` (default on) runs 1P-4P in one go; a **Players** toolbar above the hero
  tabs switches between them - the place to verify the co-op rows (`CoinGain`, `XpRequirement`,
  `DirectorPressure`): wallets/levels should look alike on every tab.
- `Seeds` runs are averaged (so `Level` can read 8.7); `BaseSeed` (toolbar **Seed** / **Reroll**)
  changes the batch. Same seed = same report, use it to A/B a config change.

## What is data and what is a knob

Everything from the assets is mirrored 1:1 (phase timeline, budget pulses, `TargetPressure`/
`MaxAliveEnemies`/`MaxPurchasesPerPulse` incl. the `CoopGlobalKey.DirectorPressure` row, enemy HP
via `BalanceConfig.EvaluateEnemyHp` (Light curve for Filler/Normal, Heavy for Specialist+) x co-op HP
x `Stats.HealthMultiplier`, XP thresholds, coin drops, weapon DPS formula, perk/level compounding,
level-up category sequence and rarity weights, Store/Blacksmith prices and `WeaponOfferCurve`).

The knobs are the things the game data can't tell you - **calibrate them from a recorded run before
reading any other delta as a balance signal**:

| Knob | Stands in for | Calibrate against |
|---|---|---|
| `HitEfficiency` | accuracy x trigger uptime on the weapon | actual `TotalDps` vs theoretical `WeaponDps` in the recorder CSV |
| `SkillUseEfficiency` | casting on cooldown x targets actually hit | per-hero skill feel |
| `AreaTargets` | enemies an AoE hits | crowd density in the phase being tuned |
| `ChannelContactUptime` | Juggernaut-style contact channels (knockback pushes enemies out) | Brute run |
| `OverkillWaste` | damage lost per kill / retarget | kills-per-minute delta |
| `OrbPickupEfficiency` curve, `CoopPickupBonus` | XP/coin orbs left to expire (30 s lifetime, 1 m radius), worse late while kiting; loss shrinks per extra player | recorder `OrbPickup` = XP orbs collected / kills per minute, at each player count |
| `EngageDistanceFraction`, `EngageReactionSeconds`, `StationaryEngageSeconds` | walk-in before a spawn can be shot (ring radius / enemy `MoveSpeed`) | `SpawnsPerMin` + `Alive` deltas |
| `SpawnFailureChance` | Director purchases that find no anchor (rest of pulse forfeited) | `SpawnsPerMin` vs `Budget` piling up |
| `EnemyLeakFraction` | spawns retired/refunded instead of killed | kills vs spawns |
| `UnquantifiedPerkDpsValue`, `SkillUpgradeDpsValue` | perks/ranks with no stat the sim can read | leave unless a hero is clearly mis-valued |
| `WeaponBuysPerBreak`, `WeaponBuyThreshold`, `BlacksmithBuysPerBreak`, `AccessoryRepairsPerBreak`, `FoodBuysPerBreak`, `CoinReserve` | the designed per-Break loop: 1 weapon, 1 perk, 1 accessory repair, 1 food (user's spec, 2026-09-13) | pick log vs what the user actually bought |
| `BarrelBreakFraction` (+ `BarrelsPerEnemyChunk` override, `BarrelLoot`, `CoinsPerBarrelOverride`) | Breakable barrel loot: LevelConfig Enemy chunk Count x barrels/chunk counted from each chunk's ChunkSpawnConfig x BreakLootData value | `CoinsEarned` delta |

Diagnosing a mismatch, in this order: (1) `SpawnsPerMin`/`Alive`/`Budget` - is the Director side
right? (2) `TotalDps` actual vs `WeaponDps` theoretical - is the player side right? (3) only then the
config itself (XP curve, budget, prices).

## Known simplifications (don't "fix" these silently - they're documented trade-offs)

Cohesive party only (no split fronts), no encounter modifiers/rift mutations, no player deaths or
incoming damage, focus-fire on lowest-HP enemy, perk procs/elemental reactions/passives/Accessory
Guard only as flat knobs, Overclock-style multipliers apply to barrels mounted at pick time, Boss
fight not simulated.

## Skill damage model - how heroes are valued

`BalanceSimSkillModel.Evaluate` reads the **base** skill: `ProjectileSkillData` (impact x
`AreaTargets` if the hit is `AreaHitData`, plus `SpawnAlternatingAreaEffectData` ticks, plus spawned
entity durations), `JuggernautSkillData` (discharges x `ChannelContactUptime`), `BerserkSkillData`
(no damage - a weapon fire-rate/reload buff at `Duration/Cooldown` uptime), everything else generic.
Then every **Activated** action in `SkillData.Actions` adds mounted `AssetRef<WeaponDataAsset>` DPS x
duration (Lux sentry) and any `Damage`/`DamageAmount` field x ticks x targets.

`EvaluateUpgrade(action, rank)` values Ascension ranks from their rank-indexed arrays against the
skill's basis (`est.Basis` = `ProjectileSkillData.Damage` / `JuggernautSkillData.Damage` /
`SpawnSentrySkillAction.SkillDamage`). It switches on the action **type name** (so renames degrade to
the generic `DamagePercent` rule instead of breaking). Ranks with nothing quantifiable fall back to
`SkillUpgradeDpsValue`. The pick log prints `+N/cast` for quantified ranks.

Facts that surprised us (verified from the assets, keep in mind before "fixing" the sim):
- **Non-Activated actions are Ascension picks**, Activated ones are baseline
  (`LevelUpUtility.AddHeroSkillUpgradeCandidates` skips Activated). Only actions in the skill's
  `Actions` list count; sub-assets sitting in the same `.asset` file are often orphans
  (`KaiVortexSkill.asset` has six).
- Kai's baseline vortex deals only its 12 impact; all damage is in Compression/Void Shards/Collapse.
- Brute's Juggernaut is a 30/s-per-enemy contact channel, hence the uptime knob.
- Lux: `LuxSentryLaser.asset` has `Damage 0` (placeholder, see `docs/lux-ascensions.md`), so Weapon
  Systems R3 adds nothing until it's authored. Every authored enemy has `Stats.ShieldMultiplier 0`.
- Coins are shared like XP: a collected coin orb goes to EVERY wallet (`CurrencyOrbSystem.Grant` ->
  `CoinUtility.GrantAll`), only the pickup is by the nearest player; wallets/spending are per player.
  So per-player income would RISE with party size (kills scale with the pressure row, barrels are
  fixed); `CoopGlobalKey.CoinGain` (1 / 0.65 / 0.50 / 0.42) scales KILL orb values back to ~solo
  (`CoinUtility.TrySpawnDrop`); barrel loot is deliberately not discounted.
- Economy: the asset prices are 10x the code defaults (`StoreConfig.asset` weapon 1000 + 250/perk,
  `BlacksmithConfig.asset` perks 500/1000/1750/3000, food 30-400). Coins come from kills AND barrels
  (`BreakableUtility.TrySpawnLoot`, not `CoinUtility` - only `Enemy` deaths go through the latter);
  barrels can't be counted from the chunk prefab's children - they only hold `BakedSpawnPreview`
  stand-ins (`ChunkSpawnBaker`); count them from `QPrototypeChunk.Prototype.SpawnConfig` ->
  `ChunkSpawnConfig.Spawns` (what `TalentGateSystem` actually spawns), identifying a barrel by its
  prototype's prefab carrying `QPrototypeBreakable`. `.qprototype` files store no GUID (derived from
  the Unity GUID), so grepping asset GUIDs across `.qprototype` files finds nothing.

## Extending

- **New hero base skill shape**: add a `case` in `BalanceSimSkillModel.Evaluate`; set `est.Basis`,
  `est.ActiveDuration`, `est.UsesArea`, and a descriptive `est.Model` (it's shown in the window).
- **New Ascension line**: add a `case "<TypeName>SkillAction"` in `EvaluateUpgrade` using
  `F("FieldName")` (reads FP / FP[] / byte / int, arrays rank-indexed). Return cumulative value at
  that rank; the policy takes the delta between ranks.
- **New Global Upgrade type**: add it to `SimStats.ApplyGlobalUpgrade`; unknown types are picked but
  have no effect.
- **New quantifiable weapon perk**: add to `SimWeapon.AddPerk`; unknown perks get the flat value.
- **New column**: append to `Col` (end of enum), fill it in `BalanceSimRunner.Snapshot`, add the
  same name at the same position in `BalanceRunRecorder.Columns`, add a tooltip in the window, and
  put it in `TableColumns` (and `CompareColumns` if the recorder can measure it).
- **New Director rule** (caps, co-op rows, unlock windows): mirror it in `BalanceSimDirector.TryPulse`
  / `TrySelectSpawn` the same tick it lands in `CombatDirectorUtility`, and note it in
  `docs/balance-simulator.md`'s "What the model mirrors".

## Compile-checking without the Editor

The Editor is usually open (`Temp/UnityLockfile`), so don't launch a headless Unity. Roslyn's `csc`
against the already-built assemblies catches everything except Unity-serialization issues:

```
# response file: -target:library -langversion:9.0 -unsafe, -define: from the matching .csproj
# <DefineConstants>, -r: every <HintPath> of that .csproj + Library/ScriptAssemblies/*.dll,
# then the .cs files. Editor files -> Quantum.Unity.Editor.csproj; recorder -> Assembly-CSharp.csproj.
dotnet /usr/local/share/dotnet/sdk/<ver>/Roslyn/bincore/csc.dll @BalanceSimEditor.rsp | grep "error CS"
```

If simulation code changed too (e.g. a new `CoopGlobalKey`), build `Quantum.Simulation.csproj`'s file
list the same way first and point the Editor/recorder response files at that fresh dll, otherwise the
stale `Library/ScriptAssemblies/Quantum.Simulation.dll` hides the new symbols. Unity's compiler is
stricter than Roslyn about `Math.Max(int, byte)` ambiguity - cast bytes to int.

## Gotchas

- `LogHelper` is `Disabled = true` by default: resolve failures never reach the Console. The window's
  yellow box lists `BalanceSimAssets.Warnings` instead - check it before blaming the model.
- `AssetRef` resolution goes through `QuantumUnityDB.GetGlobalAssetEditorInstance`; nested sub-assets
  (`Path: ...|Name`) resolve fine, dangling GUIDs return null and are skipped.
- `BreathingIndex` is 0 for the first Break (the game increments it when a Break *ends*), so
  `BlacksmithConfig.BreakTuning[0]` is the first Break.
- `GetCategoryForLevel` receives `Global.Level` *after* the increment: the first level-up is level 1,
  index 0 of `LevelSequence` (currently HeroSkill, HeroSkill, GlobalUpgrade).
- The recorder maps skill-spawned damage owners (sentries, vortices) back to the hero through
  `AreaOwner.Owner`; damage with `Silent` set is ignored.
