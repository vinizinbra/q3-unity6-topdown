# Max — Ascensions (Overdrive / Vendetta / Fire Mastery)

Replaces `docs/max-vendetta-fire-mastery.md` and `docs/max-berserk-rage.md` (both deleted). Max's kit
grew organically before this refactor: several of his Overdrive `Actions` shipped `Activated: 1`
(always-on for every player, not gated picks), a dead parallel Rage system (`Adrenaline`) sat fully
registered and silently no-oping since nothing granted the component anymore, and two entirely
unrelated mechanics were both named "Too Angry to Die". This doc covers the result: exactly 10
three-rank Ascension lines (Overdrive ×4, Passive/Vendetta+Fire Mastery ×4, Dash ×2), reusing the
identical generic rank architecture already established for Pixie/Brute (see
`docs/level-up-upgrades.md`'s own ranking section) - **zero Max-specific rank code**, and every line
authors `RankDescriptions` as real data from the start (see that same doc's `RankDescriptions` note).

Read `docs/level-up-upgrades.md` first if you haven't touched a ranked Ascension line before - `IRankedUpgrade`/`MaxRank`/`SkillUpgradeUtility.GetRank`/`RankDescriptions` are all established there, not
re-explained here.

## Baseline (not Ascensions)

- **Normal Fire Rate (+20%)**: `MaxCharacterData.AttackSpeedMultiplier = 1.20` - a pure data change on
  the same field every hero already has for their own baseline identity.
- **Overdrive's own Fire Rate math**: `BerserkSkillData.Begin/End` multiply/divide `CharacterStats.
  AttackSpeedMultiplier` by `(1 + FireRateBonus)`. Since baseline is 1.20 (not 1.0), `FireRateBonus =
  0.25` lands exactly on "+50% total during Overdrive, not +70%" with **zero "replace not stack"
  logic** - `1.20 * 1.25 = 1.50` is just correct algebra on the existing multiplicative Begin/End
  composition. `MoveSpeedBonus` (0.25) / `ReloadSpeedBonus` (0.30) are unaffected - no baseline bonus
  exists on those to compound with.
- **Rage is no longer a stat-correction mechanism.** The old `RageOverdriveUtility.ApplyCorrection`/
  `Correction` (a single threshold-flip multiplier swap the instant `Stacks == MaxStacks`) is deleted.
  `RageOverdrive.qtn` shrank to `Byte Stacks; Byte MaxStacks;` - reaching max Rage
  (`RageOverdriveUtility.IsAtMaxRage`) is now a pure boolean condition with **no baked-in effect of
  its own**; Full Throttle and Ignition are what react to it, on their own terms. `TryAdvanceStack`
  (hooked from `DamageUtility.ApplyDamage`'s weapon-hit path) / `ResetStacks` (hooked from
  `MaxOverdriveReactionSystem`'s damage-taken signals) keep their build/reset roles.
- **Entire dead Adrenaline system deleted**: `Adrenaline.qtn`, `AdrenalineSystem.cs`,
  `AdrenalineUtility.cs`, `AdrenalineRushPassiveData.cs`, its 4 old upgrades (`HotBlooded`,
  `BattleHigh`, an Adrenaline-flavored `TooAngryToDie` - distinct from the live `CheatDeathGuard`-
  based Hero Skill Ascension of the same in-fiction name, `NoTimeToBreathe`), `AdrenalineInjectionSkillAction.cs`, `MaxAdrenalineAssetGenerator.cs`, its `SystemSetup.User.cs`
  registration, the dead `AdrenalineUtility.OnDamageDealt/OnDamageTaken/GetFireRateMultiplier` calls
  in `DamageUtility`/`StatUtility`/`PlayerMovementProcessor`, and the dedicated `AdrenalineUiWidget`
  (+ its wiring in `PartyHudWidget`). **"Too Angry to Die" naming collision resolved**: only the live,
  `CheatDeathGuard`-based mechanism survives, folded into Last Stand rank 3 below.

## New shared primitives

- **`IsEligible(Frame f, EntityRef entity)`** - new virtual hook (default `true`) on both
  `PassiveUpgradeData`/`SkillActionData`, checked by `LevelUpUtility`'s candidate-collection methods
  alongside the existing rank/already-picked filters. This is what "Flashpoint shouldn't draft until
  Max has a real Burn source" needs, and is reusable by any future hero's own prerequisite-gated pick
  - not a Max-specific mechanism. Backed by a new tiny permanent tag, **`CanApplyBurn {}`**, granted
  (never removed) by every Burn-granting Ascension (Ignition rank 1, Burning Vengeance rank 1,
  Vendetta Strike rank 1); Flashpoint overrides `IsEligible` to check `f.Has<CanApplyBurn>(entity)`.
- **`MaxAscensionUtility.cs`** (mirrors `BruteAscensionUtility.cs`) - `ApplyFullThrottle`/
  `RevertFullThrottle` (Full Throttle's enter/exit-max-Rage toggle), `OnEnteredMaxRage`/
  `RevertIgnition` (Ignition rank 1's Burn-on-hit toggle + rank 3's once-per-activation Inferno
  trigger), `ApplyRadialBurn` (damage + Burn to everyone in a radius - Ignition rank 3's Inferno pulse
  and Burning Vengeance rank 3's fiery burst both call this).
- **`StatusEffects.qtn` additions**: `TemporaryWeaponDamageRemaining/Amount` (Last Stand rank 2's
  Retaliation proc, Run & Gun rank 2 - take-the-stronger/longer semantics on reapply, same shape as
  the pre-existing `TemporaryDamageReduction` pair), `RetaliationCooldownRemaining` (Last Stand rank
  2's own proc cooldown, read/written directly by `MaxOverdriveReactionSystem`, same idiom Brute's
  `GuardianReactiveCooldownRemaining` uses), `NoAmmoConsumptionRemaining` (Run & Gun rank 3, checked
  directly by `WeaponSystem` right before its own unconditional `Weapon.Ammo--`).
- **Vendetta base damage bonus** (new - never existed before this refactor): `RevengeConfig.
  DamageBonus` (seeded to 0.15 by `VendettaPassiveData.Apply`), read in `DamageUtility.
  ResolveOutgoingDamage` alongside the existing Unstable-Targeting/Stun-Damage-Bonus idiom - no
  `DamageSource` restriction, so it applies to weapon/skill/dash/Burn-tick damage alike (a Burn tick's
  own `BurnOwner` flows through this same resolution for free). Marking itself stays purely reactive
  on the base passive (an enemy has to hit Max first) - a generic "Max's own weapon hits also mark"
  hook was tried and reverted: with the bonus above applying to any marked enemy, it meant every enemy
  near Max got bonus-damaged automatically the instant his auto-fire reached it, indistinguishable
  from a flat damage buff with zero player choice involved. Vendetta Strike (rank 2+) remains the one
  deliberate way to mark proactively.
- **Vendetta on-kill heal floors**: `RevengeConfig.MinHealFraction` (1%, off Max's own MaxHealth) and
  `EnemyMaxHealthFraction` (5%, off the killed enemy's own MaxHealth - a decisive placeholder, not
  balance-passed) both seeded by `VendettaPassiveData.Apply`. `MaxVendettaSystem.OnEntityKilled`'s heal
  is the highest of `StoredDamage * HealMultiplier` and both floors, so a kill on a mark that only
  landed a light hit (or a Vendetta Strike proactive mark with zero banked StoredDamage) still heals
  something, and a genuinely tough enemy heals more even at the floor.
- **Vendetta auto-targeting**: `AimSystem.FindClosestTarget` now does a first pass restricted to
  candidates already carrying this entity's own `RevengeMark`, closest first, falling through to the
  plain closest-overall pass if nothing marked is in range. Sits entirely inside the existing
  "otherwise-valid candidates" resolution, so a sticky manual `LockedTarget` (checked earlier in
  `AimSystem.ResolveTarget`) still wins outright - manual/sticky lock > Vendetta priority > normal
  closest, for free from call order alone.

## The 10 lines

### Overdrive (`SkillActionData` on `MaxHeroSkill.Actions`, `Phase = Begin` unless noted - every
granted component here is safe to leave equipped between activations, since every reaction that reads
it already gates on Overdrive/max-Rage actually being live, so none of these classes bother
pairing Begin with an End-time revoke)

1. **Last Stand** (`LastStandSkillAction`) - rank 1 "Unshaken" grants `RageRetentionUpgrade` (a plain
   tag `RageOverdriveUtility.ResetStacks` checks, renamed from the old standalone "Rage Overdrive"
   pick it used to belong to). Rank 2 "Retaliation" additionally sets `LastStandUpgrade.
   HasRetaliation`, read by `MaxOverdriveReactionSystem.OnHealthDamageApplied`/
   `OnShieldDamageApplied` - while Overdrive is active (`RageOverdrive` present) and off cooldown,
   grants a brief Weapon Damage buff via `ApplyTemporaryWeaponDamage`. Rank 3 "Too Angry to Die"
   grants `CheatDeathGuard` (unchanged mechanism - forces Overdrive to end, clamps to 1 HP, opens a
   brief `Invulnerable` window) **and** `CheatDeathUtility.TryPreventLethal` now also calls
   `RageOverdriveUtility.Revert` + zeroes `RageOverdrive.Stacks` right there (not `ResetStacks`,
   which would be blocked by rank 1's own `RageRetentionUpgrade` a rank-3 holder always also carries)
   - so a Full Throttle/Ignition effect active at max Rage can't linger for the one tick before
     `BerserkSkillData.End` would otherwise discover it.

2. **Full Throttle** (`FullThrottleSkillAction`) - active only while Overdrive is active AND
   `RageOverdriveUtility.IsAtMaxRage`. Grants `FullThrottleUpgrade` (`WeaponDamageBonus`/
   `ReloadSpeedBonus`/`Applied`), toggled at the max-Rage threshold by `RageOverdriveUtility.
   TryAdvanceStack`/`ResetStacks` via `MaxAscensionUtility.ApplyFullThrottle`/`RevertFullThrottle` -
   same enter/exit-threshold toggle shape `JuggernautSkillData.UpdateSpeedBoost` established for
   Brute's Charged-speed tier. Rank 1: +20% Weapon Damage. Rank 2: +30%/+50% Reload Speed. Rank 3:
   +40%, plus grants the pre-existing `InstantReloadOverdrive` tag - `WeaponSystem.
   IsInstantReloadOverdriven` now checks `RageOverdriveUtility.IsAtMaxRage` directly (the old
   `RageOverdrive.Overdriven` flag it used to read no longer exists).

3. **Uncontrolled Fury** (`UncontrolledFurySkillAction`) - `UncontrolledFuryExtension` gained
   `KillCount`/`KillsPerExtension` (every-N-kills gating, not every kill) and `VendettaKillExtension`
   (rank 3's own separate, **uncapped** bonus for killing a Vendetta-marked enemy, independent of the
   capped `AccumulatedExtension` pool). Both handled in `MaxOverdriveReactionSystem.OnEntityKilled`.
   Rank 1: every 3 kills, +1s, cap +3s. Rank 2: every 2 kills, +1s, cap +5s. Rank 3: every 2 kills,
   +1s, cap +7s, + uncapped +2s per Vendetta-marked kill. **Registration order**:
   `MaxOverdriveReactionSystem` is registered *before* `MaxVendettaSystem` in `SystemSetup.User.cs` -
   both react to the same `OnEntityKilled` dispatch, and rank 3's Vendetta-kill bonus has to read
   `RevengeMark.MarkedBy` before `MaxVendettaSystem`'s own handler removes that mark.

4. **Ignition** (`IgnitionSkillAction`) - every effect gated on `RageOverdriveUtility.IsAtMaxRage`.
   Rank 1 toggles `CharacterStats.BurnOnHitStacks` on/off at the max-Rage threshold (driven by
   `MaxAscensionUtility.OnEnteredMaxRage`/`RevertIgnition`, not this class's own `Execute`) - reuses
   the existing, already-generic `TryApplyGuaranteedBurn` weapon-hit hook. Rank 2 drops Burning Ground
   patches (`SpawnedEntitySpawner.Spawn`) every `BurningGroundSpacing` units travelled while at max
   Rage - the OnGoing half of this class, distance-paced the same way `SpawnEntitySkillAction.Spacing`
   already is, reimplemented directly since the max-Rage gate is Max-specific. Rank 3 "Inferno" fires
   one `MaxAscensionUtility.ApplyRadialBurn` pulse the *first* time max Rage is reached each
   activation (`IgnitionUpgrade.InfernoTriggeredThisActivation`, reset by this class's own Begin).

### Passive (`PassiveUpgradeData`, composing onto `RevengeConfig`/`StatusSpreadOnDeath`/Fire Mastery
components - same shared-component idiom `RevengeConfig` itself already established)

5. **Blood Debt** (`BloodDebtPassiveUpgradeData`, merges the old Blood Debt + Unbroken Spirit +
   Settled Score picks) - rank 1: `RevengeConfig.MarkDuration = 12`. Rank 2: `= 16`, plus grants
   `ShieldDamageCountsForRevenge`. Rank 3: `RevengeConfig.HealMultiplier = 1.0`.

6. **Burning Vengeance** (`BurningVengeancePassiveUpgradeData`) - ranks 1-2 set `StatusSpreadOnDeath.
   TriggerOnVendettaKill = true` + Radius/BurnDuration/BurnIntensity/`MaxTargets` (existing mechanism,
   ranked). Rank 3 sets the new `StatusSpreadOnDeath.HasFieryBurst` - inside `MaxVendettaSystem.
   OnEntityKilled`'s existing Vendetta-kill spread branch, if the kill was already Burning, also fires
   one `MaxAscensionUtility.ApplyRadialBurn` pulse at the death position (reusing the same Radius/
   BurnDuration/BurnIntensity, not a second set of fields). Also grants `CanApplyBurn`.

7. **Wildfire** (`WildfirePassiveUpgradeData`) - ranks 1-2 set `StatusSpreadOnDeath.
   TriggerOnAnyBurningDeath = true` + Radius/`MaxTargets` (existing mechanism, ranked). Rank 3 sets
   the new `StatusSpreadOnDeath.WildfireRetainedFraction` - `MaxFireMasteryReactionSystem.
   OnEntityKilled` then reads the dying enemy's own live `StatusEffects.BurnRemaining`/
   `BurnDamagePerTick` (scaled by that fraction) instead of the flat authored values. "Enemies
   ignited by Wildfire can themselves spread it again" needed zero extra code beyond that -
   `TriggerOnAnyBurningDeath` lives on the owner, not per-enemy, and `OnEntityKilled` fires once per
   actual death event so same-tick recursion is already structurally impossible.

8. **Flashpoint** (`FlashpointPassiveUpgradeData`, merges the old Hot Target + Flashpoint + Cremation
   picks) - rank 1 "Hot Target": `ConditionalCriticalModifier.CriticalChanceBonusVsBurning = 0.10`
   (existing mechanism, ranked); gated by `IsEligible` on `CanApplyBurn`. Rank 2 "Flashpoint":
   `ExplosionOnConditionalHit` (Radius 3, DamageCoefficient 0.50, ProcCooldown 2s, MaxTargets 5). Rank
   3 "Cremation": `ExecuteAgainstStatus` (Normal/Specialist 15%, Elite 10%) - `BossExecutionEnabled`
   is never set true by this line at all, dropping the old per-pick toggle entirely: Boss is never
   executable, full stop.

### Dash (`SkillActionData`, ranked)

9. **Run & Gun** (`RunAndGunSkillAction`, replaces `ReloadingSlideSkillAction`) - `Phase = End`.
   Generalizes the old flat `Ammo += ceil(MagazineSize * fraction)` restore to a per-rank fraction
   (0.50/1.0/1.0), plus `StatusEffectUtility.ApplyHaste` (the entity is its own Haste source, same as
   any other self-buff) for a Fire Rate window (0.20/0.30/0.40, 2s). Rank 2 also calls
   `ApplyTemporaryWeaponDamage` (+15%, 2s). Rank 3 also sets `StatusEffects.
   NoAmmoConsumptionRemaining = 2`.

10. **Vendetta Strike** (`VendettaStrikeSkillAction`) - `Phase = Begin | OnGoing`, `Interval = 0`. Each
    rank unlocks a genuinely new effect rather than scaling the same numbers. Rank 1: applies a
    guaranteed Burn to any enemy caught in the dash sweep - no Vendetta mark yet, Burn only; also
    grants `CanApplyBurn`. Rank 2: also creates or refreshes a `RevengeMark` on that enemy, even one
    that's never damaged Max - unlike the base passive (purely reactive), this is a deliberate pick,
    which is what makes proactive marking a real choice rather than automatic (see the base-damage-
    bonus bullet above on why a generic "any weapon hit marks" version was reverted). Rank 3: also
    rewards landing the strike depending on Overdrive's own live state (`RageOverdrive`'s presence) -
    reduces the Hero Skill's cooldown by 2s (`SkillSystem.ReduceCooldown`) if dormant, or extends the
    current Overdrive activation by 1s (`OverdriveUtility.TryExtend`, same mechanism Uncontrolled Fury
    uses) if already active. Procs at most once per enemy per dash - `VendettaStrikeHitTracker`,
    granted fresh on this action's own Begin phase, same shape/precedent as Brute's
    `IronShoulderHitTracker`.

## Current status / Editor authoring needed

Code compiles once codegen picks up every changed/new `.qtn` (`RageOverdrive` shrunk;
`RevengeConfig`/`RevengeMark`/`StatusSpreadOnDeath`/`StatusEffects`/`UncontrolledFuryExtension` all
gained fields; `FullThrottleUpgrade`/`IgnitionUpgrade`/`LastStandUpgrade`/`CanApplyBurn`/
`VendettaStrikeHitTracker` are new) and `SystemSetup.User.cs`'s `MaxOverdriveReactionSystem`/
`MaxVendettaSystem` reordering. Not yet run/verified in-Editor:

1. Run `Tools > RiftRaiders > Max > Generate Ascension Assets` (and `Generate All Assets`) - authors
   and fully rewires all 10 lines, replacing `MaxCharacterData.PassiveUpgrades`/`.DashSkillUpgrades`
   and `MaxHeroSkill.Actions` end to end, and sweeps `MaxHeroSkill.asset`'s dead embedded sub-objects
   (every pre-refactor baseline/orphaned action, live-class instances included - the generic classes
   they point at still serve other heroes elsewhere, only Max's own private embedded copies go away).
   `MaxCharacterData.PassiveUpgrades`/`.DashSkillUpgrades` were already hand-trimmed to drop dangling
   references to the deleted classes' `.asset` files ahead of the generator's own full rewrite.
2. Ignition rank 2's `BurningGroundPrototype` field is pointed at the pre-existing
   `MaxBurningGroundEntityPrototype.qprototype` by the generator - confirm it actually resolves once
   run (logs a warning if not).
3. `PartyHudWidget`'s prefab may still have a `AdrenalineUiWidget` component reference on a child
   GameObject from before that class was deleted - will show as a missing-script warning until
   removed by hand in the Editor (a scene/prefab edit, not something safe to script blindly).
4. In-Editor playtest: confirm Normal Max reads +20% Fire Rate and Overdrive reads +50% total (not
   +70%); Rage building/resetting has zero stat side-effects on its own; Full Throttle/Ignition's
   effects turn on/off exactly at the Rage-max threshold, not gradually; Last Stand rank 3 ends
   Overdrive and clears Rage without leaving Max in Overdrive while invulnerable; Uncontrolled Fury's
   kill-count gating (not every-kill) and the separate uncapped Vendetta-kill +2s; Vendetta
   auto-targeting only kicks in among otherwise-valid targets and yields to a fresh manual/sticky
   lock; Vendetta Strike rank 1 is Burn-only (no mark) and rank 2+ correctly marks even unmarked
   enemies, with its per-dash hit-tracker preventing multi-proc; Flashpoint doesn't appear in the
   draft until a real Burn source is picked.
5. Grep for any remaining reference to a deleted class/component name before considering this closed.

---

# 2026-08-20 balance pass

Max drops from 10 lines to the target **9 lines × 3 ranks**. Overdrive and Dash keep their four/two
lines unchanged in shape; the Passive half loses one line to a merge.

## Roster now

| Pool | Lines |
|---|---|
| Overdrive (Hero Skill) | Last Stand, Full Throttle, Uncontrolled Fury, Ignition |
| Passive | Blood Debt, Wildfire, Flashpoint |
| Dash | Run & Gun, Vendetta Strike |

## What changed

- **Burning Vengeance deleted, merged into Wildfire.** Two near-identical Burn-spread lines (one
  scoped to Vendetta kills, one to any Burning death) composed onto the same `StatusSpreadOnDeath`
  component. `TriggerOnVendettaKill`/`HasFieryBurst` are gone, `MaxVendettaSystem` no longer spreads
  Burn at all, and `MaxFireMasteryReactionSystem.OnEntityKilled` is the single trigger path. Wildfire
  now SETS its fields per rank rather than `FPMath.Max`-composing, since it's the only writer.
- **Last Stand reworked around Rage fragility.** R1 is no longer "Rage survives being hit" — it's
  **Rage survives between activations** (`LastStandUpgrade.PersistsRage`/`StoredRageStacks`, parked at
  `BerserkSkillData.End` and handed back at `Begin`). R2 is the new "damage removes only
  `RageLossFraction` of current Rage" (`RageOverdriveUtility.ResetStacks` now scales instead of
  wiping). The old Retaliation weapon-damage proc and the `RageRetentionUpgrade` tag are both gone.
  R3 (Too Angry to Die) is unchanged mechanically but now correctly bypasses R2's softening — a
  cheated death genuinely spends the Rage.
  - Consequence: an Overdrive can now **start already at max Rage**. `FullThrottleSkillAction` and
    `IgnitionSkillAction` each re-check `IsAtMaxRage` in their own Begin (both apply paths are latched
    and idempotent), so their effects engage immediately rather than waiting for a threshold crossing
    that already happened. `RageOverdriveUtility.EnterMaxRage` is the shared entry point.
- **Full Throttle R3 is a one-shot refill, not a permanent state.** The `InstantReloadOverdrive` tag
  and `WeaponSystem.IsInstantReloadOverdriven` are deleted; `FullThrottleUpgrade.HasInstantReload`
  fires `WeaponSystem.RefillMagazine` once, on the max-Rage crossing itself, latched by `Applied`.
  The brief explicitly rules out re-resolving this every tick.
- **One capped ledger for every Overdrive extension.** `UncontrolledFuryExtension` →
  **`OverdriveExtension`**, now added by `BerserkSkillData.Begin` itself (seeded from a new
  `BaseMaxExtension`) and removed at `End`. `OverdriveUtility.TryExtend` clamps and books against it.
  Uncontrolled Fury's Vendetta-kill bonus was **uncapped** before and is not any more — it draws from
  the same pool and *replaces* (not stacks with) the ordinary per-N-kills grant for that kill. Vendetta
  Strike R3's own extension books against the same ledger. R1/R2/R3 caps are 3s/5s/10s.
- **Ignition R2 is kill-triggered.** Burning Ground was a distance-paced trail spawned every N units
  travelled while at max Rage; it now drops where a **Burning enemy you killed** died, still gated on
  max Rage (`MaxOverdriveReactionSystem.TryDropBurningGround`). Radius/damage/tick interval/duration
  are all authored on `IgnitionUpgrade` rather than baked into the prototype.
- **Blood Debt reshaped.** R1 12s mark (was 12→16 across ranks; now flat). R2 grants
  `RevengeConfig.RageOnVendettaKill` (+2 Rage per Vendetta kill) alongside the existing Shield-damage
  qualification. R3 raises `HealMultiplier` to 0.60 — deliberately lower than the old 1.0 — and adds a
  hard `MaxHealFractionPerKill` (15% of Max's own MaxHealth) so it can't become a healing engine.
- **Cremation retiered.** `ExecuteAgainstStatus` now carries `NormalHealthThreshold` (15%,
  Filler/Normal), `SpecialistHealthThreshold` (8%, Specialist/Heavy), and an Elite/Boss
  **bonus-damage** window instead of execution (`EliteBossDamageThreshold`/`EliteBossDamageBonus`,
  read by `MaxFireMasteryReactionSystem.ResolveCremationDamageBonus` from
  `DamageUtility.ResolveOutgoingDamage`). `BossExecutionEnabled`/`BossHealthThreshold`/
  `EliteHealthThreshold` are deleted — Elite and Boss are never executable, full stop.
- Run & Gun and Vendetta Strike are unchanged apart from the shared extension cap.

**Playtest first:** total Overdrive uptime with Last Stand R1 + Uncontrolled Fury R3 (carried Rage +
10s extension is the intended ceiling); Wildfire chain length now that `RetainedFraction < 1` is the
only decay; Cremation's Elite/Boss damage window against a real boss.
