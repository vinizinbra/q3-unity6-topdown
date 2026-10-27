namespace Quantum
{
    using Photon.Deterministic;

    // Weapon hits deal bonus damage equal to a fraction of the target's CURRENT health (smaller against
    // Bosses), never more than HitCap x the hit itself - the opener against big health pools,
    // Executioner's mirror at the other end of the bar.
    public unsafe class GiantSlayerMutationData : RiftMutationData
    {
        public FP CurrentHealthFraction = FP.FromString("0.02");
        public FP BossCurrentHealthFraction = FP.FromString("0.005");
        public FP HitCap = 1;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->CurrentHealthDamageFraction = CurrentHealthFraction;
            stats->CurrentHealthDamageBossFraction = BossCurrentHealthFraction;
            stats->CurrentHealthBonusHitCap = HitCap;
        }

        protected override object[] DescriptionArgs => new object[] { CurrentHealthFraction.AsFloat * 100f, BossCurrentHealthFraction.AsFloat * 100f, HitCap.AsFloat * 100f };
    }
}
