namespace Quantum
{
    // Raises the Dash slot's MaxStacks (permanent, unlike SkillSlot.AreaMultiplier which resets
    // every activation) and grants the extra charge immediately rather than making the player wait
    // for it to refill from empty - matches how a level-up pickup is expected to read. See
    // docs/global-upgrades.md.
    public unsafe class DashChargeUpgradeData : GlobalUpgradeData
    {
        public byte Charges = 1;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterSkills>(entity, out var skills) == false)
                return;

            skills->DashSkill.MaxStacks += Charges;
            skills->DashSkill.CurrentStacks += Charges;

            Log.Debug($"[LevelUp] {entity} Dash charges +{Charges} -> {skills->DashSkill.CurrentStacks}/{skills->DashSkill.MaxStacks}");
        }

        protected override object[] DescriptionArgs => new object[] { Charges };
    }
}
