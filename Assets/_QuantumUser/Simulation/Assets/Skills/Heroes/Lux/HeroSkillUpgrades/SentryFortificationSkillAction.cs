namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Ranked Sentry Ascension (Fortification, line 3/4) - turns the machine into a position the team
    // wants to hold, which is Lux's territorial support identity: fight AROUND the machine.
    //
    //  - Rank 1 "Extended Range": the sentry reaches further.
    //  - Rank 2 "Covering Fire": allies inside its aura are shielded from one hit outright - the same
    //    generic Free Hit Guard Brute's Bodyguard grants (StatusEffects.FreeHitGuardRemaining), held
    //    for as long as they stand in range, ONE per ally per turret.
    //  - Rank 3 "Fire Support": those allies also gain Fire Rate and Damage Reduction.
    //
    // Rank 2 replaced "Shield Battery" (a flat Shield-per-second trickle). Once player Shield became
    // charge-only (see Shield.qtn), a sentry refilling it made a parked Lux the single biggest standing
    // Shield source in the game - and because Shield gates the Accessory, that quietly suppressed the
    // whole accessory-loss loop for the entire party. A bounded, discrete denial does the same
    // protective job without being a fountain, and it reads as a MOMENT rather than an invisible tick.
    //
    // It also cannot stack with Bodyguard's guard: there is exactly one FreeHitGuardRemaining slot per
    // entity and ApplyFreeHitGuard is take-the-longer, so two sources contend for one slot rather than
    // granting two blocks. SentryAuraSystem additionally refuses to spend a turret's charge on an ally
    // someone else has already guarded - see its own comment.
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

        [Tooltip("Rank 2+ \"Covering Fire\" - how long a Free Hit Guard granted to an ally in the aura lasts. Kept SHORT: it is re-applied every tick they stand in range, so this is really \"how long it survives after they walk out\", not how long they get to keep it. 0 disables the rank.")]
        public FP[] GuardDuration = { FP._0, FP._1, FP._1 };

        [Tooltip("How many hit denials ONE ally may ever receive from ONE turret. 1 by design - another denial means deploying another turret, which is what ties this line to Lux's redeploy economy (Rapid Recycling, Relocation Protocol) instead of making it a passive aura. 0 disables the rank as surely as GuardDuration 0 does.")]
        public byte[] GuardsPerAlly = { 0, 1, 1 };

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
            upgrade->GuardDuration = GuardDuration[index];
            upgrade->GuardsPerAlly = GuardsPerAlly != null && index < GuardsPerAlly.Length ? GuardsPerAlly[index] : (byte)0;
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
