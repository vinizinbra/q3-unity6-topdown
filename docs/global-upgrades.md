# Global Upgrades catalog

Content for the `LevelUpPoolKind.GlobalUpgrade` pool (`LevelUpConfig.GlobalUpgrades`, see
`docs/level-up-upgrades.md`). This doc is the design catalog; `docs/level-up-upgrades.md` is the
mechanism (rolling, pausing, grant flow) and stays the source of truth for how any of this actually
gets offered and picked.

**This is the "simple, hero-wide numerical growth, stacks" pool** - small flat increments picked
over and over across a run. The game's other three level-up pools live elsewhere: **Weapon Perks**
(`docs/weapon-perks.md`, attach to a weapon, lost on swap), the per-hero **Hero Ascension** pools
(`LevelUpPoolKind.SkillUpgrade`/`PassiveUpgrade`, Dash/Hero Skill/Passive milestones, see
`docs/level-up-upgrades.md`), and **Rift Mutations** (`docs/rift-mutations.md`, a separate
`LevelUpPoolKind.RiftMutation` pool - rare, non-stackable, run-wide rule/synergy/tradeoff effects,
which is where the more dramatic build-defining picks live).

**Status: 22 of 26 rows are implemented in code.** `GlobalUpgradeData` is an abstract base with a
real `Apply(Frame f, EntityRef entity)` (same shape as `WeaponPerkData`'s `Apply(Frame, Weapon*)`),
and `GlobalUpgradeUtility.Grant` dispatches to it generically. Most concrete upgrades derive from
`CharacterStatMultiplierUpgradeData` (`Assets/_QuantumUser/Simulation/Assets/LevelUp/
CharacterStatMultiplierUpgradeData.cs`) - a shared base that multiplies one named `CharacterStats`
field by its own `Multiplier`, floored at 0. `Multiplier` always defaults to `FP._1` on every
subclass, same convention as `WeaponPerkData`'s own multiplier perks (`DamageMultiplierWeaponPerkData`
etc.) - the real tuned number lives on the authored `.asset` instance, not the C# class.

**Asset generation:** `Assets/_QuantumUser/Editor/GlobalUpgradeAssetGenerator.cs`
(`Tools/RiftRaiders/Generate Global Upgrade Assets`) authors one `.asset` instance per class in the
table below - tuned to this doc's own numbers - under
`Assets/_QuantumUser/Resources/LevelUp/GlobalUpgrade/` (created automatically if missing), and wires
all of them into `Assets/_QuantumUser/Resources/LevelUpConfig.asset`'s `GlobalUpgrades` list (that
asset already exists and is already assigned to `RuntimeConfig` in `QuantumGameScene.unity` - see
`docs/level-up-upgrades.md`). Mirrors `WeaponPerkAssetGenerator.cs` exactly: re-running is safe,
existing assets are updated in place (not duplicated), and the list is rebuilt from scratch each
run. `Icon` is left unset for every asset, same as the weapon-perk generator - still a manual
per-upgrade Inspector step.

**Still needed before ANY of this does anything at runtime:**
1. Run the generator above (or author the 21 `.asset` instances by hand).
2. Assign an `Icon` to each - the generator can't author sprites.
3. `CharacterStats.qtn`/`Health.qtn` changed, so Quantum's DSL codegen must run before any of this
   compiles - see the "Quantum `.qtn` codegen gotcha" note in `CLAUDE.md`.

## Roster

Legend: ✅ implemented and wired to a live consumer · ❌ not built (reason given)

### Weapon

Every entry here **intentionally overlaps** with an existing Weapon Perk of the same name (decided:
keep both, accept the overlap - see "Design notes" #1). Where `CharacterStats` already has its own
distinct multiplier field, that's a second independent scaling source (stacks with the Weapon Perk,
doesn't replace it - same convention `DamageUtility.GetSourceMultiplier` already uses for
Weapon/Skill damage). Where no such field exists (Magazine, Range), the upgrade targets the exact
same `Weapon` field the equivalent perk does, so the two stack on that one field instead of needing
a parallel do-nothing `CharacterStats` field.

| Upgrade | Class | Target | Status |
|---|---|---|---|
| Weapon Damage | `WeaponDamageUpgradeData` | `CharacterStats.WeaponDamageMultiplier` | ✅ |
| Fire Rate | `FireRateUpgradeData` | `CharacterStats.AttackSpeedMultiplier` | ✅ |
| Reload Speed | `ReloadSpeedUpgradeData` | `CharacterStats.ReloadSpeedMultiplier` | ✅ |
| Magazine Size | `MagazineSizeUpgradeData` | `Weapon.MagazineSize` (same field as `MagazineMultiplierWeaponPerkData`) | ✅ |
| Critical Chance | `CriticalChanceUpgradeData` | `CharacterStats.CriticalChance` (flat add) | ✅ |
| Critical Damage | `CriticalDamageUpgradeData` | `CharacterStats.CriticalDamageMultiplier` | ✅ |
| Weapon Range | `WeaponRangeUpgradeData` | `Weapon.RangeMultiplier` (same field as `RangeMultiplierWeaponPerkData`) | ✅ |
| Projectile Speed | `ProjectileSpeedUpgradeData` | `CharacterStats.ProjectileSpeedMultiplier` | ✅ (known limitation on homing projectiles - see "What changed" below) |

### Hero

| Upgrade | Class | Target | Status |
|---|---|---|---|
| Max Health | `MaxHealthUpgradeData` | `CharacterStats.MaxHealthMultiplier` + `CharacterSystem.RefreshMaxHealth` | ✅ |
| Shield | `ShieldUpgradeData` | `CharacterStats.MaxShieldMultiplier` + `CharacterSystem.RefreshMaxShield` | ✅ |
| Movement Speed | `MoveSpeedUpgradeData` | `CharacterStats.MoveSpeedMultiplier` | ✅ |
| Health Regeneration | `HealthRegenUpgradeData` | `Health.RegenRate` (new field, ticked by new `HealthRegenSystem`) | ✅ |
| Healing Received | `HealingReceivedUpgradeData` | `CharacterStats.HealingReceivedMultiplier` (now wired into `HealUtility.ResolveHealMultiplier`) | ✅ |
| Pickup Radius | `PickupRadiusUpgradeData` | `CharacterStats.PickupRangeMultiplier` | ✅ |

### Dash

| Upgrade | Class | Target | Status |
|---|---|---|---|
| Dash Cooldown | `DashCooldownUpgradeData` | `CharacterStats.DashCooldownMultiplier` (new field, split from the old dead `CooldownMultiplier` - see `StatUtility.GetSkillCooldown`) | ✅ |
| Dash Charge | `DashChargeUpgradeData` | `CharacterSkills.DashSkill.MaxStacks`/`CurrentStacks` (+1, usable immediately) | ✅ |
| Dash Invulnerability | *(none)* | — | ❌ `DashSkillData` swaps to the `IgnoreProjectile` layer for the dash's *entire* active duration already - it's 100% immune for 100% of the dash. There is no partial i-frame window for "+20% duration" to extend unless the dash itself changes to a partial-invuln model. Not building a redesign of the dash mechanic to manufacture a slot for this upgrade. |

### Hero Skill

| Upgrade | Class | Target | Status |
|---|---|---|---|
| Skill Damage | `SkillDamageUpgradeData` | `CharacterStats.SkillDamageMultiplier` | ✅ |
| Skill Cooldown | `SkillCooldownUpgradeData` | `CharacterStats.SkillCooldownMultiplier` (new field, HeroSkill's independent half of the old shared `CooldownMultiplier`) | ✅ |
| Skill Duration | `SkillDurationUpgradeData` | `CharacterStats.SkillDurationMultiplier` | ✅ |
| Skill Area | `SkillAreaUpgradeData` | `CharacterStats.AreaRadiusMultiplier` (wired into `HitPathSkillAction`/`SpawnEntitySkillAction` via `StatUtility.GetAreaMultiplier`, stacking with `SkillSlot.AreaMultiplier`; also folded into `AreaHitData.Detonate`/`ExplodeOnDestroyUtility.ResolveBlastRadius`'s own radius calc, so a thrown bomb like Bunny Bomb scales too, alongside `BlastRadiusUpgrade`/Bigger Boom) | ✅ |
| Hero Skill Charge | `HeroSkillChargeUpgradeData` | `CharacterSkills.HeroSkill.MaxStacks`/`CurrentStacks` (+1, usable immediately - same shape as Dash Charge) | ✅ |

### Economy

| Upgrade | Class | Target | Status |
|---|---|---|---|
| Experience Gain | `ExperienceGainUpgradeData` | `CharacterStats.ExperienceGainMultiplier` (new field, wired into `ExpOrbSystem`'s pickup credit) | ✅ |
| Luck | *(none)* | `CharacterStats.Luck` | ❌ field exists and is seeded, but nothing reads it anywhere - no rarity-roll/loot mechanic is designed for it to bias. Needs a design decision on what it actually does before it's worth wiring up (see "Design notes" #3). |
| Rift Shard Gain | *(none - see `docs/rift-mutations.md`)* | — | ❌ not a Global Upgrade. The Rift Shard currency system exists (`RiftShard.qtn`/`RiftShardUtility`/`RiftShardOrbSystem`), but its only gain multiplier today comes from Greed, a Rift Mutation - see `docs/rift-mutations.md`. |
| Coin Gain | *(none)* | `CharacterStats.CoinGainMultiplier` | ❌ not a Global Upgrade yet - the Coin currency system exists (see below) with a real gain-multiplier field on `CharacterStats`, but no upgrade/mutation sources it today (same gap `RiftShardGainMultiplier` had before Greed). |

**Coin** is a second, independent currency from Rift Shards (`docs/rift-mutations.md`'s own
currency) - `Coin.qtn` (the pickup)/`Coins.qtn`/`CoinConfig`/`CoinUtility`/`CoinOrbSystem`,
mirroring `RiftShardUtility`/`RiftShardOrbSystem` field-for-field. Both currencies now share the
same drop shape: a per-tier `EnemyTierStatsConfig.TierStats` pair (`RiftShardValue`/
`RiftShardDropChance` and `CoinValue`/`CoinDropChance`) gates *whether* a kill drops one at all -
`Value > 0` is necessary but not sufficient, `TrySpawnDrop` also rolls `DropChance`
(`DamageUtility.RollChance`, the same helper crit rolls use) before spawning - and the spawn
position scatters away from the exact death point (`RiftShardConfig`/`CoinConfig`'s own
`Min`/`MaxSpawnOffset`, `EnemyMovementUtility.RandomPositionInRing` - same pattern `ScrapConfig`
already used) so multiple drops off one kill don't stack exactly on top of each other. `Tools/
RiftRaiders/Generate Coin Assets` authors `CoinConfig.asset`; a `CoinOrb` `EntityPrototype` and
`RuntimeConfig.CoinConfig`/`CoinPrototype` still need Editor authoring, same gap `RiftShardOrb` has
(see `docs/rift-mutations.md`).

**As of the Cursed Rift pass (see `docs/breathing-poi.md`), both currencies moved from shared
`Frame.Global` totals to PER-PLAYER wallets** (`CharacterStats.Coins`/`CharacterStats.RiftShards`),
confirmed with the user - a Cursed Rift Coin/Rift Shard sacrifice needed to be a meaningful
individual choice, not a party-wide tax. `CurrencyOrbSystem` still finds a pickup's radius/plays
its collect event off whichever single player physically reached it, but the actual grant now
broadcasts to every connected player's own wallet (`CoinUtility.GrantAll`/
`RiftShardUtility.GrantAll`), each scaled by *their own* `CoinGainMultiplier`/
`RiftShardGainMultiplier` - "picking up 1 coin means everyone gets 1 coin," then each player
spends independently. `CoinUtility`/`RiftShardUtility` also gained a `TrySpend(f, player, amount)`
(no spend method existed before this pass). Experience is unaffected - still a single shared
`Frame.Global.TotalExperience` total, by design (co-op leveling stays shared).

## What changed to make the ✅ rows real

Several `CharacterStats` fields were seeded but had **zero consumers anywhere** before this pass
(confirmed by grep, not assumption) - each got a small, targeted wiring fix rather than staying dead:

- **`CooldownMultiplier`` → split into `DashCooldownMultiplier`/`SkillCooldownMultiplier`.** The old
  field had no consumer at all; `SkillSystem`'s two `slot->CooldownTimer = skill.Cooldown;` sites
  (`TickCooldown`/`TryBegin`) now route through the new `StatUtility.GetSkillCooldown(f, owner,
  slotId, baseCooldown)`, which picks the right field by `SkillSlotId` (`UpdateSlot` now threads
  `SkillSlotId` down to both call sites).
- **`AreaRadiusMultiplier`** had no consumer - `HitPathSkillAction`'s three radius/width/height
  calcs and `SpawnEntitySkillAction.ApplyScale` now multiply in `StatUtility.GetAreaMultiplier(f,
  owner)` alongside the existing `SkillSlot.AreaMultiplier` (which can't hold a permanent bonus
  itself - it resets to 1 every activation, see `SkillSystem.TryBegin`).
- **`ProjectileSpeedMultiplier`** had no consumer - `ProjectileSpawner.Spawn` now scales
  `projectile->Velocity` by `StatUtility.GetProjectileSpeedMultiplier(f, owner)` once at spawn,
  rather than threading it through every `ProjectileMovementData` subclass's own `Speed` field.
  Known limitation: `HomingProjectileMovementData.UpdateVelocity` re-derives velocity magnitude from
  its own `Speed` every tick once it starts turning, so the multiplier only reliably holds for a
  homing projectile's initial launch, not its whole flight.
- **`HealingReceivedMultiplier`** had no consumer - `HealUtility.ResolveHealMultiplier` now folds it
  in alongside the existing `IncreaseHealUpgrade` bonus, so it applies to every heal path
  (pickups, lifesteal, and `HealthRegenUpgradeData`'s own regen tick).
- **`ExperienceGainMultiplier`** is a brand new field - `ExpOrbSystem` already resolves the
  collecting player's own `CharacterStats*` right at the pickup point (same place
  `PickupRangeMultiplier` is read), so scaling the granted amount there was a one-line addition.

## Design notes

1. **Pool overlap with Weapon Perks is intentional (decided).** `WeaponPerkPoolData` ships a full
   ~30-perk roster (see `docs/weapon-perks.md`) including Damage/Fire Rate/Reload/Magazine/Crit
   Chance/Crit Damage/Range - all of which also appear above. Both pools roll into the same 3-option
   level-up screen (`LevelUpUtility.RollOptionsFor`), so a player can see near-identical-looking
   cards from both pools in one screen. Kept on purpose as two independent stacking sources (Weapon
   Perk = build-defining, less frequent; Global = small flat increments) rather than cut.
2. **Skill Upgrade vs Skill-stat Global Upgrade.** `docs/level-up-upgrades.md` describes the
   per-hero `SkillUpgrade` pool as living on `CharacterData` because "which skill/passive upgrades
   make sense depends on which hero is rolling." The Hero Skill section above is generic
   damage/cooldown/duration/area - fine as a hero-agnostic Global Upgrade as long as it's kept to
   flat multipliers and doesn't start creeping into hero-specific mechanics that belong in the
   per-hero pool instead.
3. **Luck has no defined meaning yet.** The field exists (`CharacterStats.Luck`, `CharacterData.Luck`)
   but was never wired to anything - unlike the other dead fields above, there's no obvious single
   consumer to fold it into (does it bias weapon-perk rarity rolls? loot drops? enemy drop chance?
   none of those mechanics read a per-player Luck value today). Needs a design decision before it's
   worth wiring, not just a plumbing fix.
4. **Economy tier weighting.** Once Gold Gain/Rift Shards exist as real systems, consider weighting
   all four Economy entries lower than combat stats (e.g. a rarer rarity tier) via
   `LevelUpConfig.GetWeight` - a struggling run shouldn't have "worse combat power this level" forced
   by an economy pick landing in the roll.
5. **Missing from the list, already free.** `CharacterStats` also has `DamageReduction`,
   `KnockbackMultiplier`/`KnockbackTakenMultiplier`, `LifeSteal`, and
   `OutgoingStatusDurationMultiplier` sitting unused by any upgrade today - cheap additions later if
   the pool needs more Hero/Weapon variety without new plumbing (worth checking each still has a live
   consumer of its own before assuming it's free, the same way this pass found several that didn't).
6. **More dramatic build-defining picks live in Rift Mutations, not here (decided).** An earlier pass
   built Glass Core/Heavy Arsenal/Close Quarters/Greed/etc. directly in this pool as
   `GlobalUpgradeData` subclasses; they were moved out into their own `RiftMutationData` hierarchy
   and `LevelUpPoolKind.RiftMutation` pool (see `docs/rift-mutations.md`) since "non-stackable,
   rare, build-defining" is a different shape from this pool's "small stacking increment" one. If a
   future pick is a one-shot tradeoff or a new rule rather than a flat +X%, it belongs there, not
   here.

---

# 2026-08-20 — dropped orbs no longer land on raised platforms

Reported from testing: coins dropped by an enemy sometimes popped up onto an upper platform, where the
player has no reason to go for them.

## Cause

`OrbSpawnUtility.SpawnWithPop` launches Coin / Rift Shard / Scrap orbs on a real ballistic arc (a solved
45° lob to a scattered ring point, plus an optional random burst). `PopMotionSystem` then re-resolves
the real ground under the orb's **current** position every tick and lands it the moment its trajectory
reaches that surface.

That re-resolve is deliberate and correct - it is what stops an orb ever being placed under a mesh it
hasn't reached yet - but it makes no distinction about *which* surface it finds. If the arc clears the
lip of a raised platform, the ground under the orb becomes that platform's top, and the orb settles up
there.

## Fix

`PopVelocity` gained `OriginGroundY` - the real ground height under the position the orb popped from,
resolved once at spawn. `PopMotionSystem` now refuses to carry an orb onto ground more than
`MaxRiseAboveOrigin` (**0.5**) above that floor.

Modelled as a **bump**, not as ignoring the surface: horizontal velocity is dropped while the vertical
component keeps integrating, so the orb stops dead against the platform's edge and falls straight down
onto its own floor. Ignoring the raised ground instead would have let the orb sail *through* the
platform and settle underneath it - strictly worse.

**Only climbing is blocked.** Ground lower than the origin passes straight through unchanged, so an
enemy killed on a ledge still scatters coins down off it, which reads fine and is often the only
reachable outcome anyway.

0.5 is deliberately generous next to `MovementDataAsset.MaxLedgeHeight` (1, the tallest step a player
auto-mantles): it absorbs ordinary terrain unevenness - a ramp, a kerb, a slightly raised slab - while
still keeping drops off anything a player would have to deliberately climb. It is a constant rather than
an authored field because it is a reachability guard, not a balance knob.

Applies to every orb that pops (Coin, Rift Shard, Scrap). Exp orbs are unaffected - they deliberately
spawn exactly on the death point and never arc (see `ExperienceUtility.TrySpawnDrop`).
