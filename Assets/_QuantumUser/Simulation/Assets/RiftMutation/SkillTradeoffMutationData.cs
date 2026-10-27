namespace Quantum
{
    using Photon.Deterministic;

    // One reusable "trade one skill axis for another" mutation, configured per asset:
    //   Hair Trigger - Hero Skill cooldown x0.5, Skill Damage x0.6 (cast twice as often, ~+20% skill DPS)
    //   Overcharge   - Hero Skill cooldown x1.4, Skill Damage x1.7 (rarer, much bigger casts, ~+21%)
    //   Blink Step   - Dash cooldown x0.5, Dash distance x0.7 (short hops, twice as often)
    // Hair Trigger and Overcharge are authored IncompatibleWith each other - they are opposite answers
    // to the same question, which is what gives them an identity a flat stat bump (the removed Ultimate
    // Commitment) never had.
    //
    // Every field is a plain factor on the thing the player feels (1 = unchanged): a cooldown of 0.5
    // HALVES the cooldown. The cooldown stats are RATES (StatUtility.GetSkillCooldown divides by them),
    // so the rate is divided by the factor. Everything composes multiplicatively with other sources -
    // Hair Trigger plus three Skill Cooldown picks (-50%) lands on 0.5 x 0.5 = 25% of the base cooldown.
    public unsafe class SkillTradeoffMutationData : RiftMutationData
    {
        public FP SkillCooldownFactor = FP._1;
        public FP SkillDamageFactor = FP._1;
        public FP DashCooldownFactor = FP._1;
        public FP DashDistanceFactor = FP._1;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            if (SkillCooldownFactor > FP._0)
                stats->SkillCooldownMultiplier = FPMath.Max(FP._0, stats->SkillCooldownMultiplier / SkillCooldownFactor);

            if (DashCooldownFactor > FP._0)
                stats->DashCooldownMultiplier = FPMath.Max(FP._0, stats->DashCooldownMultiplier / DashCooldownFactor);

            stats->SkillDamageMultiplier = FPMath.Max(FP._0, stats->SkillDamageMultiplier * SkillDamageFactor);
            stats->DashDistanceMultiplier = FPMath.Max(FP._0, stats->DashDistanceMultiplier * DashDistanceFactor);
        }

        // {0} Skill Cooldown, {1} Skill Damage, {2} Dash Cooldown, {3} Dash Distance - each a signed % change.
        protected override object[] DescriptionArgs => new object[]
        {
            (SkillCooldownFactor.AsFloat - 1f) * 100f,
            (SkillDamageFactor.AsFloat - 1f) * 100f,
            (DashCooldownFactor.AsFloat - 1f) * 100f,
            (DashDistanceFactor.AsFloat - 1f) * 100f
        };
    }
}
