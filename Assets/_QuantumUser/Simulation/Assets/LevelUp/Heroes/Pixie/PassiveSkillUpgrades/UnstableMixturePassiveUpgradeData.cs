namespace Quantum
{
    using Photon.Deterministic;

    // Ranked Ascension - Pixie's Unstable damage line. A marked (Unstable) enemy's death blast deals
    // DamagePercent of its own MaxHealth instead of the shared ExplodeOnDeathConfig.DamagePercent base
    // (10%) - see MarkExplosiveDeath.DamagePercent/DamageUtility.TryExplodeOnDeath. Also widens which
    // tiers Chain Reaction can mark: Heavy at ranks 1-2 (Specialist/Heavy kills need to be markable for
    // the bigger numbers to matter), Elite at rank 3. Bosses are never markable this way.
    //
    // Reworked 2026-10-21 from the "explosion kills store charges that empower her next explosion"
    // version (UnstableMixtureUpgrade, removed with its DelayedBlast secondary).
    public unsafe partial class UnstableMixturePassiveUpgradeData : PassiveUpgradeData
    {
        public FP[] DamagePercent = { FP.FromString("0.30"), FP.FromString("0.60"), FP.FromString("0.90") };
        public EnemyTier[] MaxAffectedTier = { EnemyTier.Heavy, EnemyTier.Heavy, EnemyTier.Elite };

        public override void Apply(Frame f, EntityRef entity, int rank)
        {
            if (f.Unsafe.TryGetPointer<MarkExplosiveDeath>(entity, out var mark) == false)
                return;

            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            mark->DamagePercent = DamagePercent[index];

            // Only ever raises the gate - never lowers one another source already widened.
            if (mark->MaxAffectedTier < (byte)MaxAffectedTier[index])
                mark->MaxAffectedTier = (byte)MaxAffectedTier[index];
        }
    }
}
