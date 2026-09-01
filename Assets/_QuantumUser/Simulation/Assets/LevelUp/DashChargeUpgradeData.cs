namespace Quantum
{
    // Raises the Dash slot's MaxStacks (permanent, unlike SkillSlot.AreaMultiplier which resets
    // every activation) and grants the extra charge immediately rather than making the player wait
    // for it to refill from empty - matches how a level-up pickup is expected to read. See
    // docs/global-upgrades.md.
    public unsafe class DashChargeUpgradeData : GlobalUpgradeData
    {
        public byte Charges = 1;

        // Suppressed once a hard cap on Dash charges is in effect (Dead Weight) - raising MaxStacks
        // would still WORK, it just could not change anything, so offering it would be a dead card.
        // Exactly the reasoning MaxPicks already encodes for "picked enough times".
        //
        // Deliberately expressed as a capability check ("is my ceiling capped?") rather than naming
        // the mutation, so any future capping source suppresses this for free and neither asset
        // hardcodes the other.
        public override bool IsEligible(Frame f, EntityRef entity)
        {
            return f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false
                || stats->DashChargeHardCap == 0;
        }

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
