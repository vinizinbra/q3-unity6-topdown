# Elemental Reactions

Reworks `ElementType` from 5 values (`Neutral`/`Fire`/`Ice`/`Lightning`/`Poison`) down to 4
(`Neutral`/`Fire`/`Ice`/`Rock`/`Void`), replacing the old 1-element-1-status mapping in
`StatusEffectUtility.TryApplyElementalStatus` with a small reaction matrix between elements. Read
this before touching anything `ElementType`/`StatusEffects`/`EffectConfig`-related - it's the source
of truth for *why* the numbers and field ownership are shaped the way they are, not just what they
are. See "Current status" at the bottom for what's actually implemented vs. still needs Editor
authoring.

## Why

The old system was 1 element -> 1 fixed status (Fire->Burn, Ice->Slow, Poison->Poison, Lightning->Stun),
each independent, no interaction between them. This rework cuts Poison and Lightning as standalone
elements - Lightning had no other consumer and is a clean delete; Poison's stacking-DoT status
(`PoisonRemaining` and friends) is fully removed too, but its one other consumer (Zara's "Poison
Damage Waves" skill) migrated to apply Void instead rather than losing the skill outright, see
"Zara's kit: Poison -> Void" below. Adds Rock, and adds Void as a 4th element whose entire purpose is
to react with whatever else is already on a target rather than apply anything on its own.

## Baseline (solo) statuses

Unchanged from today except where noted. Each is a `Weapon`-sourced hit, `ElementalChance`-gated
(`CharacterStats.ElementalChance`, the same roll crit uses), applied inside
`StatusEffectUtility.TryApplyElementalStatus`.

| Element | Solo status | Notes |
|---|---|---|
| Fire | Burn (DoT) | Unchanged from today. |
| Ice | Slow (`IceRemaining`/`IceSpeedMultiplier`) | Unchanged from today - plain overwrite-on-reapply, **no stacking**. (A stacking-toward-freeze version was explored and explicitly rejected - see "Rejected direction" below.) |
| Rock | Intimidate (`IntimidateRemaining`/`IntimidateDamageMultiplier` - reduces the *target's own outgoing damage*) | New assignment. Deliberately **not** Root - see "Why Rock isn't Root" below. |
| Void | None | Applies only `VoidRemaining` (new field, plain overwrite-on-reapply, same shape as Ice/Break/Intimidate) and a visible tell (VFX/icon) on the target. Does nothing by itself - it exists purely to be checked for by the reaction scan below. Not consumed on trigger - stays active for its own duration and can back multiple reactions. |

### Why Rock isn't Root

Root (`RootRemaining` - pins movement only, doesn't touch attacking/skills) looked like a natural fit
for Rock's baseline at first. Rejected: Root is already load-bearing elsewhere -
`JuggernautLandingRootSkillAction`/`JuggernautLandingImpactSystem` (Brute's Juggernaut skill) reads
`RuntimeConfig.EffectConfig.RootDuration` directly. If Rock's baseline used the same field, retuning
Juggernaut's root would silently retune Rock too. Root stays reserved for the Fire+Rock reaction
below, where it's genuinely a new, dedicated (`MagmaPrisonRootDuration`) value instead of a shared one.

This is the general rule for every field in this system, not just Root: **a reaction/element may only
reuse an existing config field if every other consumer of that field is being removed in this same
refactor. Otherwise it gets its own dedicated field.**

**Correction, found during implementation:** Stun and Mark were originally assumed to qualify for
reuse too (their apparent only owners - Lightning, free-floating Mark grants - are being cut). That
was wrong. `EffectConfig.StunDuration` is also read directly by `StunEffectData` (a generic,
freely-authorable `HitEffectData`), and `EffectConfig.MarkDuration`/`MarkDamageTakenMultiplier` were
also read by `MarkEffectData`, which backed **two live hero skills** - Brute's
`JuggernautMarkSkillAction` and Kai's `VortexMarkSkillAction` ("Void Mark"). Both fields had a live
consumer outside this refactor, same as Root. So in practice **none of the six reactions reused an
existing `EffectConfig` field** - every one of them got a fully dedicated field on
`ElementalReactionConfig` instead (see the table below).

**Later update:** `MarkEffectData` and both hero skills that granted it were removed outright, and
the underlying incoming-damage-multiplier mechanism they shared with this reaction was renamed
entirely to Break, since Break was already its only remaining consumer - `StatusEffects.MarkRemaining`/
`MarkDamageMultiplier` are now `BreakRemaining`/`BreakDamageMultiplier`,
`StatusEffectUtility.ApplyMark`/`HasMarkDebuff` are now `ApplyBreak`/`HasBreakDebuff`, and
`EffectConfig.MarkDuration`/`MarkDamageTakenMultiplier` are gone (nothing reads them anymore - Break
still gets its own dedicated fields on `ElementalReactionConfig`, unchanged). Stun keeps its own
dedicated field, `StunEffectDuration`, since `StunEffectData` is still a live, separate consumer of
`EffectConfig.StunDuration`.

## The reaction scan

This is the actual mechanism, and it's the same one regardless of which element triggers it - Void is
not special-cased in the algorithm, only in that it has no baseline status of its own:

> Whenever any element's `ElementalChance` roll succeeds and it's about to apply its baseline status
> to a target, first scan the target for every *other* element's active status (baseline or Void).
> For each one found, fire that pair's reaction (if its own trigger cooldown isn't active), then apply
> the landing element's own baseline status as normal (Void applies none).

Consequences worth calling out explicitly, since they came up during design and are easy to get wrong
when this is actually implemented:

- **Order doesn't matter.** Fire-then-Ice and Ice-then-Fire both produce the Stun reaction - whichever
  element lands second is the one doing the scanning, but the pair is symmetric.
- **One hit can trigger more than one reaction.** If a target already has both Ice-slow and
  Rock-Intimidate active and a Fire hit lands, that single hit checks Fire+Ice (Stun) *and* Fire+Rock
  (Magma Prison) and fires both, independently, each gated by its own cooldown. This is intentional -
  it's the payoff for a co-op team stacking different elements on the same target, not a bug to guard
  against. No cap on simultaneous triggers per hit.
- **No extra chance roll for the reaction itself.** `ElementalChance` already gated whether the second
  element's baseline landed at all; once both statuses are present, the reaction fires deterministically.
- **Every reaction has its own independent trigger cooldown**, per target, to stop repeat-proc spam
  (e.g. a fast-firing Fire weapon re-detonating Explosion on every single Void-gated proc tick). Same
  shape as the existing `JuggernautDischargeCooldown` pattern (a per-enemy immunity timer, generalized
  to one slot per reaction instead of one). This is state, not config - the config only holds the
  cooldown *length*.

### The scan has more than one entry point

`TryTriggerReactions` isn't only reached from the weapon-elemental-proc path
(`TryApplyElementalStatus`) - it's `internal`, not `private`, specifically so `BurnEffectData`/
`SlowEffectData`/`VoidEffectData` (the generic, freely-authorable `HitEffectData` classes any skill
or weapon perk can drop onto their own Effects list, independent of a weapon's `Element` roll) also
call it after applying their own baseline status. This was a real bug, not a design choice found
early: Zara's Void Damage Waves applies Void purely through `VoidEffectData`, so until this was
wired in, a target she Voided would never actually react to anything - only the narrower weapon-proc
path ever checked. `TryApplyGuaranteedBurn` (CharacterStats.BurnOnHitStacks) had the same gap for the
same reason (its own early-return happened before the scan ran) and got the same fix.

**Root and Freeze do NOT call `TryTriggerReactions`.** `RootEffectData`/`FreezeEffectData` (added
alongside `Stun`'s existing generic effect, so every CC status has one) apply Root/
AnticipationSlow directly and stop there - Root and Freeze aren't elements, they're *outputs* of
Magma Prison and Void+Ice respectively, so there's no `ElementType` to scan against when one is
granted directly by some other skill. Only Fire/Ice/Rock/Void (the 4 real elements) ever feed the
scan.

## The 6 reactions

4 elements -> C(4,2) = 6 pairs, all named:

| Pair | Name | Effect | Notes |
|---|---|---|---|
| Void + Fire | Explosion | AoE burst damage, additional to Burn | New mechanic. Fires its own `VoidExplosionReleased` event (not `WeaponExplosionReleased`, which weapon-perk explosions like Cataclysm Round share) so it gets a dedicated VFX slot (`EffectsManager.voidExplosionEffectPrefab`) instead of the generic shared blast - falls back to `defaultAreaBlastEffect` tinted purple until a bespoke prefab is authored. |
| Void + Ice | Freeze | Applies **Freeze** (new `AnticipationSlowRemaining`/`AnticipationSlowMultiplier` fields - stretches the target's attack windup, not a lockout), additional to Slow | Deliberately *not* a second hard-CC - see "Freeze: stretching anticipation, not stopping the target" below. |
| Void + Rock | Knockback | Physical push impulse, additional to Intimidate | Instant impulse, not a duration status - no new `StatusEffects` field needed, just a force magnitude. |
| Fire + Rock | Magma Prison | Applies Root + Burn together | Root reserved exclusively for this reaction (see above). |
| Ice + Fire | Stun | Applies Stun (`StunRemaining`) | Own dedicated `StunEffectDuration`, NOT `EffectConfig.StunDuration` - that field is still live (`StunEffectData`), see the correction above. |
| Ice + Rock | Break | Increased incoming damage (`BreakRemaining`/`BreakDamageMultiplier`), duration-based | Own dedicated `BreakDuration`/`BreakDamageTakenMultiplier` on `ElementalReactionConfig` - this is now the sole owner of the underlying incoming-damage-multiplier mechanism, see "Later update" above. A shield-bypass bonus clause (this reaction's damage also ignores `Shield` absorption) was discussed as a nice-to-have layered on top, not the core mechanic - core mechanic is the multiplier, since it's consistent in shape with the other two combos (a lingering debuff window) and matters against every target, not just shielded ones. |

### Freeze: stretching anticipation, not stopping the target

Freeze was originally going to reuse Stun outright (`StunRemaining` - full movement/firing/state-machine
lockout), same end state as the Ice+Fire Stun reaction. Rejected: two reactions producing an identical
outcome only differ by VFX, which wastes the second reaction slot and gives Freeze no identity of its
own. A pure deeper-slow version (push `IceSpeedMultiplier` further toward 0%) was considered next, but
that's just Ice's own baseline getting stronger, not a distinct effect a reaction should be earning.

Landed instead on stretching the target's attack windup: Freeze multiplies the time an enemy spends in
`AttackPhase.Anticipation`/`Preparation` before a `TelegraphData` becomes visible/committed (see
`EnemyActionData.AnticipationTime`/`TelegraphStartPercent`) via new `AnticipationSlowRemaining`/
`AnticipationSlowMultiplier` fields, read only where that phase's timer is decremented in `EnemySystem`.
This deliberately mirrors `TimeDilationRemaining`'s shape (a duration + a multiplier, read at one
specific timer) but targets the *opposite* phase - Kai's Void Pressure ascension already scopes
`TimeDilationMultiplier` to the Active-phase timer only, explicitly never Preparation/Telegraph (see
`StatusEffects.qtn`'s own comment on that field), so Freeze needs its own dedicated fields rather than
widening TimeDilation's scope and breaking that existing exclusion.

The payoff is a three-way split with no overlap: Ice's baseline Slow controls *mobility* (how fast a
target can chase/reposition), Ice+Fire Stun is a *hard interrupt* (stops everything, briefly), and
Void+Ice Freeze is a *defensive read window* (attacks still happen, but their tell is slower and easier
to dodge). The tradeoff: Freeze has no effect on an enemy that isn't currently mid-windup, so it does
little against melee grunts that don't telegraph - it's strongest against telegraphed/ranged attackers,
which is an acceptable, even desirable, asymmetry rather than a gap to fix.

## Config field ownership

Everything above needs numbers somewhere designers can tune without recompiling. Following this
project's existing convention of one dedicated config asset per system domain (`EffectConfig`,
`SurvivalConfig`, `ExperienceConfig`, `LevelUpConfig` all already work this way):

- **`EffectConfig`** (existing asset) gets four additions: `VoidDuration` (Void's baseline is
  single-element shaped even though it does nothing, so it belongs with the others, not with the
  reactions below), `IntimidateDuration`/`IntimidateOutgoingDamageMultiplier` (Rock's baseline -
  genuinely unclaimed, `ApplyIntimidate` takes explicit params from Brute's aura component today, not
  a shared config field), and `AnticipationSlowDuration`/`AnticipationSlowMultiplier` (the new generic
  `FreezeEffectData`'s own knob - deliberately NOT `ElementalReactionConfig`'s
  `FreezeDuration`/`FreezeAnticipationMultiplier`, which stay dedicated to the Void+Ice reaction; named
  after the underlying `StatusEffects` field rather than "Freeze" so the two are never confused for
  the same knob). `PoisonDuration`/`PoisonDamagePercent`/`PoisonFloorPercent` are deleted from
  `EffectConfig` in the same pass, once Poison is gone - not left as dead weight. Also cleaned up in
  the same pass: `EnemyTierResistanceConfig.PoisonDamageMultiplier`, the per-tier resistance field
  that only `ApplyPoison` ever read.
- **`RootEffectData`/`FreezeEffectData`/`MagmaPrisonEffectData`** (new, alongside the pre-existing
  `StunEffectData`) give Root, Freeze, and the Root+Burn combo the same freely-authorable
  `HitEffectData` any skill/weapon perk can drop onto its own Effects list, independent of both
  Juggernaut's landing-root skill and the elemental reactions that normally grant them.
  `RootEffectData` reuses the existing (already generically-scoped) `EffectConfig.RootDuration`;
  `FreezeEffectData` uses the new `AnticipationSlowDuration`/`AnticipationSlowMultiplier` pair above;
  `MagmaPrisonEffectData` needs no new fields at all - it's `RootEffectData` and `BurnEffectData`
  bundled into one authoring convenience, reusing `RootDuration` and Burn's own already-generic
  fields rather than `ElementalReactionConfig.MagmaPrisonRootDuration` (which stays dedicated to the
  Fire+Rock reaction, since that field has a live consumer). None of the three call the reaction
  scan - see "The scan has more than one entry point" above for why.
- **A new `ElementalReactionConfig` asset** (parallel structure to `EffectConfig`, referenced from
  `RuntimeConfig.User.cs`, fetched via a `GetElementalReactionConfig(f)` helper mirroring the existing
  `GetEffectConfig(f)`) holds one field-group per reaction, always `<Name>TriggerCooldown` plus
  whatever magnitude that reaction needs:

  | Reaction | Fields |
  |---|---|
  | Explosion | `ExplosionTriggerCooldown`, `ExplosionDamagePercent`, `ExplosionRadius` |
  | Freeze | `FreezeTriggerCooldown`, `FreezeDuration`, `FreezeAnticipationMultiplier` |
  | Knockback | `KnockbackTriggerCooldown` only - force/upward-force reuse `EffectConfig.GetKnockback(KnockbackTier.Strong, ...)`, see below |
  | Magma Prison | `MagmaPrisonTriggerCooldown`, `MagmaPrisonRootDuration` |
  | Stun | `StunTriggerCooldown`, `StunEffectDuration` (own field - NOT `EffectConfig.StunDuration`, see the correction above) |
  | Break | `BreakTriggerCooldown`, `BreakDuration`, `BreakDamageTakenMultiplier` (own fields - Break is the sole owner of the underlying incoming-damage-multiplier mechanism, see "Later update" above) |

  Never a field shared between a reaction and anything outside this table - see the field-ownership
  rule above. **One deliberate exception:** Knockback's force/upward-force reuse
  `EffectConfig.GetKnockback(KnockbackTier.Strong, ...)` rather than a dedicated field - that bucket
  is explicitly designed to be shared by every pusher in the game (its own doc comment: "reused by
  every KnockbackEffectData in the game... instead of each authoring its own pair"), unlike
  Root/Stun/Break which are each a single-purpose knob some other system already owns. Only
  `KnockbackTriggerCooldown` is genuinely reaction-owned.

## Zara's kit: Poison -> Void

`PoisonDamageWavesUpgrade`/`PoisonDamageWavesSkillAction`/`PoisonEffectData` (Zara's "Poison Damage
Waves" skill - baked a stacking-DoT `HitEffectData` into her periodic wave's damage effects) migrated
to `VoidDamageWavesUpgrade`/`VoidDamageWavesSkillAction`/`VoidEffectData` once Poison was fully
removed (not just cut from the weapon-proc switch - the `PoisonRemaining` stacking arrays are gone
entirely, so nothing was left for this skill to apply). Same file shape, same bake-once-at-spawn
mechanism (`SpawnAlternatingAreaEffectData.ApplyVoidUpgrade`) - only the effect itself changed.

**Real kit consequence, not just a reskin:** Void has no baseline effect of its own, so this skill
went from "deals stacking poison damage over time" to "marks enemies with Void, priming them for
whichever element (this Zara's own weapon, or a teammate's) lands next." It stops being a direct
damage-over-time skill and becomes a team-enabling one - a deliberate call for this migration, not a
side effect to design around later.

## Rejected directions for Freeze

Two earlier versions were walked back before landing on the anticipation-stretch design above:

- **Stacking Ice toward Freeze.** Ice's Slow would stack like Poison does (`array<FP>[5]`, -0.10 speed
  per stack, capped at 5 solo / 10 with Void active, 10 stacks = fully frozen at 0% speed), with Freeze
  as the emergent result of hitting the cap rather than its own triggered reaction. Dropped: needed a
  *dynamic* cap (5 normally, 10 with Void present, checked at application time) instead of Poison's
  fixed cap, needed `IceRemaining` reshaped into an array, and still left open whether 0% speed alone
  should count as a real freeze (stopping actions) or just movement.
- **Reusing Stun outright.** Freeze would just apply `StunRemaining`, same full lockout as the Ice+Fire
  Stun reaction, distinguished only by its own `FreezeDuration`/`FreezeTriggerCooldown` and VFX. Dropped
  once framed as a design question rather than a field-reuse question: two reactions producing an
  identical player-facing outcome don't earn their own identity just by having separate config knobs -
  see "Freeze: stretching anticipation, not stopping the target" above for what replaced it.

## Current status

Implemented and live: `ElementType` enum (`Neutral`/`Fire`/`Ice`/`Rock`/`Void`), all `StatusEffects.qtn`
fields (`VoidRemaining`, `AnticipationSlowRemaining`/`AnticipationSlowMultiplier`, the 6 per-reaction
cooldowns), `EffectConfig`'s `VoidDuration`/`IntimidateDuration`/`IntimidateOutgoingDamageMultiplier`/
`AnticipationSlowDuration`/`AnticipationSlowMultiplier` additions and its Poison field removals, the
`ElementalReactionConfig` asset class + a real authored instance wired into `RuntimeConfig` (both the
class and the scene's own reference to it - the asset existing alone isn't enough, `RuntimeConfig`
has to actually point at it or every `GetElementalReactionConfig` call returns null and every
reaction silently no-ops), the full reaction-scan + 6 `TryTrigger*` helpers in `StatusEffectUtility`
(reachable from both the weapon-elemental-proc path AND the generic `Burn`/`Slow`/`VoidEffectData`
hit effects - see "The scan has more than one entry point" above for the bug that gap caused),
`RootEffectData`/`FreezeEffectData` (generic hit effects for the other two CC statuses),
`EnemySystem.UpdatePreparation` reading `GetAnticipationMultiplier`, the matching view-side stretch
for Freeze (`EnemyBlobAnimationView`'s windup animation and `TelegraphGrow`'s ground-decal growth
both now scale by the same live multiplier instead of running on plain `Time.deltaTime`), HUD/particle
indicators for Void and Freeze, a dedicated `VoidExplosionReleased` VFX event/slot
(`EffectsManager.voidExplosionEffectPrefab`, falling back to a purple-tinted `defaultAreaBlastEffect`
until a bespoke prefab is authored), and Zara's Poison->Void kit migration (code + the actual `.asset`
YAML, including the `m_EditorClassIdentifier`/field-name strings Quantum's own asset serialization
needs updated on a class rename - a Unity `MonoScript` GUID surviving a file rename is not enough by
itself).

**Not done yet:** no new flash colors for Ice/Rock hits, no numbers have had a real balance pass
(every config field still carries a placeholder value), and no bespoke VFX/HUD icon prefabs exist yet
for Void/Freeze's new indicator slots - they're wired but showing nothing until something's dragged
into the Inspector, same "code's ready, Editor authoring still pending" gap this project's other
systems (Director, Experience, Level-Up) already had.
