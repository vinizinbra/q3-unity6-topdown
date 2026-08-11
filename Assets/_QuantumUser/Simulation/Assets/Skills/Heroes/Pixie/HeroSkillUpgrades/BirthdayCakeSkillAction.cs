namespace Quantum
{
    using Photon.Deterministic;

    // Ranked Hero Skill Ascension - "turns the bomb into a birthday cake": after landing (not during
    // flight - see ProjectileSystem.TryPlant, which reads this component off the owner right as the
    // bomb plants), the bomb itself becomes a Decoy for TauntDuration seconds before it detonates,
    // pulling nearby enemies toward it via the existing Decoy tag/EnemyMovementUtility.
    // TryFindNearestDecoy rather than a bespoke attraction mechanic. Rank 2 also grows the blast
    // radius itself (TauntRadiusMultiplier - see BirthdayCakeUpgrade.qtn for why that's the actual
    // mechanical lever "wider taunt" gets, since the generic decoy-pull mechanic has no radius knob of
    // its own to scale). At rank 3, the detonation also deals bonus damage (see
    // ExplodeOnDestroyUtility.ApplyBirthdayCakeBonus).
    //
    // Begin-only, deliberately not paired with End: this configures what the skill produces, not a
    // temporary buff that should only apply while the skill is actively resolving. Revoking on End
    // would race against Fire()/the eventual landing actually reading it. Re-granting fresh
    // (idempotent) every activation and never removing it sidesteps that race entirely - and reads
    // the live rank fresh via selfRef, so a rank-up mid-run takes effect on the very next throw.
    public unsafe partial class BirthdayCakeSkillAction : SkillActionData
    {
        public FP[] TauntDuration = { 1, FP.FromString("1.5"), FP.FromString("1.5") };
        public FP[] TauntRadiusMultiplier = { FP._1, FP.FromString("1.25"), FP.FromString("1.25") };
        public FP BonusDamageMultiplier = FP.FromString("0.30");

        public BirthdayCakeSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<BirthdayCakeUpgrade>(filter.Entity, out var upgrade);
            upgrade->TauntDuration = TauntDuration[index];
            upgrade->TauntRadiusMultiplier = TauntRadiusMultiplier[index];
            upgrade->HasBonusDamage = rank >= 3;
            upgrade->BonusDamageMultiplier = BonusDamageMultiplier;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
