namespace Quantum
{
    using Photon.Deterministic;

    // Lux's base Passive - Scrap Collector. Adds the persistent, opt-in LuxScrapCollector component
    // (same "spawn-time bake adds a component" shape SeedShield/SeedArmor already use in
    // CharacterSystem) carrying every tunable ScrapUtility/ScrapOrbSystem read later. A Passive
    // Ascension mutates that component directly (see LevelUp/Heroes/Lux/PassiveSkillUpgrades) rather
    // than CharacterStats, since none of Lux's tunables are generic hero stats. The real payoff is
    // reaching StacksRequired Scrap pickups, which grants one free Hero Skill charge (see
    // ScrapUtility.Grant) - CooldownReductionPerPickup starts at 0 (off) and only the Rapid Recycling
    // ascension turns it on.
    public unsafe partial class ScrapCollectorPassiveData : PassiveData
    {
        public FP DropChance = FP._0_25;
        public byte StacksRequired = 10;

        public override void Apply(Frame f, EntityRef entity, CharacterStats* stats)
        {
            f.Add(entity, new LuxScrapCollector
            {
                DropChance = DropChance,
                StacksRequired = StacksRequired,
                IncludeFillerTier = false,
                MachineHealthBonusPerPickup = FP._0,
                CooldownReductionPerPickup = FP._0,
                ScrapStacks = 0,
            });
        }
    }
}
