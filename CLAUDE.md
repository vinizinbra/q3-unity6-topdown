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
time (`WeaponSystem.Equip`/`AddPerk`), never re-applied per tick. Full design, the current 5-perk
roster vs. the larger ~35-perk target list under discussion, and a feasibility breakdown of what
each tier needs to actually get built: **`docs/weapon-perks.md`**. Read it before adding or
authoring anything perk-related.

Short version: only 5 flat-multiplier perk classes exist (damage, fire rate, magazine size, reload
cooldown, crit chance), and no `WeaponPerkPoolData` asset instance exists yet either, so no perk -
including those 5 - can currently drop or be offered at level-up. Most of the larger designed roster
(on-kill procs, magazine-position effects, pierce/ricochet/split-style post-impact behavior, ramping
buffs) needs new simulation hooks that don't exist yet - see the doc for which ones are cheap
extensions of the existing pattern vs. real new systems.

## Quantum `.qtn` codegen gotcha

Any time a `.qtn` file changes, Quantum's DSL codegen must run before C# referencing the new component/global fields will compile. The open Editor does this automatically. If you ever need to do this headlessly (batch mode/CI), see the "Quantum codegen gotcha" section in `docs/survival-director.md` - there's a chicken-and-egg trap if new C# and new `.qtn`-derived types land in the same pass, and a real risk in running a second headless Unity instance against a project that already has a live Editor open (check for `Temp/UnityLockfile` / a running `Unity` process first).
