# Weapon Perks

`WeaponPerkData` is the roguelite-style modifier a weapon roll (`WeaponGenerator`) or a level-up
pick (`LevelUpUtility`, see `docs/level-up-upgrades.md`) can grant. The full ~35-perk roster
originally sketched in this doc is now implemented as code - every perk class exists under
`Assets/_QuantumUser/Simulation/Assets/Weapon/Perks/`. Read this before touching anything
perk-related; it covers how the system actually works, the mechanisms several perks share, and what
Editor authoring is still needed before any of this can drop or be offered at runtime.

## How a perk works

- `UpgradeData` (see `docs/level-up-upgrades.md`) supplies `Icon`/`DisplayName`/`Rarity`;
  `WeaponPerkData` (`Assets/_QuantumUser/Simulation/Assets/Weapon/Perks/WeaponPerkData.cs`) adds one
  abstract method: `Apply(Frame f, EntityRef owner, Weapon* weapon)`.
- **`Description` is a live-formatted template, not static text** - same
  `Description`/`DescriptionArgs`/`GetFormattedDescription()`/`DescriptionUtility.Format` shape
  `SkillActionData` already uses (`WeaponPerkData.View.cs`). Each concrete perk class overrides
  `protected override object[] DescriptionArgs` to supply the values its own `Description`
  template's `{0}`/`{1}`/... placeholders reference (e.g. `HeavyCaliberWeaponPerkData`'s Description
  is `"{0:+0;-0}% Damage, {1:+0;-0}% Fire Rate"`, filled from its own
  `DamageMultiplier`/`FireRateMultiplier` fields) - retuning a field in the Inspector can never leave
  the card describing the wrong number, since `GetDescription()` (what `UpgradeCardWidget` actually
  reads) recomputes the sentence from those same live fields every time instead of returning a
  hand-typed string.
- A perk **bakes its effect once into `Weapon` (or one of its optional sub-components, see "Weapon
  Perk component split" below)** at equip/grant time - it's never removed and never re-applied on
  its own (only wiped wholesale by a weapon swap, see `WeaponSystem.SeedPerkRoster`). Two roll paths
  reach it: `WeaponGenerator.Roll` (a fresh drop, weighted by `WeaponPerkPoolData`) and
  `WeaponSystem.AddPerk` (a level-up pick or the debug `GrantWeaponPerkCommand`, weighted separately
  by `LevelUpConfig` - see `docs/level-up-upgrades.md`). `Weapon.Perks` (a fixed `[5]` array) only
  records *what* was granted for the UI; the baked fields are the runtime source of truth.
- Several perks need more than a one-shot bake, so three supporting mechanisms exist purely to let
  `Apply` stay that simple:
  1. **Live-read math, not baked absolutes.** Magazine-position perks (Opening Burst/Execution
     Rounds/Final Round/Escalating Rounds) and the shared ramp pool (below) are computed fresh every
     shot in `WeaponSystem` (`ResolveLiveDamage`/`ResolveLiveFireCooldown`) off `Weapon`'s own
     `Ammo`/`MagazineSize` ratio and `RampStacks` - nothing is baked into
     `DamageMultiplier`/`FireCooldownMultiplier` directly, so there's nothing to revert if a build
     changes.
  2. **A shared ramp pool.** Relentless Fire (Rare), Suppressive Cycle (Rare), and Overcharge
     Cycle (Epic) all feed one `Weapon.RampStacks` counter instead of tracking 3 independent ramps -
     each perk's `Apply` only adds its own per-stack bonus and raises `RampMaxStacks`. Stacking more
     than one just makes the one shared ramp stronger/faster. Advances by 1 on every
     `ISignalOnWeaponHitLanded` (fired from `DamageUtility.ApplyDamage` on a landed weapon hit),
     decays to 0 once `TimeSinceFireReleased` (already tracked) exceeds `RampDecayGrace`.
  3. **Two new combat signals** (`Assets/_QuantumUser/Simulation/QTN/Combat.qtn`) for on-kill/on-crit
     perks: `OnEntityKilled`/`OnCriticalHit`/`OnWeaponHitLanded`, dispatched from
     `DamageUtility.ApplyDamage` alongside its existing View-facing events, consumed by the new
     `WeaponPerkReactionSystem` (Killer Instinct, Predator Magazine, Bottomless Momentum, Critical
     Rebound, the ramp pool's own advance).
- Post-impact perks (Ricochet, Split Shot, Quantum Rounds, Explosive Sequence, Cataclysm Round) hook
  into `DirectHitData.ApplyHit`/`ApplyExpire` directly (gated on `projectile->Source ==
  DamageSource.Weapon`, reading the owner's `Weapon`) rather than through a granted owner-side
  component - every one of these effects is a property of *this weapon*, so `Weapon` stays the single
  source of truth for every perk, the same as the original 5.
- For a **Hitscan** weapon, Explosive Sequence/Quantum Rounds/Cataclysm Round apply directly inside
  `WeaponSystem.FireHitscan`/`ApplyHitscanWeaponPerks` (a real hit position is available
  synchronously, no `Projectile` entity needed). Ricochet/Split Shot/Piercing Rounds/Echo
  Chamber/Infinite Echo/Critical Rebound have no meaningful Hitscan equivalent (nothing travels) and
  simply don't apply to one - a documented simplification, not a gap.

## Roster and mechanism

| Rarity | Perk | Class | Mechanism |
|---|---|---|---|
| Common | Heavy Caliber | `HeavyCaliberWeaponPerkData` | Bake (Damage + Fire Rate tradeoff) |
| Common | Rapid Mechanism | `FireRateWeaponPerkData` (existing) | Bake |
| Common | Extended Magazine | `MagazineMultiplierWeaponPerkData` (existing) | Bake |
| Common | Fast Loader | `ReloadSpeedWeaponPerkData` | Bake |
| Common | Long Barrel | `RangeMultiplierWeaponPerkData` | Bake (`Weapon.RangeMultiplier`, feeds `Projectile.MaxTravelDistance` - see "Dynamic projectile range") |
| Common | Precision Barrel | `CriticalChanceWeaponPerkData` (existing) | Bake |
| Common | Hollow Point | `CriticalDamageWeaponPerkData` | Bake |
| Rare | Piercing Rounds | `PiercingRoundsWeaponPerkData` | Bake (`Projectile.RemainingPierces`) |
| Rare | Ricochet | `RicochetWeaponPerkData` | `DirectHitData.TryRicochet` |
| Rare | Double Tap | `DoubleTapWeaponPerkData` | Rolled per shot in `WeaponSystem.Update`, extra shot delayed via `PendingDoubleTapShot` |
| Rare | Opening Burst | `OpeningBurstWeaponPerkData` | Live magazine-position read |
| Rare | Execution Rounds | `ExecutionRoundsWeaponPerkData` | Live magazine-position read |
| Rare | Final Round | `FinalRoundWeaponPerkData` | Live magazine-position read |
| Rare | Killer Instinct | `KillerInstinctWeaponPerkData` | `OnEntityKilled` + live timer |
| Rare | Relentless Fire | `RelentlessFireWeaponPerkData` | Shared ramp pool (damage) |
| Rare | Explosive Sequence | `ExplosiveSequenceWeaponPerkData` | Shot counter + `DirectHitData`/Hitscan proc |
| Rare | Critical Rebound | `CriticalReboundWeaponPerkData` | `OnCriticalHit` reaction |
| Rare | Split Shot | `SplitShotWeaponPerkData` | `DirectHitData.SpawnSplitProjectiles` |
| Rare | Empty Chamber | `EmptyChamberWeaponPerkData` | Magazine-empty hook (`StartReload`) |
| Rare | Escalating Rounds | `EscalatingRoundsWeaponPerkData` | Live magazine-position read |
| Rare | Suppressive Cycle | `SuppressiveCycleWeaponPerkData` | Shared ramp pool (fire rate) |
| Rare | Predator Magazine | `PredatorMagazineWeaponPerkData` | `OnEntityKilled` reaction |
| Rare | Emergency Reload | `EmergencyReloadWeaponPerkData` | Reload-window `CharacterStats` toggle |
| Epic | Overcharge Cycle | `OverchargeCycleWeaponPerkData` | Shared ramp pool (both) |
| Epic | Echo Chamber | `EchoChamberWeaponPerkData` | Pending-echo queue (first 3 shots/magazine) |
| Epic | Bottomless Momentum | `BottomlessMomentumWeaponPerkData` | `OnCriticalHit` reaction |
| Epic | Cataclysm Round | `CataclysmRoundWeaponPerkData` | Last-bullet flag + `DirectHitData`/Hitscan proc |
| Epic | Combat Reboot | `CombatRebootWeaponPerkData` | Magazine-empty hook (`SkillSystem.ReduceCooldown`) |
| Legendary | Infinite Echo | `InfiniteEchoWeaponPerkData` | Pending-echo queue (every shot) |
| Legendary | Quantum Rounds | `QuantumRoundsWeaponPerkData` | Every-hit nearby-enemy damage |
| Rare | Fracture Rounds | `FractureRoundsWeaponPerkData` | `OnWeaponHitLanded` + hit counter (Rift Mark) |
| Rare | Critical Fracture | `CriticalFractureWeaponPerkData` | `OnCriticalHit` reaction (Rift Mark) |
| Rare | Unstable Payload | `UnstablePayloadWeaponPerkData` | Explosion-radius overlap (Rift Mark) |
| Rare | Focused Breach | `FocusedBreachWeaponPerkData` | Same-target contact-time tracking (Rift Mark) |
| Rare | Rift Aftershock | `RiftAftershockWeaponPerkData` | `OnEntityKilled` + nearest-enemy transfer (Rift Mark) |

`Min Kill Tier` (the original table's last column) was dropped entirely per design direction - every
on-kill perk (Killer Instinct, Predator Magazine) triggers on any kill, no tier gate.

## Rift Mark content pool

5 perks added in the same pass that built the Rift Mutation half (`docs/rift-mutations.md`) of the
Rift Mark application content pool - see `docs/elemental-reactions.md` for what Rift Mark itself is.
None of these fit the `HitEffectData`-list pattern `BurnEffectData`/`SlowEffectData`/
`RiftMarkEffectData` use (`Weapon` has no such list, and every one of these needs a *conditional*
per-hit check, not an unconditional per-asset effect) - each calls
`StatusEffectUtility.ApplyRiftMark`/`RiftMarkApplicationUtility.ApplyRequest` directly from
perk-reaction code instead, gated by its own baked flag (now on `WeaponHitTrackingPerks`/
`WeaponOnCritReactions`/`WeaponOnKillReactions`, see "Weapon Perk component split" below), the same
shape `DirectHitData.ApplyQuantumRounds`/`WeaponSystem.ApplyHitscanWeaponPerks`'s own Quantum Rounds
branch already uses for "conditionally call a status/damage utility from perk-consuming code."

- **Fracture Rounds** - `WeaponHitTrackingPerks.FractureHitCounter` increments in
  `WeaponPerkReactionSystem.OnWeaponHitLanded` (the same signal the shared ramp pool advances on -
  already excludes DoT-tick replays/non-weapon sources), a genuine confirmed-hit counter, not a
  shots-fired one like `ShotsSinceExplosiveProc`. `OnWeaponHitLanded` gained a `target` parameter
  (`Combat.qtn`) to support this - the ramp pool itself still only reads `owner`.
- **Critical Fracture** - `WeaponPerkReactionSystem.OnCriticalHit`, shares
  `RiftMarkCooldownKey.CriticalFracture` with the Rift Mutation of the same name so the two can never
  both stack from one crit (see `docs/rift-mutations.md`'s "Application/dedup architecture").
- **Unstable Payload** - hooks the two existing weapon-proc `HitEffectUtility.ApplyExplosion` call
  sites (`DirectHitData.ApplyTerminalWeaponPerks`, `WeaponSystem.ApplyHitscanWeaponPerks`) via a new
  `WeaponPerkUtility.TryApplyUnstablePayloadMarks` - runs its own overlap query over the same
  center/radius the explosion's own damage already used, marking every enemy caught once each (no
  cooldown needed - one explosion's blast loop only visits each target once by construction).
- **Focused Breach** - simulates "beam contact" as continuous same-target Hitscan hits, since this
  project has no dedicated Beam fire type. `WeaponHitTrackingPerks.FocusedBreachTarget`/
  `FocusedBreachContactTime` are runtime state tracked in `WeaponSystem.FireHitscan`'s
  hit-confirmed/missed branches (only pellet 0 of a volley tracks it, same "one beam, not N"
  reasoning Explosive Sequence/Cataclysm Round's own pellet gating uses) - losing contact (a miss, or
  the hit entity changing) resets progress.
- **Rift Aftershock** - `WeaponPerkReactionSystem.OnEntityKilled`, transfers to the nearest other
  valid enemy via `WeaponPerkUtility.TryFindNearestEnemy` within a new dedicated
  `ElementalReactionConfig.RiftAftershockRadius` (deliberately not reusing `SingularityRadius`, which
  has its own live reaction consumer). That utility gained a `Phase != Dead`/non-`Invulnerable` guard
  in the same pass - it could previously select a lingering-dead or invulnerable enemy, a real edge
  case a kill-reaction perk hits constantly.

## Element Infusion

`ElementInfusionWeaponPerkData` grafts an **extra** on-hit element onto the weapon, on top of - never
replacing - the weapon's own `WeaponDataAsset.Element`. The native element keeps flowing through
`Projectile.Element` and rolls the owner's shared `CharacterStats.ElementalChance` exactly as before;
the infused element carries its **own** authored `ProcChance` and rolls independently, so a Neutral
weapon gains an element and an already-elemental weapon can land two statuses side by side on one hit.

Storage/flow deliberately mirrors the native element one channel over:
- Baked once by `Apply` into a new optional `WeaponElementInfusion` component (`Element` + `ProcChance`),
  removed on every re-equip in `WeaponSystem.SeedPerkRoster` like every other perk cluster.
- Carried to impact via two new `Projectile` fields (`PerkElement`/`PerkElementChance`), seeded in
  `WeaponSystem.ApplyProjectilePerks` - the single post-spawn seeding point both projectile fire sites
  already funnel through. Hitscan has no projectile, so `WeaponSystem.FireHitscan` reads the component
  live instead.
- Applied through a new `StatusEffectUtility.TryApplyInfusedElement` - same Fire→Burn/Ice→Slow/
  Rock→Intimidate baseline (now extracted into a shared `ApplyElementBaseline` the native path also
  calls) plus the same Rift Mark reaction, but rolled against the perk's `ProcChance` and with **no**
  guaranteed-burn pass (that's owner-global and already ran on the native-element call - running it
  twice would double it). Called right after the native-element application in
  `HitEffectUtility.ApplyToTarget` (projectile/area hits) and `WeaponSystem.FireHitscan` (hitscan),
  sharing the same `PreHitRiftMarkStacks` snapshot so at most one Rift Mark reaction still fires per
  hit (the native call's, if it landed one - the infused call's consume then hits the live reaction
  lockout it set).

**Only one infused element per weapon**: a second Element Infusion perk last-wins, overwriting both
fields (chosen over a multi-element array to avoid bloating the hot `Projectile` component). Area hits
(`ApplyInRadius`/`Shape`/`Collider`) build their context with `PerkElement` defaulting to Neutral, so
they no-op for free - the infused element reaches only the direct projectile/hitscan hit, same reach
the native `WeaponDataAsset.Element` already had.

`WeaponPerkAssetGenerator` authors one infusion `.asset` per real element (Incendiary/Cryo/Shatter/
Void/Shock Rounds - Neutral excluded) and wires them into `WeaponPerkPoolData` alongside the rest.

## Design decisions made while implementing

- **Ramp perks share one counter** rather than tracking 3 independent ramps (see above) - confirmed
  design direction, so equipping more than one strengthens the shared ramp instead of stacking
  separate timers.
- **Echo Chamber repeats the first 3 shots of every magazine**, resetting each reload - consistent
  with the other magazine-relative perks, not a one-time-ever effect.
- Fields contributed by more than one perk (e.g. `RampDecayGrace`, `EchoDelay`) take the largest
  value any equipped contributor asks for via `FPMath.Max`, not a sum - so combining perks can't
  accidentally make a shared timing constant faster/slower than any single perk intends.
- **Double Tap's extra shot is offset by `DoubleTapDelay` (default 0.1s, `WeaponPerkAssetGenerator`)
  instead of firing the same tick as the primary shot** - added so the two are audibly/visibly two
  separate shots rather than one instantaneous double-damage burst. Queued into
  `WeaponFireTimeMods.PendingDoubleTap` (a single slot, unlike `WeaponEchoState.PendingEchoes`' `[3]`
  - Double Tap only ever queues one extra shot per primary shot) and ticked down/fired in
  `WeaponSystem.Update`/`TickPendingDoubleTap`, same FP-seconds countdown `TickPendingEchoes` uses. A
  second proc landing while one is already pending is silently dropped rather than replacing it -
  same "don't stall/replace the older one" precedent `EnqueueEcho` already uses for its own queue -
  acceptable since `DoubleTapDelay` is meant to stay well under the weapon's own fire cooldown.

## Dynamic projectile range

A `Projectile`-type weapon's shots now travel exactly as far as `WeaponDataAsset.Range *
Weapon.RangeMultiplier` (`WeaponPerkUtility.ResolveWeaponRange`) - the same distance `FireHitscan`
already limited its raycast to - instead of whatever `ProjectileDataAsset.MaxDistance` happened to be
hand-authored on that projectile's shared asset (previously a completely separate, easy-to-forget-to-
tune number; a Projectile weapon with an un-set `MaxDistance` had effectively infinite range until
`RemainingLifetime` ran out). A new `Projectile.MaxTravelDistance` field carries this per-shot
absolute cap; `ProjectileSystem.TryExpire` checks it first and only falls back to the old
`MaxDistance * MaxDistanceMultiplier` math when it's `<= 0` - i.e. for anything not fired by a weapon
(skills, enemy attacks), which are untouched by this change.

Every weapon-fire spawn site bakes the same `ResolveWeaponRange` value onto the projectile it spawns,
so a bullet spawned mid-flight by another perk can't outrun the weapon that ultimately fired it:

- `WeaponSystem.ApplyProjectilePerks` - the primary shot, Double Tap's delayed replay, and every Echo
  Chamber/Infinite Echo repeat all route through this one call site.
- `WeaponPerkReactionSystem.TryFireCriticalRebound` - previously spawned with no
  `MaxDistanceMultiplier`/pierce/bounce seeding at all (a pre-existing gap); now at least gets the
  same range cap.
- `DirectHitData.SpawnSplitProjectiles` - previously halved the parent weapon's range
  (`SplitShotRangeFraction = 0.5`, enforced as an ad hoc `RemainingLifetime` clamp) specifically so a
  fragment couldn't out-range its parent weapon. That halving is gone - a split child now gets the
  *same* full-range cap as everything else, per explicit design direction (consistency over the old
  "half range" balancing choice).
- Ricochet doesn't spawn a new entity (`DirectHitData.TryRicochet` just redirects the existing
  projectile's `Velocity`), so it already carries forward whatever `MaxTravelDistance`/
  `TraveledDistance` that entity already had - nothing to change there.

`Projectile.MaxDistanceMultiplier` still exists (read by the fallback branch above), but nothing sets
it anymore - `ApplyProjectilePerks` used to bake `Weapon.RangeMultiplier` into it, now it bakes the
fully-resolved `MaxTravelDistance` instead. Any `MaxDistanceMultiplier` value still authored on a
weapon-projectile `EntityPrototype` from before this change is now inert (superseded, not read) - the
field remains functional for the actual non-weapon projectiles that still fall back to it.

## Weapon Perk component split

`component Weapon` originally held every perk's baked fields directly - it grew to 73 flat fields as
the roster filled out, which was enough for its auto-generated `GetHashCode()`/serializer to blow
past clang's bracket-nesting limit compiling `Quantum.Simulation` for IL2CPP/Android. Since a weapon
can equip at most 5 of ~18 perks that touch these fields at once (`Weapon.Perks` is `array[5]`), most
of those fields sat unused/zeroed on every weapon anyway - wasted per-entity memory and wasted bytes
in every network/replay/checksum snapshot (Quantum serializes the whole component every tick).

The fix: perk-specific state now lives on 8 small **optional** components in `WeaponPerks.qtn`,
added via `f.AddOrGet<T>` only when a perk that needs them is actually granted, removed
unconditionally in `WeaponSystem.SeedPerkRoster` on every re-equip - a missing component means
exactly what a zeroed field used to mean ("this perk cluster wasn't rolled"). Only 13 always-present
fields (`WeaponData`/`Perks`/`MagazineSize`/`ReloadDuration`/`CriticalChance`/
`CriticalDamageBonus`/`DamageMultiplier`/`FireCooldownMultiplier`/`FireCooldownTimer`/`Ammo`/
`ReloadTimer`/`TimeSinceFireReleased`/`RangeMultiplier`) remain on `Weapon` itself.

| Component | Perks it serves |
|---|---|
| `WeaponMagazinePositionPerks` | Opening Burst, Execution Rounds, Final Round, Escalating Rounds |
| `WeaponRampState` | Relentless Fire, Suppressive Cycle, Overcharge Cycle (the shared ramp pool - mandatory single component, all 3 feed it via `FPMath.Max`/SUM) |
| `WeaponEchoState` | Echo Chamber, Infinite Echo (mandatory single component - both drive the same shared `EchoDelay`/`PendingEchoes` queue) |
| `WeaponFireTimeMods` | Piercing Rounds, Ricochet, Double Tap (also holds its own `PendingDoubleTap` single-slot queue, ticked alongside `WeaponEchoState`'s) |
| `WeaponPostImpactProcs` | Split Shot, Quantum Rounds, Explosive Sequence, Cataclysm Round |
| `WeaponReloadHooks` | Empty Chamber, Combat Reboot, Emergency Reload |
| `WeaponOnKillReactions` | Predator Magazine, Killer Instinct, Rift Aftershock |
| `WeaponOnCritReactions` | Bottomless Momentum, Critical Rebound, Critical Fracture |
| `WeaponHitTrackingPerks` | Fracture Rounds, Unstable Payload, Focused Breach |

`WeaponPerkData.Apply` gained an `EntityRef owner` parameter so a perk can `f.AddOrGet<T>(owner, out
var ptr)` for its own component - 9 perk classes that only ever touch `Weapon`'s core fields
(Heavy Caliber, Rapid Mechanism, Extended Magazine, Fast Loader, Long Barrel, Precision Barrel,
Hollow Point, and the two unnamed original Damage/Cooldown multiplier perks) needed no logic change
beyond the signature. Every consumer site that used to read a field unconditionally (no `Has*` gate -
a value of exactly 0 was what meant "not rolled") now reads through `f.Unsafe.TryGetPointer<T>`
instead, with absence meaning the same "no bonus" as before. `WeaponSystem.ApplyPixieExplosiveWeapon`
(Pixie's Explosive Rounds passive, unrelated to the perk roster but writes into
`WeaponPostImpactProcs`) had to switch to `f.AddOrGet` too, since it runs unconditionally on every
`Equip` regardless of whether an Explosive Sequence/Cataclysm perk was ever rolled.

**Emergency Reload's latch is the one genuinely fragile spot**: `WeaponReloadHooks.
EmergencyReloadApplied` gates a temporary `CharacterStats` bonus add/subtract - `SeedPerkRoster`
calls `RevertEmergencyReload` (already self-guarded on the latch) unconditionally *before* removing
`WeaponReloadHooks` on a weapon swap, same revert-then-remove idiom
`OverdriveDamageSkillAction.End()` already uses, so the bonus can't leak onto a `CharacterStats` that
survives the swap.

## Files

**New QTN**: `Combat.qtn` (`OnEntityKilled`/`OnCriticalHit`/`OnWeaponHitLanded` signals -
`OnWeaponHitLanded` later gained a `target` parameter for Fracture Rounds, see "Rift Mark content
pool" above), `WeaponPerks.qtn` (the 8 optional perk components above, plus the `PendingEcho`/
`PendingDoubleTapShot` structs - see "Weapon Perk component split").
**Edited QTN**: `Weapon.qtn` (trimmed down to its 13 core fields - every perk-specific field moved to
`WeaponPerks.qtn`), `Projectile.qtn`
(`RemainingBounces`/`MaxDistanceMultiplier`/`IsExplosiveProc`/`IsCataclysm`).
**New systems**: `WeaponPerkReactionSystem.cs` (on-kill/on-crit/ramp-advance reactions, registered in
`SystemSetup.User.cs` next to `WeaponSystem`), `WeaponPerkUtility.cs` (shared nearest-enemy query
used by Ricochet/Quantum Rounds/Critical Rebound/Rift Aftershock, plus Unstable Payload's own overlap
helper), `RiftMarkApplicationUtility.cs`/`RiftMutationMarkUtility.cs` (shared with
`docs/rift-mutations.md` - the cooldown-key dedup layer every Rift Mark perk/mutation goes through).
**Edited systems**: `WeaponSystem.cs` (fire-branch live math, Double Tap, echo queue, reload hooks for
Emergency Reload/Empty Chamber/Combat Reboot, Hitscan perk application, Focused Breach contact
tracking, plus the whole component-split cutover - `SeedPerkRoster`, `ApplyPixieExplosiveWeapon`,
every perk-field read site), `DirectHitData.cs` (Ricochet/Split Shot/Quantum Rounds/Explosive
Sequence/Cataclysm Round/Unstable Payload), `ProjectileSystem.cs` (`MaxDistanceMultiplier` in
`TryExpire`), `DamageUtility.cs` (the 3 signal dispatches), `SkillSystem.cs` (`ReduceCooldown`).
**New perk assets**: ~32 new `WeaponPerkData` subclasses under `Assets/Weapon/Perks/`, alongside the
original 5 (27 from the original roster + 5 from the Rift Mark content pool).

## View / presentation

Two generic, source-agnostic simulation entry points (`HitEffectUtility.cs`) each pair one gameplay
effect with one view event - callers don't need their own sphere-overlap loop or their own VFX
hookup, and any future perk/skill needing the same shape (a radial push, or a radial blast with no
bespoke prefab of its own) can reuse them instead of each wiring up a new event:

- **`HitEffectUtility.ApplyShockwave(f, center, radius, owner, knockbackForce, targetMask =
  Enemies)`** - knockback only (`DamageUtility.ApplyKnockback` per target in radius), fires
  `ShockwaveReleased` (`Events.qtn`) unconditionally, even if nothing was caught, so it still reads
  visually against an empty room (same convention `AreaDetonated` uses). Currently only called by
  `WeaponSystem.ApplyMagazineEmptiedPerks` for Empty Chamber.
  `EffectsManager.OnShockwaveReleased` plays a new `shockwaveEffectPrefab` field, falling back to
  `defaultAreaBlastEffect` if unset. **No dedicated shockwave VFX prefab exists in the project yet**
  (checked - nothing under `Assets/_Project/EffectPrefabs/` or elsewhere is a ready-made ring/pulse
  effect), so until one is authored and dragged into that field in the Inspector, Empty Chamber's
  shockwave plays the generic explosion blast instead of a dedicated ring VFX. The existing 3rd-party
  `CFXR Fire Ring`/`ring_shockwave` source art (Cartoon FX Remaster / Epic Toon FX) is the natural raw
  material for assembling one, following `SimpleExplosion.prefab`'s existing assembly pattern.
- **`HitEffectUtility.ApplyExplosion(f, center, radius, owner, damage, source, targetMask =
  Enemies)`** - `ApplyDamageInRadius` plus firing `WeaponExplosionReleased`, for a weapon-perk
  explosion that doesn't want/need its own bespoke VFX. Called by both
  `DirectHitData.ApplyTerminalWeaponPerks` (Projectile fire type) and
  `WeaponSystem.ApplyHitscanWeaponPerks` (Hitscan) for **Cataclysm Round** and **Explosive
  Sequence**. `EffectsManager.OnWeaponExplosionReleased` always plays `defaultAreaBlastEffect`
  directly - no dedicated field, same "no single asset to resolve a bespoke prefab from" reasoning
  `ExplodeOnDeathDetonated` already uses - so both procs get real VFX with zero additional Editor
  authoring needed.
- **`QuantumRoundsTriggered`** (`Events.qtn`) - unlike the two generic events above, this one carries
  a `Source: AssetRef<QuantumRoundsWeaponPerkData>` (baked into a new `WeaponPostImpactProcs.
  QuantumRoundsSource` field by `QuantumRoundsWeaponPerkData.Apply`, same self-referencing-AssetRef
  pattern `GroundPoundUpgrade.Source` uses), so the view resolves a **per-asset** prefab instead of a
  shared one. Fired by both `DirectHitData.ApplyQuantumRounds` (Projectile) and
  `WeaponSystem.ApplyHitscanWeaponPerks` (Hitscan) at the chained-onto enemy's own live position, not
  the original shot's impact point. `EffectsManager.OnQuantumRoundsTriggered` plays
  `QuantumRoundsWeaponPerkData.ImpactEffectPrefab` (see its `.View.cs` partial) at
  `quantumRoundsEffectScale`, falling back to `defaultAreaBlastEffect` if that field is left empty on
  the generated `.asset` - same authoring gap the shockwave bullet above documents, not yet fixed here
  either.

## Asset generation

`Assets/_QuantumUser/Editor/WeaponPerkAssetGenerator.cs` (`Tools > RiftRaiders > Generate Weapon Perk
Assets`, same menu group as `GlobalUpgradeAssetGenerator`) authors one `.asset` instance per class above - tuned to the original design table's
numbers (e.g. Heavy Caliber's `DamageMultiplier`/`FireRateMultiplier` baked to 1.2/0.9) - under
`Assets/_QuantumUser/Resources/Weapon/WeaponPerk/`, and wires all of them into the
`WeaponPerkPoolData.asset` stub that already lives there. Re-running it is safe: existing assets at
the expected path are updated in place (not duplicated), and the pool's `Perks` list is rebuilt from
scratch each run. Each spec's `Description` is a **template** (e.g. `"{0:+0;-0}% Damage,
{1:+0;-0}% Fire Rate"` for Heavy Caliber), not the final card text - it renders through that class's
own `DescriptionArgs` (see "How a perk works" above), so it always reflects whatever
`Configure`/Inspector-tuned values actually ended up on the asset, not just what the generator
happened to set on first run. `Icon` is left unset for every asset - sprites aren't something a
script can author, so that's still a manual per-perk Inspector step. Numbers not specified anywhere
in the original design table (most `Rarity`-tier proc magnitudes - ramp per-stack bonuses, Split
Shot/Explosive Sequence/Cataclysm Round damage multipliers, etc.) got a reasonable placeholder value
in the generator rather than a documented design number - treat those as a starting point for
balance passes, not a final call.

## Current status / known simplifications

Every perk in the roster has a `WeaponPerkData` class and (once `WeaponPerkAssetGenerator` is run)
a tuned `.asset` instance wired into the pool - but `LevelUpConfig.asset` itself still doesn't exist
(see `docs/level-up-upgrades.md`), so a level-up still can't offer any of this yet; a fresh weapon
drop (`WeaponGenerator.Roll`) can, once something actually calls it with this pool.

1. **`LevelUpConfig.WeaponPerkPool` still needs to be pointed at this pool asset** - the generator
   only populates `WeaponPerkPoolData.asset` itself, not `LevelUpConfig`'s reference to it (that
   config doesn't exist yet at all).
2. **Every asset's `Icon` is unset** - needs manual per-perk sprite assignment in the Inspector.
3. **Hitscan weapons don't get Ricochet/Split Shot/Piercing Rounds/Echo Chamber/Infinite
   Echo/Critical Rebound** - nothing travels for these to hook onto. Explosive Sequence/Cataclysm
   Round/Quantum Rounds apply directly at the hit point instead for a Hitscan weapon, so those three
   work on both fire types.
4. **Split Shot's children are bare repeats, not full re-rolls** - a split projectile is spawned
   directly via `ProjectileSpawner.Spawn`, not through `WeaponSystem.FireProjectile`, so it doesn't
   re-apply Piercing Rounds/Ricochet/Explosive Sequence/Cataclysm Round to itself - only the
   recursion cap (`MaxSplitShotDepth`) carries over, so a split-shot weapon can't cascade into a
   second full generation of splits. Children fan across a 90° arc centered on the parent shot's own
   heading at impact (not a full circle), and each gets the same `Projectile.MaxTravelDistance` cap
   (the firing weapon's own `WeaponDataAsset.Range * Weapon.RangeMultiplier`, via
   `WeaponPerkUtility.ResolveWeaponRange`) every other weapon-fired projectile does - see "Dynamic
   projectile range" below and `DirectHitData.SpawnSplitProjectiles`.
5. **Explosive Sequence's shot counter free-runs on Double Tap's extra shot and split-shot
   children** - Double Tap's free shot mirrors the primary shot's already-resolved
   proc flags rather than re-rolling/advancing the counter itself; split children don't touch it at
   all (see #4). It does lose the primary shot's target lock, though (see `FireDoubleTapShot`'s own
   comment) - once delayed via `DoubleTapDelay`, it replays straight down the original
   `SpawnPosition`/`AimDirection` rather than re-solving `Aim.Target`'s aim point, same simplification
   `PendingEcho` already makes for echoed shots.
6. **Rift Mark content pool** - see `docs/rift-mutations.md`'s own "Current status" for the shared
   caveats (no automated coverage for the Frame-dependent half, cross-mechanic dedup scoped to within
   each evaluation point not globally, Focused Breach's contact-time reset-on-miss-only behavior).
7. **Pellet weapons (shotguns)** - `WeaponDataAsset.PelletCount`/`SpreadAngle` fire a cone-spread
   volley from one trigger pull (both `FireHitscan` and `FireProjectile` loop over
   `WeaponSystem.GetPelletAngle`), same convention as the enemy-only `FanProjectileDeliveryData`.
   `Damage` is read PER PELLET, not as the volley's total. Piercing Rounds/Ricochet/Split
   Shot/Quantum Rounds all apply per pellet automatically, since each pellet is its own independent
   `Projectile` entity through the normal spawn/hit pipeline - no special-casing needed. Explosive
   Sequence/Cataclysm Round are the one exception: they only proc off pellet 0 of a volley, so an
   N-pellet shotgun detonates once per trigger pull instead of N times. `PelletCount` of 1 (the
   default) is a no-op for every existing weapon. No `Shotgun.asset` exists yet - author one in the
   Editor by duplicating an existing `WeaponDataAsset` and tuning `PelletCount`/`SpreadAngle`/`Damage`.
8. **Quantum Rounds has a VFX hook, unauthored** - `QuantumRoundsWeaponPerkData.ImpactEffectPrefab`
   (its own `.View.cs` partial) is played on the chained-onto enemy via a new `QuantumRoundsTriggered`
   event/`EffectsManager.OnQuantumRoundsTriggered`, baked through a new `WeaponPostImpactProcs.
   QuantumRoundsSource` self-reference (same pattern `GroundPoundUpgrade.Source` uses) so the view can
   resolve the asset that granted the perk - both the Projectile path (`DirectHitData.
   ApplyQuantumRounds`) and the Hitscan path (`WeaponSystem.ApplyHitscanWeaponPerks`) fire it. Falls
   back to `EffectsManager.defaultAreaBlastEffect` at `quantumRoundsEffectScale` until a bespoke
   particle is assigned to the generated `QuantumRoundsWeaponPerkData.asset`.
