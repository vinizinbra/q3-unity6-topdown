namespace Quantum
{
    using Photon.Deterministic;

    // Hero Skill Ascension (Healing Chorus, ranked, line 2/4 on Totem) - see docs/zara-ascensions.md.
    // Replaces IncreaseHealSkillAction/HasteOnHealSkillAction, folding "Healing Beats heal more"
    // (rank 1), "...and briefly Haste allies they heal" (rank 2), and "Encore" - excess healing
    // becomes Shield (rank 3) - into one line. Everything baked once at Totem/Speaker spawn (see
    // SpawnAlternatingAreaEffectData.ResolveHealAmount/ApplyHealingChorusUpgrade) - a behavior-shape
    // change from the old Amplified Healing, which read HealUtility.ResolveHealMultiplier live on
    // every heal, needed so Portable Speaker's own Mobile Stage inheritance (rank 3) can read a
    // plain numeric HealBonus off the owner at ITS OWN spawn time.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // gets spawned" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class HealingChorusSkillAction : SkillActionData
    {
        public FP[] HealBonus = { FP.FromString("0.30"), FP.FromString("0.60"), FP._1 };

        // Rank>=2 - baked into the spawned area's first empty HealEffects slot.
        [ExpandableAsset] public AssetRef<HitEffectData> HasteEffect;

        // Slot-0 heal effect per rank - ScaledHealEffectData (rank 1-2) or OverhealToShieldEffectData
        // (rank 3 "Encore"), overwriting the base heal magnitude itself rather than appending to it.
        public AssetRef<HitEffectData>[] HealEffectAsset = new AssetRef<HitEffectData>[3];

        public HealingChorusSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<HealingChorusUpgrade>(filter.Entity, out var upgrade);
            upgrade->HealBonus = HealBonus[index];
            upgrade->HasteEffect = rank >= 2 ? HasteEffect : default;
            upgrade->HealEffectAsset = HealEffectAsset[index];
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
