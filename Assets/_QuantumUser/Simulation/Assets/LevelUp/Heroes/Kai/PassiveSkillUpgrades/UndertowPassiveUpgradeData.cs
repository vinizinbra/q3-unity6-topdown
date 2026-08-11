namespace Quantum
{
    using Photon.Deterministic;

    // Ranked Passive Ascension (Undertow, line 2/3) - see docs/kai-ascensions.md. A weapon hit
    // starts/refreshes a continuous pull dragging the struck enemy toward whichever other enemy is
    // currently nearest to it, read live in KaiUndertowSystem.OnWeaponHitLanded/TickUndertowPulls. Rank
    // 2 pulls harder and scales further against Specialist+ targets ("heavier enemy types"). Rank 3
    // "Gravitational Bond" additionally Binds a successfully-pulled enemy for a few seconds, during
    // which Kai deals bonus damage to it (see StatusEffectUtility.ApplyBound/IsBound,
    // DamageUtility.ResolveOutgoingDamage).
    public unsafe partial class UndertowPassiveUpgradeData : PassiveUpgradeData
    {
        public FP[] PullForce = { FP._3, FP._6, FP._6 };
        public FP[] PullDuration = { FP.FromString("0.2"), FP.FromString("0.2"), FP.FromString("0.2") };
        public FP[] HeavyTierMultiplier = { FP._1, FP._1_50, FP._1_50 };

        // Rank 3 only (0 at ranks 1-2, which leaves Bound entirely unapplied).
        public FP[] BoundDuration = { FP._0, FP._0, FP._2 };
        public FP[] BoundDamageBonus = { FP._0, FP._0, FP.FromString("0.20") };

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<UndertowUpgrade>(entity, out var upgrade);
            upgrade->PullForce = PullForce[index];
            upgrade->PullDuration = PullDuration[index];
            upgrade->HeavyTierMultiplier = HeavyTierMultiplier[index];
            upgrade->BoundDuration = BoundDuration[index];
            upgrade->BoundDamageBonus = BoundDamageBonus[index];
        }
    }
}
