namespace Quantum
{
    using Photon.Deterministic;

    // Global balance tuning for the 6 elemental reactions (see docs/elemental-reactions.md) -
    // parallel to EffectConfig, but deliberately separate from it: every field here is
    // reaction-owned, none of them reused from EffectConfig's own Burn/Stun/Root/etc fields,
    // even where the reaction's effect reuses an existing status (Freeze/Stun apply Stun, Magma
    // Prison applies Root) - those existing fields already have other live consumers
    // (StunEffectData, JuggernautLandingRootSkillAction), so borrowing them would silently couple
    // an unrelated skill's tuning to a reaction's. Referenced via RuntimeConfig.ElementalReactionConfig,
    // read by StatusEffectUtility's TryTrigger* helpers.
    //
    // TriggerCooldown fields gate StatusEffects' matching *CooldownRemaining field (see
    // StatusEffects.qtn) - each reaction's own cooldown, independent of the others, so triggering
    // one never blocks a different one from also firing off the same hit.
    public class ElementalReactionConfig : AssetObject
    {
        // Void + Fire -> Explosion. Damage is a percent of whichever hit's damage triggered it
        // (the landing element's own hitDamage), same convention as Burn's DamagePercent.
        public FP ExplosionTriggerCooldown = 2;
        public FP ExplosionDamagePercent = FP._0_50;
        public FP ExplosionRadius = 3;

        // Void + Ice -> Freeze. Stretches the target's AttackPhase.Preparation/Telegraph windup
        // (StatusEffectUtility.ApplyAnticipationSlow) rather than locking it down outright - see
        // docs/elemental-reactions.md. Multiplier < 1 makes the windup take longer (e.g. 0.5 = twice
        // as long), same convention as EffectConfig.SlowSpeedMultiplier/TimeDilationMultiplier.
        public FP FreezeTriggerCooldown = 4;
        public FP FreezeDuration = 3;
        public FP FreezeAnticipationMultiplier = FP._0_50;

        // Void + Rock -> Knockback. Reuses EffectConfig's own KnockbackTier bucket rather than a
        // dedicated force field - that bucket is already the project-wide "how hard does anything
        // push" convention (see EffectConfig.GetKnockback), so this is the one exception to "never
        // reuse an EffectConfig field": it's explicitly designed to be shared by every pusher in the
        // game, not a single-purpose knob some other system already owns.
        public FP KnockbackTriggerCooldown = 2;

        // Fire + Rock -> Magma Prison. Applies Root (StatusEffectUtility.ApplyRoot) on top of
        // whichever Burn is already active - own dedicated duration, NOT EffectConfig.RootDuration
        // (that field is Juggernaut's landing-root skill's own knob).
        public FP MagmaPrisonTriggerCooldown = 3;
        public FP MagmaPrisonRootDuration = FP._1;

        // Ice + Fire -> Stun. Own dedicated duration, NOT EffectConfig.StunDuration (that field
        // backs the freely-authorable StunEffectData used elsewhere).
        public FP StunTriggerCooldown = 4;
        public FP StunEffectDuration = FP._0_50;

        // Ice + Rock -> Break. Increased incoming damage (StatusEffectUtility.ApplyBreak) on top of
        // whichever Slow/Intimidate is already active - own dedicated duration/multiplier.
        public FP BreakTriggerCooldown = 3;
        public FP BreakDuration = 3;
        public FP BreakDamageTakenMultiplier = FP.FromString("1.3");
    }
}
