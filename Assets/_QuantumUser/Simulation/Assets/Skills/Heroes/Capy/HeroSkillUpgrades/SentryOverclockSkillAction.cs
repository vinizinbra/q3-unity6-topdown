namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Ranked Sentry Ascension (Overclock, line 2/4) - the machine's attack-speed/uptime path.
    //
    //  - Rank 1: +25% sentry Fire Rate.
    //  - Rank 2: +40% total, and the sentry lives longer.
    //  - Rank 3 "Redline": +50% total, and during the final seconds of its life it goes into overdrive
    //    for a further large Fire Rate bonus.
    //
    // Redline's entry rule is the simple one the brief prefers: it latches ON the first time REMAINING
    // lifetime crosses the threshold and stays on until the sentry dies. Extending lifetime afterwards
    // (Emergency Repair, Relocation Protocol) therefore does NOT switch it back off - which is both
    // simpler to reason about and a genuinely interesting synergy rather than a trap. Remaining
    // lifetime is derived from Health/DecayRate (see Sentry.qtn), so there is no second timer to keep
    // in sync; SentryDecaySystem does the latching.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // gets spawned" upgrade.
    public unsafe partial class SentryOverclockSkillAction : SkillActionData
    {
        [Tooltip("Permanent sentry-wide Fire Rate multiplier per rank. 1.25 = +25%.")]
        public FP[] FireRateMultiplier = { FP._1_25, FP.FromString("1.40"), FP._1_50 };

        [Tooltip("Rank 2+ - extra seconds of sentry lifetime, folded in before DecayRate is derived.")]
        public FP[] DurationBonus = { FP._0, FP._2, FP._2 };

        [Header("Rank 3 - Redline")]
        [Tooltip("Seconds of REMAINING lifetime at which Redline latches on. 0 = not equipped.")]
        public FP[] RedlineThreshold = { FP._0, FP._0, FP._3 };

        [Tooltip("Further Fire Rate multiplier while Redline is active, on top of the permanent one. 2 = +100%.")]
        public FP RedlineFireRateMultiplier = FP._2;

        public SentryOverclockSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<SentryOverclockUpgrade>(filter.Entity, out var upgrade);
            upgrade->FireRateMultiplier = FireRateMultiplier[index];
            upgrade->DurationBonus = DurationBonus[index];
            upgrade->RedlineThreshold = RedlineThreshold[index];
            upgrade->RedlineFireRateMultiplier = RedlineFireRateMultiplier;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
