namespace Quantum
{
    using Photon.Deterministic;

    // Applies Void (StatusEffectUtility.ApplyVoid) - no damage, no DoT, just marks the target so a
    // later Fire/Ice/Rock proc (this owner's own, or a teammate's) triggers one of the 6 elemental
    // reactions. Migrated from PoisonEffectData once Poison was removed - see
    // docs/elemental-reactions.md. Duration is read from the shared RuntimeConfig.EffectConfig
    // rather than authored here, so every source of Void hits identically (same reasoning as
    // HasteEffectData).
    public unsafe class VoidEffectData : HitEffectData
    {
        public override void Apply(Frame f, ref HitEffectContext context)
        {
            // Excluded here rather than upstream (see HitEffectUtility.TryBuildContext) - Void
            // shouldn't mark whoever set it off, but a heal (HealEffectData) very much should be
            // able to reach them.
            if (context.Target == EntityRef.None || context.Target == context.Owner)
            {
                Log.Debug($"[Effect] VoidEffectData skipped - Target {context.Target}, Owner {context.Owner}");
                return;
            }

            EffectConfig config = StatusEffectUtility.GetEffectConfig(f);

            if (config == null)
                return;

            FP duration = StatusEffectUtility.ScaleDuration(f, context.Owner, context.Source, config.VoidDuration);

            Log.Debug($"[Effect] VoidEffectData applying to {context.Target}: duration {duration}");

            StatusEffectUtility.ApplyVoid(f, context.Target, duration);

            // Directly-authored Void (not the weapon-elemental-proc path) still needs to participate
            // in the elemental reaction scan - see StatusEffectUtility.TryTriggerReactions. This is
            // the actual gap that made Zara's Void Damage Waves never trigger a reaction: applying
            // Void through this class alone used to be a dead end.
            StatusEffectUtility.TryTriggerReactions(f, context.Target, context.Owner, context.Source, ElementType.Void, context.Damage);
        }
    }
}
