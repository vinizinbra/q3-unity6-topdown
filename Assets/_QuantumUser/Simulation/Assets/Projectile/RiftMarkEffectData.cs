namespace Quantum
{
    using Photon.Deterministic;

    // Applies a Rift Mark stack (StatusEffectUtility.ApplyRiftMark) - no damage, no DoT, just marks
    // the target so a later Fire/Ice/Rock/Lightning/Void proc (this owner's own, or a teammate's)
    // consumes a stack and fires that element's own reaction. Never triggers a reaction itself - see
    // docs/elemental-reactions.md. Renamed from VoidEffectData once Void was promoted to a real
    // damage-dealing element (same migration pattern this class itself came from - PoisonEffectData
    // once Poison was removed). Stack count/duration are read from the shared
    // RuntimeConfig.ElementalReactionConfig rather than authored here, so every source of Rift Mark
    // hits identically (same reasoning as HasteEffectData).
    public unsafe class RiftMarkEffectData : HitEffectData
    {
        public override void Apply(Frame f, ref HitEffectContext context) => Apply(f, ref context, FP._1, FP._1);

        // Zara's Remix ascension rank 2+ scales duration/magnitude generically through this overload
        // (see HitEffectData.Apply's own comment) - both default to FP._1 from the plain 2-arg Apply
        // above, reproducing the exact pre-Remix behavior for every other caller. magnitudeMultiplier
        // scales the stack count applied, rounded to the nearest whole stack.
        public override void Apply(Frame f, ref HitEffectContext context, FP durationMultiplier, FP magnitudeMultiplier)
        {
            // Excluded here rather than upstream (see HitEffectUtility.TryBuildContext) - a mark
            // shouldn't mark whoever set it off, but a heal (HealEffectData) very much should be
            // able to reach them.
            if (context.Target == EntityRef.None || context.Target == context.Owner)
            {
                Log.Debug($"[Effect] RiftMarkEffectData skipped - Target {context.Target}, Owner {context.Owner}");
                return;
            }

            ElementalReactionConfig config = StatusEffectUtility.GetElementalReactionConfig(f);

            if (config == null)
                return;

            FP duration = StatusEffectUtility.ScaleDuration(f, context.Owner, context.Source, config.BaseDuration) * durationMultiplier;
            byte stacks = (byte)FPMath.RoundToInt(config.StacksAppliedPerApplication * magnitudeMultiplier);

            Log.Debug($"[Effect] RiftMarkEffectData applying {stacks} stack(s) to {context.Target}: duration {duration}");

            StatusEffectUtility.ApplyRiftMark(f, context.Target, config, duration, stacks);
        }
    }
}
