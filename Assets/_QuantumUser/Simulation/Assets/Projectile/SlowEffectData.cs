namespace Quantum
{
    using Photon.Deterministic;

    // Ice/Chill - adds buildup toward the stacking slow (see StatusEffectUtility.ApplyIce), same
    // buildup-per-damage formula the Ice weapon-elemental-proc path uses (EffectConfig.
    // IceBuildupPerDamage * context.Damage), so a directly-authored Slow effect and a native Ice
    // weapon hit both feed the exact same Chill identity rather than two different slow systems.
    // Read by PlayerMovementProcessor and EnemySystem's chase movement via
    // StatusEffectUtility.GetSpeedMultiplier.
    public unsafe class SlowEffectData : HitEffectData
    {
        public override void Apply(Frame f, ref HitEffectContext context) => Apply(f, ref context, FP._1, FP._1);

        // Zara's Remix ascension rank 2+ scales duration/magnitude generically through this overload
        // (see HitEffectData.Apply's own comment) - both default to FP._1 from the plain 2-arg Apply
        // above. magnitudeMultiplier now strengthens the CONTRIBUTED BUILDUP directly (more buildup
        // = more slow AND closer to Freeze) rather than diluting a flat SpeedMultiplier the old
        // binary Slow used - a magnitudeMultiplier of 1 reproduces the base per-damage buildup
        // exactly, same shape as before.
        public override void Apply(Frame f, ref HitEffectContext context, FP durationMultiplier, FP magnitudeMultiplier)
        {
            if (context.Target == EntityRef.None)
                return;

            EffectConfig config = StatusEffectUtility.GetEffectConfig(f);

            if (config == null)
                return;

            FP duration = StatusEffectUtility.ScaleDuration(f, context.Owner, context.Source, config.SlowDuration) * durationMultiplier;
            FP buildup = context.Damage * config.IceBuildupPerDamage * magnitudeMultiplier;

            StatusEffectUtility.ApplyIce(f, context.Target, duration, buildup);

            // Directly-authored Slow (not the weapon-elemental-proc path) still needs to participate
            // in the elemental reaction check for its own element.
            StatusEffectUtility.TryTriggerElementalReaction(f, context.Target, context.Owner, context.Source, ElementType.Ice, context.Damage);
        }
    }
}
