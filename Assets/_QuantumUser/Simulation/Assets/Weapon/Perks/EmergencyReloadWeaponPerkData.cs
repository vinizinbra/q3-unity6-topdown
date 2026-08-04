namespace Quantum
{
    using Photon.Deterministic;

    // Consumed by WeaponSystem.TryApplyEmergencyReload/RevertEmergencyReload - folds a temporary
    // move-speed/damage-reduction bonus into CharacterStats for the duration of a real
    // (ReloadTimer-driven) reload, same additive add-on-start/subtract-on-end idiom
    // JuggernautSkillData already uses for its own temporary CharacterStats bonuses.
    public unsafe class EmergencyReloadWeaponPerkData : WeaponPerkData
    {
        public FP MoveSpeedBonus;
        public FP DamageReduction;

        public override void Apply(Frame f, EntityRef owner, Weapon* weapon)
        {
            f.AddOrGet<WeaponReloadHooks>(owner, out var hooks);
            hooks->HasEmergencyReload = true;
            hooks->EmergencyReloadMoveSpeedBonus += MoveSpeedBonus;
            hooks->EmergencyReloadDamageReduction += DamageReduction;
        }

        protected override object[] DescriptionArgs => new object[] { MoveSpeedBonus.AsFloat * 100f, DamageReduction.AsFloat * 100f };
    }
}
