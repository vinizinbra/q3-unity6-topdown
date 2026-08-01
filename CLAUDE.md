# Project notes

Unity + Photon Quantum (deterministic ECS), 2D co-op top-down roguelite shooter.

## Survival Director

A continuous-spawn combat pacing system (Survival Progression / Combat Director / Enemy Lifecycle) was added under `Assets/_QuantumUser/Simulation/{Assets,Systems,QTN}/Director/` plus a new `EnemyLifecycle` component and `EnemyDataAsset.Cost`/`Persistent` fields. Full design, file map, current status, and known simplifications: **`docs/survival-director.md`**. Read it before touching anything Director-related.

Short version: the code compiles and is registered in `SystemSetup.User.cs`, but no `SurvivalConfig`/`EnemyGroupConfig`/`DirectorConfig`/`LifecycleConfig` asset instances exist yet - those need to be authored in the Editor and assigned to `RuntimeConfig` before the Director does anything at runtime.

## Experience Drops

Enemies drop a physical `ExpOrb` pickup on death (skipped for environment/hazard kills), crediting one shared run-wide exp total + level (co-op - not tracked per-player) via a new `ExpOrb` component, `Experience.qtn` global fields, `ExperienceConfig`/`ExperienceUtility`/`ExpOrbSystem`, and a per-`EnemyTier` `ExpValue` baseline on `EnemyTierStatsConfig`. Full design, file map, current status, and known simplifications: **`docs/experience-drops.md`**. Read it before touching anything experience/leveling-related.

Short version: the code compiles and `ExpOrbSystem` is registered, but no `ExperienceConfig` asset instance or `ExpOrb` prototype prefab exist yet - those need to be authored in the Editor and assigned to `RuntimeConfig` before any exp actually drops or can be collected.

## Level-Up Upgrades

On a level-up, the simulation now pauses (a new `GameplaySystemGroup` wrapping the per-tick gameplay systems in `SystemSetup.User.cs`, toggled via Quantum's built-in `SystemDisable`/`SystemEnable`) and opens an upgrade-choice screen: every connected player rolls 3 options from `LevelUpPoolKind`'s pools (Weapon Perk / Global Upgrade / Rift Mutation are pooled globally via `LevelUpConfig`; Skill Upgrade / Passive Upgrade - together nicknamed "Hero Ascensions" - are per-hero, living directly on `CharacterData`) and picks one, via `SelectLevelUpUpgradeCommand`, before a 30s timer auto-picks randomly for anyone who hasn't. `WeaponPerkData`/`SkillActionData`/`GlobalUpgradeData`/`PassiveUpgradeData`/`RiftMutationData` all derive from a shared `UpgradeData` base (`Icon`/`DisplayName`/`Rarity`/`GetDescription()`), so `LevelUpOption` carries one `AssetRef<UpgradeData>` instead of a field per kind, every candidate is weighted the same way by `Rarity`, and the UI (`UpgradeCardWidget`) renders any of them with no switch statement. Full design, file map, current status, and known simplifications: **`docs/level-up-upgrades.md`**. Read it before touching anything level-up/pause/upgrade-choice related.

Short version: `LevelUpConfig.asset` is authored and assigned to `RuntimeConfig`, `GlobalUpgrades`/`WeaponPerkPool`/per-hero `DashSkillUpgrades`/`PassiveUpgrades` are all populated, and `UpgradeWindow` is wired into `QuantumGameScene` via `GameplayUiController.upgradeWindows[]` - a level-up now actually pauses and shows a screen. `CharacterData.HeroSkillUpgrades` no longer exists - the Hero Skill slice of the Skill Upgrade pool is pulled straight from `HeroSkill`'s own `Actions` list instead (any `SkillActionData` authored there with `Activated == false` is a candidate; granting it via `AddUpgrade` ignores `Activated` for that player only - see `SkillSystem.InvokeActions`' `isUpgrade` bypass). Remaining gap: the full end-to-end flow hasn't been manually verified in-Editor yet. See `docs/level-up-upgrades.md` for details.

The `GlobalUpgrade` pool itself (22 upgrades, small permanent stat increments that stack
indefinitely) has its own design catalog: **`docs/global-upgrades.md`**. That doc's "Economy"
section also covers **Coin**, a second independent currency from Rift Shards
(`Coin.qtn`/`Coins.qtn`/`CoinConfig`/`CoinUtility`/`CoinOrbSystem`) - both currencies now share a
per-`EnemyTier` drop-chance gate (`EnemyTierStatsConfig.TierStats`'
`RiftShardDropChance`/`CoinDropChance`, rolled via `DamageUtility.RollChance` before a kill actually
drops one) and a scattered spawn position (`Min`/`MaxSpawnOffset`, same pattern `ScrapConfig`
already used) so multiple drops off one kill don't stack exactly on top of each other.

## Rift Mutations

A fourth level-up pool alongside Global Upgrade/Weapon Perk/Hero Ascension - `LevelUpPoolKind.
RiftMutation`, its own `RiftMutationData`/`RiftMutationUtility`/`RiftMutationPicks` hierarchy (see
`Assets/_QuantumUser/Simulation/Assets/RiftMutation/`), and its own `LevelUpConfig.RiftMutations`
list - for **rare, non-stackable, run-wide** effects: a one-shot build-defining tradeoff (Glass
Core, Heavy Arsenal), a new reactive rule (Shield Breaker, Critical Focus), or both (Infinite
Momentum). "Non-stackable" is enforced pool-wide (`RiftMutationPicks`), unlike Global Upgrade's
opt-in per-asset `MaxPicks`. New `RiftMutationReactionSystem` reacts to crit/dash-activation/
shield-break signals for the mutations that need more than a one-shot `CharacterStats` bake. Greed
introduced a new **Rift Shard** currency system (`RiftShard.qtn`/`RiftShards.qtn`/
`RiftShardConfig`/`RiftShardUtility`/`RiftShardOrbSystem`), mirroring `ExpOrb`'s drop-and-collect
pattern. Full design, the complete 14-mutation roster, and current status: **`docs/rift-mutations.md`**.
Read it before touching anything Rift-Mutation-related.

Short version: the code compiles and is registered in `SystemSetup.User.cs`, but `Tools/RiftRaiders/
Generate Rift Mutation Assets` hasn't been run yet (no `.asset` instances exist), and (same gap
`ExpOrb` itself once had) no `RiftShardOrb` prototype prefab exists yet and
`RuntimeConfig.RiftShardConfig`/`RiftShardPrototype` aren't assigned, so Greed's currency half won't
drop or credit anything at runtime until that's authored in the Editor.

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

## Quantum `.qtn` codegen gotcha

Any time a `.qtn` file changes, Quantum's DSL codegen must run before C# referencing the new component/global fields will compile. The open Editor does this automatically. If you ever need to do this headlessly (batch mode/CI), see the "Quantum codegen gotcha" section in `docs/survival-director.md` - there's a chicken-and-egg trap if new C# and new `.qtn`-derived types land in the same pass, and a real risk in running a second headless Unity instance against a project that already has a live Editor open (check for `Temp/UnityLockfile` / a running `Unity` process first).
