namespace Quantum
{
    using Photon.Deterministic;

    // Knockback Mastery Hero Trait - bonus damage against a currently-Stunned target, read live in
    // DamageUtility.ResolveOutgoingDamage - see Heroes/Brute/KnockbackMastery.qtn.
    public unsafe partial class CrushingBlowPassiveUpgradeData : PassiveUpgradeData
    {
        public FP DamageMultiplierBonus = FP.FromString("0.4");

        public override void Apply(Frame f, EntityRef entity)
        {
            f.AddOrGet<CrushingBlowUpgrade>(entity, out var upgrade);
            upgrade->DamageMultiplierBonus += DamageMultiplierBonus;
        }
    }
}
