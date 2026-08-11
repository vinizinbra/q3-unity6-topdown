namespace Quantum
{
    using Photon.Deterministic;

    // Max's base Passive - see docs/max-ascensions.md. Seeds RevengeConfig only; the live marks
    // themselves (RevengeMark, one per marked enemy) are added lazily by MaxVendettaSystem the first
    // time each enemy actually lands a qualifying hit, not here.
    public unsafe class VendettaPassiveData : PassiveData
    {
        public FP BaseHealMultiplier = FP._0_50;
        public FP BaseMarkDuration = 8;

        // Base Vendetta bonus - bonus damage against a currently-marked enemy (see
        // RevengeConfig.DamageBonus's own comment). Never existed before this Ascension refactor.
        public FP BaseDamageBonus = FP.FromString("0.15");

        // Guaranteed minimum on-kill heal (see RevengeConfig.MinHealFraction's own comment) - matters
        // most for a mark that only landed a light hit on Max before dying.
        public FP BaseMinHealFraction = FP.FromString("0.01");

        // Second heal floor, off the killed enemy's own MaxHealth instead of Max's (see
        // RevengeConfig.EnemyMaxHealthFraction's own comment).
        public FP BaseEnemyMaxHealthFraction = FP.FromString("0.05");

        public override void Apply(Frame f, EntityRef entity, CharacterStats* stats)
        {
            f.Add(entity, new RevengeConfig
            {
                HealMultiplier = BaseHealMultiplier,
                MarkDuration = BaseMarkDuration,
                DamageBonus = BaseDamageBonus,
                MinHealFraction = BaseMinHealFraction,
                EnemyMaxHealthFraction = BaseEnemyMaxHealthFraction,
            });
        }
    }
}
