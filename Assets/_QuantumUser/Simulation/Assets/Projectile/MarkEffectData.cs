namespace Quantum
{
    using Photon.Deterministic;

    // Multiplies incoming damage on the target for Duration - see
    // StatusEffectUtility.GetIncomingDamageMultiplier, applied once inside DamageUtility.ApplyDamage
    // so every damage source respects it identically. Not an ElementType, so unlike Burn/Ice/Poison
    // this is only ever explicitly authored onto an attack's Effects list, never part of the
    // elemental proc roll. Duration/DamageTakenMultiplier are read from the shared
    // RuntimeConfig.EffectConfig rather than authored here, so every source of Mark hits identically
    // (same reasoning as HasteEffectData).
    public unsafe class MarkEffectData : HitEffectData
    {
        public override void Apply(Frame f, ref HitEffectContext context)
        {
            if (context.Target == EntityRef.None)
                return;

            EffectConfig config = StatusEffectUtility.GetEffectConfig(f);

            if (config == null)
                return;

            FP duration = StatusEffectUtility.ScaleDuration(f, context.Owner, context.Source, config.MarkDuration);

            StatusEffectUtility.ApplyMark(f, context.Target, duration, config.MarkDamageTakenMultiplier);
        }
    }
}
