namespace Quantum
{
    using Photon.Deterministic;

    // Knockback Mastery Hero Trait - increases outgoing knockback force. No dedicated component -
    // CharacterStats.KnockbackMultiplier is already a live-read bakeable stat (see DamageUtility.
    // ResolveKnockbackScale), so this mutates it directly, same one-line shape
    // BiggerBoomPassiveUpgradeData uses for MarkExplosiveDeath.BonusRadiusMultiplier.
    public unsafe partial class OverwhelmingForcePassiveUpgradeData : PassiveUpgradeData
    {
        public FP KnockbackMultiplierBonus = FP.FromString("0.3");

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->KnockbackMultiplier *= FP._1 + KnockbackMultiplierBonus;
        }
    }
}
