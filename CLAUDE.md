# Project notes

Unity + Photon Quantum (deterministic ECS), 2D co-op top-down roguelite shooter.

## Survival Director

A continuous-spawn combat pacing system (Survival Progression / Combat Director / Enemy Lifecycle) was added under `Assets/_QuantumUser/Simulation/{Assets,Systems,QTN}/Director/` plus a new `EnemyLifecycle` component and `EnemyDataAsset.Cost`/`Persistent` fields. `CombatDirectorUtility.ResolveBudgetMultiplier` now also scales `DirectorBudget` accumulation by live player count/run time via `BalanceConfig` - see "Run Curves & Co-op Scaling" below. Full design, file map, current status, and known simplifications: **`docs/survival-director.md`**. Read it before touching anything Director-related.

Short version: the code compiles and is registered in `SystemSetup.User.cs`, and as of 2026-08-07 `SurvivalConfig`/`EnemyGroupConfig`/`DirectorConfig`/`LifecycleConfig`/`EnemySpawnProfile` asset instances all exist, are authored, and are assigned to `RuntimeConfig` - the Director should actually spawn at runtime. `LifecycleConfig.RelevantRange` was found less than `DirectorConfig.SpawnRingRadiusMax` (the exact case the `ValidateOnce` guardrail warns about) and has been fixed - see `docs/survival-director.md`'s authoring checklist item 6. A first playable content pass was also authored the same day: all 11 `BaseEnemies`' action `Damage` values were rebalanced (one, `HeavySlammer`, was doing literal 0 damage), and `Tools > RiftRaiders > Generate Survival Director Content` (a new Editor generator script, not yet run) will author 10 `EnemyGroupConfig` encounters plus a full 6-phase `SurvivalConfig` timeline tuned for a ~15 minute run - see `docs/survival-director.md`'s "First playable content pass" section.

**2026-08-30: Elite spawns are now gated on chunk connectivity.** `Chunk.qtn` gained a persisted
adjacency graph (`ConnectedChunks`/`ConnectedChunkCount`), computed once by
`LevelGenerationSystem.ComputeChunkConnectivity` right after a level finishes generating, from data
placement already produces (reuses the existing `AreAdjacent` rectangle test). A new
`ChunkConnectivityUtility` gates `GroupSpawnerUtility.TrySpawnGroup`'s ring-anchor retry loop for
Elite+ ("major") groups only: a candidate anchor whose chunk isn't the nearest player's own chunk or
directly connected to it is rejected and retried, so an Elite can no longer spawn in a room that's
merely close in world-space (as `plan.GlobalCentroid` ring placement alone would allow) but requires
a detour to actually reach. See `docs/survival-director.md`'s "Chunk Connectivity" section. Compiles
once codegen runs; needs no Editor authoring at all. Not yet manually verified in-Editor.

**2026-08-31: a phase can now spawn a single enemy directly, with no `EnemyGroupConfig` asset
needed to wrap it.** `SurvivalPhase.AllowedEnemies` (`EnemySpawnEntry[]` - `EnemyData`/`Faction`/
`Weight`/`MinimumSurvivalTime`/`MaximumSurvivalTime`/`MaxConcurrent`, the same selection fields
`EnemyGroupConfig` itself has) is `AllowedGroups`' sibling: `CombatDirectorUtility.TrySelectSpawn`
(renamed from `TrySelectGroup`) rolls both lists into one shared weighted-draw candidate pool each
purchase, so a phase can freely mix whole encounters and lone spawns in the same pulse.
`GroupSpawnerUtility.TrySpawnEnemy` is the placement counterpart to `TrySpawnGroup` - same ring-
anchor/ground/chunk-connectivity-for-majors loop, minus the formation math, reusing the same
private `TryValidateMember`/`SpawnMember` helpers a group member uses (`SourceGroup` ends up
default/inert since there's no owning group). No `.qtn` change, no codegen dependency - plain C#
`AssetObject` fields only. See `docs/survival-director.md`'s "Direct enemy spawns" section. No
Editor generator authors `AllowedEnemies` yet (every content generator rebuilds `Phases[]`
wholesale) - it's Inspector-only for now.

## Run Curves & Co-op Scaling

A consolidated `BalanceConfig` asset (`Assets/_QuantumUser/Simulation/Balance/`) holds time-based "run curves" (one `RunCurveAnchor` row per anchor minute over a 12-minute run, `CurveChannel`) and player-count "co-op scaling" (flat P1-P4 lookups, `CoopGlobalKey` + a per-`EnemyTier` `CoopHpRow` table), with three consumers: `EnemyBalanceUtility.ResolveEnemyStats` combines the `EnemyHp`/`EnemyDmg` curves + `CoopHp`/`EnemyDamage` co-op rows with the pre-existing per-Tier HP baseline (`EnemyTierStatsConfig.MaxHealth`, **not** duplicated here) into a once-per-spawn `EnemyRuntimeStats` (HP + a generic damage multiplier) - baked into `Health.MaxHealth` and a new `EnemyCombatModifiers.DamageMultiplier` component from `EnemySystem.SeedFromEnemyData`, never re-evaluated after spawn, and actually applied to every hit an enemy lands via `HitEffectUtility.ScaleByEnemyDamageMultiplier` (the single funnel every enemy delivery type - melee/area/beam/projectile alike - ultimately calls). `CombatDirectorUtility.ResolveBudgetMultiplier` combines the `DirectorBudget` curve + co-op row into a multiplier applied to `phase.BudgetPerPulse` every Director pulse (Survival Director's own "Milestone 7", see `docs/survival-director.md`) - recomputed every pulse, not a one-time snapshot. `ExperienceUtility.ResolveXpRequirementMultiplier` applies the `XpRequirement` co-op row (no paired curve - `ExperienceConfig.RequiredExperience` already has its own per-level curve) to the level-up threshold in `ExperienceUtility.Grant`. `ExpectedPlayerDps`/`EliteFrequency` remain unconsumed. Full design, the exact curve/co-op numbers, and current status: **`docs/run-curves-coop-scaling.md`**. Read it before touching anything run-curve/co-op-scaling/enemy-HP-baseline/DirectorBudget-scaling/XP-requirement-scaling related.

Short version: the code compiles, and as of 2026-08-07 `BalanceConfig.asset` exists and is assigned to `RuntimeConfig.BalanceConfig` in both scenes, and `EnemyCombatModifiers` has been added to the shared generic enemy prototype (`GenericEnemyPrefab.prefab`) - all three consumers (`ResolveEnemyStats`, `ResolveBudgetMultiplier`, `ResolveXpRequirementMultiplier`) are live, and `HitEffectUtility.ScaleByEnemyDamageMultiplier` actually scales enemy damage now.

## Experience Drops

Enemies drop a physical `ExpOrb` pickup on death (skipped for environment/hazard kills), crediting one shared run-wide exp total + level (co-op - not tracked per-player) via a new `ExpOrb` component, `Experience.qtn` global fields, `ExperienceConfig`/`ExperienceUtility`/`ExpOrbSystem`, and a per-`EnemyTier` `ExpValue` baseline on `EnemyTierStatsConfig`. The per-level `RequiredExperience` threshold `ExperienceUtility.Grant` checks is additionally scaled by live player count via `BalanceConfig.CoopGlobalKey.XpRequirement` (`ExperienceUtility.ResolveXpRequirementMultiplier`) - see "Run Curves & Co-op Scaling" below. Full design, file map, current status, and known simplifications: **`docs/experience-drops.md`**. Read it before touching anything experience/leveling-related.

Short version: the code compiles and `ExpOrbSystem` is registered, but no `ExperienceConfig` asset instance or `ExpOrb` prototype prefab exist yet - those need to be authored in the Editor and assigned to `RuntimeConfig` before any exp actually drops or can be collected.

## Level-Up Upgrades

On a level-up, the simulation now pauses (a new `GameplaySystemGroup` wrapping the per-tick gameplay systems in `SystemSetup.User.cs`, toggled via Quantum's built-in `SystemDisable`/`SystemEnable`) and opens an upgrade-choice screen: every connected player rolls 3 options from `LevelUpPoolKind`'s pools (Weapon Perk / Global Upgrade / Rift Mutation are pooled globally via `LevelUpConfig`; Skill Upgrade / Passive Upgrade - together nicknamed "Hero Ascensions" - are per-hero, living directly on `CharacterData`) and picks one, via `SelectLevelUpUpgradeCommand`, before a 30s timer auto-picks randomly for anyone who hasn't. `WeaponPerkData`/`SkillActionData`/`GlobalUpgradeData`/`PassiveUpgradeData`/`RiftMutationData` all derive from a shared `UpgradeData` base (`Icon`/`DisplayName`/`GetDescription()`), so `LevelUpOption` carries one `AssetRef<UpgradeData>` instead of a field per kind, and the UI (`UpgradeCardWidget`) renders any of them with no switch statement. As of 2026-08-14, `Rarity` is no longer part of that shared base - only `WeaponPerkData`/`RiftMutationData` still have their own `Rarity` field and are weighted by it (`LevelUpConfig.GetWeight`); `SkillActionData`/`GlobalUpgradeData`/`PassiveUpgradeData` draw at a flat weight instead (`LevelUpUtility.ResolveWeight`) and show no rarity badge on their cards. Full design, file map, current status, and known simplifications: **`docs/level-up-upgrades.md`**. Read it before touching anything level-up/pause/upgrade-choice related.

Short version: `LevelUpConfig.asset` is authored and assigned to `RuntimeConfig`, `GlobalUpgrades`/`WeaponPerkPool`/per-hero `DashSkillUpgrades`/`PassiveUpgrades` are all populated, and `ChooseWindow` is wired into `QuantumGameScene` via `GameplayUiController.choiceWindows[]` - a level-up now actually pauses and shows a screen. `CharacterData.HeroSkillUpgrades` no longer exists - the Hero Skill slice of the Skill Upgrade pool is pulled straight from `HeroSkill`'s own `Actions` list instead (any `SkillActionData` authored there with `Activated == false` is a candidate; granting it via `AddUpgrade` ignores `Activated` for that player only - see `SkillSystem.InvokeActions`' `isUpgrade` bypass). Remaining gap: the full end-to-end flow hasn't been manually verified in-Editor yet. See `docs/level-up-upgrades.md` for details.

A given level-up can now be locked to exactly ONE of 5 player-facing categories (`LevelUpCategory`: HeroSkill merges SkillUpgrade+PassiveUpgrade, GlobalUpgrade, RiftMutation, WeaponPerk, and a new **Choose Weapon**) via `LevelUpConfig.LevelSequence`, a repeating per-level list - an empty list (the default) keeps the original mixed-all-pools roll unchanged. Choose Weapon rolls 3 distinct weapons from a new `WeaponChoicePoolData`, each with an independently-rolled perk count driven by a new persistent per-player `CharacterStats.WeaponTalentLevel` (increments on every Choose-Weapon pick) via `LevelUpConfig.ChancePerLevelPerSlot`/`MaxRolledPerks`, rendered by a dedicated `WeaponCardWidget` (not `UpgradeCardWidget`). As of 2026-08-07, a player can also decline every rolled weapon via a separate **"Keep Current"** button (`ChooseWindow.secondaryButton`, labeled "KEEP CURRENT" - shown only on a Choose-Weapon screen; the same button is later reused for Cursed Rift's own "CANCEL" - see the Breathing Phase section below) - deliberately NOT a 4th/replacement card, all 3 `Options` stay real rolled weapons. Sends a new zero-payload `KeepCurrentWeaponCommand`; `LevelUpUtility.ConfirmKeepCurrent` sets a new `LevelUpChoice.KeptCurrent` flag (not on `LevelUpOption`, since it isn't tied to any rolled slot) that `Resolve` checks before calling `GrantOption` at all - see `docs/level-up-upgrades.md`'s "Category sequencing / Choose Weapon" section. A new `Chest` entity/`ChestSystem` reuses this whole pipeline, forced to one category set once in the Editor per chest instead of per level. Also as of 2026-08-07: a **Reroll** mechanic lets a player redraw their own current `LevelUpChoice.Options` in place, spending one charge from a new persistent per-character `CharacterStats.RerollQuantity` via a new zero-payload `RerollLevelUpOptionsCommand` - see `docs/level-up-upgrades.md`'s "Reroll" section. **Not a Global Upgrade** - sourced as a pre-run meta-progression talent, same shape as `WeaponTalentLevel`: `RuntimePlayer.Talents.RerollQuantity` (its own `PlayerPrefInt`, `"reroll_quantity"`, in `MatchMakingConfig`) seeds it once at spawn - as of 2026-08-07 this and every other meta-progression field (including `WeaponLevel`) live on one nested `RuntimePlayer.Talents : PlayerTalents` struct rather than flat on `RuntimePlayer` itself (see `docs/talents.md`). Code compiles but needs Editor work before it's playable: assign `ChooseWindow`'s new `rerollButton`/`rerollChargesText` fields on the scene prefab (no reroll UI exists there yet), and nothing yet *writes* to the new PlayerPref (same pre-existing gap `WeaponTalentLevelPref` has) so every player starts at 0 charges until a settings/meta-progression screen sets it. Full design and current status: still **`docs/level-up-upgrades.md`** for the category/Choose-Weapon/Reroll half, and **`docs/chests.md`** for the Chest entity itself. Read both before touching anything category-sequencing/Choose-Weapon/Reroll/Chest related.

Short version: the code compiles, but `LevelUpConfig.LevelSequence`/`WeaponChoicePool` ship empty/unassigned (so nothing changes at runtime until authored), every `WeaponDataAsset`'s new `Icon`/`DisplayName` are unset, and no `Chest` `EntityPrototype`, `WeaponCardWidget`/`WeaponCardPerkRowWidget` prefab, or `ChooseWindow.weaponCardPrefab` wiring exist yet - see each doc's own "Current status"/"Editor authoring needed" section for the full list.

The `GlobalUpgrade` pool itself (22 upgrades, small permanent stat increments that stack
indefinitely) has its own design catalog: **`docs/global-upgrades.md`**. That doc's "Economy"
section also covers **Coin**, a second independent currency from Rift Shards
(`Coin.qtn`/`Coins.qtn`/`CoinConfig`/`CoinUtility`/`CoinOrbSystem`) - both currencies now share a
per-`EnemyTier` drop-chance gate (`EnemyTierStatsConfig.TierStats`'
`RiftShardDropChance`/`CoinDropChance`, rolled via `DamageUtility.RollChance` before a kill actually
drops one) and a scattered spawn position (`Min`/`MaxSpawnOffset`, same pattern `ScrapConfig`
already used) so multiple drops off one kill don't stack exactly on top of each other.

## Rift Mutations

Two level-up pools alongside Global Upgrade/Weapon Perk/Hero Ascension - **Rift Mutation**
(`LevelUpPoolKind.RiftMutation`/`LevelUpCategory.RiftMutation`, `LevelUpConfig.RiftMutations`, 27
entries as of 2026-08-27) and, as of 2026-08-14, a second independently-rollable **Rift Mark
Mutation** pool (`LevelUpPoolKind.RiftMarkMutation`/`LevelUpCategory.RiftMarkMutation`,
`LevelUpConfig.RiftMarkMutations`, 11 entries - previously folded into the same pool as Rift
Mutation, split out so a designer can pace/gate the two independently via
`LevelUpConfig.LevelSequence`). Both share one `RiftMutationData`/`RiftMutationUtility`/
`RiftMutationPicks` hierarchy (see `Assets/_QuantumUser/Simulation/Assets/RiftMutation/`) - a single
shared pick-history component, since both pools draw from the same catalog and their assets never
overlap - for **rare, non-stackable, run-wide** effects: a one-shot build-defining tradeoff (Glass
Core, Heavy Arsenal), a new reactive rule (Adrenaline Kick, Critical Focus), a run-wide encounter/economy
change (Overpopulation, Elite Territory, Greed), or a Rift-Mark-on-trigger effect (Critical Fracture,
Last Stand - Rift Mark Mutation). "Non-stackable" is enforced pool-wide (`RiftMutationPicks`) across
BOTH pools, unlike Global Upgrade's opt-in per-asset `MaxPicks`. Cursed Rift's own reward roll
(`LevelUpUtility.RollMutationOptions`, see the Breathing Phase section below) deliberately keeps
drawing only from Rift Mutation, never Rift Mark Mutation. `RiftMutationReactionSystem` handles the
mutations needing more than a one-shot `CharacterStats` bake, off `OnCriticalHit`/`OnEntityKilled`/
`OnAccessoryBlocked`. Greed introduced the **Rift Shard** currency system (`RiftShards.qtn`/
`RiftShardConfig`/`RiftShardUtility`, collected via the shared `CurrencyOrb`), mirroring `ExpOrb`'s
drop-and-collect pattern. Full design, the complete 38-mutation roster split across both pools, and
current status: **`docs/rift-mutations.md`**. Read it before touching anything
Rift-Mutation/Rift-Mark-Mutation-related.

**2026-08-27 rework.** The core pool was rebuilt around the **Accessory** now that it (not Shield) is
the defensive system - every Shield dependency is gone from Rift Mutations, though the Shield system
itself is untouched. Glass Core doubles Accessory durability instead of Shield; Last Bastion disables
the Accessory outright via a real `AccessoryGuard.Disabled` availability flag rather than pinning
durability at 0 (which is what makes the Store correctly stop offering a service); Shield Breaker's role was
folded into **Adrenaline Kick**, and **All or Nothing was cut entirely** - it was the only consumer of
`LevelUpUtility`'s single-choice/rarity-shift machinery, so that whole `rarityShift` parameter chain
came out with it. Fifteen mutations are new. Four are run-level (Overpopulation, Elite Territory, **Blood Tithe** and
**Escalation** - the latter two renamed from Blood Money/Pressure Cooker to free those names for the
personal, player-scoped mutations of the same flavour). Eleven are player-scoped: Spare Parts,
Adrenaline Kick, Money Talks, Danger Pay, Overkill, Scavenger Rush, Blood Money, No Safety Net,
Second Wind, Dead Weight, Pressure Cooker. **Money Talks** is
the one that needed a genuinely new shape: it bakes a RULE rather than a number, since its bonus
(+5% all damage per 100 Coins held, capped at +40%) is resolved live per hit from the wallet -
`CoinUtility.ResolveDamageBonus`, called by `DamageUtility.ResolveOutgoingDamage` - so it rises as
you save and falls the instant you spend at the Store.

The reusable pieces that pass added - **reach for these before writing anything mutation-specific**:

- **`MutationScope` (Player/Run) + `Frame.Global.RunMutationPicks`** (`QTN/RiftMutation/RunMutations.qtn`)
  - a Run-scope mutation writes shared state and is applied exactly ONCE per run no matter how many
  players are offered it. `RiftMutationUtility.Grant`/`IsBlocked` own the guard, so no call site has to
  think about co-op determinism.
- **`EncounterModifierUtility`** - the single reader of every run-wide encounter modifier (enemy Max
  Health, enemy damage, spawn density, Elite group weighting, Rift Shard gain, Pressure Cooker's
  phase ramp). All are bonuses defaulting to 0 and read as `1 + bonus`, so an untouched run is an exact
  no-op. Replaced `RiftShards.qtn`'s lone `EnemyHealthBonusMultiplier`. Density scales all THREE
  Director levers together (budget accrual, `MaxAliveEnemies`, `TargetPressure`) exactly as the
  pre-existing `SplitThreatMultiplier` does - scaling one alone just moves the bottleneck.
- **`IsEligible(Frame, EntityRef)` on `GlobalUpgradeData` and `RiftMutationData`** - the prerequisite
  hook `PassiveUpgradeData`/`SkillActionData` already had, extended to the two pools that lacked it.
  Default true. For mutations it is checked inside `RiftMutationUtility.IsBlocked`, so one override
  covers level-ups, Chests, Cursed Rift and debug grants at once. The codebase idiom is a capability
  QUERY (`f.Has<T>` / a state helper), never a string tag - Accessory-dependent mutations gate on
  `AccessoryGuardUtility.IsAvailable`, and Dash Charge suppresses itself on "is my ceiling capped?"
  rather than naming Dead Weight.
- **`SkillSystem.ResolveEffectiveMaxStacks`** - `min(MaxStacks, DashChargeHardCap)`, read at every
  availability point. Dead Weight's cap is expressed HERE rather than by subtracting from MaxStacks,
  which is what lets an already-owned +1 Charge stay owned but suppressed, keeps the cap authoritative
  under any later stacking, and leaves Dash RESTORE/BYPASS mechanics working untouched.
- **`MutationModifierUtility`** - the single composition point for every LIVE conditional player
  modifier (Money Talks' wallet, Danger Pay's health threshold, No Safety Net's Accessory state,
  Pressure Cooker's safe-time streak). One term in `ResolveOutgoingDamage` instead of one line per
  mutation, so the damage pipeline stays a fixed length as the roster grows.
- **`MutationTimerUtility`** - per-player deterministic timers, ticked off `f.DeltaTime` from
  `StatusEffectSystem`'s existing per-entity iteration rather than a new system (that filter already
  covers exactly the right entities, and already hosts `LastStandCooldownRemaining`).
- **`OverkillUtility`** - excess-damage blast. `DamageUtility`'s unclamped post-hit health already IS
  the excess, so this needed no damage-pipeline restructuring; recursion is bounded by reusing the
  existing `isChainedExplosion` flag rather than a new depth counter.
- **`signal OnCollectibleCollected(collector, CurrencyOrbType)`** (`CurrencyOrb.qtn`) and **`signal
  OnAccessoryRecovered(owner, recoverer)`** (`AccessoryGuard.qtn`) - two generic hooks. The first
  fires only from the currency-orb path, so "valid collectible" excludes Accessory recoveries/shop
  purchases structurally; the second fires only on a real world recovery, never on a Merchant
  Restore, and always reports the OWNER.
- **`RiftMutationData.IncompatibleWith`** - data-driven mutual exclusion, checked SYMMETRICALLY
  (`IsBlocked`), so a pair only needs authoring on one side and can't half-work if someone forgets to
  mirror it. **No pair is currently authored** - Glass Core / Infinite Momentum needed the rule only
  while Glass Core set Max Health to an absolute 1; once it became a plain x0.5 multiplier the 5%
  health cost was a real price again. So the path exists but is unexercised.
- **`signal OnAccessoryBlocked(owner, attacker, broken)`** + **`AccessoryEmergencyReserve`** +
  `AccessoryGuardUtility.Disable`/`ScaleMaxDurability` - the generic Accessory hooks. The signal fires
  only on a genuine block (never on recovery/purchase/non-block destruction); the reserve is a
  "would-be break instead consumes a charge" primitive whose `Charges` nothing ever refills, which is
  what makes Spare Parts' once-per-run structural rather than policed.
- **`WeaponSystem.ApplyOwnerWeaponModifiers`** - a new stage of every `Equip` (after perks and hero
  modifiers) applying `CharacterStats.MagazineSizeBonus`/`MagazineSizeOverride`. `Weapon.MagazineSize`
  is a BAKED absolute that `SeedStats` resets, which is why the old One in the Chamber silently died on
  the next weapon pickup. Same dual-call precedent (`Equip` + direct from the granting asset) that
  `ApplyPixieExplosiveWeapon` already set. Damage/fire rate/reload/pierce need nothing here - already
  live-resolved `CharacterStats` multipliers.
- **`SkillFocusUtility` + `HitEffectContext.AreaCenter`/`AreaRadius`** - a generic normalized
  distance-to-center damage multiplier for skill areas (Focused Power). `AreaRadius == 0` is the
  explicit "no meaningful spatial area" reading, so a direct hit or single-target cast is an exact
  no-op and no hero is ever named.
- **`SkillSystem.ResetCooldown`** (idempotent - two sources firing on the same block still leave one
  ready Dash, never banked charges) and **emergency activation**
  (`TryPayEmergencyActivation`, Infinite Momentum's paid Dash - a direct health write, never
  `DamageUtility.ApplyDamage`, so it can't roll crit, count as hostile damage, interrupt a revive
  channel, or cost a durability point). Unlimited, refused ONLY at the 1-health floor - above it the
  player pays what they can and lands at 1 if short. Both obvious alternatives are wrong: clamping
  the result at 1 with no gate makes every Dash at 1 health free, while demanding the full price
  leave 1 behind blocks the Dash exactly when a low player needs to escape.
- **`CharacterStats.WeaponStaggerChance`/`Duration`** - one generic roll in
  `DamageUtility.TryApplyWeaponStagger` beside `OnWeaponHitLanded`, routed through
  `StatusEffectUtility.ApplyStun` so per-tier hard-CC immunity applies with no tier check of its own.
- **`DamageUtility.RangeDamageNearThreshold`/`FarThreshold`** (5 / 10, was 5 / 12) widened to
  `internal`, so Longshot's bonus pierce and Close Quarters' kill burst key off the same numbers the
  damage falloff does rather than second copies.
- **`RiftMutationDebugUtility`** - log-only (no UI): active mutations per player, run-wide modifiers
  and what they resolve to, Accessory durability, Spare Parts charges, the Critical Focus counter,
  emergency-dash availability, and final weapon stats. `LogFiltered` fires from `IsBlocked` naming the
  mutation and reason, which is otherwise completely invisible - a filtered mutation just stops
  appearing.

Short version: the simulation code is written and `RiftMutationReactionSystem` is registered in
`SystemSetup.User.cs`, but **the 2026-08-27 pass has not been compiled or verified in-Editor** - the
changed `.qtn` files (`CharacterStats.qtn`, `LevelUp.qtn`, `Accessory/AccessoryGuard.qtn`,
`RiftShards.qtn`, new `RiftMutation/RunMutations.qtn`) need Quantum's DSL codegen to run first. Then:
run `Tools/RiftRaiders/Generate Rift Mutation Assets` (authors the 7 new assets, retunes the 9
rewritten ones, wires `Scope`/`IncompatibleWith`, rebuilds both `LevelUpConfig` lists) and
hand-delete the orphaned `ShieldBreaker.asset`/`AllOrNothing.asset`/`ImpactDrive.asset`. That re-run also repairs a real
pre-existing authoring bug: `RiftMarkMutations` currently holds **12** entries with `HeavyFracture`
duplicated where `LongFracture` should be, so that mutation has had double weight and could be drawn
twice in one roll. Every numeric value in this pass is a decisive placeholder pending a balance pass.
`HeroInfoPopupWidget.riftMarkContent` (the tab-hold party summary popup's 4th list) still needs a
scene Transform assigned - no null guard, so it must be wired before a Rift Mark Mutation pick is
ever recorded. Same gap `ExpOrb` itself once had: no `RiftShardOrb` prototype prefab exists yet and
`RuntimeConfig.RiftShardConfig`/`RiftShardPrototype` aren't assigned, so Greed/Blood Money's currency
half won't drop or credit anything at runtime until that's authored in the Editor.

## Weapon Perks

`WeaponPerkData` is the roguelite modifier a weapon drop (`WeaponGenerator`) or a level-up pick (see
"Level-Up Upgrades" above) can grant - it bakes its effect once into `Weapon`'s own fields at equip
time (`WeaponSystem.Equip`/`AddPerk`), never re-applied per tick. The full ~35-perk roster is now
implemented as code (a shared ramp pool for the 3 "keep firing" perks, two new combat signals for
on-kill/on-crit reactions, and `DirectHitData` hooks for post-impact perks like Ricochet/Split
Shot/Quantum Rounds). Full design, the complete roster-to-class mapping, and current status:
**`docs/weapon-perks.md`**. Read it before adding or authoring anything perk-related.

Short version: every perk has a `WeaponPerkData` class and the code compiles;
`Assets/_QuantumUser/Editor/WeaponPerkAssetGenerator.cs` (`Tools > RiftRaiders > Generate Weapon Perk
Assets`, same menu group as `GlobalUpgradeAssetGenerator`) authors a tuned `.asset` per perk and wires them into the existing
`WeaponPerkPoolData.asset` stub, so a fresh weapon drop can already offer perks once that's run. A
level-up still can't, though - `LevelUpConfig.asset` doesn't exist yet (see "Level-Up Upgrades"
above), so its own `WeaponPerkPool` reference has nothing to point at.

## Enemy Burrow / Invulnerable Relocation

A reusable `EnemyDeliveryData` (`BurrowDeliveryData`) lets an enemy dive underground - invulnerable and untargetable via a new `Burrowed` tag alongside the existing (previously-unused) `Invulnerable` tag - travel invisibly to a point near its target, then resurface and resume the normal telegraphed attack cycle. `AimSystem`/`VortexSystem`/`EnemyMovementUtility.TryFindNearestEnemy` were all patched to skip `Invulnerable` targets, and `EnemyBlobAnimationView` gained a reversible shrink/sink `Burrow` view state. Full design, file map, current status, and known simplifications: **`docs/enemy-burrow.md`**. Read it before touching anything burrow/invulnerability/targeting-exclusion related.

Short version: the code compiles, but no `EnemyActionData`/`BurrowDeliveryData` asset instances exist yet and no enemy's `SkillActions` references one - those need to be authored in the Editor before any enemy actually burrows.

## Mortar Elite / Random-Scatter Barrage

A new `EnemyDeliveryData` (`MortarBarrageDeliveryData`) fires several real, arc-lobbed projectiles at once via `movement.GetLaunchToTarget` on the shell's own assigned `ProjectileMovementData` (typically a `BallisticProjectileMovementData`) - `AimedShellCount` of them land exactly on the target's locked position (forcing them to move), the rest scatter randomly around that same point via the pre-existing `EnemyDeliveryData.RandomizeAroundAnchor` (same ring `ScatterDeliveryData` already uses). Always resolves instantly (`Begin()` returns `true`), so it never touches `Enemy.SkillProjectile`'s single-slot `WaitForImpact` tracking - `FanProjectileDeliveryData`'s own comment is why that field can't track more than one in-flight shot at once. Each shell's own `AreaHitData` gives blast damage on landing for free (no new damage code). The per-shell ground telegraph needed **no new simulation-side entity/component at all** and turned out fully generic, not Mortar-specific: two new shared static helpers on `EnemyDeliveryData` itself, `FireLandingWarning` (derives real flight time from a solved launch's own velocity, fires the event) and `ResolveWarningRadius` (reads the warning's radius straight off the projectile's own `AreaHitData.BlastRadius` instead of a second authored field), plus a new generic event, `ProjectileLandingWarning` (Position/Duration/Radius, no owner - named after the mechanism, not the enemy, so a future non-projectile "several telegraphed ground impacts" attack, e.g. a boss dropping a spike volley, can fire it directly with an authored fuse instead of a solved flight time). `ProjectileDeliveryData` gained an opt-in `ShowLandingWarning` bool calling the same two helpers, so the pre-existing single-shot `MortarEnemy` can opt into the identical telegraph instead of its old caster-anchored windup Circle. View-side, a small new `GroundWarningTelegraphManager` (`Assets/_QuantumUser/View/Managers/`) pulls straight from the pre-existing `TelegraphManager` pool - that pool already supports any number of simultaneous independent instances on its own, since the "one telegraph at a time" limit was only ever `EnemyAttackVisualsView`'s own single-slot bookkeeping, not the pool itself - reusing the exact same `TelegraphFade`/`TelegraphGrow` prefab shape the enemy's own windup telegraph already uses, plus the same `Physics.Raycast`-against-`Ground` snap `EnemyAttackVisualsView.SnapToGround` uses to keep the decal from reading a few pixels off under this game's tilted camera. Three real bugs were found and fixed via live in-Editor testing: an early version called `ProjectileSpawner.SolveArcLaunch` directly, which never sets `ProjectileLaunch.SpawnPosition` (spawning every shell at world origin, instantly detonating against whatever geometry was there); duplicated `LaunchAngle`/`Gravity` onto the delivery itself rather than reading them off the assigned movement asset (so the solved launch and the actual in-flight curve could disagree on gravity); and the ground-warning decal itself needed the same Unity ground-snap `EnemyAttackVisualsView` already uses. The `SpawnPosition` bug was also found and fixed in `ProjectileDeliveryData`'s and `FanProjectileDeliveryData`'s own `UseArc` branches, unrelated pre-existing code neither had ever exercised in production. Full design, file map, current status, and known simplifications: **`docs/mortar-elite.md`**. Read it before touching anything Mortar/ground-warning-telegraph related.

Short version: the code compiles once codegen picks up the new `ProjectileLandingWarning` event in `Events.qtn` - but no `MortarShell` `ProjectileDataAsset`, ground-warning `TelegraphPrefab`, `MortarBarrageDeliveryData`/`EnemyActionData` asset instances, or `MortarElite` `EnemyDataAsset` exist yet, and no `GroundWarningTelegraphManager` instance/prefab is placed in the gameplay scene - so nothing fires until those are authored in the Editor and wired into a phase/group. Turning the ground telegraph on for the pre-existing single-shot `MortarEnemy` is a one-tick `ShowLandingWarning` flag on its own `ProjectileDeliveryData` asset, no code.

## Explode-On-Destroy / Mini Bomb

**Pixie's Cluster Charge (`ClusterBombUpgrade`/`ClusterBombSkillAction`) is original/untouched** - it spawns real `Projectile` bomblets via `ProjectileSpawner`, exactly as it always did. It was briefly redirected onto a new stationary "Mini Bomb" entity shape, then reverted at the user's explicit request since the `Projectile`-based version was already working and tuned. **Do not redirect Cluster Charge onto Mini Bomb again without being asked.**

What stayed: a generic **`ExplodeOnDestroy`** component (`Damage`/`SpawnDepth`/`AssetRef<AreaHitData> Explosion`) - a hero/feature-agnostic "detonate when this entity is destroyed" hook, checked from two independent trigger points (`DestroyAfterTimeSystem` on timed expiry, and `DamageUtility.ApplyDamage`'s non-Enemy death branch when `Health` reaches 0), both a plain optional check bolted onto neither system specifically, so any future "explodes when destroyed" feature reuses it for free. The damage-death trigger is what lets it also work as a **decoy trap**: seed `Health` to 1, add the existing `Decoy` tag (draws enemy aggro, zero new code), and a real collider *on the Player physics layer* (see `Decoy.qtn`'s own comment), and it detonates the instant an enemy kills it. `Explosion` (`AreaHitData`) is reused purely as data, so it fires the same generic `HitEffectUtility.ApplyInRadius`/`AreaDetonated` explosion every other blast in this codebase uses. `DashBomb.prefab` is a working reference prototype kept in place even though no live Ascension currently spawns it (see "Pixie — Ascensions" below); Pixie's **Pocket Bombs** Ascension is the other live user, dropping a Mini Bomb this same way. Full design, file map, current status, and known simplifications: **`docs/explode-on-destroy.md`**. Read it before touching anything Cluster Charge/Mini Bomb/`ExplodeOnDestroy` related.

Short version: `DashBomb.prefab` already has an `ExplodeOnDestroy` configured by hand in the Editor from before a mid-stream rename (`ExplodeOnExpire` → `ExplodeOnDestroy`) - its generated component reference needs re-adding once codegen regenerates (see docs/explode-on-destroy.md for the exact values to re-enter: `Damage = 10`, same `Explosion` asset).

## Pixie — Ascensions

Pixie's Hero Ascension pool was consolidated (2026-08-09) from ~13 overlapping single-pick passives/skill upgrades down to **exactly 9 three-rank Ascension lines** (27 total rank-acquisitions): Cluster Bomb, Direct Hit (absorbed Concussive Force), Birthday Cake (now taunts after *landing*, not during flight), Pocket Bombs (renamed from Mini Ordnance), Unstable Mixture (absorbed Bigger Boom + Heavy Payload), Unstable Targeting, Explosive Rounds, Backblast (absorbed Volatile Escape, and later reworked from an instant blast into dropping a fused bomb - same `ExplodeOnDestroy` shape Pocket Bombs uses, so it's a full qualifying Pixie explosion), and a new second Dash path, **Hot Fuse** (dash empowers the *next* Bunny Bomb throw rather than exploding herself). Volatile Payload, the always-on baseline Bunny Bomb behaviors (Bomb Radius Up/Instant Detonate/Fireworks), the `isDashExplosion` parameter chain it left behind (once Backblast stopped needing it), and a real authoring bug - `PixieBaseSkill.asset` had a dangling GUID resolving to **Max's** `MarkExplosiveDeathSkillAction`, silently marking every enemy Pixie hit with anything, ungated - were all removed. Volatile Escape's guaranteed-marking role now lives on a new generic, hero-agnostic tag, **`ForceMarkOnDetonate`**, granted onto a specific spawned bomb entity (not its owner) and read by `ExplodeOnDestroyUtility.TryDetonate` - reusable by any future hero's "this dropped bomb always marks what it hits" ascension. This is also the first hero to exercise a new **generic multi-rank Ascension mechanism** (`MaxRank`/`IRankedUpgrade` on both `PassiveUpgradeData` and `SkillActionData`, rank tracked via the pre-existing `UpgradeHistory.Count` field - see `docs/level-up-upgrades.md`'s own "Ranked Ascensions" section), built generic and intended for every other hero's Ascension pool (Kai/Brute/Max) to reuse rather than rediscover. Full design, the per-line breakdown, and current status: **`docs/pixie-ascensions.md`** (replaces the old `docs/pixie-demolition-mastery.md`). Read it - and `docs/level-up-upgrades.md`'s ranking section - before touching anything Pixie-Ascension/explosion-reaction/rank-mechanism related.

Short version: the code compiles once codegen picks up every changed/new/removed `.qtn` file (several components were renamed/added/removed as part of the consolidation - see docs/pixie-ascensions.md's own "Current status"); `Tools/RiftRaiders/Pixie/Generate Ascension Assets` (replaces the old two-generator Chain-Reaction/Demolition-Mastery pair) authors and wires all 9 lines, **fully replacing** every list it touches (`PassiveUpgrades`, `PixieBaseSkill.Actions`, `DashSkillUpgrades`) rather than appending - deliberately, since an append/replace split between two generators is exactly what let the old pool drift out of sync with what was actually live. Pocket Bombs' `MiniBombPrototype`/`Explosion` still need hand-authoring in the Editor. Not yet manually verified end-to-end in-Editor.

## Brute — Ascensions

Brute's Hero Ascension pool - previously fragmented across a 4-trait Protector Aura pool, a 4-trait "Knockback Mastery" pool, and 8 baseline Juggernaut sub-actions that turned out to be **permanently dead code** (`BruteBaseSkill-Juggernaut.asset` had `CheckActions: 0`, so none of them ever executed regardless of their own `Activated` flag - Discharge was knockback-only, no landing damage/stun, no end-explosion, no stacking, before this refactor) - was consolidated into exactly 8 three-rank Ascension lines (4 Juggernaut/2 Protector/2 Dash), reusing the same generic rank architecture Pixie's own refactor built (`IRankedUpgrade`/`MaxRank`/`UpgradeHistoryUtility` - see "Level-Up Upgrades" above), zero Brute-specific rank code. The 4 Juggernaut lines (Momentum/Bone Breaker/Aftershock/Concussive Impact) are ranked `SkillActionData` living on `JuggernautSkillData.Actions` (`Activated = false`, same "Hero Skill Ascension" shape Pixie's `ClusterBombSkillAction`/`BirthdayCakeSkillAction` already use) - originally built as `PassiveUpgradeData` instead, but that made them show up labeled as a generic "Passive Upgrade" in the level-up UI/debug menu, indistinguishable from genuinely hero-wide passives like Iron Presence/Guardian; converting them fixed the label to "Hero Skill" everywhere with zero UI changes. `JuggernautSkillData`'s own hardcoded `Tick`/`Discharge`/`End` logic reads the components they set via plain optional `TryGetPointer` checks either way, agnostic of grant mechanism - sidestepping the dead `Actions`/`CheckActions` mechanism entirely rather than fixing it (a *picked* Ascension executes via `SkillSlot.Upgrades`, which bypasses `CheckActions` regardless). A new baseline `JuggernautSkillData.Damage` ("Juggernaut Skill Damage") is the shared percentage basis every line references. The 2 Protector lines (Iron Presence, absorbing the old standalone Fearless; Guardian, absorbing the old standalone Bulwark plus a new rank-3 reactive-DR proc reacting to `Combat.qtn`'s `OnHealthDamageApplied`/`OnShieldDamageApplied` via a new `BruteProtectorReactionSystem`) mutate the existing `ProtectorAura` component, which gained `BaseRadius` (an immutable spawn-time anchor so Guardian's ranked radius bonus can always compute a correct total) and `HasReactiveDamageReduction`. A new generic `StatusEffects.TemporaryDamageReductionRemaining/Amount` pair (deliberately not Guardian-named) is shared by both Guardian rank 3's reactive proc and Bodyguard rank 3's own dash-end proc, since both are occasional bonuses layered on top of Guardian's own continuous aura DR rather than a replacement for it. The 2 Dash lines (Iron Shoulder, Bodyguard) were already single-pick `SkillActionData` and just needed ranking - Iron Shoulder's rank 1 reproduces its exact pre-refactor knockback-only behavior with zero regression. Ground Pound was deleted entirely ("too disconnected from Brute's primary loop"); Crushing Blow's mechanism survives as the renamed `StunDamageBonusUpgrade`, now granted by Concussive Impact rank 3; Lasting Impact/Overwhelming Force fold into Concussive Impact's own landing-stun ranks/knockback bonus. Full design, the exact per-rank numbers, the `CheckActions` bug writeup, and current status: **`docs/brute-ascensions.md`**. Read it before touching anything Brute Ascension/Juggernaut/Protector Aura/Iron Shoulder/Bodyguard related.

**Juggernaut and Bodyguard were rebuilt on the charge-only Shield (2026-08-25)** - see "Recoverable Accessory Guard" below for the Shield model itself. Juggernaut's Discharge Shield gain is now a plain `ApplyFlatShield` capped at Max (Overshield is gone), which makes it Brutus's ONLY self-sufficient Shield source and therefore what keeps his own Accessory on his head. **Bodyguard no longer restores Shield at all**: on dash complete it grants a **Free Hit Guard** (the generic one-shot negation primitive) to **Brute AND every ally** within 6m/8m/8m - 2.5s at R1, 3.5s at R2+ - and pays Brute back **10/15 Shield when one of those guards actually blocks a hit**, with R3 additionally releasing a 3m knockback shockwave around whoever it saved (`BruteAscensionUtility.ApplyRadialKnockback`, a new knockback-only sibling of `ApplyRadialStunDamage`). Delivery shape is unchanged from the pre-rework line - still `Phase = End`, one radius query at the dash's end point (a `Begin | OnGoing | End` sweep was built and deliberately reverted). Brute is a full-value recipient, which closes a real loop at R2-3 (guard yourself, eat a hit, get Shield back) - and that makes `EnemyMovementUtility.FindPlayersInRadiusIncludingDashing` **load-bearing rather than defensive**: firing at dash End means the broadphase was built with Brute still on `IgnoreProjectile`, so the narrow Player mask drops him 100% of the time (the exact 2026-08-20 "Bodyguard never shielded Brute himself" bug). New `BodyguardUpgrade` component carries the rank-resolved values for the new `BruteBodyguardReactionSystem` (registered beside `BruteProtectorReactionSystem`), which reacts to `OnFreeHitGuardConsumed` - the reward deliberately lives with the ability, never in the primitive. `StatusEffects.AllyShieldRestoreCooldownRemaining` was renamed `AllyGuardGrantCooldownRemaining` and still paces per-RECIPIENT (Brute included). `BodyguardSkillAction`'s old `ShieldRestore`/`SelfEffectMultiplier`/`DamageReductionAmount`/`DamageReductionDuration` are gone.

Short version: the code compiles once codegen picks up the changed `.qtn` components (`JuggernautAscensions.qtn`, `ProtectorAura.qtn`, `JuggernautLaunched.qtn`, `StatusEffects.qtn`) and is registered in `SystemSetup.User.cs`. `BruteAscensionAssetGenerator.cs` (`Tools > RiftRaiders > Brute > Generate Ascension Assets`) replaces the two old generators and is pointed at each surviving asset's verified live path (the old `BruteProtectorAssetGenerator`'s own path constants had drifted out of sync with reality - see the doc's own "Asset path drift" section). It was run once under the earlier `PassiveUpgradeData` design for the 4 Juggernaut lines; after converting them to `SkillActionData` the 4 stale assets were deleted by hand and `BruteCharacterData.PassiveUpgrades` trimmed back to Iron Presence/Guardian, but the generator still needs re-running to author the 4 new Hero-Skill-Ascension assets and wire them into `BruteBaseSkill-Juggernaut.Actions`. `JuggernautSkillData.Damage` (30) is a placeholder pending a real balance pass. Not yet manually verified end-to-end in-Editor.

**Juggernaut Shield became genuinely temporary, not just charge-only (2026-08-30).** `Shield.qtn` gained `TemporaryDuration`/`ExpirationRemaining` (both 0 by default - opt-in, Brute is the only hero authoring `CharacterData.ShieldTemporaryDuration` above 0, at 6s) - `Current` now snaps straight to 0 the instant `ExpirationRemaining` counts down, no gradual decay, no HP conversion, no carry-over between encounters. One pool, one timer: `ShieldUtility.ApplyFlatShield` (the single funnel every grant already used - Discharge, Bodyguard's reward, the Store's Shield food offer) resets the same shared timer on every successful gain rather than opening a new one per grant, while damage/weapon-hits/movement never call that path so none of them can refresh it. `BaseMaxShield` raised 20 → 60 (the new Temporary Shield cap - still a single `Shield.Max`, no separate Overshield concept, which was already removed in the 2026-08-25 pass below). Both HUD Shield bars (`CharacterUiWidget`, `ShieldUiWidget`) pulse toward a warning color once the countdown drops below a threshold. See `docs/brute-ascensions.md`'s own "2026-08-30" section for the full writeup and the deliberate call on which grants refresh the timer.

## Max — Ascensions (Overdrive / Vendetta / Fire Mastery)

Max's kit was fully consolidated (2026-08-10) into exactly 10 three-rank Ascension lines (Overdrive
×4: Last Stand/Full Throttle/Uncontrolled Fury/Ignition; Passive ×4: Blood Debt/Burning Vengeance/
Wildfire/Flashpoint; Dash ×2: Run & Gun/Vendetta Strike), replacing a mix of always-on baseline
actions, single-pick upgrades, and a fully dead parallel Rage system (`Adrenaline`, deleted entirely).
Normal Max now has a permanent +20% Fire Rate baseline (`MaxCharacterData.AttackSpeedMultiplier`);
Overdrive's own Fire Rate bonus was re-derived (0.25, not 0.50) so the two compose to the intended
+50% total rather than stacking to +70% - zero "replace not stack" logic needed, just correct algebra
on the existing multiplicative Begin/End composition. Rage itself is no longer a stat-correction
mechanism - reaching max Rage (`RageOverdriveUtility.IsAtMaxRage`) is a pure boolean condition Full
Throttle/Ignition react to on their own, not a baked-in threshold-flip. Two entirely unrelated
mechanics used to both be named "Too Angry to Die" - only the live, `CheatDeathGuard`-based one
survives, folded into Last Stand rank 3. Full design, the per-line breakdown, and current status:
**`docs/max-ascensions.md`** (replaces `docs/max-vendetta-fire-mastery.md`/`docs/max-berserk-rage.md`,
both deleted). Read it before touching anything Max-Ascension/Overdrive/Rage/Vendetta/Fire-Mastery
related.

Short version: the code compiles once codegen picks up every changed/new `.qtn`, and
`SystemSetup.User.cs` now registers `MaxOverdriveReactionSystem` before `MaxVendettaSystem` (a real
ordering requirement - Uncontrolled Fury rank 3's Vendetta-kill bonus has to read a kill's
`RevengeMark` before `MaxVendettaSystem` consumes it). `Tools > RiftRaiders > Max > Generate Ascension
Assets` (replaces the old four-generator Adrenaline/Overdrive/Vendetta/Fire-Mastery split) authors and
wires all 10 lines, fully replacing every list it touches - not yet run. `PartyHudWidget`'s prefab may
still reference the now-deleted `AdrenalineUiWidget` on a child GameObject - needs manual removal in
the Editor. Not yet manually verified end-to-end in-Editor - see the doc's own "Current status" for
the full checklist.

## Kai — Ascensions

Kai's Hero Ascension pool was consolidated (2026-08-10) from a fragmented, mostly-dead roster - his
Vortex Hero Skill's own asset had `CheckActions: 0`, the same dead-code bug already found and fixed
for Brute's Juggernaut, so 6 of its 7 embedded sub-actions never ran and a live Kai's Vortex dealt zero
direct damage - into exactly 10 three-rank Ascension lines: 4 on the Vortex Hero Skill (Singularity/
Compression/Vortex Collapse/Void Shards), 3 Passive (Event Horizon/Undertow/First Strike), 3 Dash
(Mirror Step/Phantom Strike/Warp Wake). Reuses the exact same generic rank architecture Pixie/Brute/
Max's own refactors already established (`IRankedUpgrade`/`MaxRank`/`UpgradeHistoryUtility.GetCount`) -
no Kai-specific rank code anywhere. `KaiVortexSkill.Damage` (12) is now "Vortex Skill Damage," the
percentage basis every Compression/Vortex Collapse/Void Shards value scales off
(`KaiAscensionUtility.ResolveVortexSkillDamage`), while pull `Force` is fully decoupled into its own
baseline (`SpawnVortexEffectData.PullForce`) that Singularity multiplies rather than overrides; Cast
Damage is now genuinely dealt on impact (`DamageUtility.ApplyDamage` called directly from
`SpawnVortexEffectData.Apply`), which it never was before. Singularity's interrupt (both Preparation/
Telegraph AND Active - a charging Charger or airborne Leaper alike) needed one new generic
(hero-agnostic) utility, `EnemyActionUtility.TryInterrupt` - deliberately independent of
`EnemyTierStatsConfig.CanBeInterruptedByKnockback` (false for Heavy/Elite/Boss in the live config, a
physical-push-resistance flag unrelated to a pure state-machine cancel). Undertow rank 3
introduced a second new generic status, `StatusEffectUtility.ApplyBound`/`IsBound`. Phantom Strike moved
from a `PassiveUpgradeData` reacting to `OnSkillActivated(DashSkill)` into a genuine Dash-slot
`SkillActionData` (was mislabeled as a generic "Passive Upgrade" in the level-up UI before). Full
design, the complete per-line rank breakdown, and current status: **`docs/kai-ascensions.md`** (replaces
`docs/kai-voidwalker-mastery.md`, deleted). Read it before touching anything Kai-Ascension/Vortex/Void-
Field/Undertow/First-Strike/Mirror-Step/Phantom-Strike/Warp-Wake related.

Short version: the code compiles once codegen picks up every changed/new `.qtn` file, and
`SystemSetup.User.cs` registers `KaiUndertowSystem` (renamed from `KaiVoidwalkerMasterySystem`, now
Undertow-only) and a new `FirstStrikeMarkTimeoutSystem` at the same relative positions their
predecessors held. `Tools/RiftRaiders/Kai/Generate Ascension Assets` (replaces the old two-generator
Void-Field/Voidwalker-Mastery split) authors and wires all 10 lines plus the base Void Field passive,
fully replacing every list it touches (including an orphan-sweep of `KaiVortexSkill.asset`'s 6 dead
pre-refactor embedded sub-actions) - not yet run. Every numeric value not explicitly pinned by the
original design brief is a decisive placeholder pending a real balance pass. Warp Wake's Dash Void
currently reuses Kai's own Hero Skill vortex prefab rather than a dedicated one (cosmetic follow-up,
not a functional gap). Not yet manually verified end-to-end in-Editor - see the doc's own "Current
status" for the full checklist.

## Zara — Ascensions

Zara's Hero Ascension pool was consolidated (2026-08-11) from 8 one-off Totem sub-actions (her
`ZaraBaseSkill.asset` had the same `CheckActions: 0` dead-baseline shape already found/fixed for
Brute/Kai), 4 single-pick Resonance passives, and 4 overlapping Dash picks into exactly 10 three-rank
Ascension lines: Totem (Amplifier/Healing Chorus/Double Time/Main Stage), Resonance (Faster Tempo/
Heavy Bass/Restorative Beat/Remix), Dash (Afterbeat/Portable Speaker) - reusing the same generic
rank/draft/UI architecture Pixie/Brute/Max/Kai already established (`docs/level-up-upgrades.md`'s
"Ranked Ascensions" section), zero Zara-specific rank code needed. Her Totem's pre-existing generic
`AlternatingArea`/`AlternatingAreaSystem` (Damage-Beat/Healing-Beat alternation, shared with a new
generic `HealAmount` field mirroring the pre-existing `DamageAmount`) turned out to already implement
exactly the "Combat DJ" rhythm mechanic the redesign needed, extended rather than reinvented - Main
Stage rank 3's opening/closing bonus beats are the one genuinely new mechanism
(`AlternatingAreaSystem.FireBonusPulse`/`TryFireClosingBeat`, gated by a `MainStageBonusBeats` marker
tag stamped only on entities Main Stage itself spawns, guaranteeing a Portable Speaker - built through
the exact same `SpawnedEntitySpawner.Spawn` call - can never inherit them). Two real pre-existing bugs
were found and fixed as part of this refactor: `AlternatingAreaSystem`'s alternation defaulted to a
Healing-first pulse order rather than the spec-required Damage-first (fixed by seeding
`CurrentlyHealing = true` at spawn, so the first flip resolves to Damage), and `ResonanceUtility.
FirePulse`'s own enemy-damage call re-entered `DamageUtility.ApplyDamage`'s shared funnel, silently
regenerating Resonance from its own Pulse - fixed via a new generic `bool generatesResonance = true`
parameter on `ApplyDamage`, gating `ResonanceUtility.OnDamageDealt`, passed `false` by Zara's own
already-Resonance-sourced effects (the Pulse itself, Heavy Bass's Subwoofer, Afterbeat) only. Remix's
rank-2 "strengthened effect"/rank-3 "2 distinct effects" needed one new generic (not Zara-specific)
mechanism: a virtual 4-arg `HitEffectData.Apply(f, ref context, durationMultiplier, magnitudeMultiplier)`
overload (default forwards to the existing 2-arg `Apply`, so every other `HitEffectData` subclass
across every hero/weapon-perk is unaffected) that only `BurnEffectData`/`SlowEffectData`/
`StunEffectData`/`RiftMarkEffectData` override. Full design, the complete per-line rank breakdown, and
current status: **`docs/zara-ascensions.md`**. Read it before touching anything Zara-Ascension/Totem/
Resonance/Remix/Afterbeat/Portable-Speaker/`AlternatingArea` related.

Short version: the code compiles once codegen picks up every changed/new `.qtn` file, and
`SystemSetup.User.cs` registers a new `ZaraSubwooferPulseSystem` alongside the pre-existing
`ZaraAfterbeatSystem`. `Tools/RiftRaiders/Zara/Generate Ascension Assets` (replaces the old
`ZaraResonanceAssetGenerator.cs`, whose own `WireCharacterData` only append-and-deduped
`DashSkillUpgrades` - the exact drift bug that let the old, broken `PortableSpeaker.asset` survive
every prior regeneration) authors and wires all 10 lines, fully replacing every list it touches. Two
bugs found via live in-Editor testing were fixed: `ZaraThrowProjectileSpeaker.MaxDistance` had ended up
at `5.0` instead of `0` ("unlimited"), making the Totem's lobbed throw hit its own distance cap
mid-arc and plant the Totem in mid-air instead of on the ground; and `ZaraDeviceSpeaker.prefab` was
misidentified as Portable Speaker's spawn prototype when it's actually the THROWN PROJECTILE visual
(`ZaraThrowProjectileSpeaker.Prototype`) - Portable Speaker now correctly reuses `ZaraSpeaker.prefab`
(the Totem's own placed entity) instead, same "Dash mini-version reuses the Hero Skill's own entity"
precedent Kai's Warp Wake already established. Every numeric value not explicitly pinned by the brief
is a decisive placeholder pending a real balance pass. Not yet manually verified end-to-end in-Editor -
see the doc's own "Current status" for the full checklist.

## Talents (meta-progression) + Lobby Start

Talents are small, permanent unlocks earned OUTSIDE a match - flat named fields (`PlayerDamageLevel`...`PlayerExperienceLevel`, twelve 0-5 per-player leveling stats each worth +5%/level; `HasWeaponChest`/`HasHeroChest`/`HasGlobalUpgradeChest`/`HasUnlockedRift`/`CanFindStones`/`HasEvent`, six shared/coop bools OR'd across every connected player) living on `RuntimePlayer.Talents` - as of 2026-08-07 a single nested `PlayerTalents` struct field (grouped together with `WeaponLevel`/`RerollQuantity`, see "Level-Up Upgrades" above) rather than flat on `RuntimePlayer` itself. Same "seeded once from outside the match" contract `WeaponLevel` already had, persisted via one new `PlayerPrefObject` JSON pref (`MatchMakingConfig.TalentsPref`) mirroring `WeaponTalentLevelPref`. No hand-placed boundary entity - `ChunkType.Start` was renamed to `ChunkType.LobbyStart` in place, and `LevelGenerationSystem.TryGetLobbyStartBounds` reads that chunk's own world-space footprint straight off its existing `Transform3D`/`Chunk` fields (the same way it already reads the Boss Arena's footprint back out for its own grid-origin math); `LobbyBoundarySystem` polls that footprint each tick and transitions `Global.CurrentState` from `GameState.Lobby` to `GameState.Survival` (see "Game State" below) once ANY ONE connected, spawned player has physically walked outside it (first-one-out, deliberately not everyone-out - every connected player must still have spawned first) - `CombatDirectorSystem` (and therefore all enemy spawning/`Global.SurvivalTime` counting) only runs during `GameState.Survival`. Talent-gated spawning is a `ChunkSpawnConfig` DataAsset (`Assets/_QuantumUser/Simulation/Assets/Config/ChunkSpawnConfig.cs`), holding a `SpawnEntityWithRequirement[] Spawns` array (`AssetRef<EntityPrototype> Prototype`, `FPVector3 Offset`, `SharedTalentRequirement Requirement`, `FP Chance` per entry) - referenced via one new `AssetRef<ChunkSpawnConfig> SpawnConfig` field on `Chunk` itself (`Chunk.qtn`), typically assigned on the `LobbyStart` chunk prototype. Was originally a qtn *component* of the same shape, one instance per entity - reworked into an `AssetObject` array (same "array field on an `AssetObject`, not a component" shape `LevelConfig.ChunkPool`/`ChunkPoolEntry[]` already uses) once a single chunk needed more than one independent conditional spawn at once (e.g. Weapon+Hero+GlobalUpgrade chests together), which the old component shape couldn't do - Quantum entities can only carry one instance of a given component type. `TalentGateSystem` resolves every `Chunk` entity's own `SpawnConfig` (if assigned) exactly once (`f.Create` per satisfied/chance-rolled entry, the first entity-spawn-at-runtime pattern this codebase has used for something otherwise normally hand-placed, like a Chest). Nested/child `EntityPrototype`s were explicitly ruled out as a way to co-locate spawns with the chunk - Quantum's prefab importer only reads a prefab's root GameObject, silently ignoring nested `QuantumEntityPrototype`s. Full design, file map, current status, and known simplifications: **`docs/talents.md`**. Read it before touching anything Talents/meta-progression/LobbyStart/ChunkSpawnConfig related.

Short version: the code compiles once codegen picks up the new `Talents.qtn`/`Chunk.qtn` fields, and is registered in `SystemSetup.User.cs`, but no `TalentsConfig.asset` or `ChunkSpawnConfig.asset` exists yet (so `RuntimeConfig.TalentsConfig` and every chunk's `SpawnConfig` are unassigned) - nothing talent-gated spawns without both authored. `Assets/_QuantumUser/Entities/LevelChunk/LevelChunk.prefab` also still has the OLD component-based `SpawnEntityWithRequirement` added from before this rework - needs manual removal/replacement with a `SpawnConfig` assignment once codegen regenerates (see `docs/talents.md`'s own authoring checklist). On top of the pre-existing general "no Chest prototypes authored" gap `docs/chests.md` already tracks. Nothing currently *writes* to the new `player_talents` pref - same gap `weapon_talent_level` already had (an account/profile screen elsewhere would be what actually raises these over time). Not yet manually verified end-to-end in-Editor.

## Hero Info Popup (Tab-hold)

The Tab-hold overlay was renamed `UpgradePopupWidget` -> `HeroInfoPopupWidget` (GUID-preserving
rename, every existing scene assignment survived) and widened from a pure upgrade-history list into
a full "everything I'm currently running" readout: a new `HeroInfoWidget` (head icon, health/shield,
Base Skill and Passive Skill rows), then `CurrentWeaponUiWidget` (equipped weapon + its perks,
reused as-is), then the pre-existing 4 `LevelUpPoolKind`-split upgrade-history lists, unchanged.
`HeroInfoWidget` reimplements nothing - it composes `PlayerPortraitUiWidget`/`HealthUiWidget`/
`ShieldUiWidget`/`UpgradeWidget` and forwards `Initialize`, same shape as `PartyHudWidget`
(`UpgradeWidget` is already a generic icon+name+description+optional-level row; the two skill rows
pass level 0 so its badge stays hidden). Base Skill resolves off the LIVE `CharacterSkills.
HeroSkill.Skill`, not `CharacterData.HeroSkill`, so a mid-run Hero Skill swap shows up. One
simulation-side addition: `PassiveData` gained `Icon`/`DisplayName` via a new `PassiveData.View.cs`
partial (mirrors `SkillData`/`SkillData.View.cs`) - it previously had only a `Description`. Full
design, file map, and the Editor authoring checklist: **`docs/hero-info-popup.md`**.

Short version: the code compiles, no `.qtn` change so no codegen dependency, and the rename cost the
scene nothing. Nothing new is authored yet, though - no `HeroInfoWidget` hierarchy or
`CurrentWeaponUiWidget` instance exists under the popup, so `heroInfoWidget`/`currentWeaponWidget`
are unassigned (both are plain optional null-checks, so the popup still works as the old
upgrade-history list until they're built), and all 6 `PassiveData` assets have unauthored
`Icon`/`DisplayName`. Not yet manually verified end-to-end in-Editor.

## Minimap

A node-based minimap - the static layout (one filled square per placed `Chunk` entity, at that chunk's true relative grid position/size) plus a level outline are baked into a single procedurally-painted `Texture2D`/`RawImage` (`FilterMode.Point`, flat pixel-art look) rather than one UI element per chunk, so adjacent chunks' real footprints tile together into a connected blueprint with no adjacency/edge data needed, and repainting only touches the specific chunk(s) that actually changed, not every frame. Deliberately the first, decoupled slice of a much bigger future "Run Pacing + Exploration System" idea (assault/breathing rhythm, local aggro/leashing, POIs, world events) - only the minimap itself was built, since it needed almost nothing that didn't already exist in the simulation. A new shared/co-op `Chunk.Discovered` bool (`Chunk.qtn`) flips true via a new `ChunkDiscoverySystem` the first time any player physically enters that chunk's own world footprint - same X/Z-only containment-check idea `LobbyBoundarySystem` already uses for the LobbyStart chunk. View-side, `MinimapWidget` (`Assets/_Project/Scripts/UI/InGame/Hud/Minimap/`) reads `game.Frames.Predicted` every `QUpdate`: paints each chunk's fill on a `Discovered`/current-chunk change; computes the level's outline exactly once (gated on `Global.LevelGenerated`, not "first chunk seen" - a partial-snapshot timing bug that briefly made the outline lock onto just one chunk) via rasterize-then-edge-detect on a pixel occupancy grid (not per-chunk-pair edge matching, which gets partially-covered edges wrong), then only stamps that outline onto chunks that are actually `Discovered`; pans `mapRect` every frame to keep this instance's own local player centered inside a separately-authored mask container; plus a handful of small icon overlays for chunk types that opt in (Boss/Merchant/LobbyStart, not the generic Enemy/Traversal chunks the texture alone represents) and a marker per match player (local and remote alike). "Current chunk" and the centering both resolve per instance via `MyLocalPlayer.Slots[localSlotIndex]` - the one place this needs local-player awareness; otherwise every instance runs the identical frame query regardless of which split-screen slot it lives under, so it's deliberately *not* routed through `GameplayUiController` the way `choiceWindows[]` is - "one per split-screen local player" is purely a scene-hierarchy placement concern. A chunk rotated 90/270 has its authored Width/Depth swapped in world space (`LevelGenerationSystem.SwapsAxes`, reimplemented locally since it's `internal` to the Simulation assembly) - missing this was the actual root cause of the outline looking wrong/incomplete early on, since 3-in-4 chunks land rotated. Full design, file map, current status, and known simplifications: **`docs/minimap.md`**. Read it before touching anything minimap/chunk-discovery related.

Short version: the code compiles once codegen picks up the new `Chunk.Discovered` field, and `ChunkDiscoverySystem` is registered in `SystemSetup.User.cs` - chunks will actually start flipping `Discovered` at runtime. Nothing renders yet, though: no `MinimapWidget` scene instance, mask container, icon/player-marker template, or `chunkTypeSprites[]` exist yet - see `docs/minimap.md`'s own authoring checklist. Not yet manually verified end-to-end in-Editor.

## Environment Details

A View-only companion to the hand-authored "cube generator" (`CubeVisualBuilder`) - **reworked
2026-08-12 from a fully-procedural design into hand-placed slots**, after the procedural version
(computed positions from `ChunkWallCube` bounds + per-cell density rolls) hit real friction in
testing: `ChunkWallCube` boxes turned out to be room-spanning rather than thin wall strips, floor
height had to come from a box's `max.y` not `min.y`, and correct orientation needed non-uniform
scale hacks to counteract the camera's tilt. Now the artist hand-places `GroundDetailSlot`/
`WallTopDetailSlot`/`WallMidDetailSlot` GameObjects (`Assets/_QuantumUser/View/World/`, global
namespace like `ChunkWallCube` - wall is split into Top/Mid, not one generic wall type, since a prop
suited to a wall's upper portion usually doesn't suit its middle/base and vice versa) directly in
each chunk prefab - `[RequireComponent(typeof(SpriteRenderer))]`, a placeholder `Sprite` for Editor
preview, an authored `WorldSize`, and whatever position/rotation the artist wants - and
`ChunkDetailScatter`'s job shrinks to just: per slot, deterministically decide *whether* it shows
anything at all (`WorldTheme.Details.GroundDetailChance`/`WallTopDetailChance`/`WallMidDetailChance`,
one `[Range(0,1)]` per slot type) and, if so, *which* themed sprite
(`GroundDetails`/`WallTopDetails`/`WallMidDetails`, equal-probability `List<Sprite>`, no per-sprite
weight, no scale-variance range), then rescale to
`worldSize * ResolveUnitScale(sprite)` - `ResolveUnitScale` normalizes away the picked sprite's own
pixel size/PPU so swapping sprites never changes how big a slot reads on screen. It
**never touches position or rotation** - those stay exactly as authored, so a misplaced/misoriented
prop is now purely an Editor fix, not a script bug. Deliberately simulation-free (no `.qtn`
component, no codegen dependency), but the sprite pick still needs to agree across every
client/split-screen instance and not reshuffle on rebuild, so it's seeded from `RuntimeConfig.Seed`
combined with each chunk's own `OriginCellX`/`OriginCellZ` via a manual deterministic hash (not
.NET's `HashCode.Combine`, which is randomized per-process). Wall slots also get
`EnvironmentManager.DetailSpriteMaterial` - a dedicated `Project/Detail Sprite Height Fog` shader
(`Assets/_QuantumUser/View/Rendering/Shaders/DetailSpriteHeightFog.shader`) reimplementing just the
Height Fog block from the real level shader (`Project/Mobile Toon Modular Level`, which is opaque/
mesh-oriented and would render broken garbage on a `SpriteRenderer`), kept in sync with
`levelMaterial`'s own Height Fog properties every `EnvironmentManager.Load()`. `ChunkDetailScatter`
still has a public `Regenerate` `[Button]` for live Play Mode iteration on one chunk, and `WorldTheme`
still has `Apply To Scene (Debug)` and `Regenerate All Chunk Details (Debug)`
(`FindObjectsByType<ChunkDetailScatter>` + call `Regenerate` on each). `CubeVisualBuilder` also
gained optional, opt-in **detail avoidance** - one bool, `avoidNearWallDetails`, forces
`edgePrefabs[0]`/`outerCornerPrefabs[0]` (no separate prefab to assign) instead of the usual random
pick within `detailAvoidRadius` of an *actually-shown* wall detail. Since whether a slot actually
shows anything is a runtime roll only `ChunkDetailScatter` resolves (and `CubeVisualBuilder` has no
reliable lifecycle ordering against that), a cube with it on skips its own auto-`Generate()`
at `Start()` entirely and waits - `ChunkDetailScatter`, right after resolving every wall slot, sets
`ShownDetailPositions` (only the ones that actually passed their chance roll - an unset/empty list
if none did) on every such cube and calls `Generate()` explicitly, once. Full design, file map,
current status, and the old procedural design's own history: **`docs/environment-details.md`**. Read
it before touching anything ground/wall-detail-slot related.

Short version: the code compiles, no codegen dependency. Nothing shows yet under this new design:
no `GroundDetailSlot`/`WallTopDetailSlot`/`WallMidDetailSlot` has been hand-placed in any chunk
prefab, no `ChunkDetailScatter` added to a chunk root, no `WorldTheme.Details` sprite lists or
`*DetailChance` values authored (both default to `0`/empty, leaving every slot disabled), nothing calls
`EnvironmentManager.Load()` for a level's real "current theme" outside the debug-button/
`initialTheme` path, and no `Material` asset exists yet for the Height Fog shader (needs creating
in-Editor and assigning to `EnvironmentManager.detailSpriteMaterial` - not hand-authored here since
it needs the shader's own Unity-generated GUID) - see `docs/environment-details.md`'s own "Current
status" for the full checklist. The prior procedural design was manually verified working
end-to-end before being replaced.

## Game State

A structured top-level match-flow state machine - `Global.CurrentState`, a `GameState` enum (`Lobby, Survival, Upgrade, Event, Boss`) - replacing the independent ad hoc `Global` booleans each phase used to gate itself with (e.g. Talents/Lobby Start's own `LobbyExited`, now removed). `GameStateUtility.SetState` is the single place the value changes and fires a new match-wide `GameStateChanged` event (`Events.qtn`); it's deliberately thin (set + fire only) - each transition's own pause behavior is owned by whichever system drives it, since "does this also pause `GameplaySystemGroup`" genuinely differs per state (`Lobby` must NOT freeze player movement - they have to walk out of it; `Upgrade` does, via the pre-existing `SystemDisable<GameplaySystemGroup>` mechanism, unchanged). `LevelUpUtility.OpenUpgradeScreen`/`Resolve` now also drive `Upgrade` transitions, capturing `Global.PreUpgradeState` before switching so `Resolve` restores whichever state was actually interrupted (`Lobby` or `Survival`) rather than hardcoding `Survival` - a talent-granted Chest can be opened while still in `Lobby`. Full design, current status, and open questions: **`docs/game-state.md`**. Read it before touching anything GameState/match-flow/pause-time related.

Short version: the code compiles once codegen picks up the new `GameState.qtn`/`Events.qtn` fields. `Lobby`->`Survival` (via `LobbyBoundarySystem`) and `Survival`/`Lobby`<->`Upgrade` (via `LevelUpUtility`) are both fully wired. `Event` is still pure vocabulary only (mirrors the already-scaffolded `RuntimePlayer.HasEvent`, no system transitions into or out of it). `Boss` is no longer vocabulary-only, as of 2026-08-17: `CombatDirectorSystem.ApplyPhaseGameState` now routes `SurvivalPhaseKind.Boss` to `GameState.Boss` and triggers a one-shot `RunPhaseUtility.BeginBossEncounter` (teleport every player to the Boss Arena chunk, enable hand-placed `BossArenaGate`-tagged colliders sealing it, spawn `SurvivalPhase.BossPrototype`) - see "Boss Phase Trigger" below and `docs/run-phase.md`'s own "Boss phase trigger" section. The once-open pause question is resolved: `Boss` does **not** pause `GameplaySystemGroup`, same as `Survival`/`Breathing` - it's an active fight, not a menu. Simulation-side only so far - no View/UI code reacts to `GameStateChanged` yet, by explicit request.

## Boss Phase Trigger

The moment `SurvivalConfig`'s phase timeline (see "Survival Director" above) reaches a `Kind = Boss` entry, `RunPhaseUtility.BeginBossEncounter` fires once. Both the teleport destination(s) and the boss spawn position(s) come from two new hand-authored marker fields on a new `BossArena` component (`BossEncounter.qtn`, both `[4]`-capped arrays) rather than a single computed center - deliberately its own component, not fields on `Chunk` itself (every chunk in the level carries `Chunk`, dozens of them from procedural generation, so these arrays would otherwise sit wasted on every non-Boss chunk). A level designer places real marker GameObjects in the Boss Arena (`BossTeleportPointMarker`/`BossSpawnPointMarker`, `Assets/_QuantumUser/View/World/`) and a new `BossArenaMarkerBaker` `[Button]` (mirrors `ChunkRespawnPointBaker`, requiring both `QPrototypeChunk` and the new `QPrototypeBossArena` on the same prototype) bakes them in; unauthored (or no `BossArena` component at all) falls back to a single point at the chunk's own plain geometric center, so nothing breaks if a level never places either marker. Whatever position is resolved gets re-grounded via `EnemyMovementUtility.TryFindGroundHeight`/`GetGroundLayerMask` (same top-down ground raycast every normal Director spawn already uses), so nothing lands inside floor/prop geometry even off a slightly-off marker. Then: teleports every connected player, one marker per player slot (wraps around if fewer points than players) so they land spread out instead of stacked (`KCC.Teleport`, same idiom `DamageUtility.RespawnPlayer` already uses), enables every hand-placed `BossArenaGate`-tagged collider entity (an empty marker tag, `BossEncounter.qtn` - the level designer places the actual sealing colliders in the Editor; a new signal-only `BossArenaGateSystem` forces each one's `PhysicsCollider3D` disabled the instant it's created, regardless of what `IsEnabled` was authored on the prototype, so there's no "forgot to uncheck it" footgun; this whole mechanism does no adjacency computation of its own), and spawns `SurvivalPhase.BossPrototype` (a new `AssetRef<EntityPrototype>` field, read only for a Boss entry) once per resolved `BossSpawnPoints` entry via `EnemySystem.SeedFromEnemyData` - so 2+ authored spawn points spawn that many copies of the same boss (e.g. twin bosses), not different kinds - deliberately without `EnemyLifecycle` (same "shouldn't auto-retire, doesn't need Director pressure accounting" reasoning already established for the Scrapjaw boss-combat plan's own `SpawnPackDeliveryData` pack adds). `CombatDirectorSystem`'s own gate already stops normal Director spawning entirely once `GameState` becomes `Boss` - confirmed with the user, only the boss itself (and whatever its own abilities spawn) should be active during the fight. Right after spawning, if `SurvivalPhase.PauseDuration > 0` (a new `FP` field, 5s authored by the MVP generator - only takes effect once the generator is re-run, since editing the generator's own source doesn't retroactively update an already-authored `SurvivalConfig_MVP.asset`), `BeginBossEncounter` also `f.SystemDisable<GameplaySystemGroup>()`s - the exact same mechanism `LevelUpUtility.OpenUpgradeScreen` uses to pause a Level-Up screen, just auto-timed via a new always-on `BossPauseSystem` (counts `Global.BossPauseTimer` down, re-enables the group at 0) instead of player-choice-driven - confirmed with the user: a genuine hard freeze (player movement/weapons/skills, KCC, `EnemySystem`/`BossSystem` AI including the boss itself, the fall systems - everything inside the group), not just a visual overlay, so nothing can act while the Boss Window reveal (below) plays. This is purely the trigger/plumbing - a boss's own in-combat behavior (phases, stagger, combos) is the pre-existing, separate `BossDataAsset`/`BossSystem`/`BossRuntimeState` framework (see the Scrapjaw boss-combat plan, `.claude/plans/clever-herding-metcalfe.md`, for that framework's own design and the one boss built on it so far).

`EnemyView` (`Assets/_QuantumUser/View/Entities/Enemy/`) also gained a second, opt-in way to author a spawned enemy's visual - unblocks a boss's own one-off `EntityPrototype` specifically, since the normal path (`EnemyDataAsset.ViewPrefab`, pooled and fit-scaled to the collider radius at runtime) exists only because the SHARED generic Director prototype has to visually represent many different `EnemyData`. `SpawnSprite` now checks for an `EnemyViewRig` already baked as a real child of `spriteRoot` first; if found, it skips `ViewPrefab`/`ViewPrefabPool` entirely (nothing to instantiate, the GameObject already exists) but still applies the exact same `ResolveFitScale` sprite-bounds math, `Vector3.down * radius` bottom-pivot positioning, and `HasShadow` radius-based auto-scale to it - confirmed with the user, a boss's rig should sit at its collider's bottom center and dynamically track its own radius exactly like a normal enemy's pooled sprite does. Only rotation stays manual. A new `Resolve Scale` `[Button]` on `EnemyView` re-runs the whole resolve pass on a live entity in Play Mode for quick iteration (tweak `viewRadiusPadding`/`Stats.Radius`/the sprite, re-click, no respawn needed). No other enemy is affected.

A new `EnemyFallSystem` (`Assets/_QuantumUser/Simulation/Systems/Enemy/`) gives Boss/Elite-tier enemies the same "fall off the level → take fall damage → respawn to safety" treatment `PlayerFallSystem` already gives players - confirmed with the user, so a Boss or Elite pushed off a ledge (physics/knockback) can't end up lost/stuck instead of just dying normally like every other tier still does. The shared nearest-chunk/inset-into-bounds respawn math was extracted out of `PlayerFallSystem` into a new `FallRespawnUtility` so both systems use the exact same logic; Elite reuses it directly off its own current position (it has no tracked "last grounded" position the way `PlayerMovement` does), while Boss respawns specifically at its own sealed Boss Arena's `BossSpawnPoints[0]` (`LevelGenerationSystem.ResolveBossSpawnPositions`, ground-corrected the same way `RunPhaseUtility.BeginBossEncounter` already is) rather than the generic nearest-chunk fallback, since respawning it into some nearby chunk would strand it outside its own `BossArenaGate`-sealed boundary mid-fight.

View-side, as of 2026-08-17, the boss also gets its own dedicated HUD instead of sharing the normal enemy UI: `EnemyView.RefreshSprite` now skips `EnemyUiWidgetManager.SpawnWidget` entirely for `EnemyTier.Boss` (a new `EnemyView.IsBoss` gate) - no floating `CharacterUiWidget` above the boss at all - and a new single-instance `BossWidget` (`Assets/_Project/Scripts/UI/InGame/Hud/BossWidget.cs`) shows a top-screen name + HP bar + shield bar for whichever entity `frame.Filter<BossRuntimeState, Health>()` finds. Shield turned out to already be enemy-agnostic (`EnemySystem.SeedShield` seeds it off `EnemyDataAsset.Stats.ShieldMultiplier` for any enemy, boss included - `GrasslandOutpostBoss.asset` already has `ShieldMultiplier = 1` authored), so `BossWidget`'s shield bar needed no new simulation-side work. As of 2026-08-18, `BossWidget`/`DirectorTimelineUiWidget`/`TraversalChallengeWidget` (see "Traversal Challenge" below) all read one shared `Global.HudBanner` (`HudBannerKind`, `GameState.qtn`) instead of each independently re-deriving "am I the one that should show" off `GameState`/`ActiveTraversalChallengeCount` themselves - resolved once a tick by `CombatDirectorSystem.ApplyHudBanner` (Boss beats TraversalChallenge beats the DirectorTimeline default), so the three always stay mutually exclusive on-screen even though a Traversal Challenge deliberately never changes `GameState` itself. Every widget still polls `Global` directly every `QUpdate`, same idiom `BreathingCountdownWidget` already uses - deliberately still not the `GameStateChanged` event, so this section's own "Simulation-side only so far" line above stays accurate.

Two more View-only pieces, no simulation changes: a new `BossWarningWidget` (`Assets/_Project/Scripts/UI/InGame/Hud/BossWarningWidget.cs`) shows a "BOSS APPROACHING" HUD banner + countdown - but only during the LAST `Breathing` phase before `Boss` (peeked via `SurvivalConfig.Phases[CurrentPhaseIndex + 1].Kind == Boss`, same idiom `DirectorTimelineUiWidget`'s own marker-skip logic already uses) and only once `Global.BreathingTimeRemaining` drops to its own `warningThreshold` (10s default) - confirmed with the user, the boss encounter itself stays fully automatic (SurvivalConfig-driven), this is purely a heads-up layered on the pre-existing countdown, not a new pause/trigger stage. Separately, `BossWidget` now also triggers the `BossWindow` reveal (see "Boss Window" below) the instant it finds the boss entity for the first time each encounter (edge-detected via a new `_wasBoss` field) - reuses `BossWidget`'s own already-running "find the boss, resolve its `EnemyDataAsset`" lookup rather than a separate trigger component, and casts to `BossDataAsset` to pull `Title`/`Subtitle`/`UiSprite` for the window if it resolves.

## Boss Window

A full-screen reveal card (`Assets/_Project/Scripts/UI/InGame/BossWindow.cs`, `UiWindow` subclass) shown once per encounter, right as the boss spawns (triggered from `BossWidget`, see above) - similar in spirit to `ChooseWindow`'s own intro animation (same `ShakeGrowImpactAnimation` reveal building block, `useUnscaledTime: true` throughout) but with its own content and sequence: icon background → boss icon → title background → title text → subtitle text, each staggered via its own `ShakeGrowImpactAnimation`, then a hold, then the whole thing fades away via a `disappearCanvasGroup` (`Tween.Alpha`, 0.3s default). Not wired into `WindowManager` - called directly (`.Show()`), so it doesn't hide the HUD underneath, same "bypass WindowManager to keep the HUD visible" choice Cursed Rift/Store/Blacksmith already made. `Title`/`Subtitle`/`UiSprite` are three new fields on `BossDataAsset` (`BossDataAsset.View.cs`, a new partial file mirroring `EnemyDataAsset.View.cs`'s own simulation/view split) - deliberately separate from the base `EnemyDataAsset.EnemyName` already used by `BossWidget`'s in-combat HUD name, since the reveal card's own text doesn't need to match that 1:1. A `[Button] TestIntroAnimation()` (with an optional `testBossData` field to preview real content) lets it be tuned standalone in Play Mode without a live boss encounter.

The reveal is bracketed by a one-way camera-focus cutaway (confirmed with the user), also driven from `BossWidget`: a new `ScreenFadeWidget` (`Assets/_Project/Scripts/UI/InGame/Hud/ScreenFadeWidget.cs`, single shared instance, `Tween.Alpha` on a full-screen `CanvasGroup`) fades to black, `FollowCamera` (`Assets/_QuantumUser/View/Camera/FollowCamera.cs`) gets a new `SetFocusOverride(Transform, snap: true)`/`ClearFocusOverride(snap: true)` pair that locks its framing onto a single transform instead of averaging its normal multi-player `_targets` list (the `snap` default instantly repositions `_smoothedPosition` rather than easing, since the whole point is to already be exactly on target the instant the fade reveals it - no visible pan), then `ScreenFadeWidget` fades back in showing the boss in focus while `BossWindow` plays over it. The boss's own Unity `Transform` is resolved via `QuantumEntityViewUpdater.GetView(bossEntity)` (found once via `FindFirstObjectByType` in `BossWidget.Awake`) - same idiom `SentryView` already uses for resolving another entity's view from outside its own `CustomQuantumEntityViewComponent`. Returning to normal framing once `Global.BossPauseTimer` counts down to 0 (edge-detected each `QUpdate`, same shape as the `_wasBoss` edge-detect) is deliberately NOT a mirrored fade-out/fade-in - confirmed with the user, just `ClearFocusOverride(snap: false)` directly, letting `FollowCamera`'s own existing `Update()` lerp ease it back to the players naturally (nothing jarring to hide - the camera's already framing the arena, players and boss both right there). The enter cut degrades gracefully to a plain instant camera snap (no fade) if `ScreenFadeWidget.Instance` isn't found in the scene.

Short version: the code compiles once codegen picks up `BossEncounter.qtn`'s new `BossArena` component (`TeleportPoints`/`SpawnPoints`, moved off `Chunk` itself for the performance reason above). Nothing is authored yet: no `BossArenaGate`-tagged colliders exist in `QuantumGameScene.unity` around the Boss Arena's own corridor(s); the Boss chunk's own prototype doesn't have a `QPrototypeBossArena` added yet, and no `BossTeleportPointMarker`/`BossSpawnPointMarker`s have been placed under it either (both fall back to the chunk's plain geometric center until baked via `BossArenaMarkerBaker`'s `[Button]`, so this doesn't block testing, just leaves it unrefined); and `SurvivalConfig_MVP.asset`'s `Boss` phase's `BossPrototype` is unassigned (no real boss `EntityPrototype` exists yet). The new `BossWidget` also needs Editor wiring before it shows anything: a scene panel (name text/HP slider/shield slider) doesn't exist yet, and `DirectorTimelineUiWidget`'s new `visualRoot` field needs its existing slider/text/marker children wrapped under one new child container and assigned to it (its script currently sits directly on `DirectorTimelineWidget`, so `visualRoot` can't just be that same GameObject - see the field's own tooltip). `BossWarningWidget`/`BossWindow` are both entirely unauthored in-scene yet either (no prefab/hierarchy built, `BossWidget.bossWindow` unassigned, no `Title`/`Subtitle`/`UiSprite` authored on `GrasslandOutpostBoss.asset`). `ScreenFadeWidget` also has no scene instance yet (a full-screen black `Image`/`CanvasGroup` under the HUD Canvas) - until one exists, the camera-focus cutaway silently degrades to an instant, unhidden camera snap (no fade) rather than failing outright. Not yet manually verified end-to-end in-Editor.

## Breathing Phase, Healing Shrine, Cursed Rift & Choice Window Refactor

A repeating **Breathing Break** (~30s, ~4 per run, both configurable) now alternates with continuous
combat: `GameState` gained a `Breathing` value (not a parallel "RunPhase" enum - the existing
`GameState`/`GameStateUtility.SetState` already *is* the Run Phase concept). Breathing is
implemented as a genuine entry in `SurvivalConfig.Phases[]` (`SurvivalPhase` gained a `Boolean
IsBreathing` field - confirmed with the user, after two earlier iterations: first a standalone
`RunPhaseConfig` asset, then a flat `BreathingBreakStartTimes` list bolted onto `SurvivalConfig` -
both were superseded once Breathing became a first-class phase, since `SurvivalConfig.Phases[]`
already *is* the run's own pacing timeline and a designer just interleaves `IsBreathing = true`
entries between the Director's own combat-phase entries). `CombatDirectorSystem` (no separate
system) detects the transition as a natural extension of the same `SurvivalProgressionUtility.Tick`
phase-walk it already drives - its own gate widened from `CurrentState != Survival` to also allow
`Breathing` (so `PhaseTimer` keeps advancing *through* a Break), and it skips
`CombatDirectorUtility.TryPulse` entirely whenever the current phase `IsBreathing == true`. As of
2026-08-14, `SurvivalTime` and `PhaseTimer` are explicitly INDEPENDENT clocks (fixing a real
regression this phase-based rework had silently introduced): `PhaseTimer` still advances through
Breathing (it's what ends the Break), but `Global.SurvivalTime` - cumulative COMBAT time, consumed
by `BalanceConfig`'s run curves/co-op scaling - now freezes entirely while `IsBreathing == true`,
so a phase authored `Duration=120` always hands its successor `SurvivalTime==120` regardless of how
long any Breathing Break in between actually ran (previously it silently summed to 150 after a
30s Break). A new **Skip vote** lets any connected player send a zero-payload
`SkipBreathingCommand`; once every connected player has voted for the SAME Break (a per-player,
lazily-added `BreathingSkipVote.VotedAtBreathingIndex`, compared against `Global.BreathingIndex` -
same self-cleaning convention `PoiUsageEntry` already uses), `RunPhaseUtility
.TryForceSkipBreathing` (called from `CombatDirectorSystem` right before `Tick`) force-sets
`PhaseTimer` to the phase's own `Duration`, ending it exactly as if it had run out naturally -
`SurvivalTime` is untouched either way, so a skipped Break costs the run's own pacing nothing. See
`docs/run-phase.md`'s "Independent timers"/"Skip vote" sections. Leaving Breathing sweeps uncommitted
Cursed Rift/Store/Blacksmith interactions (`RunPhaseUtility`, called only on the tick `IsBreathing`
actually changes) - entering Breathing has NO enemy-clearing side effect: an earlier iteration
force-retired every non-`Economy.Persistent` enemy the instant a Break began, but that was removed
(2026-08-17, after it was reported as still wiping the screen on entry) once `Global.
BreathingAreaSecured` needed something real to hold on - `PhaseTimer` (and therefore the Break's own
countdown, and every Breathing-only POI's own availability) now stays frozen until every alive
enemy is actually killed or naturally `Retired` via the pre-existing `EnemyLifecycle` Irrelevant
timeout, same mechanism an Elite/Boss phase already used to hold its own encounter open - see
`docs/run-phase.md`'s "Elite / Boss phases" section. Breathing behaves like
`Lobby` - `GameplaySystemGroup` stays enabled, players remain fully controllable. Two new
Breathing-only world POIs share a small,
deliberately generic **POI availability/usage/view-state** layer (`Poi.qtn` -
`PoiAvailability{AvailableInCombat,AvailableInBreathing}` + a
`PoiUsagePolicy{Reusable,OncePerPlayerPerBreak,OncePerPlayerPerRun,OncePerWorld}` + a per-player
`PoiUsage` fixed-array component, same "keyed entries on the player entity" convention
`RiftMutationPicks`/`GlobalUpgradePicks` already use, PLUS a shared `PoiActivation` component -
`PoiViewState{Inactive,Active,Expired}`, refreshed every tick by a new `PoiActivationSystem` so
View code reads one already-resolved enum instead of re-deriving eligibility itself - `Expired`
means the POI is available but every CONNECTED player, not just the local one(s), has already used
it up this Break) - and, on the View side, a single generic `PoiView` component (reads only
`PoiActivation.State`, has no idea which POI kind it's on) instead of one near-identical View class
per POI type. The Context-Interaction prompt (below) is deliberately NOT part of `PoiView`'s own
3D view hierarchy - confirmed with the user, matching this codebase's existing
`CharacterUiWidget`/`EnemyUiWidgetManager`/`SentryUiWidgetManager` pattern for any HUD element that
tracks a specific entity: a HUD-Canvas-side `InteractionPromptWidget`/`InteractionPromptWidgetManager`
pair (plain `MonoBehaviour`s, not `CustomQuantumEntityViewComponent`s), spawned/despawned once for
the entity's whole lifetime from `PoiView.Initialize`/`DeInitialize` (only for entities carrying an
`Interactable` component) rather than living in the entity's own view prefab: **Healing Shrine**
(press-to-heal via `HealUtility.ApplyHeal`, resolved in the same tick as the press - originally a
pure walk-into-radius auto-heal with no UI at all, reworked (2026-08-14) to share the exact same
Base-Skill-button redirect Cursed Rift uses instead of two different interaction models, via a new
`HealingShrineUtility.ResolveInteractionState`/`TryInteract` pair mirroring `CursedRiftUtility`'s
own shape one-for-one - no persistent per-player component needed since there's no multi-step
choice to track between press and resolution, so the old per-tick `HealingShrineSystem` was deleted
entirely) and **Cursed Rift** (a deliberate, two-step irreversible choice - sacrifice something now
for a Rift Mutation reward - that explicitly must NOT pause the simulation, `Time.timeScale`, the
Breathing timer, or any other player; only the interacting player's own movement/weapon/skill input
is locked, via the same per-entity gate pattern `StatusEffectUtility.IsStunned`/`IsRooted` already
use, checked from `PlayerMovementProcessor`/`WeaponSystem`/`SkillSystem`). Both are opened via the
same generic **Context Interaction / Base-Skill-button redirect** (`ContextInteraction.qtn` -
`InteractableKind{CursedRift,HealingShrine}`, `Interactable{Kind,Radius,Priority}` on POI entities +
a per-player `ContextInteraction{ActiveTarget,ActiveKind,State}` resolved fresh every tick by a new
`ContextInteractionSystem`, registered right before `SkillSystem`, which now dispatches by
`ActiveKind` to whichever utility owns that interaction) - the first interact-button/prompt mechanic
in this codebase
(previously none; every pickup was pure auto-collect, see `docs/chests.md`). Target RESOLUTION is
purely geometric (closest in radius, ignores eligibility) so the world-space prompt can explain a
nearby-but-unusable POI instead of hiding silently; a `ContextInteractionState`
(`None/Available/PhaseUnavailable/AlreadyUsed/NotNeeded/Busy` - `NotNeeded` added 2026-08-14 for
Healing Shrine at full Health, generic rather than Healing-Shrine-specific so any future POI with
its own "technically available but pointless right now" case reuses it - unlike every other
non-Available state, `SkillSystem`'s own redirect gate still claims the press on `NotNeeded`
rather than falling through to a normal Hero Skill cast, since a deliberate press there should
fail loudly (a new `EventContextInteractionRejected`, fired by `HealingShrineUtility.TryInteract`,
drives a `ToastManager` "FULL HEALTH" popup via `InteractionPromptWidget` - press-triggered only,
never from mere proximity) rather than silently do something else) resolved separately for
whichever candidate
wins that scan is what actually distinguishes "usable" - `SkillSystem`'s own redirect only fires on
`State == Available`. `SkillSystem` intercepts a Hero Skill press when `State == Available` and
switches on `ActiveKind` to call `CursedRiftUtility.TryBeginInteraction` (opens the Choice Window)
or `HealingShrineUtility.TryInteract` (heals immediately, same tick) instead of sending a new
`DeterministicCommand` - deliberate, since it reuses the exact same polled-input-plus-`WasPressed` mechanism every other
skill activation already uses, not a menu-confirmation
action. The two-step choice itself (Sacrifice -> Apply Cost -> Rift Mutation Choice -> Apply
Mutation) lives in a new per-player `CursedRiftInteraction` component/`CursedRiftUtility`, processed
by a new `CursedRiftSystem` registered *inside* `GameplaySystemGroup` (unlike `LevelUpSystem`, which
lives outside it specifically because it disables that group - `CursedRiftSystem` never does, so it
has no re-entrancy hazard to guard against and keeps processing a player's commands regardless of
`GameState`, which is what lets a Breathing Break ending mid-mutation-selection - after a sacrifice
is already picked and its cost applied - leave that player's screen open until they finish, per the
design brief's own "Situation B"; `RunPhaseUtility`'s own end-of-Break sweep only cancels interactions
still in `SelectingSacrifice`, i.e. nothing paid yet). There is deliberately no separate confirm step
between picking a sacrifice and paying for it - clicking a sacrifice card applies its cost
immediately (`CursedRiftUtility.SelectSacrifice`), same "one click = one irreversible pick" idiom
every other Choice Window screen already uses (an earlier design had a `ConfirmingSacrifice` state +
a bespoke 2-button confirm sub-panel; both were removed once the user flagged the confirm step as
unnecessary new UI - see the Choice Window paragraph below). Sacrifices are their own small asset hierarchy
(`SacrificeDefinition` abstract base + `BloodOfferingSacrificeData`/`CoinOfferingSacrificeData`/
`RiftShardOfferingSacrificeData`, deliberately NOT `UpgradeData` subclasses - a sacrifice isn't an
upgrade) rather than a flat class with a `CostType` switch, matching this codebase's own established
`GlobalUpgradeData`/`RiftMutationData`/`PassiveUpgradeData` pattern. The Rift Mutation reward reuses
the *exact* existing pipeline - `LevelUpUtility.RollOptionsFor`'s weighted-draw-without-replacement
loop was extracted into a shared `DrawWeighted` helper (zero behavior change for existing categories)
behind a new public `LevelUpUtility.RollMutationOptions`, and `RiftMutationUtility.Grant` applies the
pick unchanged - no mutation logic duplicated anywhere.

The existing Level-Up/Weapon-Upgrade/Chest 3-card window was generalized, not replaced, so Cursed
Rift's Sacrifice/Mutation screens reuse the same card layout/navigation/rarity/animations -
**and, since 2026-08-14, the same window INSTANCE, not a copy of it.** The class itself was renamed
`UpgradeWindow` -> `ChooseWindow` (GUID-preserving rename, same Inspector-assigned values) after an
intermediate design (a second, hand-cloned `ChooseWindow` instance per local slot,
`GameplayUiController.poiChoiceWindows[]`, living outside `WindowManager`'s Canvas subtree, plus a
bespoke 2-button "CONFIRM SACRIFICE?" sub-panel) was explicitly rejected by the user once built - a
second, drifting-prone hand-glued window is exactly the "two near-identical things kept in sync by
hand" anti-pattern this codebase has already hit and fixed more than once (Zara's `PortableSpeaker`
drift, Pixie's dual-generator drift). `UpgradeCardWidget.CardData` still gained three empty-default
fields (`TopLabelOverride`/`ValuePreview`/`ButtonLabel`) and `ChooseWindow` still gained an optional
subtitle + an optional Cancel button - every addition defaults to reproducing the exact current
Level-Up visuals, so every existing call site is unaffected - but the confirm sub-panel
(`confirmPanel`/`confirmButton`/`backButton`/`confirmRecapText`/`onConfirmClicked`/`onBackClicked`)
is gone entirely: clicking a sacrifice card now applies its cost and rolls the mutation reward in
one simulation call, so there's nothing left for a confirm step to gate.
`GameplayUiController.BuildCardData`/`KindText` were loosened from `private` to `internal` (zero
logic change) so Cursed Rift's mutation stage reuses them directly. Cursed Rift's screen is driven
by a second method, `UpdateCursedRiftWindow` (called right after `UpdateUpgradeScreen` every
`QUpdate`), reading the exact SAME `GameplayUiController.choiceWindows[]` array (renamed from
`upgradeWindows[]`, `[FormerlySerializedAs]` preserves the scene's existing assignment) a real
Level-Up uses - it steps aside entirely whenever `Global.LevelUpScreenOpen` is true (a real Level-Up
always wins) and otherwise shows/hides each slot's window directly, checking the window's own LIVE
`activeSelf` rather than a separately-tracked bool so it self-heals regardless of what
`WindowManager` did to it. This is a genuine, confirmed-with-the-user tradeoff: because both flows
now share one instance still living under `WindowManager`'s own Canvas, a real Level-Up for a
DIFFERENT player can visually pre-empt a player's own in-progress Cursed Rift screen
(`WindowManager.ShowWindow<T>()` hides every registered window regardless of who triggered it) -
their own `CursedRiftInteraction` is untouched by this, so `UpdateCursedRiftWindow` re-shows their
screen (replaying its intro - a minor accepted visual hiccup) the instant the other player's Level-Up
closes. The always-visible `BreathingCountdownWidget` ("AREA SECURED" then "NEXT ASSAULT 00:30")
still never gets caught in this, since it lives on `GameplayWindow`, not `ChooseWindow`.

Also as part of this pass, confirmed explicitly with the user: **Coins and Rift Shards moved from
shared `Frame.Global` totals to PER-PLAYER wallets** (`CharacterStats.Coins`/`RiftShards`) - a
Cursed Rift Coin/Rift Shard sacrifice needed to be a real individual choice, not a party-wide tax.
`CurrencyOrbSystem` still finds a pickup off whichever single player physically reached it, but the
grant itself now broadcasts to every connected player's own wallet
(`CoinUtility.GrantAll`/`RiftShardUtility.GrantAll`), each scaled by *their own* gain multiplier -
"picking up 1 coin means everyone gets 1 coin," then each spends independently.
`CurrencyUiWidget` now self-binds per local slot for Coin/RiftShard (same `MyLocalPlayer.Instance
.BindToSlot` pattern `SkillCooldownUiWidget` already uses); Experience is untouched, still one
shared `Frame.Global` total. Full design, file maps, and current status: **`docs/run-phase.md`**
(Combat/Breathing state machine), **`docs/breathing-poi.md`** (Healing Shrine, Cursed Rift, Context
Interaction/Base-Skill redirect, per-player input lock), and **`docs/choice-window-refactor.md`**
(the `ChooseWindow`/`UpgradeCardWidget` generalization + the currency change). Read all three
before touching anything Breathing/Healing-Shrine/Cursed-Rift/Context-Interaction/Choice-Window/
per-player-currency related.

**2026-08-29: `PoiUsagePolicy` gained a `Cooldown` case** - a per-player, real-time cooldown
instead of a once-per-Break limit, meant to let a POI work as an anytime tool (usable in Combat
too, not just Breathing) with a repeatable time cost rather than a single use per Break. The
duration itself lives on the POI's own component (`HealingShrine.CooldownDuration`, new field,
same convention `HealPercent` already uses), not in the generic `Poi.qtn` vocabulary.
`PoiUsageEntry` gained a `CooldownRemaining` field, ticked down every frame by
`PoiUsageUtility.TickCooldowns` - called once per `PoiUsage`-carrying player entity per tick from
the existing `PoiActivationSystem` (one more loop in the same generic per-tick POI-infra pass, this
one keyed by player rather than by POI). Deliberately decays by `f.DeltaTime` rather than comparing
against a stored timestamp, since `Global.SurvivalTime` itself freezes during a Breathing Break
(see "Run Curves & Co-op Scaling" above / `docs/run-phase.md`'s "Independent timers") - a
timestamp-based cooldown would silently pause too, which isn't the intent. Healing Shrine is the
first candidate for this policy (see `docs/breathing-poi.md`'s own "Cooldown" bullet), but nothing
about it is Healing-Shrine-specific - any future POI's own component can add its own duration field
and opt in the same way.

Short version: the code compiles once codegen picks up every new/changed `.qtn` file
(`GameState.qtn` - now also `BreathingSkipVote`, `CharacterStats.qtn`, `Poi.qtn`,
`HealingShrine.qtn`, `CursedRift.qtn`, `ContextInteraction.qtn`, `Coins.qtn`/`RiftShards.qtn`),
`CommandSetup.User.cs` registers the new `SkipBreathingCommand`, and `SystemSetup.User.cs` registers
`PoiActivationSystem`/`ContextInteractionSystem`/`CursedRiftSystem` at their documented positions
(no separate Run-Phase system - that's folded into `CombatDirectorSystem`; Healing Shrine has no
system of its own either, post-2026-08-14 rework - see above). `Tools/RiftRaiders/Generate
Breathing POI Content` (`BreathingPoiContentGenerator.cs`) authors `CursedRiftConfig`/
`SacrificePoolData`/the 3 `SacrificeDefinition` instances - not yet run; it deliberately does NOT
touch `SurvivalConfig.Phases[]` (see `docs/run-phase.md` for why). There is no separate generator
for the Choice Window anymore (the earlier `CursedRiftChoiceWindowGenerator.cs`, which built a whole
second cloned window + Canvas, was deleted along with the design it supported - see the paragraph
above) - Cursed Rift now reuses `GameplayUiController.choiceWindows[0]` directly. Still needed by
hand before anything shows up at runtime: interleave `IsBreathing = true` entries into
`SurvivalConfig.Phases[]`; assign `RuntimeConfig.CursedRiftConfig` (`QuantumMenuConfig.asset`);
hand-place `HealingShrine`/`CursedRift` `EntityPrototype`s in a level, both now needing a real
`Interactable` component (`HealingShrine.prefab` already has one authored with a matching Radius,
but `Kind` still needs flipping from its stale default to `HealingShrine`; its own `HealingShrine`
component still needs `UsagePolicy`/`CooldownDuration`/`Availability.AvailableInCombat` authored if
the new Cooldown policy above is what's wanted for it, rather than the original Breathing-only
once-per-Break behavior) - a rough
`HealingShrine.prefab` and an in-progress `CursedShrine.prefab` (still on the wrong
`QPrototypeHealingShrine` component, mid-transition) already exist under
`Assets/_QuantumUser/Entities/LevelProps/`; on `choiceWindows[0]` (the existing Level-Up instance),
build and wire a `subtitleText` (`TMP_Text`) - `secondaryButton` (formerly `keepCurrentButton`,
already wired) is reused as-is, no new button needed - and on its own `cardPrefab` wire
`valuePreviewText` (`TMP_Text`, new) and `buttonLabelText` (`TMP_Text`, can just point at the
card's own existing baked button label) - none of these 3 fields exist in the scene yet; wire
`BreathingCountdownWidget` on the scene HUD prefab, including its new Skip Vote UI fields
(`skipButton`/`waitingRoot`/`waitingText` - code-complete, sends `SkipBreathingCommand` and shows
"WAITING FOR OTHER PLAYERS..." once this client's own local slot(s) have voted, but no actual
GameObjects built/assigned in the scene yet - see `docs/run-phase.md`); wire
`SkillCooldownUiWidget`'s new `contextInteractionIcon`/`interactPromptRoot` fields on the
HeroSkill-slot HUD instance; build out
`PoiView`'s Inactive/Active/Expired child visuals (already wired on `HealingShrine.prefab`) plus the
HUD-side `InteractionPromptWidgetManager` scene setup (neither exists yet); author real `Icon`
sprites for the 3 sacrifices. Not yet manually verified end-to-end in-Editor, solo or co-op - see
each doc's own "Editor authoring needed" section for the full checklist.

## Store & Blacksmith

Two more Breathing-only POIs on top of the same generic POI framework Healing Shrine/Cursed Rift
already established (`Poi.qtn`/`ContextInteraction.qtn`, see "Breathing Phase, Healing Shrine,
Cursed Rift & Choice Window Refactor" above) - a **Store** (buy weapons and food/utility items with
Coins) and a **Blacksmith** (pay Coins to add a new Weapon Perk to your currently-equipped weapon).
Both explicitly reuse the existing `ChooseWindow`/`UpgradeCardWidget`/`WeaponCardWidget` UI rather
than new parallel screens, via a new opt-in `PurchasableCardState`/`PurchasableCardUi` (price/
afford/sold-out affordance, defaults off so every existing Level-Up/Cursed-Rift call site is
unaffected) - and both reuse the existing weapon-generation/weapon-perk/currency systems rather
than duplicating them: Store's weapon offers roll via the exact same `LevelUpUtility.RollWeaponOption`
formula a Choose-Weapon level-up uses and grant via the same `WeaponChoiceUtility.Grant`; Blacksmith
adds a perk via the same `WeaponSystem.AddPerk` a level-up Weapon Perk pick uses. `GameplayUiController`'s
old binary `Global.LevelUpScreenOpen ? LevelUp : CursedRift` dispatch is now a real per-slot
`ChoiceWindowOwner` resolution (`None/LevelUp/CursedRift/Store/Blacksmith`) now that a 3rd/4th flow
shares the same window, and `CursedRiftUtility.IsInputLocked`'s old single check is now a shared
`PoiInteractionLockUtility.IsInputLocked` (OR across all three POI interaction components) read by
the same 4 call sites (`PlayerMovementProcessor`/`WeaponSystem`/`SkillSystem`/`ContextInteractionSystem`)
it used to serve alone. The one genuinely new mechanism: Store's shared inventory needed **per-player,
per-offer purchase tracking** (`StorePurchases`) since the existing `PoiUsage` is only one bit per
whole POI per player, not enough to track "did I buy offer N" independently - Blacksmith, by
contrast, reuses the existing generic `PoiUsagePolicy.OncePerPlayerPerBreak` unchanged, since it
really is "used up" per player per Break. Confirmed with the user: Blacksmith perk picks cost Coins
(same purchase UI/flow as Store, never offers an already-owned perk, no rank-upgrade mechanic);
`ShopWeaponOfferCount` (a meta-progression talent, same "seeded once at spawn" shape as
`WeaponTalentLevel`/`RerollQuantity`) maps rank 0 -> 1 Store weapon offer, rank 1 -> 2, rank 2 -> 3;
Store's screen shows both card families at once, food/utility row first, weapons row second. As of
2026-08-18, the Store also always offers a third, guaranteed, non-rolled card - "Increase Weapon
Level" - which levels up the buyer's own currently-equipped `Weapon` directly (a NEW `Weapon.Level`
field, `+5%` damage per level via `WeaponSystem.AddLevel`, same compounding idiom
`DamageMultiplierWeaponPerkData.Apply` already uses), purchasable once per player per Break. This is
a THIRD, deliberately separate "weapon level" concept from the other two already in the codebase -
`RuntimePlayer.Talents.WeaponLevel` (permanent meta-progression) and `CharacterStats.
WeaponTalentLevel` (live in-run, pure bookkeeping as of 2026-08-29 - see below) - neither of which
this purchase touches. **2026-08-29**: Store's weapon offers and a Choose-Weapon level-up/Chest pick
now scale off ONE shared, `Global.SurvivalTime`-driven curve (`LevelUpConfig.WeaponOfferCurve`,
mirroring `BalanceConfig.RunCurveAnchor`/`Evaluate`'s per-anchor-minute lerp shape) instead of two
independently-tuned mechanisms - previously Store scaled Weapon Level/starting perk count off
`Global.BreathingIndex` (`StoreConfig.BreakWeaponConfig`, now deleted) while Choose-Weapon/Chest
scaled its own starting perk count off `CharacterStats.WeaponTalentLevel` (which is why that stat is
pure bookkeeping now - it still increments, just drives nothing). A freshly-rolled weapon's starting
`Weapon.Level` is now also generic to both paths (`LevelUpOption.RolledWeaponLevel`, applied by
`WeaponChoiceUtility.Grant` - previously only a Store purchase ever produced a nonzero Level; a
Choose-Weapon/Chest pick was always Level 0). Full design, file map, edge cases, and current
status: **`docs/store-blacksmith.md`**. Read it before touching anything Store/Blacksmith/
`PurchasableCardState`/`ChoiceWindowOwner`/`PoiInteractionLockUtility`/weapon-level/
weapon-offer-scaling related.

Short version: the code compiles once codegen picks up every new/changed `.qtn` file (`Store.qtn`,
`Blacksmith.qtn`, `ContextInteraction.qtn`'s new `InteractableKind.Store`/`Blacksmith` values,
`Chunk.qtn`'s new `ChunkType.Blacksmith` value - Store itself reuses the pre-existing `Merchant`
value/`MarketChunk.prefab`, no new ChunkType needed, `CharacterStats.qtn`'s
`ShopWeaponOfferCount`, `StatusEffects.qtn`'s new `TempMoveSpeedRemaining`/`Multiplier` pair backing
the Energy Drink food offer - the temp-damage food offer instead reuses the pre-existing
`TemporaryWeaponDamageRemaining`/`Amount` unchanged, see "Max — Ascensions" above), and
`SystemSetup.User.cs`/`CommandSetup.User.cs` register `StoreSystem`/`BlacksmithSystem` and the 5 new
commands. `Tools/RiftRaiders/Generate Store & Blacksmith Content` (authors 4 `FoodOfferData`
instances, `FoodOfferPoolData`, `StoreConfig`, `BlacksmithConfig` - deliberately leaves
`WeaponPool`/`PerkPool` unassigned, same "no safe way to locate the right asset" gap every other
generator here has) has not been run yet, so nothing is authored: no `RuntimeConfig.StoreConfig`/
`BlacksmithConfig` assignment, no `Store`/`Blacksmith` `EntityPrototype` placed in a level, no
purchase-row UI (`purchaseRoot`/`priceText`/`currencyIcon`/`currencySprites`/`soldOutOverlay`) wired
on either card prefab, and `ChooseWindow`'s food row (`cards[]`) and weapon row (`weaponCards[]`)
still occupy the same overlapping rect (needs splitting into two visible sections for Store's own
screen to read correctly). Not yet manually verified end-to-end in-Editor, solo or co-op - see
`docs/store-blacksmith.md`'s own "Current status" for the full checklist.

**2026-09-01 bug fix: Blacksmith was rerolling its 3 perk offers on every visit, not once per
Breathing Break.** The roll used to live only on `BlacksmithInteraction`, the transient "window is
open" component removed on Cancel/purchase/Breathing-end - and since Blacksmith has no neutral
"just close" affordance the way Store's `CloseStoreCommand` does (its "CANCEL" button is the only
way to back out without buying), every non-purchase visit ended in a Cancel, which threw the roll
away. Fixed the same way `StoreInventory.RolledAtBreathingIndex` already works for Store: a new
per-player `BlacksmithOffer` component (`Blacksmith.qtn`) caches `PerkChoices` keyed by
`RolledAtBreathingIndex`, read/written by a new `BlacksmithUtility.EnsureOfferRolled` (mirrors
`StoreUtility.EnsureInventoryRolled` one-for-one) - only the FIRST visit each Break actually rolls;
Cancel now only closes the window, leaving the cached offer intact for the rest of that Break. See
`docs/store-blacksmith.md`'s "Blacksmith" section. Needs codegen to pick up the new `.qtn` component
before it compiles; not yet re-verified in-Editor.

## Traversal Challenge

A `ChunkType.Traversal` interactable prop turns a gap-crossing into a timed co-op puzzle: press the
Base Skill button on it (same generic `Interactable`/`ContextInteraction` redirect Healing Shrine/
Cursed Rift/Store/Blacksmith already use) and a set of platforms spawn, bridging the gap toward the
`GlobalUpgradeChestEntity` chest `TraversalChunk.asset`'s `ChunkSpawnConfig` already baked at a
fixed offset (an unfinished scaffold this feature completes). A `Duration`-second countdown (45s
authored default) starts; while it's running, `Global.SurvivalTime` AND `Global.PhaseTimer` (the
latter is what actually drives `Global.BreathingTimeRemaining`/ends a Breathing Break - without
freezing it too, a challenge activated mid-Breathing would let the Break quietly end and Director
spawning resume underneath it) both freeze, and `CombatDirectorSystem` stops spawning new enemies
globally, all via a new standalone `Global.ActiveTraversalChallengeCount` counter, checked at the
same three guard points `SurvivalPhaseKind.Breathing` already is - deliberately NOT routed through
`GameState.Breathing` itself (would fire unwanted `BreathingIndex`/POI-usage/Cursed-Rift side
effects), and `GameplaySystemGroup` is never disabled, so nobody's input is locked. Any connected player can
activate it and any connected player can complete it (reach a proximity checkpoint near the far
side) - the intended co-op case is one player fighting elsewhere while a teammate crosses, then
walking over to the now-permanently-solid platforms and collecting the chest later with no time
pressure. Both the checkpoint and every platform position are authored as offsets relative to the
owning CHUNK (resolved via `FallRespawnUtility.TryFindNearestChunk`, the same "Chunk seam gap
pattern" nearest-chunk lookup `Chunk.RespawnPoint` already uses), not the activator prop's own
placement - same frame of reference `TraversalChunk.asset`'s own `ChunkSpawnConfig` offset already
uses for the chest, and correctly rotation-aware for a chunk placed at 90/270°. If nobody reaches the checkpoint in time, every spawned platform is destroyed and the
challenge resets to retryable. Unlike every other POI, it deliberately has no `PoiUsagePolicy`/
`PoiUsage` (world-shared, not per-player-gated) and never touches `PoiInteractionLockUtility`
(nobody's input is locked). Full design, file map, current status, and known simplifications:
**`docs/traversal-challenge.md`**. Read it before touching anything Traversal-Challenge-related.

Short version: the code compiles once codegen picks up the new `TraversalChallenge.qtn`/
`ContextInteraction.qtn`/`Events.qtn` changes and is registered in `SystemSetup.User.cs`. The
countdown is a single, always-present, whole-team HUD banner (`TraversalChallengeWidget`, same
idiom `BreathingCountdownWidget` already uses for "NEXT ASSAULT") reading
`Global.TraversalChallengeTimeRemaining` directly - deliberately NOT a per-entity world-following
widget, since the pause effect is global for the whole team, not just whoever's looking at the
activator. Its own visibility, along with `BossWidget`'s and `DirectorTimelineUiWidget`'s, is now
arbitrated by one shared `Global.HudBanner` (see "Boss Phase Trigger" above) so only one of the
three ever shows at once. Nothing is authored yet - no
`TraversalChallengeActivator.prefab`/`Platform.prefab` exist, no `TraversalChallengeWidget` scene
instance under the HUD, and `TraversalChunk.prefab` doesn't have an activator instance placed under
its `Entities` child or a re-baked `ChunkSpawnConfig` yet - see the doc's own "Editor authoring
needed" checklist. Not yet manually verified end-to-end in-Editor.

## Hold-to-Revive (Alive → Downed → KO)

A player life-state machine (`Alive → Downed → KO`, neither of which existed in any form before
this pass - a lethal hit on a player used to go straight to an instant full-heal-and-teleport
`RespawnPlayer`, now deleted entirely) plus a hold-to-revive channel, built as the smallest reusable
extension of the existing Context Interaction / Base-Skill-button redirect (`ContextInteraction.qtn`,
see "Breathing Phase, Healing Shrine, Cursed Rift & Choice Window Refactor" above) - the same generic
mechanism Cursed Rift/Healing Shrine/Store/Blacksmith/Traversal Challenge already use. A Downed
player is damage-immune (reuses the existing `Invulnerable` tag) with a `DownedBleedOutDuration`
bleed-out timer as the only path to KO - confirmed with the user, and deliberately paused the instant
someone starts holding to revive them. Reviving a TEAMMATE is a genuine hold: `ReviveChannelSystem`
reads `Input.HeroSkill.IsDown` directly every tick (the first continuous-hold interaction in this
codebase - everything else is `WasPressed`-edge-triggered). **Reworked after initial testing showed
mid-combat revives were nearly impossible** (a lone reviver draws continuous enemy fire, and the
original design fully reset progress to 0 on release/out-of-range and separately froze-not-reset it
for 0.5s per hit - between the two, an uninterrupted window rarely lasted long enough, especially
for the 5s KO duration; the first attempt that actually landed tended to be the very next hold once
combat ended, which read as the target "automatically" reviving on the next Breathing Break rather
than a revive anyone actually completed): every cancel trigger (release, out-of-range, reviver
incapacitated, and now also a fresh hit - `ReviveDamageInterruptSystem`, renamed from
`ReviveDamagePauseSystem`, now interrupts the hold outright on `Combat.qtn`'s
`OnHealthDamageApplied`/`OnShieldDamageApplied` against the *reviver* instead of merely freezing it)
now leaves `ReviveProgress` untouched instead of zeroing it; `PlayerLifeStateSystem` decays it back
toward 0 at `ReviveConfig.ReviveProgressDecayRate` (default 0.5/sec, half the build rate) only while
nobody is actively holding, so a teammate resuming an interrupted revive picks up roughly where it
left off instead of starting over. **KO revival was ultimately removed entirely rather than tuned
further** - confirmed with the user ("remove KO revive functionality completely"): even after the
decay/interrupt rework, a full 5s KO hold stayed fragile under fire, so `ReviveChannel`/self-revive
now only ever apply to a Downed target (`ReviveTargetKind`/`ReviveChannel.Kind` were deleted
outright - nothing left to discriminate by), and `PlayerLifeStateUtility.EnterKO` now *removes*
`Interactable` instead of leaving it (previously untouched, since REVIVE/RESTORE was just a
View-layer label swap) - a KO'd player is no longer ever a valid revive candidate for anyone. The
reviver moves at a configurable reduced speed
(`ReviveMoveSpeedMultiplier`, default 0.30) rather than
being frozen - a deliberate carve-out in `PlayerMovementProcessor` distinct from the existing
Cursed-Rift/Store/Blacksmith full-movement-lock `PoiInteractionLockUtility` already provides,
confirmed with the spec. Revive always outranks an ordinary nearby POI regardless of distance via a
new, generic kind-based priority tier ahead of `ContextInteractionSystem`'s existing exact-distance
tie-break (`InteractableKindUtility.GetPriorityTier`) - confirmed reusable by any future
always-wins interactable, not hardcoded to Revive specifically. Every connected player also carries
their own personal, meta-progression-seeded `SelfReviveCharges` (mirrors the existing
`RerollQuantity`/`WeaponTalentLevel` talent pattern one-for-one) - usable while Downed regardless of
team composition, not solo-gated, but **no longer usable once KO'd** (`ReviveUtility.
TryPerformSelfRevive` now checks `State == Downed` specifically, not the generic `IsIncapacitated`)
now that KO revival was cut entirely - a KO'd player's own unspent charges just sit unused until
the area is secured (see below); `SelfReviveWidget` hides its own charges readout/button entirely
once KO'd rather than showing a permanently-dead control. **Self-revive is deliberately
NOT a hold** - reworked mid-implementation at the user's explicit direction into a dedicated small
HUD element (`SelfReviveWidget`, content-wise closer in spirit to `BossWindow` than `ChooseWindow` -
confirmed with the user this should NOT clone/extend `ChooseWindow`, matching this codebase's own
established anti-pattern precedent against a second parallel window; architecturally it's a Widget,
not a Window - a self-polling `QuantumGlobalMonoBehaviour` per local slot, same shape as
`SkillCooldownUiWidget`/`CurrencyUiWidget`, not a `UiWindow` subclass) with a single press/confirm
button sending
a new zero-payload `SelfReviveCommand`, processed by `PlayerLifeStateSystem`
(`ReviveUtility.TryPerformSelfRevive`) - entirely separate from `ReviveChannel`/`SkillSystem`'s Hero
Skill redirect, so a self-revive can never be damage-interrupted (no channel exists to interrupt) and never
races a teammate's own in-progress hold (both stay simultaneously valid; whichever completes first
just cancels the other via the normal "target became Alive" invalidation). A third, unconditional
path back to Alive was added after testing showed manual revival could stay out of reach for an
entire fight: **the instant `Global.BreathingAreaSecured` flips false→true** (an existing field,
recomputed every tick by `SurvivalProgressionUtility.Tick` - Breathing phase reached AND every
enemy actually dead/Retired), **every still-Downed/KO player is auto-revived**, no hold, no charge
spent - `Tick` edge-detects its own field's previous-tick value (no new field needed) and calls a
new `PlayerLifeStateUtility.ReviveAllIncapacitated`, which reuses the same `Revive()` every other
path funnels through. This is now KO's **only** way back, since neither teammate-hold nor
self-revive apply to it anymore - without it a KO'd player with nobody able to secure the area would
simply be stuck for the rest of the run. A minimal, vocabulary-only
`GameState.RunFailed` (same "wired later" precedent `GameState.Event`/pre-2026-08-17 `GameState.Boss`
already established) fires once every connected player is simultaneously down and nobody has any way
back on their own - updated for the KO change: a still-Downed player's unspent charges still count
as an escape, but a KO'd player's charges no longer do (`RunFailureSystem` now checks `State ==
Downed && SelfReviveCharges > 0`, not just charge count) - confirmed with the user as an explicit,
deliberately small addition rather than a full Game Over system. A Downed/KO player is also fully
untargetable by enemies, not just damage-immune - confirmed
with the user - the mirror image of the existing Burrow feature's own "make an enemy untargetable by
players" patch (`docs/enemy-burrow.md`), but touching a disjoint set of files since player-aims-at-
enemy and enemy-aims-at-player are separate code paths; deliberately checks
`PlayerLifeStateUtility.IsIncapacitated` rather than reusing a plain `Invulnerable` check, since that
tag is also used by two still-Alive cases (Cheat Death, post-revive grace) that must stay targetable.
The real fix is in `EnemySystem.UpdateChasing`/`UpdateRecovery` - `Enemy.Target` is otherwise fully
sticky once Chasing, so an enemy already locked onto a player before they went Downed now drops back
to Idle and re-acquires instead of harmlessly chasing them forever. A Downed/KO player's character
also visibly collapses - confirmed with the user as the priority feedback piece over a screen tint/
camera lock/impact shake (all considered, deferred) - via a new reversible `BlobAnimationView.
State.Downed` porting `EnemyBlobAnimationView`'s own Burrow shape (not Die, which is one-way since
a dying enemy is actually destroyed after); their weapon visually hides (`WeaponViewController`);
and `ShieldSystem` now also gates on `IsIncapacitated` so Shield stays frozen rather than quietly
recharging while they can't be hit anyway (`HealthRegenSystem` needed no equivalent change - it
already no-ops for them for free via `HealUtility.ApplyFlatHeal`'s own `CurrentHealth <= 0` guard).
`InteractionPromptWidget`'s hold-progress is a `Slider` now, not an `Image.fillAmount`, and its
bleed-out countdown reuses the existing `descriptionText` mechanism rather than a dedicated field.
Full design, file map, current status, and known simplifications: **`docs/revive.md`**. Read it
before touching anything Downed/KO/revive/self-revive/life-state/enemy-targeting related.

Short version: the code compiles once codegen picks up every changed/new `.qtn` file
(`PlayerLifeState.qtn`, `Poi/Revive.qtn` - most recently the `ReviveTargetKind` enum/
`ReviveChannel.Kind` field removal once KO revival was cut - `ContextInteraction.qtn`'s new
`Revive`/`Occupied` values, `StatusEffects.qtn`'s `ReviveImmunityRemaining`, `GameState.qtn`'s
`RunFailed`, `CharacterStats.qtn`'s `SelfReviveCharges`, `Events.qtn`'s
`PlayerDowned`/`PlayerKO`/`PlayerRevived`), `SystemSetup.User.cs` registers
`PlayerLifeStateSystem`/`ReviveChannelSystem`/`ReviveDamageInterruptSystem`/`RunFailureSystem`
right after `SkillSystem`, and `CommandSetup.User.cs` registers the new `SelfReviveCommand`.
`ReviveConfig.asset` (`Tools/RiftRaiders/Generate Revive Content`) is authored and has been used for
live in-Editor testing (Downed/KO/self-revive all confirmed reachable pre-KO-removal; the
teammate-hold/self-revive/KO-dead-end change itself hasn't been re-verified in-Editor yet) - see
docs/revive.md's own
"Editor authoring needed" list for whatever's still outstanding on the UI-polish side
(`ReviveInteractionPromptView`/`SelfReviveWidget` scene wiring completeness hasn't been independently
re-verified from code alone). Auto-revive-on-secure
(`PlayerLifeStateUtility.ReviveAllIncapacitated`, triggered from `SurvivalProgressionUtility.Tick`)
needs no additional Editor authoring - it reuses the existing `ReviveConfig`/`BreathingAreaSecured`.

## Hero Ascension Balance Pass (2026-08-20)

All six heroes (Max/Pixie/Kai/Brute/Zara/Lux) were normalized to **9 Ascension lines x 3 ranks**,
rebalanced, and - for Zara and Lux - substantially refactored. Read
**`docs/hero-ascension-balance-pass.md`** first: it holds the architecture decisions, the deliberate
deviations from the brief, and the list of values left open for playtesting. Per-hero detail lives in
each hero's own doc (each ends with a dated "2026-08-20 balance pass" section);
**`docs/lux-ascensions.md`** is new.

Counts now: Max 4 Overdrive/3 Passive/2 Dash. Pixie 3/3/3 (a deliberate deviation - Hot Fuse stays a
Dash line because its mechanic is dash-triggered; see the doc). Kai 4/3/2. Brute 4/3/2. Zara 4/3/2.
Lux 4/3/2.

**Generic primitives this pass added or generalized** - reach for these before writing anything
hero-specific:

- **Hard-CC diminishing returns**: `EnemyTierResistanceConfig.StunImmunityDuration`/
  `InterruptImmunityDuration`/`ImmuneToHardCC` + `StatusEffects.StunImmunityRemaining`/
  `InterruptImmunityRemaining`. `StatusEffectUtility.ApplyStun` (now returns bool) and
  `EnemyActionUtility.TryInterrupt` REJECT rather than refresh while the window runs. Defaults:
  Filler/Normal none, Specialist 2s, Heavy 3s, Elite 4s, Boss immune. This replaced Kai's own
  per-vortex interrupt tracker.
- **`WallSlamUtility.TryWallSlam`**: the shared *knockback source -> enemy movement -> valid wall impact
  -> wall-slam effect* step, extracted verbatim from Iron Shoulder's own private version once Brute's
  Groundbreaker needed the same reaction from a completely different source. Reports whether a wall was
  hit AND, separately, whether the Stun genuinely LANDED (they differ under a hard-CC immunity window or
  an `ImmuneToHardCC` tier). Any future knockback source wanting a wall reaction calls this rather than
  writing a second wall probe. It also owns the PRESENTATION half - it raises the generic `WallSlammed`
  event itself (wall contact point + push direction + whether the Stun landed), so every source routing
  through it gets the shared wall-impact VFX (`EffectsManager`) and camera shake
  (`ImpactCameraShakeListener`) with no per-source hookup; Brute's Iron Shoulder gained a wall visual it
  never had this way.
- **`EnemyStuckRecoveryUtility`**: safety net for an enemy a knockback drove INTO level geometry rather
  than against it. A hard push (Iron Shoulder sets velocity to 20 u/s, Groundbreaker up to 16.5, both
  `Override`) can move a body far enough in one physics step - into a corner of a chunk's COMPOUND
  collider, or a chunk seam - that the solver never recovers it; Quantum 3D has no CCD. Once the
  enemy's center is inside the geometry it can never get out on its own, because every wall check
  `EnemyMovementUtility` steers by raycasts FROM the enemy's own position, so `EnemySystem` just drives
  it deeper - it reads as the enemy walking into the environment and sticking. `OnEnemyKnockedBack`
  records the (known-good) spot it was standing in and opens a 3s window on `Enemy.StuckCheckTimer`;
  while that window is open `EnemySystem.Update` probes a half-radius sphere at the enemy's true
  collider center against the Ground layer and, on a genuine penetration, returns it there and zeroes
  its velocity. Deliberately a RECOVERY, not a clamp on knockback: clamping would flatten how knockback
  feels near any wall, would have to guess at drag, and still wouldn't catch an enemy popped up and
  OVER a wall (Discharge imparts +16 u/s upward, ~3.2 units of apex against the project's -40 gravity).
  Costs nothing for an enemy nobody knocked around. Reach for this rather than per-source wall clamps.
- **`LandingSource` + a 3-arg `OnPlayerLanded`**: the pre-existing generic landing signal
  (`PlayerMovement.qtn`/`AutoJumpSystem`, dormant with no consumer since Brute's old Ground Pound was
  removed) now also carries WHY the player was airborne - `Fall`/`Jump`/`Launched`, tracked on
  `PlayerMovement.AirborneSource`, claimed by `AutoJumpSystem.DoJump` and
  `DamageUtility.ApplyResolvedImpulse`, reset to `Fall` right after the signal fires. Brute's
  Groundbreaker is the first consumer.
- **ONE shared aura-DR slot**: `GuardianDamageReduction*` renamed to `AuraDamageReduction*`, now
  take-the-stronger. Brute's Guardian and Lux's Fire Support both write it, so aura DR never stacks
  additively between sources - the strongest simply wins. `TemporaryDamageReduction*` remains the
  separate REACTIVE-proc slot (Guardian R3, Zara's Protective Rhythm; Bodyguard R3 no longer uses it -
  its rank-3 payoff became a knockback shockwave, see the Brute section above).

**Zara's Resonance was REMOVED ENTIRELY and replaced by Flow State (2026-08-25)** - this supersedes
every earlier Zara/Resonance note in this file. Gone: the meter, the automatic Pulse and its damage/
heal/knockback, `Resonance.qtn`, `ResonanceUtility`, `ResonancePassiveData`, `ZaraRemixUtility`,
`ZaraProtectiveRhythmSystem`, `ResonanceFxView`, the `ResonancePulseReleased`/`RemixPulseTriggered`
events, `StatusEffects.ProtectiveRhythm*`, and `DamageUtility`'s `generatesResonance` parameter. Zara is
now 100 HP / **0 Shield** with no dormant recharge config - **Brute is the only hero with a personal
Shield mechanic**. Her passive is **Flow State** (`Flow.qtn`/`FlowStatePassiveData`/`ZaraFlowUtility`/
`ZaraFlowSystem`), deliberately **two things only - a fill and an on/off state**: `Progress` runs 0->1
over 2.5s of movement, `IsActive` flips when it lands and is worth +15% Move Speed / +15% Fire Rate,
1.25s stationary grace then the full bar drains over 4.5s, and a hostile hit empties it outright. (It
shipped first as a 3-stack ladder and was simplified the same day - "am I in the groove" is a binary,
"am I on stack 2 or 3" is bookkeeping.) The stat bonus is rebaked onto `CharacterStats` from captured
baselines on the TOGGLE only, never as the bar moves. Movement is read
from player INPUT (`Input.Direction` + an active Dash), never velocity - which is what makes knockback/
teleports/physics shoves unable to build it, with no per-source exclusion. Her three passive lines are
now **Faster Tempo** (build rate + per-stack value), **Second Wind** (recovery after a break; R3 "Keep
the Beat" = 30% DR on a hit taken at Max Flow) and **Headliner** (Max Flow payoffs; R2 boosts Totem
Beats, R3 fires a party Hype buff). Afterbeat migrated to Flow (R1 grants a stack + shaves next-stack
time per enemy dashed through; R3 grants one more stack if either beat lands). Totem and Portable
Speaker are functionally untouched.

**New GENERIC primitive - `Combat.qtn`'s `OnHostileHitConnected(target, attacker)`.** Fired from
`DamageUtility.ApplyDamage` ABOVE every negation layer (below `Invulnerable` and the friendly-fire
guard, above Free Hit Guard and Accessory Guard), so it is the authoritative **"was I hit?"** as opposed
to `OnHealthDamageApplied`'s **"did I lose anything?"**. A hit fully negated by the Accessory Guard or a
Free Hit Guard still fires it; a dodged/i-framed hit does not. Requires a live `Enemy` attacker and is
gated on `bypassOutgoingResolution == false`. Any future negation mechanic placed beneath that line
inherits correct behaviour for free. Zara's Flow is its first consumer ("guarding saves your health but
not your rhythm"); Keep the Beat's DR reaches the triggering hit because Quantum dispatches signals
synchronously above the resolution steps. `AlternatingArea` also gained a generic
`EffectivenessMultiplier` (applied to both Damage and Support beats) so Headliner R2 can scale an
already-deployed Totem without `AlternatingAreaSystem` knowing Zara exists. See
`docs/zara-ascensions.md`.
- **`AreaAllyBudget` + `AreaAllyBudgetUtility`**: per-**spawned-deployable**, per-ally spend caps (HP
  healed, cooldown reduced). Lives on the area entity, so a fresh deploy is a fresh allowance and two
  Zaras never share one. Backs Zara's global Totem healing cap and Sound Boost's cooldown cap.
- **`ModifyRemainingCooldownEffectData`**: generic "reduce this ally's remaining skill cooldown" hit
  effect, budget-aware, clamped at 0, never banks. Use this instead of hero-specific cooldown code.
- **`AllyBuffEffectData`**: generic timed ally-buff bundle (Move Speed / Fire Rate / outgoing damage /
  DR / flat Shield, all opt-in). Shared by Zara's Support Beat, her Portable Speaker and Lux's Fire
  Support aura.
- **`DelayedBlast` + `DelayedBlastSystem`**: generic one-shot "go off shortly, over there" blast parked
  on the owner. Pixie's Unstable Mixture R3 and Brute's Aftershock R3 both use it.
- **`DespawnIntent` + `DespawnIntentUtility`**: despawn/death REASON tags, so a housekeeping removal
  (a Sentry replaced past Lux's cap, a Speaker replaced, a Sentry relocated) doesn't fire on-death
  effects. Absence of the component means "genuine death", so nothing had to be retrofitted.
- **`StatusEffects.TempOutgoingDamage*`**: timed outgoing-damage buff across every `DamageSource` (the
  Weapon-only pair already existed).
- **`HitEffectContext.SourceEntity`**: the area entity that produced a hit, so a per-instance effect can
  find its own instance.
- **`WeaponSystem.RefillMagazine`**: explicit one-shot magazine refill (Max's Full Throttle R3).
- **`WeaponPostImpactProcs.ExplosiveSequenceChance`/`ExplosiveSequenceCooldown`**: optional proc chance
  and optional internal cooldown on the shared explosive-proc path. Both default to "off", so the
  Explosive Sequence weapon perk is unchanged; Pixie's Explosive Rounds is the one consumer.
- **Proc-source tagging (reused, not new)**: `isExplosion` + `isChainedExplosion` now also gate Pixie's
  Unstable Mixture stack GAIN and SPEND - a chained blast is a payout, never a new link.

Short version: all three assemblies (Simulation, View, Editor) were verified to compile cleanly against
freshly-run codegen. Every hero's asset generator was updated (`Tools > RiftRaiders > <Hero> > Generate
Ascension Assets`; `LuxScrapAssetGenerator` was replaced by `LuxAscensionAssetGenerator`) and **none of
them has been run yet** - until they are, the live `.asset` files still describe the old rosters. Stale
`.asset` files for deleted lines (Max's Burning Vengeance, Pixie's Unstable Targeting + the old Passive
Direct Hit, Kai's Warp Wake, Zara's Heavy Bass/Restorative Beat/Healing Chorus, Lux's Efficient
Salvage/Enhancement/Portable Cover, Brute's Unstoppable) need deleting by hand. Nothing has been
verified in-Editor yet.

**Follow-up the same day - Brute's third Passive replaced.** Brute stays at 9x3; **Unstoppable was cut
and Groundbreaker put in its place**, so his Passive pool is now Iron Presence / Guardian /
Groundbreaker. Groundbreaker is terrain/verticality CC - *high ground -> drop -> impact shockwave ->
knock enemies away -> wall slam -> stun -> burst window* - reacting to the generic `OnPlayerLanded`
signal above with a plain configurable `MinimumFallHeight` (2, deliberately double
`MovementDataAsset.MaxLedgeHeight`) rather than anything tied to map tiles or terrain tiers, and reusing
`WallSlamUtility` for the wall half and the pre-existing generic Rupture status for rank 3's Exposed
window. It deliberately shares NO design space with Momentum (no generation/retention/reset, no Move
Speed, no Juggernaut duration), and rank 3's Exposed is gated on the wall Stun genuinely landing - never
on merely being caught in the shockwave. Files: `Groundbreaker.qtn`, `BruteGroundbreakerSystem`,
`GroundbreakerPassiveUpgradeData`, `WallSlamUtility`, `event GroundbreakerSlammed`. View FX (all with
working fallbacks, so nothing breaks unauthored): a radius-scaled landing burst + optional ground decal
and a generic wall-impact spark on `EffectsManager`, plus a new `ImpactCameraShakeListener`
(`View/Camera/`, local-player-filtered, radius-scaled landing shake) - see `docs/brute-ascensions.md`'s
"View FX" section. Deleted with
Unstoppable: `Unstoppable.qtn`, `BruteUnstoppableSystem`, `UnstoppablePassiveUpgradeData`,
`BruteAscensionUtility.ResolveImpactDamageMultiplier`, and the two generic hooks that existed SOLELY for
it - `CharacterStats.HardCcDurationMultiplier` and `StatusEffects.HardCcImmunityRemaining` (the per-tier
hard-CC diminishing-returns row above was kept - Kai/Brute/Zara all still use it). Full writeup:
**`docs/brute-ascensions.md`**'s own "2026-08-20 (later)" section.

## Recoverable Accessory Guard

Every hero wears a **Signature Accessory** that eats one incoming hit outright, loses one durability
point, and physically pops off into the world as a collectible the owner has to walk back for
(`Equipped -> Airborne -> Dropped -> Equipped`, or `-> Broken` at 0 durability, where no collectible
spawns at all). Durability persists across the whole run - `Survival -> Break -> Survival` - and is
only ever restored by paying a Merchant, deliberately turning it into both a *spatial* combat
resource (a block relocates you mid-fight) and an *economic* Break decision (restore defense now vs.
save Coins for weapons/perks). The block hook is a single early-return in `DamageUtility.ApplyDamage`
placed ABOVE every resolution step (a block NEGATES the hit - no crit roll, no elemental proc, no
Rage/Resonance build, no `OnWeaponHitLanded`/`OnHealthDamageApplied` signal, and therefore no
revive-channel interrupt) and BELOW the `Invulnerable` check (Cheat Death/post-revive grace must not
burn a durability point), gated on `bypassOutgoingResolution == false` - the same gate
`OnWeaponHitLanded` already uses to exclude DoT-tick replays, which for free also excludes
`PlayerFallSystem`'s fall damage and `SentryDecaySystem`'s self-drain. Multi-hit sources are
self-limiting with no cooldown/i-frame window, since `TryBlock` only fires while `Equipped`. The
dropped collectible is ONE shared, fully generic `EntityPrototype`
(`RuntimeConfig.Prefabs.DroppedAccessoryPrototype`). Unlike every currency drop it picks its LANDING
POINT FIRST and then solves an exact arc onto it (`AccessoryGuardUtility.ResolveLandingPosition`
samples ring spots and takes the first with real Ground under it - water is its own Unity layer, so
it's rejected by the same test as a pit, no water-specific check needed; falls back to the owner's
own feet, solid by definition; variety comes from a randomised launch ANGLE, never velocity, which
would break the solved arc). Lobbing it blind and correcting afterwards was a visible teleport, which
is why the fall-rescue below is now only a backstop for post-landing displacement. Three further
deliberate differences from a currency orb: collection always returns it to its **OWNER** regardless
of who picks it up (co-op - `AllowAllyRecovery`, default on; deliberately NO proximity
requirement and no carry mechanic - an owner-must-be-nearby gate was built and reverted, since its
failure mode is a silent "nothing happened" and the case barely occurs, and carry-and-deliver charges
the helper twice while the owner pays nothing; the two coherent designs are the extremes, both
reachable from that one flag - and `AccessoryRecovered` carries a `Recoverer` purely for View credit), **no lifetime at all** (a timer would silently turn a recoverable
resource into a broken one), and it **may land on higher ground** - `PopMotionSystem`'s 0.5-unit climb
clamp is opted out per drop via a new `PopVelocity.CanLandHigher` (false for every currency/scrap
caller, so nothing else changes), because a coin you must climb for is a chore whereas retrieval IS
this mechanic. `Airborne -> Dropped` is read straight off `PopVelocity`'s
own removal by `PopMotionSystem` rather than a second timer, so an accessory can't be re-caught
mid-air - `DroppedAccessoryView` reads that same signal to spin the sprite while it's in the air and
ease it forward to 0 on landing. That view OWNS both billboarding and the spin in one `LateUpdate`
write (it reproduces `Billboard`'s own `LookRotation` and composes the spin on top in billboard
space, disabling any `Billboard` on that transform - two writers to one rotation is a script-order
coin flip), with `spinAxis` defaulting to Z so a camera-facing sprite rolls in the screen plane
instead of going edge-on. The angle is accumulated in its own float field: reading
`localEulerAngles` back is silently broken on a tilted transform (`AccessoryOrb`'s sprite child sits
at X = 45), which is what made the first version not spin at all. Recovery restores the STATE,
not the durability. A BREAKING block (durability -> 0) still flies the accessory, as
`DroppedAccessory.Broken` debris - non-collectible, untracked by the guard, destroyed on landing,
where `AccessoryBroken` fires so the shatter VFX (`EffectsManager`) plays at the resting point rather
than on the player. Because a blocked hit returns before `EntityDamaged` fires, every normal damage
reaction is bypassed, so `HitFeedback` (character flash, top-priority `FlashDamage` tier) and
`HurtOverlayUiWidget` (screen flash + a flat `blockHitStopDuration` hit stop, since there's no damage
to scale a tier off) both listen to `AccessoryBlocked` instead - without them a block reads as a
miss. Recovery (walking over it, or the Merchant restore) plays its own particle at the pickup point plus a
LOW-priority `recoverFlashColor` flash, so it can't stomp a simultaneous hit flash.
`CharacterUiWidget` shows an `accessoryEquippedRoot` plus an `accessoryGuardPips[]` array
deactivated from the right (index i shown while i < CurrentDurability). Finding a drop again is
a FOURTH ground arrow on `MovementRingView` (the local player's own movement ring), not a HUD element
- deliberately folded into that component rather than a parallel one of its own, since it is the same
job as its pre-existing target arrow: a flat ground sprite orbiting the character, aimed
at a world entity, sharing the ring's own grounded fade and `PositionAndRotateArrow`. Reads
`AccessoryGuard.Accessory` directly (broken debris is untracked by the guard, so a break correctly
shows nothing), positions off `EntityViewManager.GetEntityTransform` with a `Transform3D` fallback,
XZ-only for both heading and distance. Entirely optional - gated on `accessoryArrowSprite` being
assigned, with an optional companion `accessoryIconSprite` painted at spawn from that hero's own
`CharacterData.Accessory.CollectibleSprite` (the same sprite `DroppedAccessoryView` puts on the
pickup) which keeps its flat authored rotation while the arrow turns. Local-player-only comes for
free from `executeOnlyOnLocal`. Max is the one hero needing
extra View work: `BerserkFxView` swaps his head sprite across 3 tiers (Normal/Berserk/Overdrive) and
that renderer lives inside only ONE of `AccessoryView`'s two roots, so it gained a parallel hatless
set (`noAccessoryHeadSprite` + per-tier sprites, each falling back to its with-hat counterpart) and
writes BOTH heads on every tier change - deliberately without reading `AccessoryGuard` itself, so the
two components stay ordering-independent and no accessory logic is duplicated.

Merchant repair/replacement is sold on the **Store**'s own screen, modelled directly on its existing
guaranteed "Increase Weapon Level" offer (not part of the rolled `StoreInventory`, price resolved
live, own zero-payload `BuyAccessoryServiceCommand`): `2/3 -> repair 25`, `1/3 -> repair 50`,
`0/3 Broken -> replace 100`, always straight back to full - **there is no partial/per-point restore
path anywhere in the feature**. Costs are an explicit `FP[] RepairCostByMissingDurability` array
indexed by MISSING durability (plus `BrokenReplacementCost`) on `AccessoryGuardConfig`, not a formula,
with an Editor-only `OnValidate` guardrail enforcing "more damaged -> more expensive" and
"replacement > any repair". It needs no once-per-Break purchase tracking of its own - a successful
service restores to full, which immediately resolves to `AccessoryServiceKind.None`, so **the state is
the limit** - and it can never consume the weapon purchase allowance, since it touches neither
`StoreUtility.ResolveWeaponOfferCount` nor `StorePurchases.Entries`. Repairing while the accessory is
still lying out in the level is deliberately allowed; `AccessoryGuardUtility.Restore` destroys the
outstanding collectible, which is what upholds the never-both-worn-and-on-the-floor invariant.

**The gameplay system is completely hero-agnostic** - no simulation file (and no View file that drives
behaviour) ever names a hero. The per-hero half is one `HeroAccessoryPresentation` struct on a new
`CharacterData.View.cs` partial (`DisplayName`/`CollectibleSprite`/`CollectibleScale` - the scale
exists because ONE prototype serves every hero, so a cap and a headset need per-hero size
correction; it multiplies the prefab's authored scale, 0/unset reads as 1), resolved generically
through the owner's own `CharacterStats.CharacterData` by `DroppedAccessoryView` (paints the shared
prototype's `SpriteRenderer`). The WORN half deliberately does NOT live in hero data: `AccessoryView` is a
two-way SWITCH between two hand-placed GameObjects on the hero's own view prefab
(`equippedVisual` ON / `unequippedVisual` OFF while `Equipped`, reversed otherwise), because these
rigs are sprite-based (`head_0`/`Torso_0`/`CharBody`) so "wearing it" and "not wearing it" are two
different authored head sprites rather than a prop parented onto a bare head - the same
active-object-swap idiom `BlobAnimationView` already uses for Alive/Downed/KO, and it keeps per-hero
RIG references on the prefab (next to `BlobAnimationView`'s) while per-hero ASSETS stay in
`CharacterData`. `AccessoryView` POLLS `AccessoryGuard.State` every
`QUpdate` rather than subscribing to the 5 new events - state is authoritative and self-healing, same
reasoning `BlobAnimationView`/`WeaponViewController` document for their own Downed/KO swaps; the
events (`AccessoryBlocked`/`Landed`/`Recovered`/`Broken`/`Restored`) exist for one-shot FX and have no
subscriber yet. The component is seeded by `CharacterSystem` (gated on `PlayerLink`) rather than
authored per-hero-prototype, so the whole mechanic switches on/off with a single
`RuntimeConfig.AccessoryGuardConfig` assignment. Full design, file map, the acceptance-criteria
coverage table, and current status: **`docs/accessory-guard.md`**. Read it before touching anything
accessory/durability/accessory-repair related.

**Shield now exists to serve this feature (2026-08-25).** Player Shield was reworked from a free
auto-regenerating absorb pool into an earned, charge-only buffer whose job is to keep the Accessory on
your head, because a bar that refills every 5s blunted the whole Merchant-repair decision. Three rules:
(1) player Shield **never auto-recharges** (`Shield.ChargeOnly`, seeded from the new
`CharacterData.ShieldChargeOnly`, on for all six heroes) and starts a run **empty**; (2) **Shield
protects the Accessory, up to what it can actually soak** - `DamageUtility.ApplyDamage` skips
`AccessoryGuardUtility.TryBlock` only while `Shield.Current` fully covers the incoming hit (a gate,
deliberately NOT moving the hook below `AbsorbWithShield`, which would forfeit the block's
no-crit/no-proc/no-signal negation contract); (3) **Overshield is deleted** - `ShieldUtility.ApplyOvershield` and
every `OvershieldCapMultiplier` are gone, all grants cap at Max. Enemy/boss shields are untouched
(`EnemySystem.SeedShield` never sets the flag), so the Shielder enemy, the `ShieldWall` group and
`BossWidget`'s bar still recharge classically. **As of 2026-08-29, a hit BIGGER than the remaining
Shield no longer overflows to Health** - it's the accessory's cue instead: `TryBlock` negates the
whole hit (Shield left untouched, not drained) and spends a durability point, so an overwhelming hit
now costs the hat, not your life; only once the accessory itself can't block
(`Broken`/`Disabled`/no durability) does the old drain-then-overflow-to-Health path still run. See
`docs/accessory-guard.md`'s "2026-08-29 — Shield only covers what it can afford" section. Also
new and generic: **Free Hit Guard** (`StatusEffects.FreeHitGuardRemaining`/`FreeHitGuardSource`,
`StatusEffectUtility.ApplyFreeHitGuard`/`TryConsumeFreeHitGuard`) - a one-shot timed complete negation
consumed immediately ABOVE the accessory hook (a free gift outranks a Coin-priced durability point),
reporting via `Combat.qtn`'s `OnFreeHitGuardConsumed` so the granting ability owns the reward; Brute's
Bodyguard is its first consumer, not its owner. Knock-ons deliberately NOT retuned yet (balance calls,
each listed in the doc): Kai/Pixie/Max have no Shield source of their own and sit at 0; the
`PlayerMaxShieldLevel` talent; and the now player-dead
`StatusEffects.ShieldRegen*`/`ShieldRegenBuffView`. Two knock-ons WERE resolved (both 2026-08-27):
every Shield-dependent **Rift Mutation** was rebuilt around the Accessory instead (Glass Core doubles
durability and halves Health, Last Bastion disables the Accessory, Infinite Momentum costs Health, and
Shield Breaker's role became the Accessory-block-triggered Adrenaline Kick) - see "Rift Mutations" above; and
the "+10 Max Shield" Global Upgrade was replaced by **Toughness** ("-10% Damage Taken", a new
compounding `CharacterStats.DamageTakenMultiplier` read by `DamageUtility.ResolveDamageReduction` -
multiplicative precisely because that pool stacks indefinitely) - see `docs/global-upgrades.md`.

**2026-08-30: a block no longer costs a durability point regardless of hit size.** Flagged by the
user - Filler/Swarm chip damage was draining the same Coin-priced durability point a real Heavy hit
does. A new `AccessoryGuardConfig.MinDamageToBlock` (default 0, opt-in) makes `AccessoryGuardUtility.
TryBlock` fall through untouched for any hit below it, straight to normal Health resolution - no new
`.qtn`, no codegen dependency. See `docs/accessory-guard.md`'s own "2026-08-30" section.

Short version: the code compiles once codegen picks up the new `Accessory/AccessoryGuard.qtn` and
`Events.qtn`'s 5 new events; `AccessoryGuardSystem` is registered inside `GameplaySystemGroup`
alongside the other pickup systems and `BuyAccessoryServiceCommand` in `CommandSetup.User.cs`.
`Tools > RiftRaiders > Generate Accessory Guard Content` authors `AccessoryGuardConfig.asset` - not
yet run. Nothing is authored: `RuntimeConfig.AccessoryGuardConfig` and
`RuntimeConfig.Prefabs.DroppedAccessoryPrototype` are both unassigned (the first one being unassigned
disables the mechanic entirely - nothing is seeded, so nothing blocks), no `DroppedAccessory`
prototype exists, no hero's `CharacterData.Accessory` block is filled in (`MaxGeometricHat.png` is
already imported for Max), no hero view prefab has an `AccessoryView` (needs its `equippedVisual`/`unequippedVisual` pair assigned), and no hero prefab has `MovementRingView.accessoryArrowSprite` assigned (unauthored the radar arrow simply shows nothing - see `docs/accessory-guard.md`'s "Radar" section). The Store's
food/utility row is packed dynamically from `StoreConfig.OfferWeaponLevelUp`/`OfferAccessoryService`
(two hardcoded card-index constants were deleted - that fragility is what let the accessory card sit
past `cardCount` and silently never render), defaulting to `[food, food, accessory service]` which
fits the stock `ChooseWindow.cardCount` of 3; re-enabling Increase Weapon Level needs cardCount 4.
Not yet manually verified in-Editor.

## Local-testing Bots

`RuntimePlayer.IsBot` turns a player slot into a bot: a real Quantum player whose `Input` is
synthesized by the simulation (`BotInputSystem` -> `BotBrain.Data`) instead of polled from a device,
so one person can run a full co-op party locally and actually watch a hero's kit fire. `IsBot` is
read in exactly ONE place - `PlayerSpawnUtility.Spawn`, which turns it into a `BotBrain` component -
and every per-tick path keys off the component instead. The single integration point is a new
`PlayerInputUtility.Resolve(f, entity, playerLink)` returning `Input*` (bot's `BotBrain.Data` or
`f.GetPlayerInput`); the five consumers (`PlayerMovementProcessor`/`SkillSystem`/`AutoJumpSystem`/
`ReviveChannelSystem`/`WeaponSystem`) each swapped one line and none of them knows a bot exists.
Same "fake a player's Input on an entity" shape `InputSource.qtn` already established for Lux's
sentry gun, deliberately a SECOND component since a bot really does have a `PlayerLink` and
`WeaponSystem.HasFireDriver` reads "has `InputSource`" as "is a non-player shooter". The AI itself is
minimal on purpose: follow the lowest-`PlayerRef` human, `SteerAroundWalls` deflection plus ledge/
void avoidance (no pathfinding), a leash teleport as the stuck-recovery, and Dash/Hero Skill pulsed
on randomized `[min,max]` countdowns gated on an enemy being nearby. Void avoidance is not optional
polish: the player AUTO-HOP reacts to "no ground ahead" by JUMPING, so a bot walking at a pit gets
launched into it - `BotInputSystem.TryFindSafeDirection` therefore probes FURTHER ahead than
`MovementDataAsset.EdgeProbeDistance` and rejects the direction before auto-hop can fire, deflecting
+/-45/90 degrees to walk along a chasm lip, and reporting "blocked" (which itself triggers the leash,
since the target can be metres away across a gap) only when every candidate is a pit. It never writes `Input.Fire` - firing is
already auto-attack off `Aim.Target`. View-side, the ONLY structural change is
`QuantumHelper.GetLocalSlotIndex`: a bot resolves to -1 AND never consumes a local slot, so a human
sitting behind two bots in `LocalPlayers[]` is still slot 0 and every existing "is available to the
local player" call site (camera targets, `MyLocalPlayer.Slots`, every `BindToSlot(0, ...)` widget,
`GameplayUiController.choiceWindows[]`) stays bot-unaware for free - including AUDIO, which needed
no bot-specific code: `EntitySound.ResolveVolume` asks `MyLocalPlayer.IsLocalEntity`, so a bot's
sounds are mixed exactly like a networked teammate's (`quieterWhenRemote` scales them down,
`localPlayerOnly` drops them entirely), and `LocalPlayerAudioListener`/`VoiceDirector` skip them. Two "don't make the human wait"
gates were added, both opt-out via `RuntimeConfig.Bots`: a bot random-picks its own level-up option
the tick the screen opens (`LevelUpUtility.AutoConfirm`, the same draw the 30s timeout would have
made) and auto-votes to skip every Breathing Break. Full design, file map, the local-slot reasoning,
and current status: **`docs/bots.md`**. Read it before touching anything bot/player-input-resolution/
local-slot-index related.

Short version: the code compiles once codegen picks up the new `BotBrain.qtn`, and `BotInputSystem`
is registered in `SystemSetup.User.cs` inside `GameplaySystemGroup` right before `KCCSystem`. There
is NO asset authoring at all - all tuning lives on `RuntimeConfig.Bots` (under the existing
`[Header("Debug")]`, every `FP` treating 0 as "use the built-in default"). Only scene work: on
`QuantumRunnerLocalDebug.LocalPlayers[]`, add entries, tick **Is Bot**, and give each bot its own
`PlayerAvatar`. Not yet manually verified in-Editor.

## Loading / Generating Level Screen

A full-screen screen covering the whole match start - from the moment a run is actually starting until
the local hero is standing in the world - then fading and handing off to `InMatchWindow`. **Reworked
2026-08-26 from a `QuantumGameScene`-side widget into a menu-side `UiWindow`.** The original
`LoadingScreenWidget` lived in the gameplay scene on its own Canvas; that was the wrong home on three
counts: `SessionRunner.StartAsync` is what additively LOADS `QuantumGameScene`, so a screen living
there doesn't exist for the first and slowest part of the start; it has to out-sort the gameplay HUD
Canvas it lives beside; and the menu's own `WindowManager` was already the thing driving
`MainMenuWindow -> ConnectingWindow -> InMatchWindow`, so the loading screen belonged IN that chain
rather than beside it. It's now `LoadingWindow` (`Assets/_Project/Scripts/UI/Menu/`), a real `UiWindow`
under `MainMenuTab`'s `WindowManager` - the chain is
`MainMenuWindow -> ConnectingWindow -> LoadingWindow -> InMatchWindow`, one continuous overlay with no
gap where an unfinished level is visible. `ShowWindow<T>()` hiding every other window is not the
hazard here it is for Cursed Rift's screen: during a match start nothing else should show, and being
pre-empted by the failure path's `MainMenuWindow`/`OnDisconnected`'s `AlertPopup` is correct. The one
thing kept from the old design is its **own nested Canvas with Override Sorting, `sortingOrder` 999**
- menu Canvases sort at 0 and the gameplay HUD at 11, so without it the HUD of a match that hasn't
visually started draws over the screen hiding it (no `CanvasScaler` - a nested Canvas inherits its
parent's, which is what keeps it on the same reference resolution as every other menu window).

The actual bug this fixed: `StartRunner` called `ShowWindow<InMatchWindow>()` the moment `AddPlayer`
returned, and `InMatchWindow.Show` disables the whole menu Canvas - i.e. that call is what REVEALS the
gameplay scene, at a point where the level isn't generated and no hero has spawned. Two lines changed
there: the top's `ShowWindow<ConnectingWindow>()` became `ShowWindow<LoadingWindow>()` (so the screen
also covers the scene load itself; `ConnectingWindow` still owns the connect/room phase from
`MainMenuWindow`, and the callbacks it stopped registering during the start window only raised alerts
`OnDisconnected`/`StartRunner`'s own `catch` already raise), and the post-`AddPlayer`
`ShowWindow<InMatchWindow>()` was deleted - that transition is `LoadingWindow`'s own call now, made
only once `MyLocalPlayer.AnyLocalPlayerSetup` is true (hero exists AND its view registered), fading
ITSELF out first and showing `InMatchWindow` on complete, since that window disabling the menu Canvas
would otherwise cut the fade off mid-way. The menu background behind it fades along with it
(`fadeWithScreen`, an array of objects outside the window's own hierarchy - one tween value drives the
window's `CanvasGroup` and every entry, and a `CanvasGroup` is auto-added to anything lacking one), or
the fade would reveal the MAIN MENU rather than the game; the hand-off's own `Hide` deliberately
leaves those at alpha 0 while every other `Hide` restores them to 1, gated on having actually faded
since `ShowWindow` calls `Hide` on every non-shown window. **Zero simulation change** - no `.qtn` edit, no system, no
codegen dependency: the bar reads `Global.LevelGenCursor`/`LevelGenTotal`, which `Chunk.qtn`'s own
comment already named a "Generating level..." screen as the intended consumer of. Three bands
(Connecting 0-0.15, Generating 0.15-0.85 - the only one with countable work - Entering 0.85-1),
monotonic by construction, one `LogHelper` line per stage change so a genuine hang is diagnosable from
the log; minimum display duration, and a 45s failsafe that hands off anyway naming the stage it was
stuck in. Full design and the authoring checklist: **`docs/loading-screen.md`**.

Short version: the code compiles against existing types only, but nothing is in the menu scene yet -
run `Tools > RiftRaiders > Create Loading Window` with `MenuScene` open (it parents the window under
`MainMenuTab.windowManager`, which registers it for free via `GetComponentsInChildren`, and builds +
wires the override-sorting Canvas, backdrop, title, stage label, progress bar, percent and tip line)
and save. `LoadingScreenWidget.cs`/`LoadingScreenBuilder.cs` are deleted; the old builder had been run
with `gamesceneBackup.unity` open rather than `QuantumGameScene`, which is why the previous screen
never appeared in a real match - that backup scene now holds a missing-script `LoadingScreen`
GameObject, harmless but worth deleting if it's ever opened. Not yet verified in-Editor.

## Quantum `.qtn` codegen gotcha

Any time a `.qtn` file changes, Quantum's DSL codegen must run before C# referencing the new component/global fields will compile. The open Editor does this automatically. If you ever need to do this headlessly (batch mode/CI), see the "Quantum codegen gotcha" section in `docs/survival-director.md` - there's a chicken-and-egg trap if new C# and new `.qtn`-derived types land in the same pass, and a real risk in running a second headless Unity instance against a project that already has a live Editor open (check for `Temp/UnityLockfile` / a running `Unity` process first).
