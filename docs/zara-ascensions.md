# Zara — Ascensions

Zara's Hero Ascension pool was consolidated (2026-08-11) from a fragmented set of 8 one-off Totem
sub-actions, 4 single-pick Resonance passives, and 4 overlapping Dash picks into exactly 10 three-rank
Ascension lines (Hero Skill/Totem: Amplifier/Healing Chorus/Double Time/Main Stage; Passive/Resonance:
Faster Tempo/Heavy Bass/Restorative Beat/Remix; Dash: Afterbeat/Portable Speaker), reusing the exact
generic ranked-Ascension architecture Pixie/Brute/Max/Kai's own refactors already established
(`IRankedUpgrade`/`MaxRank`/`UpgradeHistoryUtility` - see `docs/level-up-upgrades.md`'s "Ranked
Ascensions" section) - zero Zara-specific rank code anywhere.

Her fantasy: a Combat DJ / support controller whose Totem alternates Damage Beat → Healing Beat →
Damage Beat → Healing Beat, whose Resonance builds from combat into a periodic burst, and whose Dash
carries that rhythm around the battlefield.

## The `CheckActions` bug (same pattern already found and fixed for Brute/Kai)

`ZaraBaseSkill.asset` (the Totem) had `CheckActions: 0` - all 8 of its old embedded sub-actions
(`Activated: 0`) only ever executed once picked via `SkillSlot.Upgrades`, never as baseline, regardless
of the flag. Not a functional bug (every hero's Ascension pool works this way - a picked Ascension
bypasses `CheckActions` entirely), just the same pre-existing dead-baseline shape every other hero's
Hero Skill has, unchanged by this refactor's own 4 new ranked lines.

## Base Totem changes

- **Healing Beat stays percent-based** (10% of target's own MaxHealth), matching every other heal
  already in Zara's kit (Resonance Pulse, Restorative Beat) - not a flat amount. Spec's "10 base
  healing" is illustrative given Zara's own 100 MaxHealth.
- Damage Beat stays flat `DamageAmount = 10` - unchanged.
- Beat Interval baseline stays `1.0s` - Double Time modifies it per-rank.
- **Fixed a real pre-existing bug found during implementation**: `AlternatingAreaSystem`'s alternation
  (`healNext = CurrentlyHealing == false`) meant a freshly-spawned Totem/Speaker's first pulse was
  always a HEALING beat, not a Damage beat, since `CurrentlyHealing` defaults to `false` and the first
  flip resolves to `true`. Spec explicitly requires "Default sequence begins with Damage Beat." Fixed
  by seeding `alternating->CurrentlyHealing = true` at spawn (both
  `SpawnAlternatingAreaEffectData.Configure` and `PortableSpeakerSkillAction.SpawnSpeaker`), so the
  first flip now resolves to Damage as intended.

## The `AlternatingArea` system - the central reusable mechanism

`AlternatingArea`/`AlternatingAreaSystem` (paired with generic `AreaDamage`/`AreaDamageSystem`,
`AlternatingAreaSystem` running immediately before `AreaDamageSystem`) already implemented exactly the
Damage-Beat/Healing-Beat alternation this whole redesign needed - one shared `TickInterval` drives both
cadence and phase-flip, `DamagePulseCount` already only advances on Damage Beats (never Healing Beats),
and the existing `TryApplyStunUpgrade`/live-per-pulse-check idiom (renamed `TryApplyBassDropStun`) was
the exact template for Amplifier's Bass Drop. This refactor extended it, not reinvented it:
`AlternatingArea` gained a `HealAmount` field (mirroring the pre-existing `DamageAmount`, both percent-
of-target for Heal / flat for Damage), and `AlternatingAreaSystem` gained two new public pieces used
only by Main Stage rank 3: `FireBonusPulse` (a pure extra pulse, reusing `AreaDamage.Effects` as
scratch space so a bonus Damage Beat still runs through Bass Drop's own check) and `TryFireClosingBeat`
(mirrors `VortexSystem.TryExplodeOnDestroy`'s "predict destruction one tick early" idiom).

## The 10 Ascension lines

### Totem (4 lines, all `SkillActionData` on `ZaraBaseSkill.Actions`)

**1. Amplifier** - replaces Amplified Damage/Knockback Pulse/Stunning Pulse. R1 +30% Damage Beat
damage (baked once at spawn, `AmplifierUpgrade.DamageBonus`, read by
`SpawnAlternatingAreaEffectData.ResolveDamageAmount`). R2 +60% total, adds knockback (baked into the
first empty `DamageEffects` slot at spawn, `AmplifierKnockback.asset`, Small tier). R3 "Bass Drop"
+100% total, every 3rd Damage Beat Stuns (`AmplifierUpgrade.StunInterval`/`StunEffect`, checked live
every damage pulse by `AlternatingAreaSystem.TryApplyBassDropStun`, off the Totem's own
`AreaOwner->Owner` - safe to check live since the upgrade is Begin-only and never revoked).

**2. Healing Chorus** - replaces Amplified Healing/Haste Pulse. R1 +30% Healing Beat healing (a
behavior-shape change from the old live-per-heal-checked Amplified Healing - now baked once at spawn,
`HealingChorusUpgrade.HealBonus`, required so Portable Speaker's Mobile Stage inheritance can read a
plain numeric field off the owner at its own spawn time). R2 +60% total, allies healed gain Haste ~2s
(new `TimedHasteEffectData`, its own Duration/AttackSpeedMultiplier rather than the shared
`EffectConfig` 5s default). R3 "Encore" +100% total, 50% of excess healing becomes Shield (new
`OverhealToShieldEffectData`, replacing `ScaledHealEffectData` in `HealEffects[0]` entirely at rank 3 -
uses `HealUtility.ApplyFlatHeal`'s new `FP` return value to compute `requested - applied`, then
`ShieldUtility.ApplyFlatShield` - this was `ApplyOvershield` until Overshield was removed game-wide on
2026-08-25, see the section at the end of this doc).

**3. Double Time** - replaces Rapid Pulse. Direct per-rank interval override (1.0 → 0.85 → 0.70 →
0.50s), not a rate multiplier - spec pins exact seconds. `DoubleTimeUpgrade.BeatInterval`, baked once
at spawn (`SpawnAlternatingAreaEffectData.ResolveTickInterval`). Naturally speeds up everything
beat-driven for free: Amplifier's Bass Drop frequency, Healing Chorus uptime, Main Stage's bonus-beat
relative timing, and half its own interval-shrink is what Portable Speaker's Mobile Stage inherits.

**4. Main Stage** - replaces Bigger Totem, adds opening/closing bonus beats. R1 +30% beat radius
(`MainStageUpgrade.RadiusBonus` - deliberately its OWN field, not the shared cross-hero
`SpawnRadiusUpgrade`, since `SpawnedEntitySpawner.Spawn`'s own `ApplyRadiusUpgrade` step runs for
EVERY spawn an owner makes, Totem AND Portable Speaker alike - granting the shared component would
have silently leaked Main Stage's radius onto every Speaker too). R2 +50% total, +2s duration
(`MainStageUpgrade.DurationBonus`). R3 "Main Stage" +75% total, an immediate opening Damage Beat on
deploy and a final Healing Beat on expiry (`AlternatingAreaSystem.FireBonusPulse`/`TryFireClosingBeat`,
gated by a `MainStageBonusBeats` marker tag stamped on the spawned Totem entity itself at spawn - never
on the owner - which is the load-bearing guarantee that a Portable Speaker, spawned through the exact
same `SpawnedEntitySpawner.Spawn` call, never gets bonus beats regardless of the owner's own Main Stage
rank).

### Resonance (4 lines, all `PassiveUpgradeData` on `ZaraCharacterData.PassiveUpgrades`)

**5. Faster Tempo** - R1/R2/R3 Resonance generation +25%/+50%/+75% (`Resonance.GenerationPerDamage`,
multiplied off a separately-captured `BaseGenerationPerDamage` so re-picking never compounds). R3
"Never Stop" additionally retains 20% of the threshold instead of fully wrapping to 0 after a pulse
(`Resonance.RetainFraction`, read by `ResonanceUtility.AddResonance`'s wrap logic: `Current =
Max(Max * RetainFraction, overflow)`, preserving "don't waste a big overshoot" while guaranteeing the
floor).

**6. Heavy Bass** - R1/R2/R3 Resonance Pulse damage +50%/+75%/+100% and knockback tier
Small/Medium/Strong (switched from the old flat +10 `DamageBonus` to a percent, matching spec's own
wording and Amplifier's shape). R3 "Subwoofer" additionally schedules a second, smaller delayed
shockwave ~0.4s later (new `ZaraSubwooferPulse` component/`ZaraSubwooferPulseSystem`, same countdown
shape as `ZaraAfterbeat`/`ZaraAfterbeatSystem` - reuses the main pulse's own knockback force, 50% of
its damage, does not heal).

**7. Restorative Beat** - R1/R2/R3 Resonance Pulse healing 7.5%/10%/12.5% Max Health
(`Resonance.HealPercent`). R2+ allies healed gain Haste ~2s. R3 converts 50% of excess healing into
Shield (same `requested - applied` / `ShieldUtility.ApplyOvershield` mechanism Healing Chorus rank 3
uses, applied inline in `ResonanceUtility.FirePulse`'s own ally-heal loop rather than via a
`HitEffectData`, since the Pulse's heal was already a direct `HealUtility` call, not an Effects-list
indirection).

**8. Remix** - every 3rd Resonance Pulse (`Resonance.PulseCount % 3 == 0`) applies 1 (R1) or 2 distinct
(R3 "Full Remix") randomly-chosen effects from an editor-configurable pool
(`Resonance.RemixPool`/`RemixPoolEntry`, replacing the old flat `AssetRef<HitEffectData>[5]`) to every
enemy the pulse damages, via deterministic `f.RNG`. R2 strengthens the chosen effect(s) generically -
`HitEffectData` gained a virtual 4-arg `Apply(f, ref context, durationMultiplier, magnitudeMultiplier)`
overload (default forwards to the plain 2-arg `Apply`, so every other `HitEffectData` subclass across
every hero/weapon-perk is unaffected); only `BurnEffectData`/`SlowEffectData`/`StunEffectData`
override it, each interpreting duration/magnitude in its own terms (Stun ignores
magnitude - no separate axis). `ZaraRemixUtility.ApplyRemixEffect` is a thin dispatcher, not a
switch-on-type reimplementation. Rank 3's second pick is guaranteed distinct via a simple "skip the
first index" second draw (`ResonanceUtility.ResolveRemixEntries`) - the minimal correct primitive for
picking exactly 2 of N. A new `RemixPulseTriggered` event carries the selected effect(s) to the view
(`ResonanceFxView.OnRemixPulseTriggered`, a small optional cue prefab, tinted per effect) - simulation
picks, client only displays.

### Dash (2 lines, all `SkillActionData` on `ZaraCharacterData.DashSkillUpgrades`)

**9. Afterbeat** - absorbs Quick Tempo (R1: dashing grants 20% of Resonance.Max, not a flat amount -
`ResonanceUtility.GrantPercent`, `AfterbeatSkillAction.ResonancePercentOnDash`). R2 adds a delayed
(~1s) damaging/knocking pulse at the dash's own starting
position, scaling off a newly-reactivated "Hero Skill Damage" basis (`ZaraBaseSkill.Damage`, previously
dead - see `ZaraAscensionUtility.ResolveHeroSkillDamage`, mirroring `KaiAscensionUtility.
ResolveVortexSkillDamage`). R3 "Double Beat" adds an identical second pulse at the dash's ENDING
position too (`ZaraAfterbeat` gained two full slots, `Start*`/`End*`, rather than one shared one), and
enemies hit generate additional Resonance, capped per dash
(`ResonancePerEnemyHit`/`MaxResonancePerDash`/`ResonanceGrantedThisDash`, reset every dash). Afterbeat's
own damage passes `generatesResonance: false` to `DamageUtility.ApplyDamage` - confirmed with the user
that rank 3's capped bonus is the ONLY Resonance Afterbeat ever grants, not an addition on top of the
generic per-damage hook.

**10. Portable Speaker** - absorbs Healing Step (R2's dash-end heal). New `PortableSpeakerSkillAction`
(replaces the old, broken `PortableSpeaker.asset` `SpawnEntitySkillAction` instance) spawns
`ZaraSpeaker.prefab` (the Totem's OWN real placed entity, reused directly rather than a dedicated
prefab - see "Corrections" below) and hand-configures its `AlternatingArea`/`AreaDamage` directly (same
"spawn, then configure" shape `SpawnAlternatingAreaEffectData`/Kai's `WarpWakeSkillAction` already use)
at 50% of the Totem's own baseline Damage/Heal values, using the SAME alternating rhythm. R2 grows
duration/radius, dash-end also heals nearby allies 5% Max Health. R3 "Mobile Stage" inherits 50% of
Amplifier's `DamageBonus`, Healing Chorus's `HealBonus`, and Double Time's own interval-shrink from
whichever Totem Ascensions the owner also holds (read directly off the owner's
`AmplifierUpgrade`/`HealingChorusUpgrade`/`DoubleTimeUpgrade` components at Speaker-spawn-time) -
deliberately does NOT inherit Amplifier's knockback/Bass-Drop-stun or Main Stage's radius/duration, and
never adds `MainStageBonusBeats` to anything it spawns, so a Speaker can never fire opening/closing
bonus beats regardless of the owner's own Main Stage rank.

## The self-feeding Resonance loop (found and fixed as part of this refactor)

`ResonanceUtility.FirePulse`'s own enemy-damage call re-entered `DamageUtility.ApplyDamage`'s shared
funnel, which unconditionally called `ResonanceUtility.OnDamageDealt` for the same Zara mid-pulse - a
real, previously-unmitigated self-feeding loop. Fixed generically: `DamageUtility.ApplyDamage` gained
one new optional `bool generatesResonance = true` parameter (default preserves every existing call
site), gating the `ResonanceUtility.OnDamageDealt` call. Three call sites pass `false`: the Resonance
Pulse's own enemy-damage call (the fix), Heavy Bass rank 3 Subwoofer's second shockwave, and Afterbeat's
own delayed-pulse damage (both Start and End) - every other damage source in the game (weapon fire,
Totem/Speaker Damage Beats, any other hero) is completely unaffected.

## Removed / merged

**Removed entirely** (not repurposed): Void Pulse (`VoidDamageWavesSkillAction`/`VoidDamageWavesUpgrade`
+ qtn) - confirmed removed from Zara's normal Ascension tree per spec; Rift Mark may still exist as one
possible Remix result, but is no longer an unconditional Totem Damage Beat side-effect.

**Deleted, folded into a new line**: `IncreaseDamageSkillAction`/`KnockbackOnDamageSkillAction`/
`StunEveryWavesSkillAction` → Amplifier. `IncreaseHealSkillAction`/`HasteOnHealSkillAction` → Healing
Chorus. `IncreaseWavesTickRateSkillAction` → Double Time. Zara's own `SpawnRadiusUpSkillAction`
instance ("Bigger Totem") → Main Stage (the generic `SpawnRadiusUpSkillAction` class itself stays,
shared by other heroes). `QuickTempoSkillAction` → Afterbeat rank 1. `HealingStepSkillAction` →
Portable Speaker rank 2. The old `PortableSpeaker.asset` (`SpawnEntitySkillAction` instance) → replaced
by the new `PortableSpeakerSkillAction` class.

**Kept, made ranked in place** (not deleted, just rewritten): `FasterTempoPassiveUpgradeData`,
`HeavyBassPassiveUpgradeData`, `RestorativeBeatPassiveUpgradeData`, `RemixPassiveUpgradeData`.

**Corrections made after initial implementation** (found via live in-Editor testing):
- `ZaraThrowProjectileSpeaker.MaxDistance` (on `ZaraBaseSkill.asset`) had somehow ended up set to `5.0`
  instead of its correct `0` ("unlimited," per `ThrownProjectileMovementData`'s own design - a lobbed
  throw has no distance ceiling, only Speed/LaunchVelocityY/Gravity govern where it lands). At 5.0 the
  Totem's thrown projectile would hit `ProjectileSystem.TryExpire`'s distance cap mid-arc, before its
  own ground raycast ever fired, causing `DirectHitData.ApplyExpire` to plant the Totem wherever the
  projectile happened to be airborne instead of on the ground - "the throwable stays in air." Reverted
  to `0`.
- **`ZaraDeviceSpeaker.prefab` was misidentified.** It is NOT a placed pulsing-area prototype at all -
  it's the THROWN PROJECTILE visual (`ZaraThrowProjectileSpeaker.Prototype`, the object that flies from
  Zara to the Totem's landing spot), confirmed by the user. The original assumption ("the only
  unaccounted-for Zara entity prototype, so it must be Portable Speaker's spawn target") was wrong. The
  `QPrototypeAreaDamage`/cleared-effects edits made to it were reverted (by the user); its original
  `QPrototypeProjectile` component - never actually stray - is what lets it fly at all. **Portable
  Speaker's prototype is now `ZaraSpeaker.prefab`** (the Totem's own real placed entity, which already
  has the correct `AreaDamage`+`AlternatingArea` setup) - reused directly rather than authoring a
  dedicated prefab, same "Dash mini-version reuses the Hero Skill's own entity" precedent Kai's Warp
  Wake already established for its Dash Void (a cosmetic follow-up - Portable Speaker currently looks
  identical to the full Totem, just smaller via its own collider scale - not a functional gap).

## Current status

The code compiles once Quantum codegen picks up every changed/new `.qtn` file (`AlternatingArea.qtn`,
`Resonance.qtn`, `ZaraAfterbeat.qtn`, new `Amplifier.qtn`/`HealingChorus.qtn`/`DoubleTime.qtn`/
`MainStage.qtn`/`ZaraSubwooferPulse.qtn`, `Events.qtn`'s new `RemixPulseTriggered`, and the 7 deleted
old single-field upgrade `.qtn` files), and `ZaraSubwooferPulseSystem` is registered in
`SystemSetup.User.cs` alongside `ZaraAfterbeatSystem`. `Tools > RiftRaiders > Zara > Generate Ascension
Assets` (replaces the old `ZaraResonanceAssetGenerator`) authors and wires all 10 lines, fully replacing
every list it touches (fixing the old generator's append-only-dedupe bug for `DashSkillUpgrades`) - not
yet run. Every numeric value not explicitly pinned by the spec is a decisive placeholder pending a real
balance pass (Amplifier's knockback tier, Subwoofer's delay/radius/damage split, both Overshield cap
multipliers, Remix's rank-2 multipliers, Afterbeat's knockback force/Resonance-per-hit/per-dash cap,
Portable Speaker's dash-end-heal radius). The Totem's own throw is confirmed working in-Editor after
the `MaxDistance`/`ZaraDeviceSpeaker.prefab` corrections above (re-run the generator so
`PortableSpeakerSkillAction.asset.Prototype` picks up `ZaraSpeaker.prefab`). Still to playtest: Bass
Drop's every-3rd-beat stun, Main Stage's opening/closing bonus beats (and that a Portable Speaker never
gets them), the Resonance self-feed fix, Remix's guaranteed-2-distinct rank 3, Portable Speaker itself
end-to-end, and Portable Speaker's Mobile Stage inheritance.

---

# 2026-08-20 balance pass — Combat DJ / Tempo Support

Zara drops from 10 lines to the target **9 lines × 3 ranks**, and her identity is retargeted: **support
first, healer second.** Every line that used to buy more healing now buys tempo, mitigation or control
instead. Healing is deliberately capped from three directions.

## Roster now

| Pool | Lines |
|---|---|
| Totem (Hero Skill) | Amplifier, **Sound Boost**, Double Time, Main Stage |
| Resonance (Passive) | Faster Tempo, **Protective Rhythm**, Remix |
| Dash | Afterbeat, Portable Speaker |

Removed: **Heavy Bass** (standalone line cut — Amplifier is her offensive path; the Subwoofer
component/system and `Resonance.Subwoofer*` are deleted). **Healing Chorus** → reworked into Sound
Boost. **Restorative Beat** → replaced by Protective Rhythm.

## Base Totem: "Healing Beat" is now a Support Beat

The alternation itself is unchanged (`AlternatingArea`, Damage → Support → Damage → Support, never
both at once). What a Support Beat *does* changed:

- Heals **1%** Max HP (was 10%) — a trickle, not a heal.
- Grants **+10% Move Speed and +10% Fire Rate** for ~2s, via the new generic `AllyBuffEffectData`.
- `SpawnAlternatingAreaEffectData.HealEffects` now has a **slot contract** Sound Boost writes into:
  `[0]` the heal, `[1]` the ally buff bundle, `[2]` reserved for Sound Boost R2+'s cooldown reduction.
  Slot-indexed rather than "first empty slot", so a rank swap replaces the buff rather than leaving two
  competing ones on the same beat.

### Global Totem healing cap

`SpawnAlternatingAreaEffectData.MaxHealFractionPerAlly` (20% of Max HP) is applied to **every Totem at
every Sound Boost rank**, not just the top rank — which is what stops Double Time (more beats) from
letting a lower rank out-heal a higher one. It's enforced by the new generic
`AreaAllyBudget`/`AreaAllyBudgetUtility`, living on the **spawned Totem entity**: a fresh deploy is a
fresh allowance for everyone, and two Zaras' Totems never share one. Once spent, Support Beats still
deliver Move Speed / Fire Rate / cooldown reduction — only the HP half switches off.

## The lines

- **Amplifier** — unchanged (+30% / +60% + knockback / +100% + Bass Drop stun every 3rd Damage Beat).
  Bass Drop's stuns now automatically respect the generic per-tier CC immunity window.
- **Sound Boost** (replaces Healing Chorus) — R1: heal 2% Max HP, +15% Move Speed / +15% Fire Rate.
  R2: every Support Beat also reduces affected allies' **remaining Hero Skill cooldown** by 0.5s.
  R3 "Power Chord": heal 5% Max HP and +15% **outgoing damage** for 2s.
  - The cooldown reduction is the new generic `ModifyRemainingCooldownEffectData` — remaining-cooldown
    only, clamped at 0, never banking. Capped per Totem per ally by
    `SoundBoostUpgrade.MaxCooldownReductionPerTotem` (exposed as the brief requires; shipped at a
    generous **6s**, expected tuning range 3-4s). The budget is charged only for reduction that
    actually landed, so an already-ready skill never eats the allowance.
  - Each rank's buff profile is ONE authored `AllyBuffEffectData` asset, not a pile of numbers — the
    same generic effect Lux's Fire Support aura uses.
- **Double Time** — unchanged (1.0 → 0.85 → 0.70 → 0.50s). Its synergy with Sound Boost is intentional
  and balanced through `MaxCooldownReductionPerTotem`, not special-cased.
- **Main Stage** — unchanged (+30/50/75% radius, +2s duration at R2, R3's opening Damage Beat and
  closing Support Beat).
- **Faster Tempo** — unchanged (+25/50/75% generation, R3 retains 20% of the threshold).
- **Protective Rhythm** (replaces Restorative Beat) — **fully superseded 2026-08-25.** It granted
  Shield (10/15/20% of the ally's own Max Shield) plus DR at R2+. It now HEALS instead — 3/4/5% Max HP,
  DR 10/20% at R2+, and a Resonance feedback loop at R3 — because Shield stopped being something every
  hero can receive. See "Protective Rhythm reworked off Shield onto healing" at the end of this doc.
  **It never touches HP healing.** The DR routes through the shared reactive-DR slot
  (`ApplyTemporaryDamageReduction`, take-the-stronger), so a co-op stack with Brute's Guardian/Bodyguard
  resolves through the generic policy instead of adding up.
- **Remix** — R2 additionally starts the next Resonance cycle at 20% (`Resonance.RemixRetainFraction`).
  **Explicitly resolved against Faster Tempo R3:** the two are **take-the-maximum**, never additive —
  `AddResonance` applies Faster Tempo's floor, then `FirePulse` raises it to Remix's if higher. There is
  exactly one floor, never two stacked. Status pool, weights and deterministic `f.RNG` selection are
  unchanged; the View is still told the result via `RemixPulseTriggered` and never rolls anything.
- **Afterbeat** — R1 "Quick Tempo" is now a **flat 20 Resonance** (was 20% of Max) *plus* 10 per enemy
  the dash physically passes through (a new OnGoing sweep, deduped per enemy per dash via
  `ZaraAfterbeat.SweptEnemies`). R2 is the delayed pulse at the dash start; R3 adds the end pulse and
  its own per-enemy Resonance. R1's sweep and R3's pulse hits draw on **one shared per-dash allowance**
  (`MaxResonancePerDash`, 40), so they can't compound past the cap.
- **Portable Speaker** — reworked around three rules the brief pins down:
  - ~~**Never heals HP, at any rank**~~ — **reversed 2026-08-25**, see "Portable Speaker now heals" at
    the end of this doc. It was enforced by construction (no heal effect authored, `HealAmount` 0, no
    `AreaAllyBudget`); it now heals at half the Totem's live value and carries its own budget.
  - **Capped active count per Zara** (`MaxActiveSpeakers`: 1 / 1 / 2). A new one past the cap silently
    retires her **oldest** (smallest `DestroyAfterTime.RemainingTime`) via
    `DespawnIntentUtility.DespawnSilently(Replaced)`, so no on-destroy effect misreads housekeeping as
    a death. Scoped by `AreaOwner.Owner`, so two Zaras never share a cap. New `PortableSpeaker` marker.
  - **R2's dash-end effect is a BUFF, not a heal** — the same generic `AllyBuffEffectData` asset.
  - **R3 "Mobile Stage" inheritance is simplified.** It inherits Double Time's interval shrink and Main
    Stage's radius at `MobileStageInheritanceFraction`, Amplifier's damage bonus, and Sound Boost via
    **its own authored reduced-effect Speaker-variant assets** (`SpeakerSupportBuffEffect`/
    `SpeakerCooldownEffect`) rather than a runtime multiplier — "a different data profile, not complex
    hero-specific inheritance code". It does NOT inherit HP healing, the per-Totem healing cap, Main
    Stage's duration bonus, Amplifier's knockback/Bass-Drop stun, or Main Stage's opening/closing bonus
    beats (`MainStageBonusBeats` is never stamped on a Speaker).

## Base Resonance retuning

`Resonance.Max` stays 500 and `HealPercent` drops to **2%** (the emergency heal). The doc-level guidance
is now explicit on the asset: tune `Max` against `GenerationPerDamage` and Zara's real DPS toward
roughly **one Pulse every 10-12s** in active combat, rather than treating 500 as a meaningful number in
isolation.

**Playtest first:** Sound Boost + Double Time cooldown reduction (the single most build-defining thing
she gives a team — `MaxCooldownReductionPerTotem` is the knob); Resonance pulse cadence; whether the
20% Totem healing cap is reached in a normal Totem lifetime; two Speakers overlapping at rank 3; two
Zaras in the same match.

---

# 2026-08-20 (later) — Portable Speaker never buffed Zara herself

Found while fixing the identical defect in Brute's Bodyguard (full writeup in
`docs/brute-ascensions.md`, "Bodyguard never shielded Brute himself"). Not reported from testing — found
by inspection, because it is the same code shape for the same reason.

Portable Speaker rank 2+ grants a short ally buff on dash completion, scanning with
`EnemyMovementUtility.FindPlayersInRadius`. That helper's `Player`-only layer mask deliberately cannot
see a **dashing** player (`DashSkillData` parks the dasher on `IgnoreProjectile` for the dash's duration,
which is what gives Dash its i-frames). Since this fires at dash **End**, it coincides with that swap by
definition, and `Core.PhysicsSystem3D` runs before every user system — so the broadphase the query reads
was already built with Zara still on `IgnoreProjectile`. She buffed every nearby ally except the one who
earned it, every single time.

Fixed by switching to `EnemyMovementUtility.FindPlayersInRadiusIncludingDashing` (renamed from
`FindPlayersInRadiusForPickup`, which already existed for exactly this problem). No behavior change for
anything else.

The Speaker's own spawned beats are unaffected — they pulse from a placed entity over time, not at the
instant of the dash.

---

# 2026-08-25 — Overshield removed; Protective Rhythm grants plain Shield

Consequence of the game-wide Shield rework (full writeup in `docs/accessory-guard.md`'s
"Shield reworked into the Accessory's protective layer" section): player Shield no longer
auto-recharges, and **Overshield is deleted outright** — `ShieldUtility.ApplyOvershield` and every
`OvershieldCapMultiplier` are gone, so all grants cap at the target's own Max.

Two of Zara's lines touched it:

- **Protective Rhythm** — `Resonance.OvershieldPercentOfMaxShield` → **`ShieldPercentOfMaxShield`**, and
  `OvershieldCapMultiplier` dropped from the component, `ProtectiveRhythmPassiveUpgradeData` and
  `ResonancePassiveData`'s seeding. The per-rank values are unchanged (10% / 15% / 20% of the ally's
  own Max Shield), as is the rank 2+ damage reduction.
- **Healing Chorus rank 3 "Encore"** (`OverhealToShieldEffectData`) — the overheal conversion now calls
  `ApplyFlatShield`; `OvershieldCapMultiplier` removed. `ShieldConversionPercent` is unchanged.

**Her support value went UP, not down.** Both of these used to top up a bar that would have refilled
itself in five seconds. Now Shield is charge-only and is what keeps an ally's Accessory from being
knocked off — and Kai, Pixie and Max have no Shield source of their own at all — so Zara is one of the
few standing team Shield sources in the game, and Protective Rhythm quietly protects her team's *gear*
as much as their health. Worth a look during the next balance pass: the line may now be undertuned in
the other direction.

Nothing else in her kit changed. Sound Boost and Portable Speaker never granted Shield (their
`AllyBuffEffectData.FlatShieldRestore` is authored 0 everywhere), and `ZaraAscensionAssetGenerator` was
updated for the renamed field plus the rank text, which no longer says "Overshield" — it still has not
been re-run.

---

# 2026-08-25 (later) — Protective Rhythm reworked off Shield onto healing

Shield turned out to be the wrong currency for this line the moment it became charge-only. **Max and
Pixie both author `BaseMaxShield: 0`**, so `ApplyFlatShield` capped at their own Max granted them
literally nothing — rank 1 was dead weight against a third of the roster, and there was no way for
them to opt in. Healing is the only defensive currency every hero can actually receive.

| Rank | Effect |
|---|---|
| R1 | Resonance Pulse heals nearby allies for **3% Max HP** |
| R2 | Heal **4% Max HP**, allies gain **10% DR for 2s** |
| R3 "Fortissimo" | Heal **5% Max HP**, DR **20% for 2s**, and damage allies take while protected **feeds Resonance back to Zara** |

This does reverse the line's original "deliberately never more HP healing" stance — that existed to
stop Zara becoming a sustain engine, and Shield was how she got a defensive payload without one. With
Shield gone as a universal target, the honest options were "heal" or "nothing", and the numbers are
sized accordingly: 3-5% of Max HP on a pulse that fires roughly every 10-12s is a trickle, not
sustain. Her emergency-heal baseline is unchanged at 2%.

**The heal OVERWRITES rather than stacks.** `Apply` writes `Resonance.HealPercent` directly, replacing
the 2% baseline with 3/4/5% — the same "one value, owned by whoever writes it" shape Faster Tempo uses
for `GenerationPerDamage`. There is exactly one heal number in the pulse, so it can never double-apply.

## Fortissimo's feedback loop

Rank 3 is the only genuinely new mechanism, and it closes a real loop: her defensive investment is
repaid by the team being under pressure, so she pulses more often exactly when a party needs it most.
A Zara protecting nobody — or protecting a party nothing is hitting — gains nothing from the rank.

It needed its own marker, `StatusEffects.ProtectiveRhythmRemaining` + `ProtectiveRhythmSource`
(`StatusEffectUtility.ApplyProtectiveRhythm`/`TryGetProtectiveRhythmSource`), stamped by `FirePulse`
alongside the DR and for the same duration. It deliberately does **not** key off the DR slot that
window also writes: `TemporaryDamageReduction` is shared with Brute's Guardian rank 3 and Bodyguard
rank 3, so reading it would pay Zara Resonance for damage taken under a *Brute's* protection. Recording
the source is also what lets two Zaras in one match each be paid only for their own window.

`ZaraProtectiveRhythmSystem` (registered beside `ZaraAfterbeatSystem`) reacts to
`OnHealthDamageApplied`/`OnShieldDamageApplied`. Three deliberate details:

- **Shield damage counts too.** Rarer now that player Shield is charge-only, but a hit soaked by Shield
  is still a hit taken under her protection — excluding it would quietly make her worse at protecting
  whoever is best equipped to survive.
- **The attacker must be a live `Enemy`**, so self-inflicted and environmental damage can't be farmed.
- **Routed through `ResonanceUtility.Grant`, not `OnDamageDealt`** — this is not Zara dealing damage, so
  it must not be gated by the `generatesResonance` flag that exists to stop her own Resonance-sourced
  effects (the Pulse, Subwoofer, Afterbeat) regenerating Resonance from themselves. No re-entrancy
  hazard: a pulse triggered by this only ever damages enemies, and an enemy never carries the marker.

`ProtectedResonancePerDamage` (1.0 per point at rank 3) is a placeholder pending a real balance pass —
weigh it against `Resonance.Max` (500) so it accelerates her pulses without replacing her own damage as
the primary way she charges.

**Current status:** code-complete pending codegen for `Resonance.qtn`/`StatusEffects.qtn`.
`ZaraAscensionAssetGenerator` is updated with the new fields and rank text but **still has not been
run**, so `ProtectiveRhythm.asset` keeps its old Shield-era serialisation and card text until it is.

---

# 2026-08-25 (later still) — Portable Speaker now heals

A Speaker heals allies for **`HealPercentOfTotem` (50%) of whatever the Totem's Support Beat would
currently restore**, at every rank. The old "never heals HP, by construction" rule is gone.

Same reason Protective Rhythm moved off Shield: healing is now the defensive currency Zara's kit
actually trades in, and a deployable that only buffs reads as strictly worse than one that heals when
half her lines are built around the Support Beat.

## It tracks Sound Boost automatically

`ResolveTotemHealAmount` reads Zara's live `SoundBoostUpgrade.HealPercent` if she holds that line, else
the Totem baseline — then halves it. So a Speaker follows Sound Boost up its ladder (2% → 2% → 5%
becomes 1% → 1% → 2.5%) with **no second per-rank table to keep in sync**. Mirrors
`SpawnAlternatingAreaEffectData.ResolveHealAmount`, which is private to that asset; `TotemBaseHeal` is
mirrored the same deliberate way `TotemBaseDamage` already was, with both authored side by side in
`ZaraAscensionAssetGenerator` to limit drift.

The heal slot reuses the Totem's **own** `ZaraScaledHealPulse.asset` rather than a Speaker copy. That
effect takes its percentage from `AlternatingArea.HealAmount`, which is where the halving happens — so
one asset serves both and there is no parallel heal number that can drift.

## It needed its own budget — this part is load-bearing

A Speaker now carries an `AreaAllyBudget` (`MaxHealFractionPerAlly`, 10% — half the Totem's 20%,
matching the halved heal). This is **not** belt-and-braces: a rank-3 Speaker inherits Double Time's
shorter Beat interval, so without a cap more beats would let a lower Sound Boost rank out-heal a higher
one. That is the exact failure the Totem's own cap was added to prevent, and Mobile Stage reintroduces
it here. Cooldown-reduction allowance still rides on Sound Boost's own per-Totem number rather than
getting a second one.

Mobile Stage does **not** add healing on top — the heal already applies at every rank, so there is
nothing left for rank 3 to inherit. It still never inherits Main Stage's bonus beats or duration, or
Amplifier's knockback/Bass-Drop stun.

## Watch this in playtest

Zara now has **three** healing sources: the Totem's Support Beat, the Resonance Pulse (2% baseline,
3-5% with Protective Rhythm), and now up to two Speakers. Each is individually capped or paced, but
nothing caps them in aggregate, and the design's stated intent is "support first, healer second". If a
Sound Boost + Protective Rhythm + Mobile Stage Zara ends up as a pure healer, the Speaker's
`MaxHealFractionPerAlly` and `HealPercentOfTotem` are the two cheapest knobs to turn down.

**Current status:** code-complete pending codegen. `ZaraAscensionAssetGenerator` authors the new
`HealEffect`/`TotemBaseHeal`/`HealPercentOfTotem`/`MaxHealFractionPerAlly` fields and the updated rank-1
card text, but **still has not been run** — until it is, `PortableSpeakerSkillAction.asset` has no heal
effect assigned and the Speaker keeps healing nothing.

---

# 2026-08-25 (final) — Resonance removed; Flow State is her new passive

Resonance is **gone from the project entirely** - the meter, the automatic Pulse and all of its damage,
healing and knockback; `Resonance.qtn`, `ResonanceUtility`, `ResonancePassiveData`, `ZaraRemixUtility`,
`ZaraProtectiveRhythmSystem`, `ResonanceFxView`, the `ResonancePulseReleased`/`RemixPulseTriggered`
events, the `StatusEffects.ProtectiveRhythm*` marker and `DamageUtility`'s `generatesResonance`
parameter. Her Shield is zeroed too (100 HP / 0 Shield, no dormant recharge config) - **Brute is now the
only hero with a personal Shield mechanic.**

Her new passive is **Flow State**, and it is deliberately **two things only: a fill, and whether it is
on.** Flow belongs to ZARA, never to her Totem.

> It shipped first as a 3-stack ladder with per-stack bonuses, then was simplified the same day. That
> was more machinery than the fantasy needed - "am I in the groove or not" is a binary a player reads
> instantly, while "am I on stack 2 or 3" is bookkeeping. One bar filling toward one payoff says the
> same thing with a third of the state, and every Ascension below reads better against it.

## The rules

| | |
|---|---|
| Fill | `Progress` 0 → 1 over **2.5s** of continuous meaningful movement |
| Active | `IsActive` flips true the moment the bar lands; worth **+15% Move Speed and +15% Fire Rate** |
| Movement | Player **input** (`Input.Direction` past `MovementInputThreshold`) or an active Dash |
| Stationary grace | **1.25s** - the bar is simply held, costs nothing |
| Decay | past grace, the full bar drains over **4.5s**; a single moving tick stops it dead |
| Broken | any hostile hit that **connects** → bar to 0 (a third with Second Wind R2+), Flow off |

**Movement is input-driven, never velocity-driven.** That single choice is what makes knockback,
teleports, physics shoves and environmental displacement unable to build Flow - none of them touch
input, so none of them need their own exclusion check.

## The generic primitive this needed: `OnHostileHitConnected`

The brief's hardest requirement is that a hit **blocked by the Accessory Guard or a Free Hit Guard
still breaks Flow**, while a genuinely dodged hit does not. Listening to HP loss cannot express that.

So `Combat.qtn` gained a new signal, fired from `DamageUtility.ApplyDamage` **above every negation
layer**:

```
Invulnerable        -> return          (dodged/i-framed: never fires)
friendly fire       -> return          (never fires)
>>> OnHostileHitConnected <<<          (fires here)
Free Hit Guard      -> negate, return  (already fired)
Accessory Guard     -> negate, return  (already fired)
...resolution... damage lands          (already fired)
```

Placement *is* the design: it is the authoritative **"was I hit?"**, as opposed to
`OnHealthDamageApplied`/`OnShieldDamageApplied`'s **"did I lose anything?"**. Any future negation
mechanic added beneath that line inherits correct Flow-breaking for free, with no per-mechanic hook -
which is exactly what the brief asked for. It requires a live `Enemy` attacker (so environmental and
self-inflicted damage are not "attacks") and is gated on `bypassOutgoingResolution == false` (so a DoT
tick replaying an already-resolved magnitude is not a second connect).

## The three lines

**Faster Tempo** (kept its name - its ROLE survived, only the resource changed)
R1 builds 25% faster · R2 50% faster and Active worth +18% · R3 "Full Tempo" 75% faster and a further
+10% Fire Rate while Active.

**Second Wind** (replaces Protective Rhythm)
R1 +20% Move Speed for 1.5s when Flow breaks · R2 a hit drops the bar to a third instead of 0 ·
R3 "Keep the Beat" a hit taken while Active deals 30% less damage, 6s cooldown.

**Headliner** (replaces Remix)
R1 +10% outgoing damage while Active · R2 Totem Beats 15% more effective while Active ·
R3 "Headliner" ACTIVATING Flow grants her and allies within 6m +10% Move Speed / Fire Rate for 3s,
8s cooldown.

## Three implementation decisions worth knowing

**Flow's own bonus writes `CharacterStats`, not the timed status slots.** It rebakes Move Speed / Fire
Rate from `BaseMoveSpeedMultiplier`/`BaseAttackSpeedMultiplier` (captured once at seed) on the on/off
TOGGLE only - never as the bar moves - so repeated toggles can never compound and the per-tick cost is
nothing. Had Flow used the shared timed slots
instead, Headliner's own Hype buff - which *does* use them, take-the-stronger - would silently have
stopped stacking on top of it.

**Keep the Beat's DR reaches the hit that triggered it.** Quantum dispatches signals synchronously, so
a reaction to `OnHostileHitConnected` still lands before `ResolveDamageReduction` reads it later in the
same `ApplyDamage` call. It routes through the generic reactive-DR slot rather than a bespoke hook,
which is what keeps it from interfering with Accessory durability or Free Hit Guard logic - both sit
*above* DR and have already had their say.

**Headliner R2 does not put Zara inside a generic system.** `AlternatingArea` gained a generic
`EffectivenessMultiplier` that `AlternatingAreaSystem` applies to both Damage and Support beats; Zara's
own code writes it on the areas she owns, on the activation edge and at spawn. That system serves any
future hero's alternating area and still knows nothing about her.

## Afterbeat, migrated

Identity unchanged (her Dash/movement line), resource swapped. R1 "Quick Tempo" fills **35% of the bar**
on dash plus **10% per unique enemy dashed through** (capped at 40% per dash, deduped). R2 is the delayed
beat at the dash start, unchanged. R3 "Double Beat" adds the end beat, and landing **either** on at least
one enemy fills another **35% - once per dash, never per enemy**.

## Untouched

**Portable Speaker** had no Resonance dependency and is functionally intact (it still heals at half the
Totem's rate, from earlier today). **The Totem** still runs Damage Beat → Support Beat; Flow never
becomes a Totem resource, and the Totem never generates Flow.

## Current status

Code-complete pending codegen (`Flow.qtn`, `ZaraAfterbeat.qtn`, `AlternatingArea.qtn`, `Combat.qtn`,
`Events.qtn`, `StatusEffects.qtn`). `ZaraFlowSystem` is registered in `SystemSetup.User.cs` where
`ZaraProtectiveRhythmSystem` used to sit. Every obsolete asset is deleted (`ResonancePassiveData`,
`ProtectiveRhythm`, `Remix`, plus the long-stale `HeavyBass`/`RestorativeBeat`).

**Not yet done - Editor work:**
1. Run `Tools/RiftRaiders/Zara/Generate Ascension Assets` - it authors `FlowStatePassiveData.asset`,
   `SecondWind.asset` and `Headliner.asset` and rewires `ZaraCharacterData`. **Until it is run, Zara has
   no base passive asset at all** (the old one is deleted), so nothing works.
2. Point `ZaraCharacterData.Passive` at the new `FlowStatePassiveData.asset` if the generator's own
   wiring doesn't (verify after running).
3. Build the HUD's `flowFill` (an Image with **Image Type = Filled**) and optional `activeRoot` on
   Zara's `ZaraHudWidget`.
4. Add `ZaraFlowView` to her view prefab and assign its single `flowParticle`.

Not verified in-Editor.
