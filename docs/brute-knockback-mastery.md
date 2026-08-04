# Brute — Knockback Mastery Hero Traits

A 4-trait Hero Trait pool for Brute (Ground Pound / Crushing Blow / Lasting Impact / Overwhelming
Force), mirroring Max's Fire Mastery and Pixie's Demolition Mastery pools. Unlike those two, none of
Brute's existing kit (Juggernaut charge/discharge, Protector Aura/Intimidate, Iron Shoulder dash)
already involved a "Brute jumps and lands" mechanic - `JuggernautLandingImpactSystem` watches an
*enemy* Brute launched, not Brute himself. Confirmed with the user: rather than build a brand new
leap ability, Ground Pound reuses the existing, hero-agnostic `AutoJumpSystem` (auto-hop/mantle over
ledges, plus a manual jump button) - its "just landed" edge previously did nothing gameplay-wise.

## The 4 traits

- **Ground Pound** (`GroundPoundUpgrade`) - landing from at least `MinFallDistance` of net drop (not
  any landing - a flat auto-hop/mantle or a ground-level manual jump doesn't count) pulses a pure
  knockback burst (no damage, radial push), sized by a `KnockbackTier` (Small/Medium/Strong) picked
  on the asset rather than an authored raw force number, and plays a VFX authored directly on the
  `GroundPoundPassiveUpgradeData` asset itself (`BlastEffectPrefab`, via a `.View.cs` partial).
- **Crushing Blow** (`CrushingBlowUpgrade`) - bonus damage vs a currently-Stunned target, read live
  in `DamageUtility.ResolveOutgoingDamage` via `StatusEffectUtility.IsStunned` - same shape as Max's
  Hot Target / Pixie's Unstable Targeting.
- **Lasting Impact** (`LastingImpactUpgrade`) - a Stun *Brute himself* causes lasts longer, read live
  inside `StatusEffectUtility.ApplyStun`.
- **Overwhelming Force** - increases outgoing knockback force. No dedicated component at all -
  `CharacterStats.KnockbackMultiplier` is already a live-read bakeable stat (`DamageUtility.
  ResolveKnockbackScale`), so the upgrade mutates it directly, same one-line shape
  `BiggerBoomPassiveUpgradeData` uses for `MarkExplosiveDeath.BonusRadiusMultiplier`.

## Architecture

- **Ground Pound** needed a new generic signal, `OnPlayerLanded(EntityRef entity, FP fallDistance)`
  (`PlayerMovement.qtn`), fired from `AutoJumpSystem`'s existing `wasGrounded == false && isGrounded
  == true` branch - one additive line, zero behavior change for every other hero. `fallDistance` is
  the drop in `LastGroundedPosition.Y` between takeoff and landing, computed from a local captured at
  the very top of `Update` (before `LastGroundedPosition` gets overwritten for the current tick) -
  `AutoJumpSystem` just reports the raw distance and stays hero-agnostic; it's
  `BruteKnockbackMasterySystem` that decides what counts as "a real fall" by comparing against
  `GroundPoundUpgrade.MinFallDistance` (default 2, tunable per the asset). Consumed by
  `BruteKnockbackMasterySystem` (mirrors `PixieDemolitionMasterySystem`'s shape), gated on
  `GroundPoundUpgrade`'s presence.
  - The knockback itself is applied via a small inline loop (`Physics3D.OverlapShape` +
    `DamageUtility.ApplyKnockback` per enemy caught) rather than the shared
    `HitEffectUtility.ApplyShockwave` helper - that helper always fires the generic
    `ShockwaveReleased` event alongside it (whose own `Effect` field means "skip, a dedicated view
    already handles this," not a prefab to resolve), which would double up the VFX now that Ground
    Pound needs its own asset-authored prefab. Instead it bakes `GroundPoundUpgrade.Source` as a
    self-reference (`upgrade->Source = this;`, same pattern `VortexExplodeOnDestroy.Source` already
    uses) and fires a dedicated `GroundPoundTriggered` event carrying it, so `EffectsManager`
    (`OnGroundPoundTriggered`) can resolve `GroundPoundPassiveUpgradeData.BlastEffectPrefab` off the
    exact asset that granted the trait - authored via a `.View.cs` partial, same split-file
    convention `AreaHitData.View.cs`/`VortexExplodeOnDestroySkillAction.View.cs` use. Falls back to
    `EffectsManager`'s `defaultAreaBlastEffect` if left unset.
  - `GroundPoundUpgrade.Force`/`UpwardForce` aren't authored directly either - the asset picks a
    `KnockbackTier` (reusing the same `Small`/`Medium`/`Strong` enum every `KnockbackEffectData` in
    the game already uses), resolved once through the shared `RuntimeConfig.EffectConfig.GetKnockback`
    at `Apply` time and baked into the component as plain `FP`. `KnockbackTier` is a plain C# enum
    (declared in `EffectConfig.cs`, never through qtn's own `enum` syntax), so it can't live on an
    unmanaged qtn component directly - baking the resolved numbers instead of the enum itself sidesteps
    that. A re-pick takes the stronger tier's numbers (`FPMath.Max` on both fields) rather than adding,
    since a tier is a discrete bucket, not a stackable delta.
- **Lasting Impact** required `StatusEffectUtility.ApplyStun` to learn who's applying the Stun - it
  previously took no `owner` parameter at all, even though every one of its ~4 call sites already
  had an owner locally available:
  - `IronShoulderSkillAction.cs` - `TryStunIfPushedIntoWall` gained an `owner` param, passed
    `filter.Entity` from its one call site.
  - `JuggernautLandingImpactSystem.cs` - passes the `owner` local already captured earlier in the
    same method.
  - `StunEffectData.cs` - passes `context.Owner`.
  - `StatusEffectUtility.TryTriggerOverload` - gained its own `owner` param (mirroring
    `TryTriggerRupture`'s existing signature one branch above it), passed through from its own
    caller.
  `ApplyStun` reads `LastingImpactUpgrade` off `owner` (if valid) before the target's own tier
  resistance multiply - own-side bonus first, then target-side resistance, same order
  `ResolveKnockbackScale` already uses for its own owner/target split.
- **Crushing Blow** lives right alongside Pixie's Unstable Targeting in `DamageUtility.
  ResolveOutgoingDamage` - both are "bonus damage vs a target's own live status," same shape.

## Current status

- Code compiles once Quantum's DSL codegen picks up `KnockbackMastery.qtn`/`PlayerMovement.qtn`'s new
  signal.
- `Tools/RiftRaiders/Brute/Generate Knockback Mastery Assets` (also chained into `Generate All
  Assets`) authors all 4 `.asset` instances and appends them to `BruteCharacterData.PassiveUpgrades` -
  **append-only**, unlike `BruteProtectorAssetGenerator`'s own `WireCharacterData`, which fully
  replaces that list; running both leaves all 8 entries (4 Protector Aura + 4 Knockback Mastery)
  intact regardless of order.
