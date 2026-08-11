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
got): every enemy caught by a Discharge grants Brutus `ShieldGainPerHit` (5) as **Overshield**, not a
capped-at-Max restore - `ShieldUtility.ApplyOvershield` adds straight to `Shield.Current`, capped at
`OvershieldCapMultiplier` (1.5x) of his own Max Shield rather than 1x, so a multi-enemy Discharge can
push him above his own Max Shield without stacking unboundedly. Nothing else needed to support the
"above Max" half: `ShieldSystem`'s passive regen already no-ops whenever `Current >= Max`, and
`DamageUtility.AbsorbWithShield` already drains `Current` by a plain `Min(Current, damage)` regardless
of how far above Max it sits - the overshield just bleeds off as normal Shield would as damage lands.

**1. Momentum** (`MomentumSkillAction` → `MomentumUpgrade`, absorbs old Momentum + Unstoppable)
- Rank 1: Momentum builds 25% faster while running during Juggernaut (`GenerationMultiplier` scales
  `SkillSlot.LastStepDistance` before it's added toward the next Charge point - see
  `JuggernautSkillData.AdvanceCharge`); +10% Move Speed while Charged (`ChargedMoveSpeedBonus`, folded
  into `ResolveChargedMoveSpeedBonus`); after Discharge, Charge only resets down to 30% instead of
  fully draining (`DischargeRetentionFraction` - see `JuggernautSkillData.TryDischarge`).
- Rank 2: 40% faster; +20% Move Speed while Charged; Discharge resets down to 60% instead of fully
  draining.
- Rank 3: 40% faster (same as rank 2); +30% Move Speed while Charged; Discharge no longer resets
  Charge at all (`DischargeRetentionFraction` 100%).

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
