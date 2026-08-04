namespace Quantum
{
    using Photon.Deterministic;

    // Passive Ascension - deals bonus damage against any enemy currently carrying ExplodeOnDeath
    // ("Unstable" - marked to explode on death, from Chain Reaction or any other source), read live
    // in DamageUtility.ResolveOutgoingDamage. Distinct from Unstable Mixture (BonusDamageMultiplier),
    // which only scales a marked enemy's own death-explosion payout - see
    // MarkExplosiveDeath.qtn's own comment on the two.
    public unsafe partial class UnstableTargetingPassiveUpgradeData : PassiveUpgradeData
    {
        public FP DamageMultiplierBonus = FP.FromString("0.3");

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<MarkExplosiveDeath>(entity, out var mark) == false)
                return;

            mark->DamageBonusVsUnstable += DamageMultiplierBonus;
        }
    }
}
