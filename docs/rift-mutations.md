# Rift Mutations catalog

Content for the `LevelUpPoolKind.RiftMutation` pool (`LevelUpConfig.RiftMutations`, see
`docs/level-up-upgrades.md` for the general rolling/pausing/grant mechanism that pool plugs into).
This doc is the design catalog and the pool's own mechanism (non-stacking, reaction signals); it
stays the source of truth for how a Rift Mutation is authored and how it differs from a plain
Global Upgrade.

## What a Rift Mutation is

> A rare, non-stackable, run-wide effect that creates a new rule, synergy, or meaningful trade-off.

The game's level-up pools now split into five categories:

1. **Global Upgrades** (`docs/global-upgrades.md`) - simple hero-wide numerical growth, stacks
   indefinitely (Weapon Damage +10%, Move Speed +10%, ...).
2. **Weapon Perks** (`docs/weapon-perks.md`) - attach to a weapon, lost on swap.
3. **Hero Ascensions** - the existing per-hero `SkillUpgrade`/`PassiveUpgrade` pools (Dash/Hero
   Skill/Passive milestones, `CharacterData.DashSkillUpgrades`/`PassiveUpgrades`/`HeroSkill.Actions`
   - see `docs/level-up-upgrades.md`). Naming only - no separate `LevelUpPoolKind` value.
4. **Rift Mutations** (this doc, "## Roster" below) - rare, non-stackable, build-defining: a one-shot
   tradeoff (Glass Core, Heavy Arsenal), a new reactive rule (Shield Breaker, Critical Focus), or both
   at once (Infinite Momentum). 14 entries.
5. **Rift Mark Mutations** (this doc, "## Rift Mark content pool" below) - a second, independently-
   rollable Rift Mutation pool: 11 mutations that all apply Rift Mark on some trigger condition
   (Critical Fracture, Last Stand, ...). Split from Rift Mutations (its own `LevelUpPoolKind`/
   `LevelUpCategory`/`LevelUpConfig.RiftMarkMutations` list, own weighted roll, own
   `LevelUpConfig.LevelSequence` slot) so a designer can pace/gate the two groups independently -
   e.g. schedule Rift Mark Mutation levels more frequently since they're smaller reactive effects,
   Rift Mutation levels more rarely since they're rare build-defining picks.

Despite being two separate pools, Rift Mutation and Rift Mark Mutation share almost everything else:
both draw from the same `RiftMutationData` catalog, both use the same `RiftMutationPicks`/
`RiftMutationUtility` for non-stack tracking (their assets never overlap, so one shared 32-slot pick
history is correct for both), and Cursed Rift's own reward roll (`LevelUpUtility.RollMutationOptions`,
see the Breathing Phase / Cursed Rift section of `CLAUDE.md`) deliberately keeps drawing **only** from
the core Rift Mutation pool - Rift Mark Mutation is a normal level-up category only, never a Cursed
Rift reward source.

"Non-stackable" is a **pool-wide** property here, not an opt-in per-asset cap the way
`GlobalUpgradeData.MaxPicks` is - every `RiftMutationData` is implicitly picked at most once per
entity (across BOTH pools), enforced by `RiftMutationPicks`/`RiftMutationUtility` below.

**Status: all 25 designed mutations are implemented in code** - the original 14 (`RiftMutationData`
is an abstract base with a real `Apply(Frame f, EntityRef entity)`, same shape as `GlobalUpgradeData`,
dispatched generically by `RiftMutationUtility.Grant`) plus 11 more added in the same pass that built
the **Rift Mark content pool** - see "Rift Mark content pool" below for those 11 and the Weapon Perk
half of that same pool (`docs/weapon-perks.md`). As of 2026-08-14, the 25 are wired into two separate
`LevelUpConfig` lists (`RiftMutations`/`RiftMarkMutations`) rather than one - see "Mechanism" below.

## Mechanism

- **`RiftMutationData : UpgradeData`** (`Assets/_QuantumUser/Simulation/Assets/RiftMutation/
  RiftMutationData.cs` + `.View.cs`) - same `Apply`/`Description`/`DescriptionArgs`/
  `GetFormattedDescription()` shape as `GlobalUpgradeData`, but **no `MaxPicks` field** - see above.
- **`RiftMutationPicks` component** (`LevelUp.qtn`) - `array<AssetRef<RiftMutationData>>[32]
  Picked` (grown from `[16]` when the Rift Mark content pool's 11 mutations pushed the catalog to
  25), this entity's full pick history, **shared across both the Rift Mutation and Rift Mark
  Mutation pools** - not split, since both draw from the same `RiftMutationData` catalog and their
  assets never overlap. Mirrors `GlobalUpgradePicks` but simpler (no per-entry `Count`, since every
  mutation caps at 1).
- **`RiftMutationUtility.cs`** - `Grant(f, entity, mutationRef)` calls `Apply` then always records
  the pick (no `MaxPicks > 0` gate to check first, unlike `GlobalUpgradeUtility.Grant`);
  `IsAlreadyPicked(f, entity, mutationRef)` is what `LevelUpUtility.CollectRiftMutationCandidates`/
  `CollectRiftMarkMutationCandidates` both check before offering one again. Entirely pool-agnostic -
  unchanged by the two-pool split.
- **`LevelUpConfig.RiftMutations`/`RiftMarkMutations : List<AssetRef<RiftMutationData>>`** - two
  separate lists (added 2026-08-14, previously one flat `RiftMutations` list covering all 25) - own
  rarity axis from `WeaponPerkPool`/`GlobalUpgrades`, but the exact same weighted draw
  (`LevelUpConfig.GetWeight`) and the exact same All or Nothing rarity-shift/single-choice override
  as every other pool - "rare" falls out naturally since most mutations are tagged Epic/Legendary,
  no separate pool-frequency knob needed. `RollMutationOptions` (Cursed Rift's reward roll) only ever
  reads `RiftMutations`, never `RiftMarkMutations` - see "What a Rift Mutation is" above.
- **`LevelUpPoolKind.RiftMutation = 5` / `RiftMarkMutation = 7`** (`LevelUp.qtn`) - the `Kind` a
  rolled `LevelUpOption` carries; `LevelUpUtility.GrantOption` dispatches both to the same
  `RiftMutationUtility.Grant` call (identical grant path, only the source list/weighting differs).
  Mirrored by `LevelUpCategory.RiftMutation = 2` / `RiftMarkMutation = 5`, the player-facing
  "which pool does this level draw from" lock used by `LevelUpConfig.LevelSequence`/`Chest.Kind`.
- **Debug grant path** - `GrantRiftMutationCommand`/`RiftMutationSystem` (mirrors
  `GrantGlobalUpgradeCommand`/`GlobalUpgradeSystem` exactly) let any Rift Mutation or Rift Mark
  Mutation be tried out at runtime without a real level-up screen. Three ways in: every
  `RiftMutationData` asset has a "Grant To Local Player" button in its own Inspector while in Play
  Mode (`RiftMutationData.Debug.cs`, same `EditorButtonAttribute` convention as
  `GlobalUpgradeData.Debug.cs`), the `RiftMutationDebugTrigger` component on the `DEBUGGER` GameObject
  in `QuantumGameScene.unity` (next to the existing `GlobalUpgradeDebugTrigger`/
  `WeaponPerkDebugTrigger`), or the "Rift Mutation"/"Rift Mark Mutation" tabs in the in-game
  `DebugUpgradeMenuWindow` (populated by `DebugUpgradeMenuTrigger` alongside its existing Hero/
  Global/Weapon Perk tabs - the Rift Mark tab's own `riftMarkTabButton` still needs manual scene
  wiring, same gap the original Rift tab once had). No revert path exists (same reasoning as Global
  Upgrades) - restart Play Mode to reset a player. **Every `DeterministicCommand` subclass must also
  be registered in `CommandSetup.User.cs`'s `AddCommandFactoriesUser`** (`factories.Add(new
  GrantRiftMutationCommand())`) - Quantum's networking/replay layer can't instantiate/deserialize a
  command it has no factory for, so a missed registration here makes a Send silently do nothing at
  runtime with no compile error to catch it. Known limitation: `RiftMutationSystem`'s debug-grant
  path always records history as `LevelUpPoolKind.RiftMutation` regardless of which pool the granted
  asset actually belongs to (real level-up/Chest grants are unaffected) - low-priority,
  debug-tooling-only.
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

The **Rift Mutation** pool (`LevelUpPoolKind.RiftMutation`/`LevelUpCategory.RiftMutation`,
`LevelUpConfig.RiftMutations`) - 14 entries, the pool Cursed Rift's reward roll draws from
exclusively.

| Mutation | Class | Rarity | Effect |
|---|---|---|---|
| Glass Core | `GlassCoreMutationData` | Legendary | `CharacterStats.MaxShieldMultiplier` ×2 + `MaxHealthMultiplier` set so `Health.MaxHealth` becomes exactly 1 (absolute, not multiplied further - "becomes 1" is a target, not a relative increment). |
| Last Bastion | `LastBastionMutationData` | Legendary | `MaxHealthMultiplier` ×2 + `Shield.Max`/`Current` zeroed directly (bypasses `CharacterSystem.RefreshMaxShield`'s `newMax <= 0` guard on purpose - that guard protects an *unintentional* zero, this one is deliberate). |
| Heavy Arsenal | `HeavyArsenalMutationData` | Epic | `WeaponDamageMultiplier` +75% / `AttackSpeedMultiplier` -35% - character-level mirror of `HeavyCaliberWeaponPerkData`'s tradeoff shape, stacks with that perk. |
| Bullet Storm | `BulletStormMutationData` | Epic | Same two fields as Heavy Arsenal, opposite tuning (+Fire Rate, -Damage). |
| One in the Chamber | `OneInTheChamberMutationData` | Legendary | `Weapon.MagazineSize` = 1 + `WeaponMagazinePositionPerks.FinalRoundDamageBonus` (see `docs/weapon-perks.md`'s component split) - reuses the exact field `FinalRoundWeaponPerkData`/`WeaponSystem.ResolveLiveDamage` already read live off `Ammo == 1`, so every shot at magazine size 1 qualifies for free. Known limitation shared with `MagazineSizeUpgradeData`: a later weapon pickup resets `Weapon.MagazineSize` and removes `WeaponMagazinePositionPerks` entirely (see `WeaponSystem.SeedPerkRoster`) - nothing re-applies Rift Mutations on equip. |
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

## Rift Mark content pool

The **Rift Mark Mutation** pool (`LevelUpPoolKind.RiftMarkMutation`/
`LevelUpCategory.RiftMarkMutation`, `LevelUpConfig.RiftMarkMutations`) - its own independently-
rollable pool as of 2026-08-14 (previously folded into the Rift Mutation pool above). 11 mutations
added in the same pass that built the Weapon Perk half (`docs/weapon-perks.md`) of the Rift Mark
application content pool - see `docs/elemental-reactions.md` for what Rift Mark itself is. Every one
of these bakes a plain `Boolean Has<X>Mutation` flag onto `CharacterStats` at pick time, same
convention every other mutation here already uses - none of them add a tag/marker component. Not a
Cursed Rift reward source - see "What a Rift Mutation is" above.

| Mutation | Class | Rarity | Effect |
|---|---|---|---|
| Critical Fracture | `CriticalFractureMutationData` | Rare | Critical hits from any source (weapon or skill) apply 1 Rift Mark - `RiftMutationMarkUtility.TryCriticalFracture`, per-target cooldown (`RiftMarkCooldownKey.CriticalFracture`, shared with the Weapon Perk of the same name so the two can never both stack from one crit). |
| Skill Fracture | `SkillFractureMutationData` | Rare | Hero Skill hits apply 1 Rift Mark - `RiftMutationMarkUtility.TrySkillFracture`, per-target cooldown so a persistent field/DoT/pulse can't reapply every tick. |
| Rift Dash | `RiftDashMutationData` | Rare | Dashing through an enemy applies 1 Rift Mark, once per enemy per dash - the universal `DashSkillData.Tick` itself gained an overlap-sweep check (gated on the mutation flag), deduped via a fresh-per-`Begin` `RiftDashMarkTracker` component (`array<EntityRef>[8]`+count, same shape Brute's own `IronShoulderHitTracker` uses for its dash ascension). |
| Heavy Fracture | `HeavyFractureMutationData` | Rare | Large hits apply 1 Rift Mark - `RiftMutationMarkUtility.TryHeavyFracture`/`IsHeavyHit` (pure), qualifies on either a flat damage threshold or a percent-of-target's-own-MaxHealth threshold, evaluated per resolved hit only (never aggregated), per-target cooldown. |
| Close Fracture | `CloseFractureMutationData` | Rare | Hits against nearby enemies periodically apply Rift Mark - `RiftMutationMarkUtility.TryCloseOrLongFracture`, plain `FPVector3.Distance` (not squared) against `ElementalReactionConfig.CloseRangeThreshold`, matching `DamageUtility.ResolveRangeDamageMultiplier`'s own convention. |
| Long Fracture | `LongFractureMutationData` | Rare | Mirror of Close Fracture, `LongRangeThreshold` instead. |
| Execution Fracture | `ExecutionFractureMutationData` | Rare | Hitting enemies already below `ExecutionHealthThreshold` (25% MVP default) applies Rift Mark - `RiftMutationMarkUtility.TryExecutionFracture`/`IsBelowExecutionThreshold` (pure), checked against health **before** this hit's own damage, per-target cooldown. |
| First Contact | `FirstContactMutationData` | Rare | The first valid damaging hit against a full-health enemy applies Rift Mark - `RiftMutationMarkUtility.TryFirstContact`, one-time flag (`StatusEffects.FirstContactTriggered`), not a cooldown; only ever fires if the specific hit that happens to land first against a full-health target also comes from a mutation-holding player. |
| Last Stand | `LastStandMutationData` | Epic | Taking a large hit (`LastStandThreshold`, flat damage) releases a Rift pulse marking every nearby enemy, never the player - `RiftMutationMarkUtility.EvaluateLastStand`, called separately from the other 7 (this is the *player's own received* hit, not an enemy's), per-player cooldown (`CharacterStats.LastStandCooldownRemaining`), not per-target. |
| Fractured Presence | `FracturedPresenceMutationData` | Rare | Enemies that remain within `FracturedPresenceRadius` of this player for `FracturedPresenceExposureTime` become Rift-marked - `RiftMutationMarkUtility.TickFracturedPresence`, called once per `StatusEffects`-bearing entity per tick from `StatusEffectSystem.Update` (not damage-hooked). Tracked per (player, enemy) pair on the enemy's own `StatusEffects.FracturedPresenceExposedBy`/`ExposureTime` 4-slot array (same find-or-evict-soonest shape `HasteRemaining`/`HasteSource` already use), per-target cooldown after applying. |
| Overflowing Rift | `OverflowingRiftMutationData` | Epic | Applying Rift Mark to a target already at `MaxStacks` releases a small Rift pulse instead of wasting the application - `RiftMarkApplicationUtility.TryTriggerOverflowingRift`, called from *inside* `ApplyRequest` itself (not a mark-requesting mechanic like the other 10), gated by its own dedicated `StatusEffects.OverflowingRiftCooldownRemaining` (separate from the shared per-mechanic array). Stacks stay clamped, duration still refreshes, deliberately restrained (own `OverflowingRiftPulseDamage`/`Radius` fields, own `OverflowingRiftTriggered` VFX event) - never comparable in strength to a full Rift Reaction, and can't recursively re-trigger since it never calls back into `ApplyRiftMark`/`ApplyRequest`. |

### Application/dedup architecture

- **`RiftMarkApplicationRequest`** (`Assets/_QuantumUser/Simulation/Systems/RiftMarkApplicationUtility.cs`) -
  a plain C# struct (Source/Target/HitSequence/ApplicationSource/RequestedStacks/Owner/CooldownKey),
  not a persisted Quantum component - every request is collected and resolved entirely within one
  hit's synchronous call chain (never crosses a frame boundary), same reasoning `HitEffectContext`
  already uses for its own transient per-hit state.
- **`RiftMarkCooldownKey`** indexes `StatusEffects.MarkApplicationCooldowns[8]` - one shared array of
  per-target cooldown slots for CriticalFracture/SkillFracture/HeavyFracture/CloseFracture/
  LongFracture/ExecutionFracture/FocusedBreach/FracturedPresence, all defaulting to
  `ElementalReactionConfig.StandardMarkApplicationCooldown` (2s MVP) unless a mechanic overrides.
  Mechanics with their own dedupe shape (Fracture Rounds' hit counter, Unstable Payload's
  once-per-explosion-by-construction, Rift Dash's per-dash tracker, First Contact's one-time flag,
  Last Stand's per-player cooldown, Overflowing Rift's own field) pass `RiftMarkCooldownKey.None`
  instead.
- **`RiftMarkApplicationUtility.TryConsumeCooldown`/`ApplyRequest`** - the shared checked-then-set
  atomic gate (identical shape to every `*CooldownRemaining` check in `StatusEffectUtility`) plus the
  actual `ApplyRiftMark` call + Overflowing Rift branch. This single mechanism is what makes "Weapon
  Critical Fracture and global Critical Fracture never both stack from one crit" fall out for free -
  both request through the same `RiftMarkCooldownKey.CriticalFracture` slot, so whichever runs first
  within a hit wins it and the other sees it already consumed.
- **`RiftMutationMarkUtility.EvaluateOnDamage`** - the single per-hit orchestrator for
  First Contact/Execution/Skill/Critical(mutation)/Heavy/Close/Long Fracture, called once from
  `DamageUtility.ApplyDamage` (after damage/crit resolve, before health is subtracted, so pre-hit
  health/distance are both still live), gated to real combat hits (`Weapon`/`Skill` source, excluding
  DoT-tick replays). Evaluates every qualifying mutation in a fixed, most-narrow-first priority order
  and requests **at most one** application - "prefer one Rift Mark application per hit event" (see
  the original design brief). A coincidental overlap between this evaluation and a *separate* Weapon
  Perk on the same physical hit (e.g. Fracture Rounds' 6th shot also happening to be a Heavy hit) is
  a known MVP simplification, not deduped - only Critical Fracture's perk/mutation pair is
  guaranteed not to double-fire, via the shared cooldown key above.

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
Mutation Assets`) authors one `.asset` instance per class in the rosters above - tuned to this doc's
own numbers - under `Assets/_QuantumUser/Resources/LevelUp/RiftMutation/` (created automatically if
missing, same flat folder for both pools), and wires them into `LevelUpConfig.asset`'s two lists:
the 14 core specs into `RiftMutations`, the 11 specs tagged `MutationSpec.RiftMarkPool = true` into
`RiftMarkMutations`. Mirrors `GlobalUpgradeAssetGenerator.cs`/`WeaponPerkAssetGenerator.cs` exactly:
re-running is safe, existing assets are updated in place, both lists are rebuilt from scratch each
run. `Icon` is left unset for every asset - manual per-mutation Inspector step, same as every other
pool's generator.

## Current status / known simplifications

The code compiles and every mutation (25, including the 11-strong Rift Mark content pool) has a
class + `RiftMutationAssetGenerator` spec, but:

1. **Re-run the generator** (`Tools/RiftRaiders/Generate Rift Mutation Assets`) after the 2026-08-14
   two-pool split - `LevelUpConfig.asset`'s on-disk `RiftMutations` list still holds all 25 GUIDs
   from before the split (11 of which now belong in `RiftMarkMutations`) until this runs again. In
   that window, the Rift Mutation category still (incorrectly) offers all 25 and Rift Mark Mutation
   offers nothing (0 candidates falls back to the existing mixed-pool roll, not an error - see
   `RollOptionsFor`'s empty-category fallback).
2. **A `RiftShardOrb` `EntityPrototype` and `RiftShardConfig.asset` still need Editor authoring**
   before Greed's currency half does anything at runtime - `Tools/RiftRaiders/Generate Rift Shard
   Assets` authors the config; the prototype and its `RuntimeConfig` wiring are manual, same
   documented gap `ScrapOrbPrototype` has today.
3. **Every asset's `Icon` is unset** - needs manual per-mutation sprite assignment.
4. **The distance thresholds behind Close Quarters/Longshot (5/12 units) are a placeholder**, not a
   tuned design number - same category as several proc magnitudes across the Weapon Perk roster.
5. **No mutual-exclusion between distinct mutations** - see "Roster" above. Only picking the *same*
   mutation twice is blocked (`RiftMutationPicks`).
6. **Rift Mark content pool - no automated coverage for the Frame-dependent half** - cooldown-key
   gating, the priority-ordered dispatch, Rift Dash's overlap sweep, and Fractured Presence's
   exposure accumulator all need a live `StatusEffects*`/`Frame` this project has no simulation test
   harness for; only the two genuinely pure pieces (`RiftMutationMarkUtility.IsHeavyHit`/
   `IsBelowExecutionThreshold`) have EditMode tests
   (`Assets/_QuantumUser/Editor/Tests/RiftMarkApplicationTests.cs`) - verify the rest manually
   in-Editor, same gap `docs/elemental-reactions.md`'s own "Current status" already documents for the
   core mechanic.
7. **Cross-mechanic mark-application dedup is scoped to within each evaluation point, not global** -
   `RiftMutationMarkUtility.EvaluateOnDamage`'s own 7 damage-hook mutations are fully deduped against
   each other (one priority-ordered pass, at most one application), and Critical Fracture's Weapon
   Perk/Mutation pair is deduped via a shared cooldown key, but a coincidental overlap between a
   Weapon Perk and an *unrelated* mutation on the same physical hit isn't - see "Application/dedup
   architecture" above.
8. **Fractured Presence's per-tick proximity scan is O(enemies × players)**, not spatially
   partitioned - acceptable at this project's co-op player count, would need revisiting before a
   much larger concurrent-entity count.
9. **`Weapon.FocusedBreachContactTime` only resets on an explicit miss or a target change**, not on a
   continuous per-tick decay while simply not firing - a reasonable MVP reading of "losing contact
   resets or rapidly decays progress" given Hitscan firing is already discrete, not a true
   continuous-beam decay.
10. **Two-pool split (2026-08-14) - Editor wiring still needed**: `HeroInfoPopupWidget.riftMarkContent`
    (the tab-hold party summary popup's new 4th list) has no null guard and needs a ScrollRect
    Content Transform authored in the scene before this ships live, or a Rift Mark Mutation pick
    will NRE the popup the first time it tries to render. `DebugUpgradeMenuWindow.riftMarkTabButton`
    (the debug menu's new 5th tab) is safe to leave unwired for now - degrades to a `LogHelper.Warn`,
    same as the original Rift tab once did. Separately, whether/where
    `LevelUpCategory.RiftMarkMutation` appears in `LevelUpConfig.asset`'s `LevelSequence` is an
    open design/tuning decision - the split only makes the category legally selectable, it doesn't
    schedule it into a run.
