namespace Quantum
{
    using Photon.Deterministic;

    // Consumed by WeaponSystem.ApplyMagazineEmptiedPerks - emptying the magazine reduces the Hero
    // Skill's own cooldown (SkillSystem.ReduceCooldown).
    public unsafe class CombatRebootWeaponPerkData : WeaponPerkData
    {
        public FP CooldownReduction = 2;

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            f.AddOrGet<WeaponReloadHooks>(owner, out var hooks);
            hooks->HasCombatReboot = true;
            hooks->CombatRebootCooldownReduction += CooldownReduction;
        }

        protected override object[] DescriptionArgs => new object[] { CooldownReduction.AsFloat };
    }
}
