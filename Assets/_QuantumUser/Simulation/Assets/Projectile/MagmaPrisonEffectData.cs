namespace Quantum
{
    using Photon.Deterministic;

    // Applies Root + Burn together - the same compound effect the old Fire+Rock "Magma Prison"
    // elemental reaction used to produce, before that reaction was retired when Rift Mark replaced
    // the pairwise reaction scan (see docs/elemental-reactions.md's "What was retired") - this
    // freely-authorable HitEffectData is now the only way to get it, any skill/weapon perk can drop
    // it onto its own Effects list independent of landing any particular element. Needs no dedicated
    // EffectConfig fields of its own: RootDuration and BurnDuration/BurnDamagePercent/BurnFloorPercent
    // are already established as freely-shared generic knobs (RootDuration was scoped generic from
    // the start; Burn's fields already back BurnEffectData/the Fire weapon-proc/
    // TryApplyGuaranteedBurn), so this is just RootEffectData and BurnEffectData bundled into one
    // authoring convenience.
    public unsafe class MagmaPrisonEffectData : HitEffectData
    {
        public override void Apply(Frame f, ref HitEffectContext context)
        {
            // Excluded here rather than upstream (see HitEffectUtility.TryBuildContext) - same as
            // Damage/Burn/Root/Stun/Knockback, neither half of this should land on whoever set it off.
            if (context.Target == EntityRef.None || context.Target == context.Owner)
                return;

            EffectConfig config = StatusEffectUtility.GetEffectConfig(f);

            if (config == null)
                return;

            FP rootDuration = StatusEffectUtility.ScaleDuration(f, context.Owner, context.Source, config.RootDuration);
            StatusEffectUtility.ApplyRoot(f, context.Target, rootDuration);

            FP burnDuration = StatusEffectUtility.ScaleDuration(f, context.Owner, context.Source, config.BurnDuration);
            FP damagePerTick = StatusEffectUtility.ComputeDotDamagePerTickWithFloor(f, context.Owner, context.Damage,
                config.BurnDamagePercent, config.BurnFloorPercent, config.BurnDuration, config.TickInterval);
            StatusEffectUtility.ApplyBurn(f, context.Target, burnDuration, damagePerTick, context.Owner, context.Source, config.TickInterval);

            Log.Debug($"[Effect] MagmaPrisonEffectData applied to {context.Target} - Root {rootDuration}s, Burn {burnDuration}s at {damagePerTick}/tick");
        }
    }
}
