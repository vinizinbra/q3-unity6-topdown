# Max — Vendetta & Fire Mastery

**Update (post-implementation): Vendetta marks are per-enemy, not single-target.** The design below
originally had Max hold one `RevengeTarget`/`StoredRevengeDamage` pair (a single current mark,
replaced outright whenever a new enemy landed a qualifying hit). That was changed so Max (or any
future Vendetta holder) can have an arbitrary number of enemies marked simultaneously - killing
*any* of them heals based on *that enemy's own* stored damage. The mechanism: `RevengeTarget`/
`StoredRevengeDamage` were deleted, and `RevengeMark` (originally just a target-side display mirror)
now lives on the enemy as the single source of truth, gaining its own `StoredDamage` field. Since
every enemy already gets its own component instance for free in ECS, this needed no fixed-size array
or cap - `RevengeMarkTimeoutSystem` (renamed from `RevengeTargetTimeoutSystem`) ticks every active
mark independently. An enemy can only be marked by one holder at a time (a second holder's hit
reassigns `MarkedBy` and resets that enemy's `StoredDamage`), but a single holder can have many
enemies marked at once. Sections below (component table, "Mark replacement/accumulation", the
two-attackers edge case) describe the original single-target version and are superseded by this
note - see `MaxVendettaSystem.cs`/`RevengeMarkTimeoutSystem.cs`/`Vendetta.qtn` for the current
authoritative behavior.

**Addition: 4 Berserk/Overdrive Hero Skill Upgrades**, out of scope for this doc's own design
(Berserk/`RageOverdrive` predate Vendetta and have no dedicated design doc of their own) but touching
code this doc describes - `MaxVendettaSystem.OnEntityKilled` now also extends the current Overdrive
activation if `VendettaRushExtension` is present (Vendetta Rush upgrade). The other 3 -
Too Angry to Die (`CheatDeathGuard`/`CheatDeathUtility`, hooked into `DamageUtility.ApplyDamage`'s
Health clamp), Seeing Red (`SeeingRedSkillAction`, a Begin-only shockwave reusing `AreaQueryUtility`/
`StatusEffectUtility.ApplyBurn`), and Uncontrolled Fury (`UncontrolledFuryExtension` +
`MaxOverdriveReactionSystem`, a new `ISignalOnEntityKilled` reactor independent of
`MaxVendettaSystem`) - live in `Assets/_QuantumUser/Simulation/Assets/Skills/Heroes/Max/
HeroSkillUpgrades/` alongside the existing `RageOverdriveSkillAction`/`OverdriveDamageSkillAction`/
`OverdriveInstantReloadSkillAction`, following that same Begin/End-paired `SkillActionData` shape.
Shared "extend the current Overdrive" logic lives in `OverdriveUtility.TryExtend`. Same authoring gap
as everything else in this doc: no `.asset` instances exist yet, and none are wired into
`MaxHeroSkill.asset`'s `Actions` list.

**Current status: code-complete, not yet authored in the Editor.** Vendetta itself (`Vendetta.qtn`,
`VendettaPassiveData`, `MaxVendettaSystem`, `RevengeTargetTimeoutSystem`, `MaxVendettaHealFxView`),
all 4 Vendetta Upgrades, all 4 Fire Mastery Hero Traits (`FireMastery.qtn`,
`MaxFireMasteryReactionSystem`), and the two generic additions (`AreaQueryUtility`,
`FireMasterySpreadUtility`) exist and are registered in `SystemSetup.User.cs`. Two Editor
generators - `Tools/RiftRaiders/Max/Generate Vendetta Assets` and `.../Generate Fire Mastery
Assets` (`MaxVendettaAssetGenerator.cs`/`MaxFireMasteryAssetGenerator.cs`) - author all 9 `.asset`
instances and wire them into `MaxCharacterData` (the Vendetta generator also repoints `Passive` off
`AdrenalineRushPassiveData` and strips its 4 old upgrades from `PassiveUpgrades`), but neither has
been *run* yet - until someone runs both from the Unity Editor menu, `MaxCharacterData` still points
at Adrenaline Rush and none of this is live. `AdrenalineRushPassiveData`/its 4 upgrades/their `.cs`
files were deliberately left on disk (dead code, safe to delete once the Vendetta generator has run
and nothing references them anymore) rather than deleted as part of this pass. Below is the original
design spec this was implemented from - some fields (e.g. `AreaQueryUtility`'s exact signature,
`FireMasterySpreadUtility`'s Burn-application convention, `ExecuteAgainstStatus`'s tier bucketing)
were resolved slightly differently during implementation than sketched below; the code itself is
authoritative where they diverge.

It specifies Max's new base Passive (**Vendetta**, replacing Adrenaline Rush per an explicit decision
below) plus four Vendetta upgrades and four "Fire Mastery" Hero Traits, built entirely on this
project's *existing* generic combat/status/area-effect infrastructure (researched in depth before
writing this — see citations throughout) plus a small, explicitly-justified set of new generic (not
Max-specific) engine additions.

## Decision: Vendetta replaces Adrenaline Rush

Max already ships a base Passive today — **Adrenaline Rush** (`AdrenalineRushPassiveData` →
seeds an `Adrenaline` component; runtime logic in `AdrenalineUtility.cs`/`AdrenalineSystem.cs`)
— plus 4 Passive Upgrades built on it (`BattleHighPassiveUpgradeData`,
`HotBloodedPassiveUpgradeData`, `NoTimeToBreathePassiveUpgradeData`,
`TooAngryToDiePassiveUpgradeData`). `CharacterData.Passive` is a single `AssetRef<PassiveData>`
slot, not a list — a hero has exactly one base Passive.

**Confirmed with the user: Vendetta replaces Adrenaline Rush entirely.** Implementation must:
- Retire `AdrenalineRushPassiveData.asset` from `MaxCharacterData.Passive`, pointing it at the new
  `VendettaPassiveData.asset` instead.
- Remove the 4 Adrenaline Rush upgrade entries from `MaxCharacterData.PassiveUpgrades`, replacing
  them with Vendetta's own 4 upgrades (Unbroken Spirit / Settled Score / Blood Debt / Burning
  Vengeance) plus the 4 new Fire Mastery traits (Hot Target / Cremation / Wildfire / Flashpoint) —
  8 entries total in the list.
- `Adrenaline`/`AdrenalineUtility`/`AdrenalineSystem` and the 4 old upgrade classes become dead code
  - delete them rather than leaving orphaned unused systems registered in `SystemSetup.User.cs`.
  `AdrenalineUtility.OnDamageDealt`/`OnDamageTaken` calls inside `DamageUtility.ApplyDamage`
  (lines ~135-136) and the `GetWeaponDamageMultiplier` fold-in inside `ResolveOutgoingDamage`
  (~line 530) must be removed too.

## Core building blocks this design reuses (all pre-existing, all hero-agnostic)

| Building block | File | Used for |
|---|---|---|
| `Combat.qtn` signals (`OnEntityKilled`, `OnCriticalHit`) | `Assets/_QuantumUser/Simulation/QTN/Combat.qtn` | Vendetta kill-consumption, Flashpoint, Wildfire, Burning Vengeance trigger points |
| `DamageUtility.ApplyDamage`/`ResolveOutgoingDamage` | `Assets/_QuantumUser/Simulation/Systems/DamageUtility.cs` | The single damage/crit/death pipeline every mechanic below hooks into - no bespoke damage path |
| `StatusEffectUtility.ApplyBurn` / `IsBurning` | `Assets/_QuantumUser/Simulation/Systems/StatusEffectUtility.cs` | Burn application/query for Burning Vengeance, Wildfire, Hot Target, Cremation, Flashpoint |
| `HitEffectUtility.ApplyExplosion` / `ApplyDamageInRadius` | `Assets/_QuantumUser/Simulation/Systems/HitEffectUtility.cs` | Flashpoint's explosion (damage + `WeaponExplosionReleased` VFX event, for free) |
| `WeaponPerkReactionSystem`-style signal-reaction system shape | `Assets/_QuantumUser/Simulation/Systems/WeaponPerkReactionSystem.cs` | The exact pattern `MaxVendettaSystem`/`MaxFireMasteryReactionSystem` below replicate: `SystemMainThread` (or `SystemMainThreadFilter` when a per-tick cooldown needs ticking), no domain logic in `Update`, `ISignalOn...` handlers doing an early-out `TryGetPointer` on an optional component |
| "Read live every calculation, nothing to bake/revert" idiom | `Weapon.qtn`'s shared ramp pool / Killer Instinct fire-rate bonus | Hot Target's crit bonus - never baked into `CharacterStats.CriticalChance`, read fresh every roll |
| "Multiple upgrades compose onto one shared component via `FPMath.Max`" idiom | `WeaponRampState` (fed by Relentless Fire/Suppressive Cycle/Overcharge Cycle) | `StatusSpreadOnDeath` composing Burning Vengeance + Wildfire; `RevengeConfig` composing Settled Score + Blood Debt |
| `PassiveUpgradeData` / `PassiveUpgradeUtility.Grant` | `Assets/_QuantumUser/Simulation/Assets/LevelUp/PassiveUpgradeData.cs`, `Systems/PassiveUpgradeUtility.cs` | All 8 new upgrades/traits are ordinary `PassiveUpgradeData` subclasses in the existing pool - **zero new upgrade plumbing needed** |
| `f.AddOrGet<T>` / `f.Unsafe.TryGetPointer<T>` / `f.Remove<T>` | Quantum SDK, already this project's established idiom | Every component below |

**Two small, generic (non-Max-specific) additions this design needs that don't exist yet:**

1. **Health-vs-Shield damage reporting.** `DamageUtility.ApplyDamage` currently computes the
   Health/Shield split internally (`AbsorbWithShield`) but never exposes it - no signal fires for
   plain (non-crit, non-kill) damage of any kind. Vendetta's core rule ("only actual Health damage
   triggers Vendetta... Shield damage does not by default") is impossible to implement correctly
   without this. **Proposed fix: two new signals**, `OnHealthDamageApplied` and
   `OnShieldDamageApplied`, fired unconditionally (any source, any owner including `EntityRef.None`)
   from `ApplyDamage` right where the existing Health/Shield split already happens. This is exactly
   the "Shield and Health damage reporting" generic system your own architecture requirements list as
   expected infrastructure - not a Vendetta-only hack. See scaffolding below.
2. **A capped-radius enemy query.** Every existing area utility (`ApplyExplosion`,
   `ApplyDamageInRadius`, `WeaponPerkUtility.TryFindNearestEnemy`) either has no target cap or only
   returns the single nearest match - none support "up to N enemies in a radius." Wildfire and
   Flashpoint both require `MaxTargets`. **Proposed fix**: a small `AreaQueryUtility.FindEnemiesInRadius(Frame f, FPVector3 center, FP radius, EntityRef exclude, int maxTargets)` helper, generic and reusable by any future capped-radius mechanic, following the exact
   `OverlapShape` + `Enemy`/`Dead`/`Invulnerable` filtering shape `WeaponPerkUtility.TryFindNearestEnemy` already uses.

A third small addition, **a generic heal utility** (`HealingUtility.Heal(Frame f, EntityRef entity, FP amount)`, clamped to missing Health), is needed for Vendetta's on-kill heal - your own
architecture list already names "Heal entity" as an expected transient/generic operation, so this is
exactly that, not new scope.

---

## 1. Component definitions

| Component | Fields | Added by / when | Removed when | Read by |
|---|---|---|---|---|
| `RevengeConfig` | `FP HealMultiplier; FP MarkDuration;` | `VendettaPassiveData.Apply`, once at spawn | Never (lives for the character's lifetime) | `MaxVendettaSystem` |
| `RevengeTarget` | `EntityRef Target; FP RemainingDuration;` | Lazily, first qualifying hit taken | Mark consumed (kill) or expires (timer) | `MaxVendettaSystem`, its own timeout tick |
| `StoredRevengeDamage` | `FP Amount;` | Lazily, alongside `RevengeTarget` | Same moment as `RevengeTarget` | `MaxVendettaSystem` |
| `ShieldDamageCountsForRevenge` | *(tag, no fields)* | `UnbrokenSpiritPassiveUpgradeData.Apply` | Never | `MaxVendettaSystem` |
| `StatusSpreadOnDeath` | `Boolean TriggerOnVendettaKill; Boolean TriggerOnAnyBurningDeath; FP Radius; FP BurnDuration; FP BurnIntensity; Int32 MaxTargets;` | `BurningVengeancePassiveUpgradeData.Apply` and/or `WildfirePassiveUpgradeData.Apply` (compose via `FPMath.Max`, see below) | Never | `MaxVendettaSystem` (Vendetta-kill trigger), `MaxFireMasteryReactionSystem` (any-burning-death trigger) |
| `ConditionalCriticalModifier` | `FP CriticalChanceBonusVsBurning;` | `HotTargetPassiveUpgradeData.Apply` | Never | `DamageUtility.ResolveOutgoingDamage` (live read) |
| `ExecuteAgainstStatus` | `FP NormalHealthThreshold; FP EliteHealthThreshold; FP BossHealthThreshold; Boolean BossExecutionEnabled;` | `CremationPassiveUpgradeData.Apply` | Never | `MaxFireMasteryReactionSystem` |
| `ExplosionOnConditionalHit` | `FP Radius; FP DamageCoefficient; FP ProcCooldown; FP CooldownRemaining; Int32 MaxTargets; Boolean AllowRecursiveProc;` | `FlashpointPassiveUpgradeData.Apply` | Never | `MaxFireMasteryReactionSystem` |

All 8 are added to the entity that picked the corresponding upgrade (Max), never to the target/enemy
side. None of them are ever added by anything checking "is this hero Max" - presence of the
component *is* the gate, seeded purely by which `PassiveUpgradeData` assets got granted.

## 2. Upgrade configuration / data structures

All 8 are ordinary `PassiveUpgradeData` subclasses (same base class, same `Apply(Frame f, EntityRef entity)` signature, same `PassiveUpgradeUtility.Grant` call path every other hero's passive
upgrades already use - see `ExplosiveRoundsPassiveUpgradeData.cs` for the established shape).

```csharp
// Assets/_QuantumUser/Simulation/Assets/Character/Heroes/Max/VendettaPassiveData.cs
public unsafe class VendettaPassiveData : PassiveData
{
    public FP BaseHealMultiplier = FP._0_50;
    public FP BaseMarkDuration = 8;

    public override void Apply(Frame f, EntityRef entity, CharacterStats* stats)
    {
        f.Add(entity, new RevengeConfig { HealMultiplier = BaseHealMultiplier, MarkDuration = BaseMarkDuration });
    }
}

// Assets/_QuantumUser/Simulation/Assets/LevelUp/Heroes/Max/PassiveSkillUpgrades/UnbrokenSpiritPassiveUpgradeData.cs
public unsafe partial class UnbrokenSpiritPassiveUpgradeData : PassiveUpgradeData
{
    public override void Apply(Frame f, EntityRef entity) => f.Add(entity, new ShieldDamageCountsForRevenge());
}

// .../SettledScorePassiveUpgradeData.cs
public unsafe partial class SettledScorePassiveUpgradeData : PassiveUpgradeData
{
    public FP HealMultiplier = FP._1; // "100%" - composes via Max, so a re-pick or a lower-tier
                                       // duplicate can't downgrade an already-granted 100%
    public override void Apply(Frame f, EntityRef entity)
    {
        f.AddOrGet<RevengeConfig>(entity, out var config);
        config->HealMultiplier = FPMath.Max(config->HealMultiplier, HealMultiplier);
    }
}

// .../BloodDebtPassiveUpgradeData.cs
public unsafe partial class BloodDebtPassiveUpgradeData : PassiveUpgradeData
{
    public FP AdditionalDuration = 4;
    public override void Apply(Frame f, EntityRef entity)
    {
        f.AddOrGet<RevengeConfig>(entity, out var config);
        config->MarkDuration += AdditionalDuration; // additive bonus, not an override
    }
}

// .../BurningVengeancePassiveUpgradeData.cs
public unsafe partial class BurningVengeancePassiveUpgradeData : PassiveUpgradeData
{
    public FP Radius = 4;
    public FP BurnDuration = 3;
    public FP BurnIntensity = FP._0_10;
    public int MaxTargets = 4;

    public override void Apply(Frame f, EntityRef entity)
    {
        f.AddOrGet<StatusSpreadOnDeath>(entity, out var spread);
        spread->TriggerOnVendettaKill = true;
        spread->Radius = FPMath.Max(spread->Radius, Radius);
        spread->BurnDuration = FPMath.Max(spread->BurnDuration, BurnDuration);
        spread->BurnIntensity = FPMath.Max(spread->BurnIntensity, BurnIntensity);
        spread->MaxTargets = spread->MaxTargets > MaxTargets ? spread->MaxTargets : MaxTargets;
    }
}
```

The 4 Fire Mastery traits follow the identical shape (`HotTargetPassiveUpgradeData` →
`f.AddOrGet<ConditionalCriticalModifier>`, `CremationPassiveUpgradeData` →
`f.AddOrGet<ExecuteAgainstStatus>`, `WildfirePassiveUpgradeData` → `f.AddOrGet<StatusSpreadOnDeath>`
with `TriggerOnAnyBurningDeath = true` instead, `FlashpointPassiveUpgradeData` →
`f.AddOrGet<ExplosionOnConditionalHit>`) - omitted here for brevity, all one-liner bakes matching the
pattern above.

## 3. Systems and responsibilities

| System | Type | Reacts to | Responsibility |
|---|---|---|---|
| `MaxVendettaSystem` | `SystemMainThread` + `ISignalOnHealthDamageApplied`, `ISignalOnShieldDamageApplied`, `ISignalOnEntityKilled` | New signals + existing kill signal | Mark creation/replacement, damage accumulation, heal-on-kill, mark consumption, Burning Vengeance trigger |
| `RevengeTargetTimeoutSystem` | `SystemMainThreadFilter<Filter{Entity, RevengeTarget}>` | Per-tick | Decrements `RemainingDuration`; on expiry, removes `RevengeTarget`+`StoredRevengeDamage` (mark lapses, no heal) - mirrors `WeaponSystem.TickKillerInstinct`'s exact shape |
| `MaxFireMasteryReactionSystem` | `SystemMainThreadFilter<Filter{Entity, ExplosionOnConditionalHit}>` + `ISignalOnCriticalHit`, `ISignalOnHealthDamageApplied`, `ISignalOnEntityKilled` | New + existing signals, plus per-tick cooldown ticking | Flashpoint (crit reaction), Cremation (health-damage reaction), Wildfire (kill reaction) |
| `DamageUtility.ResolveOutgoingDamage` (existing, extended) | n/a | n/a | Hot Target's live crit-chance read, one new `if` before the existing roll |
| `AreaQueryUtility` (new, generic) | static utility, no system | n/a | `FindEnemiesInRadius` capped query, reused by Wildfire + Flashpoint |
| `HealingUtility` (new, generic) | static utility, no system | n/a | `Heal`, reused by Vendetta's on-kill heal (and any future heal-granting mechanic) |

## 4. Event flow

**Damage flow (every hit, any source):**
1. `WeaponSystem`/skill/DoT-tick calls `DamageUtility.ApplyDamage(f, target, damage, owner, source, ...)`.
2. `ResolveOutgoingDamage` rolls the crit: base `CharacterStats.CriticalChance` (+`Weapon.CriticalChance`
   if source is Weapon) **+ `ConditionalCriticalModifier.CriticalChanceBonusVsBurning` if `owner` has
   one and `target` is currently Burning** (Hot Target, live-read, step added to this design).
3. Armor/Shield/Health split resolves as today; **new**: `OnShieldDamageApplied` fires if shield
   absorbed >0, then `OnHealthDamageApplied` fires if Health took >0 - both carry `directHit` (false
   for DoT-tick replays).
4. `MaxVendettaSystem.OnHealthDamageApplied`/`OnShieldDamageApplied` (the latter only if
   `ShieldDamageCountsForRevenge` present) create/refresh/accumulate the mark (see below).
5. `MaxFireMasteryReactionSystem.OnHealthDamageApplied` checks Cremation - if `target` is Burning,
   below its tier threshold, and `owner` has `ExecuteAgainstStatus`, forces `CurrentHealth = 0`.
6. `ApplyDamage`'s own existing `if (health->CurrentHealth <= FP._0)` death check now sees the
   execution (if any) and proceeds through the **unmodified** existing death branch - events, drops,
   `OnEntityKilled`, all fire exactly as they do for a normal kill.
7. If the hit was a crit, `OnCriticalHit` fires - `MaxFireMasteryReactionSystem` checks Flashpoint.

**Mark replacement/accumulation (inside `MaxVendettaSystem.TryAccumulate`):**
1. Bail if `target` (the damaged entity) has no `RevengeConfig` (not a Vendetta-passive holder).
2. Bail if `owner` is `EntityRef.None`, doesn't exist, isn't tagged `Enemy`, or is `Invulnerable` -
   no mark from environmental/invalid sources.
3. `f.AddOrGet<RevengeTarget>`/`f.AddOrGet<StoredRevengeDamage>`.
4. If `mark->Target != owner`: **replace** - overwrite `Target`, zero `StoredRevengeDamage.Amount`
   (a new attacker's mark starts its own tally; the old attacker's stored damage is discarded, since
   healing is scoped to "that marked target" only).
5. Refresh `RemainingDuration = config->MarkDuration` (any qualifying hit, same or new attacker,
   keeps the mark alive at full duration - it doesn't merely not-decay, it resets, so a sustained
   fight with one attacker never lets the mark lapse mid-fight).
6. `stored->Amount += amount`.

**Death / heal resolution (`MaxVendettaSystem.OnEntityKilled`):**
1. Bail unless `owner` (whoever got kill credit) has a `RevengeTarget` whose `Target == target`
   (the entity that just died) - i.e. Max must personally land the kill on his own marked target.
2. `heal = StoredRevengeDamage.Amount * RevengeConfig.HealMultiplier`; `HealingUtility.Heal` clamps
   to missing Health internally.
3. `f.Remove<RevengeTarget>` + `f.Remove<StoredRevengeDamage>` - mark consumed.
4. If `owner` has `StatusSpreadOnDeath` with `TriggerOnVendettaKill == true` (Burning Vengeance),
   spread Burn via `AreaQueryUtility.FindEnemiesInRadius` + `StatusEffectUtility.ApplyBurn`.

**Wildfire spread (`MaxFireMasteryReactionSystem.OnEntityKilled`, independent of Vendetta):**
1. Bail unless `owner` has `StatusSpreadOnDeath` with `TriggerOnAnyBurningDeath == true`.
2. Bail unless `target` was Burning at the moment of death (`StatusEffectUtility.IsBurning`, read
   before any deferred destroy).
3. `AreaQueryUtility.FindEnemiesInRadius(f, deathPosition, spread->Radius, exclude: target, spread->MaxTargets)`, `StatusEffectUtility.ApplyBurn` on each.

**Flashpoint explosion (`MaxFireMasteryReactionSystem.OnCriticalHit`):**
1. Bail unless `owner` has `ExplosionOnConditionalHit` with `CooldownRemaining <= 0`.
2. Bail unless `target` is currently Burning.
3. Set `CooldownRemaining = ProcCooldown` **before** calling `HitEffectUtility.ApplyExplosion` - this
   is what blocks same-tick reentrancy if the explosion's own damage crits a Burning enemy again.
4. `HitEffectUtility.ApplyExplosion(f, targetPosition, explosion->Radius, owner, damage * explosion->DamageCoefficient, source)` - ownership/kill-attribution flows through unmodified since `owner` stays Max the whole way down the existing `ApplyExplosion → ApplyDamageInRadius → ApplyDamage` chain.
5. If `AllowRecursiveProc == true`, the cooldown set in step 3 is deliberately skipped/shortened so a
   data-authored upgrade rank can explicitly permit chaining - off by default.

## 5. Quantum-compatible C# scaffolding

**New QTN** (`Assets/_QuantumUser/Simulation/QTN/Heroes/Max/Vendetta.qtn`):
```qtn
component RevengeConfig
{
    FP HealMultiplier;
    FP MarkDuration;
}

component RevengeTarget
{
    EntityRef Target;
    FP RemainingDuration;
}

component StoredRevengeDamage
{
    FP Amount;
}

component ShieldDamageCountsForRevenge
{
}

component StatusSpreadOnDeath
{
    Boolean TriggerOnVendettaKill;
    Boolean TriggerOnAnyBurningDeath;
    FP Radius;
    FP BurnDuration;
    FP BurnIntensity;
    Int32 MaxTargets;
}

component ConditionalCriticalModifier
{
    FP CriticalChanceBonusVsBurning;
}

component ExecuteAgainstStatus
{
    FP NormalHealthThreshold;
    FP EliteHealthThreshold;
    FP BossHealthThreshold;
    Boolean BossExecutionEnabled;
}

component ExplosionOnConditionalHit
{
    FP Radius;
    FP DamageCoefficient;
    FP ProcCooldown;
    FP CooldownRemaining;
    Int32 MaxTargets;
    Boolean AllowRecursiveProc;
}
```

**New signals** (add to `Combat.qtn`):
```qtn
signal OnHealthDamageApplied(EntityRef target, EntityRef owner, FP amount, DamageSource source, Boolean directHit);
signal OnShieldDamageApplied(EntityRef target, EntityRef owner, FP amount, DamageSource source, Boolean directHit);
```

**`DamageUtility.ApplyDamage` hook** (surgical edit around the existing Health/Shield split):
```csharp
FP mitigated = ReduceByArmor(f, target, totalDamage);
FP remaining = AbsorbWithShield(f, target, mitigated);
FP shieldAbsorbed = mitigated - remaining;
bool directHit = bypassOutgoingResolution == false;

if (shieldAbsorbed > FP._0)
    f.Signals.OnShieldDamageApplied(target, owner, shieldAbsorbed, source, directHit);

if (remaining > FP._0)
    f.Signals.OnHealthDamageApplied(target, owner, remaining, source, directHit);

health->CurrentHealth = FPMath.Max(FP._0, health->CurrentHealth - remaining);

if (health->CurrentHealth <= FP._0)
{
    // existing death branch, unchanged - Cremation's execution (which forced CurrentHealth to 0
    // inside the OnHealthDamageApplied handler just above) flows through here identically to a
    // normal kill.
    ...
}
```

**`ResolveOutgoingDamage` hook** (Hot Target - one `if` before the existing roll):
```csharp
FP chance = stats->CriticalChance;
FP multiplier = stats->CriticalDamageMultiplier;

if (source == DamageSource.Weapon && f.Unsafe.TryGetPointer<Weapon>(owner, out var weapon) == true)
{
    chance += weapon->CriticalChance;
    multiplier += weapon->CriticalDamageBonus;
}

if (f.Unsafe.TryGetPointer<ConditionalCriticalModifier>(owner, out var critMod) == true
    && StatusEffectUtility.IsBurning(f, target) == true)
{
    chance += critMod->CriticalChanceBonusVsBurning;
}

if (RollChance(f, chance) == false)
    return damage;
```

**`MaxVendettaSystem.cs`** (full sketch):
```csharp
[Preserve]
public unsafe class MaxVendettaSystem : SystemMainThread,
    ISignalOnHealthDamageApplied, ISignalOnShieldDamageApplied, ISignalOnEntityKilled
{
    public override void Update(Frame f) { }

    public void OnHealthDamageApplied(Frame f, EntityRef target, EntityRef owner, FP amount, DamageSource source, bool directHit)
        => TryAccumulate(f, target, owner, amount);

    public void OnShieldDamageApplied(Frame f, EntityRef target, EntityRef owner, FP amount, DamageSource source, bool directHit)
    {
        if (f.Unsafe.TryGetPointer<ShieldDamageCountsForRevenge>(target, out _) == false)
            return;

        TryAccumulate(f, target, owner, amount);
    }

    private static void TryAccumulate(Frame f, EntityRef target, EntityRef owner, FP amount)
    {
        if (f.Unsafe.TryGetPointer<RevengeConfig>(target, out var config) == false)
            return;

        if (IsValidVendettaAttacker(f, owner) == false)
            return;

        f.AddOrGet<RevengeTarget>(target, out var mark);
        f.AddOrGet<StoredRevengeDamage>(target, out var stored);

        if (mark->Target != owner)
        {
            mark->Target = owner;
            stored->Amount = FP._0;
        }

        mark->RemainingDuration = config->MarkDuration;
        stored->Amount += amount;
    }

    private static bool IsValidVendettaAttacker(Frame f, EntityRef owner)
    {
        return owner != EntityRef.None
            && f.Exists(owner) == true
            && f.Has<Enemy>(owner) == true
            && f.Has<Invulnerable>(owner) == false;
    }

    public void OnEntityKilled(Frame f, EntityRef target, EntityRef owner, DamageSource source)
    {
        if (f.Unsafe.TryGetPointer<RevengeTarget>(owner, out var mark) == false || mark->Target != target)
            return;

        if (f.Unsafe.TryGetPointer<StoredRevengeDamage>(owner, out var stored) == true
            && f.Unsafe.TryGetPointer<RevengeConfig>(owner, out var config) == true)
        {
            HealingUtility.Heal(f, owner, stored->Amount * config->HealMultiplier);
        }

        f.Remove<RevengeTarget>(owner);
        f.Remove<StoredRevengeDamage>(owner);

        if (f.Unsafe.TryGetPointer<StatusSpreadOnDeath>(owner, out var spread) == true
            && spread->TriggerOnVendettaKill == true
            && f.Unsafe.TryGetPointer<Transform3D>(target, out var transform) == true)
        {
            FireMasterySpreadUtility.SpreadBurn(f, transform->Position, owner, spread);
        }
    }
}
```

`RevengeTargetTimeoutSystem.cs` mirrors `WeaponSystem.TickKillerInstinct` exactly (decrement,
remove-both-components at zero, no heal on this path - a lapsed mark is not a consumed mark).

`FireMasterySpreadUtility.SpreadBurn` (shared by Burning Vengeance and Wildfire) and
`AreaQueryUtility.FindEnemiesInRadius`/`HealingUtility.Heal` are omitted here for length - each is a
10-20 line static method following the exact `WeaponPerkUtility.TryFindNearestEnemy` overlap-query
shape (for the area query) and `DamageUtility.ApplyDamage`'s health-clamp shape (for heal). Exact
`ApplyBurn` tick-interval/damage-per-tick argument choice should be cross-checked against
`BurnEffectData`'s own call site before implementation - not fully re-derived here.

**Verify before implementing** (not confirmed by research, flagged rather than guessed):
- Exact accessor for an enemy's tier (Normal/Elite/Boss) for Cremation's threshold selection - check
  `EnemyTierStatsConfig`/`Enemy.qtn` for the real field/enum name.
- `StatusEffectUtility.ApplyBurn`'s exact tick-interval convention for a non-hit-triggered application
  (Wildfire/Burning Vengeance apply Burn outside the normal `HitEffectData` pipeline `BurnEffectData`
  uses) - confirm the right `tickInterval`/`damagePerTick` inputs to pass.

## 6. Which systems are generic and reusable by other heroes

- `OnHealthDamageApplied`/`OnShieldDamageApplied` signals + the `ApplyDamage` hook - **fully generic**,
  fires for every entity/source in the game, zero Max awareness. Any future "reacts to taking
  Health/Shield damage" mechanic (any hero) reuses this for free.
- `AreaQueryUtility.FindEnemiesInRadius` - **fully generic**, just `OverlapShape` + the same
  `Enemy`/`Dead`/`Invulnerable` filter every other area query already uses, with a cap. Directly
  reusable by Flashpoint, Wildfire, and any future capped-radius effect.
- `HealingUtility.Heal` - **fully generic**, no Max/Vendetta awareness at all.
- The `ConditionalCriticalModifier` hook inside `ResolveOutgoingDamage` - **generic mechanism**
  (any owner with the component gets a conditional crit bonus vs a Burning target); the *condition*
  (Burning) is currently hardcoded to this one status check since that's all the brief asks for, but
  the component/hook shape would extend cleanly to a `StatusFlag` field if a future hero wanted "bonus
  crit vs Stunned" etc.
- `MaxVendettaSystem`/`MaxFireMasteryReactionSystem`/`RevengeTargetTimeoutSystem` themselves are
  **not generic** - they're Max-specific reaction systems, exactly mirroring how
  `WeaponPerkReactionSystem` is itself a generic *pattern* but a specific, single system instance.
  A future hero with an analogous "mark and consume" passive would write its own sibling system
  following the same shape, not extend this one - keeps the "if hero is Max" branching out of shared
  code entirely, at the cost of some structural duplication between hero-specific reaction systems
  (an accepted, precedented tradeoff in this codebase already).

## 7. Justification for every new component

- **`RevengeConfig`** - persists indefinitely (character lifetime), needed because Settled Score and
  Blood Debt must permanently modify values read at arbitrary future mark-creation moments; can't be
  transient since nothing else re-supplies these values per-tick.
- **`RevengeTarget`** - must persist across many ticks (the mark lasts 8+ seconds); the entity
  reference plus its countdown timer are exactly the kind of "state that must persist between
  simulation ticks" the brief requires a component for.
- **`StoredRevengeDamage`** - same persistence requirement as `RevengeTarget`, kept as a separate
  component (not folded into `RevengeTarget`) because it changes at a different frequency (only on
  a qualifying hit) than `RevengeTarget.RemainingDuration` (every tick) - see the bloat review below
  for the counter-argument considered and rejected.
- **`ShieldDamageCountsForRevenge`** - a permanent, binary, per-character flag; a tag component is
  the minimum possible footprint for "is this upgrade active," and it must persist (it's not a
  one-frame decision).
- **`StatusSpreadOnDeath`** - permanent config (radius/duration/intensity/cap), composed by up to two
  different upgrades; must persist since it's read at an unpredictable future death event, not
  computable from anything transient.
- **`ConditionalCriticalModifier`** - permanent per-character bonus, read at every future crit roll;
  same reasoning as `RevengeConfig` - the alternative (baking into `CharacterStats.CriticalChance`)
  is explicitly forbidden by the brief ("do not permanently modify Max's base Critical Chance").
- **`ExecuteAgainstStatus`** - permanent per-tier thresholds; must persist, read at an unpredictable
  future damage event.
- **`ExplosionOnConditionalHit`** - permanent config *plus* a live cooldown timer that must survive
  across ticks (`CooldownRemaining`) - a textbook case for "state must persist between ticks."

None of the 8 exist merely to pass data between systems within one tick - every one is read on a
*different* tick than the one that wrote it (often much later), which is precisely the bar the
brief sets for justifying a persistent component over a transient event/request.

## 8. Component-bloat review — what stays transient, and alternatives considered

**Kept transient (no component):**
- **"Apply Burn"** - always a direct `StatusEffectUtility.ApplyBurn` call, never a queued/component-
  backed request. Burn's own persistent state already lives on the existing `StatusEffects`
  component (out of scope to touch here).
- **"Heal entity"** - a plain `HealingUtility.Heal` call inside the `OnEntityKilled` handler, same
  tick as the kill. No "pending heal" component.
- **"Clear Vendetta target"** - just `f.Remove<RevengeTarget>()`/`f.Remove<StoredRevengeDamage>()`
  calls, not a request/event that something else processes later.
- **"Execute target"** - realized as a single-line `health->CurrentHealth = FP._0` mutation inline in
  the `OnHealthDamageApplied` handler, deliberately *not* a new "pending execution" component or a
  bespoke kill call - this is what lets it fall through the existing death pipeline for free, as the
  brief requires.
- **Flashpoint's explosion itself** - synchronous `HitEffectUtility.ApplyExplosion` call, matching
  every existing explosion in this codebase (research confirmed there is no deferred/queued
  explosion pattern anywhere today) - no new "pending explosion" component needed since Flashpoint's
  trigger (a crit) and its effect (the explosion) are correctly same-tick, unlike `ExplodeOnDeath`
  (which genuinely needs to wait for a future, unknown-timing death and *does* justify a component).

**Alternatives considered and rejected:**
- **Merging `StoredRevengeDamage` into `RevengeTarget`** (`RevengeTarget { EntityRef Target; FP RemainingDuration; FP StoredDamage; }`) - would save one component type, and the two are always
  added/removed together (identical lifetime). Rejected because they change at meaningfully
  different frequencies (`RemainingDuration` every tick via the timeout system; `StoredDamage` only
  on qualifying hits), and the brief explicitly calls out both "split components by... update
  frequency" and lists them as two separate example components - if a future profiling pass shows
  the split doesn't matter in practice, merging is a trivial, low-risk follow-up.
- **One component per upgrade for `StatusSpreadOnDeath`** (`BurningVengeanceSpread`/`WildfireSpread`
  as two distinct types) - would avoid the shared-component composition complexity. Rejected in favor
  of composing onto one component via `FPMath.Max`, directly matching the already-precedented
  `WeaponRampState` pattern in this exact codebase (3 different perks feeding 1 shared component) -
  consistency with an established, working pattern outweighs the marginal simplicity of two types.
- **A single monolithic `MaxKit` component** holding every field above - this is the literal
  "one large Max-specific component" the brief explicitly forbids; rejected outright, called out here
  only to make the rejection explicit and intentional rather than assumed.
- **Reusing `Adrenaline`'s shape** (one big per-hero passive component, matching how Adrenaline Rush
  itself is built) - Adrenaline Rush is a real, precedented pattern in this *specific* codebase, so
  there's a legitimate case for consistency-with-existing-code here. Rejected in favor of the brief's
  explicit, detailed composition-over-inheritance mandate, which should be read as intentionally
  steering away from the Adrenaline Rush shape, not unaware of it - flagged here so this tradeoff is
  visible rather than silently overridden.

## 9. Edge cases and deterministic handling

| Edge case | Handling |
|---|---|
| Environmental damage (`owner == EntityRef.None`) | `IsValidVendettaAttacker` rejects it - no mark created, matches the explicit requirement. |
| Attacker dies/despawns while marked | `RevengeTarget.Target` becomes a stale `EntityRef`; the timeout system still expires it normally. `OnEntityKilled`'s own `target` parameter is what's compared against `mark->Target`, so a *different* death can never accidentally match a stale mark. |
| Two different enemies damage Max in the same tick | Deterministic by dispatch order - `DamageUtility.ApplyDamage` calls are already sequential (single-threaded Quantum simulation), so whichever call happens second in that tick's fixed processing order wins the replace, consistently across all clients. |
| Max's own Vendetta-mark holder is Max himself (self-damage) somehow | `Enemy` tag check in `IsValidVendettaAttacker` excludes it - Max is never `Enemy`-tagged. |
| Enemy deals Shield damage without Unbroken Spirit | `OnShieldDamageApplied` handler bails immediately on the `ShieldDamageCountsForRevenge` check - no mark/accumulation side effect at all, confirmed by design (not just "heals for 0"). |
| Vendetta target killed by something *other than Max* (e.g. another player, a DoT not attributed to Max) | `OnEntityKilled`'s `owner` won't be Max, so `MaxVendettaSystem`'s `mark->Target != target` /owner-mismatch check fails - no heal, mark is **not** consumed (per requirement: only *killing* it consumes the mark - if someone else kills it, the mark simply keeps ticking down or gets replaced on the next hit Max takes). |
| Healing would exceed Max's missing Health | `HealingUtility.Heal` clamps internally (`min(amount, MaxHealth - CurrentHealth)`), never overheals. |
| Cremation threshold check on an already-dead/destroyed target | Guarded by the existing `Health`/`MaxHealth <= 0` early-out already present in the scaffolding; a target with no `Health` component can't reach this handler at all (the signal only fires for entities `ApplyDamage` already validated have `Health`). |
| Wildfire chain across many ticks (A's death ignites B, B's later death ignites C, ...) | Bounded structurally: Wildfire never deals lethal damage itself, only applies Burn - the earliest a chained death can occur is a *later* tick's `StatusEffectSystem` burn-tick, never nested in the same call stack as the triggering death, so there is no same-tick unbounded recursion. Chain *length* across many ticks is naturally bounded by `MaxTargets`/`Radius` and the finite number of enemies actually present. |
| Flashpoint's own explosion re-triggers Flashpoint (its damage crits a Burning enemy) | `CooldownRemaining` is set *before* `ApplyExplosion` is called, so the nested `OnCriticalHit` (if any) sees a non-zero cooldown and bails - unless `AllowRecursiveProc` is explicitly set, per requirement. |
| Multiple systems subscribed to the same signal in one tick (`MaxVendettaSystem` and `MaxFireMasteryReactionSystem` both implement `ISignalOnEntityKilled`) | Deterministic: Quantum invokes signal subscribers in system-registration order, which is a fixed list in `SystemSetup.User.cs` - identical on every client. |
| A hit that is simultaneously a crit *and* lethal *and* triggers Cremation | Order is fixed by `ApplyDamage`'s own source-code sequence: Shield/Health signals fire → Cremation's execution (if any) mutates `CurrentHealth` → the existing death check → `OnEntityKilled` (Vendetta heal + Burning Vengeance/Wildfire) → separately, `OnCriticalHit` fires wherever `ApplyDamage` already fires it relative to the death branch (before mitigation, per existing code) - Flashpoint's explosion is a *separate* effect from the execution, both can legitimately fire off the same hit. |

## 10. Implementation order (MVP)

1. **Core additions first, standalone, no Max content yet**: `OnHealthDamageApplied`/
   `OnShieldDamageApplied` signals + the `ApplyDamage` hook; `AreaQueryUtility.FindEnemiesInRadius`;
   `HealingUtility.Heal`. Each is independently testable against existing gameplay (should be a
   no-op for everything that isn't Max yet) before any Vendetta code exists.
2. **Vendetta base only**: `Vendetta.qtn` (just `RevengeConfig`/`RevengeTarget`/`StoredRevengeDamage`),
   `VendettaPassiveData`, `MaxVendettaSystem` (mark creation/accumulation/heal-on-kill, no Unbroken
   Spirit/Burning Vengeance yet), `RevengeTargetTimeoutSystem`. Wire `MaxCharacterData.Passive` to it,
   remove `AdrenalineRushPassiveData` and its 4 upgrades from `MaxCharacterData.PassiveUpgrades`.
   Playable/testable end-to-end at this point.
3. **Remaining 3 Vendetta upgrades**: `ShieldDamageCountsForRevenge` + `UnbrokenSpiritPassiveUpgradeData`
   (extend `MaxVendettaSystem.OnShieldDamageApplied`); `SettledScorePassiveUpgradeData`;
   `BloodDebtPassiveUpgradeData`; `StatusSpreadOnDeath` (Vendetta-kill trigger only) +
   `BurningVengeancePassiveUpgradeData` + the spread call in `MaxVendettaSystem.OnEntityKilled`.
4. **Fire Mastery, independent of Vendetta**: `ConditionalCriticalModifier` +
   `HotTargetPassiveUpgradeData` + the `ResolveOutgoingDamage` hook (simplest trait, good next step -
   no new system needed at all).
5. **`ExecuteAgainstStatus` + `CremationPassiveUpgradeData`** + `MaxFireMasteryReactionSystem`'s
   `OnHealthDamageApplied` handler (first use of the new reaction system) - verify the enemy-tier
   accessor question flagged in §5 before writing this.
6. **`StatusSpreadOnDeath`'s any-burning-death trigger + `WildfirePassiveUpgradeData`** +
   `MaxFireMasteryReactionSystem.OnEntityKilled` (reuses the same spread utility Burning Vengeance
   already established in step 3).
7. **`ExplosionOnConditionalHit` + `FlashpointPassiveUpgradeData`** +
   `MaxFireMasteryReactionSystem`'s `OnCriticalHit` handler and its per-tick cooldown `Update` -
   last, since it's the only piece needing the Filter-based per-tick ticking half of the system.
8. Author all 8 `.asset` instances (an `Editor` generator following `WeaponPerkAssetGenerator.cs`'s
   pattern would be the natural authoring tool, matching this codebase's existing convention for
   every other multi-asset content pool), wire the 8 entries into `MaxCharacterData.PassiveUpgrades`,
   delete the retired Adrenaline Rush files, update `CLAUDE.md`/register in `SystemSetup.User.cs`.
