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
`ShieldUtility.ApplyOvershield`).

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
every hero/weapon-perk is unaffected); only `BurnEffectData`/`SlowEffectData`/`StunEffectData`/
`RiftMarkEffectData` override it, each interpreting duration/magnitude in its own terms (Stun ignores
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
