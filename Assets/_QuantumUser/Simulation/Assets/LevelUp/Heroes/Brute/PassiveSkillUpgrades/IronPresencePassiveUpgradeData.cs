namespace Quantum
{
    using Photon.Deterministic;

    // Ranked Ascension - absorbs the old Iron Presence + Fearless concepts. Intimidated enemies in the
    // aura take more knockback (see StatusEffectUtility.ApplyKnockbackTaken, driven by
    // ProtectorAuraSystem.ApplyToEnemies); rank 2+ additionally makes Brute deal bonus damage against
    // Intimidated targets (see ProtectorAuraUtility.GetFearlessBonusMultiplier, folded into
    // DamageUtility.ResolveOutgoingDamage). Each rank SETS the total values (not additive across ranks).
    public unsafe partial class IronPresencePassiveUpgradeData : PassiveUpgradeData
    {
        public FP[] KnockbackTakenMultiplier = { FP.FromString("1.25"), FP.FromString("1.25"), FP._1_50 };
        public FP[] FearlessBonusVsIntimidated = { FP._0, FP.FromString("0.20"), FP.FromString("0.35") };

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            if (f.Unsafe.TryGetPointer<ProtectorAura>(entity, out var aura) == false)
                return;

            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            aura->IntimidateKnockbackTakenMultiplier = KnockbackTakenMultiplier[index];
            aura->FearlessBonusVsIntimidated = FearlessBonusVsIntimidated[index];
        }
    }
}
