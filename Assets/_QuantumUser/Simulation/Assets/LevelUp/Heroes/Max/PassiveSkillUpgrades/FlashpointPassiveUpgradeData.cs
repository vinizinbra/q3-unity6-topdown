namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Passive line 8 - merges the old standalone Hot Target (rank 1) + Flashpoint (rank 2) +
    // Cremation (rank 3) picks into one 3-rank Ascension - all Fire Mastery traits, each composing
    // onto its own dedicated component exactly as its standalone predecessor did. Requires a real
    // Burn source first (IsEligible/CanApplyBurn - see docs/max-ascensions.md's "Burn Ascension
    // Eligibility" section), since every effect here only matters against a Burning target. Each rank
    // SETS the total values for whichever component(s) that rank unlocks - a lower rank leaves higher
    // ranks' components untouched rather than granting them early with placeholder values.
    // BossExecutionEnabled is never set true here - the brief drops the old per-pick toggle entirely,
    // Boss is never executable by this line, full stop.
    public unsafe partial class FlashpointPassiveUpgradeData : PassiveUpgradeData
    {
        [Header("Rank 1 - Hot Target")]
        public FP CriticalChanceBonusVsBurning = FP._0_10;

        [Header("Rank 2 - Flashpoint")]
        public FP ExplosionRadius = 3;
        public FP ExplosionDamageCoefficient = FP._0_50;
        public FP ExplosionProcCooldown = 2;
        public int ExplosionMaxTargets = 5;

        [Header("Rank 3 - Cremation")]
        public FP NormalHealthThreshold = FP.FromString("0.15");
        public FP EliteHealthThreshold = FP._0_10;

        public override bool IsEligible(Frame f, EntityRef entity) => f.Has<CanApplyBurn>(entity);

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            f.AddOrGet<ConditionalCriticalModifier>(entity, out var modifier);
            modifier->CriticalChanceBonusVsBurning = CriticalChanceBonusVsBurning;

            if (rank >= 2)
            {
                f.AddOrGet<ExplosionOnConditionalHit>(entity, out var explosion);
                explosion->Radius = ExplosionRadius;
                explosion->DamageCoefficient = ExplosionDamageCoefficient;
                explosion->ProcCooldown = ExplosionProcCooldown;
                explosion->MaxTargets = ExplosionMaxTargets;
            }

            if (rank >= 3)
            {
                f.AddOrGet<ExecuteAgainstStatus>(entity, out var execute);
                execute->NormalHealthThreshold = NormalHealthThreshold;
                execute->EliteHealthThreshold = EliteHealthThreshold;
            }
        }
    }
}
