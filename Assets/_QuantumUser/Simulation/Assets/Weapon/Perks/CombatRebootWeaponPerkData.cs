namespace Quantum
{
    using Photon.Deterministic;

    // Consumed by WeaponSystem.ApplyMagazineEmptiedPerks - emptying the magazine reduces the Hero
    // Skill's own cooldown (SkillSystem.ReduceCooldown).
    public unsafe class CombatRebootWeaponPerkData : WeaponPerkData
    {
        public FP CooldownReduction = 2;

        public override void Apply(Frame f, Weapon* weapon)
        {
            weapon->HasCombatReboot = true;
            weapon->CombatRebootCooldownReduction += CooldownReduction;
        }

        protected override object[] DescriptionArgs => new object[] { CooldownReduction.AsFloat };
    }
}
