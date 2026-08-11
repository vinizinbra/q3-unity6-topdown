namespace Quantum
{
    using Photon.Deterministic;

    // Ranked Dash Ascension (Phantom Strike, line 2/3) - see docs/kai-ascensions.md. A genuine
    // Dash-slot SkillActionData now (was a PassiveUpgradeData reacting to OnSkillActivated(DashSkill) -
    // moved here so it shows up correctly as a Dash pick like every other hero's Dash Ascensions,
    // rather than a generic "Passive Upgrade"). Grants a one-shot charge (PhantomStrikeCharge)
    // consumed by the very next shot fired (WeaponSystem.Update/ApplyProjectilePerks), baking bonus
    // damage and bonus Pierce onto that shot off the same single charge - see
    // Heroes/Kai/PhantomStrike.qtn. AddOrGet (not Add) for the charge - a second dash before the first
    // charge is ever consumed by a shot just re-arms it rather than erroring.
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // gets spawned" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class PhantomStrikeSkillAction : SkillActionData
    {
        public FP[] DamageMultiplierBonus = { FP.FromString("0.50"), FP.FromString("0.75"), FP._1 };

        // "Massive Pierce" at rank 3 - a large flat int rather than a dedicated infinite-pierce flag;
        // Projectile.RemainingPierces is Int32, so this is well within range and functionally
        // unlimited in practice.
        public int[] PierceBonus = { 1, 2, 99 };

        public PhantomStrikeSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<PhantomStrikeUpgrade>(filter.Entity, out var upgrade);
            upgrade->DamageMultiplierBonus = DamageMultiplierBonus[index];
            upgrade->PierceBonus = PierceBonus[index];

            f.AddOrGet<PhantomStrikeCharge>(filter.Entity, out _);
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
