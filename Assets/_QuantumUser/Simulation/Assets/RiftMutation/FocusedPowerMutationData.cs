namespace Quantum
{
    using Photon.Deterministic;

    // Precise Skill placement. A much smaller Skill area, but damage that climbs steeply toward the
    // exact center of it - so the mutation rewards aiming a skill well rather than handing out
    // another flat Skill Damage number (which is what it used to do, and what made it
    // indistinguishable from Ultimate Commitment).
    //
    // The falloff itself is generic: SkillFocusUtility reads only the area center/radius that
    // HitEffectUtility already records on every overlap hit, so any skill with a real spatial area
    // gets it automatically, and a skill with no meaningful area (a direct hit, a single-target
    // cast) reports radius 0 and is simply unaffected. No hero is named anywhere.
    public unsafe class FocusedPowerMutationData : RiftMutationData
    {
        public FP SkillAreaMultiplier = FP._1;
        public FP CenterDamageBonus = FP._0;

        public override void Apply(Frame f, EntityRef entity)
        {
            if (f.Unsafe.TryGetPointer<CharacterStats>(entity, out var stats) == false)
                return;

            stats->AreaRadiusMultiplier = FPMath.Max(FP._0, stats->AreaRadiusMultiplier * SkillAreaMultiplier);
            stats->SkillCenterFocusBonus = FPMath.Max(stats->SkillCenterFocusBonus, CenterDamageBonus);
        }

        protected override object[] DescriptionArgs => new object[]
        {
            (SkillAreaMultiplier.AsFloat - 1f) * 100f,
            CenterDamageBonus.AsFloat * 100f
        };
    }
}
