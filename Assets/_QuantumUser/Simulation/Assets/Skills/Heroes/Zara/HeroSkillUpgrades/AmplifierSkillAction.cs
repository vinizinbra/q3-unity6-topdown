namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Ascension (Amplifier, ranked, line 1/4 on Totem) - see docs/zara-ascensions.md.
    // Replaces IncreaseDamageSkillAction/KnockbackOnDamageSkillAction/StunEveryWavesSkillAction,
    // folding "Damage Beats hit harder" (rank 1), "...and knock enemies back" (rank 2), and
    // "Bass Drop" - every 3rd Damage Beat also Stuns (rank 3) - into one line. DamageBonus/
    // KnockbackEffect are baked once at Totem/Speaker spawn time (see
    // SpawnAlternatingAreaEffectData.ResolveDamageAmount/ApplyAmplifierKnockback); StunInterval/
    // StunEffect are checked live every Damage Beat (see AlternatingAreaSystem.TryApplyBassDropStun)
    // since "every 3rd" can't be baked in once.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // gets spawned" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class AmplifierSkillAction : SkillActionData
    {
        public FP[] DamageBonus = { FP.FromString("0.30"), FP.FromString("0.60"), FP._1 };

        [ExpandableAsset] public AssetRef<HitEffectData> KnockbackEffect;

        // Bass Drop (rank 3 only) - every StunInterval-th Damage Beat also Stuns. 0 at rank 1-2 is
        // AlternatingAreaSystem.TryApplyBassDropStun's own no-op gate.
        public byte[] StunInterval = { 0, 0, 3 };
        [ExpandableAsset] public AssetRef<HitEffectData> StunEffect;

        public AmplifierSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<AmplifierUpgrade>(filter.Entity, out var upgrade);
            upgrade->DamageBonus = DamageBonus[index];
            upgrade->KnockbackEffect = rank >= 2 ? KnockbackEffect : default;
            upgrade->StunInterval = StunInterval[index];
            upgrade->StunEffect = rank >= 3 ? StunEffect : default;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
