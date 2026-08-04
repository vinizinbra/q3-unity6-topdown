namespace Quantum
{
    using Photon.Deterministic;

    // Pins the target's movement only (unlike Stun, attacking/skills/firing stay untouched) for
    // Duration - see StatusEffectUtility.IsRooted/ApplyRoot. Plain overwrite-on-reapply. Duration is
    // read from the shared RuntimeConfig.EffectConfig.RootDuration rather than authored here, so
    // every source of Root hits identically (same reasoning as HasteEffectData/StunEffectData) -
    // that field was already scoped as a generic shared value even before this class existed (see
    // its own comment on EffectConfig), previously only reached via JuggernautLandingRootSkillAction.
    // This gives Root a freely-authorable HitEffectData the same way Stun already has one,
    // independent of Juggernaut's own skill (the old Fire+Rock Magma Prison elemental reaction that
    // once used its own dedicated ElementalReactionConfig.MagmaPrisonRootDuration was retired when
    // Rift Mark replaced the pairwise reaction scan - see docs/elemental-reactions.md).
    public unsafe class RootEffectData : HitEffectData
    {
        public override void Apply(Frame f, ref HitEffectContext context)
        {
            // Excluded here rather than upstream (see HitEffectUtility.TryBuildContext) - a root
            // shouldn't root whoever set it off, same as Damage/Burn/Stun/Knockback.
            if (context.Target == EntityRef.None || context.Target == context.Owner)
                return;

            EffectConfig config = StatusEffectUtility.GetEffectConfig(f);

            if (config == null)
                return;

            FP duration = StatusEffectUtility.ScaleDuration(f, context.Owner, context.Source, config.RootDuration);

            StatusEffectUtility.ApplyRoot(f, context.Target, duration);

            Log.Debug($"[Effect] RootEffectData applied to {context.Target} for {duration}s");
        }
    }
}
