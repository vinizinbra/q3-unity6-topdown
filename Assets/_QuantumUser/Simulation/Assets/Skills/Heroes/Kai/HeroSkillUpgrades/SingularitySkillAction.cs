namespace Quantum
{
    using Photon.Deterministic;

    // Ranked Vortex Ascension (Singularity, line 1/4) - see docs/kai-ascensions.md. Replaces the old
    // VortexPowerPulseSkillAction/VortexPowerPulseUpgrade (an absolute Force/TickInterval override) -
    // this instead MULTIPLIES the vortex's own PullForce baseline and collider radius (see
    // SpawnVortexEffectData.ApplySingularityUpgrade), composing with Skill Area rather than replacing
    // it. Also grants Vortex the ability to interrupt caught enemies' own attacks, winding up or
    // already committed alike (EnemyActionUtility.TryInterrupt) - tier eligibility/caps are pure data
    // here (MaxEligibleTierIndex/UnlimitedBelowOrEqualTierIndex), read generically by VortexSystem
    // with zero hardcoded tier names. Rank 3 additionally adds a periodic stronger gravity pulse on
    // top of the base pull (VortexGravityPulse).
    //
    // Begin-only, deliberately not paired with End - same reasoning as every other "configures what
    // gets spawned" upgrade this session: re-granting fresh (idempotent) every activation and never
    // removing it means it's simply always there once picked, with nothing to race against.
    public unsafe partial class SingularitySkillAction : SkillActionData
    {
        public FP[] PullRadiusMultiplier = { FP.FromString("1.30"), FP.FromString("1.50"), FP.FromString("1.75") };
        public FP[] PullForceMultiplier = { FP._1, FP.FromString("1.30"), FP._1_50 };

        // (byte)EnemyTier this rank can interrupt up to (inclusive) - Normal(1)/Heavy(3)/Elite(4).
        // Bosses (5) are always immune, at every rank - MaxEligibleTierIndex never reaches that high.
        public byte[] MaxEligibleTierIndex = { (byte)EnemyTier.Normal, (byte)EnemyTier.Heavy, (byte)EnemyTier.Elite };

        // Filler/Normal (index <= 1) are always unlimited, at every rank - only tiers above this get
        // capped at one successful interrupt per enemy per Vortex cast (see VortexInterruptTracker).
        public byte[] UnlimitedBelowOrEqualTierIndex = { (byte)EnemyTier.Normal, (byte)EnemyTier.Normal, (byte)EnemyTier.Normal };

        public bool[] HasGravityPulse = { false, false, true };
        public FP GravityPulseForceMultiplier = 3;
        public FP GravityPulseInterval = 1;

        public SingularitySkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<SingularityUpgrade>(filter.Entity, out var upgrade);
            upgrade->PullRadiusMultiplier = PullRadiusMultiplier[index];
            upgrade->PullForceMultiplier = PullForceMultiplier[index];
            upgrade->MaxEligibleTierIndex = MaxEligibleTierIndex[index];
            upgrade->UnlimitedBelowOrEqualTierIndex = UnlimitedBelowOrEqualTierIndex[index];
            upgrade->HasGravityPulse = HasGravityPulse[index];
            upgrade->GravityPulseForceMultiplier = GravityPulseForceMultiplier;
            upgrade->GravityPulseInterval = GravityPulseInterval;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
