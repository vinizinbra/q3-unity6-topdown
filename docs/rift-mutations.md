# Rift Mutations catalog

Content for the `LevelUpPoolKind.RiftMutation` pool (`LevelUpConfig.RiftMutations`, see
`docs/level-up-upgrades.md` for the general rolling/pausing/grant mechanism that pool plugs into).
This doc is the design catalog and the pool's own mechanism (non-stacking, reaction signals); it
stays the source of truth for how a Rift Mutation is authored and how it differs from a plain
Global Upgrade.

## What a Rift Mutation is

> A rare, non-stackable, run-wide effect that creates a new rule, synergy, or meaningful trade-off.

The game's level-up pools now split into four categories:

1. **Global Upgrades** (`docs/global-upgrades.md`) - simple hero-wide numerical growth, stacks
   indefinitely (Weapon Damage +10%, Move Speed +10%, ...).
2. **Weapon Perks** (`docs/weapon-perks.md`) - attach to a weapon, lost on swap.
3. **Hero Ascensions** - the existing per-hero `SkillUpgrade`/`PassiveUpgrade` pools (Dash/Hero
   Skill/Passive milestones, `CharacterData.DashSkillUpgrades`/`PassiveUpgrades`/`HeroSkill.Actions`
   - see `docs/level-up-upgrades.md`). Naming only - no separate `LevelUpPoolKind` value.
4. **Rift Mutations** (this doc) - rare, non-stackable, build-defining: a one-shot tradeoff (Glass
   Core, Heavy Arsenal), a new reactive rule (Shield Breaker, Critical Focus), or both at once
   (Infinite Momentum).

"Non-stackable" is a **pool-wide** property here, not an opt-in per-asset cap the way
`GlobalUpgradeData.MaxPicks` is - every `RiftMutationData` is implicitly picked at most once per
entity, enforced by `RiftMutationPicks`/`RiftMutationUtility` below.

**Status: all 14 designed mutations are implemented in code** (`RiftMutationData` is an abstract
base with a real `Apply(Frame f, EntityRef entity)`, same shape as `GlobalUpgradeData`, dispatched
generically by `RiftMutationUtility.Grant`).

## Mechanism

- **`RiftMutationData : UpgradeData`** (`Assets/_QuantumUser/Simulation/Assets/RiftMutation/
  RiftMutationData.cs` + `.View.cs`) - same `Apply`/`Description`/`DescriptionArgs`/
  `GetFormattedDescription()` shape as `GlobalUpgradeData`, but **no `MaxPicks` field** - see above.
- **`RiftMutationPicks` component** (`LevelUp.qtn`) - `array<AssetRef<RiftMutationData>>[16]
  Picked`, this entity's full pick history for the pool. Mirrors `GlobalUpgradePicks` but simpler
  (no per-entry `Count`, since every mutation caps at 1).
- **`RiftMutationUtility.cs`** - `Grant(f, entity, mutationRef)` calls `Apply` then always records
  the pick (no `MaxPicks > 0` gate to check first, unlike `GlobalUpgradeUtility.Grant`);
  `IsAlreadyPicked(f, entity, mutationRef)` is what `LevelUpUtility.CollectRiftMutationCandidates`
  checks before offering one again.
- **`LevelUpConfig.RiftMutations : List<AssetRef<RiftMutationData>>`** - own list, own rarity axis
  from `WeaponPerkPool`/`GlobalUpgrades`, but the exact same weighted draw
  (`LevelUpConfig.GetWeight`) and the exact same All or Nothing rarity-shift/single-choice override
  as every other pool - "rare" falls out naturally since most mutations are tagged Epic/Legendary,
  no separate pool-frequency knob needed.
- **`LevelUpPoolKind.RiftMutation = 5`** (`LevelUp.qtn`) - the `Kind` a rolled
  `LevelUpOption` carries; `LevelUpUtility.GrantOption`'s new case dispatches to
  `RiftMutationUtility.Grant`.
- **Debug grant path** - `GrantRiftMutationCommand`/`RiftMutationSystem` (mirrors
  `GrantGlobalUpgradeCommand`/`GlobalUpgradeSystem` exactly) let any Rift Mutation be tried out at
  runtime without a real level-up screen. Three ways in: every `RiftMutationData` asset has a "Grant
  To Local Player" button in its own Inspector while in Play Mode (`RiftMutationData.Debug.cs`,
  same `EditorButtonAttribute` convention as `GlobalUpgradeData.Debug.cs`), the
  `RiftMutationDebugTrigger` component on the `DEBUGGER` GameObject in `QuantumGameScene.unity`
  (next to the existing `GlobalUpgradeDebugTrigger`/`WeaponPerkDebugTrigger`), or the "Rift
  Mutation" tab in the in-game `DebugUpgradeMenuWindow` (populated by `DebugUpgradeMenuTrigger`
  alongside its existing Hero/Global/Weapon Perk tabs). No revert path exists (same reasoning as
  Global Upgrades) - restart Play Mode to reset a player. **Every `DeterministicCommand` subclass
  must also be registered in `CommandSetup.User.cs`'s `AddCommandFactoriesUser`** (`factories.Add(new
  GrantRiftMutationCommand())`) - Quantum's networking/replay layer can't instantiate/deserialize a
  command it has no factory for, so a missed registration here makes a Send silently do nothing at
  runtime with no compile error to catch it.
- **`RiftMutationReactionSystem`** (`Assets/_QuantumUser/Simulation/Systems/`, registered in
  `SystemSetup.User.cs` next to `WeaponPerkReactionSystem`) - the handful of mutations that need
  more than a one-shot `CharacterStats` bake react here, off three signals:
  - `ISignalOnCriticalHit` (`Combat.qtn`, already existed) - **not** gated to `DamageSource.Weapon`
    the way `WeaponPerkReactionSystem`'s own handler is, since these are character-level effects
    meant to fire on any crit source.
  - `ISignalOnSkillActivated` (`CharacterSkills.qtn`, new) - fired unconditionally from
    `SkillSystem.TryBegin` the instant an activation is confirmed (free cast or stack spend alike),
    same "fire once, at the real spend moment" convention `OnFreeCastUsed` already follows.
    General-purpose, not mutation-specific.
  - `ISignalOnShieldBroken` (`Shield.qtn`, new) - fired from `DamageUtility.AbsorbWithShield` the
    exact tick `Shield.Current` crosses from >0 to <=0 (captured before/after the absorb
    subtraction) - not on every hit taken while already broken, not on a hit that only partially
    drains it.
- **`DamageUtility.ResolveRangeDamageMultiplier(f, owner, target, stats)`** - attacker-side range
  falloff for Close Quarters/Longshot, called from `ResolveOutgoingDamage` right after the existing
  `DamageMultiplier * GetSourceMultiplier(...)` line, same shape/placement as the existing
  target-side `ResolveFrontalDamageMultiplier`. Lerps between `CharacterStats.
  NearDamageMultiplier`/`FarDamageMultiplier` off the flat attacker-target distance, against fixed
  design-constant thresholds (5/12 units) - not per-asset tunable yet, a placeholder starting point
  for balance passes like several other proc magnitudes already in this codebase.

## Roster

| Mutation | Class | Rarity | Effect |
|---|---|---|---|
| Glass Core | `GlassCoreMutationData` | Legendary | `CharacterStats.MaxShieldMultiplier` ×2 + `MaxHealthMultiplier` set so `Health.MaxHealth` becomes exactly 1 (absolute, not multiplied further - "becomes 1" is a target, not a relative increment). |
| Last Bastion | `LastBastionMutationData` | Legendary | `MaxHealthMultiplier` ×2 + `Shield.Max`/`Current` zeroed directly (bypasses `CharacterSystem.RefreshMaxShield`'s `newMax <= 0` guard on purpose - that guard protects an *unintentional* zero, this one is deliberate). |
| Heavy Arsenal | `HeavyArsenalMutationData` | Epic | `WeaponDamageMultiplier` +75% / `AttackSpeedMultiplier` -35% - character-level mirror of `HeavyCaliberWeaponPerkData`'s tradeoff shape, stacks with that perk. |
| Bullet Storm | `BulletStormMutationData` | Epic | Same two fields as Heavy Arsenal, opposite tuning (+Fire Rate, -Damage). |
| One in the Chamber | `OneInTheChamberMutationData` | Legendary | `Weapon.MagazineSize` = 1 + `Weapon.FinalRoundDamageBonus` - reuses the exact field `FinalRoundWeaponPerkData`/`WeaponSystem.ResolveLiveDamage` already read live off `Ammo == 1`, so every shot at magazine size 1 qualifies for free. Known limitation shared with `MagazineSizeUpgradeData`: a later weapon pickup resets `Weapon.MagazineSize`/`FinalRoundDamageBonus` from that weapon's own data - nothing re-applies Rift Mutations on equip. |
| Close Quarters | `CloseQuartersMutationData` | Rare | `NearDamageMultiplier` +50% / `FarDamageMultiplier` -30% - see `DamageUtility.ResolveRangeDamageMultiplier` above. Longshot is the mirror opposite. |
| Longshot | `LongshotMutationData` | Rare | Same two fields as Close Quarters, opposite tuning (+Far, -Near). |
| Ultimate Commitment | `UltimateCommitmentMutationData` | Epic | `SkillDamageMultiplier` ×2 / `SkillCooldownMultiplier` ×0.5 - that field is a *rate* (higher = faster, see `StatUtility.GetSkillCooldown`'s `baseCooldown / multiplier`), so halving it doubles the effective cooldown duration even though the field itself shrinks. |
| Focused Power | `FocusedPowerMutationData` | Epic | `AreaRadiusMultiplier` ×0.5 / `SkillDamageMultiplier` ×2 - smaller skill area, bigger hit. |
| Infinite Momentum | `InfiniteMomentumMutationData` | Epic | `DashCooldownMultiplier` ×2 (faster Dash) + new `DashShieldCost` field. The Shield-drain-then-spill-to-Health side is reactive, not baked - see `RiftMutationReactionSystem.OnSkillActivated`. |
| Critical Focus | `CriticalFocusMutationData` | Epic | New `CharacterStats.CritSkillCooldownReduction` field - flat cooldown seconds refunded on **both** Hero Skill and Dash per crit (an earlier design split this per-slot into two independent picks; merged into one since Rift Mutations don't stack). See `RiftMutationReactionSystem.OnCriticalHit`. |
| Shield Breaker | `ShieldBreakerMutationData` | Rare | `CharacterStats.ShieldBreakGrantsDashCharge` (bool) - the instant your own Shield breaks, refill one Dash charge (`CurrentStacks`, capped at `MaxStacks`) so it's immediately usable. A proc, not a permanent capacity increase like Dash Charge (Global Upgrade). See `RiftMutationReactionSystem.OnShieldBroken`. |
| All or Nothing | `AllOrNothingMutationData` | Epic | `CharacterStats.AllOrNothingActive` (bool) - doesn't touch a stat. Read by `LevelUpUtility.RollOptionsFor` to force every subsequent level-up roll for this entity down to a single, rarity-shifted option instead of the normal up-to-3 (Common→Rare→Epic→Legendary, capped at Legendary). |
| Greed | `GreedMutationData` | Legendary | `CharacterStats.RiftShardGainMultiplier` ×2 + `Frame.Global.EnemyHealthBonusMultiplier` +50% (run-wide - raises every enemy's Max Health the instant *any* player picks it, read by `EnemySystem.SeedHealth`). Also the prerequisite for the Rift Shard currency system existing at all - see below. |

No exclusivity system exists *between* distinct mutations (only within one, via `RiftMutationPicks`)
- nothing stops a build from picking directly opposing tradeoffs (Heavy Arsenal + Bullet Storm,
Close Quarters + Longshot); they just partially cancel.

## Rift Shard currency (Greed's prerequisite)

Greed's "Currency ×2" needed a real currency to double - none existed anywhere in the simulation
before this pool. **Rift Shards** were built to fill that gap, mirroring the existing `ExpOrb`
drop/collect pattern almost field-for-field (a third instance of that pattern - `ScrapOrb`, Lux's
Scrap Collector passive, was the second):

- **`RiftShard.qtn`** - `component RiftShard { FP Value; }`, the physical pickup, same shape as
  `ExpOrb.qtn`.
- **`RiftShards.qtn`** - global block: `FP TotalRiftShards` (co-op shared run-wide total, like
  `Experience.qtn`'s `TotalExperience`) and `FP EnemyHealthBonusMultiplier` (Greed's own
  difficulty-scaling side effect - lives here for lack of a better-fitting block, not
  currency-specific; starts at 0/no-bonus, read as `1 + this`).
- **`RiftShardConfig.cs`** (`AssetObject`) - `PickupRadius`/`OrbLifetime`, mirrors `ExperienceConfig`
  minus the leveling curve (no leveling attached to this currency), plus `Min`/`MaxSpawnOffset`
  (scatters the spawn away from the exact death point, mirroring `ScrapConfig`'s own fields).
- **`RiftShardUtility.cs`** - `TrySpawnDrop(f, target, owner)` mirrors `ExperienceUtility.
  TrySpawnDrop` plus `ScrapUtility`'s own drop-chance/scatter shape: a new `RiftShardValue`/
  `RiftShardDropChance` pair on `EnemyTierStatsConfig.TierStats` (mirroring `ExpValue`) both gate
  the drop - `Value > 0` is necessary but not sufficient, `DropChance` (`DamageUtility.RollChance`)
  still has to roll true - and the spawn position scatters via `EnemyMovementUtility.
  RandomPositionInRing` when `MaxSpawnOffset > 0`. Called from `DamageUtility.ApplyDamage`'s death
  branch alongside the existing `ExperienceUtility`/`ScrapUtility`/`CoinUtility` calls.
  `Grant(f, amount)` just adds to `TotalRiftShards` (no leveling logic to mirror).
- **Coin** (`docs/global-upgrades.md`'s own "Economy" section) is a second, independent currency
  built the same pass, mirroring `RiftShardUtility`/`RiftShardOrbSystem` field-for-field (`Coin.qtn`/
  `Coins.qtn`/`CoinConfig`/`CoinUtility`/`CoinOrbSystem`, `CharacterStats.CoinGainMultiplier`) - see
  that doc for the full writeup. Unlike Rift Shards, no Rift Mutation sources its gain multiplier
  yet.
- **`RiftShardOrbSystem.cs`** - mirrors `ExpOrbSystem` exactly: any player in pickup range collects
  it, amount scaled by the collecting player's own new `CharacterStats.RiftShardGainMultiplier`
  (doubled by Greed), credited to the shared run-wide total.
- **`RuntimeConfig.RiftShardConfig`/`RiftShardPrototype`** (`Assets/_QuantumUser/Simulation/Default/
  RuntimeConfig.User.cs`) - next to the existing `ExperienceConfig`/`ExpOrbPrototype` pair.
- **`EnemySystem.SeedHealth`** reads the new global bonus: `MaxHealth = EnemyTierStatsConfig.
  Resolve(f, data.Tier).MaxHealth * (1 + f.Global->EnemyHealthBonusMultiplier)`.

Unlike Gold Gain (a second, still entirely undesigned currency - see `docs/global-upgrades.md`), no
standalone "+X% Rift Shards" Global Upgrade exists - Greed is currently the only source of
`RiftShardGainMultiplier`.

## Asset generation

`Assets/_QuantumUser/Editor/RiftMutationAssetGenerator.cs` (`Tools/RiftRaiders/Generate Rift
Mutation Assets`) authors one `.asset` instance per class in the roster above - tuned to this doc's
own numbers - under `Assets/_QuantumUser/Resources/LevelUp/RiftMutation/` (created automatically if
missing), and wires all of them into `LevelUpConfig.asset`'s `RiftMutations` list. Mirrors
`GlobalUpgradeAssetGenerator.cs`/`WeaponPerkAssetGenerator.cs` exactly: re-running is safe, existing
assets are updated in place, the list is rebuilt from scratch each run. `Icon` is left unset for
every asset - manual per-mutation Inspector step, same as every other pool's generator.

## Current status / known simplifications

The code compiles and every mutation has a class + `RiftMutationAssetGenerator` spec, but:

1. **Run the generator** (`Tools/RiftRaiders/Generate Rift Mutation Assets`) - no `.asset` instances
   exist until it (or hand-authoring) is done, same gap every other pool's catalog doc describes.
2. **A `RiftShardOrb` `EntityPrototype` and `RiftShardConfig.asset` still need Editor authoring**
   before Greed's currency half does anything at runtime - `Tools/RiftRaiders/Generate Rift Shard
   Assets` authors the config; the prototype and its `RuntimeConfig` wiring are manual, same
   documented gap `ScrapOrbPrototype` has today.
3. **Every asset's `Icon` is unset** - needs manual per-mutation sprite assignment.
4. **The distance thresholds behind Close Quarters/Longshot (5/12 units) are a placeholder**, not a
   tuned design number - same category as several proc magnitudes across the Weapon Perk roster.
5. **No mutual-exclusion between distinct mutations** - see "Roster" above. Only picking the *same*
   mutation twice is blocked (`RiftMutationPicks`).
