namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine;

    // Ranked Juggernaut Ascension - Brute's primary offensive build engine. Lives on
    // JuggernautSkillData.Actions (Activated = false) - see MomentumSkillAction's own comment for why
    // this is a Hero Skill Ascension, not PassiveUpgradeData.
    //
    //  - Rank 1: every enemy struck during the channel adds StackDamagePercent to the closing blast,
    //    up to MaxStacks.
    //  - Rank 2: each stack ALSO widens the blast (StackRadiusPercent) - so a long route through a
    //    pack pays off twice over, which is the whole point of the line.
    //  - Rank 3 "Earthquake": at max stacks the blast is followed by a second shockwave, via the
    //    generic DelayedBlast component (shared with Pixie's own delayed secondary, not a
    //    Brute-specific timer).
    //
    // Stacks are the pre-existing JuggernautCharge.UnitsHit (cumulative enemies knocked back by
    // Discharge this activation) read directly - no new tracking component, and it resets for free
    // every activation since JuggernautCharge is removed at End regardless. See
    // JuggernautSkillData.TryEndExplosion. Bakes Source as a self-reference every Begin so the view
    // can resolve BlastEffectPrefab off the exact asset that granted this.
    public unsafe partial class AftershockSkillAction : SkillActionData
    {
        [Tooltip("Aftershock damage added per enemy struck during Juggernaut.")]
        public FP[] StackDamagePercent = { FP.FromString("0.15"), FP.FromString("0.15"), FP.FromString("0.15") };

        [Tooltip("Rank 2+ - Aftershock radius added per stack, on top of the damage bonus.")]
        public FP[] StackRadiusPercent = { FP._0, FP.FromString("0.05"), FP.FromString("0.05") };

        public byte[] MaxStacks = { 5, 5, 5 };

        [Header("Rank 3 - Earthquake")]
        [Tooltip("Stacks needed for the second shockwave to trigger at all. 0 disables it (ranks 1-2).")]
        public byte[] EarthquakeStackThreshold = { 0, 0, 5 };

        [Tooltip("Fraction of the primary Aftershock's own (already stack-scaled) damage and radius.")]
        public FP EarthquakeDamagePercent = FP.FromString("0.60");
        public FP EarthquakeRadiusMultiplier = FP._1;
        public FP EarthquakeDelay = FP._0_50;

        public AftershockSkillAction()
        {
            Phase = SkillActionPhase.Begin;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase firedPhase, AssetRef<SkillActionData> selfRef)
        {
            int rank = System.Math.Max(1, SkillUpgradeUtility.GetRank(f, filter.Entity, selfRef));
            int index = System.Math.Clamp(rank, 1, (int)MaxRank) - 1;

            f.AddOrGet<AftershockUpgrade>(filter.Entity, out var upgrade);
            upgrade->StackDamagePercent = StackDamagePercent[index];
            upgrade->StackRadiusPercent = StackRadiusPercent[index];
            upgrade->MaxStacks = MaxStacks[index];
            upgrade->EarthquakeStackThreshold = EarthquakeStackThreshold[index];
            upgrade->EarthquakeDamagePercent = EarthquakeDamagePercent;
            upgrade->EarthquakeRadiusMultiplier = EarthquakeRadiusMultiplier;
            upgrade->EarthquakeDelay = EarthquakeDelay;
            upgrade->Source = this;
        }

        public override void Execute(Frame f, ref SkillSystem.Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase firedPhase)
        {
            // Unreachable - the selfRef overload above is always called by SkillSystem.Invoke. Kept
            // only because SkillActionData.Execute is abstract.
        }
    }
}
