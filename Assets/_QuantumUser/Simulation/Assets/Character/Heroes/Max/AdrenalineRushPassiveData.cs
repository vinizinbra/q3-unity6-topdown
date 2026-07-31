namespace Quantum
{
    using Photon.Deterministic;

    // Max's base Passive - Adrenaline Rush. Adds the persistent, opt-in Adrenaline component (same
    // "spawn-time bake adds a component" shape SeedShield/SeedArmor already use in CharacterSystem)
    // carrying every tunable AdrenalineUtility/AdrenalineSystem read later. A Passive Ascension
    // mutates that component directly (see LevelUp/Heroes/Max/PassiveSkillUpgrades) rather than
    // CharacterStats, since the Fire Rate/Move Speed bonus is a live read, never baked - see
    // Adrenaline.qtn's own comment.
    public unsafe partial class AdrenalineRushPassiveData : PassiveData
    {
        public byte MaxStacks = 20;
        public byte GainPerHit = 1;
        public FP FireRatePerStack = FP._0_05;
        public FP MoveSpeedPerStack = FP._0_05;
        public FP DecayDelay = 3;
        public FP DecayInterval = FP._0_50;

        public override void Apply(Frame f, EntityRef entity, CharacterStats* stats)
        {
            f.Add(entity, new Adrenaline
            {
                MaxStacks = MaxStacks,
                GainPerHit = GainPerHit,
                FireRatePerStack = FireRatePerStack,
                MoveSpeedPerStack = MoveSpeedPerStack,
                DecayDelay = DecayDelay,
                DecayInterval = DecayInterval,
                Stacks = 0,
                TimeSinceLastGain = FP._0,
                DecayTimer = FP._0,
                WeaponDamageBonusAtMax = FP._0,
                DamageReductionAtMax = FP._0,
                DamageReductionDuration = FP._0,
                NoDecayNearWeaponRange = false,
            });
        }
    }
}
