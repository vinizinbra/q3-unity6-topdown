namespace Quantum
{
    using Photon.Deterministic;

    // Coins flow in faster, but bleed out every time you actually get hurt - so a big balance is
    // something you are constantly at risk of losing, not something you passively accumulate.
    //
    // The drop half is deliberately routed through the PER-PLAYER CoinGainMultiplier rather than by
    // scaling the world drop itself. Coin drops are shared in co-op (one orb, every player credited
    // - see CoinUtility.GrantAll), so multiplying the drop would hand this player's mutation to the
    // whole team. Scaling their own gain multiplier is the least invasive player-scoped equivalent
    // and needs no new plumbing at all.
    //
    // The loss half reacts to OnHealthDamageApplied, which is exactly "this player actually lost
    // health" - so an Accessory-blocked hit (which returns from ApplyDamage long before that signal)
    // and a Shield-only hit (which fires OnShieldDamageApplied instead) both correctly cost nothing.
    public unsafe class BloodMoneyMutationData : RiftMutationData
    {
        public FP CoinDropMultiplier = FP._1;
        public FP CoinLossPercentOnHpDamage = FP._0;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->CoinGainMultiplier = FPMath.Max(FP._0, stats->CoinGainMultiplier * CoinDropMultiplier);
            stats->CoinLossPercentOnHpDamage = FPMath.Max(stats->CoinLossPercentOnHpDamage, CoinLossPercentOnHpDamage);
        }

        protected override object[] DescriptionArgs => new object[]
        {
            (CoinDropMultiplier.AsFloat - 1f) * 100f,
            CoinLossPercentOnHpDamage.AsFloat * 100f
        };
    }
}
