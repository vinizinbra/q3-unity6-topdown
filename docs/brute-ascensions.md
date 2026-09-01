# Brute — Ascensions

Brute's Hero Ascension pool was consolidated from 10 fragmented single-pick passives (plus 8 dead
Juggernaut baseline sub-actions - see "The `CheckActions` bug" below) into exactly 8 three-rank
Ascension lines: 4 Juggernaut (his active Hero Skill), 2 Protector (his passive aura), 2 Dash. Reuses
the exact same generic rank architecture built for Pixie's own refactor (`IRankedUpgrade`, `MaxRank`
on both `PassiveUpgradeData`/`SkillActionData`, `UpgradeHistoryUtility.GetCount`, rank-aware
`Apply`/`Execute` overloads) - see `docs/level-up-upgrades.md`'s own architecture section. No
Brute-specific rank code exists anywhere in this refactor.

## The `CheckActions` bug (found and fixed as part of this refactor)

Before this refactor, `BruteBaseSkill-Juggernaut.asset` had `CheckActions: 0`.
`SkillSystem.InvokeActions` resolves `int actionCount = skill.CheckActions == true ?
skill.Actions.Count : 0;` - with `CheckActions` false, **none of Juggernaut's 8 baseline sub-actions
ever executed**, regardless of their own `Activated` flag (all 8 had `Activated: 1`). Concretely, before
this refactor: Discharge was knockback-only (zero damage), no landing damage/stun, no end-of-channel
explosion, no Momentum-generation/Charged-speed bonuses beyond the flat baseline fields, no stacking -
Juggernaut's own file header literally said "Pure knockback, no damage of its own." All 4 new Juggernaut
Ascension lines below therefore replace permanently-dead code, not working behavior - there was nothing
to migrate away from. The fix: the old 8 sub-action classes are deleted entirely (not re-enabled) - all 4 new Juggernaut
lines are ranked `SkillActionData` living on `JuggernautSkillData.Actions` instead (`Activated =
false`), same "Hero Skill Ascension" shape Pixie's `ClusterBombSkillAction`/`BirthdayCakeSkillAction`
already use (see "Hero Skill, not Passive Upgrade" below for why this replaced an earlier
`PassiveUpgradeData` design). `CheckActions` staying `false` is fine either way - it only gates
whether `Actions`' own baseline (`Activated == true`) entries execute automatically; a *picked*
Ascension is copied into `SkillSlot.Upgrades` at grant time (`SkillSystem.AddUpgrade`) and invoked
unconditionally from there regardless of `CheckActions`. `Actions` itself is purely the draft-
eligibility source list `LevelUpUtility.AddHeroSkillUpgradeCandidates` reads.

## Hero Skill, not Passive Upgrade

Momentum/Bone Breaker/Aftershock/Concussive Impact were originally built as ranked `PassiveUpgradeData`
(set once at pick time via `Apply`, read by `JuggernautSkillData` via `TryGetPointer`) since none of
them need their own Begin/OnGoing/End phase hooks. That worked mechanically, but it meant all 4 showed
up labeled as a generic **"Passive Upgrade"** in both the level-up card UI (`GameplayUiController.
KindText`) and the debug menu (`DebugUpgradeMenuTrigger`'s `"Passive"` section) - indistinguishable
from Iron Presence/Guardian, which genuinely are hero-wide passives. That's misleading for something
this specifically tied to Juggernaut, so they were converted to ranked `SkillActionData` on
`JuggernautSkillData.Actions` instead (`Phase = Begin`, refreshing the same qtn component's fields off
the live rank on every Juggernaut cast rather than once at pick time - functionally identical, since
none of these values depend on anything that changes activation to activation). This fixes the label
to **"Hero Skill"** everywhere with zero UI code changes - `KindText`/the debug menu already resolve
the label purely from which list/Actions-array an option was drafted from, exactly like Pixie's Cluster
Bomb/Birthday Cake already did. The read side (`JuggernautSkillData`'s own `Tick`/`Discharge`/`End`
logic) needed **no changes at all** - it already read `MomentumUpgrade`/`BoneBreakerUpgrade`/
`AftershockUpgrade`/`ConcussiveImpactUpgrade` via plain `TryGetPointer`, agnostic of how/when the
component got set.

## Shared baseline: "Juggernaut Skill Damage"

`JuggernautSkillData.Damage` (new field, default 30 - a placeholder pending a real balance pass) is the
percentage basis every line below references: Bone Breaker scales it directly for Discharge's own hit,
Aftershock/Concussive Impact/Iron Shoulder all reference the same **raw, un-Bone-Breaker-scaled** value
for their own percentages, so investing in Bone Breaker doesn't silently also buff the other three.
Resolved via `BruteAscensionUtility.ResolveJuggernautSkillDamage(f, owner)` (mirrors
`PixieAscensionUtility.ResolveBunnyBombDamage` exactly) wherever a line needs it outside
`JuggernautSkillData` itself (Iron Shoulder, Concussive Impact's landing shockwave).

`BruteAscensionUtility.ApplyRadialStunDamage(f, center, radius, owner, damage, stunDuration)` is the
other shared primitive - a generic "damage + stun everyone in radius" sweep, used by Concussive
Impact's rank 3 landing shockwave, Iron Shoulder's rank 3 wall-slam shockwave, and Aftershock's rank 3
high-pressure stun pulse.

## The 8 Ascension lines

### Juggernaut (4 lines, all `SkillActionData` on `JuggernautSkillData.Actions`)

Baseline, no Ascension needed (same treatment `ActiveMoveSpeedBonus`/`ChargedMoveSpeedBonus` already
got): every enemy caught by a Discharge grants Brutus `ShieldGainPerHit` (5) Shield, capped at his own
Max via `ShieldUtility.ApplyFlatShield`.

> **Superseded in detail, unchanged in shape.** This was originally an **Overshield** (above-Max, 1.5x
> cap) because player Shield refilled itself for free and a grant needed something to make it feel
> meaningful. As of 2026-08-25 Shield is charge-only and Overshield is deleted outright — which makes
> this Discharge grant Brutus's ONLY self-sufficient Shield source, and therefore what keeps his
> Accessory on his head. See the "Juggernaut and Bodyguard rebuilt on the charge-only Shield" section
> at the end of this doc, and `docs/accessory-guard.md`.

**1. Momentum** (`MomentumSkillAction` → `MomentumUpgrade`, absorbs old Momentum + Unstoppable)
- Rank 1: Momentum builds 25% faster while running during Juggernaut (`GenerationMultiplier` scales
  `SkillSlot.LastStepDistance` before it's added toward the next Charge point - see
  `JuggernautSkillData.AdvanceCharge`); +10% Move Speed while Charged (`ChargedMoveSpeedBonus`, folded
  into `ResolveChargedMoveSpeedBonus`); after Discharge, Charge only resets down to 30% instead of
  fully draining (`DischargeRetentionFraction` - see `JuggernautSkillData.TryDischarge`).
- Rank 2: 40% faster; +20% Move Speed while Charged; Discharge resets down to 60% instead of fully
  draining.
- Rank 3: 40% faster (same as rank 2); +30% Move Speed while Charged; Discharge no longer resets
  Charge at all (`DischargeRetentionFraction` 100%); **and Juggernaut refuses to expire while Brutus
  is still sitting on a full Charge** (`HoldUntilDischarge`) - if `Duration` runs out while
  `ChargePoints >= MaxCharge`, `JuggernautSkillData.Tick` returns `false` instead of finishing, so the
  whole channel (damage reduction, Charged speed, the lot) stays live until he actually cashes the
  Charge in on an enemy, and ends on that discharge. Without it, a full Charge earned in the last
  second of the channel is simply thrown away.
  - The hold keys off **Charge, never the retention fraction** - at rank 3 retention is 100%, so
    `ChargePoints` stays at Max after a discharge too, and holding on "still Charged" alone would
    never terminate. `TryDischarge` returns whether it actually fired, and Tick refuses to hold on
    the tick it did.
  - `slot->StateTimer` is pinned to exactly 0 for the duration of the hold rather than left running
    negative - `SkillCooldownUiWidget` prints it straight out as the channel's remaining seconds, so
    an unclamped overtime reads as "-3.4s" on the HUD.
  - **Aftershock rides along with it.** `TryEndExplosion` fires from `End`, which only runs via
    `SkillSystem.FinishSkill`, so the closing blast is deferred too - and it lands on the SAME tick as
    the final discharge, at the point of contact, with that discharge's own hits already counted in
    its stacks (`Discharge` increments `JuggernautCharge.UnitsHit` before `End` reads it). Rank 3
    therefore turns the end of the channel into an aimed one-two rather than a timer that can dump
    the blast in an empty corridor. Rank 3 Aftershock's own Earthquake `DelayedBlast` is scheduled off
    that same blast, so it follows to the same place.
  - Juggernaut's own cooldown only arms in `SkillSystem.FinishSkill`, so the overtime delays the
    next cast by however long Brutus takes to spend the Charge - the natural price of the free
    damage reduction, no extra guard needed. A banked spare charge can still cancel-and-recast out
    of it through the pre-existing `CanCancelAndRecast` path.

**2. Bone Breaker** (`BoneBreakerSkillAction` → `BoneBreakerUpgrade`, new - "Discharge damage
progression")
- Rank 1: +30% Discharge damage. Rank 2: +60%. Rank 3: +100%, plus Specialist/Heavy-tier targets take
  an additional +30% Discharge damage. Discharge now deals `JuggernautSkillData.Damage` unconditionally
  (the baseline this refactor added - previously zero); Bone Breaker scales that baseline via
  `DamageMultiplierBonus`/`TierDamageBonus`, fed into the existing `DamageUtility.ApplyDamage` entry
  point so it still flows through the normal outgoing-damage pipeline (crit, global multipliers)
  instead of a bespoke reimplementation.

**3. Aftershock** (`AftershockSkillAction` → `AftershockUpgrade`, merges old Aftershock +
Building Pressure)
- "Building Pressure" is internal state, not a standalone mechanic - it reuses the already-existing
  `JuggernautCharge.UnitsHit` (cumulative enemies knocked back by Discharge this whole activation) as
  its stack count directly, no new component needed; resets for free every activation since
  `JuggernautCharge` itself is removed at `End` regardless.
- Rank 1: on Juggernaut end, release an AoE shockwave for 100% of Juggernaut Skill Damage, using
  `JuggernautSkillData.AftershockRadius` (4, matching the old dead `JuggernautEndExplosionUpgrade`'s
  working default) as the baseline radius.
- Rank 2: each enemy struck by Discharge during the cast adds +15% damage (max 6 stacks); +20% radius.
- Rank 3: +20% per stack (max 8 stacks); if ≥5 stacks were built, also Stuns everyone caught.
- See `JuggernautSkillData.TryEndExplosion`.

**4. Concussive Impact** (`ConcussiveImpactSkillAction` → `ConcussiveImpactUpgrade`, absorbs
Heavy Impact + old Concussive Impact + Lasting Impact + Overwhelming Force + Crushing Blow)
- Baseline Discharge keeps its normal knockback/launch; this Ascension makes a launch dangerous. Baked
  onto the launched target via `JuggernautLaunched` (not tracked on Brute) so
  `JuggernautLandingImpactSystem` can resolve everything purely off the target even if Brute's own
  Juggernaut has already ended.
- Rank 1: launched enemies take landing damage (30% of Juggernaut Skill Damage, unconditional - not a
  chance roll) + Stunned 0.75s on landing.
- Rank 2: +25% launch/knockback force; landing damage 50%; landing stun 1.0s.
- Rank 3: landing damage 75%; landing stun 1.25s; landing also creates a 2.5m impact shockwave (40%
  Juggernaut damage, 1s Stun on nearby enemies); Brute additionally deals +40% damage to any enemy he's
  Stunned, via `StunDamageBonusUpgrade` (renamed from the old standalone Crushing Blow ascension's own
  `CrushingBlowUpgrade` - same component, same read site in `DamageUtility.ResolveOutgoingDamage`, just
  now granted by Concussive Impact rank 3 instead of its own separate pick). This bonus has no
  `DamageSource` restriction, so it automatically applies to weapon/skill/dash damage alike - Iron
  Shoulder's own rank 2+ collision damage gets it for free with zero extra code.
- Lasting Impact's old role (a stun-duration *multiplier*, read inside `StatusEffectUtility.ApplyStun`)
  isn't carried forward as a separate layer - Concussive Impact's landing-stun ranks are already
  complete absolute durations.
- Landing Root (the old `JuggernautLandingRootUpgrade`/`JuggernautLandingRootSkillAction`) is dropped
  entirely, not merged - not part of the 8 approved lines.

### Protector (2 lines, both `PassiveUpgradeData`, mutate the existing `ProtectorAura` component)

**5. Iron Presence** (`IronPresencePassiveUpgradeData`, merges old Iron Presence + Fearless)
- Rank 1: Intimidated enemies in the aura move 15% slower, take +25% knockback force.
- Rank 2: + Brute deals +20% damage to Intimidated enemies in the aura (absorbed from the old
  standalone Fearless ascension - see `ProtectorAuraUtility.GetFearlessBonusMultiplier`).
- Rank 3: slow 25%, knockback +50%, damage bonus +35%.

**6. Guardian** (`GuardianPassiveUpgradeData`, merges old Bulwark + Guardian - deliberately **not**
Bodyguard, which stays its own separate Dash line)
- `ProtectorAura.BaseRadius` (new field, set once at spawn by `ProtectorPassiveData`, never touched by
  any Ascension) lets Guardian's ranked `RadiusBonus` compute a correct total (`BaseRadius +
  RadiusBonus[rank]`) on every re-pick instead of needing to know/undo a previous rank's own addition.
- Rank 1: aura radius +2m; allies inside gain 10% Damage Reduction.
- Rank 2: radius +3m (relative to `BaseRadius`, not additive on top of rank 1's own bonus); allies gain
  20% DR.
- Rank 3: allies gain 25% DR; additionally, when an ally in the aura loses Shield/Health from an enemy
  hit, they get +15% further DR for 1.5s (~4s cooldown per ally - `StatusEffects.
  GuardianReactiveCooldownRemaining`, gated by `ProtectorAura.HasReactiveDamageReduction`). Driven by
  `BruteProtectorReactionSystem`, reacting to `Combat.qtn`'s `OnHealthDamageApplied`/
  `OnShieldDamageApplied` (same signals `MaxVendettaSystem` already reacts to for an unrelated hero) -
  scans every live `ProtectorAura` for one covering the hit ally (co-op match sizes are tiny, 0-4
  Brutes).
- The reactive DR bonus deliberately does **not** share `StatusEffects.
  GuardianDamageReductionRemaining/Amount` (the aura's own continuous DR pair, refreshed every tick by
  `ProtectorAuraSystem.ApplyToAllies`) - reusing that pair would get the reactive bonus stomped by the
  very next tick's continuous refresh. It uses a new, independent, genuinely generic pair instead - see
  below.

### Dash (2 lines, both already `SkillActionData`, only needed ranking)

**7. Iron Shoulder** (`IronShoulderSkillAction`)
- Rank 1 is exactly the pre-refactor behavior (knockback + wall-stun, no damage) - zero regression for
  an existing rank-1 pick. Elite/Boss enemies are no longer hard-excluded from the shove at any rank -
  they go through the same `DamageUtility.ApplyKnockback` call as everyone else, which already scales
  (or fully resists) by the target's own tier resistance (`StatusEffectUtility.GetTierResistance`), so a
  heavy target naturally shrugs off more of the push without a separate skip.
- Rank 2: adds direct-collision damage (60% Juggernaut Skill Damage); a wall-slam adds +50% additional
  damage on top.
- Rank 3: a successful wall-slam additionally fires a 3m impact shockwave (80% Juggernaut damage,
  Stuns nearby enemies) via `BruteAscensionUtility.ApplyRadialStunDamage` - deliberately not another
  knockback/wall-check, so it can never recursively re-trigger the wall reaction itself. Its damage
  naturally synergizes with Concussive Impact's own bonus vs Stunned targets since it flows through the
  normal `DamageUtility.ApplyDamage` pipeline - no extra code needed for that.

**8. Bodyguard** (`BodyguardSkillAction`)

> **Fully superseded 2026-08-25** — Bodyguard no longer restores Shield to allies at all. It now grants
> Brute and every nearby ally a **Free Hit Guard** (a one-shot complete negation of their next hit) on
> dash complete, and pays Brute back in Shield when one actually blocks. Everything below is the
> pre-rework record; see "Juggernaut and Bodyguard rebuilt on the charge-only Shield" at the end of
> this doc for what is live.

- Rank 1: on Dash complete, restore 10% Max Shield to allies within 6m.
- Rank 2: 15%, radius 8m.
- Rank 3: 20%; affected allies also get +20% DR for 2s (via the same shared `TemporaryDamageReduction`
  primitive Guardian rank 3 uses - see below).
- Brute himself is included in the ally scan (he trivially ends the dash within his own radius of
  himself), but only ever gets `SelfEffectMultiplier` (default 50%) of the full ally amount - a
  reduced, not full, self-benefit, authored as a plain configurable field rather than hardcoded.

## New shared primitive: `TemporaryDamageReduction`

`StatusEffects.TemporaryDamageReductionRemaining/Amount` - a second, independent timed DR pair,
deliberately generic (not Guardian-named) since two different reactive procs both write to it: Guardian
rank 3's on-hit proc and Bodyguard rank 3's on-dash-end proc. Both are occasional bonuses layered ON TOP
of Guardian's own continuous aura DR, not a replacement for it, so neither can share that pair (which
the aura rewrites every tick). `StatusEffectUtility.ApplyTemporaryDamageReduction`/
`GetTemporaryDamageReductionMultiplier`, folded into `DamageUtility.ResolveDamageReduction` alongside
the other two DR sources (all three stack multiplicatively). Uses take-the-stronger/longer semantics on
reapply (unlike every other timed multiplier in `StatusEffects`, which plainly overwrites) - a weak proc
landing while a strong one is still active extends nothing and overwrites nothing, so it can never cut
the strong window short.

## Removed / merged (do not re-add as standalone lines)

- **Ground Pound** - deleted entirely, not merged anywhere ("too disconnected from Brute's primary
  Juggernaut/Protector/Dash gameplay loop" per design). `GroundPoundUpgrade`/
  `GroundPoundPassiveUpgradeData`/`BruteKnockbackMasterySystem` (its only consumer of
  `PlayerMovement.qtn`'s `OnPlayerLanded` signal) all deleted. `OnPlayerLanded` itself stays - generic,
  hero-agnostic infrastructure, not Brute-specific, with no current consumer.
- **Bulwark**, **Fearless** - folded into Guardian/Iron Presence respectively (see above), own classes
  deleted.
- **Crushing Blow** - its mechanism survives as the renamed `StunDamageBonusUpgrade`, now granted by
  Concussive Impact rank 3 instead of its own pick; the old standalone
  `CrushingBlowPassiveUpgradeData` class deleted.
- **Lasting Impact**, **Overwhelming Force** - folded into Concussive Impact's landing-stun
  ranks/`KnockbackForceBonus` respectively; own classes and `LastingImpactUpgrade` deleted.
- **Landing Root** (`JuggernautLandingRootUpgrade`/`JuggernautLandingRootSkillAction`) - dropped, not
  merged into anything.
- **"Barricade"** - a never-fully-authored 3rd Dash Ascension (`BruteWall.asset`, a bare
  `SpawnEntitySkillAction` "Wall Prototype") that had been partially wired into
  `BruteCharacterData.DashSkillUpgrades` despite the old generator's own log claiming it still needed
  manual work. Not part of the approved 8 lines - asset deleted.

## Asset path drift (found and fixed as part of this refactor)

The old `BruteProtectorAssetGenerator.cs`'s own path constants had drifted out of sync with where the
live, actually-referenced assets sit on disk (confirmed by cross-referencing `BruteCharacterData.
asset`'s own GUIDs) - e.g. it targeted `Resources/Passives/Brute/PassiveSkillUpgrades/` for
Bulwark/Guardian/IronPresence/Fearless, but the real live assets sat at `Resources/Skills/Brute/
Brute_PassiveSkill/Brute_PassiveSkillUpgrades/` instead. Re-running the old generator as-is would have
silently forked duplicate assets at the wrong path. `BruteAscensionAssetGenerator.cs` (the replacement)
is pointed at the verified live paths for every surviving asset (Guardian/IronPresence/
ProtectorPassiveData/IronShoulderSkillAction/BodyguardSkillAction), so it updates them in place -
preserving GUID/wiring - rather than forking.

## Architecture notes

- All 4 Juggernaut lines are ranked `SkillActionData` on `JuggernautSkillData.Actions` (see "Hero
  Skill, not Passive Upgrade" above) - `Phase = Begin` only; none of them need OnGoing/End hooks of
  their own, since `JuggernautSkillData`'s own hardcoded `Tick`/`Discharge`/`End` logic reads the
  components they set via plain `TryGetPointer`, same mechanism the dead pre-refactor system was
  *designed* to use before the `CheckActions` bug made it moot.
- The 4 Juggernaut `.cs` classes live under `Assets/_QuantumUser/Simulation/Assets/Skills/Heroes/
  Brute/HeroSkillUpgrades/` (the same folder the old, deleted sub-actions used to live in) and their
  `.asset` instances under `Resources/Skills/Brute/Brute_HeroSkill/Brute_HeroSkillUpgrades/` -
  sibling to `BruteBaseSkill-Juggernaut.asset`, mirroring Pixie's `Pixie_HeroSkillUpgrades/`
  convention exactly. `Brute_PassiveSkill/Brute_PassiveSkillUpgrades/` now holds only the 2 lines that
  are genuinely hero-wide passives (Iron Presence, Guardian).
- `BruteAscensionAssetGenerator.cs` replaces `BruteProtectorAssetGenerator.cs`/
  `BruteKnockbackMasteryAssetGenerator.cs` - one generator now fully owns every list it touches
  (`BruteCharacterData.PassiveUpgrades`/`DashSkillUpgrades`, `BruteBaseSkill-Juggernaut.Actions`) end to
  end, same fix the Pixie Ascension refactor already applied for the identical append-vs-replace drift
  bug. Every per-rank `FP[]` value is explicitly re-set on every run (not left to a C# field-initializer
  default), the same fix that resolved Pixie's own "Direct Hit shows 0%" corrupted-array bug - applied
  here from the start.

## Current status

The code compiles once Quantum's `.qtn` codegen picks up the new/changed components
(`JuggernautAscensions.qtn`, `ProtectorAura.qtn`'s `BaseRadius`/`HasReactiveDamageReduction`,
`JuggernautLaunched.qtn`'s Shockwave fields, `StatusEffects.qtn`'s
`TemporaryDamageReductionRemaining/Amount`/`GuardianReactiveCooldownRemaining`) and is registered in
`SystemSetup.User.cs` (`BruteProtectorReactionSystem`). The generator was run once under the earlier
`PassiveUpgradeData` design for the 4 Juggernaut lines - after converting them to `SkillActionData`
(see "Hero Skill, not Passive Upgrade" above), the 4 now-stale `.asset` instances at the old
`Brute_PassiveSkillUpgrades/` path were deleted by hand and `BruteCharacterData.PassiveUpgrades`
trimmed back to just Iron Presence/Guardian in the meantime, but **`Tools > RiftRaiders > Brute >
Generate Ascension Assets` (or `Generate All Assets`) still needs to be run** to actually author the 4
new Hero-Skill-Ascension assets at `Brute_HeroSkill/Brute_HeroSkillUpgrades/` and wire them into
`BruteBaseSkill-Juggernaut.Actions`. `JuggernautSkillData.Damage` (30) is a placeholder pending a real
balance pass alongside the rest of Brute's kit. Not yet manually verified end-to-end in the Editor.

---

# 2026-08-20 balance pass

Brute goes from 8 lines to the target **9 lines × 3 ranks** — a new third Protector line, plus a
redesign of his offensive build engine and a rein-in of his team damage reduction.

## Roster now

| Pool | Lines |
|---|---|
| Juggernaut (Hero Skill) | Momentum, Bone Breaker, Aftershock, Concussive Impact |
| Protector (Passive) | Iron Presence, Guardian, **Unstoppable** — *superseded, see the next section* |
| Dash | Iron Shoulder, Bodyguard |

> **Superseded below.** Unstoppable was cut later the same day and replaced by **Groundbreaker**. The
> Unstoppable material in the rest of this section is kept as the record of why it existed; nothing it
> describes is still in the codebase.

## What changed

- **Aftershock is now Brute's primary build engine.** Was "+15%/+20% per enemy hit, capped at 6/8
  stacks, +20% radius, stun at 5+". Now every rank uses the same +15%/stack up to **5 stacks**;
  R2 adds **+5% radius per stack**; R3 "Earthquake" replaces the stun with a **second shockwave 0.5s
  later** at 60% of the primary's own (already stack-scaled) damage. The reward for routing through a
  crowd before ending Juggernaut is now the whole point of the line. `AftershockUpgrade.RadiusMultiplier`
  and `StunsAtHighPressure` are gone; `StackRadiusPercent` and the four Earthquake fields replace them.
  Earthquake uses the new generic `DelayedBlast`/`DelayedBlastSystem` (shared with Pixie), not a
  Brute-specific timer.
- **Guardian's permanent team DR capped at 15%.** Was 10% / 20% / 25% and climbing. Now 10% / 15% /
  15% — rank 3's payoff is a **reactive** burst (+20% DR for 2s, 5s cooldown per ally) rather than a
  bigger always-on number, and rank 2 adds 30% knockback resistance
  (`ProtectorAura.AllyKnockbackTakenMultiplier`, via the existing generic
  `StatusEffectUtility.ApplyKnockbackTaken`). Every reactive value is now authored on the Ascension
  (`ReactiveDamageReductionAmount`/`Duration`/`ReactiveCooldownPerAlly`) instead of being hardcoded
  constants in `BruteProtectorReactionSystem`, and that system now picks the STRONGEST covering aura
  rather than the first one iterated.
  - The aura itself writes the shared aura-DR slot (`StatusEffectUtility.ApplyAuraDamageReduction`,
    renamed from the Guardian-specific one and now take-the-stronger), which is what makes "Guardian
    from multiple Brutes must NOT stack additively" true by construction — and what keeps a Brute +
    Zara + Lux DR stack from compounding.
- **Bodyguard restores a FLAT Shield, on a per-ally cooldown.** Was 10%/15%/20% of the ally's own Max
  Shield with no pacing — a dash-cooldown build could pump unbounded effective Shield into a
  high-Shield teammate. Now 10 / 15 / 20 flat, gated by
  `StatusEffects.AllyShieldRestoreCooldownRemaining` (4.5s, authored). The cooldown lives on the ALLY,
  not on Brute: per-Brute would still let two Brutes chain-refill one teammate, and would punish
  dashing between different allies — exactly the play this line should reward. R3's DR values are
  authored rather than hardcoded.
- **Unstoppable (new Protector line).**
  - R1 "Thick Skull": hard CC lasts 30% less on him — implemented purely through the new generic
    `CharacterStats.HardCcDurationMultiplier`, folded into `StatusEffectUtility.ApplyStun`/`ApplyRoot`
    beside the enemy-tier resistances. Nothing Brute-specific reaches those paths.
  - R2 "Come At Me": being hit by a Specialist-or-tougher enemy grants +1 Momentum (only while
    Juggernaut is channelling, since `JuggernautCharge` only exists then), on an authored trigger
    cooldown.
  - R3 "Unstoppable": at max Momentum he's immune to knockback and hard CC and deals +20% impact
    damage. Immunity is refresh-only (`StatusEffects.HardCcImmunityRemaining` plus a zero
    `ApplyKnockbackTaken`), the same idiom every aura here uses — so it lapses on its own the instant
    Momentum drops or Juggernaut ends, with no removal path to get wrong. The damage bonus is applied
    by `BruteAscensionUtility.ResolveImpactDamageMultiplier` at all three body-collision sources:
    Discharge, Aftershock and Iron Shoulder.
  - New `UnstoppableUpgrade` component + `BruteUnstoppableSystem`.
- Momentum, Bone Breaker, Concussive Impact, Iron Presence and Iron Shoulder are unchanged. Concussive
  Impact's repeated stuns now automatically respect the generic per-tier CC immunity window (see
  `EnemyTierResistanceConfig`), with no change to the line itself.

**Playtest first:** Aftershock stack acquisition rate (5 stacks should be a real routing goal, not
automatic); Guardian + Zara's Protective Rhythm + Lux's Fire Support stacked on one ally; Unstoppable
R3's uptime given Momentum retention from Momentum R3.

---

# 2026-08-20 (later) — Unstoppable removed, Groundbreaker added

Brute stays at **9 lines × 3 ranks**; the third Protector line is replaced outright.

## Why Unstoppable was cut

It was built around CC resistance, Stun/knockback immunity, Momentum generation on being hit, and a
max-Momentum defensive state. Every one of those either overlapped Momentum's own design space or
read as a stat tweak rather than a mechanic. Replaced by a line that occupies a completely different
space: **terrain and verticality**.

## Roster now

| Pool | Lines |
|---|---|
| Juggernaut (Hero Skill) | Momentum, Bone Breaker, Aftershock, Concussive Impact |
| Protector (Passive) | Iron Presence, Guardian, **Groundbreaker** |
| Dash | Iron Shoulder, Bodyguard |

## Groundbreaker

The loop: **high ground → drop → impact shockwave → knock enemies away → wall slam → stun → damage
window.**

- **R1 "Heavy Landing"** — landing from a real drop throws nearby enemies directly away from the
  landing point (radius 3, moderate knockback, low impact damage).
- **R2 "Crash Landing"** — harder knockback, real impact damage, and anything shoved into a wall is
  **Stunned** (1s).
- **R3 "Seismic Impact"** — radius 4.5 (+50%), knockback ~+65% over R1, damage 75% of Juggernaut Skill
  Damage, and anything actually **wall-stunned** becomes **Exposed** (+25% damage taken, 3s).

It deliberately contains **no** Momentum generation/retention/reset, no Move Speed, and no Juggernaut
duration change. That half of the kit stays entirely Momentum's responsibility.

### Landing trigger — generic, not map-specific

It reacts to the pre-existing **`OnPlayerLanded(entity, fallDistance, source)`** signal
(`PlayerMovement.qtn`/`AutoJumpSystem`), which already existed as hero-agnostic infrastructure with
**no consumer** since Brute's old Ground Pound was removed. `fallDistance` is the drop in *grounded* Y
between takeoff and landing, so the trigger is a plain configurable height threshold
(`MinimumFallHeight`) rather than anything tied to map tiles or terrain tiers — it works from terrain
transitions, elevated platforms, jumps, and any future launch mechanic alike.

Everything the brief rules out is excluded for free rather than special-cased: ordinary movement never
ungrounds at all, a same-height dash and a walked-down step both report ~0, an upward auto-mantle is
clamped to 0, and **the known false auto-hop at chunk-cube seams reports ~0 too** — a real argument for
the threshold approach over any "did we leave the ground" test.

Default `MinimumFallHeight = 2`, deliberately double `MovementDataAsset.MaxLedgeHeight` (1, the tallest
ledge Brute can auto-mantle), so ordinary traversal can never reach it. The level has no discrete
height-level grid (floor is baked at Y=0; vertical variation is hand-placed chunk geometry), so world
units *are* the project's vertical representation here.

**`AllowedLandingSources`** is a real authored knob, not a placeholder: a new generic `LandingSource`
enum (`Fall`/`Jump`/`Launched`) is now carried on `PlayerMovement.AirborneSource` and through the
signal. `Fall` is the default and needs no writer; `AutoJumpSystem.DoJump` claims `Jump`;
`DamageUtility.ApplyResolvedImpulse` claims `Launched` when an upward impulse hits a player. It resets
to `Fall` on landing, right after the signal. All three are allowed by default — the height
requirement is the real filter.

### Wall slam — one shared implementation, not a second system

Iron Shoulder's private `TryStunIfPushedIntoWall` was **extracted verbatim** into a new hero-agnostic
**`WallSlamUtility.TryWallSlam`**, which both lines now call. Groundbreaker supplies only a different
knockback source; it owns no wall-collision code of its own. The architecture is now literally
*knockback source → enemy movement → valid wall impact → wall-slam effect*.

`TryWallSlam` reports the wall hit **and**, separately, whether the Stun genuinely *landed* — those
differ whenever the target sits inside a hard-CC immunity window or is a tier authored
`ImmuneToHardCC`. Iron Shoulder ignores the second (its damage bonus keys off the wall); Groundbreaker
needs it.

### The damage-window rule

Exposed is gated on the **Stun actually landing**, never on merely being caught in the shockwave and
never on merely finding a wall. The reward is specifically: good positioning → correct knockback angle
→ wall impact → Stun → burst opportunity. Exposed reuses the pre-existing generic **Rupture** status
(`StatusEffectUtility.ApplyRupture`, take-the-stronger), exactly as Lux's Overload Core rank 3 does —
no Brute-specific status was added.

### Concussive Impact: no double-trigger, by construction

Concussive Impact reacts to an **enemy's** own landing after being launched by Discharge
(`JuggernautLaunched`, stamped only by `JuggernautSkillData.Discharge`, consumed by
`JuggernautLandingImpactSystem`). Groundbreaker reacts to **Brute's** own landing. Different trigger,
different entity — and Groundbreaker never stamps `JuggernautLaunched`, so the same landing cannot
satisfy both and neither can feed the other. No guard was needed, and none was added.

Iron Presence needs no special-casing either: Groundbreaker's knockback goes through the ordinary
`DamageUtility.ApplyKnockback`, which already folds in `StatusEffects.KnockbackTakenMultiplier` —
so Iron Presence's reduced-knockback-resistance debuff on Intimidated enemies composes automatically.

### Determinism

The simulation decides everything: whether it triggers, who is caught, knockback direction, the wall
result, the Stun, and the Exposed window. The View only renders the new `GroundbreakerSlammed` event.

### View FX

Presentation only — the simulation has already decided everything by the time any of this runs.

**Landing shockwave.** `EffectsManager` handles `GroundbreakerSlammed`: a radius-scaled burst at the
landing point plus an optional ground crack/dust decal, ground-probed via the same `TryFindGroundBelow`
the enemy-death decal already uses so a crack can't float over a slope. One prefab authored at radius 1
covers all three ranks (3 / 3 / 4.5) rather than needing three. Falls back to `defaultAreaBlastEffect`
tinted a dusty earth tone — the same dedicated-slot-with-tinted-fallback pattern Detonation/Singularity/
Overflowing Rift already use, so it reads distinctly even before a bespoke prefab exists.

The prefab lives on `EffectsManager` rather than on `GroundbreakerPassiveUpgradeData` — unlike
`SkillActionData`, `PassiveUpgradeData.Apply` gets no self `AssetRef` to travel with the event, which is
exactly why Kai's Undertow and every reaction VFX already sit on the manager too.

**Wall impact — a generic event, not a Groundbreaker one.** A new `WallSlammed` event is fired by
**`WallSlamUtility` itself**, so every knockback source routing through that utility gets the impact VFX
with no per-source hookup. Brute's **Iron Shoulder dash gets this for free** — it had no dedicated wall
visual before. It carries the wall **contact point** (resolved from `CastDistanceNormalized`, since
`Hit3D.Point` is only populated under `ComputeDetailedInfo`, which this query deliberately doesn't pay
for), the push direction so the burst sprays *into* the surface instead of puffing symmetrically, and
`Stunned` — which drives a heavier variant, because a wall hit resisted by a hard-CC immunity window
isn't the same payoff as one that landed and opened the Exposed window.

**Camera shake.** A new `ImpactCameraShakeListener` (`View/Camera/`, same shape as
`WeaponCameraShakeListener` including its `[Button]` test triggers) shakes on both events, filtered to a
**local** player's own impacts so a remote Brute across the map doesn't rattle this client. Landing
amplitude scales linearly with the event's radius against an authored reference radius and is clamped,
so rank 3 hits harder than ranks 1–2 off one value instead of three. Both event hookups have their own
on/off toggle. Tuning lives on the component rather than in `CameraShakeConfig`, whose per-`WeaponShakeTier`
vocabulary doesn't describe a landing or a wall impact.

**Exposed** needs nothing new — it reuses the generic Rupture status, which `StatusEffectsManager` and
`CharacterUiWidget` already visualize.

## Removed with Unstoppable

`UnstoppablePassiveUpgradeData`, `Unstoppable.qtn` (`UnstoppableUpgrade`), `BruteUnstoppableSystem`,
and `BruteAscensionUtility.ResolveImpactDamageMultiplier` plus its three call sites (Discharge,
Aftershock, Iron Shoulder).

Two **generic** hooks had been created solely for Unstoppable and had no other consumer, so they were
removed too rather than left as invisible dead code:

- `CharacterStats.HardCcDurationMultiplier` (+ `StatusEffectUtility.GetHardCcDurationMultiplier` and
  its seed in `CharacterSystem`) — Unstoppable R1's only mechanism.
- `StatusEffects.HardCcImmunityRemaining` (+ its tick and its checks in `ApplyStun`/`ApplyRoot`/
  `TryConsumeInterruptImmunity`) — Unstoppable R3's only mechanism.

**Deliberately kept**, because they are used elsewhere: the per-tier hard-CC diminishing-returns
windows (`EnemyTierResistanceConfig.StunImmunityDuration`/`InterruptImmunityDuration`/`ImmuneToHardCC`
+ `StatusEffects.StunImmunityRemaining`/`InterruptImmunityRemaining`), which Kai's Singularity, Brute's
own Concussive Impact and Zara's Bass Drop all rely on.

## Status

All four assemblies (`Quantum.Simulation`, `Quantum.Unity`, `Quantum.Unity.Editor`, `Assembly-CSharp` —
the View code lives in the last of these) verified to compile against freshly-run codegen. `Tools >
RiftRaiders > Brute > Generate Ascension Assets` authors Groundbreaker and rewires
`BruteCharacterData.PassiveUpgrades` — **not yet run**, and the stale `Unstoppable.asset` needs deleting
by hand. Not verified in-Editor.

**Editor authoring for the FX** (all of it optional — every slot has a working fallback, so nothing
breaks if it's skipped): author `groundbreakerImpactPrefab` / `groundbreakerDecalPrefab` and
`wallSlamEffectPrefab` on the scene's `EffectsManager` (unset falls back to `defaultAreaBlastEffect`,
tinted for the landing), and add an `ImpactCameraShakeListener` component to a HUD/manager GameObject in
`QuantumGameScene` (no component in the scene = no shake, nothing else changes).

**Playtest first:** whether `MinimumFallHeight = 2` matches the real terrain drops in generated levels
(this is the single value that decides whether the line ever triggers); how often a knocked-back enemy
actually finds a wall inside `WallCheckDistance` at rank 2+, since the whole rank-3 payoff is gated
behind it; and whether R1's knockback-only version reads as satisfying without the wall reaction.

---

# 2026-08-20 (later still) — Bodyguard never shielded Brute himself

Reported from live testing: Bodyguard restored Shield to allies but never to the casting Brute.

**Cause.** Bodyguard scanned with `EnemyMovementUtility.FindPlayersInRadius`, whose `Player`-only layer
mask deliberately cannot see a dashing player — `DashSkillData.Begin` parks the dasher on
`IgnoreProjectile` for the dash's whole duration, which is what gives Dash its i-frames against enemy
attacks and projectiles.

Bodyguard fires at **dash End**, so it coincides with that layer swap *by definition*. `DashSkillData.End`
does restore the layer one line before the End-phase actions run
([SkillSystem.cs:424-425](Assets/_QuantumUser/Simulation/Systems/Player/SkillSystem.cs#L424-L425)) — but
that is far too late: `Core.PhysicsSystem3D` runs **before every user system**, so the broadphase the
overlap query reads was already built this tick with Brute still on `IgnoreProjectile`. The result is a
100% failure, not an intermittent one. Allies were always found correctly, which is exactly why it read
as "Bodyguard doesn't shield me."

The `SelfEffectMultiplier` half of the design was therefore dead code — it had never once run.

**Fix.** Use the existing broader mask, which already exists for precisely this problem. The helper and
mask were renamed for what they *do* rather than for their first caller, since the exclusion has nothing
to do with pickups and every friendly query hits it:

- `GetPickupLayerMask` → **`GetPlayerIncludingDashingLayerMask`**
- `FindPlayersInRadiusForPickup` → **`FindPlayersInRadiusIncludingDashing`**

The five existing pickup/chest/traversal callers are unchanged in behavior.

**Same bug, same day, different hero.** Zara's **Portable Speaker** rank 2+ dash-end ally buff had the
identical defect for the identical reason and got the identical fix — see `docs/zara-ascensions.md`.

**Deliberately not changed.** `ProtectorAuraSystem`, `SentryAuraSystem` and `ResonanceUtility.FirePulse`
also use the narrow mask, but they are refresh-only auras re-applied every tick with a 1s window against
a 0.5s `DashDuration` — the buff never actually lapses, so a dashing player loses nothing. Only queries
that fire *at* dash end are guaranteed to coincide with the layer swap.

---

# 2026-08-20 (later still) — enemies knocked INTO the environment and stuck

Reported from live testing: some of Brute's knockbacks pushed enemies into the walls rather than
against them, and they stayed there.

## Why it happens

Enemies are ordinary dynamic `PhysicsBody3D`s (mass 100, non-trigger, Enemy layer), and the Enemy layer
*does* collide with Ground, so the normal case is correct — a shoved enemy hits a wall and stops. But:

- Brute's knockbacks are by far the hardest in the game and use `KnockbackApplyMode.Override`, which
  sets velocity outright: **Iron Shoulder 20 u/s** (`KnockbackTier.Strong`), **Groundbreaker up to
  16.5**. That is ~0.33 and ~0.28 units in a single 60 Hz step.
- A chunk's walls are one **compound** `PhysicsCollider3D` baked by `ChunkCompoundColliderBuilder`, and
  this project already has documented gaps at **chunk seams** (see the auto-jump seam bug). A hard
  enough push into a compound corner or a seam can end a step inside the geometry, and Quantum's 3D
  physics has no continuous collision detection to fall back on.
- Once the enemy's center is *inside*, it can never get out on its own: every wall check
  `EnemyMovementUtility` steers by (`IsBlockedByWall` and friends) raycasts **from the enemy's own
  position**. From in there, there is no wall ahead to avoid — so `EnemySystem` cheerfully drives it
  deeper on the next tick. That is the "walked into the environment and got stuck" symptom.

Juggernaut's Discharge is a second, separate route in: its impulse is
`(velocityXZ * 0.10 + Up * 4) * 4`, i.e. **+16 u/s straight up**, which against the project's **-40**
gravity apexes at ~3.2 units — enough to pop an enemy clean over a low wall and drop it behind one.

## Fix — recovery, not a clamp

New generic `EnemyStuckRecoveryUtility` (`Systems/Enemy/`), plus two fields on `Enemy`
(`PreKnockbackPosition`, `StuckCheckTimer`):

- `EnemySystem.OnEnemyKnockedBack` records where the enemy was standing at the moment it was hit — a
  known-good spot — and opens a 3s watch window. This is done **before** the
  `CanBeInterruptedByKnockback` early-out, so Heavy/Elite/Boss (which take the impulse without being
  staggered) are covered too.
- While that window is open, `EnemySystem.Update` probes a **half-radius** sphere at the enemy's true
  collider center against the Ground layer (`HitStatics | HitKinematics` — a chunk collider is a
  kinematic entity collider, so `HitStatics` alone finds no walls at all). Half-radius means resting
  *flush* against a wall can never trip it; only a center that has genuinely sunk in does.
- On a hit: return the enemy to the recorded position, zero its velocity, close the window.

It runs before any movement work, including `TickKnockbackRecovery`'s own early-out, since a push can
bury an enemy mid-stagger.

**Why not clamp the knockback instead?** Capping every impulse against a wall probe would flatten how
knockback feels anywhere near a wall (which is most of an arena), would have to guess at drag to know
how far a push actually carries, and *still* wouldn't catch the over-the-wall case. This changes no
combat numbers at all, and costs nothing for an enemy nobody has knocked around.

## Not changed — flagged for your call

Juggernaut Discharge's **+16 u/s upward** is extreme next to every other knockback in the game
(`KnockbackTier.Strong` upward is 1.0; Groundbreaker's is 2). It is authored as
`KnockbackUpwardForce = 4` × `KnockbackForce = 4` in `BruteBaseSkill-Juggernaut.asset` — the
multiplication is easy to miss when tuning either number alone. Lowering `KnockbackUpwardForce` to ~1
would bring the pop in line without touching the horizontal push. Left alone because it is a balance
decision, not a bug.

---

# 2026-08-25 — Juggernaut and Bodyguard rebuilt on the charge-only Shield

Shield stopped being a self-refilling absorb pool and became an earned, spendable buffer whose job is
to keep your Accessory on your head — full writeup in `docs/accessory-guard.md`'s own
"Shield reworked into the Accessory's protective layer" section. Read that first; this section only
covers Brute's two consumers.

## Juggernaut — barely changed, but it means something different now

Discharge still grants `ShieldGainPerHit` (5) per enemy caught. Two edits:

- `ShieldUtility.ApplyOvershield` → `ApplyFlatShield`, so it caps at Max like every other grant.
  `OvershieldCapMultiplier` is gone from `JuggernautSkillData`.
- Nothing else.

What changed is the *meaning*. This is now Brutus's **only self-sufficient Shield source**, and since
holding any Shield stops his Accessory being knocked off, an aggressive multi-enemy Discharge is his
defensive payoff twice over: the Shield itself, and his hat staying on. The 50% channel DR was always
his defensive window; now the channel also produces something that outlives it.

**Playtest knob:** 5/enemy against his authored `BaseMaxShield` of 50 is 10 Discharged enemies for a full charge,
against an 8s channel on a 15s cooldown with a 1s per-enemy discharge cooldown. Reachable in a dense
crowd, out of reach solo. 30-40 Max Shield is the likelier landing spot — tune after a real run rather
than pre-emptively.

## Bodyguard — Free Hit Guard

| Rank | Effect |
|---|---|
| R1 | On dash complete, **Brute and allies within 3m** gain **Free Hit Guard for 2.5s** — the next damaging hit is completely negated |
| R2 | Radius **6m**, guard lasts **3.5s**. When one blocks a hit, **Brute gains 10 Shield** |
| R3 | When one blocks, it also releases a **3m knockback shockwave** around whoever it saved. **Brute gains 15 Shield** instead |

The old line restored flat Shield at dash end. That only ever topped up a bar which refilled itself
anyway; with Shield charge-only, the interesting thing to hand out is a guaranteed negation, and
Brute's own reward is Shield he **earns when a guard actually blocks** rather than something handed to
him for dashing. The old `SelfEffectMultiplier` is gone — Brute is simply a full-value recipient now.

**Delivery shape is unchanged from the pre-rework line:** still `Phase = End`, still one radius query
at the dash's end point, still the same 4.5s per-recipient cooldown. Radii were hand-tuned to
**3m / 6m / 8m** (they used to plateau at 6m/8m/8m) — growing every rank makes rank 1 tight enough
that guarding a teammate is a deliberate act of aiming the dash at them rather than something that
happens incidentally, and gives the line a reason to be levelled beyond its Shield payback. A
`Begin | OnGoing | End` sweep version was built and reverted — a dash-end radius is the intended feel.

**Brute is included at full value.** He trivially ends the dash inside his own radius, and at rank 1
that self-guard is the point of dashing defensively. At ranks 2-3 it closes a real loop: guard
yourself, eat a hit with it, get Shield back. The per-recipient cooldown applies to him exactly as to
anyone else, so a dash-cooldown build cannot hold a permanent guard.

### The layer mask is load-bearing, not defensive

`EnemyMovementUtility.FindPlayersInRadiusIncludingDashing` is what makes "it also triggers on Brute"
actually true. This fires at dash **End**, and `DashSkillData` parks the dasher on `IgnoreProjectile`
for the dash's whole duration (that is what gives Dash its i-frames). `DashSkillData.End` restores the
layer one line before End-phase actions run, but `Core.PhysicsSystem3D` runs before every user system,
so the broadphase this query reads was already built this tick with Brute still on `IgnoreProjectile`.
The narrow `Player` mask drops him **100% of the time, not intermittently** — which is exactly how the
old self-restore silently never ran (see "Bodyguard never shielded Brute himself" above). Switching
this query back to `FindPlayersInRadius` would silently re-break self-inclusion.

### New generic primitive: Free Hit Guard

`StatusEffects.FreeHitGuardRemaining` + `FreeHitGuardSource`, with
`StatusEffectUtility.ApplyFreeHitGuard`/`HasFreeHitGuard`/`TryConsumeFreeHitGuard`. A one-shot, timed,
complete negation of the next damaging hit — **hero-agnostic; Bodyguard is its first consumer, not its
owner**.

Consumed in `DamageUtility.ApplyDamage` immediately **above** the Accessory Guard hook, under the same
`bypassOutgoingResolution == false` direct-hit gate. Above, deliberately: a free hit is a gift with a
timer on it, whereas a durability point costs Coins at a Merchant to restore — so the free one must be
spent first, or it could lapse unused while the expensive one was burned.

The reward deliberately lives **outside** the primitive. `Combat.qtn`'s
`OnFreeHitGuardConsumed(target, source, attacker)` reports only that a guard triggered and who granted
it; `BruteBodyguardReactionSystem` reads Brute's own `BodyguardUpgrade` component and decides what that
save is worth. Any future hero, perk or consumable can hand out a free block on its own terms without
touching the primitive. There is also a `FreeHitGuardConsumed` event carrying the position, for the
same reason `AccessoryBlocked` exists: a fully negated hit fires no `EntityDamaged`, so without it a
save reads as a miss.

`BodyguardUpgrade` (`Bodyguard.qtn`) carries the rank-resolved `GuardDuration`/`ShieldReward`/
`ShockwaveRadius`/`ShockwaveForce`, refreshed on every dash. The reaction system runs long after the
dash is over, when only entity refs remain — re-resolving the rank from the skill asset at that point
is not possible.

Rank 3's shockwave is centred on **whoever the guard saved**, not on Brute: the guard routinely
outlives the dash, so when it protects an ally Brute is usually nowhere near, and centring it on him
would detonate it away from the fight it exists to break up. (When Brute's own guard blocks, he *is*
the saved party, so it lands on him — the same code path, no special case.) It uses a new
knockback-only sibling of the existing radial helper, `BruteAscensionUtility.ApplyRadialKnockback` —
no damage, no stun, because the point is buying space right after a near-death, not sneaking a damage
line into a defensive rank.

The cooldown survives the rework, renamed
`StatusEffects.AllyShieldRestoreCooldownRemaining` → `AllyGuardGrantCooldownRemaining`. It is still on
the RECIPIENT rather than on Brute, for the reasons the 2026-08-20 pass documented: per-Brute would let
two Brutes chain-guard one teammate, and would punish dashing *between* allies — exactly the play this
line should reward. Now that Brute guards himself, it is also what paces his own self-guard uptime.

### Known simplification

A Free Hit Guard is consumed by any damaging hit, including one the ally's own Shield would have fully
absorbed. Adding a "don't spend it if Shield covers this" check was considered and rejected as
complexity the spec did not ask for — the guard is simply the stronger layer and goes first.

## Current status

Code-complete, pending codegen for `Shield.qtn`/`StatusEffects.qtn`/`Combat.qtn`/`Events.qtn`/the new
`Bodyguard.qtn`. `BruteBodyguardReactionSystem` is registered in `SystemSetup.User.cs` beside
`BruteProtectorReactionSystem`. `BruteAscensionAssetGenerator` authors the new Bodyguard fields and rank
descriptions but **has still never been run** — until it is, `BodyguardSkillAction.asset` keeps its old
`ShieldRestore`/`SelfEffectMultiplier` serialisation and stale rank text, `Unstoppable.asset` is still
on disk, and `BruteBaseSkill-Juggernaut.asset` still carries its 5 pre-refactor embedded sub-actions.
Not yet verified in-Editor.

### View — Free Hit Guard

Three pieces, all reusing existing infrastructure rather than adding a parallel one.

**The guard while it's up** — `CharacterUiWidget.UpdateFreeHitGuard`: a `freeHitGuardRoot` shown while
`StatusEffects.FreeHitGuardRemaining > 0`, plus a `freeHitGuardFill` (`Image`, Type = Filled) draining
as the timer runs down. This is the load-bearing piece for a *granted* buff — without it a teammate has
no way to know Brute gave them anything, and the ability is invisible to the very person it protects
until it silently saves them. The fill (rather than an icon) is deliberate: a guard that lapses unused
should visibly be running out, so there's a reason to go spend it.

It needed one simulation field, `StatusEffects.FreeHitGuardDuration`. Every other timed status on this
widget shows a countdown NUMBER via `StatusIndicator.timerText` and so needs no denominator; a fill is
the first readout that has to know what "full" was. Deriving it View-side by remembering the largest
`Remaining` ever seen breaks the moment a longer guard refreshes a shorter one, which is exactly what
happens when a rank-2 Brute re-guards someone a rank-1 Brute already covered.

**The moment it blocks** — a negated hit fires no `EntityDamaged`, so without explicit hookups it lands
completely silently and reads as a miss. Same gap `AccessoryBlocked` documented; the guard now shares
all three of its handlers:

| Where | What | Keyed off |
|---|---|---|
| `HitFeedback` | `FlashDamage(freeHitGuardFlashColor)` — top-priority tier, so it never loses to a heal glow | `Target` — flashes whoever was saved, teammate included |
| `HurtOverlayUiWidget` | hit-stop + screen flash, flat `blockHitStopDuration` (no damage to scale a tier off) | `Target`, local-player-gated — your screen shouldn't freeze for a save across the map |
| `EffectsManager` | `freeHitGuardEffectPrefab` at the contact point, tinted cyan | position from the event |

### A negated hit is never a damage colour

Both negations run through `EffectsManager.PlayNegatedHitImpact` — shared plumbing only. Each keeps its
**own** prefab, scale and tint, because they are different mechanics from different sources and which
one just saved you should be readable at a glance:

| | Accessory block | Free Hit Guard |
|---|---|---|
| Prefab | `accessoryBlockedEffectPrefab` | `freeHitGuardEffectPrefab` |
| Impact tint | blue `(0.25, 0.6, 1)` | **none** — plays in its authored colours |
| Character flash | `HitFeedback.blockFlashColor`, blue | `HitFeedback.freeHitGuardFlashColor`, cyan |

The guard's impact is deliberately **untinted**. Its prefab is authored for this one job, so the colours
are the artist's to own — runtime-tinting a purpose-built effect only fights the authoring. The
accessory block keeps its tint because it leans on the generic hit spark, where the tint is doing real
work turning a borrowed effect into a negation. `PlayNegatedHitImpact` takes a `Color?` for exactly
this: pass one to recolour a borrowed prefab, pass null to leave a bespoke one alone.

What carries the "this was stopped, not damage" signal in both cases is the **character flash**, and
that is where cyan matters: `blockFlashColor` was **white** before this pass, i.e. identical to taking
an ordinary hit. Both are cool colours now, distinct from the damage palette (white / orange burn /
grey frontal-reduced) and from each other.

Both flashes deliberately route through `FlashDamage` (the top-priority tier) despite not being damage:
a negation is still an **impact**, and it must never lose out to a heal/shield/pickup glow landing in
the same moment.

Everything degrades cleanly unauthored — each impact falls back `<own prefab>` →
`meleeHitEffectPrefab` → `defaultAreaBlastEffect`, and every `CharacterUiWidget` field is an optional
null-check.

**Editor authoring still needed:**
1. Build the `freeHitGuardRoot`/`freeHitGuardFill` pair on `CharacterUiWidget.prefab` — the fill
   `Image` **must** have Image Type = Filled or `fillAmount` silently does nothing.
2. Author `freeHitGuardEffectPrefab` and `accessoryBlockedEffectPrefab` on `EffectsManager`. Until then
   both fall back to `meleeHitEffectPrefab` (`SoftRadialPunchMedium`), which is a *brawling impact* —
   it reads as "you got hit hard", the opposite of what a negation means. The fallback exists so
   nothing is silent, not because it's the right art.
3. Only the accessory block is tinted, and only while it's borrowing the generic spark — check it
   actually lands in Play Mode. `PlayEffect`'s tinted overload writes `main.startColor` on the instance
   and every child system; a prefab driving colour through Color-over-Lifetime or a material tint will
   ignore it and keep its authored colour. Once a bespoke `accessoryBlockedEffectPrefab` exists, that
   tint can be dropped too (pass null) for the same reason the guard's already is.

---

# 2026-08-30 — Juggernaut Shield made temporary

Player Shield stopped being merely "charge-only" (earned, never regenerates) and became genuinely
**temporary** on top of that: Brute's Juggernaut Shield now decays entirely a fixed window after the
last successful gain, rather than sitting banked indefinitely until spent or restored at a Merchant.
The design goal: a *temporary second life earned through aggression* — Discharge enemies, get a short
defensive window, keep discharging or lose it — not a stored resource that survives into the next
encounter untouched.

## What changed

Two new fields on `Shield` (`Shield.qtn`), both 0 by default so nothing except an entity that opts in
is affected:

- **`TemporaryDuration`** — how long Current survives after the most recent successful grant, seeded
  once from a new `CharacterData.ShieldTemporaryDuration` (`CharacterSystem.SeedShield`). 0 disables
  expiration outright — every hero/enemy that never opts in reads it as 0 and the two fields below are
  simply never touched, so this costs nothing for the common case.
- **`ExpirationRemaining`** — the live countdown, ticked by `ShieldSystem.TickExpiration` (folded into
  the existing `ChargeOnly` early-return, since only a charge-only shield can meaningfully expire — a
  classically recharging enemy/boss shield never reads either field). Hits 0 → `Current` snaps straight
  to 0 in the same tick. No gradual decay, no conversion to Health, no partial preservation.

**One pool, one timer, by construction.** `ExpirationRemaining` is *reset* to `TemporaryDuration`, never
added to or stacked — `ShieldUtility.ApplyFlatShield` (the single shared "grant Shield" entry point
every source in the game already funnels through: Juggernaut Discharge, Bodyguard's Shield reward,
the Store's `RestoreShieldFoodOfferData`) refreshes it on every successful grant. A second, third,
fourth Discharge in the same window collapses into the same one timer rather than opening independent
expirations — there is no way for this to accidentally create several competing countdowns.

**Deliberate interpretation: any successful grant refreshes the timer, not only a Juggernaut
Discharge specifically.** The alternative — only Discharge refreshes it, leaving Bodyguard's/the
Store's grants to land on whatever the timer already happened to be — has a real failure mode: a
grant landing after `ExpirationRemaining` had already run out to 0 would get wiped by
`TickExpiration` the very next tick, making that grant functionally worthless. Routing the refresh
through the one shared funnel instead keeps every source consistent and avoids that bug, while the
actual intent behind "must keep discharging to keep the Shield" still holds: **taking damage, dealing
weapon damage, and moving never call `ApplyFlatShield`**, so none of them can refresh it — only a
genuine Shield gain can, exactly per spec.

## Brute's own numbers

`BruteCharacterData.asset`: `BaseMaxShield` raised 20 → **60** (the new Temporary Shield cap — there
is still no per-normal/Overshield distinction, a single `Shield.Max` covers it exactly as it already
did before this pass), new `ShieldTemporaryDuration` authored at **6** seconds.
`JuggernautSkillData.ShieldGainPerHit` is unchanged at **5** per enemy caught by a Discharge — already
correct for the spec, it just now also refreshes the new expiration timer via the shared
`ShieldUtility` funnel rather than only capping at Max.

## Not changed

- **Shield-before-Health ordering, the Accessory Guard gate, and Juggernaut's own 50% channel DR** are
  all untouched — `DamageUtility`'s pipeline reads `Shield.Current` exactly as before; *why* it might
  be sitting at 0 (spent on damage vs. simply timed out) makes no difference to that pipeline.
- **No new "Shield expired" signal/event.** `OnShieldBroken`/`ShieldBroken` (the existing "Current
  crossed from >0 to <=0" hook, today driving `ShieldBroken` shatter VFX and the Shield Breaker Rift
  Mutation) deliberately does **not** fire on expiration — that hook means "a hit just broke your
  shield," and firing a shatter/impact effect on a quiet timeout would misrepresent what actually
  happened. Both HUD widgets already poll `Shield.Current`/`ExpirationRemaining` every frame, so the
  bar reads correctly either way with no extra plumbing.
- **Overshield/1.5x-Max semantics** — already fully removed in the 2026-08-25 pass above; nothing left
  to migrate.
- **Passive regeneration** — already off for every player (`ChargeOnly`); this pass only adds decay on
  top, it doesn't touch the regen gate.

## UI

Both Shield readouts (`CharacterUiWidget`, the world-space per-entity bar; `ShieldUiWidget`, the
player-1-cluster/party-strip bar) poll the two new fields directly, no event needed:

- `CharacterUiWidget.UpdateShieldExpirationWarning` pulses the fill `Image` toward a configurable
  `shieldWarningColor` once `ExpirationRemaining` drops to/below `shieldWarningThreshold` (1.5s
  default) — yields to the pre-existing recharge-shine coroutine rather than fighting it for the same
  `Image.color`.
- `ShieldUiWidget` does the same pulse and additionally appends a `(X.Xs)` countdown to its text
  readout while warning.
- Both are no-ops for anyone with `TemporaryDuration == 0` (everyone except Brute today), so no other
  hero's Shield bar changes behavior.

## Current status

Code-complete, pending Quantum's `.qtn` codegen for `Shield.qtn`'s two new fields (same "the open
Editor picks this up automatically" gotcha every other pass in this file notes). Not yet verified in
Play Mode. `BruteBaseSkill-Juggernaut.asset` still carries its pre-refactor stale
`OvershieldCapMultiplier` YAML key and its old dead sub-action `Actions` list (`CheckActions: 0`) —
both are the same pre-existing "generator hasn't been run yet" gap the 2026-08-20 sections above
already track, untouched by this pass; `Tools > RiftRaiders > Brute > Generate Ascension Assets`
regenerating that asset will drop the stale key on its own.
