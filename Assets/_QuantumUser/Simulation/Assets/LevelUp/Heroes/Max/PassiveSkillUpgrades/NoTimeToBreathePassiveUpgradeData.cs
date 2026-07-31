namespace Quantum
{
    using Photon.Deterministic;

    // Passive Ascension - Adrenaline decays slower, and does not decay at all while an enemy is
    // within weapon range - see AdrenalineSystem.Update/IsEnemyInWeaponRange.
    public unsafe partial class NoTimeToBreathePassiveUpgradeData : PassiveUpgradeData
    {
        public FP DecayIntervalBonus = FP._0_50;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<Adrenaline>(entity, out var adrenaline) == false)
                return;

            adrenaline->DecayInterval += DecayIntervalBonus;
            adrenaline->NoDecayNearWeaponRange = true;
        }
    }
}
