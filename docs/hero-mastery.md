# Hero Mastery

Every hero has exactly two Mastery lines: one **Weapon Family Mastery** (fixed weapon family) and one
**Element Mastery** (fixed element). Each has 3 ranks. Every rank adds a damage multiplier; rank 3 also
unlocks one hero-specific "R3 Special" effect. Both lines are drafted through the ordinary level-up
Passive Upgrade pool, exactly like any other Ascension - there is no separate Mastery UI, currency, or
progression system.

## Current status

Code-complete. Not yet authored/verified in-Editor - run
`Tools/RiftRaiders/Hero Mastery/Generate All Mastery Assets` once in the Unity Editor to create/wire
all 12 new `.asset` files (2 per hero) into every hero's `CharacterData.PassiveUpgrades`, without
touching any hero's other Ascension/Passive lines. Requires Quantum DSL codegen to run first (new
`.qtn` types) - see CLAUDE.md's "Quantum `.qtn` codegen gotcha".

## Regenerating

`HeroMasteryAssetGenerator.cs` (`Assets/_QuantumUser/Editor/`) is the single source of truth for all
12 Mastery assets' tuned values - each `Create<Hero>Mastery()` method authors that hero's Weapon
Family + Element pair via the same `CreateOrUpdate` idiom every other Ascension generator uses. Two
ways to run it:

- **`Tools/RiftRaiders/Hero Mastery/Generate All Mastery Assets`** - regenerates and rewires all 12
  Mastery assets across all 6 heroes in one pass, touching nothing else. `WireInto` merges
  (append-if-missing by the asset's own stable Guid) into each hero's `PassiveUpgrades` rather than
  replacing the whole list, since this tool only owns 2 of however many entries a hero has - safe to
  run on its own at any time, including before that hero's full Ascension roster has ever been
  generated.
- Each hero's own **`Tools/RiftRaiders/<Hero>/Generate Ascension Assets`** also calls the matching
  `Create<Hero>Mastery()` and folds the result into its own full-list-replace of `PassiveUpgrades`
  alongside that hero's other Ascension lines - so regenerating a hero's whole roster keeps Mastery in
  sync too, from the exact same tuned values, with nothing duplicated between the two tools.

## Design

- **Weapon Family** (`WeaponFamily.qtn`) - a new, independent axis alongside the existing `ElementType`
  (`ElementType.qtn`). A weapon is `(Family, Element)`, never coupled - `WeaponDataAsset.Family` is a
  new field, hand-tagged on the 6 real player weapons (`PistolWeapon`/`SMG`/`AssaultRifle`/
  `ShotgunWeapon`/`SniperWeapon`/`GrenadeLauncher`.asset); everything else (Lux's sentry guns, test/
  basic weapons) stays `WeaponFamily.None` and simply never matches a Family Mastery.
- **Element** reuses the existing `ElementType` enum (`Neutral/Fire/Ice/Rock/Void/Lightning`) - the
  brief's "Electric" is `ElementType.Lightning`. Rock/Void have no Mastery mapped to them; nothing
  changed about their existing baseline-status behavior.
- **Mastery data** - two abstract base classes, `WeaponFamilyMasteryData`/`ElementMasteryData`
  (`Assets/_QuantumUser/Simulation/Assets/LevelUp/`), both `: PassiveUpgradeData`. `MaxRank = 3`,
  `DamageMultiplierPerRank` is a 3-entry `FP[]` (15/30/50% for a standard line - every Weapon Family
  line and every non-Neutral Element line - 10/20/40% for Neutral; Neutral is NOT special-cased in
  code, it is simply an `ElementMasteryData` instance authoring a weaker curve, tuned lower because it
  always applies regardless of which weapon is equipped). Both curves are centralized as
  `HeroMasteryAssetGenerator.StandardDamageMultiplierPerRank`/`NeutralDamageMultiplierPerRank` rather
  than repeated per hero, so retuning either curve is a one-line change. `Apply(f, entity, rank)` is
  `sealed` on both bases: it always installs the shared `WeaponFamilyMastery`/`ElementMastery` runtime
  component (`HeroMastery.qtn`) with the resolved multiplier, then calls the `protected virtual
  ApplyRank(f, entity, rank)` hook a concrete hero subclass overrides to install its own R3 Special
  component only `if (rank >= 3)` - the exact "always-installed component + an optional rank-3-only
  extra" shape `ConcussiveImpactSkillAction`/`BoneBreakerSkillAction` already use elsewhere in this
  codebase.
- **12 concrete lines**, one pair per hero, under
  `Assets/_QuantumUser/Simulation/Assets/LevelUp/Heroes/<Hero>/PassiveSkillUpgrades/`. Each hero's
  `Editor/<Hero>AscensionAssetGenerator.cs` authors and wires its own pair into that hero's
  `CharacterData.PassiveUpgrades` (same `CreateOrUpdate`/full-list-replace pattern every other
  Ascension line already follows) - no new pool, no new `LevelUpPoolKind`. `LevelUpUtility.
  CollectPerHeroCandidates` already iterates the whole `PassiveUpgrades` list generically, so this is
  the only wiring needed for a Mastery line to show up in the normal level-up draft.
- **Damage resolution** - `HeroMasteryUtility.cs` (`Systems/Combat/`) is the single generic read point,
  called once from `DamageUtility.ResolveOutgoingDamage` (`if (source == DamageSource.Weapon) damage *=
  HeroMasteryUtility.ResolveDamageMultiplier(...)`). It resolves the owner's currently-equipped
  `Weapon` once, then checks `WeaponFamilyMastery`/`ElementMastery` plus every R3 Special that is
  itself a conditional multiplier (Point Blank, Vendetta bonus, Infernal Rage, Deadeye) off that same
  `WeaponDataAsset`. **No hero-identity branch anywhere in this file or in `DamageUtility`** - every
  check is "does the owner hold this generic component, does the equipped weapon's Family/Element
  match".

## R3 Specials - implementation notes

| Hero | Line | R3 Special | How |
|---|---|---|---|
| Brute | Shotgun | Point Blank | `PointBlankUpgrade{DamageBonus,Range}`, read in `HeroMasteryUtility.ResolvePointBlank` via plain owner-target `Transform3D` distance. No dependency on pellet count/fire rate/magazine size. |
| Brute | Neutral | Armored Assault | Two independent components: `NeutralWeaponKnockbackBonusUpgrade` (read in `DamageUtility.ResolveKnockbackScale`, gated on the owner's equipped weapon being Neutral) and `ChargedDamageBonusUpgrade` (read in `ResolveOutgoingDamage`, gated on `BruteAscensionUtility.IsJuggernautCharged` - **no Stunned-target bonus**, and no second Charge resource; reuses Juggernaut's existing `JuggernautCharge`/`MaxCharge`). |
| Pixie | Grenade Launcher | Rocket Conversion | `ProjectileMovementOverride{Family,Movement}` - a new **generic** per-shot movement-asset override. `Projectile.MovementOverride` (new field) is resolved once at fire time (`WeaponSystem.FireProjectile`/`FireEchoProjectile`) and preferred every tick by `ProjectileSystem.Update` over `ProjectileDataAsset.Movement`. The shot's `Hit`/explosion asset, perks, and Element are all untouched - it is the same Grenade Launcher shot, just flying on `GrenadeLauncherRocketMovement.asset` (a `StraightProjectileMovementData` instance) instead of the weapon's own ballistic arc. Direct Hit needs **zero special-casing** - it only ever reads the resolved explosion center/radius (`DemolitionMasteryUtility.ApplyProximityEffects`), which a straight-line impact produces exactly like an arc's. |
| Pixie | Fire | Incendiary Rounds | Every Fire-weapon hit detonates a real `AreaHitData` explosion, guaranteed (not a proc chance, unlike Pixie's separate Explosive Rounds Ascension). `FireWeaponExplosiveShotUpgrade{Explosion: AssetRef<AreaHitData>}` - fully author-configured (BlastRadius/TargetMask/Effects) via the same asset type Grenade Launcher's own weapon uses, rather than bespoke fields. Triggered from `StatusEffectUtility.TryTriggerFireWeaponExplosion`, called unconditionally for every Fire-element weapon hit (`TryApplyElementalStatus`), using `AreaHitData`'s own public, Projectile-agnostic `Detonate(...)` overload - the same one `ExplodeOnDestroyUtility` uses for a planted bomb with no live `Projectile*`. Direct Hit/Unstable Mixture/Pocket Bombs/Cluster Bomb all apply automatically, zero extra plumbing. (Replaced the original "Flash Burn" design - detonating part of a Burning target's remaining Burn - which is no longer implemented.) |
| Max | Pistol | Vendetta | `VendettaPistolUpgrade{DamageBonus}`, read in `HeroMasteryUtility.ResolveVendettaPistolBonus` against the target's existing `RevengeMark` - no duplicate mark. |
| Max | Fire | Infernal Rage | `InfernalRageUpgrade{DamageBonus}`, gated on `f.Has<RageOverdrive>(owner)` (component **presence** = "an Overdrive activation is running", the same convention every other Overdrive Ascension already reads). Generates no Rage. Both this bonus and the base Fire Mastery %-damage multiplier also apply on a **Neutral** weapon whenever Ignition's guaranteed Burn is active (`HeroMasteryUtility.HasGuaranteedBurn` reads `CharacterStats.BurnOnHitStacks != 0`) - that hit lands a Burn just like a Fire weapon's would, so it counts as eligible for Fire Mastery too. |
| Kai | Sniper | Deadeye | `DeadeyeUpgrade{DamageBonus}` + a new **generic** `WeaponFamilyFirstHitTracker` component (4-slot `(Owner,Family)` ledger per target, `WeaponFamilyFirstHitUtility.TryConsumeFirstHit`) - reusable by any future "first hit of family X from owner Y" effect, deliberately separate from Kai's own `FirstStrikeMark` so the two Ascensions' state never merges. |
| Kai | Neutral | Ghost Shot | See "Ghost Shot / generic Damage Echo" below. Kai's Element Mastery was originally Ice ("Cold Blooded") - **replaced** by Neutral/Ghost Shot; Cold Blooded/`ColdBloodedUpgrade`/`ColdBloodedWindow` are no longer implemented. |
| Zara | SMG | Full Tempo | `ConditionalWeaponFireRateBonus{Family,FireRateBonus,Active}` - a **generic** component read by `WeaponSystem.ResolveLiveFireCooldown` with zero Flow-specific knowledge. `Active` is flipped by `ZaraFlowUtility.ApplyStatBonuses` on Flow's own activation edge (the same place `FasterTempoPassiveUpgradeData`'s own Fire Rate bonus already rebakes). **Renamed** `FasterTempoPassiveUpgradeData`'s existing rank-3 nickname from "Full Tempo" to "Perfect Rhythm" to avoid colliding with this new name - cosmetic only, no mechanics moved. |
| Zara | Electric | High Voltage | `SelfFireRateOnJoltUpgrade{FireRateBonus,Duration}`, triggered from `StatusEffectUtility.ApplyElementBaseline`'s `Lightning` case (`TryTriggerSelfFireRateOnJolt`) - applies via the existing per-source Haste slots (`StatusEffectUtility.ApplyHaste`, refresh-not-stack by construction). |
| Lux | Assault Rifle | Targeting Link | `TargetingLinkUpgrade{SentryDamageBonus,MarkDuration}` + `TargetingLinkMark{MarkedBy,Remaining}` on the target, ticked by a new small `TargetingLinkSystem`. See "Lux multiplayer ownership" below. |
| Lux | Neutral | Neutral Focus | See "Neutral Focus / generic Priority Target" below. Lux's Element Mastery was originally Electric ("Overcharge" - a Sentry Fire Rate buff on Jolt) - **replaced** by Neutral/Neutral Focus; Overcharge, `SentryFireRateOnJoltUpgrade`, and `Sentry.OverchargeFireRateMultiplier`/`OverchargeRemaining` are no longer implemented. |

### Ghost Shot / generic Damage Echo

Kai's Neutral Mastery R3 "Ghost Shot" - the first shot of every fresh magazine is echoed a moment
later for 50% of its own damage - is implemented as **configuration of a new generic, hero-agnostic
behavior**, not Kai-specific code: `DamageEchoUpgrade`/`PendingDamageEcho`/`DamageEchoSystem`/
`DamageEchoUtility` (`DamageEcho.qtn`, `Systems/Combat/`). Any future hero/relic/weapon-perk/mutation
wanting "this weapon's first shot each magazine repeats a moment later, a fraction of the damage"
installs `DamageEchoUpgrade` with its own `RequiredElement`/`DamageMultiplier`/`Delay`/`Visual`/
`EchoHit` instead of a bespoke implementation. `KaiNeutralMasteryData.ApplyRank` (rank 3 only) is the
only Kai-specific code involved - it just installs that generic component with Kai's own tuned values.

**Fire-time, not hit-time - and hit or miss, either way.** Earlier iterations scheduled an echo from
`DamageUtility.ApplyDamage`, which meant a shot that never connected got no echo at all. Scheduling now
happens from `WeaponSystem`'s own fire pipeline instead (`FireProjectile`/`FireHitscan`'s per-pellet
loops, plus `FireDoubleTapShot` for Double Tap's free extra shot), once per REAL pellet/projectile
actually launched, calling `DamageEchoUtility.TryScheduleEcho(f, owner, damage, direction)` right after
that pellet spawns - independent of whether it goes on to hit anything. `damage` is that shot's own
pre-hit resolved value (`WeaponSystem.ResolveLiveDamage`'s return - post Weapon/Element Mastery,
Phantom Strike, magazine-position bonuses) rather than a post-hit one, which means the echo's damage is
**not** crit-adjusted - crit is only ever rolled once a shot actually connects (`ResolveOutgoingDamage`),
which scheduling no longer waits for. This was a deliberate tradeoff, not an oversight.

**Only the first shot of a fresh magazine qualifies.** `WeaponSystem.Update` computes
`isFirstBullet = filter.Weapon->Ammo == filter.Weapon->MagazineSize` (read before this shot's own
`Ammo--`, same magazine-position idiom Opening Burst/Escalating Rounds/Execution Rounds/Final Round
already use) and threads it through `FireShot`/`FireProjectile`/`FireHitscan` down to the per-pellet
scheduling call, which is a no-op whenever it's `false`. `PendingDoubleTapShot` carries its own
`IsFirstBullet`, snapshotted from the primary shot, so a Double Tap replay of the first bullet still
counts - the delayed replay is still "part of firing the first bullet" from a magazine's perspective.

**One echo PER PELLET, fanning out the same way the original shot did.** A multi-pellet weapon (a
shotgun) spawns several real pellets from one trigger pull, each in the same per-pellet loop this hook
sits in - so an N-pellet weapon's first shot schedules N independent echoes, one per pellet, each along
THAT pellet's own resolved fired direction (`launch.Velocity.Normalized` in `FireProjectile`, the
already-spread-rotated `pelletDirection` in `FireHitscan`). This direction is captured once into
`PendingDamageEcho.Direction` and replayed literally unchanged at execute time - the SAME "snapshot the
direction, don't re-aim live" idiom `PendingEcho`/`PendingDoubleTapShot` already use for their own
delayed replays (Echo Chamber/Infinite Echo/Double Tap) - rather than re-deriving where the owner is
currently aiming. Every OTHER shot in the magazine (2nd, 3rd, ... through the last) schedules nothing at
all, even if it also connects.

**Delivery: a real traveling projectile that IS the current weapon's own shot.**
`DamageEchoSystem.SpawnEchoProjectile` reads the OWNER's CURRENTLY-EQUIPPED `Weapon.WeaponData` ->
`WeaponDataAsset.ProjectileData` (re-resolved fresh at execute time, not frozen at schedule time - so
if the owner swapped weapons mid-delay the echo reflects whatever they're holding NOW) and spawns a real
`Projectile` entity from THAT asset via the same `ProjectileSpawner` every weapon shot uses - same
`Prototype`, same `Movement` - so the echo looks and flies exactly like a normal shot from whatever
weapon is currently equipped (a Sniper's echo is a Sniper bolt, a Grenade Launcher's arcs and lands like
a real grenade) with zero per-weapon authoring. It launches from a freshly-resolved spawn origin (the
owner's live position/aim/weapon offset - the same `SpawnAnchor`/`SpawnOffset`/hold-offset resolution
`WeaponSystem.Update` uses for a genuine shot) along the frozen `Direction` captured at schedule time,
via `ProjectileMovementData.GetLaunch` (free-aim, no locked target - there is nothing to lock onto,
only a fixed heading). Only `Hit` is swapped, via the generic `Projectile.HitOverride` field
(`ProjectileSpawner.Spawn`'s `hitOverride` param, mirroring the existing `MovementOverride` mechanism
built for Pixie's Rocket Conversion) - `ProjectileSystem` prefers `HitOverride` over `projectileData.Hit`
every tick it resolves hit behavior. `EchoHitData` (the one `EchoHit` asset today, `GhostShotHit.asset`)
is deliberately minimal - no `Effects` list, no elemental status application, no on-hit proc chain - it
exists to do exactly one thing: call `DamageUtility.ApplyDamage(..., bypassOutgoingResolution: true)`
with the projectile's own already-resolved `Damage` the instant it lands, and never re-detonates a
Grenade Launcher's own full area explosion (the echo's `Hit` is `EchoHitData`, never `AreaHitData`,
regardless of which weapon is echoed). No-op (nothing spawns) if the owner currently has no valid
weapon/`ProjectileData` to echo (e.g. unequipped mid-delay) or no valid launch solution - there is no
instant-damage fallback any more, since there is no longer a specific target to deal instant damage to.
`EchoProjectile` (a tiny component added onto the spawned entity, carrying just the `Visual` `AssetRef`)
is what lets `EchoHitData.ApplyHit` still fire `DamageEchoTriggered` with the right particle reference
once travel time means the echo lands well after `DamageEchoSystem` scheduled the spawn. Presentation -
"ghostly" - is left entirely to the user parenting their own particle onto the spawned projectile's
view; nothing in simulation distinguishes a Ghost Shot echo's projectile from a normal shot beyond its
`HitOverride`.

**Deterministic delayed execution.** `TryScheduleEcho` appends `(DelayRemaining, Damage, Visual,
EchoHit, Direction)` into the owner's own `PendingDamageEcho` component (a fixed 16-slot array,
find-a-free-slot-or-drop - generous headroom for an N-pellet weapon's own echoes landing in the same
`Delay` window, see "recursion/stacking safety" below). `DamageEchoSystem`, a plain
`SystemMainThreadFilter` ticking every owner with a `PendingDamageEcho`, decrements each active slot by
`f.DeltaTime` every simulation tick and fires on expiry - ordinary Quantum tick time, no
`MonoBehaviour`/coroutine/`Unity.Time` anywhere in the path.

**Recursion prevention - structural, not a flag.** `TryScheduleEcho` is only ever called from
`WeaponSystem`'s own fire pipeline (a genuine pellet spawn). The echo's own damage call
(`EchoHitData.ApplyHit` -> `DamageUtility.ApplyDamage(..., bypassOutgoingResolution: true)`) never
re-enters `WeaponSystem` at all, so an echo is structurally incapable of scheduling another echo, with
no `CanTriggerSelf`-style flag needed anywhere.

**Does NOT compose with Echo Chamber / Infinite Echo, unlike an earlier hit-based design.** Those
weapon perks replay shots through their OWN separate pellet-spawn path (`WeaponSystem.FireEcho` ->
`FireEchoProjectile`/`FireHitscan`), which does not call `DamageEchoUtility.TryScheduleEcho` - so an
Echo Chamber/Infinite Echo replay currently produces no Ghost Shot echo of its own, even on the first
shot of a magazine. This is a known, intentional scope limit (not silently dropped - flagged here) to
keep this redesign to the primary fire path and Double Tap's own replay (which DOES compose, via
`PendingDoubleTapShot.IsFirstBullet`); extending `FireEchoProjectile`/`FireHitscan`'s Echo Chamber call
site with the same per-pellet hook would restore it, as a follow-up.

**Presentation.** `DamageEchoTriggered` (`Owner, Target, Position, Damage, Visual`) fires once the echo
actually lands (never for a cancelled one). `Visual` is an `AssetRef<DamageEchoVisualData>` - a small,
generic, hero-agnostic asset whose Unity-only `.View.cs` partial carries the actual `ParticleSystem`
prefab, exactly the same "configure the particle on the asset itself" split
`QuantumRoundsWeaponPerkData`/`QuantumRoundsWeaponPerkData.View.cs` already establishes.
`KaiNeutralMasteryData.GhostShotVisual` points at Kai's own `GhostShotVisual.asset` instance (its
`EffectPrefab` left for hand-authoring in the Inspector). `EffectsManager.OnDamageEchoTriggered`
resolves it the same way `OnQuantumRoundsTriggered` resolves `Source.ImpactEffectPrefab`, falling back
to `defaultAreaBlastEffect` at `damageEchoEffectScale` if unassigned - a plain point-spark `PlayEffect`
call at the target's position, the same shape every other one-shot reaction VFX in `EffectsManager`
already uses. **Limitation:** true GameObject-parenting onto a target's live `EntityView` transform (so
the spark visually follows a still-moving target rather than staying at the frozen impact position)
is NOT implemented - every existing `EffectsManager` handler is position-only, pooled, and unparented,
and adding parenting would mean either extending the shared `PlayEffect` utility or a dedicated new
script; flagged here as a follow-up rather than guessed at.

**In-flight ghost particle, separate from the impact spark above.** `ProjectileView.Initialize` ->
`AttachEchoGhostParticle` checks the spawned entity for the generic `EchoProjectile` component
(present only on an echo's own projectile, never a normal shot); if present and its `Visual` has an
`EffectPrefab`, that prefab is instantiated as a child of the projectile's visual root BEFORE
`ProjectileVisualController.Detach` takes over, so it rides along with the flight for free - picked up
by that controller's own `GetComponentsInChildren<ParticleSystem>` scan (cleared on the
teleport-to-muzzle, hidden while `RemainingSpawnDelay` hasn't elapsed) with zero bespoke code. On a
real impact the instance is handed to `ParticleGracefulStop` via
`ProjectileVisualController.Settings.EchoGhostParticle` (the same treatment `TrailParticle` already
gets in `Finish`) so already-emitted particles keep fading instead of being cut off mid-emission by the
rest of the visual's `Destroy(gameObject)`; an orphan/teardown cleanup (no real impact) still destroys
it immediately alongside everything else, same as `TrailParticle`. This is the mechanism for "parent my
own particle onto the projectile" - configure it once on `GhostShotVisual.asset`'s `EffectPrefab` and
it plays for the whole flight of every Ghost Shot echo, generic and hero-agnostic (driven off
`EchoProjectile`, not anything Kai-specific).

### Neutral Focus / generic Priority Target

Lux's Neutral Mastery R3 "Neutral Focus" - a Neutral weapon hit makes that enemy her Focus Target,
which all of her own Sentries prioritize while it stays in their own normal range and stays valid - is
implemented as **configuration of a new generic, hero-agnostic behavior**, mirroring how Ghost Shot
sits on top of Damage Echo: `PriorityTargetUpgrade`/`PriorityTarget`/`PriorityTargetSystem`/
`PriorityTargetUtility` (`PriorityTarget.qtn`, `Systems/Combat/`). Any future hero/relic/summon/command/
mark wanting "my controlled units should prefer THIS target over their own normal targeting, while it
stays valid" installs `PriorityTargetUpgrade` with its own `RequiredElement`/`Duration` instead of a
bespoke implementation. `LuxNeutralMasteryData.ApplyRank` (rank 3 only) is the only Lux-specific code
involved.

**Setting/refreshing/replacing the Focus Target.** `DamageUtility.ApplyDamage` calls
`PriorityTargetUtility.TrySetPriorityTarget(f, owner, target)` under the exact same gate as Damage Echo
(`source == Weapon && bypassOutgoingResolution == false` - a genuine weapon trigger pull, never Sentry/
Skill/Dash damage or a DoT tick). It unconditionally overwrites the owner's single `PriorityTarget`
slot (`Target`/`Remaining`) - since an owner only ever has ONE active Focus Target by design, this same
overwrite gives "refresh on the same target" and "replace on a different target" for free, with no
list/array and no extra branching. `PriorityTargetSet` fires only when the target actually *changes*
(not every refresh on the same one), for presentation.

**A priority override, never forced targeting.** `SentryBarrelSystem.Update` tries
`PriorityTargetUtility.TryGetValidPriorityTarget(f, sentry->Owner, position, engagementRange, ...)`
FIRST, falling back to the untouched `EnemyMovementUtility.TryFindNearestEnemy` call if it returns
false. `TryGetValidPriorityTarget` re-validates the stored target against the exact same rules
`TryFindNearestEnemy` already applies to every candidate it considers (has `Transform3D`, `Enemy.Phase
!= Dead`, not `Invulnerable`) plus an explicit flat-distance check against the CALLER's own
`engagementRange` (the same `ResolveEngagementRange` value the fallback path already uses) - a Sentry
never fires outside its own range, never skips its own validity rules, and behaves exactly as before
this existed whenever there's no valid Focus Target.

**Target dies / expires.** `PriorityTargetSystem` ticks `Remaining` down every tick and clears the slot
(firing `PriorityTargetCleared`) either on expiry OR the instant the stored target itself becomes
invalid (destroyed, or `Enemy.Phase == Dead`), whichever comes first - so a killed Focus Target doesn't
linger for the rest of `Duration`. Correctness doesn't depend on this system's tick order relative to
`SentryBarrelSystem`, since `TryGetValidPriorityTarget` re-validates the target live on every read
regardless of whether this system has gotten around to clearing it yet.

**Newly spawned/destroyed Sentries.** `SentryBarrelSystem` reads `sentry->Owner`'s live `PriorityTarget`
every tick (never baked at spawn), so a Sentry deployed while a Focus Target is already active
recognizes it from its very first tick, and a destroyed Sentry simply stops existing - there is no
per-Sentry state to leak or go stale.

**Deterministic multi-target explosive resolution.** A Neutral Grenade Launcher's explosion damages
several enemies via multiple individual `DamageUtility.ApplyDamage` calls (existing, untouched damage
architecture), each reaching the same `TrySetPriorityTarget` hook in whatever order that pipeline
already iterates its targets in (a deterministic physics-query order, identical on every client) - each
qualifying call simply overwrites the last. **Chosen rule: last valid processed target wins**, per the
existing deterministic iteration order - the simplest option consistent with how the pipeline already
works, requiring no new priority/nearest/highest-damage logic layered on top.

**Damage-neutral by design.** Neither `PriorityTargetUpgrade`/`PriorityTarget` nor
`TryGetValidPriorityTarget` touch Sentry Damage, Fire Rate, Exposed, or any vulnerability - Sentry
targeting is the only thing a `PriorityTarget` ever changes.

### Lux multiplayer ownership

A Sentry barrel fires with **itself** as `owner` (see `WeaponSystem.Update`'s `filter.Entity`), and a
barrel never carries `CharacterStats` - so any bonus gated behind the `CharacterStats` check in
`DamageUtility.ResolveOutgoingDamage` never reaches Sentry-attributed damage at all (confirmed by
`SpawnSentrySkillAction`'s own comment on why Skill Damage has to be pre-baked into the barrel's
`Weapon.DamageMultiplier` instead). Targeting Link's bonus therefore has to run **before** that gate -
`HeroMasteryUtility.GetTargetingLinkMultiplier` is called from the same pre-gate block as
`StatusEffectUtility.GetOutgoingDamageMultiplier`/`ProtectorAuraUtility.GetFearlessBonusMultiplier`,
resolves `owner` (a `SentryBarrel`) -> its `Sentry` chassis -> that `Sentry.Owner`, and only pays out if
*that specific* Lux both still holds `TargetingLinkUpgrade` and placed the `TargetingLinkMark` on the
target (`TargetingLinkMark.MarkedBy == sentry.Owner`) - another player's Sentry, or a mark a different
Lux placed, never qualifies. Neutral Focus is isolated the same way at the OTHER end - `PriorityTarget`
lives on Lux herself (not the Sentry), and `SentryBarrelSystem` reads it via `sentry->Owner`, so one
Lux's Focus Target can only ever redirect Sentries whose own `Owner` is that same Lux.

## Configurable values

Every per-rank damage multiplier and every R3 Special's tuning numbers (Point Blank's Range, Armored
Assault's two bonuses, Rocket Conversion's `Speed`, Incendiary Rounds' `AreaHitData` (BlastRadius/damage %), Vendetta/Infernal
Rage/Deadeye's bonuses, Ghost Shot's `DamageMultiplier`/`Delay`/`Visual`/`EchoHit`, Full Tempo/High Voltage's Fire Rate bonuses and
durations, Targeting Link's bonus and mark duration, Neutral Focus's `Duration`) are plain fields on
that hero's concrete data class, authored in its `AscensionAssetGenerator.Generate()` - none of it is
hardcoded in the resolution code. All initial values above are decisive placeholders per the design
brief, not a balance pass.

## Known limitations

- **Neutral Mastery's Knockback bonus** (`NeutralWeaponKnockbackBonusUpgrade`, read in
  `DamageUtility.ResolveKnockbackScale`) is keyed off the owner's *currently-equipped weapon's Element*
  at the moment of knockback, since `ApplyKnockback`/`ApplyKnockbackImpulse` carry no `DamageSource`/
  weapon-origin of their own. In practice this is exactly "Neutral weapon hits gain knockback" (weapon
  hits are the overwhelming majority of what routes through `ApplyKnockback` for a hero), but a rare
  non-weapon knockback Brute triggers (e.g. a skill) while a Neutral weapon happens to be equipped would
  also receive the bonus.
- **Rocket Conversion's aim-at-center resolution** (`WeaponSystem.ResolveAimsAtCenter`, used once up
  front to pick an aim point before `FireProjectile` runs) still reflects the weapon's *default*
  Movement's `AimsAtTargetCenter`, not the override's - a minor aim-point nuance (center vs. edge), not
  a functional break; the rocket still flies at the target.
- **Deadeye/Targeting Link "per target" state** (`WeaponFamilyFirstHitTracker`,
  `TargetingLinkMark`) is scoped globally per target, not per-co-op-lobby-vs-per-owner beyond their own
  4-slot/`MarkedBy` scoping - acceptable given the existing player cap, called out here rather than
  silently assumed.
- **Ghost Shot does NOT compose with Echo Chamber/Infinite Echo**, unlike an earlier hit-based design
  that explicitly did - see "Ghost Shot / generic Damage Echo"'s own paragraph on this. Double Tap's
  own replay still composes (`PendingDoubleTapShot.IsFirstBullet`).
- **Ghost Shot's `PendingDamageEcho` queue** is a fixed 16 slots per owner. A high-pellet-count weapon
  (a shotgun) schedules one echo PER pellet on its first shot, and Double Tap can schedule a second
  full volley on top - a build stacking enough pellets across both could theoretically schedule more
  than 16 echoes inside one `Delay` window; `DamageEchoUtility.ScheduleEcho` drops (logs, doesn't
  crash) any echo past that cap rather than growing unbounded. Not expected to matter at any pellet
  count currently authored, called out rather than silently assumed.
- **Neutral Focus is evaluated per-barrel, not per-Sentry-chassis** - `TryGetValidPriorityTarget` is
  called once per `SentryBarrel` (the existing targeting granularity - each barrel independently aims,
  see `SentryBarrelSystem`'s own class comment), using THAT barrel's own `ResolveEngagementRange`. A
  Sentry with multiple different weapon systems (Weapon Systems Ascension: Cannon/Minigun/Rocket/Laser,
  each with its own range) can therefore have some barrels prioritize the Focus Target while others
  don't, if it's in range of one but not another - this matches "each Sentry checks whether the Focus
  Target is within its normal attack range" read per-weapon rather than per-chassis, and requires no
  extra code, but is worth calling out since the brief's wording could be read either way.
- **Ghost Shot's impact spark is still position-only**, not parented to the target's live view
  transform (see "Ghost Shot / generic Damage Echo"'s own Presentation paragraph) - but Ghost Shot now
  travels as a REAL projectile entity, spawned from the owner's currently-equipped weapon's own
  `ProjectileDataAsset`, with its own live `ProjectileView` the whole flight - a "ghostly" look is left
  entirely to the user parenting their own particle onto that view (nothing in simulation marks the
  entity as special beyond its `HitOverride`). Only the brief burst at the moment of impact remains
  unparented.
- **Ghost Shot echo projectiles never lock onto a target** - they spawn via `ProjectileMovementData.
  GetLaunch` (free-aim, along the fixed `Direction` snapshotted at schedule time) rather than
  `GetLaunchToTarget`, and `ProjectileSpawner.Spawn` is called with no `target:` argument. A weapon
  whose own `ProjectileData.Movement` is `HomingProjectileMovementData` therefore has nothing to home
  onto and just flies straight along that direction (its own documented no-target fallback) - same as
  every other movement type. This is deliberate: an echo no longer repeats a specific hit on a specific
  enemy, only the shot itself, so there is no "original target" left to track.
