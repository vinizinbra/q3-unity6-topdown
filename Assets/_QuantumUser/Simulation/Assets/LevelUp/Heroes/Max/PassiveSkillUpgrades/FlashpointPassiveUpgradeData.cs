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
    // Elite/Boss are never executable by this line, full stop - rank 3 gives them a bonus-damage
    // window instead (EliteBossDamageThreshold/Bonus, read by
    // MaxFireMasteryReactionSystem.ResolveCremationDamageBonus), so the rank still pays off in a
    // boss fight without deleting one.
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
        [Tooltip("Burning Filler/Normal enemies at or below this fraction of Max Health are executed outright.")]
        public FP NormalHealthThreshold = FP.FromString("0.15");

        [Tooltip("Same, for Specialist/Heavy - deliberately a tighter window than Normal.")]
        public FP SpecialistHealthThreshold = FP.FromString("0.08");

        [Tooltip("Elite/Boss are NEVER executed. Instead they take EliteBossDamageBonus extra damage while Burning and at or below this fraction of Max Health.")]
        public FP EliteBossDamageThreshold = FP.FromString("0.15");
        public FP EliteBossDamageBonus = FP._0_25;

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
                execute->SpecialistHealthThreshold = SpecialistHealthThreshold;
                execute->EliteBossDamageThreshold = EliteBossDamageThreshold;
                execute->EliteBossDamageBonus = EliteBossDamageBonus;
            }
        }
    }
}
