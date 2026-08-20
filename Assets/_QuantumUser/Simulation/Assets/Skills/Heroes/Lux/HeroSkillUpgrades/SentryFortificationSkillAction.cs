namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Ranked Sentry Ascension (Fortification, line 3/4) - turns the machine into a position the team
    // wants to hold, which is Lux's territorial support identity: fight AROUND the machine.
    //
    //  - Rank 1 "Extended Range": the sentry reaches further.
    //  - Rank 2 "Shield Battery": allies inside its aura recover Shield over time - a real, flat
    //    per-second amount, deliberately replacing the old "multiply the ally's own shield recharge
    //    rate by 100", which scaled with the recipient and was effectively unbounded.
    //  - Rank 3 "Fire Support": allies inside the aura also gain Fire Rate and Damage Reduction.
    //
    // Rank 3's buff is ONE authored AllyBuffEffectData - the same generic effect asset Zara's Support
    // Beat uses. That matters for more than reuse: its Damage Reduction lands in the single shared
    // aura-DR slot (StatusEffects.AuraDamageReductionRemaining, take-the-stronger), so "buffs from
    // multiple Sentries must not stack" and "Brute's Guardian + Lux's Fire Support must not compound"
    // are both true by construction, with no per-source bookkeeping anywhere.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // gets spawned" upgrade.
    public unsafe partial class SentryFortificationSkillAction : SkillActionData
    {
        public FP[] RangeBonus = { FP._2, FP._2, FP._2 };

        [Tooltip("Rank 2+ - FLAT Shield per second restored to allies inside the aura (baseline player Shield is 50).")]
        public FP[] AllyShieldPerSecond = { FP._0, FP._3, FP._3 };

        [Tooltip("Fraction of the sentry's own Range the support aura reaches - deliberately tighter than its targeting range.")]
        public FP AuraRangeRatio = FP._0_50;

        [Header("Rank 3 - Fire Support")]
        [Tooltip("An AllyBuffEffectData (Fire Rate + Damage Reduction) reapplied every tick to allies in the aura. Leave ranks 1-2 unassigned.")]
        [ExpandableAsset] public AssetRef<HitEffectData>[] FireSupportEffect = new AssetRef<HitEffectData>[3];

        public SentryFortificationSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<SentryFortificationUpgrade>(filter.Entity, out var upgrade);
            upgrade->RangeBonus = RangeBonus[index];
            upgrade->AllyShieldPerSecond = AllyShieldPerSecond[index];
            upgrade->AuraRangeRatio = AuraRangeRatio;
            upgrade->FireSupportEffect = FireSupportEffect != null && index < FireSupportEffect.Length
                ? FireSupportEffect[index]
                : default;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
