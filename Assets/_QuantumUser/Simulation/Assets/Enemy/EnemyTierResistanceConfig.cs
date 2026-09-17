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

        // How much of Chill's own speed reduction actually lands, independent of
        // SlowDurationMultiplier above (which only shortens how long it lasts) - 1 = full strength,
        // 0 = the slow is applied but has no effect on movement speed at all. Blended the same way
        // SlowEffectData's own magnitudeMultiplier already blends a diluted Chill toward FP._1 (see
        // StatusEffectUtility.ApplyIce), so a Boss can be Chilled for the normal duration but barely
        // slowed by it, instead of a full Filler-strength root disguised as a slow.
        public FP ChillForceMultiplier = FP._1;

        public FP BurnDamageMultiplier = FP._1;
        public FP RuptureDurationMultiplier = FP._1;

        // Stagger (Shock's Jolt) taper - NOT a full immunity flag like ImmuneToHardCC below: Boss
        // stays interruptible by Shock, just tapered, per this project's "Boss stays vulnerable to
        // soft CC" convention and the explicit decision not to special-case any one enemy archetype.
        public FP StaggerDurationMultiplier = FP._1;

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

        // Freeze's OWN duration taper - deliberately separate from StunDurationMultiplier/
        // ImmuneToHardCC below, since Freeze (unlike Stun) is meant to stay vulnerable on every
        // tier, Boss included, just far shorter - the same "Boss stays vulnerable, just tapered"
        // convention ChillForceMultiplier/StaggerDurationMultiplier above already use, applied to a
        // genuine hard-CC instead of a soft one. See StatusEffectUtility.ApplyFreeze.
        public FP FreezeDurationMultiplier = FP._1;

        // Anti-permafreeze window (see StatusEffects.FreezeRecoveryRemaining) - how long, measured
        // from the moment a Freeze lands, buildup can no longer re-trigger Freeze once this tier's
        // own (already-tapered) Freeze duration ends. 0 (Filler/Normal's default) disables the gate
        // entirely, same convention as StunImmunityDuration/InterruptImmunityDuration above.
        public FP FreezeRecoveryDuration = FP._0;

        // Blocks Root outright at this tier (see StatusEffectUtility.ApplyRoot) - authored true for
        // Boss only. Deliberately does NOT block Stun/Freeze/Slow/Burn/Rupture, which stay governed
        // by their own duration multipliers/immunity windows instead, so a DoT/slow/Stun/Freeze
        // build never goes dead against a Boss - only movement-pin Root does. Used to also gate Stun
        // (ApplyStun) and interrupt (TryConsumeInterruptImmunity) outright; that coupling was
        // removed so every tier, Boss included, can be genuinely Stunned/Frozen - see those methods'
        // own comments and Boss's StunDurationMultiplier/FreezeDurationMultiplier below.
        public bool ImmuneToHardCC = false;

        // A landed Stun always freezes this tier's whole state-machine dispatch (EnemySystem.Update's
        // own IsStunned gate - unconditional, no per-tier lever, since "can this enemy act at all
        // right now" isn't what this flag is about). This is the separate question of whether that
        // Stun ALSO reaches into EnemyActionUtility.TryInterrupt and cancels whatever action was
        // already committed (see StatusEffectUtility.ApplyStun) - true (the default, every tier)
        // means yes; a tier authored false still gets frozen in place for the Stun's duration, but
        // resumes and finishes its windup/charge/etc. once Stun expires, same as every tier did
        // before TryInterrupt was wired in here at all. Independent of ImmuneToHardCC above (which
        // rejects the Stun outright, never reaching this flag) and of
        // InterruptibleDuringTelegraph/InterruptibleDuringActive (a per-ACTION authoring choice,
        // this is a per-TIER one - both still apply on top of this when it's true).
        public bool StunCancelsAction = true;
    }

    // Global per-tier tuning for how much of each status effect actually lands on an enemy -
    // referenced via RuntimeConfig.EnemyTierResistanceConfig, read by
    // StatusEffectUtility.GetTierResistance/DamageUtility.ResolveKnockbackScale. Filler/Normal are
    // meant to stay all-1s (unresisted); Specialist/Elite/Boss taper hard CC (Knockback/Stun/Root/Slow)
    // while Burn/Break are expected to stay near 1 so DoT-based builds don't go dead against tougher
    // enemies.
    public class EnemyTierResistanceConfig : AssetObject
    {
        // Stun/Freeze duration are both expressed as a per-tier multiplier of their own Normal-tier
        // reference base (ElementalReactionConfig.ShockStunProcDuration = 1.0s,
        // EffectConfig.FreezeDuration = 2.0s respectively - see each field's own comment), so Normal
        // needs its multipliers set to exactly 1.0 alongside every other tier below rather than
        // relying on the class default, purely for readability at a glance - functionally identical
        // to leaving them unset. Filler intentionally does NOT mirror Normal here (unlike most other
        // fields on this tier) - it's slightly LESS resisted (Stun 1.1s, Freeze 2.0s) since it's
        // meant to be the single easiest tier to hard-CC.
        public TierStatusResistance Filler = new TierStatusResistance
        {
            StunDurationMultiplier = FP.FromString("1.1"),
            StunImmunityDuration = FP.FromString("0.75"),
            InterruptImmunityDuration = FP.FromString("0.75"),
            FreezeDurationMultiplier = FP._1,
            FreezeRecoveryDuration = FP.FromString("0.5"),
        };

        public TierStatusResistance Normal = new TierStatusResistance
        {
            StunDurationMultiplier = FP._1,
            StunImmunityDuration = FP._1,
            InterruptImmunityDuration = FP._1,
            FreezeDurationMultiplier = FP._1,
            FreezeRecoveryDuration = FP.FromString("0.75"),
        };

        // Specialist/Heavy/Elite/Boss taper both Stun/Freeze duration AND how soon each can re-land
        // on the same target (StunImmunityDuration/FreezeRecoveryDuration) - tougher enemies get hit
        // for less time per proc, but also can't be re-CC'd back-to-back, so a fast-firing
        // Electric/Ice build reads as "frequent brief interruptions" against trash and "rare, short
        // openings" against a Boss rather than either extreme losing all identity.
        public TierStatusResistance Specialist = new TierStatusResistance
        {
            StunDurationMultiplier = FP.FromString("0.9"),
            StunImmunityDuration = FP.FromString("1.25"),
            InterruptImmunityDuration = FP.FromString("1.25"),
            StaggerDurationMultiplier = FP.FromString("0.75"),
            ChillForceMultiplier = FP.FromString("0.75"),
            FreezeDurationMultiplier = FP.FromString("0.85"),
            FreezeRecoveryDuration = FP._1,
        };

        public TierStatusResistance Heavy = new TierStatusResistance
        {
            StunDurationMultiplier = FP.FromString("0.8"),
            StunImmunityDuration = FP.FromString("1.75"),
            InterruptImmunityDuration = FP.FromString("1.75"),
            StaggerDurationMultiplier = FP.FromString("0.6"),
            ChillForceMultiplier = FP.FromString("0.6"),
            FreezeDurationMultiplier = FP.FromString("0.7"),
            FreezeRecoveryDuration = FP.FromString("1.5"),
        };

        public TierStatusResistance Elite = new TierStatusResistance
        {
            StunDurationMultiplier = FP.FromString("0.65"),
            StunImmunityDuration = FP.FromString("2.5"),
            InterruptImmunityDuration = FP.FromString("2.5"),
            StaggerDurationMultiplier = FP._0_50,
            ChillForceMultiplier = FP._0_50,
            FreezeDurationMultiplier = FP.FromString("0.6"),
            FreezeRecoveryDuration = FP.FromString("2.5"),
        };

        // Boss: Stun 1.0s * 0.5 = 0.5s, Freeze 2.0s * 0.5 = 1.0s - genuinely stunnable/freezable now
        // (see ApplyStun/TryConsumeInterruptImmunity, which no longer check ImmuneToHardCC below),
        // just brief and gated by the longest re-proc cooldown of any tier (4s). ImmuneToHardCC
        // stays true here for Root's own separate gate only (ApplyRoot) - Boss is still meant to be
        // immune to being rooted in place, that guarantee is untouched by this Stun/Freeze pass.
        public TierStatusResistance Boss = new TierStatusResistance
        {
            StunDurationMultiplier = FP._0_50,
            StunImmunityDuration = FP._4,
            InterruptImmunityDuration = FP._4,
            ImmuneToHardCC = true,
            StaggerDurationMultiplier = FP.FromString("0.4"),
            ChillForceMultiplier = FP.FromString("0.4"),
            FreezeDurationMultiplier = FP._0_50,
            FreezeRecoveryDuration = FP._4,
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
