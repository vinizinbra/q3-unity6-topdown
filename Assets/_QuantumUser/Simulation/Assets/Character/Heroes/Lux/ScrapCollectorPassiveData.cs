namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Lux's base Passive - Scrap Collector. Adds the persistent, opt-in LuxScrapCollector component
    // (same "spawn-time bake adds a component" shape SeedShield/SeedArmor already use in
    // CharacterSystem) carrying every tunable ScrapUtility/ScrapOrbSystem reads later. A Passive
    // Ascension mutates that component directly (see LevelUp/Heroes/Lux/PassiveSkillUpgrades) rather
    // than CharacterStats, since none of Lux's tunables are generic hero stats.
    //
    // The baseline is deliberately just the economy's SKELETON: Normal-tier-and-up kills sometimes
    // drop Scrap, and collecting enough grants one Fabrication Charge (a free Sentry deploy regardless
    // of cooldown). Everything that makes Scrap flow faster, convert into cooldown, or improve a live
    // machine is an Ascension.
    //
    // The two hard limits the brief calls out - at most 1 stored Fabrication Charge, at most N active
    // Sentries - both live here as data. Together they're what stops a
    // Sentry -> kill -> Scrap -> Sentry exponential loop: extra Charges can't bank, and extra Sentries
    // retire the oldest rather than accumulating.
    public unsafe partial class ScrapCollectorPassiveData : PassiveData
    {
        [Tooltip("Chance a Normal-tier-or-above kill drops Scrap. Filler-tier kills drop nothing until the Scavenger Ascension opens them up.")]
        public FP DropChance = FP._0_25;

        [Tooltip("Scrap pickups needed for one Fabrication Charge.")]
        public byte StacksRequired = 10;

        [Tooltip("How many Sentries this Lux may have deployed at once. Deploying past it retires her oldest (silently - no Overload Core payout).")]
        public byte MaxActiveSentries = 2;

        public override void Apply(Frame f, EntityRef entity, CharacterStats* stats)
        {
            f.Add(entity, new LuxScrapCollector
            {
                DropChance = DropChance,
                StacksRequired = StacksRequired,
                MaxActiveSentries = MaxActiveSentries,
                ScrapStacks = 0,

                // Every Ascension-owned field starts off. Written explicitly rather than left to the
                // component's zero-init so the full baseline contract is visible in one place.
                IncludeFillerTier = false,
                FillerDropChance = FP._0,
                GuaranteedDropTierIndex = 0,
                GuaranteedDropCount = 0,
                BossGuaranteedScrap = 0,
                CooldownReductionPerPickup = FP._0,
                CooldownReductionOnCharge = FP._0,
                FieldModDamagePerStack = FP._0,
                FieldModFireRatePerStack = FP._0,
                FieldModMaxStacks = 0,
            });
        }
    }
}
