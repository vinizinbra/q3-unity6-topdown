namespace Quantum
{
    using System;
    using Photon.Deterministic;

    [Serializable]
    public class TierStatusResistance
    {
        public FP KnockbackMultiplier = FP._1;
        public FP StunDurationMultiplier = FP._1;
        public FP RootDurationMultiplier = FP._1;
        public FP SlowDurationMultiplier = FP._1;
        public FP BurnDamageMultiplier = FP._1;
        public FP BreakDurationMultiplier = FP._1;
    }

    // Global per-tier tuning for how much of each status effect actually lands on an enemy -
    // referenced via RuntimeConfig.EnemyTierResistanceConfig, read by
    // StatusEffectUtility.GetTierResistance/DamageUtility.ResolveKnockbackScale. Filler/Normal are
    // meant to stay all-1s (unresisted); Specialist/Elite/Boss taper hard CC (Knockback/Stun/Root/Slow)
    // while Burn/Break are expected to stay near 1 so DoT-based builds don't go dead against tougher
    // enemies.
    public class EnemyTierResistanceConfig : AssetObject
    {
        public TierStatusResistance Filler = new TierStatusResistance();
        public TierStatusResistance Normal = new TierStatusResistance();
        public TierStatusResistance Specialist = new TierStatusResistance();
        public TierStatusResistance Heavy = new TierStatusResistance();
        public TierStatusResistance Elite = new TierStatusResistance();
        public TierStatusResistance Boss = new TierStatusResistance();

        public TierStatusResistance Get(EnemyTier tier) => tier switch
        {
            EnemyTier.Normal => Normal,
            EnemyTier.Specialist => Specialist,
            EnemyTier.Heavy => Heavy,
            EnemyTier.Elite => Elite,
            EnemyTier.Boss => Boss,
            _ => Filler,
        };
    }
}
