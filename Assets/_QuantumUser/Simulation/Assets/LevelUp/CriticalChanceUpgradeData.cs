namespace Quantum
{
    using Photon.Deterministic;

    // Flat add, not a multiplier - mirrors CriticalChanceWeaponPerkData exactly, just targeting
    // CharacterStats.CriticalChance instead of Weapon.CriticalChance (the two stack, same
    // independent-sources convention as WeaponDamageUpgradeData). See docs/global-upgrades.md.
    public unsafe class CriticalChanceUpgradeData : GlobalUpgradeData
    {
        public FP Chance;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->CriticalChance += Chance;
        }

        protected override object[] DescriptionArgs => new object[] { FPMath.RoundToInt(Chance * 100) };
    }
}
