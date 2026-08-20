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
        public FP RuptureDurationMultiplier = FP._1;

        // -- Hard-CC immunity windows (generic diminishing returns) --
        // How long AFTER a hard-CC application lands before the same kind can land again on this
        // tier. Measured from the moment the CC is applied, so an authored 3s Stun immunity on a 1s
        // Stun leaves a 2s gap where the target is free and can't be re-locked. 0 (Filler/Normal's
        // default) disables the gate entirely and reproduces the pre-existing plain
        // overwrite-on-reapply behavior exactly - which is also what every non-Enemy target (the
        // player) always gets, since GetTierResistance only ever resolves for enemies.
        //
        // This is the generic mechanism the spec's "repeated pulses must NOT repeatedly hard-interrupt
        // the same protected enemy" asks for - shared by Kai's Singularity gravity pulses, Brute's
        // Concussive Impact landing stuns, and Zara's Bass Drop, rather than each re-deriving its own
        // per-cast tracker. See StatusEffectUtility.ApplyStun/EnemyActionUtility.TryInterrupt.
        public FP StunImmunityDuration = FP._0;
        public FP InterruptImmunityDuration = FP._0;

        // Blocks hard CC (Stun/action-interrupt) outright at this tier, regardless of the windows
        // above - authored true for Boss only. Deliberately does NOT block Slow/Root/Burn/Rupture,
        // which stay governed by their own duration multipliers, so a DoT/slow build never goes dead
        // against a Boss.
        public bool ImmuneToHardCC = false;
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

        // Hard-CC immunity windows default to the spec's suggested per-tier ramp (Filler/Normal 0,
        // Specialist 2s, Heavy 3s, Elite 4s, Boss immune). These are field initializers, so an
        // already-authored EnemyTierResistanceConfig.asset picks them up for free - Unity leaves a
        // field at its constructor value when the serialized YAML has no key for it yet.
        public TierStatusResistance Specialist = new TierStatusResistance
        {
            StunImmunityDuration = 2,
            InterruptImmunityDuration = 2,
        };

        public TierStatusResistance Heavy = new TierStatusResistance
        {
            StunImmunityDuration = 3,
            InterruptImmunityDuration = 3,
        };

        public TierStatusResistance Elite = new TierStatusResistance
        {
            StunImmunityDuration = 4,
            InterruptImmunityDuration = 4,
        };

        public TierStatusResistance Boss = new TierStatusResistance
        {
            ImmuneToHardCC = true,
        };

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
