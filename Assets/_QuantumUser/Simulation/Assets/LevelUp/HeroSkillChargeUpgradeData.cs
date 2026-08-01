namespace Quantum
{
    // Raises the Hero Skill slot's MaxStacks (permanent, unlike SkillSlot.AreaMultiplier which
    // resets every activation) and grants the extra charge immediately rather than making the
    // player wait for it to refill from empty - matches how a level-up pickup is expected to read.
    // Same shape as DashChargeUpgradeData, just targeting HeroSkill instead of DashSkill. See
    // docs/global-upgrades.md.
    public unsafe class HeroSkillChargeUpgradeData : GlobalUpgradeData
    {
        public byte Charges = 1;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterSkills>(entity, out var skills) == false)
                return;

            skills->HeroSkill.MaxStacks += Charges;
            skills->HeroSkill.CurrentStacks += Charges;

            Log.Debug($"[LevelUp] {entity} Hero Skill charges +{Charges} -> {skills->HeroSkill.CurrentStacks}/{skills->HeroSkill.MaxStacks}");
        }

        protected override object[] DescriptionArgs => new object[] { Charges };
    }
}
