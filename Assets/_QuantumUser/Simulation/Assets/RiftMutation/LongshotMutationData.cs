namespace Quantum
{
    using Photon.Deterministic;

    // Distant positioning and piercing lines. Deliberately NOT the mathematical mirror of Close
    // Quarters: as well as inverting the range falloff, a long-range shot gains extra PIERCE, which
    // turns holding distance into an actively different way to shoot (lining enemies up) rather than
    // the same play at the other end of a number line.
    //
    // The pierce is granted per SHOT at fire time (WeaponSystem.ResolveLongRangePierceBonus), not
    // per hit - pierce has to be baked into the projectile/hitscan walk before any individual
    // target's distance is known - and rides the generic pierce system every weapon perk already
    // uses, so it composes with Piercing Rounds and Phantom Strike for free.
    public unsafe class LongshotMutationData : RiftMutationData
    {
        public FP FarMultiplier = FP._1;
        public FP NearMultiplier = FP._1;
        public int LongRangePierceBonus = 0;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->FarDamageMultiplier = FPMath.Max(FP._0, stats->FarDamageMultiplier * FarMultiplier);
            stats->NearDamageMultiplier = FPMath.Max(FP._0, stats->NearDamageMultiplier * NearMultiplier);
            stats->LongRangePierceBonus += LongRangePierceBonus;
        }

        protected override object[] DescriptionArgs => new object[]
        {
            (FarMultiplier.AsFloat - 1f) * 100f,
            LongRangePierceBonus,
            (NearMultiplier.AsFloat - 1f) * 100f
        };
    }
}
