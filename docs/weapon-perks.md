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
  abstract method: `Apply(Frame f, Weapon* weapon)`.
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
- A perk **bakes its effect once into `Weapon`'s own fields** at equip/grant time - it's never
  removed and never re-applied. Two roll paths reach it: `WeaponGenerator.Roll` (a fresh drop,
  weighted by `WeaponPerkPoolData`) and `WeaponSystem.AddPerk` (a level-up pick or the debug
  `GrantWeaponPerkCommand`, weighted separately by `LevelUpConfig` - see `docs/level-up-upgrades.md`).
  `Weapon.Perks` (a fixed `[5]` array) only records *what* was granted for the UI; the baked fields
  below are the runtime source of truth.
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
| Common | Long Barrel | `RangeMultiplierWeaponPerkData` | Bake (+ `Projectile.MaxDistanceMultiplier`) |
| Common | Precision Barrel | `CriticalChanceWeaponPerkData` (existing) | Bake |
| Common | Hollow Point | `CriticalDamageWeaponPerkData` | Bake |
| Rare | Piercing Rounds | `PiercingRoundsWeaponPerkData` | Bake (`Projectile.RemainingPierces`) |
| Rare | Ricochet | `RicochetWeaponPerkData` | `DirectHitData.TryRicochet` |
| Rare | Double Tap | `DoubleTapWeaponPerkData` | Rolled per shot in `WeaponSystem.Update` |
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

`Min Kill Tier` (the original table's last column) was dropped entirely per design direction - every
on-kill perk (Killer Instinct, Predator Magazine) triggers on any kill, no tier gate.

## Design decisions made while implementing

- **Ramp perks share one counter** rather than tracking 3 independent ramps (see above) - confirmed
  design direction, so equipping more than one strengthens the shared ramp instead of stacking
  separate timers.
- **Echo Chamber repeats the first 3 shots of every magazine**, resetting each reload - consistent
  with the other magazine-relative perks, not a one-time-ever effect.
- Fields contributed by more than one perk (e.g. `RampDecayGrace`, `EchoDelay`) take the largest
  value any equipped contributor asks for via `FPMath.Max`, not a sum - so combining perks can't
  accidentally make a shared timing constant faster/slower than any single perk intends.

## Files

**New QTN**: `Combat.qtn` (`OnEntityKilled`/`OnCriticalHit`/`OnWeaponHitLanded` signals).
**Edited QTN**: `Weapon.qtn` (every new bake/runtime field above, plus the `PendingEcho` struct),
`Projectile.qtn` (`RemainingBounces`/`MaxDistanceMultiplier`/`IsExplosiveProc`/`IsCataclysm`).
**New systems**: `WeaponPerkReactionSystem.cs` (on-kill/on-crit/ramp-advance reactions, registered in
`SystemSetup.User.cs` next to `WeaponSystem`), `WeaponPerkUtility.cs` (shared nearest-enemy query
used by Ricochet/Quantum Rounds/Critical Rebound).
**Edited systems**: `WeaponSystem.cs` (fire-branch live math, Double Tap, echo queue, reload hooks for
Emergency Reload/Empty Chamber/Combat Reboot, Hitscan perk application), `DirectHitData.cs`
(Ricochet/Split Shot/Quantum Rounds/Explosive Sequence/Cataclysm Round), `ProjectileSystem.cs`
(`MaxDistanceMultiplier` in `TryExpire`), `DamageUtility.cs` (the 3 new signal dispatches),
`SkillSystem.cs` (`ReduceCooldown`).
**New perk assets**: ~27 new `WeaponPerkData` subclasses under `Assets/Weapon/Perks/`, alongside the
original 5.

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
   second full generation of splits.
5. **Explosive Sequence's shot counter free-runs on Double Tap's extra shot and split-shot
   children** - Double Tap's free shot mirrors the primary shot's already-resolved
   proc flags rather than re-rolling/advancing the counter itself; split children don't touch it at
   all (see #4).
6. **Pellet weapons (shotguns)** - `WeaponDataAsset.PelletCount`/`SpreadAngle` fire a cone-spread
   volley from one trigger pull (both `FireHitscan` and `FireProjectile` loop over
   `WeaponSystem.GetPelletAngle`), same convention as the enemy-only `FanProjectileDeliveryData`.
   `Damage` is read PER PELLET, not as the volley's total. Piercing Rounds/Ricochet/Split
   Shot/Quantum Rounds all apply per pellet automatically, since each pellet is its own independent
   `Projectile` entity through the normal spawn/hit pipeline - no special-casing needed. Explosive
   Sequence/Cataclysm Round are the one exception: they only proc off pellet 0 of a volley, so an
   N-pellet shotgun detonates once per trigger pull instead of N times. `PelletCount` of 1 (the
   default) is a no-op for every existing weapon. No `Shotgun.asset` exists yet - author one in the
   Editor by duplicating an existing `WeaponDataAsset` and tuning `PelletCount`/`SpreadAngle`/`Damage`.
