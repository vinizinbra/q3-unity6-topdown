# Project notes

Unity + Photon Quantum (deterministic ECS), 2D co-op top-down roguelite shooter.

## Survival Director

A continuous-spawn combat pacing system (Survival Progression / Combat Director / Enemy Lifecycle) was added under `Assets/_QuantumUser/Simulation/{Assets,Systems,QTN}/Director/` plus a new `EnemyLifecycle` component and `EnemyDataAsset.Cost`/`Persistent` fields. Full design, file map, current status, and known simplifications: **`docs/survival-director.md`**. Read it before touching anything Director-related.

Short version: the code compiles and is registered in `SystemSetup.User.cs`, but no `SurvivalConfig`/`EnemyGroupConfig`/`DirectorConfig`/`LifecycleConfig` asset instances exist yet - those need to be authored in the Editor and assigned to `RuntimeConfig` before the Director does anything at runtime.

## Experience Drops

Enemies drop a physical `ExpOrb` pickup on death (skipped for environment/hazard kills), crediting one shared run-wide exp total + level (co-op - not tracked per-player) via a new `ExpOrb` component, `Experience.qtn` global fields, `ExperienceConfig`/`ExperienceUtility`/`ExpOrbSystem`, and a per-`EnemyTier` `ExpValue` baseline on `EnemyTierStatsConfig`. Full design, file map, current status, and known simplifications: **`docs/experience-drops.md`**. Read it before touching anything experience/leveling-related.

Short version: the code compiles and `ExpOrbSystem` is registered, but no `ExperienceConfig` asset instance or `ExpOrb` prototype prefab exist yet - those need to be authored in the Editor and assigned to `RuntimeConfig` before any exp actually drops or can be collected.

## Level-Up Upgrades

On a level-up, the simulation now pauses (a new `GameplaySystemGroup` wrapping the per-tick gameplay systems in `SystemSetup.User.cs`, toggled via Quantum's built-in `SystemDisable`/`SystemEnable`) and opens an upgrade-choice screen: every connected player rolls 3 options from `LevelUpPoolKind`'s four pools (Weapon Perk / Global Upgrade are pooled globally via `LevelUpConfig`; Skill Upgrade / Passive Upgrade are per-hero, living directly on `CharacterData`) and picks one, via `SelectLevelUpUpgradeCommand`, before a 30s timer auto-picks randomly for anyone who hasn't. `WeaponPerkData`/`SkillActionData`/`GlobalUpgradeData`/`PassiveUpgradeData` all derive from a shared `UpgradeData` base (`Icon`/`DisplayName`/`Rarity`/`GetDescription()`), so `LevelUpOption` carries one `AssetRef<UpgradeData>` instead of a field per kind, every candidate is weighted the same way by `Rarity`, and the UI (`UpgradeCardWidget`) renders any of them with no switch statement. Full design, file map, current status, and known simplifications: **`docs/level-up-upgrades.md`**. Read it before touching anything level-up/pause/upgrade-choice related.

Short version: the code compiles and `LevelUpSystem` is registered, but no `LevelUpConfig` asset instance exists yet, none of the four upgrade pools have any entries authored, and no `UpgradeWindow` is wired into the scene - a level-up still happens, it just never pauses anything or shows a screen until that authoring is done.

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
