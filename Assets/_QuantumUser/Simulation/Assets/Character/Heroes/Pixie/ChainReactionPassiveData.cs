namespace Quantum
{
    using Photon.Deterministic;

    // Pixie's base Passive - Chain Reaction. Grants the same MarkExplosiveDeath tag Max's Berserk
    // upgrade grants (see MarkExplosiveDeathSkillAction) - but permanently, spawn-time baked like
    // every other hero's passive here, rather than only for the duration of one skill activation.
    // Every hit Pixie lands marks its target to explode when it eventually dies (see
    // DamageUtility.TryMarkExplodeOnDeath/TryExplodeOnDeath), gated to Filler/Normal tier only via
    // MaxAffectedTier - her Unstable Mixture ascension raises that gate to Heavy. A Passive
    // Ascension mutates the same MarkExplosiveDeath component directly (see
    // LevelUp/Heroes/Pixie/PassiveSkillUpgrades) rather than CharacterStats, since none of these
    // tunables are generic hero stats - see MarkExplosiveDeath.qtn's own comments for what each one
    // does and why Max's Berserk is unaffected by any of them (every field defaults to "no effect").
    public unsafe partial class ChainReactionPassiveData : PassiveData
    {
        // Base probability [0,1] that a qualifying explosion hit marks its target - not every
        // explosion spreads the chain (see MarkExplosiveDeath.MarkChance/DamageUtility.
        // TryMarkExplodeOnDeath).
        public FP MarkChance = FP._0_50;

        public override void Apply(Frame f, EntityRef entity, CharacterStats* stats)
        {
            f.AddOrGet<MarkExplosiveDeath>(entity, out var mark);
            mark->Stacks = 1;
            mark->RequiresExplosion = true;
            mark->HasTierGate = true;
            mark->MaxAffectedTier = (byte)EnemyTier.Normal;
            mark->HasMarkChance = true;
            mark->MarkChance = MarkChance;
            mark->BonusRadiusMultiplier = FP._1;
            mark->BonusDamageMultiplier = FP._1;
            mark->ChainReactionMultiplier = FP._0;
            mark->TierRadiusMultiplier = FP._1;
            mark->DamageBonusVsUnstable = FP._1;
        }
    }
}
