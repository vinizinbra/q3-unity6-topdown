namespace Quantum
{
    using Photon.Deterministic;

    // Passive Ascension - increases the death-explosion damage (see
    // MarkExplosiveDeath.BonusDamageMultiplier / DamageUtility.TryExplodeOnDeath). Compounds with
    // itself and with Heavy Payload's own separate multiplier rather than one overwriting the other.
    public unsafe partial class UnstableMixturePassiveUpgradeData : PassiveUpgradeData
    {
        public FP DamageMultiplierBonus = FP._0_25;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<MarkExplosiveDeath>(entity, out var mark) == false)
                return;

            mark->BonusDamageMultiplier += DamageMultiplierBonus;
        }
    }
}
