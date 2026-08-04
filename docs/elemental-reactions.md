# Elemental Reactions & Rift Mark

Reworks `ElementType` to 6 values (`Neutral`/`Fire`/`Ice`/`Rock`/`Void`/`Lightning`) and replaces the
old pairwise element-vs-element reaction scan with a single stackable status - **Rift Mark** - that
each of the 5 real elements (Fire/Ice/Rock/Void/Lightning) can land on and consume to fire its own
named reaction. Rift Mark and Void are deliberately separate concepts now: Void is a weapon affinity
flavored around inward displacement/negative knockback, Rift is an unstable amplifying force that
enables amplified Affinity Reactions - see "History" below for how they used to be conflated. Read
this before touching anything `ElementType`/`StatusEffects`/`RiftMark`/`ElementalReactionConfig`-related -
it's the source of truth for *why* the numbers and field ownership are shaped the way they are, not
just what they are. See "Current status" at the bottom for what's actually implemented vs. still needs
Editor authoring.

## History: from pairwise Void to stackable Rift Mark

The original version of this system (still worth understanding, since some of its design calls
carry forward) cut Poison and Lightning as standalone elements and added a 4th element, Void, whose
entire purpose was to react with whatever else was already on a target - Void applied no baseline of
its own, was **never consumed** when it backed a reaction (one application could back several
reactions over its lifetime), and the reaction scan ran between **any two** of Fire/Ice/Rock/Void's
active statuses (6 pairs: Explosion, Freeze, Knockback, Magma Prison, Stun, Break).

That mechanic is retired. Void is now promoted to a real damage-dealing element (5th alongside a
reintroduced Lightning), and the "mark" role moves to its own dedicated, **stackable** status - Rift
Mark - applied only by a dedicated skill/perk effect (`RiftMarkEffectData`), never by a weapon's own
`Element` roll. Landing Fire/Ice/Rock/Lightning/Void on a target that already carries at least one
Rift Mark stack **consumes exactly one stack** and fires exactly **one** reaction - never more than
one per hit, and never a "back several reactions over its lifetime" free-ride the way the old Void
did. Fire+Rock (Magma Prison) and the old Ice+Fire/Ice+Rock pairings have no equivalent in this model
and were retired from the scan entirely (see "What was retired" below) - every reaction is now
`<element> + RiftMark`, nothing reacts with anything else. The reactions themselves were also renamed
to their final names in the same pass - Explosion→Detonation, Freeze→Deep Freeze, Break→Rupture,
Stun→Overload (Singularity was already final).

Void and Lightning deliberately have **no baseline status of their own** even as real elements - their
identity lives in hand-authored `WeaponDataAsset` traits instead (e.g. a Lightning gun starting with
Ricochet, a Void gun starting with Pierce - Void's own flavor is inward displacement/negative
knockback specifically, not a status), not in status-effect code. This is why
`TryApplyElementalStatus`'s baseline switch has no `case` for either.

## Rift Mark

A stackable status, config-driven cap, MVP default 2 stacks:

| Field (`StatusEffects.qtn`) | Type | Role |
|---|---|---|
| `RiftMarkStacks` | `Byte` | Current stack count, `0..ElementalReactionConfig.MaxStacks` |
| `RiftMarkRemaining` | `FP` | One shared duration for every stack - not tracked per stack |
| `RiftMarkReactionLockoutRemaining` | `FP` | Global gate after any stack is consumed - independent of each reaction's own per-reaction cooldown below |

Applied only by `RiftMarkEffectData` (`StatusEffectUtility.ApplyRiftMark`) - a freely-authorable
`HitEffectData`, same shape as `BurnEffectData`/`SlowEffectData`, any skill or weapon-perk Effects
list can drop it. There is no weapon-rollable "Rift" `ElementType` - marking is deliberately a
skill/perk-only mechanic, distinct from the 5 real elements a weapon can roll. Weapon Perks and Rift
Mutations (see their own docs) are the other two application sources, layered on top through the
application/dedup layer described there.

Every application (`ApplyRiftMark`) clamps `RiftMarkStacks` to `[0, MaxStacks]` and, when
`RefreshDurationOnApply` is true, resets the shared duration - **including at max stacks**, so
reapplying an already-maxed mark still refreshes it (this is the whole reason `RefreshDurationOnApply`
exists as its own flag rather than only refreshing on an actual stack-count change).

`StatusEffectSystem.TickRiftMark` decrements `RiftMarkRemaining` only while `RiftMarkStacks > 0`, and
zeroes `RiftMarkStacks` outright the instant the duration lapses - expiration removes every remaining
stack at once, not one at a time.

## Configuration - `ElementalReactionConfig`

Rift Mark's own 6 tunables live on the *existing* `ElementalReactionConfig` asset (not a new one) -
it's already the one dedicated config asset for this whole domain, so Rift Mark's knobs join it
rather than spawning a parallel `RuntimeConfig` entry:

| Field | MVP default | Role |
|---|---|---|
| `MaxStacks` | 2 | Hard cap every `ApplyRiftMark` clamps to |
| `BaseDuration` | 5s | Shared duration applied/refreshed on every apply |
| `RefreshDurationOnApply` | true | Whether reapplying resets the shared duration |
| `StacksAppliedPerApplication` | 1 | Stacks `RiftMarkEffectData` grants per hit |
| `StacksConsumedPerReaction` | 1 | Stacks a fired reaction consumes |
| `ReactionLockoutDuration` | 0.75s | Global cooldown after any consumption, on top of each reaction's own |

None of these are hardcoded anywhere in status/damage/reaction/UI/VFX/gameplay code - every consumer
reads them off the resolved `ElementalReactionConfig` (`StatusEffectUtility.GetElementalReactionConfig`).
The same asset also carries the Weapon Perk/Rift Mutation content pool's own data-driven values
(thresholds, radii, the shared per-mechanic cooldown array) - see `docs/weapon-perks.md`/
`docs/rift-mutations.md`.

## Event order

For every hit (weapon-elemental-proc path or a directly-authored guaranteed-element effect like
`BurnEffectData`), Rift Mark resolves deterministically in this order:

1. **Capture pre-hit stacks.** `HitEffectContext.PreHitRiftMarkStacks` is set once, at the very top of
   `HitEffectUtility.ApplyToTarget`/`WeaponSystem.FireHitscan`, before anything about this hit runs -
   including this same hit's own Effects list.
2. **Resolve normal damage and the landing element's own baseline** (Fire→Burn, Ice→Slow,
   Rock→Intimidate; Lightning/Void have none).
3. **Check validity.** `StatusEffectUtility.IsValidAffinityProc(preHitStacks, lockoutRemaining)` -
   true only if the target had at least one stack *before* this hit, and the shared lockout isn't
   currently active.
4. **Consume + react.** If valid, `TryConsumeRiftMarkReaction` dispatches to exactly one
   `TryTrigger*` (gated by that reaction's own `*CooldownRemaining`, same as before); if it actually
   fires, exactly `StacksConsumedPerReaction` stacks are removed and the shared lockout starts.
5. **Resolve this hit's own Rift Mark grants**, if any (`RiftMarkEffectData`, elsewhere in the same
   Effects list) - runs after step 4, using the *live* `RiftMarkStacks` at that point, so a stack this
   hit grants is layered on top of (never substitutes for) whatever step 4 already resolved.
6. Stacks are clamped to `MaxStacks` and duration refreshed as part of step 5's `ApplyRiftMark` call.

**A mark created by a hit is never consumed by that same hit** - guaranteed by step 1's snapshot,
not by call ordering within the Effects list, so it holds regardless of the order a skill's Effects
list happens to author `RiftMarkEffectData` relative to its damage/element effects. A hit against an
*already*-marked target can still consume the pre-existing stack in step 4 and grant a fresh one in
step 5 - both can happen on one hit, just never in a way that lets a hit consume what it itself just
created.

Same-frame double-consumption (a shotgun-pattern weapon's multiple pellets, or several `HitEffectData`
entries in one Effects list each independently calling `TryConsumeRiftMarkReaction` for their own
element) is prevented by `RiftMarkReactionLockoutRemaining` being checked-then-set atomically inside
`TryConsumeRiftMarkReaction` - Quantum's single-threaded, ordered per-frame system execution means
nothing can interleave between that check and that write within one tick. Weapon Perk/Rift Mutation
application requests go through the same event-order discipline, one level up - see
`docs/rift-mutations.md`'s "Event resolution order" section.

## The 5 reactions

Every reaction keeps its pre-existing effect from the old pairwise system (only Singularity is new) -
just repointed from an element-pair trigger to an element+RiftMark trigger, and renamed to its final
name in the same pass this doc's history section describes:

| Element + RiftMark | Reaction | Effect | Notes |
|---|---|---|---|
| Fire | Detonation | AoE burst damage, additional to Burn | Was Void+Fire "Explosion". Fires `DetonationReleased` (renamed from `ExplosionReleased`, itself renamed from `VoidExplosionReleased`) for its own dedicated VFX slot. |
| Ice | Deep Freeze | Applies **Deep Freeze** (`AnticipationSlowRemaining`/`Multiplier` - stretches attack windup, not a lockout), additional to Slow | Was Void+Ice "Freeze". See "Deep Freeze: stretching anticipation, not stopping the target" below. |
| Rock | Rupture | Increased incoming damage (`RuptureRemaining`/`RuptureDamageMultiplier`), **plus a knockback impulse bundled in** | Was Ice+Rock "Break" with the old standalone Knockback reaction (was Void+Rock) folded in as one combined push-and-debuff proc - one reaction, one cooldown, instead of two. |
| Lightning | Overload | Applies Stun (`StunRemaining`) | Was Ice+Fire "Stun". Own dedicated `OverloadStunDuration`, not `EffectConfig.StunDuration` (still live via `StunEffectData`). |
| Void | Singularity | Pulls every enemy within `SingularityRadius` toward the reaction's target | New mechanic - no prior equivalent. Instant knockback-style impulse, direction inverted (pull instead of push), same shape as the old Knockback reaction. Fires `SingularityTriggered` for its own VFX slot. |

### What was retired

Fire+Rock (**Magma Prison**) had no equivalent element+RiftMark pairing in the new design and was
removed from the scan outright - `TryTriggerMagmaPrison`, `MagmaPrisonCooldownRemaining`, and
`ElementalReactionConfig.MagmaPrisonTriggerCooldown`/`MagmaPrisonRootDuration` are gone.
**`MagmaPrisonEffectData`** (the standalone, freely-authorable Root+Burn `HitEffectData` any skill can
still drop) is untouched - it never called the reaction scan to begin with, so nothing about it
depended on Fire+Rock existing as a reaction.

### Deep Freeze: stretching anticipation, not stopping the target

*(Unchanged from the original design - carried forward verbatim since the rationale doesn't depend on
which trigger fires it, only the reaction's own name changed.)*

Deep Freeze was originally going to reuse Stun outright (`StunRemaining` - full movement/firing/
state-machine lockout), same end state Lightning+RiftMark's own Overload reaction produces. Rejected:
two reactions producing an identical outcome only differ by VFX, which wastes the second reaction slot
and gives Deep Freeze no identity of its own. Landed instead on stretching the target's attack windup:
Deep Freeze multiplies the time an enemy spends in `AttackPhase.Anticipation`/`Preparation` before a
`TelegraphData` becomes visible/committed, via `AnticipationSlowRemaining`/`AnticipationSlowMultiplier`,
read only where that phase's timer is decremented in `EnemySystem`. This deliberately mirrors
`TimeDilationRemaining`'s shape but targets the opposite phase - Kai's Void Pressure ascension already
scopes `TimeDilationMultiplier` to the Active-phase timer only, explicitly never Preparation/Telegraph.

The payoff is a three-way split with no overlap: Ice's baseline Slow controls *mobility*, Lightning's
Overload is a *hard interrupt*, and Ice+RiftMark's Deep Freeze is a *defensive read window* (attacks
still happen, but their tell is slower and easier to dodge). Deep Freeze has no effect on an enemy
that isn't currently mid-windup, so it does little against melee grunts that don't telegraph - an
acceptable asymmetry, not a gap to fix.

## Field ownership

Same convention as every other system domain here (`EffectConfig`, `SurvivalConfig`,
`ExperienceConfig`, `LevelUpConfig`): **a reaction/element may only reuse an existing config field if
every other consumer of that field is being removed in the same pass. Otherwise it gets its own
dedicated field.** Consequently every reaction on `ElementalReactionConfig` owns its fields outright,
with one deliberate exception: Rupture's knockback force/upward-force reuse
`EffectConfig.GetKnockback(KnockbackTier.Strong, ...)` rather than a dedicated pair, since that bucket
is explicitly designed to be shared by every pusher in the game (its own doc comment says as much) -
only `RuptureTriggerCooldown` etc. are genuinely reaction-owned. `EffectConfig` itself still carries
Fire/Ice/Rock's baseline fields (`BurnDuration`, `SlowDuration`, `IntimidateDuration`, ...) - those are
untouched by this rework, only Void's old `VoidDuration` field was removed (nothing reads it anymore -
Rift Mark's duration is `ElementalReactionConfig.BaseDuration` instead).

## Multiplayer and determinism

- Every timer (`RiftMarkRemaining`, `RiftMarkReactionLockoutRemaining`, each reaction's own
  `*CooldownRemaining`) is a plain `FP` countdown ticked by `f.DeltaTime` in `StatusEffectSystem`,
  Quantum's fixed-tick deterministic delta - no `UnityEngine.Time` anywhere in this system.
  `StatusEffectSystem`/`StatusEffectUtility` are 100% deterministic simulation code.
- Any player can apply a mark (any owner's `RiftMarkEffectData`-carrying skill/perk) or consume one
  (any owner's Fire/Ice/Rock/Lightning/Void hit) - `ApplyRiftMark`/`TryConsumeRiftMarkReaction` never
  branch on which player's client is local; ownership (`EntityRef owner`) is tracked only for
  attribution (who gets credit for a reaction's damage/event), never gates whether the stack
  behavior itself runs.
- Simultaneous applications/consumptions across players resolve in whatever order Quantum's
  deterministic system/entity iteration processes them that tick - same guarantee every other
  `StatusEffects` field in this codebase already relies on, nothing Rift-Mark-specific was added or
  needed here.

## UI and VFX

- `HitFeedback.riftMarkMaterial`/`MarkState.Rift` - binary material swap while `IsRiftMarked` is true,
  doesn't itself distinguish 1 vs 2 stacks (that's the HUD indicator's job below).
- `StatusEffectsManager.riftMarkParticlePrefab` - persistent world-space particle while marked, same
  as before, not currently stack-count-aware (a single particle, not one per stack).
- `CharacterUiWidget.riftMarkIndicator` (a `StatusIndicator`) - unlike every other status indicator,
  its `timerText` doesn't show the countdown; `UpdateRiftMark` repurposes it to show the current stack
  count as `"xN"` instead. The shared duration is still tracked in `StatusEffects.RiftMarkRemaining`
  and drives `SetShown`, it's just not displayed as text anymore.
- `EffectsManager.detonationEffectPrefab`/`singularityEffectPrefab`/`overflowingRiftPulsePrefab` -
  one-shot dedicated VFX slots for reactions/mutations that fire a world-space event
  (`DetonationReleased`/`SingularityTriggered`/`OverflowingRiftTriggered`), each falling back to
  `defaultAreaBlastEffect` tinted its own fallback color until a bespoke prefab is authored, same
  pattern every other one-shot reaction/perk blast in this file already uses.
- **Color rule**: Rift Mark's own presentation (material glow, particle, indicator, application
  flash, Overflowing Rift's pulse) uses hot-pink `#FD3971` with dark/black fracture shapes - purple is
  reserved for Void specifically (Singularity's own VFX legitimately stays purple/dark, since that
  reaction's whole identity is Void reacting, not the mark itself). Rift Mark stays visually quiet
  until consumed: no full-enemy tint, no large floating text per application, one shared visual
  language across every application source (weapon perk or mutation) rather than a new VFX per
  source - see `docs/rift-mutations.md`/`docs/weapon-perks.md` for the specific perks/mutations that
  request Rift Mark applications.

## Current status

Implemented and live: `ElementType` enum (`Neutral`/`Fire`/`Ice`/`Rock`/`Void`/`Lightning`, Lightning
appended at the end to avoid renumbering Void's existing ordinal), `StatusEffects.qtn`'s
`RiftMarkStacks`/`RiftMarkRemaining`/`RiftMarkReactionLockoutRemaining`/`DetonationCooldownRemaining`/
`DeepFreezeCooldownRemaining`/`OverloadCooldownRemaining`/`RuptureCooldownRemaining`/
`SingularityCooldownRemaining` fields (and removal of `VoidRemaining`/`KnockbackCooldownRemaining`/
`MagmaPrisonCooldownRemaining`), `ElementalReactionConfig`'s 6 Rift Mark fields plus the per-reaction
fields (renamed to their final Detonation/DeepFreeze/Overload/Rupture/Singularity names),
`StatusEffectUtility`'s full rework (`ApplyRiftMark`/`GetRiftMarkStacks`/`IsRiftMarked`/
`ConsumeRiftMarkStack`, the pure `ClampStacks`/`IsValidAffinityProc` helpers, `TryConsumeRiftMarkReaction`
and all 5 `TryTrigger*` reactions), `RiftMarkEffectData` (renamed from `VoidEffectData`, no longer
calls into the reaction scan at all - marks and reactions are now asymmetric), the
`PreHitRiftMarkStacks` event-order plumbing through `HitEffectContext`/`HitEffectUtility`/
`WeaponSystem.FireHitscan`/`BurnEffectData`/`SlowEffectData`, every renamed VFX/UI consumer
(`HitFeedback`, `StatusEffectsManager`, `EffectsManager`, `CharacterUiWidget`,
`ProjectileElementalFxView`'s Lightning particle slot), the hot-pink `#FD3971` Rift Mark color rule,
and a minimal EditMode NUnit suite (`Assets/_QuantumUser/Editor/Tests/RiftMarkStackTests.cs`) covering
the pure stack-math/proc-validity logic.

**Not done yet / known simplifications:**
- **Poison** was explicitly deferred during design ("don't know yet") - no `ElementType.Poison`, no
  reaction. `TryConsumeRiftMarkReaction`'s switch defaults to `false` for anything outside the 5
  handled elements, so adding a 6th later is a self-contained addition, not an architecture change.
- **Enemies never deal elemental damage** - every `EnemyDeliveryData` hardcodes `ElementType.Neutral`,
  a pre-existing limitation this rework didn't touch.
- **Brute's Protector Aura Intimidate** bypasses `TryApplyElementalStatus` entirely (calls
  `ApplyIntimidate` directly from the aura component), so it still doesn't participate in the reaction
  scan - a pre-existing gap, not new to Rift Mark.
- **No automated coverage for the Frame-dependent half** - actual `StatusEffects` component mutation,
  reaction dispatch, and multiplayer determinism have no test harness in this project to hook into
  (no Quantum simulation test infrastructure exists anywhere here yet); verify those manually
  in-Editor (apply/reapply to confirm 0→1→2→2 clamp+refresh, two different elements against a 2-stack
  target to confirm 2→1→0 consumption + indicator update, a same-tick multi-hit case to confirm the
  lockout holds).
- **No bespoke `singularityEffectPrefab`/`overflowingRiftPulsePrefab`** authored yet (both fall back
  to the tinted default blast, same "code's ready, needs Editor authoring" gap this project's other
  systems already carry) - `detonationEffectPrefab` *is* already authored (carried over from the
  reaction's earlier Explosion-named prefab).
- **Zara's kit keeps its historical `VoidDamageWavesUpgrade`/`VoidDamageWavesSkillAction` names** -
  same precedent as the original Poison→Void migration keeping its name through a mechanic change;
  only the effect it grants (`RiftMarkEffect`, formerly `VoidEffect`) changed.
- **Weapon Perks and Rift Mutations that apply/consume Rift Mark** - see `docs/weapon-perks.md`/
  `docs/rift-mutations.md` for the full content pool and their own current-status sections.
