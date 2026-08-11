# Kai — Ascensions

Kai's Hero Ascension pool was consolidated from a fragmented, mostly-dead roster into exactly 10
three-rank Ascension lines (4 Vortex Hero Skill, 3 Passive, 3 Dash), reusing the exact same generic
rank architecture built for Pixie/Brute/Max's own refactors
(`IRankedUpgrade`, `MaxRank` on both `PassiveUpgradeData`/`SkillActionData`, `UpgradeHistoryUtility.
GetCount`, rank-aware `Apply`/`Execute` overloads - see `docs/level-up-upgrades.md`'s own architecture
section). No Kai-specific rank code exists anywhere in this refactor.

## The `CheckActions` bug (found and fixed as part of this refactor)

Before this refactor, `KaiVortexSkill.asset` had `CheckActions: 0` - the same dead-code bug already
found and fixed for Brute's Juggernaut (see `docs/brute-ascensions.md`'s own "CheckActions bug"
section). `SkillSystem.InvokeActions` resolves `int actionCount = skill.CheckActions == true ?
skill.Actions.Count : 0;` - with `CheckActions` false, none of Vortex's 7 embedded baseline sub-actions
ever executed, regardless of their own `Activated` flag. 6 of the 7 (Damage Pulse/Vortex Collapse/
Random Blast/Bigger Vortex/Homing Shard/Crowd Damage) had `Activated: 1`, so they were simultaneously
dead AND unofferable (`LevelUpUtility.AddHeroSkillUpgradeCandidates` only offers `Activated == false`
entries) - a live Kai's Vortex dealt **zero direct damage** at baseline. Only "Power Pulse"
(`Activated: 0`) was ever reachable at all. The fix: same as Brute's, the old sub-action classes are
deleted or repurposed (not re-enabled) - all 4 new Vortex Ascension lines are ranked `SkillActionData`
living on `KaiVortexSkill.Actions` instead (`Activated = false`), granted via `SkillSlot.Upgrades` at
pick time, which bypasses `CheckActions` entirely regardless of its value.

## Base Vortex changes

`KaiVortexSkill.Damage` (12) used to double as `Vortex.Force` (pull strength) and was never actually
dealt as damage anywhere - no effect on the thrown projectile's `DirectHitData` ever called
`DamageUtility.ApplyDamage`. Fixed to match the "one Damage field is both a literal on-hit number and
the percentage basis every Ascension scales off" idiom already used by `JuggernautSkillData.Damage`/
`BruteAscensionUtility.ResolveJuggernautSkillDamage`:

- `KaiVortexSkill.Damage` is now "Vortex Skill Damage" - the basis every Compression/Vortex Collapse/
  Void Shards percent reads via `KaiAscensionUtility.ResolveVortexSkillDamage(f, owner)`.
- It's also dealt once, directly, as flat Cast Damage - `SpawnVortexEffectData.Apply` calls
  `DamageUtility.ApplyDamage(f, context.Target, context.Damage, context.Owner, context.Source)`
  whenever the throw actually hit an entity (`context.Target != EntityRef.None`), right alongside
  spawning the vortex. This is a deliberate simplification from routing it through a second
  `KaiHitActivation.Effects` entry - functionally identical, avoids touching the deeply-embedded
  projectile hit-effect chain.
- Pull `Force` is fully decoupled from Cast Damage: `SpawnVortexEffectData.PullForce` (8, placeholder)
  is its own baseline, which Singularity multiplies rather than overrides.

## The 10 Ascension lines

### Vortex (4 lines, all `SkillActionData` on `KaiVortexSkill.Actions`)

**1. Singularity** (`SingularitySkillAction` → `SingularityUpgrade`, replaces `VortexPowerPulseUpgrade`)
- R1 "Disruption": pull radius +30%; interrupts Filler/Normal enemies' own attacks - winding up
  (Preparation/Telegraph) OR already committed (Active, e.g. a charging Charger or an airborne
  Leaper) - unlimited per cast.
- R2 "Crushing Gravity": pull radius +50%, force +30%; interruption reaches Specialist/Heavy, capped
  at once per enemy per Vortex cast (`VortexInterruptTracker`, a fixed-array component living on the
  vortex entity itself, discarded free on destroy).
- R3 "Singularity": pull radius +75%, force +50%; adds a periodic stronger gravity pulse
  (`VortexGravityPulse`, 3x force, every 1.0s); interruption reaches Elite (still capped). Bosses
  always immune - `MaxEligibleTierIndex` never reaches that high at any rank.
- Interruption itself is a NEW generic utility, `EnemyActionUtility.TryInterrupt` (Enemy system
  folder, hero-agnostic) - deliberately independent of `EnemyTierStatsConfig.
  CanBeInterruptedByKnockback` (false for Heavy/Elite/Boss in the live config - that flag is about
  physical-push resistance, a different concern from a pure state-machine cancel with no impulse).
  Handles BOTH halves of an enemy's own action state machine, same Preparation/Telegraph-vs-Active
  branching `EnemySystem.OnEnemyKnockedBack` already uses internally (`EnemySystem.CancelWindup`/
  `CancelActive`, both bumped from `private` to `internal` so this utility can call them directly) -
  never touches Idle/Chasing/Execute/Recovery/Dead. Tier eligibility/caps are pure data
  (`MaxEligibleTierIndex`/`UnlimitedBelowOrEqualTierIndex`, indexed by `(byte)EnemyTier`) - zero
  hardcoded tier names in `VortexSystem` itself. The utility also respects `EnemyActionData.
  InterruptibleDuringTelegraph`/`InterruptibleDuringActive` BY DEFAULT (a couple of attacks -
  Charger/Grenadier's own charge-up - are explicitly authored non-interruptible on both) - Vortex's
  own two call sites pass `ignoreInterruptibleFlag: true`, since Singularity is a dedicated hard-CC
  pick that should punch through those exemptions too, not just `CanBeInterruptedByKnockback`. A
  future non-Ascension caller of this utility would get the respectful default instead.

**2. Compression** (`CompressionSkillAction`, merges old `VortexDamagePulseSkillAction` +
`VortexCrowdDamageSkillAction`)
- R1: Vortex deals 20% Vortex Skill Damage every 0.5s to trapped enemies (revives the pre-existing,
  previously-dead `VortexDamageUpgrade`/`AreaDamage` mechanism).
- R2: +8% damage per enemy trapped beyond the first, capped at 8 enemies (+56% max) - revives
  `VortexCrowdDamageUpgrade`/`VortexSystem.ResolveCrowdMultiplier`.
- R3 "Implosion": every 3rd pulse also detonates at the vortex's own center for 75% Skill Damage
  (`VortexImplosionUpgrade`, new), itself scaled by the same crowd multiplier.

**3. Vortex Collapse** (`VortexCollapseSkillAction`, repurposes `VortexExplodeOnDestroySkillAction`)
- R1: on expiry/destruction, detonates for 150% Skill Damage (revives the pre-existing, previously-dead
  `VortexExplodeOnDestroy`/`VortexSystem.TryExplodeOnDestroy`).
- R2: 200% Skill Damage, +25% blast radius.
- R3 "Event Collapse": 250% Skill Damage, +50% radius total, plus one strong `ApplyPull` sweep
  immediately before detonating (`PreExplosionPullForce`).

**4. Void Shards** (`VoidShardsSkillAction`, repurposes `VortexHomingProjectileSkillAction`)
- R1: fires 1 homing Void Shard every 1.0s for 30% Skill Damage, stops on the first enemy hit (revives
  the pre-existing, previously-dead `VortexHomingProjectileUpgrade`/`VortexSystem.TryHomingProjectile`).
- R2: 0.75s interval, 40% Skill Damage, wider search radius, pierces through 2 enemies per shard.
- R3: fires 2 shards per volley (45% each, each piercing through 3 enemies), the second preferring a
  distinct target from the first when one's available within range (falls back to the same target
  otherwise - resolved fresh per fire tick, no persistent tracker).
- Piercing (`VortexHomingProjectileUpgrade.PierceCount`) overrides whatever `Projectile.
  RemainingPierces` the shard's own `ProjectileDataAsset` baked in at spawn (`ProjectileSpawner.Spawn`
  already ran `DirectHitData.Initialize` by the time `VortexSystem.TryHomingProjectile` sets it) -
  "how many enemies this shard pierces" is a property of the Ascension rank, not the base shard asset.

### Passive (3 lines, all `PassiveUpgradeData` on `KaiCharacterData.PassiveUpgrades`)

Base Void Field (`VoidFieldPassiveData`, Inspector-only, unpicked): radius 2.5m, slows enemy
projectiles passing through to 60% speed. No enemy-attack-speed slow at baseline - that's Event
Horizon rank 3 only.

**5. Event Horizon** (`EventHorizonPassiveUpgradeData`, merges old `EventHorizonPassiveUpgradeData` +
`TimeDilationPassiveUpgradeData` + `VoidPressurePassiveUpgradeData`)
- R1: Void Field radius +1.5m (4.0m total); projectile speed drops to 50%.
- R2 "Time Dilation": radius +2.5m total (5.0m); projectile speed drops to 40%.
- R3 "Void Pressure": radius/projectile speed unchanged from R2 (5.0m / drops to 20%); enemies
  standing in the field also have their own attack-execution timers slowed to 60% speed ("40%
  slower"). Which tiers this affects is now data (`ProjectileSlowField.MaxAffectedEnemyTierIndex`,
  seeded to Specialist by `VoidFieldPassiveData.Apply`, preserving the exact pre-refactor
  Filler/Normal/Specialist-only behavior) rather than a hardcoded comparison in `VoidFieldSystem`.
  `SpeedMultiplierBonus`/`EnemyTimeDilationMultiplier` are both subtracted-from-baseline/set-fresh
  each rank (see `EventHorizonPassiveUpgradeData.Apply`) - values are illustrative placeholders,
  tune freely in `KaiAscensionAssetGenerator.cs`.

**6. Undertow** (`UndertowPassiveUpgradeData`, now ranked)
- R1: a weapon hit pulls the struck enemy toward its nearest other enemy (`DamageUtility.ApplyPull`,
  never `ApplyKnockback` - doesn't stagger the victim, same idiom Vortex's own pull uses).
- R2: stronger pull; +50% force specifically against Specialist+ targets (`HeavyTierMultiplier`).
- R3 "Gravitational Bond": a landed pull also Binds the target for 2s (`StatusEffectUtility.
  ApplyBound` - a new GENERIC, not Kai-named, status, same `TemporaryDamageReductionRemaining`-style
  precedent); Kai deals +20% damage to any Bound enemy (not per-source - any Kai with the bonus
  benefits from any Bound enemy, since Bound is a status like Stun/Intimidate, not an
  attribution-scoped mark).
- **Visual feedback**: `UndertowPull` (lives on the struck enemy) carries `LinkTarget`/`StyleId` -
  simulation only authors this data. Rendered by `KaiUndertowLinksView`, attached to KAI's own
  prefab (not per-enemy - `UndertowPull`/`EnemyAllyLinkView`'s own per-enemy pattern was the first
  attempt, but Kai's prefab is the single known attachment point vs. every enemy variant needing it),
  scanning ALL live `UndertowPull` instances every frame and handing them out to a small fixed pool
  of pre-authored child `LineRenderer` slots (find-or-assign, same "cap concurrent count" idiom
  `StatusEffects.HasteRemaining`'s own array uses) since Undertow can affect several enemies at once.
  No event needed for the ongoing visual - polling live simulation state avoids event/rollback drift.
  A separate one-shot `UndertowTriggered` event drives a small impact/mark flash on both entities
  (`EffectsManager.OnUndertowTriggered`).

**7. First Strike** (`FirstStrikePassiveUpgradeData`, now ranked)
- R1: first hit against an enemy deals +40% bonus damage.
- R2: +70%.
- R3 "Perfect Opening": +100%, and the mark refreshes after 5s without Kai damaging that specific
  enemy.
- Replaces the old bare `KaiFirstStruck` tag (which could only ever express "has this enemy ever been
  hit," no source-tracking, no refresh) with `FirstStrikeMark` - a `RevengeMark`-shaped component
  (`EntityRef MarkedBy; FP RemainingGrace;`) living on the target, matching the spec's own "track per
  Kai-source and enemy-target" ask. `RemainingGrace` is 0 at ranks 1-2 (permanent mark, "never
  removed" preserved exactly - a new `FirstStrikeMarkTimeoutSystem`, copied from `RevengeMarkTimeoutSystem`,
  ignores a mark with `RemainingGrace <= 0`) and 5 at rank 3 (ticks down, frees the mark at 0). In 2-Kai
  co-op, a second Kai's hit reclaims the mark rather than tracking both simultaneously - same
  single-active-holder precedent `RevengeMark` itself already established.

### Dash (3 lines, all `SkillActionData` on `KaiCharacterData.DashSkillUpgrades`)

**8. Mirror Step** (`MirrorStepSkillAction`, repurposes `ReflectProjectilesSkillAction`)
- R1 "Reflect": while dashing, enemy projectiles within 3m are reflected back toward their owner
  (velocity flipped, re-owned by Kai) - Elite/Boss-owned projectiles excluded.
- R2: radius 4.5m; reflected projectiles deal 1.5x their own damage (new - the old Reflect never
  scaled damage at all).
- R3 "Evasive Reflex": each successful reflection reduces Vortex's cooldown by 0.5s, capped at 2s per
  Dash (`MirrorStepCooldownAccumulator`, a running per-dash total reset every Dash Begin - NOT reusable
  from the old boolean-shaped `EvasiveReflexUpgrade`, which could only ever fire once per dash).

**9. Phantom Strike** (`PhantomStrikeSkillAction`, new class)
- **Architectural correction**: moved from a `PassiveUpgradeData` reacting to
  `OnSkillActivated(DashSkill)` into a genuine Dash-slot `SkillActionData`, matching every other
  hero's Dash Ascension shape (was previously mislabeled as a generic "Passive Upgrade" in the
  level-up UI).
- R1: after Dashing, the next weapon hit deals +50% damage and +1 Pierce.
- R2: +75% damage, +2 Pierce.
- R3: +100% damage, +99 Pierce ("massive Pierce" as a large flat int, not a dedicated infinite-pierce
  flag - `Projectile.RemainingPierces` is `Int32`).
- The one-shot-consumable-charge mechanism itself (`PhantomStrikeCharge`, `AddOrGet` so a second dash
  before the first charge is consumed just re-arms it, consumed at fire time not hit time in
  `WeaponSystem`) is kept exactly as it was - already correct, just granted from this class's own
  `Execute` now instead of a signal handler. `WeaponSystem`'s `grantPierce` bool was generalized to
  `grantPierceAmount` (an `int`) throughout the fire path (`Update`/`FireShot`/`FireProjectile`/
  `ApplyProjectilePerks`/`PendingDoubleTapShot.GrantPierceAmount`) so rank 2/3's higher bonus survives.

**10. Warp Wake** (`WarpWakeSkillAction`, new class)
- R1 "Void Trail": Dashing drops a temporary Void (1.5s) that pulls nearby enemies inward.
- R2: larger radius, stronger pull, plus a real `AreaDamage` pulse (25% Skill Damage/0.5s).
- R3 "Repulsion": on expiry, the Void also repels nearby enemies (`VortexRepulseOnDestroy`, new
  component - `DamageUtility.ApplyKnockback` + damage, kept separate from `VortexExplodeOnDestroy`
  since "pull in then explode" and "push out instead" are different enough shapes to keep the
  per-asset `Source` typing unambiguous for the view).
- Reuses `Vortex`/`VortexSystem`/`AreaDamage` directly rather than a new system -
  `VortexSystem.Filter` only requires `{Transform3D, PhysicsCollider3D, Vortex}`, fully hero-agnostic,
  so any spawned entity carrying those three gets pull-pulses for free. Defaults to Kai's own
  `KaiVortexEntityPrototype` (same visual as his Hero Skill's vortex) rather than a dedicated new
  prefab - nothing gameplay-relevant depends on which prototype is used; a distinct Dash Void visual
  is a follow-up authoring task, not a functional gap.

## Removed / merged

Deleted outright: `TimeDilationPassiveUpgradeData`, `VoidPressurePassiveUpgradeData`,
`VortexCrowdDamageSkillAction`, `VortexRandomExplosionSkillAction`(+`.View.cs`, Random Blast dropped
entirely per design - not one of the 30 ranks), `KaiFirstStruck` (component, from the old
`VoidwalkerMastery.qtn`), `EvasiveReflexUpgrade` + `EvasiveReflexPassiveUpgradeData`,
`DashShockwaveSkillAction`(+`.View.cs`, absorbed into Warp Wake R3), `PhantomStrikePassiveUpgradeData`
(recreated as a Dash `SkillActionData`), `KaiVoidFieldAssetGenerator.cs`,
`KaiVoidwalkerMasteryAssetGenerator.cs`, `docs/kai-voidwalker-mastery.md`. The old, orphaned/never-wired
`SpawnVoid.asset` ("Void Trail" attempt) and every stale `.asset` instance left over from the deleted
classes above were also deleted.

Repurposed in place (renamed, mechanism revived/extended, not deleted): `VortexPowerPulseSkillAction`
→`SingularitySkillAction`; `VortexDamagePulseSkillAction`→`CompressionSkillAction`;
`VortexExplodeOnDestroySkillAction`→`VortexCollapseSkillAction`; `VortexHomingProjectileSkillAction`
→`VoidShardsSkillAction`; `EventHorizonPassiveUpgradeData` (kept, now ranked, absorbs 2 siblings);
`UndertowPassiveUpgradeData`/`FirstStrikePassiveUpgradeData` (kept, now ranked);
`ReflectProjectilesSkillAction`→`MirrorStepSkillAction`; `KaiVoidwalkerMasterySystem`→`KaiUndertowSystem`
(now Undertow-only - Evasive Reflex folded into Mirror Step R3, Phantom Strike moved to its own Dash
`SkillActionData`, First Strike still hooks directly into `DamageUtility.ResolveOutgoingDamage`).

New generic (hero-agnostic) systems added, reusable by any future kit: `EnemyActionUtility.
TryInterrupt` (cancels an enemy's own Preparation/Telegraph OR Active action); `StatusEffectUtility.
ApplyBound`/`IsBound` (a plain Bound status flag).

## Current status

Short version: the code compiles once Quantum codegen picks up every changed/new `.qtn` file
(`Singularity.qtn`, `VortexImplosionUpgrade.qtn`, `VortexRepulseOnDestroy.qtn`, `Undertow.qtn`,
`FirstStrike.qtn`, `PhantomStrike.qtn`, `MirrorStep.qtn`, edits to `Vortex.qtn`/`VortexExplodeOnDestroy
.qtn`/`VortexHomingProjectileUpgrade.qtn`/`ProjectileSlowField.qtn`/`StatusEffects.qtn`/`Events.qtn`/
`WeaponPerks.qtn`), and `KaiUndertowSystem`/`FirstStrikeMarkTimeoutSystem` are registered in
`SystemSetup.User.cs` at the same relative positions their predecessors held. `Tools/RiftRaiders/Kai/
Generate Ascension Assets` (replaces the old two-generator Void-Field/Voidwalker-Mastery split) authors
and wires all 10 lines plus the base Void Field passive, fully replacing every list it touches (
`PassiveUpgrades`, `DashSkillUpgrades`, `KaiVortexSkill.Actions` - including an orphan-sweep that
purges the 6 dead pre-refactor sub-actions still embedded in `KaiVortexSkill.asset`) - **not yet run**.
Every numeric value not explicitly pinned by the original design brief (Vortex's baseline `PullForce`,
Singularity's gravity-pulse force, Compression's Implosion radius fraction, Void Shards' search-radius
multiplier, Event Horizon's rank 2/3 radius growth, Undertow's rank 2 pull-force/heavy-tier numbers,
Mirror Step's rank 2 radius, Warp Wake's full numeric pass) is a decisive placeholder pending a real
balance pass, same convention as every other hero's own Ascension refactor in this codebase. Warp
Wake's Dash Void currently reuses Kai's own Hero Skill vortex prefab rather than a dedicated one - a
cosmetic follow-up, not a functional gap. Not yet manually verified end-to-end in-Editor.
