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

A given level-up can now be locked to exactly ONE of 5 player-facing categories (`LevelUpCategory`: HeroSkill merges SkillUpgrade+PassiveUpgrade, GlobalUpgrade, RiftMutation, WeaponPerk, and a new **Choose Weapon**) via `LevelUpConfig.LevelSequence`, a repeating per-level list - an empty list (the default) keeps the original mixed-all-pools roll unchanged. Choose Weapon rolls 3 distinct weapons from a new `WeaponChoicePoolData`, each with an independently-rolled perk count driven by a new persistent per-player `CharacterStats.WeaponTalentLevel` (increments on every Choose-Weapon pick) via `LevelUpConfig.ChancePerLevelPerSlot`/`MaxRolledPerks`, rendered by a dedicated `WeaponCardWidget` (not `UpgradeCardWidget`). A new `Chest` entity/`ChestSystem` reuses this whole pipeline, forced to one category set once in the Editor per chest instead of per level. Full design and current status: still **`docs/level-up-upgrades.md`** for the category/Choose-Weapon half, and **`docs/chests.md`** for the Chest entity itself. Read both before touching anything category-sequencing/Choose-Weapon/Chest related.

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

What stayed: a generic **`ExplodeOnDestroy`** component (`Damage`/`SpawnDepth`/`AssetRef<AreaHitData> Explosion`) - a hero/feature-agnostic "detonate when this entity is destroyed" hook, checked from two independent trigger points (`DestroyAfterTimeSystem` on timed expiry, and `DamageUtility.ApplyDamage`'s non-Enemy death branch when `Health` reaches 0), both a plain optional check bolted onto neither system specifically, so any future "explodes when destroyed" feature reuses it for free. The damage-death trigger is what lets it also work as a **decoy trap**: seed `Health` to 1, add the existing `Decoy` tag (draws enemy aggro, zero new code), and a real collider *on the Player physics layer* (see `Decoy.qtn`'s own comment), and it detonates the instant an enemy kills it. `Explosion` (`AreaHitData`) is reused purely as data, so it fires the same generic `HitEffectUtility.ApplyInRadius`/`AreaDetonated` explosion every other blast in this codebase uses. Currently used by Pixie's Dash Ascension "Leave Explosive Bomb" (`DashBomb.prefab`), not by Cluster Charge. Full design, file map, current status, and known simplifications: **`docs/explode-on-destroy.md`**. Read it before touching anything Cluster Charge/Mini Bomb/`ExplodeOnDestroy` related.

Short version: `DashBomb.prefab` already has an `ExplodeOnDestroy` configured by hand in the Editor from before a mid-stream rename (`ExplodeOnExpire` → `ExplodeOnDestroy`) - its generated component reference needs re-adding once codegen regenerates (see docs/explode-on-destroy.md for the exact values to re-enter: `Damage = 10`, same `Explosion` asset).

## Pixie — Demolition Mastery

A 4-trait Hero Trait pool for Pixie (Direct Hit/Concussive Force/Volatile Payload/Mini Ordnance), mirroring Max's own Fire Mastery pool, all reacting to *any* Pixie explosion (Bunny Bomb, dash bombs, explosive weapons) via two new generic signals in `Combat.qtn` (`OnExplosionCriticalHit`, `OnAreaExplosionDetonated`) and a new shared per-target hook (`DemolitionMasteryUtility.ApplyProximityEffects`, called from `HitEffectUtility.ApplyInRadius`/`ApplyDamageInRadius`) - not a Pixie-specific branch bolted onto either. Mini Ordnance (the design's own "Cluster Charges" trait) is a deliberately distinct mechanism/name from the pre-existing `ClusterBombUpgrade`/`ClusterBombSkillAction` (Pixie's Bunny Bomb Hero Skill Upgrade) - see the "Explode-On-Destroy / Mini Bomb" section above for why that distinction matters. Full design, file map, and current status: **`docs/pixie-demolition-mastery.md`**. Read it before touching anything Demolition Mastery/explosion-reaction related.

Short version: the code compiles once codegen picks up the new `.qtn` signals/components; `Tools/RiftRaiders/Pixie/Generate Demolition Mastery Assets` authors and wires all 4 traits (append-only onto `PixieCharacterData.PassiveUpgrades`, doesn't touch Chain Reaction's own 4 entries), but Mini Ordnance's `MiniBombPrototype`/`Explosion` still need hand-authoring in the Editor.

## Brute — Knockback Mastery

A 4-trait Hero Trait pool for Brute (Ground Pound/Crushing Blow/Lasting Impact/Overwhelming Force). Unlike Max/Pixie's pools, Brute had no existing "jumps and lands" mechanic of his own (`JuggernautLandingImpactSystem` watches an *enemy* he launched, not himself), so Ground Pound reuses the generic, hero-agnostic `AutoJumpSystem` (auto-hop/mantle over ledges) instead of a new dedicated leap ability - a new `OnPlayerLanded(EntityRef entity, FP fallDistance)` signal (`PlayerMovement.qtn`) fires from its existing landing edge, reporting raw fall distance so `BruteKnockbackMasterySystem` itself decides what counts as a real fall (gated on `GroundPoundUpgrade.MinFallDistance`, so a flat auto-hop/mantle or ground-level manual jump doesn't trigger it). `StatusEffectUtility.ApplyStun` gained an `owner` parameter (previously had none) so Lasting Impact can read a live duration bonus off whoever's stunning - all ~4 existing call sites (`IronShoulderSkillAction`, `JuggernautLandingImpactSystem`, `StunEffectData`, `TryTriggerOverload`) now pass their own already-local owner through, zero behavior change for anyone without the trait. Overwhelming Force needs no new component at all - it mutates the already-live-read `CharacterStats.KnockbackMultiplier` directly. Ground Pound's knockback pulse plays a VFX authored directly on its own `GroundPoundPassiveUpgradeData` asset (`BlastEffectPrefab`, via a `.View.cs` partial and a dedicated `GroundPoundTriggered` event) rather than the shared `ShockwaveReleased` pipeline, mirroring `VortexExplodeOnDestroy.Source`'s self-referencing-AssetRef pattern. Its push strength isn't an authored raw number either - the asset picks a `KnockbackTier` (Small/Medium/Strong, the same bucket every `KnockbackEffectData` in the game already reads off `RuntimeConfig.EffectConfig.GetKnockback`), resolved once at `Apply` time and baked into `GroundPoundUpgrade` as plain `FP` since the enum itself (a plain C# enum, never declared through qtn) can't live on an unmanaged component. Full design, file map, and current status: **`docs/brute-knockback-mastery.md`**. Read it before touching anything Knockback Mastery/Stun-duration/knockback-force related.

Short version: the code compiles once codegen picks up the new `.qtn` signal/components; `Tools/RiftRaiders/Brute/Generate Knockback Mastery Assets` authors and wires all 4 traits (append-only onto `BruteCharacterData.PassiveUpgrades`, doesn't touch Protector Aura's own 4 entries). One Editor-authoring gap: `GroundPound.asset`'s `BlastEffectPrefab` still needs a particle prefab hand-assigned - falls back to `EffectsManager`'s default area blast effect until then.

## Quantum `.qtn` codegen gotcha

Any time a `.qtn` file changes, Quantum's DSL codegen must run before C# referencing the new component/global fields will compile. The open Editor does this automatically. If you ever need to do this headlessly (batch mode/CI), see the "Quantum codegen gotcha" section in `docs/survival-director.md` - there's a chicken-and-egg trap if new C# and new `.qtn`-derived types land in the same pass, and a real risk in running a second headless Unity instance against a project that already has a live Editor open (check for `Temp/UnityLockfile` / a running `Unity` process first).
