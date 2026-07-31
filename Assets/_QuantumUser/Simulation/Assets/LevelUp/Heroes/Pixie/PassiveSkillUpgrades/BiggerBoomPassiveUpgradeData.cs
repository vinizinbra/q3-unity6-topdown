namespace Quantum
{
    using Photon.Deterministic;

    // Passive Ascension - increases the radius of every explosion Pixie causes: the death-explosion
    // mechanic (MarkExplosiveDeath.BonusRadiusMultiplier / DamageUtility.TryExplodeOnDeath), her
    // weapon's explosive procs (WeaponSystem.ApplyHitscanWeaponPerks/DirectHitData.
    // ApplyTerminalWeaponPerks), her bomb (AreaHitData.Detonate), and Backblast - all read the same
    // field live via DamageUtility.ResolvePixieExplosionRadiusMultiplier. Compounds with itself and
    // with Heavy Payload's own separate multiplier rather than one overwriting the other.
    public unsafe partial class BiggerBoomPassiveUpgradeData : PassiveUpgradeData
    {
        public FP RadiusMultiplierBonus = FP._0_25;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<MarkExplosiveDeath>(entity, out var mark) == false)
                return;

            mark->BonusRadiusMultiplier += RadiusMultiplierBonus;
        }
    }
}
