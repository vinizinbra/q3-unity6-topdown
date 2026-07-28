# Weapon Perks

`WeaponPerkData` is the roguelite-style modifier a weapon roll (`WeaponGenerator`) or a level-up
pick (`LevelUpUtility`, see `docs/level-up-upgrades.md`) can grant. This doc has two halves: how the
system actually works today (5 perk types, all flat stat multipliers), and the full ~35-perk design
target discussed for expanding the pool - most of which needs new hooks that don't exist yet. Read
this before authoring new `WeaponPerkData` assets or adding perk-adjacent systems.

## How a perk works today

- `UpgradeData` (see `docs/level-up-upgrades.md`) supplies `Icon`/`DisplayName`/`Rarity`;
  `WeaponPerkData` (`Assets/_QuantumUser/Simulation/Assets/Weapon/Perks/WeaponPerkData.cs`) adds one
  abstract method: `Apply(Frame f, Weapon* weapon)`.
- A perk **bakes its effect once into `Weapon`'s own fields** - it's never removed and never
  re-applied, so nothing re-derives it per tick. `Weapon.Perks` (`Weapon.qtn`, a fixed `[5]` array)
  only exists so the UI can name/compare what a roll actually contains; the baked stats are the
  source of truth at runtime.
- **Two ways a perk reaches a weapon:**
  - `WeaponGenerator.Roll` - a fresh drop. Weighted draw without replacement off a
    `WeaponPerkPoolData` (its own `Common/Uncommon/Rare/Epic/LegendaryWeight`), then
    `WeaponSystem.Equip` re-seeds every stat from `WeaponDataAsset` and calls every drawn perk's
    `Apply` in order.
  - `WeaponSystem.AddPerk` - granted after the fact (a level-up pick, or the debug
    `GrantWeaponPerkCommand`). Fills the first empty `Perks` slot and calls `Apply` immediately
    against the weapon's *current* (already-baked) stats - order-dependent, same as a fresh roll,
    just spread over time instead of upfront.
  - Level-up rolling reuses `WeaponPerkPoolData.Perks` as its candidate list but weights every
    candidate through `LevelUpConfig`'s own separate weight table, not the pool's - deliberately
    different tuning knobs for "found a weapon" vs. "picked at level-up," see
    `docs/level-up-upgrades.md`.
- **Which `Weapon` fields a perk can touch today:** `MagazineSize`, `ReloadDuration`,
  `CriticalChance`, `CriticalDamageBonus` (baked absolutes, seeded from `WeaponDataAsset` at equip),
  and `DamageMultiplier`/`FireCooldownMultiplier` (standing multipliers - `WeaponSystem.Update`
  reads `WeaponDataAsset.Damage`/`FireCooldownTime` fresh every shot and scales by these, so tuning
  the base asset value takes effect immediately without a re-equip). There is no per-shot, per-kill,
  per-crit, or per-magazine-position state on `Weapon` at all right now.
- **Only 5 concrete perk assets exist**, one file each in
  `Assets/_QuantumUser/Simulation/Assets/Weapon/Perks/`:

| Class | Field | Effect |
|---|---|---|
| `DamageMultiplierWeaponPerkData` | `Multiplier` | `DamageMultiplier *= Multiplier` |
| `FireRateWeaponPerkData` | `Multiplier` | `FireCooldownMultiplier /= Multiplier` (shots/sec scales up) |
| `CooldownMultiplierWeaponPerkData` | `Multiplier` | `FireCooldownMultiplier *= Multiplier` (same field, other direction - whichever reads naturally when authoring) |
| `MagazineMultiplierWeaponPerkData` | `Multiplier` | `MagazineSize` scaled, floored to 1 |
| `CriticalChanceWeaponPerkData` | `Chance` | `CriticalChance += Chance` |

No `WeaponPerkPoolData` asset instance exists yet either - the pool referenced by
`LevelUpConfig.WeaponPerkPool` (see `docs/level-up-upgrades.md`) still needs to be authored and
populated before any of these 5 can actually drop or be offered.

## Designed roster (target design, not yet implemented)

Below is the full perk list under discussion, organized by rarity. Nothing in this table beyond the
5 perks above has a `WeaponPerkData` class yet - treat this as the spec to build against, not
current behavior.

### Common

| Perk | Effect |
|---|---|
| Heavy Caliber | +20% Damage, -10% Fire Rate |
| Rapid Mechanism | +15% Fire Rate |
| Extended Magazine | +25% Magazine |
| Fast Loader | +20% Reload Speed |
| Long Barrel | +20% Weapon Range |
| Precision Barrel | +8% Crit Chance |
| Hollow Point | +25% Crit Damage |

### Uncommon

| Perk | Effect | Min Kill Tier |
|---|---|---|
| Piercing Rounds | +1 Pierce | — |
| Ricochet | Bounce once | — |
| Double Tap | 15% chance to fire an extra projectile | — |
| Opening Burst | First 20% of magazine: +25% Fire Rate | — |
| Execution Rounds | Last 20% of magazine: +30% Damage | — |
| Final Round | Last bullet deals +100% Damage | — |
| Killer Instinct | +15% Fire Rate for 2s after kill | Normal |
| Relentless Fire | Consecutive hits increase damage | — |

### Rare

| Perk | Effect | Min Kill Tier |
|---|---|---|
| Explosive Sequence | Every 5th shot explodes | — |
| Critical Rebound | Crits fire a secondary projectile | — |
| Split Shot | Projectile splits after impact | — |
| Empty Chamber | Empty magazine releases a shockwave that knocks back enemies | — |
| Escalating Rounds | Damage increases through the magazine | — |
| Suppressive Cycle | Fire Rate increases while continuously firing | — |
| Predator Magazine | Restore 10% magazine on kill | Specialist |
| Emergency Reload | Gain Move Speed and Damage Reduction while reloading | — |

### Epic

| Perk | Effect |
|---|---|
| Overcharge Cycle | Continuous fire builds Damage and Fire Rate |
| Echo Chamber | First 3 shots repeat after a delay |
| Bottomless Momentum | Crits can restore ammo |
| Cataclysm Round | Final shot becomes a massive explosive projectile |
| Combat Reboot | Emptying the magazine reduces Hero Skill cooldown |

### Legendary

| Perk | Effect |
|---|---|
| Infinite Echo | Every projectile repeats once |
| Quantum Rounds | Hits damage an additional nearby enemy |

## What each tier actually needs to be built

The 5 shipped perks all fit the existing "bake into `Weapon` at equip" model because they're flat,
unconditional stat edits. Most of the designed roster is not that shape - grouped by what's missing:

- **Fits the current model as-is** - the 7 Common-tier perks above: same shape as the 5 that already
  exist (Heavy Caliber/Rapid Mechanism/Extended Magazine/Fast Loader are direct analogues of the
  existing multiplier perks; Long Barrel needs `Weapon` to carry a range multiplier read by
  `WeaponSystem.FireHitscan`'s `Range` and `ProjectileDataAsset.MaxDistance`; Precision Barrel/Hollow
  Point are direct analogues of `CriticalChanceWeaponPerkData`, just also need a
  `CriticalDamageMultiplier`-style perk for the existing `CriticalDamageBonus` field).
- **Cheaper than it looks - one new baked stat, no new hook:** Piercing Rounds. Precedent already
  exists: `Projectile.RemainingPierces` and `DirectHitData.PierceCount`
  (`Assets/_QuantumUser/Simulation/Assets/Projectile/DirectHitData.cs`) already implement pierce
  end-to-end for projectile impacts - a perk just needs a new `Weapon.BonusPierce` baked field that
  `ProjectileSpawner.Spawn` adds on top of whatever `DirectHitData.PierceCount` initializes.
- **Needs magazine-position tracking** (which shot in the current clip is this?) - Opening Burst,
  Execution Rounds, Final Round, Escalating Rounds, Empty Chamber, Cataclysm Round, Predator
  Magazine, Combat Reboot. `Weapon` tracks `Ammo` already, so "am I in the last 20%" is derivable
  from `Ammo`/`MagazineSize` - but nothing today branches fire/reload/kill logic on it.
- **Needs an on-kill or on-crit signal** - Killer Instinct, Predator Magazine, Bottomless Momentum,
  Combat Reboot, Critical Rebound. `EntityDied`/`EntityDamaged` (`Events.qtn`) already carry an
  `Owner`/`IsCritical`, but they're View-facing `event`s fired from `DamageUtility`, not
  simulation-consumable `ISignal`s - a perk would need a real signal (or a direct call from
  `DamageUtility`/`ExperienceUtility` into a new weapon-side reaction) rather than reading a View
  event back into sim logic.
- **Needs a post-impact projectile pipeline** - Ricochet, Split Shot, Explosive Sequence, Echo
  Chamber, Infinite Echo, Quantum Rounds. There's a hit-effects precedent to extend
  (`ProjectileHitData.ApplyHit`/`ApplyEffects`, `HitEffectUtility`, `AreaHitData`'s spawned-area
  detonation), but nothing lets a *weapon perk* append a new post-impact behavior onto whatever
  `ProjectileDataAsset`/`DirectHitData` the base weapon already fires.
- **Needs stacking/decaying buff state on `Weapon`** - Relentless Fire, Suppressive Cycle, Overcharge
  Cycle. All three are variations on "keep doing X, a value ramps" - none of `Weapon`'s baked fields
  are time-decaying or ever reset mid-equip today; this needs new fields plus per-tick logic in
  `WeaponSystem.Update`, not a one-shot `Apply`.
- **Emergency Reload** (move speed + damage reduction while reloading) needs `Weapon`'s
  `ReloadTimer > 0` state to reach `CharacterStats`/damage resolution, which today only
  `WeaponSystem` reads.

## Design notes to resolve before authoring

- **Overlapping "fire rate ramps up" perks at three rarities** - Killer Instinct (Uncommon),
  Suppressive Cycle (Rare), Overcharge Cycle (Epic) are the same mechanic at increasing power.
  Decide whether they stack additively or multiplicatively before more than one can be equipped at
  once (`Weapon.Perks` holds 5) - multiplicative stacking of three ramping fire-rate perks compounds
  fast.
- **Overlapping "damage ramps through use" perks** - Relentless Fire, Escalating Rounds, Execution
  Rounds, Final Round all reward sustained/late-magazine fire. Same stacking question.
- **`Min Kill Tier` is only used by 2 of the ~30 non-Common perks** (Killer Instinct: Normal,
  Predator Magazine: Specialist). Worth deciding if this is a real gating mechanic (offer-time
  filter based on kills-so-far, mirroring `EnemyTier`) before it's authored onto only two rows.
- **Double Tap's 15% chance** is the only low-percent proc among otherwise-guaranteed Uncommon
  effects (Piercing Rounds, Opening Burst) - low-%, no-feedback procs tend to read as "broken" rather
  than "lucky." A guaranteed cadence (`Explosive Sequence`'s "every 5th shot" pattern) is more
  legible.
- **Legendary pool is thin** (2 entries vs. Epic's 5) - fine if Legendary is meant to be rare, but
  means fast repeat picks in longer runs once `LegendaryWeight` starts hitting.

## Current status / known simplifications

Only the 5 flat-multiplier perks listed above exist as code, and none of the supporting assets are
authored yet:

1. **No `WeaponPerkPoolData` asset instance** - the pool `LevelUpConfig.WeaponPerkPool` needs
   (`docs/level-up-upgrades.md`) doesn't exist, so neither a fresh weapon drop nor a level-up pick
   can offer any perk yet, even the 5 that are fully coded.
2. **~30 of the ~35 designed perks have no `WeaponPerkData` class at all** - see "What each tier
   actually needs to be built" above for which ones are cheap (new baked stat, same `Apply` shape)
   vs. which need new simulation-level hooks (on-kill/on-crit signal, magazine-position branching,
   post-impact pipeline, decaying buff state) that don't exist anywhere in the weapon/projectile
   systems today.
3. **No range stat exists on `Weapon`** - `WeaponDataAsset.Range` (hitscan) /
   `ProjectileDataAsset.MaxDistance` (projectile) are read directly, not scaled by any baked `Weapon`
   multiplier - Long Barrel needs that field added first.
4. **No damage-multiplier equivalent for crit damage** - `CriticalDamageBonus` is seeded from
   `WeaponDataAsset` and never touched by any perk; Hollow Point needs a
   `CriticalDamageMultiplierWeaponPerkData` sibling to the 5 that exist.
