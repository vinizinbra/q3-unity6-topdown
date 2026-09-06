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
4. **Rift Mutations** (this doc, "## Roster" below) - rare, non-stackable, build-defining: a one-shot
   tradeoff (Glass Core, Heavy Arsenal), a new reactive rule (Adrenaline Kick, Critical Focus), a
   run-wide encounter/economy change (Overpopulation, Greed), or a mix. 27 entries.

"Non-stackable" is a **pool-wide** property here, not an opt-in per-asset cap the way
`GlobalUpgradeData.MaxPicks` is - every `RiftMutationData` is implicitly picked at most once per
entity, enforced by `RiftMutationPicks`/`RiftMutationUtility` below.

**Status: every designed mutation in the roster below is implemented in code** - `RiftMutationData`
is an abstract base with a real `Apply(Frame f, EntityRef entity)`, same shape as `GlobalUpgradeData`,
dispatched generically by `RiftMutationUtility.Grant`. They are wired into a single `LevelUpConfig`
list (`RiftMutations`) - see "Mechanism" below.

## Mechanism

- **`RiftMutationData : UpgradeData`** (`Assets/_QuantumUser/Simulation/Assets/RiftMutation/
  RiftMutationData.cs` + `.View.cs`) - same `Apply`/`Description`/`DescriptionArgs`/
  `GetFormattedDescription()` shape as `GlobalUpgradeData`, but **no `MaxPicks` field** - see above.
- **`RiftMutationPicks` component** (`LevelUp.qtn`) - `array<AssetRef<RiftMutationData>>[48]
  Picked`, this entity's full pick history. Mirrors `GlobalUpgradePicks` but simpler (no per-entry
  `Count`, since every mutation caps at 1).
- **`RiftMutationUtility.cs`** - `Grant(f, entity, mutationRef)` calls `Apply`, records the pick, and
  (for a `Run`-scope mutation) records it run-wide as well. `IsBlocked(f, entity, mutationRef)` is the
  single offer-eligibility gate - already picked, run-scope duplicate, or incompatible with something
  owned - checked by `LevelUpUtility.CollectRiftMutationCandidates`
  and re-checked by `Grant` itself so the debug-grant path is covered too. `IsAlreadyPicked` remains
  public for the debug menu's own "already granted" display.
- **`LevelUpConfig.RiftMutations : List<AssetRef<RiftMutationData>>`** - own rarity axis from
  `WeaponPerkPool`/`GlobalUpgrades`, but the exact same weighted draw (`LevelUpConfig.GetWeight`) and
  the exact same All or Nothing rarity-shift/single-choice override as every other pool - "rare" falls
  out naturally since most mutations are tagged Epic/Legendary, no separate pool-frequency knob
  needed. `RollMutationOptions` (Cursed Rift's reward roll) reads this same list - see "What a Rift
  Mutation is" above.
- **`LevelUpPoolKind.RiftMutation = 5`** (`LevelUp.qtn`) - the `Kind` a rolled `LevelUpOption`
  carries; `LevelUpUtility.GrantOption` dispatches it to `RiftMutationUtility.Grant`.
  Mirrored by `LevelUpCategory.RiftMutation = 2`, the player-facing "which pool does this level
  draw from" lock used by `LevelUpConfig.LevelSequence`/`Chest.Kind`.
- **Debug grant path** - `GrantRiftMutationCommand`/`RiftMutationSystem` (mirrors
  `GrantGlobalUpgradeCommand`/`GlobalUpgradeSystem` exactly) let any Rift Mutation be tried out at
  runtime without a real level-up screen. Three ways in: every `RiftMutationData` asset has a
  "Grant To Local Player" button in its own Inspector while in Play Mode (`RiftMutationData.Debug.cs`,
  same `EditorButtonAttribute` convention as `GlobalUpgradeData.Debug.cs`), the
  `RiftMutationDebugTrigger` component on the `DEBUGGER` GameObject in `QuantumGameScene.unity` (next
  to the existing `GlobalUpgradeDebugTrigger`/`WeaponPerkDebugTrigger`), or the "Rift Mutation" tab in
  the in-game `DebugUpgradeMenuWindow` (populated by `DebugUpgradeMenuTrigger` alongside its existing
  Hero/Global/Weapon Perk tabs). No revert path exists (same reasoning as Global Upgrades) - restart
  Play Mode to reset a player. **Every `DeterministicCommand` subclass must also be registered in
  `CommandSetup.User.cs`'s `AddCommandFactoriesUser`** (`factories.Add(new
  GrantRiftMutationCommand())`) - Quantum's networking/replay layer can't instantiate/deserialize a
  command it has no factory for, so a missed registration here makes a Send silently do nothing at
  runtime with no compile error to catch it.
- **`RiftMutationReactionSystem`** (`Assets/_QuantumUser/Simulation/Systems/`, registered in
  `SystemSetup.User.cs` next to `WeaponPerkReactionSystem`) - the handful of mutations that need
  more than a one-shot `CharacterStats` bake react here, off three signals:
  - `ISignalOnCriticalHit` (`Combat.qtn`, already existed) - **not** gated to `DamageSource.Weapon`
    the way `WeaponPerkReactionSystem`'s own handler is, since these are character-level effects
    meant to fire on any crit source.
  - `ISignalOnEntityKilled` (`Combat.qtn`, already existed) - Close Quarters' close-kill speed burst.
  - `ISignalOnAccessoryBlocked` (`Accessory/AccessoryGuard.qtn`, new 2026-08-27) - Adrenaline Kick.
    Fires only on a genuine block; see "Accessory primitives" below.

  `ISignalOnShieldBroken` (`Shield.qtn`) no longer has a listener here - Shield Breaker was replaced
  by Adrenaline Kick. The signal itself is left declared; Shield is otherwise untouched by this system.
- **`DamageUtility.ResolveRangeDamageMultiplier(f, owner, target, stats)`** - attacker-side range
  falloff for Close Quarters/Longshot, called from `ResolveOutgoingDamage` right after the existing
  `DamageMultiplier * GetSourceMultiplier(...)` line, same shape/placement as the existing
  target-side `ResolveFrontalDamageMultiplier`. Lerps between `CharacterStats.
  NearDamageMultiplier`/`FarDamageMultiplier` off the flat attacker-target distance, against fixed
  design-constant thresholds (5/12 units) - not per-asset tunable yet, a placeholder starting point
  for balance passes like several other proc magnitudes already in this codebase.

## Roster

The **Rift Mutation** pool (`LevelUpPoolKind.RiftMutation`/`LevelUpCategory.RiftMutation`,
`LevelUpConfig.RiftMutations`) - 27 entries, the pool Cursed Rift's reward roll draws from
exclusively. Rewritten and expanded 2026-08-27 - see "2026-08-27 pass" below for what changed and why.

`Scope` is `Player` unless stated - a `Run` mutation changes shared simulation state and is applied
exactly once per run no matter how many players are offered it (see "Player vs Run scope" below).

| Mutation | Class | Rarity | Scope | Effect |
|---|---|---|---|---|
| Glass Core | `GlassCoreMutationData` | Legendary | Player | Accessory max durability x2 (`AccessoryGuardUtility.ScaleMaxDurability`, so 3 -> 6) + Max Health x0.5. Both are multipliers, so it composes with other Max Health picks instead of overwriting them. Keeps working across recovery/repair/replacement for free, since `Restore` sets current from max. |
| Last Bastion | `LastBastionMutationData` | Legendary | Player | `MaxHealthMultiplier` x2 + `AccessoryGuardUtility.Disable` - an explicit `AccessoryGuard.Disabled` availability flag, NOT durability pinned at 0, which is what makes the Store correctly stop offering a service. |
| Heavy Arsenal | `HeavyArsenalMutationData` | Epic | Player | +60% Weapon Damage / -30% Fire Rate / +50% Knockback / 15% chance to stagger for 0.5s. The stagger is one generic roll in `DamageUtility.TryApplyWeaponStagger`, routed through `StatusEffectUtility.ApplyStun` so per-tier hard-CC immunity applies with no tier check of its own. |
| Bullet Storm | `BulletStormMutationData` | Epic | Player | +50% Fire Rate / +50% Magazine Size / -30% Weapon Damage / -25% Reload Speed. Magazine goes through `CharacterStats.MagazineSizeBonus`, re-applied on every equip - see "Weapon-stat pipeline" below. |
| One in the Chamber | `OneInTheChamberMutationData` | Legendary | Player | `CharacterStats.MagazineSizeOverride = 1` + `WeaponDamageMultiplier` x5. The override beats any magazine bonus, so it still means exactly one round alongside Bullet Storm. Both halves survive a weapon swap - the previous implementation wrote `Weapon.MagazineSize`/`WeaponMagazinePositionPerks` directly and was silently wiped by the next pickup. |
| Close Quarters | `CloseQuartersMutationData` | Rare | Player | +50% damage within 5 units / -30% beyond 10 (`DamageUtility.ResolveRangeDamageMultiplier`, lerped between) + a close KILL grants +20% Move Speed for 2s (`RiftMutationReactionSystem.OnEntityKilled` -> `StatusEffectUtility.ApplyTempMoveSpeed`, which overwrites on reapply, so repeat kills refresh rather than stack). |
| Longshot | `LongshotMutationData` | Rare | Player | Up to +50% damage at range / -25% within 5 units, **plus +1 Pierce on a shot taken beyond 10 units**. Deliberately not Close Quarters' mirror: the pierce makes distance a different way to shoot, not the same play inverted. Granted per SHOT at fire time (`WeaponSystem.ResolveLongRangePierceBonus`), since pierce belongs to the projectile/hitscan walk, not to an individual hit. |
| Ultimate Commitment | `UltimateCommitmentMutationData` | Epic | Player | `SkillDamageMultiplier` x2 / `SkillCooldownMultiplier` x0.5 - that field is a *rate* (`StatUtility.GetSkillCooldown` divides by it), so halving it doubles the actual cooldown duration. Hero-Skill-scoped; Dash has its own field. |
| Focused Power | `FocusedPowerMutationData` | Epic | Player | `AreaRadiusMultiplier` x0.5 + `SkillCenterFocusBonus` 1.5 - skill damage climbs to +150% at the exact center of an area, falling to none at the rim. See "Skill center focus" below. |
| Infinite Momentum | `InfiniteMomentumMutationData` | Epic | Player | While Dash is on cooldown, keep Dashing for 5% of Max Health each time - **unlimited**, refused only at exactly 1 health (the floor) - below full price you still get the Dash and are left at 1, since spending your last sliver to escape is the point. Direct health write, never `DamageUtility.ApplyDamage`. A fraction of MAX health, so the cost stays meaningful against any build - including Glass Core's halved pool. |
| Critical Focus | `CriticalFocusMutationData` | Epic | Player | Every 3 crits, -1s on BOTH Hero Skill and Dash. A deterministic crit COUNT (`CharacterStats.CritFocusProgress`), reset on trigger - not a hidden real-time internal cooldown. DoT-tick replays are excluded for free, since `OnCriticalHit` only fires from the real resolution path. |
| Adrenaline Kick | `AdrenalineKickMutationData` | Epic | Player | An Accessory block resets Dash AND cuts 50% off the Hero Skill's **remaining** cooldown (8s left -> 4s left), not off its base. Replaces Shield Breaker, whose trigger (a player Shield breaking) became unreachable for most heroes. Reacts to `OnAccessoryBlocked`, which fires ONLY on a genuine block - never on recovery, purchase or a non-block destruction. |
| Spare Parts | `SparePartsMutationData` | Epic | Player | Once per run, a destroyed Accessory instantly returns with 2 durability, bypassing the wait-for-Breathing rule. Backed by the generic `AccessoryEmergencyReserve` component; nothing ever refills `Charges`, so "once per run, not reset at a Break, not re-armed by a repair" is structural rather than policed. |
| Danger Pay | `DangerPayMutationData` | Epic | Player | Below 40% Max Health: +35% ALL damage, +20% Move Speed. A live CONDITION, not a timed buff - `MutationModifierUtility.IsInDanger` is re-evaluated at every damage resolution and every movement tick, so healing back over the line removes both halves immediately with nothing to expire. |
| Overkill | `OverkillMutationData` | Epic | Player | Damage dealt beyond a killed enemy's remaining health detonates at the corpse for 50% of the excess (radius 3). The excess needed no new plumbing - `DamageUtility`'s unclamped post-hit health already IS it; it is only captured before Cheat Death rewrites it. See "Overkill recursion" below. |
| Scavenger Rush | `ScavengerRushMutationData` | Rare | Player | 5 collectibles within 3s grants +30% Move Speed and +30% Fire Rate for 4s. Listens to the generic `OnCollectibleCollected`, which fires only from the currency-orb path - so Accessory recoveries, Merchant purchases and static interactables are excluded structurally, not by a list. Buff rides the shared timed-buff slots, so it refreshes rather than stacks. |
| Blood Money | `BloodMoneyMutationData` | Legendary | Player | +50% Coins, but lose 10% of your CURRENT Coins whenever you actually lose health. The gain half rides the per-player `CoinGainMultiplier` rather than scaling the world drop - coin drops are shared in co-op, so scaling the drop would hand the mutation to the whole team. The loss reacts to `OnHealthDamageApplied`, so an Accessory-blocked hit and a Shield-only hit both cost nothing. |
| No Safety Net | `NoSafetyNetMutationData` | Legendary | Player | +75% ALL damage while the Accessory is Airborne/Dropped/Broken. Tracks no state of its own - reads `AccessoryGuard.State` via `AccessoryGuardUtility.IsExposed` at every hit. Deliberately inert (and unofferable) for a player whose Accessory was removed by Last Bastion, which would otherwise be a permanent free bonus. |
| Second Wind | `SecondWindMutationData` | Epic | Player | Recovering your Accessory heals 5% Max Health. Reacts to `OnAccessoryRecovered`, which fires only on a real world recovery - so a Merchant repair/replacement never heals, re-touching the collectible can't farm it, and a teammate returning it heals the OWNER. Once per drop cycle falls out of the guard's own state machine. |
| Dead Weight | `DeadWeightMutationData` | Legendary | Player | +50% Weapon Damage, Dash hard-capped at 1 charge, Dash cooldown x1.5. The cap is `min(MaxStacks, cap)` at every availability read, never a subtraction - see "Dead Weight hard cap" below. |
| Pressure Cooker | `PressureCookerMutationData` | Epic | Player | +3% ALL damage per full second without taking damage, capped at +30%; any real hit resets it. Counter is `CharacterStats.SafeTimeSeconds`, advanced off `f.DeltaTime` in `MutationTimerUtility` - deterministic, never a View timer. An Accessory block leaves the streak intact (it never reaches a damage signal); **a Shield-only hit DOES reset it**, by explicit request - this deviates from the original brief, which asked for Shield-only hits to be ignored. |
| Money Talks | `MoneyTalksMutationData` | Epic | Player | +5% ALL damage per full 100 Coins currently carried, capped at +40% (800 Coins). Resolved **live** per hit by `CoinUtility.ResolveDamageBonus` from `DamageUtility.ResolveOutgoingDamage` - never baked at pick time, because the point is that it climbs as you save and drops the moment you spend at the Store. Stepped per whole 100 so the next breakpoint is a number the player can aim at. |
| Greed | `GreedMutationData` | Legendary | **Run** | +100% Rift Shards for the whole team (`Global.RiftShardGainBonus`, applied by `RiftShardUtility.GrantAll` before each player's own multiplier) + `Global.EnemyMaxHealthBonus` +50%. Team-wide reward because the drawback is team-wide. |
| Overpopulation | `OverpopulationMutationData` | Epic | **Run** | Spawn density +40% / enemy Max Health -25%. Bosses are exempt from the penalty - not by a check in the mutation, but because `ResolveEnemyHealthMultiplier` ignores a *negative* run-wide bonus for `EnemyTier.Boss` as a general rule. |
| Elite Territory | `EliteTerritoryMutationData` | Legendary | **Run** | Spawn density -30% / Elite-bearing groups weighted x2.5. Pure spawn *selection* weighting via `EncounterModifierUtility.ResolveGroupWeightMultiplier` reusing `CombatDirectorUtility.GroupContainsMajor` - nothing substitutes or upgrades enemy types. Boss spawning is untouched (a Boss phase never pulses). |
| Blood Tithe | `BloodTitheMutationData` | Epic | **Run** | +75% Rift Shards team-wide / enemies deal +25% damage. A different axis from Greed: that one pays in enemy HEALTH (longer fights), this one in enemy DAMAGE (riskier ones). Read live, so it affects enemies already on screen. |
| Escalation | `EscalationMutationData` | Epic | **Run** | Spawn density ramps 1.0x -> 1.75x across each Combat/Elite phase, resetting at the next. Derived from the phase's own `PhaseTimer / Duration`, so it is deterministic and resets for free when `SurvivalProgressionUtility.Tick` zeroes `PhaseTimer`. Breathing/Boss phases excluded. |

Distinct mutations are non-exclusive by default - nothing stops a build from picking directly
opposing tradeoffs (Heavy Arsenal + Bullet Storm, Close Quarters + Longshot); they just partially
cancel, which is a fair outcome of the player's own choice. **No exclusive pair is currently
authored** - the mechanism exists and is data-driven (see "Mutation incompatibility" below), but
nothing in the present roster needs it.


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
missing), and wires them into `LevelUpConfig.asset`'s `RiftMutations` list. Mirrors
`GlobalUpgradeAssetGenerator.cs`/`WeaponPerkAssetGenerator.cs` exactly: re-running is safe, existing
assets are updated in place, the list is rebuilt from scratch each run. `Icon` is left unset for
every asset - manual per-mutation Inspector step, same as every other pool's generator.

## 2026-08-27 pass - Accessory rework, run-scope mutations, generic primitives

The core pool was authored before the **Accessory Guard** replaced Shield as the player's defensive
system (`docs/accessory-guard.md`). Four mutations still read or wrote `Shield`, so they were dead or
misleading; the pool was also almost entirely flat stat bumps, with nothing that changed how a run
itself plays. This pass rewrote 9 mutations, deleted 2, added 7, and - the architectural half - added
the generic primitives they all sit on. **The Shield system itself is untouched**: only Rift Mutations
lost their Shield dependencies.

**Removed:** `ShieldBreakerMutationData` (its trigger, a player Shield breaking, is unreachable for
most heroes now) and `ImpactDriveMutationData` (a strict subset of Adrenaline Kick, which already
resets the Dash) - both roles are covered by Adrenaline Kick. `AllOrNothingMutationData` - cut by request; since
`CharacterStats.AllOrNothingActive` was the only consumer of `LevelUpUtility`'s single-choice /
rarity-shift machinery, that whole mechanism came out with it (the `bool rarityShift` parameter
threaded through `AddCandidate`/`ResolveWeight`/every `Collect*Candidates`, and the
`choiceCount = allOrNothing ? 1 : ...` branches). `DrawWeighted` and `LevelUpConfig.GetWeight` are
unchanged - the normal 3-card roll is unaffected.

### Player vs Run scope

`RiftMutationData.Scope` (`MutationScope.Player`/`Run`). A Player-scope mutation writes only its
picker's own components, so two players independently picking it is fine. A Run-scope mutation writes
`Frame.Global`, so applying it twice would silently double a run-wide difficulty or economy modifier.

`Frame.Global.RunMutationPicks` (`QTN/RiftMutation/RunMutations.qtn`) is the guard - `RiftMutationUtility.Grant`
records Run-scope picks there and `IsBlocked` rejects a second application, so co-op determinism
needs no thought at any call site. `RiftMutationPicks` still records the pick on whoever took it, so
their own history/HUD reads correctly.

### Run-wide encounter modifiers

`RunMutations.qtn` holds every run-wide modifier; `EncounterModifierUtility` is the single reader -
nothing touches those fields directly. All are BONUSES defaulting to 0 and read as `1 + bonus`, so an
untouched run returns exactly 1 everywhere and every consumer behaves bit-for-bit as before. This
replaced `RiftShards.qtn`'s lone `EnemyHealthBonusMultiplier`, which was Greed's difficulty side
effect parked in the currency's block for lack of a better home.

Consumers, all one-line insertions next to an existing multiplier:

- **Enemy Max Health** - `EnemySystem.SeedHealth`, baked per spawn. Tier-aware: a *negative* total is
  ignored for `EnemyTier.Boss`.
- **Enemy damage** - `HitEffectUtility.ScaleByEnemyDamageMultiplier`, read live. Also added to
  `ApplyDamageInRadius`, which bypassed that helper entirely - a pre-existing gap that would have let
  an enemy's own `ExplodeOnDeath` ignore both this and its per-spawn co-op multiplier.
- **Spawn density** - scales all THREE Director levers together, exactly as the pre-existing
  `SplitThreatMultiplier` already does: budget accrual (`ResolveBudgetMultiplier`), `maxAlive`
  (`TryPulse`), and `TargetPressure` (`PlayerClusterDirectorUtility.BuildAnchors`). Scaling one alone
  just moves the bottleneck to the other two.
- **Elite weighting** - `CombatDirectorUtility.TrySelectSpawn`'s weighted roll, reusing that file's
  own `GroupContainsMajor` (widened `private` -> `internal`, no behaviour change) so "major group" has
  one definition rather than two that could drift.
- **Rift Shard gain** - `RiftShardUtility.GrantAll`, applied to the base amount before each player's
  own multiplier so run-wide and personal bonuses compose multiplicatively.

### Accessory primitives

All hero-agnostic and mutation-agnostic:

- **`signal OnAccessoryBlocked(owner, attacker, broken)`** - fired by `AccessoryGuardUtility.TryBlock`
  in every branch (dropped / degraded-in-place / broken), and ONLY on a genuine block. Deliberately
  the only new signal: the brief's `OnAccessoryDropped`/`Destroyed`/`Recovered`/`Purchased` are
  already covered by existing View-facing events with no simulation consumer.
- **`AccessoryGuard.Disabled`** - a real availability flag (Last Bastion), honoured by `TryBlock`,
  `Restore` and `AccessoryServiceUtility.ResolveService`. Zero-default is "enabled", same convention
  as `State`'s own `Equipped = 0`.
- **`AccessoryEmergencyReserve { Charges, RestoreDurability }`** - a would-be break instead consumes a
  charge and puts the accessory straight back on, with no world collectible spawned. Nothing ever
  refills `Charges`, which is what makes Spare Parts' "once per run, not reset at a Break, not
  re-armed by a repair" structural rather than a rule some system polices.
- **`AccessoryGuardUtility.ScaleMaxDurability`/`Disable`** - Glass Core and Last Bastion's two hooks.

### Weapon-stat pipeline

`Weapon.MagazineSize` is a BAKED absolute that `WeaponSystem.SeedStats` resets on every `Equip`, which
is why the old One in the Chamber silently stopped working the first time a player picked up a weapon.
`WeaponSystem.ApplyOwnerWeaponModifiers` is a new stage of `Equip`, run after perks and hero
modifiers, applying `CharacterStats.MagazineSizeBonus`/`MagazineSizeOverride` (override wins). It is
also called directly from the two mutations that write those fields so a mid-run pick affects the
weapon already in hand - the same dual-call precedent `ApplyPixieExplosiveWeapon` already set.

The full order is: `WeaponDataAsset` -> `SeedStats` -> `ApplyPerks` -> hero modifiers ->
**`ApplyOwnerWeaponModifiers`** -> live per-shot resolution. Damage, fire rate, reload and pierce need
nothing here - they are already live-resolved `CharacterStats` multipliers and survive a swap for free.

### Skill center focus

`HitEffectContext` gained `AreaCenter`/`AreaRadius`, populated by `HitEffectUtility.ApplyInRadius` (its
own sphere) and `ApplyInCollider` (a spherical area collider only). `SkillFocusUtility.ResolveCenterFocusMultiplier`
scales `context.Damage` in `ApplyToTarget`, beside the existing enemy-damage scaling.

`AreaRadius == 0` is the explicit "no meaningful spatial area" reading - a direct hit, a single-target
cast, a swept volume, a non-spherical collider. That is what lets Focused Power work on every hero's
area skills with no hero checks and degrade to an exact no-op elsewhere.

### Other generic primitives

- **`SkillSystem.ResetCooldown`** - full stacks, no cooldown. Idempotent, which is exactly what makes
  two independent sources firing on the same event resolve to one ready Dash rather than banked charges.
- **Emergency activation** - `SkillSystem.TryPayEmergencyActivation` (Infinite Momentum). Costs no
  stack, does not touch the running cooldown, and pays in a direct health write rather than through
  `DamageUtility.ApplyDamage` - so it cannot roll crit, proc a status, count as hostile damage,
  interrupt a revive channel, or cost an Accessory durability point. **Unlimited, refused only at the
  1-health floor** - anywhere above it the Dash is granted and the player pays what they can, landing
  at 1 if they couldn't cover the full price. Two rejected alternatives, both wrong in opposite
  directions: clamping the result at 1 with no gate makes every Dash at 1 health FREE (unlimited
  free mobility), while requiring the full price to leave 1 behind refuses the Dash exactly when a
  low player needs to escape - the one situation the mutation exists for.
- **Weapon stagger** - `CharacterStats.WeaponStaggerChance`/`Duration`, rolled once in
  `DamageUtility.TryApplyWeaponStagger` beside `OnWeaponHitLanded`, so it covers hitscan, pellets,
  projectiles and explosive procs uniformly and excludes DoT-tick replays for free. Routed through
  `StatusEffectUtility.ApplyStun`, which already owns per-tier hard-CC immunity.
- **Range thresholds** - `DamageUtility.RangeDamageNearThreshold`/`FarThreshold` (5 / 10, was 5 / 12)
  widened to `internal`, so Longshot's pierce and Close Quarters' kill burst key off the same two
  numbers the damage falloff does rather than second copies.

### Dead Weight hard cap

The single most load-bearing detail of this pass. `SkillSlot.MaxStacks` keeps whatever charge
upgrades accumulated into it; `SkillSystem.ResolveEffectiveMaxStacks(f, owner, slotId, slot)` returns
`min(MaxStacks, CharacterStats.DashChargeHardCap)` (0 = uncapped, the codebase's standard ceiling
convention) and is read at **every point that decides availability**: `TickCooldown`'s two recovery
gates, `ResetCooldown`, and `FinishSkill`'s re-arm gate.

Why a cap and not `MaxStacks -= 1`:

- an already-owned "+1 Dash Charge" **stays owned** and simply stops mattering, exactly as the brief
  requires - a subtraction would destroy that information irreversibly;
- the cap stays authoritative no matter how many charges are stacked afterwards (calculated 3 ->
  effective 1);
- removing the cap would restore the real value exactly.

It also leaves RESTORE and BYPASS mechanics working, because neither raises the ceiling: Adrenaline
Kick's Dash reset hands back the one charge you have, and Infinite Momentum's paid Dash skips the
charge check entirely. **Neither is a conflict**, and neither needed a special case.

Offering is handled separately: `DashChargeUpgradeData.IsEligible` returns false while a cap is in
effect, so a +Charge card that could no longer do anything stops appearing. It tests the CAPABILITY
("is my ceiling capped?") rather than naming Dead Weight, so any future capping source suppresses it
for free.

### Overkill recursion

`DamageUtility` already computes the unclamped post-hit health, which **is** the excess damage - so
Overkill needed no restructuring of the damage pipeline, only a capture before `CheatDeathUtility`
rewrites that value to 1 and before `CurrentHealth` is clamped to 0.

The blast is raised with `isChainedExplosion: true`, and `OverkillUtility.TryDetonate` refuses to run
for a hit that was already chained. That is the whole recursion brake, and it deliberately reuses the
exact flag Pixie's Chain Reaction already terminates on rather than introducing a depth counter - so
both mechanics share one definition of "this hit is already a knock-on". A blast can kill, but its
kills can never blast again.

It also fires strictly AFTER `OnEntityKilled`/`EntityDied`, so normal kill attribution, drops and
on-kill reactions all resolve against the original hit first.

### Prerequisite gating

`IsEligible(Frame, EntityRef)` now exists on `GlobalUpgradeData` and `RiftMutationData` too, matching
the hook `PassiveUpgradeData`/`SkillActionData` already had. Default `true`, so nothing existing
changed. For mutations it is checked inside `RiftMutationUtility.IsBlocked`, which means one override
covers level-ups, Chests, Cursed Rift rewards and the debug-grant path at once.

The codebase's established idiom for "does this player have capability X" is a state/marker query via
`f.Has<T>` (see `FlashpointPassiveUpgradeData` checking `CanApplyBurn`), not a string tag - so the
Accessory-dependent mutations (No Safety Net, Second Wind, Spare Parts) gate on
`AccessoryGuardUtility.IsAvailable`, which is false once Last Bastion has removed the Accessory
outright.

### Mutation incompatibility

`RiftMutationData.IncompatibleWith` (a `List<AssetRef<RiftMutationData>>`), checked by
`RiftMutationUtility.IsBlocked` **symmetrically** - the candidate may list an owned mutation, or an
owned mutation may list the candidate - so an exclusive pair only needs authoring on one of its two
sides, and cannot half-work because someone forgot to mirror it.

`IsBlocked` is the single gate for all three offer rules (already picked, run-scope duplicate,
incompatible) and is wired into `CollectRiftMutationCandidates`, which covers normal level-ups,
Chests and Cursed Rift's `RollMutationOptions` at once since they share that collector. `Grant`
re-checks it, covering the debug-grant path too.

**Known simplification:** the filter is against *already-owned* mutations, so two mutually exclusive
cards can still appear on the same screen. That is harmless - only one can be picked, and the other
becomes unofferable immediately after - and avoiding it would mean making the pool-generic
`DrawWeighted` mutation-aware.

### Debugging

`RiftMutationDebugUtility` - log-only, no UI. `LogPlayerState` dumps a player's owned mutations,
Accessory current/max/state/disabled, Spare Parts charges remaining, the Critical Focus counter,
emergency-dash availability, and the final weapon stats after the full resolution pipeline.
`LogRunState` dumps every run-wide field plus the multipliers they actually resolve to. Both are called
from `Grant`; `LogFiltered` fires from `IsBlocked` naming the mutation and the reason - which is the
single most useful line here, since an incompatibility or run-scope duplicate is otherwise completely
invisible (the mutation just quietly stops appearing).


## Current status / known simplifications

Every mutation in the roster above has a class and a `RiftMutationAssetGenerator` spec. **Nothing
from the 2026-08-27 pass has been verified in-Editor yet**, and the changed `.qtn` files
(`CharacterStats.qtn`, `LevelUp.qtn`, `Accessory/AccessoryGuard.qtn`, `RiftShards.qtn`, the new
`RiftMutation/RunMutations.qtn`) need Quantum's DSL codegen to run before any of the new C# compiles.
Outstanding:

1. **Re-run the generator** (`Tools/RiftRaiders/Generate Rift Mutation Assets`) - it authors the 7 new
   assets, retunes the 9 rewritten ones, wires `Scope`/`IncompatibleWith`, and rebuilds
   `LevelUpConfig.RiftMutations`. Until it runs, the on-disk assets still describe the
   pre-2026-08-27 roster. Then **delete `ShieldBreaker.asset` and `AllOrNothing.asset` by hand** -
   the generator rebuilds the list from its own specs, so both drop out of the pool automatically,
   but the orphaned asset files remain on disk (and would now fail to resolve, since their classes
   are gone).
2. **A `RiftShardOrb` `EntityPrototype` and `RiftShardConfig.asset` still need Editor authoring**
   before Greed's currency half does anything at runtime - `Tools/RiftRaiders/Generate Rift Shard
   Assets` authors the config; the prototype and its `RuntimeConfig` wiring are manual, same
   documented gap `ScrapOrbPrototype` has today.
3. **Every asset's `Icon` is unset** - needs manual per-mutation sprite assignment.
4. **The distance thresholds behind Close Quarters/Longshot (5/10 units) are a placeholder**, not a
   tuned design number - same category as several proc magnitudes across the Weapon Perk roster. They
   are now `internal` on `DamageUtility` and shared by Longshot's pierce and Close Quarters' kill
   burst, so retuning them moves all three together.
5. **Mutual exclusion is authored per pair, and filters against already-OWNED mutations only** - see
   "Mutation incompatibility" above. Two incompatible cards can still share one screen.
6. **Every numeric value in the 2026-08-27 pass is a decisive placeholder pending a balance pass** -
   the stagger chance/duration, the Close Quarters speed burst, Pressure Cooker's 1.75x end point, and
   the Overpopulation/Elite Territory density trades in particular.
7. **`Weapon.FocusedBreachContactTime` only resets on an explicit miss or a target change**, not on a
   continuous per-tick decay while simply not firing - a reasonable MVP reading of "losing contact
   resets or rapidly decays progress" given Hitscan firing is already discrete, not a true
   continuous-beam decay.
