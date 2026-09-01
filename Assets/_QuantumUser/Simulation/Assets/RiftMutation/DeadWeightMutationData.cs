namespace Quantum
{
    using Photon.Deterministic;

    // Trade mobility for firepower: much heavier weapon damage, but you are permanently reduced to a
    // single, slower Dash charge.
    //
    // The charge limit is a HARD CAP, deliberately not a subtraction. SkillSlot.MaxStacks keeps
    // whatever charge upgrades accumulated into it and SkillSystem.ResolveEffectiveMaxStacks returns
    // min(MaxStacks, cap) wherever availability is decided. That distinction matters three ways:
    // an already-owned "+1 Charge" upgrade stays owned and simply stops mattering (rather than being
    // destroyed), the cap stays authoritative no matter how many charges are stacked afterwards, and
    // removing the cap would restore the real value exactly.
    //
    // It also leaves RESTORE and BYPASS mechanics working, because neither raises the ceiling: a
    // Dash reset hands back the one charge you have, and a paid emergency Dash skips the charge
    // check entirely. Neither is treated as a conflict.
    public unsafe class DeadWeightMutationData : RiftMutationData
    {
        public FP WeaponDamageBonus = FP._0;
        public byte DashChargeHardCap = 1;
        public FP DashCooldownMultiplier = FP._1;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->WeaponDamageMultiplier = FPMath.Max(FP._0, stats->WeaponDamageMultiplier * (FP._1 + WeaponDamageBonus));

            // Lowest cap wins, so a second capping source could only ever tighten it.
            stats->DashChargeHardCap = stats->DashChargeHardCap == 0 || DashChargeHardCap < stats->DashChargeHardCap
                ? DashChargeHardCap
                : stats->DashChargeHardCap;

            // DashCooldownMultiplier is a RATE (StatUtility.GetSkillCooldown divides by it), so a
            // LONGER cooldown means dividing the rate - hence 1 / multiplier rather than multiplying.
            if (DashCooldownMultiplier > FP._0)
            {
                stats->DashCooldownMultiplier = FPMath.Max(FP._0, stats->DashCooldownMultiplier / DashCooldownMultiplier);
            }

            // Charges already banked above the new ceiling are trimmed immediately, so the cap reads
            // as instant rather than only taking effect after the next spend.
            if (f.Unsafe.TryGetPointer<CharacterSkills>(entity, out var skills) == true
                && skills->DashSkill.CurrentStacks > stats->DashChargeHardCap)
            {
                skills->DashSkill.CurrentStacks = stats->DashChargeHardCap;
            }
        }

        protected override object[] DescriptionArgs => new object[]
        {
            WeaponDamageBonus.AsFloat * 100f,
            DashChargeHardCap,
            (DashCooldownMultiplier.AsFloat - 1f) * 100f
        };
    }
}
