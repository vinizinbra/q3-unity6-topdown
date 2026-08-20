namespace Quantum
{
    using Photon.Deterministic;

    // Generic "grant this ally a bundle of short timed buffs" hit effect - one authored asset instead
    // of stacking four near-identical single-stat effects into a 4-slot Effects array. Every field is
    // opt-in: a 0 simply skips that half, so the same class covers a plain Fire-Rate pulse, a
    // Move-Speed pulse, or all of them at once.
    //
    // Consumers so far: Zara's Support Beat (Move Speed + Fire Rate, plus Power Chord's outgoing
    // damage at rank 3), her Portable Speaker's reduced-effect variant, and Lux's Fire Support Sentry
    // aura (Fire Rate + Damage Reduction). Nothing here is hero-specific.
    //
    // Every underlying primitive already handles multi-source stacking on its own terms - Haste keeps
    // one slot per source (so two Zaras don't overwrite each other), Move Speed/outgoing damage/aura
    // DR all use refresh-or-take-the-stronger - which is what satisfies the spec's "buffs refresh
    // duration rather than stack infinitely."
    public unsafe class AllyBuffEffectData : HitEffectData
    {
        public FP Duration = 2;

        // Fractional bonuses (0.15 = +15%). Each is skipped entirely when 0.
        public FP MoveSpeedBonus = FP._0;
        public FP FireRateBonus = FP._0;
        public FP OutgoingDamageBonus = FP._0;

        // 0-1 incoming-damage reduction, routed to the shared continuous-aura slot (see
        // StatusEffects.AuraDamageReductionRemaining) - so two aura sources never stack additively,
        // the strongest simply wins.
        public FP DamageReductionAmount = FP._0;

        // Flat Shield restored per application (NOT a percent - a percentage-of-max restore from a
        // fast-pulsing source is exactly the unbounded-sustain shape the spec calls out). 0 = none.
        public FP FlatShieldRestore = FP._0;

        public override void Apply(Frame f, ref HitEffectContext context)
        {
            if (context.Target == EntityRef.None)
                return;

            if (MoveSpeedBonus > FP._0)
            {
                StatusEffectUtility.ApplyTempMoveSpeed(f, context.Target, Duration, FP._1 + MoveSpeedBonus);
            }

            if (FireRateBonus > FP._0)
            {
                // context.Owner is the Haste source, not the pulsing entity - so two Totems owned by
                // the same Zara share one slot (they're the same source and shouldn't compound),
                // while two different Zaras each hold their own.
                StatusEffectUtility.ApplyHaste(f, context.Target, context.Owner, Duration, FP._1 + FireRateBonus);
            }

            if (OutgoingDamageBonus > FP._0)
            {
                StatusEffectUtility.ApplyTempOutgoingDamage(f, context.Target, Duration, OutgoingDamageBonus);
            }

            if (DamageReductionAmount > FP._0)
            {
                StatusEffectUtility.ApplyAuraDamageReduction(f, context.Target, Duration, DamageReductionAmount);
            }

            if (FlatShieldRestore > FP._0 && f.Unsafe.TryGetPointer<Shield>(context.Target, out var shield) == true)
            {
                ShieldUtility.ApplyFlatShield(f, context.Target, context.Owner, shield, FlatShieldRestore);
            }
        }
    }
}
