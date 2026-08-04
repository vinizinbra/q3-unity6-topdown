namespace Quantum
{
    using Photon.Deterministic;

    // Knockback Mastery Hero Trait - landing from at least MinFallDistance of net drop (see
    // BruteKnockbackMasterySystem.OnPlayerLanded, fired from AutoJumpSystem's own generic landing
    // edge) pulses a pure knockback burst - see Heroes/Brute/KnockbackMastery.qtn. Force/UpwardForce
    // aren't authored directly - Tier picks a bucket from the shared RuntimeConfig.EffectConfig
    // (same Small/Medium/Strong every KnockbackEffectData in the game already pulls from via
    // EffectConfig.GetKnockback), resolved once here at Apply time and baked into the component as
    // plain FP so BruteKnockbackMasterySystem never needs to touch EffectConfig itself. Bakes Source
    // as a self-reference ("upgrade->Source = this;") purely so the view can resolve BlastEffectPrefab
    // (see the .View.cs partial) off the exact asset that granted this - same pattern
    // VortexExplodeOnDestroySkillAction.Source already uses.
    public unsafe partial class GroundPoundPassiveUpgradeData : PassiveUpgradeData
    {
        public FP Radius = 4;
        public KnockbackTier Tier = KnockbackTier.Medium;
        public FP MinFallDistance = FP._2;

        public override void Apply(Frame f, EntityRef entity)
        {
            bool isFirstPick = f.Unsafe.TryGetPointer<GroundPoundUpgrade>(entity, out _) == false;

            f.AddOrGet<GroundPoundUpgrade>(entity, out var upgrade);
            upgrade->Radius = FPMath.Max(upgrade->Radius, Radius);

            EffectConfig config = StatusEffectUtility.GetEffectConfig(f);
            if (config != null)
            {
                config.GetKnockback(Tier, out FP force, out FP upwardForce);
                // A re-pick only ever takes the stronger tier's push - Tier is a discrete bucket, not
                // a stackable delta, so "additive" (the old raw-Force behavior) no longer applies.
                upgrade->Force = FPMath.Max(upgrade->Force, force);
                upgrade->UpwardForce = FPMath.Max(upgrade->UpwardForce, upwardForce);
            }

            // A re-pick only ever lowers the threshold (easier to trigger), same "never regress" shape Radius uses via Max.
            upgrade->MinFallDistance = isFirstPick ? MinFallDistance : FPMath.Min(upgrade->MinFallDistance, MinFallDistance);
            upgrade->Source = this;
        }
    }
}
