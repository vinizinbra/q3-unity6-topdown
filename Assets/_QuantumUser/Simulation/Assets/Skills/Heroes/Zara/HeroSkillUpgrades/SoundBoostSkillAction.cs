namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Ranked Hero Skill Ascension (Sound Boost, line 2/4 on the Totem) - Zara's primary
    // combat-support path. Replaces the old "Healing Chorus", which bought more and more healing per
    // rank; this line buys TEMPO instead, which is what actually makes her a support rather than a
    // healer.
    //
    //  - Rank 1: Support Beat healing doubles (1% -> 2% Max HP) and its Move Speed/Fire Rate buff
    //    steps up (~+15% each).
    //  - Rank 2 "Sound Boost": every Support Beat also shaves time off affected allies' remaining
    //    Hero Skill cooldown - the single most build-defining thing she can give a team. Capped per
    //    Totem per ally (MaxCooldownReductionPerTotem, enforced by the generic AreaAllyBudget on the
    //    Totem itself), so Double Time's extra beats speed the delivery up without raising the total.
    //  - Rank 3 "Power Chord": healing steps up again (5% Max HP) and the Support Beat additionally
    //    grants a short outgoing-damage window.
    //
    // Every rank's buff profile is ONE authored AllyBuffEffectData asset rather than a pile of numbers
    // here - the same generic effect the Portable Speaker and Lux's Fire Support aura use, so a
    // designer tunes a buff in one place regardless of who emits it. SpeakerSupportBuffEffect/
    // SpeakerCooldownEffect are the reduced-effectiveness variants Portable Speaker rank 3 "Mobile
    // Stage" reads - a different data profile, not different code.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // gets spawned" upgrade: re-granting fresh (idempotent) every activation and never removing it
    // means it's simply always there once picked, with nothing to race against.
    public unsafe partial class SoundBoostSkillAction : SkillActionData
    {
        [Tooltip("Support Beat healing as a fraction of the target's own MaxHealth, per rank. Still bounded by the Totem's global per-ally healing cap.")]
        public FP[] HealPercent = { FP.FromString("0.02"), FP.FromString("0.02"), FP._0_05 };

        [Tooltip("Per rank - the Move Speed / Fire Rate / (rank 3) outgoing-damage bundle each Support Beat applies.")]
        [ExpandableAsset] public AssetRef<HitEffectData>[] SupportBuffEffect = new AssetRef<HitEffectData>[3];

        [Header("Rank 2 - Hero Skill cooldown reduction")]
        [Tooltip("Per rank - a ModifyRemainingCooldownEffectData. Leave rank 1's entry unassigned; that's what keeps the effect off until rank 2.")]
        [ExpandableAsset] public AssetRef<HitEffectData>[] CooldownEffect = new AssetRef<HitEffectData>[3];

        [Tooltip("Total seconds of Hero Skill cooldown reduction ONE Totem may give ONE ally. Expected tuning range is 3-4s; left generous for the first playtest, per the brief. 0 = uncapped.")]
        public FP MaxCooldownReductionPerTotem = 6;

        [Header("Portable Speaker (Mobile Stage) variants")]
        [Tooltip("Reduced-effectiveness counterparts of the two effects above, read ONLY by Portable Speaker rank 3.")]
        [ExpandableAsset] public AssetRef<HitEffectData>[] SpeakerSupportBuffEffect = new AssetRef<HitEffectData>[3];
        [ExpandableAsset] public AssetRef<HitEffectData>[] SpeakerCooldownEffect = new AssetRef<HitEffectData>[3];

        public SoundBoostSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<SoundBoostUpgrade>(filter.Entity, out var upgrade);
            upgrade->HealPercent = HealPercent[index];
            upgrade->SupportBuffEffect = Resolve(SupportBuffEffect, index);
            upgrade->CooldownEffect = Resolve(CooldownEffect, index);
            upgrade->MaxCooldownReductionPerTotem = MaxCooldownReductionPerTotem;
            upgrade->SpeakerSupportBuffEffect = Resolve(SpeakerSupportBuffEffect, index);
            upgrade->SpeakerCooldownEffect = Resolve(SpeakerCooldownEffect, index);
        }

        // Tolerates a short/unauthored array rather than throwing - an unassigned slot simply means
        // "this rank grants nothing from that half", which is exactly what rank 1's cooldown entry is.
        private static AssetRef<HitEffectData> Resolve(AssetRef<HitEffectData>[] source, int index)
        {
            return source != null && index >= 0 && index < source.Length ? source[index] : default;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
