namespace Quantum
{
    using Photon.Deterministic;

    // Ranked Dash Ascension - Pixie's second Dash path alongside Backblast: instead of exploding
    // herself, the dash empowers her NEXT Bunny Bomb throw (see PixieHotFuseCharge.qtn/
    // ProjectileSkillData.Fire/AreaHitData.Detonate for how the charge is applied and consumed).
    // Mechanically distinct from Backblast (dash itself is offensive) - this uses the dash purely as
    // setup, and the two coexist freely: both are independent SkillSlot.Upgrades entries on the same
    // Dash slot, so one dash activation invokes both Execute calls in the same Begin phase with no
    // extra coordination code needed.
    //
    // Rank 3's InstantDetonate only short-circuits a direct ENEMY hit (see ProjectileHitData.
    // ShouldDetonate/InstantDetonate.qtn) - it does not affect ground/geometry contact, so a bomb
    // that lands instead of hitting an enemy still plants and runs its normal fuse behavior
    // untouched, including Birthday Cake's taunt-then-detonate sequence if that's also equipped.
    // Hot Fuse's damage/radius bonuses still apply either way (Fire()/Detonate() apply them
    // unconditionally, independent of whether instant-detonation actually triggered).
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

            f.AddOrGet<PixieHotFuseCharge>(filter.Entity, out var charge);
            charge->Remaining = Window;
            charge->DamageMultiplier = DamageMultiplier[index];
            charge->RadiusMultiplier = RadiusMultiplier[index];
            charge->InstantDetonate = rank >= 3;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
