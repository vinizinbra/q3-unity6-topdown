# Run perks (planned)

Global perks acquired mid-run, applying to the character rather than to one weapon or skill. See
[weapons.md](weapons.md) for `WeaponPerkData` and [skills.md](skills.md) for `SkillActionData` —
this is a third, distinct thing, and the difference is why it needs its own mechanism:

| | Applies to | Shape | Mechanism |
|---|---|---|---|
| `WeaponPerkData` | One weapon | A number | Baked into `Weapon` at equip; never re-derived |
| `SkillActionData` | One skill slot | A behavior at a lifecycle point | Run from `SkillSlot.Upgrades` on activation |
| **`RunPerkData`** | The character | **A reaction to an event, often temporary and conditional** | **Nothing yet — see below** |

A run perk can't be baked the way a weapon perk is. "Deal 40% more to enemies above 90% health"
isn't a number that exists at equip time; it's a decision made at the moment of a hit, knowing who
was hit and how. That's the whole design problem.

## The planned list

### Offensive

| Perk | Effect |
|---|---|
| Opening Strike | Deal 40% more damage to enemies above 90% health. |
| Executioner | Deal 30% more damage to enemies below 25% health. |
| Relentless | Consecutive hits against the same enemy grant 2% damage, stacking to 20%. Resets when changing targets. |
| Combat Rhythm | Every sixth weapon hit deals 100% additional damage. |
| Skill Primer | Using a skill grants 25% weapon damage for 4 seconds. |
| Armed Response | After dealing weapon damage, your next skill deals 35% more damage. |
| Crowd Breaker | Deal 25% more damage to slowed, stunned, rooted, pushed, or pulled enemies. |
| Overkill | Excess damage from killing an enemy creates a small explosion around it. |

### Defense and sustain

| Perk | Effect |
|---|---|
| Emergency Barrier | Falling below 30% health grants a temporary shield. Once every 30 seconds. |
| Second Wind | The first lethal hit each stage leaves you at 1 health and grants brief invulnerability. |
| Excess Recovery | Healing received while at full health becomes temporary shield. |
| Adaptive Armor | Taking damage from the same enemy type repeatedly grants stacking resistance to that type. |
| No Time to Bleed | Killing an enemy restores a little health. Elite kills restore considerably more. |

### Movement and skills

| Perk | Effect |
|---|---|
| Kinetic Reload | Dashing restores 25% of the current weapon's magazine. |
| Flow State | Weapon hits reduce skill cooldown slightly, at most once per second. |
| Momentum | Killing an enemy grants 10% movement speed for 3 seconds, stacking to three times. |
| Impact Entry | Using your traversal skill releases a shockwave that damages and pushes nearby enemies. |

### Co-op

| Perk | Effect |
|---|---|
| Focus Fire | Enemies damaged by two different players within 2 seconds become exposed, taking 15% more damage from the whole team. |
| Shared Recovery | Healing you receive also gives the nearest injured ally 30% of the amount. |
| United Front | While near an ally, gain 12% damage resistance and 10% skill recharge speed. |

## What each perk actually needs

Legend: **✓** exists · **~** partially exists · **✗** missing.

| Perk | Needs | Status |
|---|---|---|
| Impact Entry | `ExplodeSkillAction` on `Begin` | ✓ **already possible, zero code** |
| Kinetic Reload | A `SkillActionData` writing `weapon->Ammo` | ✓ trivial |
| Opening Strike / Executioner | Target health % at damage time | ~ readable, but no hook to act on it |
| Crowd Breaker | CC states (slow/stun/root/push/pull) | ✗ no status system |
| Overkill | The excess damage past 0 | ✗ `ApplyDamage` clamps and discards it |
| Combat Rhythm / Armed Response | Whether a hit came from weapon or skill | ✓ **`DamageSource` — built** |
| Relentless | Attacker's last target + hit streak | ✗ no per-attacker combat memory |
| Skill Primer / Momentum | A stat bonus that expires | ✗ **no temporary modifiers** |
| Emergency Barrier / Flow State | Internal cooldowns | ✗ no ICD state |
| Second Wind | Intercepting a lethal hit, + per-stage flag | ✗ `ApplyDamage` kills unconditionally |
| Excess Recovery / No Time to Bleed / Shared Recovery | Healing | ✗ **no heal path exists at all** |
| Adaptive Armor / No Time to Bleed | Enemy type identity, elite flag | ✗ neither exists |
| Focus Fire | Per-target "who hit me, when" + an Exposed status | ✗ |
| United Front | Ally proximity | ✗ |

## The five things that unblock most of the list

### 1. `RunPerkData` with event hooks

Two of twenty are stat tweaks. The rest are *reactions*. So this mirrors `SkillActionData` — a
polymorphic asset with lifecycle hooks — not `WeaponPerkData`:

```csharp
public abstract unsafe class RunPerkData : AssetObject
{
    public virtual void OnDealDamage(Frame f, ref DamageContext context) { }
    public virtual void OnTakeDamage(Frame f, ref DamageContext context) { }
    public virtual void OnKill(Frame f, EntityRef killer, EntityRef victim, FP overkill) { }
    public virtual void OnHeal(Frame f, EntityRef entity, ref FP amount) { }
    public virtual void OnSkillUsed(Frame f, EntityRef entity, SkillData skill) { }
}
```

Acquired perks live in a `RunPerks` component (`array<AssetRef<RunPerkData>>[N]`), the same
runtime-owned pattern as `SkillSlot.Upgrades`.

### 2. `DamageSource` — built

`ApplyDamage(f, target, damage, owner, DamageSource source = None)` now knows what fired a hit, and
`CharacterStats` has `WeaponDamageMultiplier` / `SkillDamageMultiplier` stacking on top of the
global `DamageMultiplier`. This is what makes Skill Primer ("+25% *weapon* damage") and Armed
Response ("+35% *skill* damage") expressible at all — with one multiplier each would buff the thing
that triggered it.

The owner can't be asked for the source (it holds a weapon *and* skills at once), so the source
rides on whatever outlives the moment of firing:

| Damage | Source set at | Carried on |
|---|---|---|
| Hitscan | Fire time | passed directly |
| Projectile | Fire time, lands later | `Projectile.Source` |
| Dash fire trail | Skill activation, burns later | `PersistentArea.Source` |
| Grenade's lingering fire | Weapon fired the grenade | inherited via `HitEffectContext.Source` |

Enemy attacks pass `None` and are unaffected — they have no `CharacterStats` to multiply by.

### 2b. `DamageContext` — still worth doing

The remaining offensive perks need to *modify a hit in flight*, which the loose-argument signature
still can't express:

```csharp
public struct DamageContext
{
    public EntityRef Owner;
    public EntityRef Target;
    public DamageSource Source;   // done - currently a loose arg
    public FP Damage;             // mutable - perks scale it   <- Opening Strike, Executioner
    public bool IsCritical;
    public FP Overkill;           // set on kill                <- Overkill
}
```

Folding the existing args into this is now mostly mechanical, since every call site already passes
a source.

### 3. Temporary modifiers — and why `CharacterStats` can't do it

**This is the structural problem.** `CharacterStats` is seeded once and mutated permanently, on the
explicit reasoning that a perk is never removed. Momentum breaks that: +10% move speed, three
stacks, each expiring on its own 3-second timer. You cannot `stats->MoveSpeedMultiplier -= FP._0_10`
on expiry and trust it — stacks expire out of order, and repeated multiply/divide drifts.

So temporary bonuses need their own layer, recomputed rather than accumulated:

```qtn
// Timed bonuses on top of CharacterStats. Recomputed from the live entries every tick - never
// added into CharacterStats, which stays the permanent layer.
component StatModifiers
{
    array<StatModifier>[8] Modifiers;
}

struct StatModifier
{
    StatType   Stat;
    FP         Amount;
    FP         RemainingTime;
}
```

Readers then ask for an effective value (`StatUtility.GetDamageMultiplier(f, entity)` =
`CharacterStats.DamageMultiplier + active bonuses`) rather than reading the field raw. Expiry is
just dropping an entry, so nothing has to be undone. Needed by Skill Primer, Momentum, Emergency
Barrier, United Front, Second Wind, Focus Fire, Adaptive Armor.

### 4. A healing path

Three perks heal, and `DamageUtility` only subtracts. There's no `HealUtility`, no
`EntityHealed` event, and `CharacterStats.HealingReceivedMultiplier` and `LifeSteal` currently have
nothing to multiply. "Temporary shield" (Emergency Barrier, Excess Recovery) also needs `Shield` to
distinguish temporary points from `Max` — otherwise regen refills them for free.

### 5. Status effects

Crowd Breaker needs slow/stun/root/push/pull to *be* something; Focus Fire needs an Exposed state.
This is the status system already deferred once (see `CharacterStats.ElementalChance`, which is
seeded but has no consumer for exactly this reason).

## Answering "what do we add to CharacterStats?"

**Added:** `WeaponDamageMultiplier`, `SkillDamageMultiplier` — the source split, since Skill Primer
and Armed Response are unwritable without it. `MoveSpeedMultiplier` — Momentum needs it;
`CharacterData.BaseMoveSpeed` was renamed to it and is now consumed by `PlayerMovementProcessor`
as a multiplier on `MovementDataAsset.WalkSpeed`/`RunSpeed`.

**Everything else these perks need is already there** — and mostly still unconsumed:
`DamageMultiplier` ✓ (wired), `DamageReduction` (Adaptive Armor, United Front),
`CooldownMultiplier` (Flow State, United Front), `HealingReceivedMultiplier` (Shared Recovery),
`LifeSteal` (No Time to Bleed), `AreaRadiusMultiplier`, `KnockbackMultiplier`.

**But adding fields is not what unblocks this list.** Most of these perks need machinery that
doesn't exist regardless of how many fields `CharacterStats` has — hooks, a damage context, timed
modifiers, healing, statuses. And one of them (`StatModifiers`) says `CharacterStats` should stay
*permanent-only*, with temporary bonuses layered on top rather than baked in.

## Suggested order

1. ~~**`DamageSource`**~~ — done. Unblocked the source-specific multipliers.
2. **`RunPerkData` + `RunPerks`** — the hook surface, and the thing every remaining perk waits on.
   Impact Entry and Kinetic Reload need neither and can ship today as skill actions.
3. **`DamageContext`** — fold the loose args into a mutable struct. Unblocks Opening Strike,
   Executioner, Overkill.
4. **`StatModifiers`** — timed layer. Unblocks Skill Primer, Momentum, United Front.
5. **Healing** — `HealUtility`, `EntityHealed`, temporary shield points.
6. **Status effects** — the largest, and gates only Crowd Breaker and Focus Fire. Last.
