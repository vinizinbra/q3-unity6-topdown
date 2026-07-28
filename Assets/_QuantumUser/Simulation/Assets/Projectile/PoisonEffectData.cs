namespace Quantum
{
    using Photon.Deterministic;

    // Stacks, unlike Burn - see StatusEffectUtility.ApplyPoison. A second Poison proc on an already
    // poisoned target adds an independent ticking instance instead of refreshing the first.
    // Duration/DamagePercent are read from the shared RuntimeConfig.EffectConfig rather than authored
    // here, so every source of Poison hits identically (same reasoning as HasteEffectData).
    public unsafe class PoisonEffectData : HitEffectData
    {
        public override void Apply(Frame f, ref HitEffectContext context)
        {
            // Excluded here rather than upstream (see HitEffectUtility.TryBuildContext) - a poison
            // shouldn't poison whoever set it off, but a heal (HealEffectData) very much should be
            // able to reach them.
            if (context.Target == EntityRef.None || context.Target == context.Owner)
            {
                Log.Debug($"[Effect] PoisonEffectData skipped - Target {context.Target}, Owner {context.Owner}");
                return;
            }

            EffectConfig config = StatusEffectUtility.GetEffectConfig(f);

            if (config == null)
                return;

            FP duration = StatusEffectUtility.ScaleDuration(f, context.Owner, context.Source, config.PoisonDuration);
            FP damagePerTick = StatusEffectUtility.ComputeDotDamagePerTickWithFloor(f, context.Owner, context.Damage,
                config.PoisonDamagePercent, config.PoisonFloorPercent, config.PoisonDuration, config.TickInterval);

            Log.Debug($"[Effect] PoisonEffectData applying to {context.Target}: duration {duration}, {damagePerTick}/tick (context.Damage {context.Damage})");

            StatusEffectUtility.ApplyPoison(f, context.Target, duration, damagePerTick, context.Owner, context.Source, config.TickInterval);
        }
    }
}
