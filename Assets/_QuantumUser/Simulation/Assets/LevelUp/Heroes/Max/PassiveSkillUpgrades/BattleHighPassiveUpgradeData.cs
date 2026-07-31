namespace Quantum
{
    using Photon.Deterministic;

    // Passive Ascension - at maximum Adrenaline, gain increased Weapon Damage - see
    // AdrenalineUtility.GetWeaponDamageMultiplier, folded into DamageUtility.ResolveOutgoingDamage.
    public unsafe partial class BattleHighPassiveUpgradeData : PassiveUpgradeData
    {
        public FP WeaponDamageBonusAtMax = FP._0_25;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<Adrenaline>(entity, out var adrenaline) == false)
                return;

            adrenaline->WeaponDamageBonusAtMax += WeaponDamageBonusAtMax;
        }
    }
}
