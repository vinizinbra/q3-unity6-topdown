namespace Quantum
{
    using Photon.Deterministic;

    // Sacrifice HP for emergency mobility. While Dash is on cooldown you may take ONE extra Dash by
    // paying a percentage of Max Health; the opportunity comes back when a real Dash charge does.
    //
    // The cost is deliberately a FRACTION of Max Health rather than a flat amount, so it scales with
    // a build instead of becoming trivial - and it can never be lethal (SkillSystem floors the
    // result at 1). It is applied as a direct health write rather than through
    // DamageUtility.ApplyDamage: this is a self-inflicted price, not a hit, so it must not roll
    // crit, proc a status, count as hostile damage, interrupt a revive channel, or cost an Accessory
    // durability point.
    //
    // Because the cost is a fraction of MAX health rather than a flat number, it stays meaningful
    // against any build - including Glass Core's halved health pool, which is why the two need no
    // mutual-exclusion rule between them.
    public unsafe class InfiniteMomentumMutationData : RiftMutationData
    {
        public FP HealthCostFraction = FP._0;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->EmergencyDashHealthCost = FPMath.Max(stats->EmergencyDashHealthCost, HealthCostFraction);
        }

        protected override object[] DescriptionArgs => new object[] { HealthCostFraction.AsFloat * 100f };
    }
}
