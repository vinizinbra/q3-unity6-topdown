# Pixie — Ascensions

Pixie's Hero Ascension pool was consolidated (2026-08-09) from ~13 overlapping single-pick passives/
skill upgrades down to **exactly 9 three-rank Ascension lines** (27 total rank-acquisitions), fixing
an authoring bug in the process (see "Base passive" below). This replaces the old "Demolition Mastery"
4-trait pool doc - Demolition Mastery as a distinct pool no longer exists as such; Direct Hit and
(renamed) Pocket Bombs survive as 2 of the 9 lines, Concussive Force was folded into Direct Hit, and
Volatile Payload was dropped entirely.

This is also where Pixie's Ascensions first exercise the **generic multi-rank Ascension architecture**
(`MaxRank`/`IRankedUpgrade`/`UpgradeHistoryUtility` - see `docs/level-up-upgrades.md`'s own "Ranked
Ascensions" section) - built generic from the start since every other hero's pool (Kai/Brute/Max) is
expected to go through the same treatment. Read that doc's section first for the shared mechanism;
this doc only covers what's Pixie-specific.

## The 9 Ascension lines

Each line is `MaxRank = 3`. Lines stay in whichever pool they already lived in - **Cluster Bomb,
Birthday Cake, Backblast, and Hot Fuse are `SkillActionData`** (they tie into skill Begin/OnGoing/End
phase execution - exactly right for "do something when the skill fires/dash starts/dash ends"); the
rest are `PassiveUpgradeData`. A ranked `SkillActionData`'s `Execute` reads its own live rank each
activation via `SkillUpgradeUtility.GetRank(f, entity, selfRef)` and branches internally - rank never
changes `SkillSlot.Upgrades`' shape, so "only available at rank 3" is just an `if` inside `Execute`.

1. **Cluster Bomb** (`ClusterBombSkillAction`, HeroSkill Action) - after Bunny Bomb explodes, spawns
   `Count` smaller Projectile-based bomblets (unchanged mechanism - `ClusterBombUpgrade`/
   `AreaHitData.TrySpawnClusterBomblets` - **do not redirect this onto the Mini-Bomb/`ExplodeOnDestroy`
   shape again**, see `docs/explode-on-destroy.md`'s own note), each dealing `DamagePercent` of the
   triggering explosion's own damage (threaded through `Detonate`'s `damage` parameter, not a fixed
   value - and not re-derived from `PixieAscensionUtility.ResolveBunnyBombDamage`, so a Hot-Fuse-
   empowered throw's bonus flows into bomblet damage exactly once, not twice). Ranks: Count 2/3/4,
   DamagePercent 40/45/50%.
2. **Direct Hit** (`DirectHitPassiveUpgradeData`) - enemies within the inner 35% of any of Pixie's
   explosions take bonus damage (`DemolitionMasteryUtility.ApplyDirectHit`, never baked into
   `CharacterStats`). At rank 3, the same inner-zone hits also apply strong knockback - folded in from
   the old standalone Concussive Force ascension (do not re-add it as a separate line). Ranks:
   DamageMultiplierBonus 30/50/75%, knockback only at rank 3.
3. **Birthday Cake** (`BirthdayCakeSkillAction`, HeroSkill Action) - after Bunny Bomb *lands* (not
   during flight - the old `DecoyOnThrowUpgrade` flight-time taunt was removed), it becomes a Decoy
   for `TauntDuration` seconds before detonating (`ProjectileSystem.TryPlant` reads
   `BirthdayCakeUpgrade` off the owner right as the bomb plants, adds the existing `Decoy` tag to the
   *landed bomb entity itself*, and drives its fuse from `TauntDuration` instead of the bomb's own
   authored `PlantedFuseTime`). Rank 2 also grows the blast radius itself
   (`TauntRadiusMultiplier` - the generic decoy-pull mechanic has no radius knob of its own to scale
   without hardcoding into shared enemy AI, so "wider taunt" is expressed as a bigger blast instead).
   Rank 3 additionally scales the whole detonation's damage. Both gated on the detonating entity
   currently holding `Decoy` (`ExplodeOnDestroyUtility.ApplyBirthdayCakeBonus`/
   `ResolveBirthdayCakeRadiusMultiplier`), so this can never fire for Pocket Bombs' Mini Bomb or a
   dropped DashBomb. Ranks: TauntDuration 1.0/1.5/1.5s, TauntRadiusMultiplier 1.0/1.25/1.25,
   bonus damage only at rank 3 (+30%).
4. **Pocket Bombs** (`PocketBombsPassiveUpgradeData`, formerly "Mini Ordnance") - any qualifying Pixie
   explosion (`OnAreaExplosionDetonated`, never fired from a Mini Bomb's own detonation - see
   `docs/explode-on-destroy.md`) rolls `Chance` to drop a stationary Mini Bomb dealing `DamagePercent`
   of Bunny Bomb's own *base* damage (`PixieAscensionUtility.ResolveBunnyBombDamage` - deliberately
   the un-empowered base value, since Pocket Bombs can trigger off any explosion, not specifically a
   Hot-Fuse-empowered throw). Ranks: Chance 15/25/35%, DamagePercent 35/45/55%. `MiniBombPrototype`/
   `Explosion` still need Editor authoring (see "Current status").
5. **Unstable Mixture** (`UnstableMixturePassiveUpgradeData`) - merges the old standalone Bigger Boom/
   Unstable Mixture/Heavy Payload ascensions. Marked-enemy death explosions (Chain Reaction, see "Base
   passive" below) gain damage and radius; Specialist/Heavy-tier kills get an *additional* fixed +50%
   radius (`TierRadiusMultiplier`, radius-only - `DamageUtility.TryExplodeOnDeath` was fixed to stop
   applying this to damage too), and the line also raises the base passive's own `MaxAffectedTier` gate
   to Heavy so Specialist/Heavy kills can be marked at all (this was Heavy Payload's old role). Ranks:
   BonusDamageMultiplier +30/60/90%, BonusRadiusMultiplier +15/30/40%.
6. **Unstable Targeting** (`UnstableTargetingPassiveUpgradeData`) - bonus damage against any enemy
   currently marked to explode on death (`MarkExplosiveDeath.DamageBonusVsUnstable`, read live in
   `DamageUtility.ResolveOutgoingDamage` - applies to every Pixie damage source, not just Bunny Bomb).
   Ranks: +20/35/50%.
7. **Explosive Rounds** (`ExplosiveRoundsPassiveUpgradeData`) - weapon hits also proc a small
   explosion, reusing the existing Explosive-Sequence weapon-perk pipeline
   (`WeaponSystem.ApplyPixieExplosiveWeapon`, forced to `Interval = 1`) - already a full qualifying
   Pixie explosion (fires `OnAreaExplosionDetonated`/`isExplosion:true`), so it already interacts with
   Pocket Bombs/Direct Hit/Unstable Targeting for free. Recursion-safe: the proc is only ever invoked
   directly from the original hitscan/projectile fire path (gated to pellet 0 of a volley), never
   re-entered from `OnWeaponHitLanded`, so its own explosion damage can't re-trigger itself. Ranks:
   DamageMultiplier 20/30/40%, Radius 2/2.4/2.4.
8. **Backblast** (`BackblastSkillAction`, Dash Ascension, `Phase = Begin | End`) - the dash itself is
   offensive: drops a bomb at the dash's start (every rank) and, from rank 2, also at the dash's end -
   **a dropped bomb with a short fuse, not an instant blast** (reworked 2026-08-09; see "Reworked from
   instant explosion" below). Each bomb spawns via `SpawnedEntitySpawner.Spawn` (same call Pocket Bombs
   already uses), carries `ExplodeOnDestroy{Damage, Explosion, TriggersSpawnUpgrades: true}`, and
   detonates through `AreaHitData.Detonate`'s full path once its fuse runs out - a genuine, full
   qualifying Pixie explosion (`OnAreaExplosionDetonated` fires, Direct Hit's proximity bonus applies,
   normal Chain Reaction marking already works). Damage is `DamagePercent` of Bunny Bomb's *base*
   damage (`ResolveBunnyBombDamage` - a dropped bomb, not a thrown one, so there's no "the throw" to
   inherit Hot Fuse's bonus from). At rank 3, the spawned bomb also gets the generic `ForceMarkOnDetonate`
   tag, guaranteeing every enemy it hits marks for Chain Reaction regardless of the base passive's own
   tier-gate/chance roll (folded in from the old standalone Volatile Escape ascension - see
   `ForceMarkOnDetonate.qtn`). `BombPrototype`/`Explosion` still need Editor authoring (see "Current
   status") - `DashBomb.prefab`/its own `AreaHitData` (`docs/explode-on-destroy.md`) is an existing
   reference prototype this can point straight at. Ranks: DamagePercent 50/50/75%, End-phase bomb from
   rank 2, guaranteed marking at rank 3.
9. **Hot Fuse** (`HotFuseSkillAction`, Dash Ascension, `Phase = Begin`) - Pixie's second Dash path,
   mechanically distinct from Backblast: instead of the dash itself being offensive, the dash sets up
   a stronger *next* Bunny Bomb. See its own section below.

## Hot Fuse

Dash grants a short-lived charge (`PixieHotFuseCharge`) that empowers only the *next* Bunny Bomb throw
within `Window` (3s) - not a timed buff applied to every throw for the next few seconds. Re-dashing
before throwing just refreshes the charge to whatever rank granted it. If Bunny Bomb isn't thrown in
time, `PixieHotFuseTimerSystem` (same shape as `ExplodeOnDeathTimerSystem`) ticks `Remaining` down and
removes the unused charge - no "immortal charge" cleanup gap.

- **Rank 1**: +30% damage on the next throw.
- **Rank 2**: +30% damage, +30% explosion radius.
- **Rank 3**: +60% damage total, +30% radius, and the empowered bomb detonates instantly **on a direct
  enemy hit**.

Consumption is split across two points, since damage/instant-detonate need to be locked in at throw
time but radius is only resolved later, at the bomb's own detonation:

- **`ProjectileSkillData.Fire`** (throw time, gated to `SkillSlotId.HeroSkill` so this can never touch
  any other projectile skill) - multiplies this specific throw's damage by `DamageMultiplier`, and if
  `InstantDetonate` is set, grants the existing `InstantDetonate` tag onto the owner.
- **`AreaHitData.Detonate`** (this bomb's own detonation, whichever path resolves it - direct hit or
  planted-fuse expiry) - a new optional `radiusMultiplier` parameter (default 1, only ever passed a
  real value by `ExplodeOnDestroyUtility.TryDetonate`) multiplies `RadiusMultiplier` in, then the charge
  and the `InstantDetonate` tag are both removed - both were only ever meant for this one throw.

Only one Bunny Bomb can be in flight from one caster at a time (`ProjectileSkillData.Tick` blocks a
re-throw until the current one resolves), so reading/consuming the charge at Detonate time correctly
scopes to the throw that consumed it at Fire time in the overwhelming common case - a player dashing
again mid-flight to refresh the charge before the first bomb lands is a narrow, low-stakes edge case,
not specifically guarded against.

**A real pre-existing bug was fixed to make rank 3 safe**: `InstantDetonate`'s shared logic
(`ProjectileHitData.ShouldDetonate`) used to make a bomb detonate on *any* contact - including bare
ground - once the tag was present, which would have skipped `ProjectileSystem.TryPlant` (the landing
hook Birthday Cake depends on) entirely. It's now scoped to only override `DetonateOnEnemyHit` - a
ground/geometry hit is completely unaffected, so a bomb that lands instead of hitting an enemy still
plants and runs its normal fuse behavior, Birthday Cake's taunt included, exactly as if Hot Fuse
weren't equipped at all. Hot Fuse's damage/radius bonuses still apply either way, since `Fire`/
`Detonate` apply them unconditionally, independent of whether instant-detonation actually triggered.

**Interactions** (all verified, no dedicated interaction code needed beyond the fix above):
- **+ Backblast**: both are independent `SkillSlot.Upgrades` entries on the same Dash slot - one dash
  activation invokes both `Execute` calls in the same `Begin` phase automatically, no coordination
  code needed. Backblast's own damage always reads the *base* `ResolveBunnyBombDamage` (a dash
  explosion isn't a bomb throw), so it's entirely unaffected by an active Hot Fuse charge.
- **+ Direct Hit**: stacks multiplicatively as normal - Hot Fuse scales the thrown `Damage` before the
  projectile spawns, Direct Hit scales the resolved `damage` per-target inside the explosion's own
  radius loop; both apply to the same eventual number, once each.
- **+ Cluster Bomb**: bomblet damage is derived from the parent explosion's actual `damage` parameter
  (already Hot-Fuse-scaled once, if applicable), not from a fresh `ResolveBunnyBombDamage` call - so
  Hot Fuse's bonus flows into bomblet damage exactly once, never twice.
- **+ Birthday Cake**: see the `ShouldDetonate` fix above - Birthday Cake's landing/taunt sequence
  always runs untouched; Hot Fuse's damage/radius bonuses still apply on top of it.

## Base passive — Chain Reaction

Unchanged mechanism (`ChainReactionPassiveData` grants `MarkExplosiveDeath`, marking a qualifying
explosion's target to explode on death - see `DamageUtility.TryMarkExplodeOnDeath`/`TryExplodeOnDeath`).
**A real authoring bug was fixed**: `PixieBaseSkill.asset`'s `HeroSkill.Actions` list contained a
dangling GUID that resolved to **Max's** `MarkExplosiveDeathSkillAction` sub-asset ("Explosive Death"),
so every Pixie player was unconditionally marking every enemy they hit with anything, with no
tier/chance/explosion gate at all - the base Chain Reaction passive already covers this ground
correctly (gated), so the fix was simply removing that entry; there was never a second, intentionally-
authored "Explosive Death" ascension to merge. `PixieAscensionAssetGenerator` fully replacing
`PixieBaseSkill.Actions` (rather than appending) is what makes this fix stick on every re-run.

## Removed / merged (do not re-add as standalone lines)

- **Bomb Radius Up, Instant Detonate, Fireworks** - all 3 were baseline, always-on Bunny Bomb behaviors
  (`Activated: 1`, never actually offerable), deleted entirely at the user's explicit confirmation -
  Bunny Bomb is a plain thrown/planted bomb again; all remaining behavior comes from the 9 lines above.
- **Concussive Force** → folded into Direct Hit rank 3.
- **Bigger Boom, Heavy Payload** → folded into Unstable Mixture.
- **Volatile Escape** → folded into Backblast rank 3 (now via the generic `ForceMarkOnDetonate` tag
  granted onto the specific bomb, rather than the deleted `MarkExplosiveDeath.DashExplosionBypassesTierGate`
  field - see "Backblast reworked from instant explosion" below).
- **Volatile Payload** → dropped entirely, not merged anywhere.
- **Perfect Chain** → deleted (was never wired into the live pool by any generator to begin with).
- **Slow Fuse, "Leave Explosive Bomb"** → removed from the Dash Ascension pool (map to none of the 9
  approved lines - the brief's own math, 9 × 3 = 27 total rank-acquisitions, only works if nothing
  else is offerable). `DashBomb.prefab` (the reference `ExplodeOnDestroy` prototype "Leave Explosive
  Bomb" would have used) stays in place unreferenced - see `docs/explode-on-destroy.md`.

## Backblast reworked from instant explosion (2026-08-09)

Backblast originally called `HitEffectUtility.ApplyExplosion` directly for an instant blast at the
dash's start/end. It was reworked to drop a fused bomb instead (`SpawnedEntitySpawner.Spawn` +
`ExplodeOnDestroy{TriggersSpawnUpgrades: true}`, the same shape Pocket Bombs' Mini Bomb already uses),
so the bomb detonates through `AreaHitData.Detonate`'s real, unabridged path a short moment later
rather than instantly - see the per-line entry above for the current design.

This removed the last live setter of `isDashExplosion`, a parameter that used to thread through
`DamageUtility.ApplyDamage`/`TryMarkExplodeOnDeath` and `HitEffectUtility.ApplyDamageInRadius`/
`ApplyExplosion` purely so Backblast's instant blast could flag itself for `MarkExplosiveDeath`'s
now-deleted `DashExplosionBypassesTierGate` bypass. Since a bomb-type detonation (`AreaHitData.Detonate`
→ `HitEffectUtility.ApplyInRadius`) never had an `isDashExplosion` path to begin with, keeping the old
mechanism wasn't an option once Backblast became bomb-based - `isDashExplosion` was removed entirely
(all 4 signatures) rather than left as unused dead weight, and rank 3's guaranteed marking was rebuilt
as a new generic, hero-agnostic component instead: **`ForceMarkOnDetonate`** (`ForceMarkOnDetonate.qtn`)
- a tag granted onto the *specific spawned bomb entity* (not the owner), checked by
`ExplodeOnDestroyUtility.TryDetonate` right after a `TriggersSpawnUpgrades: true` detonation, force-
marking every enemy caught in that exact blast for Chain Reaction regardless of tier/chance. Scoping it
to the bomb entity rather than the owner is what keeps it from leaking onto an unrelated explosion the
same Pixie causes (her own Bunny Bomb, a Pocket Bombs drop) - only rank-3 Backblast bombs ever carry it.
`AreaHitData.Detonate`'s public overload was changed from `void` to returning the resolved `FP` radius
specifically so this sweep can reuse the exact area just damaged without recomputing the whole
Unstable-Mixture/Skill-Area/Hot-Fuse multiplier chain a second time - every other caller is free to
ignore the return value.

## Architecture notes

- **Direct Hit's proximity bonus** (and its rank-3 knockback) is resolved inline, per target, inside
  the two shared radius-hit loops - `DemolitionMasteryUtility.ApplyProximityEffects`
  (`Assets/_QuantumUser/Simulation/Systems/Heroes/Pixie/DemolitionMasteryUtility.cs`) computes the
  distance-from-center fraction once. Called from `HitEffectUtility.ApplyInRadius` (bomb-type blasts)
  and `ApplyDamageInRadius` (weapon-perk-type blasts, reached via `ApplyExplosion`). Strictly opt-in
  (`TryGetPointer` on the owner's own `DirectHitUpgrade`) - zero behavior change for every other
  hero/mechanic reaching these same two widely-shared methods.
- **Pocket Bombs** reacts to `OnAreaExplosionDetonated` (`Combat.qtn`), fired once per genuine radius
  blast only from the two *original* explosion sources (`AreaHitData.Detonate` and
  `HitEffectUtility.ApplyExplosion`) - deliberately never from `ExplodeOnDestroyUtility.TryDetonate`,
  which alone is what makes "a Mini Bomb cannot generate another Pocket Bombs drop" true, no
  depth-tracking needed. Consumed by `PixieDemolitionMasterySystem` (now a single-signal system, since
  Volatile Payload's `OnExplosionCriticalHit` handler was removed with it).
- **`PixieAscensionUtility.ResolveBunnyBombDamage`** (`Systems/Heroes/Pixie/`) is the one shared helper
  for "X% of Bunny Bomb damage" - resolves `CharacterData.HeroSkill`'s `ProjectileSkillData.Damage`
  plus any `ProjectileDamageUpgrade` multiplier, returning the BASE value (not run through
  `DamageUtility.ResolveOutgoingDamage` - callers pass `DamagePercent * this` into their own
  `ApplyExplosion`/`ApplyDamage` call, which resolves the full live multiplier stack exactly once).
  Used by Pocket Bombs and Backblast; Cluster Bomb derives from the actual triggering `damage`
  parameter instead (see Hot Fuse's interaction notes above for why that distinction matters).

## Current status

- Code compiles once Quantum's DSL codegen picks up every changed/new/removed `.qtn` file (open the
  Editor, or see CLAUDE.md's "Quantum `.qtn` codegen gotcha" for the headless path) - this pass
  touched `MarkExplosiveDeath.qtn` (renamed/added/removed fields), `ClusterBombUpgrade.qtn`/
  `Heroes/Pixie/DemolitionMastery.qtn` (renamed/changed fields), added `Heroes/Pixie/
  PixieHotFuseCharge.qtn`/`BirthdayCakeUpgrade.qtn`/`ForceMarkOnDetonate.qtn`, and removed
  `BlastRadiusUpgrade.qtn`/`FireworksUpgrade.qtn`/`DecoyOnThrowUpgrade.qtn`.
- `Tools/RiftRaiders/Pixie/Generate Ascension Assets` (also chained into `Generate All Assets`)
  replaces the old `PixieChainReactionAssetGenerator`/`PixieDemolitionMasteryAssetGenerator` pair -
  **every list it touches is now fully replaced, not appended** (`PassiveUpgrades`,
  `PixieBaseSkill.Actions`, `DashSkillUpgrades`), specifically to avoid the append/replace split that
  let the old pair drift out of sync with what was actually live in the first place.
- **Pocket Bombs' `MiniBombPrototype`/`Explosion` and Backblast's `BombPrototype`/`Explosion` both
  still need Editor authoring** - same pre-existing gap every generator-created feature in this
  codebase has: a minimal stationary `EntityPrototype` (`Transform3D` only, no `PhysicsCollider3D`/
  movement data) and an `AreaHitData` asset with a small `BlastRadius` each, neither of which a
  generator can author. `DashBomb.prefab`/its own `AreaHitData` (`docs/explode-on-destroy.md`) is an
  existing reference prototype Backblast specifically can point straight at.
- **Not yet manually verified end-to-end in-Editor** - pick each of the 9 lines to rank 3 across a
  couple of runs, confirm rank 2 is never offered before rank 1 and each line disappears from the pool
  after rank 3, and specifically confirm Backblast's End-phase bomb only drops from rank 2 onward, its
  rank-3 bombs force-mark everything they hit, and Hot Fuse's instant-detonate only triggers at rank 3
  on a direct enemy hit (not on landing).
