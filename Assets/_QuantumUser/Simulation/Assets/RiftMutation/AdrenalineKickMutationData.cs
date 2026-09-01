namespace Quantum
{
    using Photon.Deterministic;

    // Being saved by your Accessory accelerates your whole kit: a block resets the Dash outright AND
    // cuts the Hero Skill's REMAINING cooldown by a fraction.
    //
    // "Remaining, not base" is the design point - 8 seconds left becomes 4 seconds left - which
    // makes the effect feel the same whether it lands early or late in a cooldown, rather than being
    // worthless right after a cast.
    //
    // Writes two independent CharacterStats flags rather than one "Adrenaline Kick owned" bool, so a
    // future mutation can grant either half on its own and the two still compose - and because
    // SkillSystem.ResetCooldown is idempotent, two sources firing on one block still leave exactly
    // one ready Dash rather than banked charges.
    public unsafe class AdrenalineKickMutationData : RiftMutationData
    {
        public FP SkillCooldownFraction = FP._0;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->AccessoryBlockResetsDash = true;
            stats->AccessoryBlockSkillCooldownFraction = FPMath.Max(stats->AccessoryBlockSkillCooldownFraction, SkillCooldownFraction);
        }

        protected override object[] DescriptionArgs => new object[] { SkillCooldownFraction.AsFloat * 100f };
    }
}
