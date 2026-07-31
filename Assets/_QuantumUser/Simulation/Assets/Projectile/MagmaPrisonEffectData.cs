namespace Quantum
{
    using Photon.Deterministic;

    // Applies Root + Burn together - the same compound Fire+Rock produces via the Magma Prison
    // reaction (see docs/elemental-reactions.md), but as a single freely-authorable HitEffectData
    // any skill/weapon perk can drop onto its own Effects list, independent of actually landing both
    // elements. Unlike RootEffectData/FreezeEffectData, this needs no new EffectConfig fields of its
    // own: RootDuration and BurnDuration/BurnDamagePercent/BurnFloorPercent are already established
    // as freely-shared generic knobs (RootDuration was scoped generic from the start; Burn's fields
    // already back BurnEffectData/the Fire weapon-proc/TryApplyGuaranteedBurn), so this is just
    // RootEffectData and BurnEffectData bundled into one authoring convenience - NOT
    // ElementalReactionConfig.MagmaPrisonRootDuration, which stays dedicated to the actual reaction
    // (that field has a live consumer - the reaction itself - so sharing it here would silently
    // couple this effect's tuning to Fire+Rock's own balance).
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
