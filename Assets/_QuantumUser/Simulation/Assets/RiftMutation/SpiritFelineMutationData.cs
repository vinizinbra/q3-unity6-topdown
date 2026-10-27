namespace Quantum
{
    using Photon.Deterministic;

    // Every hit hurts more, but no single hit can take more than HitDamageCap of your Max Health - big
    // hitters stop being scary, swarms and chip damage become the threat. The cap lives in
    // MutationCombatUtility.ResolveIncomingDamage; the vulnerability rides DamageTakenMultiplier.
    public unsafe class SpiritFelineMutationData : RiftMutationData
    {
        public FP DamageTakenFactor = 2;
        public FP HitDamageCap = FP.FromString("0.14");

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->DamageTakenMultiplier = FPMath.Max(FP._0, stats->DamageTakenMultiplier * DamageTakenFactor);

            // Tightest cap wins, so a second capping source could only make it stricter.
            stats->HitDamageCapFraction = stats->HitDamageCapFraction <= FP._0 ? HitDamageCap : FPMath.Min(stats->HitDamageCapFraction, HitDamageCap);
        }

        protected override object[] DescriptionArgs => new object[] { (DamageTakenFactor.AsFloat - 1f) * 100f, HitDamageCap.AsFloat * 100f };
    }
}
