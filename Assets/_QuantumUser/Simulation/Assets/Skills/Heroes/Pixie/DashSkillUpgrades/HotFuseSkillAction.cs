namespace Quantum
{
    using Photon.Deterministic;

    // Ranked Dash Ascension - instead of exploding herself, the dash empowers Pixie's NEXT Bunny Bomb
    // throw (see PixieBombCharge.qtn/ProjectileSkillData.ApplyBombCharge/AreaHitData.Detonate for how
    // the charge is applied and consumed). Mechanically distinct from Backblast (dash itself is
    // offensive) - this uses the dash purely as setup.
    //
    //  - Rank 1: next bomb deals +30% damage.
    //  - Rank 2: +30% damage and +30% radius.
    //  - Rank 3: +60% damage, +30% radius, and a direct impact detonates it immediately.
    //
    // Rank 3's InstantDetonate only short-circuits a direct ENEMY hit (see ProjectileHitData.
    // ShouldDetonate/InstantDetonate.qtn) - it does not affect ground/geometry contact, so a bomb that
    // lands instead of hitting an enemy still plants and runs its normal fuse behavior untouched,
    // including Birthday Cake's taunt-then-detonate sequence if that's also equipped. Hot Fuse's
    // damage/radius bonuses still apply either way.
    //
    // Writes only its OWN fields on the shared charge (see PixieBombCharge.qtn) - Blast Jump writes
    // its own, so a build holding both gets both regardless of which action's Execute happens to run
    // first within the same dash.
    //
    // Re-granting fresh (idempotent) every dash and never removing it directly mirrors
    // ClusterBombSkillAction/BirthdayCakeSkillAction's own Begin-only pattern - reads live rank fresh
    // via selfRef, so a rank-up mid-run takes effect on the very next dash.
    public unsafe partial class HotFuseSkillAction : SkillActionData
    {
        public FP Window = 3;
        public FP[] DamageMultiplier = { FP.FromString("1.30"), FP.FromString("1.30"), FP.FromString("1.60") };
        public FP[] RadiusMultiplier = { FP._1, FP.FromString("1.30"), FP.FromString("1.30") };

        public HotFuseSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<PixieBombCharge>(filter.Entity, out var charge);
            PixieAscensionUtility.ExtendBombChargeWindow(charge, Window);
            charge->HotFuseDamageMultiplier = DamageMultiplier[index];
            charge->HotFuseRadiusMultiplier = RadiusMultiplier[index];
            charge->InstantDetonate = rank >= 3;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
