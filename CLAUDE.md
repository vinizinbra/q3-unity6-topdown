# Project notes

Unity + Photon Quantum (deterministic ECS), 2D co-op top-down roguelite shooter.

## Survival Director

A continuous-spawn combat pacing system (Survival Progression / Combat Director / Enemy Lifecycle) was added under `Assets/_QuantumUser/Simulation/{Assets,Systems,QTN}/Director/` plus a new `EnemyLifecycle` component and `EnemyDataAsset.Cost`/`Persistent` fields. `CombatDirectorUtility.ResolveBudgetMultiplier` now also scales `DirectorBudget` accumulation by live player count/run time via `BalanceConfig` - see "Run Curves & Co-op Scaling" below. Full design, file map, current status, and known simplifications: **`docs/survival-director.md`**. Read it before touching anything Director-related.

Short version: the code compiles and is registered in `SystemSetup.User.cs`, and as of 2026-08-07 `SurvivalConfig`/`EnemyGroupConfig`/`DirectorConfig`/`LifecycleConfig`/`EnemySpawnProfile` asset instances all exist, are authored, and are assigned to `RuntimeConfig` - the Director should actually spawn at runtime. `LifecycleConfig.RelevantRange` was found less than `DirectorConfig.SpawnRingRadiusMax` (the exact case the `ValidateOnce` guardrail warns about) and has been fixed - see `docs/survival-director.md`'s authoring checklist item 6. A first playable content pass was also authored the same day: all 11 `BaseEnemies`' action `Damage` values were rebalanced (one, `HeavySlammer`, was doing literal 0 damage), and `Tools > RiftRaiders > Generate Survival Director Content` (a new Editor generator script, not yet run) will author 10 `EnemyGroupConfig` encounters plus a full 6-phase `SurvivalConfig` timeline tuned for a ~15 minute run - see `docs/survival-director.md`'s "First playable content pass" section.

## Run Curves & Co-op Scaling

A consolidated `BalanceConfig` asset (`Assets/_QuantumUser/Simulation/Balance/`) holds time-based "run curves" (one `RunCurveAnchor` row per anchor minute over a 12-minute run, `CurveChannel`) and player-count "co-op scaling" (flat P1-P4 lookups, `CoopGlobalKey` + a per-`EnemyTier` `CoopHpRow` table), with three consumers: `EnemyBalanceUtility.ResolveEnemyStats` combines the `EnemyHp`/`EnemyDmg` curves + `CoopHp`/`EnemyDamage` co-op rows with the pre-existing per-Tier HP baseline (`EnemyTierStatsConfig.MaxHealth`, **not** duplicated here) into a once-per-spawn `EnemyRuntimeStats` (HP + a generic damage multiplier) - baked into `Health.MaxHealth` and a new `EnemyCombatModifiers.DamageMultiplier` component from `EnemySystem.SeedFromEnemyData`, never re-evaluated after spawn, and actually applied to every hit an enemy lands via `HitEffectUtility.ScaleByEnemyDamageMultiplier` (the single funnel every enemy delivery type - melee/area/beam/projectile alike - ultimately calls). `CombatDirectorUtility.ResolveBudgetMultiplier` combines the `DirectorBudget` curve + co-op row into a multiplier applied to `phase.BudgetPerPulse` every Director pulse (Survival Director's own "Milestone 7", see `docs/survival-director.md`) - recomputed every pulse, not a one-time snapshot. `ExperienceUtility.ResolveXpRequirementMultiplier` applies the `XpRequirement` co-op row (no paired curve - `ExperienceConfig.RequiredExperience` already has its own per-level curve) to the level-up threshold in `ExperienceUtility.Grant`. `ExpectedPlayerDps`/`EliteFrequency` remain unconsumed. Full design, the exact curve/co-op numbers, and current status: **`docs/run-curves-coop-scaling.md`**. Read it before touching anything run-curve/co-op-scaling/enemy-HP-baseline/DirectorBudget-scaling/XP-requirement-scaling related.

Short version: the code compiles, and as of 2026-08-07 `BalanceConfig.asset` exists and is assigned to `RuntimeConfig.BalanceConfig` in both scenes, and `EnemyCombatModifiers` has been added to the shared generic enemy prototype (`GenericEnemyPrefab.prefab`) - all three consumers (`ResolveEnemyStats`, `ResolveBudgetMultiplier`, `ResolveXpRequirementMultiplier`) are live, and `HitEffectUtility.ScaleByEnemyDamageMultiplier` actually scales enemy damage now.

## Experience Drops

Enemies drop a physical `ExpOrb` pickup on death (skipped for environment/hazard kills), crediting one shared run-wide exp total + level (co-op - not tracked per-player) via a new `ExpOrb` component, `Experience.qtn` global fields, `ExperienceConfig`/`ExperienceUtility`/`ExpOrbSystem`, and a per-`EnemyTier` `ExpValue` baseline on `EnemyTierStatsConfig`. The per-level `RequiredExperience` threshold `ExperienceUtility.Grant` checks is additionally scaled by live player count via `BalanceConfig.CoopGlobalKey.XpRequirement` (`ExperienceUtility.ResolveXpRequirementMultiplier`) - see "Run Curves & Co-op Scaling" below. Full design, file map, current status, and known simplifications: **`docs/experience-drops.md`**. Read it before touching anything experience/leveling-related.

Short version: the code compiles and `ExpOrbSystem` is registered, but no `ExperienceConfig` asset instance or `ExpOrb` prototype prefab exist yet - those need to be authored in the Editor and assigned to `RuntimeConfig` before any exp actually drops or can be collected.

## Level-Up Upgrades

On a level-up, the simulation now pauses (a new `GameplaySystemGroup` wrapping the per-tick gameplay systems in `SystemSetup.User.cs`, toggled via Quantum's built-in `SystemDisable`/`SystemEnable`) and opens an upgrade-choice screen: every connected player rolls 3 options from `LevelUpPoolKind`'s pools (Weapon Perk / Global Upgrade / Rift Mutation are pooled globally via `LevelUpConfig`; Skill Upgrade / Passive Upgrade - together nicknamed "Hero Ascensions" - are per-hero, living directly on `CharacterData`) and picks one, via `SelectLevelUpUpgradeCommand`, before a 30s timer auto-picks randomly for anyone who hasn't. `WeaponPerkData`/`SkillActionData`/`GlobalUpgradeData`/`PassiveUpgradeData`/`RiftMutationData` all derive from a shared `UpgradeData` base (`Icon`/`DisplayName`/`Rarity`/`GetDescription()`), so `LevelUpOption` carries one `AssetRef<UpgradeData>` instead of a field per kind, every candidate is weighted the same way by `Rarity`, and the UI (`UpgradeCardWidget`) renders any of them with no switch statement. Full design, file map, current status, and known simplifications: **`docs/level-up-upgrades.md`**. Read it before touching anything level-up/pause/upgrade-choice related.

Short version: `LevelUpConfig.asset` is authored and assigned to `RuntimeConfig`, `GlobalUpgrades`/`WeaponPerkPool`/per-hero `DashSkillUpgrades`/`PassiveUpgrades` are all populated, and `UpgradeWindow` is wired into `QuantumGameScene` via `GameplayUiController.upgradeWindows[]` - a level-up now actually pauses and shows a screen. `CharacterData.HeroSkillUpgrades` no longer exists - the Hero Skill slice of the Skill Upgrade pool is pulled straight from `HeroSkill`'s own `Actions` list instead (any `SkillActionData` authored there with `Activated == false` is a candidate; granting it via `AddUpgrade` ignores `Activated` for that player only - see `SkillSystem.InvokeActions`' `isUpgrade` bypass). Remaining gap: the full end-to-end flow hasn't been manually verified in-Editor yet. See `docs/level-up-upgrades.md` for details.

A given level-up can now be locked to exactly ONE of 5 player-facing categories (`LevelUpCategory`: HeroSkill merges SkillUpgrade+PassiveUpgrade, GlobalUpgrade, RiftMutation, WeaponPerk, and a new **Choose Weapon**) via `LevelUpConfig.LevelSequence`, a repeating per-level list - an empty list (the default) keeps the original mixed-all-pools roll unchanged. Choose Weapon rolls 3 distinct weapons from a new `WeaponChoicePoolData`, each with an independently-rolled perk count driven by a new persistent per-player `CharacterStats.WeaponTalentLevel` (increments on every Choose-Weapon pick) via `LevelUpConfig.ChancePerLevelPerSlot`/`MaxRolledPerks`, rendered by a dedicated `WeaponCardWidget` (not `UpgradeCardWidget`). As of 2026-08-07, a player can also decline every rolled weapon via a separate **"Keep Current"** button (`UpgradeWindow.keepCurrentButton`, shown only on a Choose-Weapon screen) - deliberately NOT a 4th/replacement card, all 3 `Options` stay real rolled weapons. Sends a new zero-payload `KeepCurrentWeaponCommand`; `LevelUpUtility.ConfirmKeepCurrent` sets a new `LevelUpChoice.KeptCurrent` flag (not on `LevelUpOption`, since it isn't tied to any rolled slot) that `Resolve` checks before calling `GrantOption` at all - see `docs/level-up-upgrades.md`'s "Category sequencing / Choose Weapon" section. A new `Chest` entity/`ChestSystem` reuses this whole pipeline, forced to one category set once in the Editor per chest instead of per level. Also as of 2026-08-07: a **Reroll** mechanic lets a player redraw their own current `LevelUpChoice.Options` in place, spending one charge from a new persistent per-character `CharacterStats.RerollQuantity` via a new zero-payload `RerollLevelUpOptionsCommand` - see `docs/level-up-upgrades.md`'s "Reroll" section. **Not a Global Upgrade** - sourced as a pre-run meta-progression talent, same shape as `WeaponTalentLevel`: `RuntimePlayer.Talents.RerollQuantity` (its own `PlayerPrefInt`, `"reroll_quantity"`, in `MatchMakingConfig`) seeds it once at spawn - as of 2026-08-07 this and every other meta-progression field (including `WeaponLevel`) live on one nested `RuntimePlayer.Talents : PlayerTalents` struct rather than flat on `RuntimePlayer` itself (see `docs/talents.md`). Code compiles but needs Editor work before it's playable: assign `UpgradeWindow`'s new `rerollButton`/`rerollChargesText` fields on the scene prefab (no reroll UI exists there yet), and nothing yet *writes* to the new PlayerPref (same pre-existing gap `WeaponTalentLevelPref` has) so every player starts at 0 charges until a settings/meta-progression screen sets it. Full design and current status: still **`docs/level-up-upgrades.md`** for the category/Choose-Weapon/Reroll half, and **`docs/chests.md`** for the Chest entity itself. Read both before touching anything category-sequencing/Choose-Weapon/Reroll/Chest related.

Short version: the code compiles, but `LevelUpConfig.LevelSequence`/`WeaponChoicePool` ship empty/unassigned (so nothing changes at runtime until authored), every `WeaponDataAsset`'s new `Icon`/`DisplayName` are unset, and no `Chest` `EntityPrototype`, `WeaponCardWidget`/`WeaponCardPerkRowWidget` prefab, or `UpgradeWindow.weaponCardPrefab` wiring exist yet - see each doc's own "Current status"/"Editor authoring needed" section for the full list.

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

## Explode-On-Destroy / Mini Bomb

**Pixie's Cluster Charge (`ClusterBombUpgrade`/`ClusterBombSkillAction`) is original/untouched** - it spawns real `Projectile` bomblets via `ProjectileSpawner`, exactly as it always did. It was briefly redirected onto a new stationary "Mini Bomb" entity shape, then reverted at the user's explicit request since the `Projectile`-based version was already working and tuned. **Do not redirect Cluster Charge onto Mini Bomb again without being asked.**

What stayed: a generic **`ExplodeOnDestroy`** component (`Damage`/`SpawnDepth`/`AssetRef<AreaHitData> Explosion`) - a hero/feature-agnostic "detonate when this entity is destroyed" hook, checked from two independent trigger points (`DestroyAfterTimeSystem` on timed expiry, and `DamageUtility.ApplyDamage`'s non-Enemy death branch when `Health` reaches 0), both a plain optional check bolted onto neither system specifically, so any future "explodes when destroyed" feature reuses it for free. The damage-death trigger is what lets it also work as a **decoy trap**: seed `Health` to 1, add the existing `Decoy` tag (draws enemy aggro, zero new code), and a real collider *on the Player physics layer* (see `Decoy.qtn`'s own comment), and it detonates the instant an enemy kills it. `Explosion` (`AreaHitData`) is reused purely as data, so it fires the same generic `HitEffectUtility.ApplyInRadius`/`AreaDetonated` explosion every other blast in this codebase uses. `DashBomb.prefab` is a working reference prototype kept in place even though no live Ascension currently spawns it (see "Pixie — Ascensions" below); Pixie's **Pocket Bombs** Ascension is the other live user, dropping a Mini Bomb this same way. Full design, file map, current status, and known simplifications: **`docs/explode-on-destroy.md`**. Read it before touching anything Cluster Charge/Mini Bomb/`ExplodeOnDestroy` related.

Short version: `DashBomb.prefab` already has an `ExplodeOnDestroy` configured by hand in the Editor from before a mid-stream rename (`ExplodeOnExpire` → `ExplodeOnDestroy`) - its generated component reference needs re-adding once codegen regenerates (see docs/explode-on-destroy.md for the exact values to re-enter: `Damage = 10`, same `Explosion` asset).

## Pixie — Ascensions

Pixie's Hero Ascension pool was consolidated (2026-08-09) from ~13 overlapping single-pick passives/skill upgrades down to **exactly 9 three-rank Ascension lines** (27 total rank-acquisitions): Cluster Bomb, Direct Hit (absorbed Concussive Force), Birthday Cake (now taunts after *landing*, not during flight), Pocket Bombs (renamed from Mini Ordnance), Unstable Mixture (absorbed Bigger Boom + Heavy Payload), Unstable Targeting, Explosive Rounds, Backblast (absorbed Volatile Escape, and later reworked from an instant blast into dropping a fused bomb - same `ExplodeOnDestroy` shape Pocket Bombs uses, so it's a full qualifying Pixie explosion), and a new second Dash path, **Hot Fuse** (dash empowers the *next* Bunny Bomb throw rather than exploding herself). Volatile Payload, the always-on baseline Bunny Bomb behaviors (Bomb Radius Up/Instant Detonate/Fireworks), the `isDashExplosion` parameter chain it left behind (once Backblast stopped needing it), and a real authoring bug - `PixieBaseSkill.asset` had a dangling GUID resolving to **Max's** `MarkExplosiveDeathSkillAction`, silently marking every enemy Pixie hit with anything, ungated - were all removed. Volatile Escape's guaranteed-marking role now lives on a new generic, hero-agnostic tag, **`ForceMarkOnDetonate`**, granted onto a specific spawned bomb entity (not its owner) and read by `ExplodeOnDestroyUtility.TryDetonate` - reusable by any future hero's "this dropped bomb always marks what it hits" ascension. This is also the first hero to exercise a new **generic multi-rank Ascension mechanism** (`MaxRank`/`IRankedUpgrade` on both `PassiveUpgradeData` and `SkillActionData`, rank tracked via the pre-existing `UpgradeHistory.Count` field - see `docs/level-up-upgrades.md`'s own "Ranked Ascensions" section), built generic and intended for every other hero's Ascension pool (Kai/Brute/Max) to reuse rather than rediscover. Full design, the per-line breakdown, and current status: **`docs/pixie-ascensions.md`** (replaces the old `docs/pixie-demolition-mastery.md`). Read it - and `docs/level-up-upgrades.md`'s ranking section - before touching anything Pixie-Ascension/explosion-reaction/rank-mechanism related.

Short version: the code compiles once codegen picks up every changed/new/removed `.qtn` file (several components were renamed/added/removed as part of the consolidation - see docs/pixie-ascensions.md's own "Current status"); `Tools/RiftRaiders/Pixie/Generate Ascension Assets` (replaces the old two-generator Chain-Reaction/Demolition-Mastery pair) authors and wires all 9 lines, **fully replacing** every list it touches (`PassiveUpgrades`, `PixieBaseSkill.Actions`, `DashSkillUpgrades`) rather than appending - deliberately, since an append/replace split between two generators is exactly what let the old pool drift out of sync with what was actually live. Pocket Bombs' `MiniBombPrototype`/`Explosion` still need hand-authoring in the Editor. Not yet manually verified end-to-end in-Editor.

## Brute — Ascensions

Brute's Hero Ascension pool - previously fragmented across a 4-trait Protector Aura pool, a 4-trait "Knockback Mastery" pool, and 8 baseline Juggernaut sub-actions that turned out to be **permanently dead code** (`BruteBaseSkill-Juggernaut.asset` had `CheckActions: 0`, so none of them ever executed regardless of their own `Activated` flag - Discharge was knockback-only, no landing damage/stun, no end-explosion, no stacking, before this refactor) - was consolidated into exactly 8 three-rank Ascension lines (4 Juggernaut/2 Protector/2 Dash), reusing the same generic rank architecture Pixie's own refactor built (`IRankedUpgrade`/`MaxRank`/`UpgradeHistoryUtility` - see "Level-Up Upgrades" above), zero Brute-specific rank code. The 4 Juggernaut lines (Momentum/Bone Breaker/Aftershock/Concussive Impact) are ranked `SkillActionData` living on `JuggernautSkillData.Actions` (`Activated = false`, same "Hero Skill Ascension" shape Pixie's `ClusterBombSkillAction`/`BirthdayCakeSkillAction` already use) - originally built as `PassiveUpgradeData` instead, but that made them show up labeled as a generic "Passive Upgrade" in the level-up UI/debug menu, indistinguishable from genuinely hero-wide passives like Iron Presence/Guardian; converting them fixed the label to "Hero Skill" everywhere with zero UI changes. `JuggernautSkillData`'s own hardcoded `Tick`/`Discharge`/`End` logic reads the components they set via plain optional `TryGetPointer` checks either way, agnostic of grant mechanism - sidestepping the dead `Actions`/`CheckActions` mechanism entirely rather than fixing it (a *picked* Ascension executes via `SkillSlot.Upgrades`, which bypasses `CheckActions` regardless). A new baseline `JuggernautSkillData.Damage` ("Juggernaut Skill Damage") is the shared percentage basis every line references. The 2 Protector lines (Iron Presence, absorbing the old standalone Fearless; Guardian, absorbing the old standalone Bulwark plus a new rank-3 reactive-DR proc reacting to `Combat.qtn`'s `OnHealthDamageApplied`/`OnShieldDamageApplied` via a new `BruteProtectorReactionSystem`) mutate the existing `ProtectorAura` component, which gained `BaseRadius` (an immutable spawn-time anchor so Guardian's ranked radius bonus can always compute a correct total) and `HasReactiveDamageReduction`. A new generic `StatusEffects.TemporaryDamageReductionRemaining/Amount` pair (deliberately not Guardian-named) is shared by both Guardian rank 3's reactive proc and Bodyguard rank 3's own dash-end proc, since both are occasional bonuses layered on top of Guardian's own continuous aura DR rather than a replacement for it. The 2 Dash lines (Iron Shoulder, Bodyguard) were already single-pick `SkillActionData` and just needed ranking - Iron Shoulder's rank 1 reproduces its exact pre-refactor knockback-only behavior with zero regression. Ground Pound was deleted entirely ("too disconnected from Brute's primary loop"); Crushing Blow's mechanism survives as the renamed `StunDamageBonusUpgrade`, now granted by Concussive Impact rank 3; Lasting Impact/Overwhelming Force fold into Concussive Impact's own landing-stun ranks/knockback bonus. Full design, the exact per-rank numbers, the `CheckActions` bug writeup, and current status: **`docs/brute-ascensions.md`**. Read it before touching anything Brute Ascension/Juggernaut/Protector Aura/Iron Shoulder/Bodyguard related.

Short version: the code compiles once codegen picks up the changed `.qtn` components (`JuggernautAscensions.qtn`, `ProtectorAura.qtn`, `JuggernautLaunched.qtn`, `StatusEffects.qtn`) and is registered in `SystemSetup.User.cs`. `BruteAscensionAssetGenerator.cs` (`Tools > RiftRaiders > Brute > Generate Ascension Assets`) replaces the two old generators and is pointed at each surviving asset's verified live path (the old `BruteProtectorAssetGenerator`'s own path constants had drifted out of sync with reality - see the doc's own "Asset path drift" section). It was run once under the earlier `PassiveUpgradeData` design for the 4 Juggernaut lines; after converting them to `SkillActionData` the 4 stale assets were deleted by hand and `BruteCharacterData.PassiveUpgrades` trimmed back to Iron Presence/Guardian, but the generator still needs re-running to author the 4 new Hero-Skill-Ascension assets and wire them into `BruteBaseSkill-Juggernaut.Actions`. `JuggernautSkillData.Damage` (30) is a placeholder pending a real balance pass. Not yet manually verified end-to-end in-Editor.

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

## Talents (meta-progression) + Lobby Start

Talents are small, permanent unlocks earned OUTSIDE a match - flat named fields (`PlayerDamageLevel`...`PlayerExperienceLevel`, twelve 0-5 per-player leveling stats each worth +5%/level; `HasWeaponChest`/`HasHeroChest`/`HasGlobalUpgradeChest`/`HasUnlockedRift`/`CanFindStones`/`HasEvent`, six shared/coop bools OR'd across every connected player) living on `RuntimePlayer.Talents` - as of 2026-08-07 a single nested `PlayerTalents` struct field (grouped together with `WeaponLevel`/`RerollQuantity`, see "Level-Up Upgrades" above) rather than flat on `RuntimePlayer` itself. Same "seeded once from outside the match" contract `WeaponLevel` already had, persisted via one new `PlayerPrefObject` JSON pref (`MatchMakingConfig.TalentsPref`) mirroring `WeaponTalentLevelPref`. No hand-placed boundary entity - `ChunkType.Start` was renamed to `ChunkType.LobbyStart` in place, and `LevelGenerationSystem.TryGetLobbyStartBounds` reads that chunk's own world-space footprint straight off its existing `Transform3D`/`Chunk` fields (the same way it already reads the Boss Arena's footprint back out for its own grid-origin math); `LobbyBoundarySystem` polls that footprint each tick and transitions `Global.CurrentState` from `GameState.Lobby` to `GameState.Survival` (see "Game State" below) once every connected, spawned player has physically walked outside it - `CombatDirectorSystem` (and therefore all enemy spawning/`Global.SurvivalTime` counting) only runs during `GameState.Survival`. Talent-gated spawning is a `ChunkSpawnConfig` DataAsset (`Assets/_QuantumUser/Simulation/Assets/Config/ChunkSpawnConfig.cs`), holding a `SpawnEntityWithRequirement[] Spawns` array (`AssetRef<EntityPrototype> Prototype`, `FPVector3 Offset`, `SharedTalentRequirement Requirement`, `FP Chance` per entry) - referenced via one new `AssetRef<ChunkSpawnConfig> SpawnConfig` field on `Chunk` itself (`Chunk.qtn`), typically assigned on the `LobbyStart` chunk prototype. Was originally a qtn *component* of the same shape, one instance per entity - reworked into an `AssetObject` array (same "array field on an `AssetObject`, not a component" shape `LevelConfig.ChunkPool`/`ChunkPoolEntry[]` already uses) once a single chunk needed more than one independent conditional spawn at once (e.g. Weapon+Hero+GlobalUpgrade chests together), which the old component shape couldn't do - Quantum entities can only carry one instance of a given component type. `TalentGateSystem` resolves every `Chunk` entity's own `SpawnConfig` (if assigned) exactly once (`f.Create` per satisfied/chance-rolled entry, the first entity-spawn-at-runtime pattern this codebase has used for something otherwise normally hand-placed, like a Chest). Nested/child `EntityPrototype`s were explicitly ruled out as a way to co-locate spawns with the chunk - Quantum's prefab importer only reads a prefab's root GameObject, silently ignoring nested `QuantumEntityPrototype`s. Full design, file map, current status, and known simplifications: **`docs/talents.md`**. Read it before touching anything Talents/meta-progression/LobbyStart/ChunkSpawnConfig related.

Short version: the code compiles once codegen picks up the new `Talents.qtn`/`Chunk.qtn` fields, and is registered in `SystemSetup.User.cs`, but no `TalentsConfig.asset` or `ChunkSpawnConfig.asset` exists yet (so `RuntimeConfig.TalentsConfig` and every chunk's `SpawnConfig` are unassigned) - nothing talent-gated spawns without both authored. `Assets/_QuantumUser/Entities/LevelChunk/LevelChunk.prefab` also still has the OLD component-based `SpawnEntityWithRequirement` added from before this rework - needs manual removal/replacement with a `SpawnConfig` assignment once codegen regenerates (see `docs/talents.md`'s own authoring checklist). On top of the pre-existing general "no Chest prototypes authored" gap `docs/chests.md` already tracks. Nothing currently *writes* to the new `player_talents` pref - same gap `weapon_talent_level` already had (an account/profile screen elsewhere would be what actually raises these over time). Not yet manually verified end-to-end in-Editor.

## Game State

A structured top-level match-flow state machine - `Global.CurrentState`, a `GameState` enum (`Lobby, Survival, Upgrade, Event, Boss`) - replacing the independent ad hoc `Global` booleans each phase used to gate itself with (e.g. Talents/Lobby Start's own `LobbyExited`, now removed). `GameStateUtility.SetState` is the single place the value changes and fires a new match-wide `GameStateChanged` event (`Events.qtn`); it's deliberately thin (set + fire only) - each transition's own pause behavior is owned by whichever system drives it, since "does this also pause `GameplaySystemGroup`" genuinely differs per state (`Lobby` must NOT freeze player movement - they have to walk out of it; `Upgrade` does, via the pre-existing `SystemDisable<GameplaySystemGroup>` mechanism, unchanged). `LevelUpUtility.OpenUpgradeScreen`/`Resolve` now also drive `Upgrade` transitions, capturing `Global.PreUpgradeState` before switching so `Resolve` restores whichever state was actually interrupted (`Lobby` or `Survival`) rather than hardcoding `Survival` - a talent-granted Chest can be opened while still in `Lobby`. Full design, current status, and open questions: **`docs/game-state.md`**. Read it before touching anything GameState/match-flow/pause-time related.

Short version: the code compiles once codegen picks up the new `GameState.qtn`/`Events.qtn` fields. `Lobby`->`Survival` (via `LobbyBoundarySystem`) and `Survival`/`Lobby`<->`Upgrade` (via `LevelUpUtility`) are both fully wired. `Event`/`Boss` are pure vocabulary only - no system transitions into or out of either yet (`Event` mirrors the already-scaffolded `RuntimePlayer.HasEvent`; `Boss` would need a hook into the existing `BossSystem`'s own encounter entry/exit, not investigated). Whether `Event`/`Boss` should pause the whole `GameplaySystemGroup` (like `Upgrade`) or just the Director (like `Lobby`) is still undecided. Simulation-side only so far - no View/UI code reacts to `GameStateChanged` yet, by explicit request.

## Quantum `.qtn` codegen gotcha

Any time a `.qtn` file changes, Quantum's DSL codegen must run before C# referencing the new component/global fields will compile. The open Editor does this automatically. If you ever need to do this headlessly (batch mode/CI), see the "Quantum codegen gotcha" section in `docs/survival-director.md` - there's a chicken-and-egg trap if new C# and new `.qtn`-derived types land in the same pass, and a real risk in running a second headless Unity instance against a project that already has a live Editor open (check for `Temp/UnityLockfile` / a running `Unity` process first).
